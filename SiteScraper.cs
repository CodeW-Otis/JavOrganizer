using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Shared plumbing for site scrapers: HTTP with rotating browser
/// fingerprints, the FlareSolverr Cloudflare fallback, per-site pacing,
/// ban/unreachable tracking and HTML/XPath helpers.
/// </summary>
/// <remarks>
/// <para>Language, FlareSolverr and request-delay settings are read from the
/// plugin configuration on every request, so saving the configuration page
/// takes effect immediately without a server restart.</para>
/// <para>
/// <b>Anti-detect strategy</b>: every request presents a coherent, randomly
/// chosen browser profile (user agent plus matching sec-ch-ua client hints,
/// platform, Accept-Language weighting and header order). Profiles rotate
/// per request while no Cloudflare clearance is held; once FlareSolverr
/// solves a challenge, the exact browser agent is pinned (Cloudflare
/// validates the pairing) until it stops working, at which point the
/// profile rotates again.</para>
/// <para>
/// <b>Anti-ban strategy</b>: per-site rate limiting with randomized jitter,
/// exponential backoff on transient rate limits (429/503), a six-hour
/// cooling-off after an explicit ban page, and an unreachable circuit
/// breaker that skips a site for a while after repeated failures so scans
/// never waste time hammering a dead endpoint.</para>
/// </remarks>
public abstract partial class SiteScraper : IDisposable
{
    private protected readonly ILogger Logger;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private readonly string _cookieHeader;
    private readonly object _clearanceLock = new();
    private readonly Random _jitter = new();

    private string? _clearanceUserAgent;
    private bool _clearanceActive;
    private Task<(string Html, string? UserAgent)?>? _inflightSolve;
    private DateTime _bannedUntil = DateTime.MinValue;
    private DateTime _unreachableUntil = DateTime.MinValue;
    private int _consecutiveFailures;
    private PerSiteRateLimiter? _rateLimiter;
    private int _profileCursor;

