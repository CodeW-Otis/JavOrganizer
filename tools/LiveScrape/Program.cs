// Live-network test harness for the JavOrganizer scraping pipeline.
//
// This is NOT part of the offline suite (which must never touch the network).
// It runs the real scrapers against the real sites for one product code and
// reports, per site, whether the site was reached and what metadata came back.
//
// Usage:
//   LiveScrape <CODE> [siteKey ...]
//
// With no site keys every registered scraper is exercised.

using System.Diagnostics;
using Jellyfin.Plugin.JavOrganizer;

var code = args.Length > 0 ? args[0] : "MIAB-492";
var only = args.Skip(1).ToArray();

// The scrapers read Plugin.EffectiveConfiguration, which falls back to
// built-in defaults when no plugin instance exists — exactly this harness's
// situation. Those defaults have an empty FlareSolverr URL, which makes
// every Cloudflare-protected site look blocked even though the live plugin
// (which loads this same file) reaches them fine. Reading the real
// configuration file keeps the harness honest about what the server does.
var flareSolverr = LiveConfig.LoadFlareSolverrUrl();

Console.WriteLine($"=== JavOrganizer live scrape test: {code} ===");
Console.WriteLine($"Started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"FlareSolverr: {(flareSolverr.Length == 0 ? "(none configured; Cloudflare sites will report blocked)" : flareSolverr)}");
Console.WriteLine();

var logger = new ConsoleLogger();
var scrapers = BuildScrapers(logger, only);

Console.WriteLine($"Sites to exercise: {scrapers.Count}");
foreach (var (key, s) in scrapers)
{
    Console.WriteLine($"  {key,-14} {s.GetType().Name,-22} available={s.IsAvailable}");
}

Console.WriteLine();
Console.WriteLine("=== running each site ===");
Console.WriteLine($"{"SITE",-14} {"REACHED",-8} {"MS",-7} {"RESULT"}");
Console.WriteLine(new string('-', 110));

var results = new List<SiteResult>();

foreach (var (key, scraper) in scrapers)
{
    var sw = Stopwatch.StartNew();
    JavVideo? video = null;
    string outcome;
    bool reached;

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        video = await scraper.FindByCodeAsync(code, cts.Token);
        sw.Stop();
        reached = scraper.SiteWasReached;

        if (video is null)
        {
            outcome = reached ? "no match" : "NOT REACHED (blocked/error)";
        }
        else
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(video.Title)) bits.Add("title");
            if (video.CoverUrl is not null) bits.Add("cover");
            if (video.ReleaseDate is not null) bits.Add("date");
            if (video.RuntimeMinutes is not null) bits.Add("runtime");
            if (video.Maker.Length > 0) bits.Add("maker");
            if (video.Genres.Count > 0) bits.Add($"genres({video.Genres.Count})");
            if (video.Actresses.Count > 0) bits.Add($"actresses({video.Actresses.Count})");
            if (video.MaleActors.Count > 0) bits.Add($"maleActors({video.MaleActors.Count})");
            if (video.PreviewUrls.Count > 0) bits.Add($"previews({video.PreviewUrls.Count})");
            if (video.PersonImageUrls.Count > 0) bits.Add($"personPhotos({video.PersonImageUrls.Count})");
            if (video.CastStarIds.Count > 0) bits.Add($"starIds({video.CastStarIds.Count})");
            outcome = "OK: " + string.Join(", ", bits);
        }
    }
    catch (OperationCanceledException)
    {
        sw.Stop();
        reached = false;
        outcome = "TIMEOUT after 60s";
    }
    catch (Exception ex)
    {
        sw.Stop();
        reached = false;
        outcome = $"EXCEPTION {ex.GetType().Name}: {ex.Message}";
    }

    Console.WriteLine($"{key,-14} {(reached ? "yes" : "no"),-8} {sw.ElapsedMilliseconds,-7} {outcome}");
    results.Add(new SiteResult(key, reached, video, sw.ElapsedMilliseconds, outcome));
}

Console.WriteLine();
Console.WriteLine("=== summary ===");
var reachedCount = results.Count(r => r.Reached);
var parsedCount = results.Count(r => r.Video is not null);
Console.WriteLine($"  sites exercised : {results.Count}");
Console.WriteLine($"  sites reached   : {reachedCount}");
Console.WriteLine($"  sites with data : {parsedCount}");
Console.WriteLine();

Console.WriteLine("=== merged record (what the library would receive) ===");
// Merge in the SAME priority order the plugin uses (javlibrary, then the
// hand-written scrapers, then catalog sites by ascending priority number).
// Merging in discovery order would wrongly let a low-priority site win.
JavVideo? merged = null;
var byKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    ["javlibrary"] = 10,
    ["javdb"] = 20,
    ["javbus"] = 30,
    ["missav"] = 40,
    ["missav-mirror"] = 41
};
foreach (var s in JavSiteCatalog.Sites)
{
    byKey[s.Key] = s.Priority;
}

foreach (var r in results
    .Where(r => r.Video is not null)
    .OrderBy(r => byKey.TryGetValue(r.Key, out var p) ? p : 999))
{
    merged = JavMetadataProvider.Merge(merged, r.Video);
}

