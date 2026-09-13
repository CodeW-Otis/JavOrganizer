using System.Collections.Concurrent;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Creates and caches scraper instances for every site in the catalog.
/// </summary>
/// <remarks>
/// <para>Instances are keyed by site + language, so changing the language in
/// the configuration transparently builds fresh scrapers while the old ones
/// are disposed; the expensive resources (<see cref="System.Net.Http.HttpClient"/>,
/// Cloudflare clearances) stay cached for unchanged combinations.</para>
/// <para>All scrapers share one <see cref="PerSiteRateLimiter"/>, whose
/// spacing reads the live configuration, so changing the request delay also
/// takes effect without a restart.</para>
/// <para>The four hand-written scrapers (JavLibrary, JavDB, JavBus and the
/// MissAV template family) get fixed priorities 10–40; generic catalog
/// sites follow with 50+.</para>
/// </remarks>
internal static class JavScraperFactory
{
    private static readonly ConcurrentDictionary<string, SiteScraper> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object LimiterLock = new();
    private static PerSiteRateLimiter _limiter = new(() => TimeSpan.FromMilliseconds(
        Math.Max(250, Plugin.EffectiveConfiguration.RequestDelayMs)));

    /// <summary>
    /// Gets the shared per-site rate limiter smoothing requests across all
    /// parallel workers and all sites.
    /// </summary>
    internal static PerSiteRateLimiter Limiter
    {
        get
        {
            lock (LimiterLock)
            {
                return _limiter;
            }
        }
    }

    /// <summary>
    /// The set of currently enabled scrapers, ranked by priority: JavLibrary,
    /// JavDB, JavBus and the MissAV family first (hand-written, richest
    /// parsing), then the generic catalog sites. Banned and unreachable
    /// sites are skipped and their slots pass to the next site down the
    /// ranking.
    /// </summary>
    /// <returns>Scrapers sorted by ascending priority.</returns>
    internal static List<SiteScraper> GetEnabledScrapers()
    {
        var config = Plugin.EffectiveConfiguration;
        var scrapers = new List<(SiteScraper Scraper, int Priority)>();

        if (config.UseJavBus)
        {
            scrapers.Add((GetOrCreate<JavBusScraper>("javbus"), 30));
        }

        if (config.UseMissAv)
        {
            scrapers.Add((GetMissAv("missav", "https://missav.ws/"), 40));
        }

        if (config.UseMissAvMirror)
        {
            scrapers.Add((GetMissAv("missav-mirror", "https://missav.ai/"), 41));
        }

        if (config.UseJavDb)
        {
            scrapers.Add((GetOrCreate<JavDbScraper>("javdb"), 20));
        }

        foreach (var site in JavSiteCatalog.Sites)
        {
            if (!JavSiteCatalog.IsEnabled(site.Key))
            {
                continue;
            }

            scrapers.Add((GetOrCreateGeneric(site), site.Priority));
        }

        return scrapers
            .OrderBy(entry => entry.Priority)
            .Select(entry => entry.Scraper)
            .ToList();
    }

    /// <summary>
    /// Reads or creates the shared JavLibrary scraper (priority 10 — always
    /// the baseline when enabled; it is permanently enabled as the primary
    /// source).
    /// </summary>
    internal static JavLibraryScraper GetLibraryScraper()
    {
        var lang = Plugin.EffectiveConfiguration.Language;
        return (JavLibraryScraper)Cache.GetOrAdd($"javlibrary:{lang}", _ =>
        {
            var scraper = new JavLibraryScraper(PluginLogger.Logger, lang);
            scraper.AttachRateLimiter(Limiter);
            return scraper;
        });
    }

    /// <summary>
    /// Reads or creates a scraper by key, building it with the supplied
    /// factory on a cache miss.
    /// </summary>
    private static SiteScraper GetOrCreate<T>(string key)
        where T : SiteScraper
        => Cache.GetOrAdd($"{key}:{Plugin.EffectiveConfiguration.Language}", _ => Build<T>(key));

    /// <summary>
    /// Reads or creates a MissAV-template scraper bound to a specific
    /// domain (main site or mirror).
    /// </summary>
    private static SiteScraper GetMissAv(string key, string baseUrl)
    {
        var lang = Plugin.EffectiveConfiguration.Language;
        return Cache.GetOrAdd($"{key}:{lang}", _ =>
        {
            var scraper = new MissAvScraper(PluginLogger.Logger, lang, baseUrl);
            scraper.AttachRateLimiter(Limiter);
            return scraper;
        });
    }

    private static SiteScraper GetOrCreateGeneric(SiteDefinition site)
        => Cache.GetOrAdd($"{site.Key}:{Plugin.EffectiveConfiguration.Language}", _ =>
        {
            var scraper = new GenericSiteScraper(site, PluginLogger.Logger);
            scraper.AttachRateLimiter(Limiter);
            return scraper;
        });

    private static SiteScraper Build<T>(string key)
        where T : SiteScraper
    {
        SiteScraper scraper = key switch
        {
            "javbus" => new JavBusScraper(PluginLogger.Logger, Plugin.EffectiveConfiguration.Language),
            "javdb" => new JavDbScraper(PluginLogger.Logger),
            _ => throw new InvalidOperationException($"Unknown scraper key '{key}'.")
        };
        scraper.AttachRateLimiter(Limiter);
        return scraper;
    }
}

/// <summary>
/// Lazy process-wide logger fallback used when building scrapers outside a
/// Jellyfin DI scope (unit tests, manual runs). Inside the server, the
/// plugin instance's logger factory supplies the real one.
/// </summary>
internal static class PluginLogger
{
    private static Microsoft.Extensions.Logging.ILogger? _logger;

    /// <summary>
    /// Sets the logger used by factory-built scrapers; called once when the
    /// plugin instance is constructed.
    /// </summary>
    internal static void Initialize(Microsoft.Extensions.Logging.ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets a usable logger: the real one when initialized, otherwise a
    /// null logger that swallows output.
    /// </summary>
    internal static Microsoft.Extensions.Logging.ILogger Logger =>
        _logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
