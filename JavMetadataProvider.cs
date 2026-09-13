using System.Text;
#if !JF_LEGACY_PERSON
using Jellyfin.Data.Enums;
#endif
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Remote metadata provider that resolves movie items by the JAV product
/// code found in their file name. It scrapes up to
/// <see cref="PluginConfiguration.MaxSitesPerScrape"/> websites per code —
/// ranked by priority, skipping banned and unreachable sites — merges the
/// results, and caches them so each video is scraped once and refreshed
/// only after the configured TTL (default 30 days).
/// </summary>
/// <remarks>
/// <para>Jellyfin requires <c>TItemType : IHasLookupInfo&lt;TLookupInfoType&gt;</c>,
/// and only <see cref="Movie"/> (a subclass of <see cref="Video"/>) implements
/// <c>IHasLookupInfo&lt;MovieInfo&gt;</c>, so the provider is registered for
/// <see cref="Movie"/> even though it fills fields of the underlying video item.</para>
/// <para>Codes that no site could match are remembered as negative cache
/// entries, so library re-scans only query the sites for genuinely new files.
/// Thin records — a scrape that produced only a title and nothing else — are
/// re-scraped after a short grace period instead of being served from cache
/// forever, which is what left some library items stuck at "title only".</para>
/// </remarks>
public sealed class JavMetadataProvider : IRemoteMetadataProvider<Movie, MovieInfo>
{
    /// <summary>
    /// A cached record older than this with no usable substance (no cover,
    /// genres, cast, date or runtime) is re-scraped instead of served, so a
    /// partial scrape from a bad moment heals itself on a later scan.
    /// </summary>
    private static readonly TimeSpan ThinRetryGrace = TimeSpan.FromHours(6);

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavMetadataProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger scoped to this provider.</param>
    public JavMetadataProvider(ILogger<JavMetadataProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        // All site selection happens per request through the factory, so
        // configuration saves (site toggles, language, delay, site cap)
        // take effect immediately without a server restart.
    }

    /// <summary>
    /// Gets the provider name shown in library metadata settings.
    /// </summary>
    public string Name => "JavOrganizer";

    /// <summary>
    /// Gets the well-known provider-id key this plugin writes onto items.
    /// </summary>
    public const string ProviderIdKey = "JavLibrary";