if (merged is null)
{
    Console.WriteLine("  NOTHING MERGED - no site returned a record");
    return 1;
}

Console.WriteLine($"  code        : {merged.Code}");
Console.WriteLine($"  title       : {Truncate(merged.Title, 90)}");
Console.WriteLine($"  releaseDate : {merged.ReleaseDate:yyyy-MM-dd}");
Console.WriteLine($"  runtime     : {merged.RuntimeMinutes} min");
Console.WriteLine($"  maker       : {merged.Maker}");
Console.WriteLine($"  director    : {merged.Director}");
Console.WriteLine($"  label       : {merged.Label}");
Console.WriteLine($"  cover       : {merged.CoverUrl}");
Console.WriteLine($"  genres      : {merged.Genres.Count} -> {Truncate(string.Join(", ", merged.Genres.Take(6)), 80)}");
Console.WriteLine($"  actresses   : {merged.Actresses.Count} -> {Truncate(string.Join(", ", merged.Actresses.Take(6)), 80)}");
Console.WriteLine($"  maleActors  : {merged.MaleActors.Count} -> {Truncate(string.Join(", ", merged.MaleActors.Take(6)), 80)}");
Console.WriteLine($"  previews    : {merged.PreviewUrls.Count}");
Console.WriteLine($"  personPhotos: {merged.PersonImageUrls.Count}");
Console.WriteLine($"  starIds     : {merged.CastStarIds.Count} -> {Truncate(string.Join(", ", merged.CastStarIds.Select(kv => kv.Key + "=" + kv.Value).Take(6)), 80)}");
Console.WriteLine($"  built name  : {Truncate(JavMetadataProvider.BuildName(merged), 90)}");
Console.WriteLine();

var people = merged.Actresses.Concat(merged.MaleActors).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
if (people.Count > 0)
{
    Console.WriteLine("=== performer photo resolution (the cover fix) ===");
    foreach (var p in people.Take(12))
    {
        var url = SiteScraper.PersonImageFor(merged, p);
        Console.WriteLine($"  {p,-28} -> {(url is null ? "(none - falls back to a video cover)" : Truncate(url, 70))}");
    }
}

return 0;

static List<(string Key, SiteScraper Scraper)> BuildScrapers(ConsoleLogger logger, string[] only)
{
    var lang = "en";
    var all = new List<(string, SiteScraper)>
    {
        ("javlibrary", new JavLibraryScraper(logger, lang)),
        ("javbus", new JavBusScraper(logger, lang)),
        ("javdb", new JavDbScraper(logger)),
        ("missav", new MissAvScraper(logger, lang, "https://missav.ws/")),
        ("missav-mirror", new MissAvScraper(logger, lang, "https://missav.ai/")),
    };

    foreach (var site in JavSiteCatalog.Sites)
    {
        all.Add((site.Key, new GenericSiteScraper(site, logger)));
    }

    return only.Length == 0
        ? all
        : all.Where(a => only.Contains(a.Item1, StringComparer.OrdinalIgnoreCase)).ToList();
}

static string Truncate(string? s, int n)
    => string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= n ? s : s[..n] + "...");

internal sealed record SiteResult(string Key, bool Reached, JavVideo? Video, long Ms, string Outcome);

/// <summary>
/// Reads the live Jellyfin plugin configuration so the harness scrapes with
/// the same settings the server uses. Only the FlareSolverr URL matters:
/// without it every Cloudflare-protected site reports "blocked", which would
/// misrepresent a working setup as broken.
/// </summary>
/// <remarks>
/// The value is exported as an environment variable that
/// <see cref="FlareSolverrUrls"/> honours when the plugin has no loaded
/// configuration, so the harness exercises the same code path the server
/// does rather than a private test seam.
/// </remarks>
internal static class LiveConfig
{
    /// <summary>
    /// Finds the plugin's configuration XML under the Jellyfin data folder
    /// and exports the configured FlareSolverr API URL.
    /// </summary>
    /// <returns>The URL, or an empty string when none is configured.</returns>
    internal static string LoadFlareSolverrUrl()
    {
        var overrideValue = Environment.GetEnvironmentVariable(FlareSolverrUrls.UrlOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue;
        }

        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                return string.Empty;
            }

            // Jellyfin keeps plugin configuration beside the plugin folders.
            var candidate = Path.Combine(root, "Jellyfin", "plugins", "configurations", "Jellyfin.Plugin.JavOrganizer.xml");
            if (!File.Exists(candidate))
            {
                return string.Empty;
            }

            var xml = File.ReadAllText(candidate);
            var match = System.Text.RegularExpressions.Regex.Match(xml, @"<FlareSolverrUrl>([^<]*)</FlareSolverrUrl>");
            var url = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
            if (url.Length > 0)
            {
                Environment.SetEnvironmentVariable(FlareSolverrUrls.UrlOverrideVariable, url);
            }

            return url;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

internal sealed class ConsoleLogger : Microsoft.Extensions.Logging.ILogger<GenericSiteScraper>
{
    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
        {
            Console.WriteLine($"      [{logLevel}] {formatter(state, exception)}");
        }
    }

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Noop();

    private sealed class Noop : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