    /// <summary>
    /// Coherent browser profiles: user agent plus the client-hint headers a
    /// real browser of that family would send. One is picked per request
    /// (round-robin with random start) so the plugin's traffic never
    /// presents a single fixed fingerprint; a solved Cloudflare clearance
    /// pins the exact profile the solving browser used, because Cloudflare
    /// validates the user-agent/clearance pairing.
    /// </summary>
    private static readonly (string UserAgent, string? SecChUa, string? SecChUaPlatform, string AcceptLanguage)[] BrowserProfiles =
    [
        ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
            "\"Chromium\";v=\"126\", \"Google Chrome\";v=\"126\", \"Not.A/Brand\";v=\"24\"", "\"Windows\"", "en-US,en;q=0.9"),
        ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
            "\"Chromium\";v=\"128\", \"Google Chrome\";v=\"128\", \"Not.A/Brand\";v=\"24\"", "\"Windows\"", "en-US,en;q=0.8,ja;q=0.9"),
        ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0",
            "\"Chromium\";v=\"130\", \"Microsoft Edge\";v=\"130\", \"Not.A/Brand\";v=\"24\"", "\"Windows\"", "en-US,en;q=0.9"),
        ("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0",
            null, "\"Windows\"", "en-US,en;q=0.7,ja;q=0.5"),
        ("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15",
            null, "\"macOS\"", "en-US,en;q=0.9"),
        ("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36",
            "\"Chromium\";v=\"129\", \"Google Chrome\";v=\"129\", \"Not.A/Brand\";v=\"24\"", "\"Linux\"", "en-US,en;q=0.9"),
        ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            "\"Chromium\";v=\"131\", \"Google Chrome\";v=\"131\", \"Not.A/Brand\";v=\"24\"", "\"Windows\"", "en-US,en;q=0.9,ja;q=0.7"),
        ("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
            "\"Chromium\";v=\"128\", \"Google Chrome\";v=\"128\", \"Not.A/Brand\";v=\"24\"", "\"macOS\"", "en-US,en;q=0.8")
    ];

    /// <summary>
    /// Attaches a shared per-site rate limiter, enforcing polite spacing on
    /// this site across every parallel worker. Scrapers created without one
    /// fall back to no pacing (unit tests).
    /// </summary>
    /// <param name="limiter">The shared limiter instance.</param>
    internal void AttachRateLimiter(PerSiteRateLimiter limiter) => _rateLimiter = limiter;

    /// <summary>
    /// Gets a value indicating whether the most recent scrape round actually
    /// reached this site — that is, at least one page load was answered by
    /// the site itself (as opposed to every request being rate-limited,
    /// banned, blocked or failed). Callers use this to decide whether a
    /// "not found" result is a real miss worth caching.
    /// </summary>
    internal bool SiteWasReached { get; private set; }

    /// <summary>
    /// Resets the reached state at the start of a scrape round; the first
    /// page this scraper actually receives during the round flags it
    /// reached again.
    /// </summary>
    internal void BeginScrapeRound() => SiteWasReached = false;

    /// <summary>
    /// Gets a value indicating whether the site has told this machine to go
    /// away (temporary IP ban). While set, all requests to the site are
    /// skipped so scans do not waste time hammering a wall.
    /// </summary>
    internal bool IsBanned
    {
        get
        {
            lock (_clearanceLock)
            {
                return DateTime.UtcNow < _bannedUntil;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the site is currently considered
    /// unreachable (repeated transport failures): requests are skipped for
    /// a cooling-off window so a dead or blocking site never slows scans
    /// down or worsens its opinion of this machine.
    /// </summary>
    internal bool IsUnreachable
    {
        get
        {
            lock (_clearanceLock)
            {
                return DateTime.UtcNow < _unreachableUntil;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the site should be contacted at all
    /// right now (not banned, not in the unreachable window). The site
    /// selector uses this to pick replacements down the priority ranking.
    /// </summary>
    internal bool IsAvailable => !IsBanned && !IsUnreachable;

#if NET7_0_OR_GREATER
    // Challenge markers that appear ONLY on an interstitial page — never on a
    // solved/normal page. "challenge-platform" is deliberately absent: every
    // Cloudflare-protected page carries that script tag, so matching it made
    // the plugin discard successfully solved pages (and re-solve forever).
    [GeneratedRegex(@"cf-browser-verification|cf_chl_opt|cf_chl_|Just a moment|Verify you are human|Attention Required|ddos-guard|checking your browser|Enable JavaScript and cookies to continue", RegexOptions.IgnoreCase)]
    private static partial Regex CloudflareRegex();

    [GeneratedRegex(@"banned your access|禁止了你的訪問|blocked your access|access denied|too many requests", RegexOptions.IgnoreCase)]
    private static partial Regex BannedRegex();

    [GeneratedRegex(@"request could not be satisfied|ERROR: 40[035]|Generated by cloudfront|Bad Gateway generated by cloudfront", RegexOptions.IgnoreCase)]
    private static partial Regex EdgeErrorRegex();
#else
    private static readonly Regex CloudflareRegexInstance = new(@"cf-browser-verification|cf_chl_opt|cf_chl_|Just a moment|Verify you are human|Attention Required|ddos-guard|checking your browser|Enable JavaScript and cookies to continue", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BannedRegexInstance = new(@"banned your access|禁止了你的訪問|blocked your access|access denied|too many requests", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EdgeErrorRegexInstance = new(@"request could not be satisfied|ERROR: 40[035]|Generated by cloudfront|Bad Gateway generated by cloudfront", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex CloudflareRegex() => CloudflareRegexInstance;
    private static Regex BannedRegex() => BannedRegexInstance;
    private static Regex EdgeErrorRegex() => EdgeErrorRegexInstance;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="SiteScraper"/> class.
    /// </summary>
    /// <param name="logger">Logger for scraping diagnostics.</param>
    /// <param name="baseUrl">Absolute base URL of the site (used for relative links).</param>
    /// <param name="cookieHeader">Optional cookie header sent with every request
    /// (for example age-verification tokens).</param>
    protected SiteScraper(ILogger logger, string baseUrl, string? cookieHeader = null)
    {
        Logger = logger;
        BaseUrl = baseUrl.TrimEnd('/') + "/";
        _cookieHeader = cookieHeader ?? string.Empty;

        _http = new HttpClient(new HttpClientHandler
        {
            CookieContainer = _cookies,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Anti-detect: no user agent or language is pinned on the client —
        // every request carries a freshly rotated, internally coherent
        // browser profile. Only static cookies live here.
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            _http.DefaultRequestHeaders.Add("Cookie", cookieHeader);
        }

        _profileCursor = _jitter.Next(BrowserProfiles.Length);
    }

    /// <summary>
    /// Gets the absolute base URL of the site.
    /// </summary>
    protected string BaseUrl { get; }

    private static string? FlareSolverrUrl =>
        string.IsNullOrWhiteSpace(Plugin.EffectiveConfiguration.FlareSolverrUrl)
            ? null
            : Plugin.EffectiveConfiguration.FlareSolverrUrl.TrimEnd('/');

    private static int RequestDelayMs => Math.Max(0, Plugin.EffectiveConfiguration.RequestDelayMs);

    /// <summary>
    /// Releases the underlying <see cref="HttpClient"/>.
    /// </summary>
    public void Dispose()
    {
        _http.Dispose();
    }

    /// <summary>
    /// Fetches a path from the site. Direct requests are attempted first; a
    /// Cloudflare challenge triggers the FlareSolverr fallback, after which
    /// the clearance cookie is adopted and subsequent requests stay direct.
    /// When several workers hit a challenge at once, only one FlareSolverr
    /// solve runs and the rest wait for it and reuse the result. Banned and
    /// unreachable states short-circuit before any network work.
    /// </summary>
    /// <param name="path">Relative path (or absolute URL) to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Page HTML, or <c>null</c> when both direct and fallback requests fail.</returns>
    protected async Task<string?> GetHtmlAsync(string path, CancellationToken ct)
    {
        // The site has banned this machine, or it has failed repeatedly —
        // do not waste time (or worsen the ban) by requesting anything.
        if (IsBanned)
        {
            Logger.LogDebug("{Site}: skipping '{Path}' — site temporarily banned this address", SiteName, path);
            return null;
        }

        if (IsUnreachable)
        {
            Logger.LogDebug("{Site}: skipping '{Path}' — site marked unreachable, cooling off", SiteName, path);
            return null;
        }

        // Anti-ban pacing: every request to this site waits for its slot so
        // parallel workers are smoothed into a polite per-site stream.
        var url = ToAbsoluteUrl(path);
        if (_rateLimiter is not null)
        {
            using var slot = await _rateLimiter.AcquireAsync(SiteName, ct).ConfigureAwait(false);
            return await FetchCoreAsync(url, ct).ConfigureAwait(false);
        }

        return await FetchCoreAsync(url, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Direct request first, then the shared FlareSolverr solve when
    /// Cloudflare challenges. Any page actually received marks the site as
    /// reached for this round and resets the unreachable counter; total
    /// failure advances the circuit breaker.
    /// </summary>
    /// <param name="url">Absolute URL to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Page HTML, or <c>null</c> when both paths fail.</returns>
    private async Task<string?> FetchCoreAsync(string url, CancellationToken ct)
    {
        // Fast path: direct request. Works whenever a valid Cloudflare
        // clearance (or no challenge at all) is in effect.
        var html = await TryDirectAsync(url, ct).ConfigureAwait(false);
        if (html is not null)
        {
            SiteWasReached = true;
            MarkReached();
            return html;
        }

        // Slow path: solve through FlareSolverr's real browser, sharing one
        // solve across all concurrent workers.
        var solved = await SolveSharedAsync(url, ct).ConfigureAwait(false);
        if (solved is not null)
        {
            SiteWasReached = true;
            MarkReached();
            return solved;
        }

        MarkFailedFetch();
        return null;
    }

    /// <summary>
    /// A page was actually received: reset the unreachable streak.
    /// </summary>
    private void MarkReached()
    {
        lock (_clearanceLock)
        {
            _consecutiveFailures = 0;
        }
    }

    /// <summary>
    /// Both the direct and the FlareSolverr path failed: advance the
    /// unreachable circuit breaker. Three consecutive failures cool the
    /// site off for a randomized 30–60 minutes, so scans stop wasting time
    /// (and requests) on a dead or blocking site.
    /// </summary>
    private void MarkFailedFetch()
    {
        int failures;
        lock (_clearanceLock)
        {
            _consecutiveFailures++;
            failures = _consecutiveFailures;
        }

        if (failures >= 3)
        {
            var minutes = 30 + _jitter.Next(31);
            lock (_clearanceLock)
            {
                _unreachableUntil = DateTime.UtcNow.AddMinutes(minutes);
                _consecutiveFailures = 0;
            }

            Logger.LogInformation("{Site}: unreachable after repeated failures; skipping for {Minutes} minutes", SiteName, minutes);
        }
    }

    /// <summary>
    /// Marks the site as banned for a cooling-off period, so scrapers stop
    /// requesting it until the ban is likely lifted.
    /// </summary>
    /// <param name="hours">Approximate hours to back off.</param>
    private void MarkBanned(double hours)
    {
        lock (_clearanceLock)
        {
            _bannedUntil = DateTime.UtcNow.AddHours(hours);
        }
    }

    /// <summary>
    /// Builds a GET request carrying the next browser profile: when a
    /// Cloudflare clearance is held, the solving browser's exact user agent
    /// is pinned (Cloudflare validates the pairing); otherwise the profile
    /// rotates across realistic UA/sec-ch-ua/Accept-Language combinations
    /// so no single fingerprint is ever presented twice in a row.
    /// </summary>
    /// <param name="url">Absolute URL to fetch.</param>
    /// <returns>The request message with full browser headers.</returns>
    private HttpRequestMessage BuildProfiledRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        string userAgent;
        string? secChUa = null;
        string? platform = null;
        string acceptLanguage;

        lock (_clearanceLock)
        {
            if (_clearanceActive && !string.IsNullOrWhiteSpace(_clearanceUserAgent))
            {
                userAgent = _clearanceUserAgent;
                // Chrome-family hints when the clearance browser was
                // Chrome-based; omit them for other families (a real
                // non-Chrome browser would not send them either).
                if (userAgent.Contains("Chrome", StringComparison.Ordinal) && !userAgent.Contains("Edg", StringComparison.Ordinal))
                {
                    secChUa = ExtractSecChUaForAgent(userAgent);
                    platform = userAgent.Contains("Windows", StringComparison.Ordinal)
                        ? "\"Windows\""
                        : userAgent.Contains("Mac", StringComparison.Ordinal) ? "\"macOS\"" : "\"Linux\"";
                }

                acceptLanguage = "en-US,en;q=0.9";
            }
            else
            {
                var profile = BrowserProfiles[_profileCursor % BrowserProfiles.Length];
                _profileCursor++;
                userAgent = profile.UserAgent;
                secChUa = profile.SecChUa;
                platform = profile.SecChUaPlatform;
                acceptLanguage = profile.AcceptLanguage;
            }
        }

        // Header order matches a real browser's ordering (sec-ch-ua
        // client hints first, then the classic headers).
        if (secChUa is not null)
        {
            request.Headers.TryAddWithoutValidation("sec-ch-ua", secChUa);
            request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        }

        if (platform is not null)
        {
            request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", platform);
        }

        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
        request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");

        // Referer mirrors a browser navigating within the site.
        request.Headers.TryAddWithoutValidation("Referer", BaseUrl);

        if (!string.IsNullOrWhiteSpace(_cookieHeader))
        {
            request.Headers.TryAddWithoutValidation("Cookie", _cookieHeader);
        }

        return request;
    }

    /// <summary>
    /// Derives a coherent sec-ch-ua header string from a Chrome-family user
    /// agent (major version extracted).
    /// </summary>
    private static string ExtractSecChUaForAgent(string userAgent)
    {
        var match = Regex.Match(userAgent, @"Chrome/(\d+)");
        var version = match.Success ? match.Groups[1].Value : "126";
        return $"\"Chromium\";v=\"{version}\", \"Google Chrome\";v=\"{version}\", \"Not.A/Brand\";v=\"24\"";
    }

    /// <summary>
    /// Runs a FlareSolverr solve for the URL, coalescing concurrent solves
    /// into one: the first worker performs it while the others await the
    /// same task and then retry directly with the fresh clearance.
    /// </summary>
    /// <param name="url">Absolute URL to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page HTML, or <c>null</c> on any failure.</returns>
    private async Task<string?> SolveSharedAsync(string url, CancellationToken ct)
    {
        bool performedSolve;
        Task<(string Html, string? UserAgent)?> solve;

        lock (_clearanceLock)
        {
            if (_inflightSolve is not null)
            {
                // Someone else is already solving; join them instead of
                // queuing another browser round-trip.
                performedSolve = false;
                solve = _inflightSolve;
            }
            else
            {
                performedSolve = true;
                var solveTask = TryFlareSolverrAsync(url, ct);
                _inflightSolve = solveTask.ContinueWith(t =>
                {
                    lock (_clearanceLock)
                    {
                        _inflightSolve = null;
                    }

                    return t.Result;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                solve = _inflightSolve;
            }
        }

        var result = await solve.ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        if (performedSolve)
        {
            // This worker's URL is exactly what the solve fetched.
            return result.Value.Html;
        }

        // This worker only waited on someone else's solve (for a different
        // URL); now that the clearance is adopted, fetch our URL directly.
        var direct = await TryDirectAsync(url, ct).ConfigureAwait(false);
        return direct ?? result.Value.Html;
    }

    /// <summary>
    /// Attempts a plain HTTP GET with a rotated browser profile. Transient
    /// rate limiting (429/503) is waited out with exponential backoff and
    /// retried; a Cloudflare challenge page returns <c>null</c> so the caller
    /// can re-solve and clears the pinned profile so rotation resumes.
    /// </summary>
    /// <param name="url">Absolute URL to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Page HTML, or <c>null</c> when blocked or failed.</returns>
    private async Task<string?> TryDirectAsync(string url, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = BuildProfiledRequest(url);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (CloudflareRegex().IsMatch(html))
                {
                    // A CloudFront/edge "request could not be satisfied" page
                    // is a hard block, not a solvable challenge — solving it
                    // only burns browser round-trips. Treat it like a ban.
                    if (EdgeErrorRegex().IsMatch(html))
                    {
                        Logger.LogDebug("{Site}: edge/CDN refused '{Url}' (hard block); backing off", SiteName, url);
                        MarkFailedFetch();
                        return null;
                    }

                    // The pinned clearance no longer works; drop it so the
                    // next request presents a fresh rotated profile.
                    lock (_clearanceLock)
                    {
                        if (_clearanceActive)
                        {
                            _clearanceActive = false;
                        }
                    }

                    Logger.LogDebug("{Site}: direct request challenged for '{Url}', switching to FlareSolverr", SiteName, url);
                    return null;
                }

                if (EdgeErrorRegex().IsMatch(html))
                {
                    Logger.LogDebug("{Site}: edge/CDN error page for '{Url}'; not a challenge, skipping", SiteName, url);
                    MarkFailedFetch();
                    return null;
                }

                if (BannedRegex().IsMatch(html))
                {
                    Logger.LogWarning("{Site}: this machine is temporarily banned; backing off for {Hours} hours", SiteName, 6);
                    MarkBanned(6);
                    return null;
                }

                if (response.IsSuccessStatusCode)
                {
                    return html;
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                    && attempt < maxAttempts)
                {
                    // The sites throttle bursts. Back off exponentially with
                    // jitter rather than burning a FlareSolverr solve.
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * Math.Pow(2, attempt - 1));
                    retryAfter += TimeSpan.FromMilliseconds(_jitter.Next(500));
                    Logger.LogDebug("{Site}: rate-limited on '{Url}', retrying in {Delay}ms (attempt {Attempt})", SiteName, url, retryAfter.TotalMilliseconds, attempt);
                    await Task.Delay(retryAfter, ct).ConfigureAwait(false);
                    continue;
                }

                // Other status codes mean the clearance expired; let the
                // caller re-solve through FlareSolverr.
                Logger.LogDebug("{Site} direct request '{Url}' returned {Status}", SiteName, url, response.StatusCode);
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;
                }

                Logger.LogDebug(ex, "{Site} direct request for '{Url}' failed", SiteName, url);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Requests a URL through a FlareSolverr instance and, on success,
    /// adopts the clearance cookies and browser user agent for direct use.
    /// </summary>
    /// <param name="url">Absolute URL to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fetched HTML plus the browser user agent, or <c>null</c> on any failure.</returns>
    private async Task<(string Html, string? UserAgent)?> TryFlareSolverrAsync(string url, CancellationToken ct)
    {
        var flaresolverrUrl = FlareSolverrUrl;
        if (flaresolverrUrl is null)
        {
            Logger.LogWarning("{Site}: Cloudflare bypass needed but no FlareSolverr URL is configured", SiteName);
            return null;
        }

        try
        {
            // 60 s: real Cloudflare challenges regularly take longer than a
            // 30 s budget; an aborted solve fails every worker waiting on
            // the shared in-flight solve, and the site gets blamed for it.
            var payload = JsonSerializer.Serialize(new { cmd = "request.get", url, maxTimeout = 60000 });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await JavHttp.FlareSolverr.PostAsync(flaresolverrUrl, content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("solution", out var solution)
                || !solution.TryGetProperty("response", out var page))
            {
                Logger.LogWarning("{Site}: FlareSolverr response for '{Url}' had no solution.response", SiteName, url);
                return null;
            }

            var resultHtml = page.GetString();
            if (string.IsNullOrEmpty(resultHtml))
            {
                Logger.LogWarning("{Site}: FlareSolverr returned empty HTML for '{Url}'", SiteName, url);
                return null;
            }

            // FlareSolverr's browser can receive the ban page just like a
            // direct request can; back off instead of retrying into a wall.
            if (BannedRegex().IsMatch(resultHtml))
            {
                Logger.LogWarning("{Site}: this machine is temporarily banned (FlareSolverr path); backing off for {Hours} hours", SiteName, 6);
                MarkBanned(6);
                return null;
            }

            if (CloudflareRegex().IsMatch(resultHtml))
            {
                Logger.LogWarning("{Site}: FlareSolverr could not solve the challenge for '{Url}'", SiteName, url);
                return null;
            }

            var userAgent = solution.TryGetProperty("userAgent", out var ua) ? ua.GetString() : null;

            // Adopt the browser's Cloudflare clearance so subsequent requests
            // go direct. FlareSolverr hands back the exact cookies the browser
            // used; replaying them (plus the matching user agent, which
            // Cloudflare validates) makes plain HttpClient requests pass.
            AdoptClearance(solution, userAgent);
            Logger.LogInformation("{Site}: FlareSolverr fetched '{Url}' and granted direct access", SiteName, url);
            return (resultHtml, userAgent);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{Site}: FlareSolverr request for '{Url}' failed", SiteName, url);
            return null;
        }
    }

    /// <summary>
    /// Stores the clearance cookies and user agent returned by FlareSolverr,
    /// pinning the profile so direct requests replay the exact browser.
    /// </summary>
    /// <param name="solution">The FlareSolverr solution element.</param>
    /// <param name="userAgent">The browser user agent to replay.</param>
    private void AdoptClearance(JsonElement solution, string? userAgent)
    {
        try
        {
            if (solution.TryGetProperty("cookies", out var cookies) && cookies.ValueKind == JsonValueKind.Array)
            {
                foreach (var cookie in cookies.EnumerateArray())
                {
                    var name = cookie.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var value = cookie.TryGetProperty("value", out var v) ? v.GetString() : null;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    // Strip an existing copy, then add the fresh one; this
                    // refreshes cf_clearance cleanly when it rotates.
                    _cookies.Add(new Cookie(name, value, "/", new Uri(BaseUrl).Host));
                }
            }

            lock (_clearanceLock)
            {
                _clearanceUserAgent = userAgent;
                _clearanceActive = !string.IsNullOrWhiteSpace(userAgent);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "{Site}: failed adopting FlareSolverr clearance", SiteName);
        }
    }

    /// <summary>
    /// Waits the configured politeness delay between page fetches, when the
    /// scan wants more than one page per code. The delay is jittered so
    /// multi-page walks never look machine-regular.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    protected async Task ThrottleAsync(CancellationToken ct)
    {
        var delay = RequestDelayMs;
        if (delay > 0)
        {
            // 70–150% of the configured delay.
            var jittered = (int)(delay * (0.7 + (_jitter.NextDouble() * 0.8)));
            await Task.Delay(jittered, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves a possibly-relative URL against the site base.
    /// </summary>
    /// <param name="path">Relative or absolute URL.</param>
    /// <returns>The absolute URL.</returns>
    protected string ToAbsoluteUrl(string path)
    {
        if (Uri.IsWellFormedUriString(path, UriKind.Absolute))
        {
            return path;
        }

        return new Uri(new Uri(BaseUrl, UriKind.Absolute), path).ToString();
    }

    /// <summary>
    /// Makes a scraped image URL absolute against the site base.
    /// </summary>
    /// <param name="url">Raw image URL from a page.</param>
    /// <returns>An absolute HTTPS URL, or <c>null</c> when empty.</returns>
    protected string? ToAbsoluteImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var value = url.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + value;
        }

        if (Uri.IsWellFormedUriString(value, UriKind.Absolute))
        {
            return value;
        }

        return new Uri(new Uri(BaseUrl, UriKind.Absolute), value).ToString();
    }

    private protected string SelectText(HtmlDocument doc, string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            return string.Empty;
        }

        var node = doc.DocumentNode.SelectSingleNode(xpath);
        return node is null ? string.Empty : WebUtility.HtmlDecode(node.InnerText).Trim();
    }

    private protected List<string> SelectTexts(HtmlDocument doc, string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            return [];
        }

        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes is null || nodes.Count == 0)
        {
            return [];
        }

        return nodes
            .Select(n => WebUtility.HtmlDecode(n.InnerText).Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private protected string? SelectAttribute(HtmlDocument doc, string xpath, string attribute)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            return null;
        }

        var node = doc.DocumentNode.SelectSingleNode(xpath);
        var value = node?.GetAttributeValue(attribute, null);
        return string.IsNullOrWhiteSpace(value) ? null : WebUtility.HtmlDecode(value);
    }

    private protected List<string> SelectAttributes(HtmlDocument doc, string xpath, string attribute)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            return [];
        }

        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes is null || nodes.Count == 0)
        {
            return [];
        }

        return nodes
            .Select(n => n.GetAttributeValue(attribute, null))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => WebUtility.HtmlDecode(v!))
            .ToList();
    }

    private protected static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private protected static string? FirstNonEmptyNullable(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private protected static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Fast path: the whole value is a date.
        if (DateTime.TryParse(text, out var date))
        {
            return date;
        }

        // Slow path: the value is a labelled line like "Release Date: 2020-06-05"
        // (JavBus renders bare text nodes next to the label span) — pull the
        // first date-like string out of it.
        var m = InlineDateRegex().Match(text);
        return m.Success
            && int.TryParse(m.Groups[1].Value, out var year)
            && int.TryParse(m.Groups[2].Value, out var month)
            && int.TryParse(m.Groups[3].Value, out var day)
            ? BuildDate(year, month, day)
            : null;
    }

    private static DateTime? BuildDate(int year, int month, int day)
    {
        if (year is < 1980 or > 2100 || month is < 1 or > 12 || day is < 1 or > 31)
        {
            return null;
        }

        try
        {
            return new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

#if NET7_0_OR_GREATER
    [GeneratedRegex(@"\b((?:19|20)\d{2})[-/.](\d{1,2})[-/.](\d{1,2})\b")]
    private static partial Regex InlineDateRegex();
#else
    private static readonly Regex InlineDateRegexInstance = new(@"\b((?:19|20)\d{2})[-/.](\d{1,2})[-/.](\d{1,2})\b", RegexOptions.Compiled);

    private static Regex InlineDateRegex() => InlineDateRegexInstance;
#endif

    private protected static int? ParseRuntime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = new string(text.Trim().Where(char.IsDigit).ToArray());
        // Sanity bound: a JAV runtime is at most a day. Anything larger is
        // misparsed noise (a date, an id) and must not become a runtime.
        return int.TryParse(digits, out var minutes) && minutes > 0 && minutes < 24 * 60 ? minutes : null;
    }

    /// <summary>
    /// Reports whether a scraped title is really an error/interstitial page
    /// ("403 ERROR", "Access denied", "Just a moment…") rather than a real
    /// video title. Such pages must never produce a cached record.
    /// </summary>
    /// <param name="title">The scraped title text.</param>
    /// <returns><c>true</c> when the title identifies an error page.</returns>
    internal static bool IsErrorTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var trimmed = title.Trim();
        if (trimmed.Length is < 4 or > 300)
        {
            return true;
        }

        return trimmed.StartsWith("403", StringComparison.Ordinal)
            || trimmed.StartsWith("404", StringComparison.Ordinal)
            || trimmed.StartsWith("503", StringComparison.Ordinal)
            || trimmed.Contains("request could not be satisfied", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("just a moment", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("attention required", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("checking your browser", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("redirecting", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("error code: 1", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the short site name used in log messages.
    /// </summary>
    protected abstract string SiteName { get; }

    /// <summary>
    /// Finds the video for a product code on this site. Implemented by each
    /// concrete scraper: the hand-written ones walk their own search pages,
    /// generic ones execute their <see cref="SiteDefinition"/>.
    /// </summary>
    /// <param name="code">Product code in any casing/padding.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scraped record, or <c>null</c> when the site has nothing.</returns>
    public abstract Task<JavVideo?> FindByCodeAsync(string code, CancellationToken ct);
}