    /// <inheritdoc />
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken ct)
    {
        var result = new MetadataResult<Movie>();

        var code = JavCodeParser.ExtractCode(info.Path) ?? JavCodeParser.ExtractCode(info.Name);
        if (code is null)
        {
            _logger.LogDebug("No JAV code found in '{Name}' / '{Path}'", info.Name, info.Path);
            return result;
        }

        var video = await GetVideoWithCacheAsync(code, ct).ConfigureAwait(false);
        if (video is null)
        {
            _logger.LogDebug("No site had metadata for code '{Code}'", code);
            return result;
        }

        result.HasMetadata = true;
        result.Item = MapToMovie(video);

        foreach (var actress in video.Actresses)
        {
            AddPerson(result, actress, isActor: true);
        }

        foreach (var actor in video.MaleActors)
        {
            AddPerson(result, actor, isActor: true);
        }

        if (!string.IsNullOrWhiteSpace(video.Director))
        {
            AddPerson(result, video.Director, isActor: false);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken ct)
    {
        return Task.FromResult<IEnumerable<RemoteSearchResult>>([]);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken ct)
    {
        return JavHttp.GetImageResponse(url, ct);
    }

    /// <summary>
    /// Adds a cast member, null-safe and deduplicated by name.
    /// </summary>
    /// <param name="result">The result receiving the person.</param>
    /// <param name="name">Person display name.</param>
    /// <param name="isActor"><c>true</c> for an actor/actress role, <c>false</c> for director.</param>
    private static void AddPerson(MetadataResult<Movie> result, string name, bool isActor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // People is null until the first AddPerson; treat that as "no duplicates".
        if (result.People is not null
            && result.People.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

#if JF_LEGACY_PERSON
        // Jellyfin 10.8: PersonInfo.Type is a plain string.
        result.AddPerson(new PersonInfo { Name = name, Type = isActor ? "Actor" : "Director" });
#else
        result.AddPerson(new PersonInfo { Name = name, Type = isActor ? PersonKind.Actor : PersonKind.Director });
#endif
    }

    /// <summary>
    /// Maps a merged <see cref="JavVideo"/> onto a Jellyfin movie item.
    /// </summary>
    /// <param name="video">The scraped record.</param>
    /// <returns>A new <see cref="Movie"/> with the mapped fields.</returns>
    internal static Movie MapToMovie(JavVideo video)
    {
        var item = new Movie
        {
            // Keep the JAV code in the title so items are identifiable in the
            // library ("ABP-123 Title…"); fall back to the bare code when the
            // scraped title is empty.
            Name = BuildName(video),
            Overview = BuildOverview(video),
            ProductionYear = video.ReleaseDate?.Year,
            PremiereDate = video.ReleaseDate,
            RunTimeTicks = video.RuntimeMinutes is > 0
                ? TimeSpan.FromMinutes(video.RuntimeMinutes.Value).Ticks
                : null
        };

        if (!string.IsNullOrWhiteSpace(video.Maker))
        {
            item.Studios = [video.Maker];
        }

        if (video.Genres.Count > 0)
        {
            item.Genres = [.. video.Genres];
        }

        if (!string.IsNullOrWhiteSpace(video.Label))
        {
            item.Tags = [video.Label];
        }

        if (!string.IsNullOrWhiteSpace(video.VideoId))
        {
            item.ProviderIds ??= new Dictionary<string, string>();
            item.ProviderIds[ProviderIdKey] = video.VideoId;
        }
        else if (!string.IsNullOrWhiteSpace(video.Code))
        {
            // Sites whose pages carry no per-video id (JavBus, generic
            // catalog sites) are identified by their product code — never
            // by a random query-string fragment.
            item.ProviderIds ??= new Dictionary<string, string>();
            item.ProviderIds[ProviderIdKey] = video.Code;
        }

        return item;
    }

    /// <summary>
    /// Builds the item name as "CODE Title", keeping the JAV product code
    /// visible in the library. When the scraped title already starts with the
    /// code it is used as-is; when there is no title the bare code is used.
    /// </summary>
    /// <param name="video">The scraped record.</param>
    /// <returns>The display name for the item.</returns>
    internal static string BuildName(JavVideo video)
    {
        var title = video.Title?.Trim() ?? string.Empty;
        if (SiteScraper.IsErrorTitle(title) && title.Length > 0)
        {
            // Error-page leftovers ("403 ERROR") never belong in a name.
            title = string.Empty;
        }

        if (title.Length == 0)
        {
            return video.Code;
        }

        if (title.StartsWith(video.Code, StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        return $"{video.Code} {title}";
    }

    /// <summary>
    /// Builds the overview block shown in the client detail view.
    /// </summary>
    /// <param name="video">The scraped record.</param>
    /// <returns>Human-readable overview text.</returns>
    internal static string BuildOverview(JavVideo video)
    {
        var builder = new StringBuilder();
        builder.Append("Provider: JavOrganizer");
        builder.Append("\nCode: ").Append(video.Code);
        if (!string.IsNullOrWhiteSpace(video.Maker))
        {
            builder.Append("\nMaker: ").Append(video.Maker);
        }

        if (!string.IsNullOrWhiteSpace(video.Director))
        {
            builder.Append("\nDirector: ").Append(video.Director);
        }

        if (!string.IsNullOrWhiteSpace(video.Label))
        {
            builder.Append("\nLabel: ").Append(video.Label);
        }

        if (video.Actresses.Count > 0)
        {
            builder.Append("\nActresses: ").Append(string.Join(", ", video.Actresses));
        }

        if (video.MaleActors.Count > 0)
        {
            builder.Append("\nActors: ").Append(string.Join(", ", video.MaleActors));
        }

        if (video.Genres.Count > 0)
        {
            builder.Append("\nGenres: ").Append(string.Join(", ", video.Genres));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a cached record for the code when fresh enough, otherwise
    /// scrapes the enabled sites, merges their records, and stores the
    /// result — or a negative marker when nothing matched.
    /// </summary>
    /// <param name="code">Product code in any casing/padding.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The merged record, or <c>null</c> when not found.</returns>
    private async Task<JavVideo?> GetVideoWithCacheAsync(string code, CancellationToken ct)
    {
        var normalized = JavCodeParser.Normalize(code);
        if (normalized.Length == 0)
        {
            return null;
        }

        var cached = JavCache.TryRead(normalized, _logger);
        if (cached is not null)
        {
            // A substantive record is served from cache; a thin one (title
            // only — the state that left items at "title only" forever) is
            // re-scraped once its grace period passes, so later scans heal
            // it when sites recover or more of them are enabled.
            if (IsSubstantive(cached) || JavCache.IsFreshFor(normalized, ThinRetryGrace))
            {
                _logger.LogDebug("Cache hit for '{Code}'", normalized);
                return cached;
            }

            _logger.LogDebug("Thin cache record for '{Code}'; re-scraping for more data", normalized);
        }

        // Skip the sites entirely when the code was recently looked up and
        // nothing was found, so repeated library scans stay cheap.
        if (JavCache.IsRecentlyNotFound(normalized, _logger))
        {
            _logger.LogDebug("Negative cache hit for '{Code}'; skipping scrape", normalized);
            return null;
        }

        var result = await ScrapeAllSitesAsync(code, ct).ConfigureAwait(false);
        if (result.Video is not null)
        {
            JavCache.TryWrite(normalized, result.Video, _logger);
        }
        else if (result.AnySiteReached)
        {
            // Only a *confirmed* miss — at least one site answered and
            // genuinely had nothing — is remembered. Rate limits, bans and
            // other transient failures never poison the cache.
            JavCache.TryWriteNegative(normalized, _logger);
        }
        else
        {
            _logger.LogDebug("All sites failed transiently for '{Code}'; not caching a miss", normalized);
        }

        return result.Video;
    }

    /// <summary>
    /// Reports whether a record carries enough substance to be worth
    /// serving as-is: any of a cover, genres, cast, release date or runtime
    /// beyond the bare title.
    /// </summary>
    private static bool IsSubstantive(JavVideo video) =>
        !string.IsNullOrWhiteSpace(video.CoverUrl)
        || video.Genres.Count > 0
        || video.Actresses.Count > 0
        || video.MaleActors.Count > 0
        || video.ReleaseDate is not null
        || video.RuntimeMinutes is not null;

    /// <summary>
    /// The outcome of a multi-site scrape: the merged record when any site
    /// matched, and whether at least one site was genuinely reached (as
    /// opposed to every site being blocked, banned or rate-limited away).
    /// </summary>
    /// <param name="Video">Merged record, or <c>null</c> when nothing matched.</param>
    /// <param name="AnySiteReached"><c>true</c> when at least one site answered.</param>
    private readonly record struct ScrapeOutcome(JavVideo? Video, bool AnySiteReached);

    /// <summary>
    /// Scrapes every enabled site for the code — up to the configured
    /// maximum number of sites at the same time — and merges their records.
    /// Sites are ranked by priority; banned or unreachable sites are
    /// skipped and the next site in the ranking takes their slot, so the
    /// fan-out always uses the configured budget on sites that can actually
    /// answer. All failures are tolerated per site: a site that is disabled,
    /// banned, blocked or simply missing the code costs nothing — the merge
    /// takes whatever came back.
    /// </summary>
    /// <param name="code">Product code in any casing/padding.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The merged record and whether any site was reached.</returns>
    private async Task<ScrapeOutcome> ScrapeAllSitesAsync(string code, CancellationToken ct)
    {
        // JavLibrary is the baseline (richest parsing, previews); then the
        // other hand-written scrapers; then the generic catalog sites in
        // priority order. Available (non-banned, reachable) sites fill the
        // configured per-code slots.
        var enabled = JavScraperFactory.GetEnabledScrapers();
        var library = JavScraperFactory.GetLibraryScraper();

        var sites = new List<SiteScraper> { library };
        sites.AddRange(enabled.Where(s => !ReferenceEquals(s, library)));

        var cap = Math.Clamp(Plugin.EffectiveConfiguration.MaxSitesPerScrape, 1, 20);
        var selected = sites
            .Where(s => s.IsAvailable)
            .Take(cap)
            .ToList();

        _logger.LogDebug(
            "Scraping {Selected} of {Enabled} enabled sites for '{Code}' (cap {Cap})",
            selected.Count,
            sites.Count,
            code,
            cap);

        // Fan out to every selected site simultaneously; each one finds its
        // page, follows candidates and parses on its own. Each site also
        // reports whether it was genuinely reachable (not rate-limited,
        // banned or blocked away), which keeps transient failures from
        // turning into cached "not found" results.
        var tasks = selected
            .Select(s => GuardAsync(s.FindByCodeAsync(code, ct), s, SiteNameOf(s), code))
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Merge in priority order: the list is already ranked, so earlier
        // (richer) sites win fields and later sites fill the gaps.
        JavVideo? merged = null;
        var anySiteReached = false;
        foreach (var result in results)
        {
            merged = Merge(merged, result.Video);
            anySiteReached |= result.AnySiteReached;
        }

        return new ScrapeOutcome(merged, anySiteReached);
    }

    /// <summary>
    /// Resolves a scraper's display name without knowing its concrete type.
    /// </summary>
    private static string SiteNameOf(SiteScraper scraper) => scraper.GetType().Name.Replace("Scraper", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Runs a site scrape, converting any exception into a logged miss so a
    /// single failing site never breaks the whole multi-site merge. When the
    /// site never actually answered (rate-limited, banned or failed on
    /// every request), the failure is flagged so the caller does not cache
    /// the miss.
    /// </summary>
    /// <param name="task">The site task to await.</param>
    /// <param name="scraper">The scraper instance, for its reached state.</param>
    /// <param name="site">Site name for logging.</param>
    /// <param name="code">Product code for logging.</param>
    /// <returns>The site's record and whether the site was genuinely reached.</returns>
    private async Task<ScrapeOutcome> GuardAsync(Task<JavVideo?> task, SiteScraper scraper, string site, string code)
    {
        try
        {
            var video = await task.ConfigureAwait(false);
            return new ScrapeOutcome(video, scraper.SiteWasReached);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Site} scrape for '{Code}' failed; continuing with other sites", site, code);
            return new ScrapeOutcome(null, false);
        }
    }

    /// <summary>
    /// Merges two scraped records: the primary (higher-priority site) wins
    /// for fields it has; the secondary fills gaps and contributes its
    /// gender-separated cast lists when present. Text fields (title, cast,
    /// maker, director, label) additionally prefer the more Latin-script
    /// variant, so a Japanese-only record from one site is upgraded to the
    /// English form when any other site provides one. Null-safe in either
    /// direction, so a failure on one site costs nothing on the other.
    /// </summary>
    /// <param name="primary">Baseline record (may be <c>null</c>).</param>
    /// <param name="secondary">Additional record (may be <c>null</c>).</param>
    /// <returns>The merged record, or <c>null</c> when both are <c>null</c>.</returns>
    internal static JavVideo? Merge(JavVideo? primary, JavVideo? secondary)
    {
        if (primary is null)
        {
            return secondary;
        }

        if (secondary is null)
        {
            return primary;
        }

        primary.Title = PreferLatinText(primary.Title, secondary.Title);
        primary.VideoId = FirstNonEmpty(primary.VideoId ?? string.Empty, secondary.VideoId ?? string.Empty);
        primary.ReleaseDate ??= secondary.ReleaseDate;
        primary.RuntimeMinutes ??= secondary.RuntimeMinutes;
        primary.Maker = PreferLatinText(primary.Maker, secondary.Maker);
        primary.Director = PreferLatinText(primary.Director, secondary.Director);
        primary.Label = PreferLatinText(primary.Label, secondary.Label);
        primary.CoverUrl ??= secondary.CoverUrl;

        if (primary.Genres.Count == 0 && secondary.Genres.Count > 0)
        {
            primary.Genres = [.. secondary.Genres];
        }

        // Gender-separated lists from gender-aware sites replace the
        // unseparated cast whenever available; otherwise keep what the
        // primary had. Names prefer the Latin-script variant, so Japanese
        // names are upgraded to romanized forms when a site has them.
        if (secondary.Actresses.Count > 0)
        {
            primary.Actresses = primary.Actresses.Count == 0 || CjkRatio(primary.Actresses) > CjkRatio(secondary.Actresses)
                ? [.. secondary.Actresses]
                : primary.Actresses;
        }

        if (secondary.MaleActors.Count > 0
            && (primary.MaleActors.Count == 0 || CjkRatio(primary.MaleActors) > CjkRatio(secondary.MaleActors)))
        {
            primary.MaleActors = [.. secondary.MaleActors];
        }

        if (primary.PreviewUrls.Count == 0 && secondary.PreviewUrls.Count > 0)
        {
            primary.PreviewUrls = [.. secondary.PreviewUrls];
        }

        return primary;

        static string FirstNonEmpty(string a, string b) =>
            string.IsNullOrWhiteSpace(a) ? b : a;

        // ---- Latin-script preference ----

        static string PreferLatinText(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a))
            {
                return b;
            }

            if (string.IsNullOrWhiteSpace(b))
            {
                return a;
            }

            return TextCjkRatio(a) > TextCjkRatio(b) ? b : a;
        }

        static double TextCjkRatio(string text) =>
            CjkCount(text) / (double)Math.Max(1, text.Length);

        static double CjkRatio(IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            return values.Average(v => TextCjkRatio(v));
        }

        static int CjkCount(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var count = 0;
            foreach (var ch in text)
            {
                if (ch is >= '\u3040' and <= '\u30FF'      // Hiragana + Katakana.
                    or >= '\u3400' and <= '\u4DBF'        // CJK extension A.
                    or >= '\u4E00' and <= '\u9FFF'         // CJK unified.
                    or >= '\uFF66' and <= '\uFF9D')        // Halfwidth Katakana.
                {
                    count++;
                }
            }

            return count;
        }
    }
}
