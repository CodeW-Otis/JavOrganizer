using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// User-adjustable settings of the JavOrganizer plugin, editable from the
/// plugin's configuration page in the web client.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class
    /// with defaults suitable for an unconfigured server. New sites default to
    /// enabled; the per-code cap keeps per-code traffic polite even with the
    /// whole catalog turned on.
    /// </summary>
    public PluginConfiguration()
    {
        Language = "en";
        UseJavDb = true;
        UseJavBus = true;
        UseMissAv = true;
        UseMissAvMirror = true;
        UseOneJav = true;
        UseFanza = true;
        UseFanzaDvd = true;
        UseJavLand = true;
        Use123Av = true;
        UseSextb = true;
        UseSupJav = true;
        UseJavGg = true;
        UseJavSeen = true;
        UseJavMix = true;
        UseJavQuick = true;
        UseJavTube = true;
        UseJavGuru = true;
        UseMgstage = true;
        AutoScanOnStartup = true;
        MaxConcurrentScrapes = 4;
        MaxSitesPerScrape = 8;
        BuildCollections = true;
        MinVideosPerCollection = 2;
        CacheHours = 720; // 30 days.
        NegativeCacheHours = 168; // 1 week.
        RequestDelayMs = 500;
        FlareSolverrUrl = string.Empty;
        FlaresolverrExecutablePath = string.Empty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin automatically scans
    /// the library for videos without JavOrganizer metadata when the server
    /// starts, and scrapes them.
    /// </summary>
    public bool AutoScanOnStartup { get; set; }

    /// <summary>
    /// Gets or sets how many videos are scraped in parallel during scans.
    /// Higher values fill the library faster but hit the sites harder;
    /// 4 is a polite default. Zero falls back to sequential scraping.
    /// </summary>
    public int MaxConcurrentScrapes { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of websites scraped at the same time
    /// for one product code. Enabled sites are ranked by priority (the best
    /// and most reliable sources first); the cap keeps each video's fan-out
    /// polite while spreading requests across many different sites — which is
    /// itself anti-ban protection, because every single site sees far fewer
    /// requests. Banned or unreachable sites are skipped and the next site
    /// in the ranking takes their slot. Range 1–20; default 8.
    /// </summary>
    public int MaxSitesPerScrape { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JavBus is scraped. JavBus
    /// serves pages directly with an age cookie and rarely blocks, making it
    /// a fast second source for covers and previews.
    /// </summary>
    public bool UseJavBus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the MissAV mirror domain
    /// (missav.ai) is scraped in addition to the main site. MissAV rotates
    /// its domains, so the mirror adds resilience when one domain is blocked.
    /// </summary>
    public bool UseMissAvMirror { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether MissAV is scraped. MissAV
    /// labels cast members with explicit Actress/Actor fields, making it a
    /// gender-aware source alongside JavDB.
    /// </summary>
    public bool UseMissAv { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether OneJAV is scraped. OneJAV
    /// provides covers, release dates and cast from its per-code pages.
    /// </summary>
    public bool UseOneJav { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether FANZA (DMM digital) is
    /// scraped. FANZA is the official Japanese catalog with very rich data;
    /// it sometimes blocks non-Japanese IPs, in which case the site is
    /// skipped automatically.
    /// </summary>
    public bool UseFanza { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the FANZA DVD (mono) catalog
    /// is scraped in addition to the digital one — two independent pages.
    /// </summary>
    public bool UseFanzaDvd { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JavLand is scraped. JavLand
    /// is a JavBus-family site with per-code pages.
    /// </summary>
    public bool UseJavLand { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 123AV is scraped. 123AV uses
    /// the same page template family as MissAV and labels cast by gender.
    /// </summary>
    public bool Use123Av { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether SEXTB is scraped. SEXTB
    /// serves per-code pages with cover and cast.
    /// </summary>
    public bool UseSextb { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether SupJav is scraped
    /// (WordPress-style search + detail pages).
    /// </summary>
    public bool UseSupJav { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JavGG is scraped
    /// (WordPress-style search + detail pages).
    /// </summary>
    public bool UseJavGg { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JavSeen is scraped
    /// (WordPress-style search + detail pages).
    /// </summary>
    public bool UseJavSeen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JavMix is scraped
    /// (WordPress-style search + detail pages).
    /// </summary>
    public bool UseJavMix { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JavQuick is scraped
    /// (WordPress-style search + detail pages).
    /// </summary>
    public bool UseJavQuick { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JavTube is scraped
    /// (WordPress-style search + detail pages).
    /// </summary>
    public bool UseJavTube { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether jav.guru is scraped.
    /// jav.guru labels its cast by gender ("Actress:" / "Actor:") and serves
    /// English titles, so it both fills gaps and refines the gender split
    /// used by the performer collections.
    /// </summary>
    public bool UseJavGuru { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether MGStage is scraped — the
    /// official store for amateur labels (SIRO, DVAJ-on-stage codes).
    /// </summary>
    public bool UseMgstage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the collections task builds
    /// per-person collections plus Most Viewed / Most Liked rankings.
    /// </summary>
    public bool BuildCollections { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether collections are also built
    /// per studio (e.g. "Studio: MOODYZ").
    /// </summary>
    public bool BuildStudioCollections { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether collections are also built
    /// per genre (e.g. "Genre: Creampie").
    /// </summary>
    public bool BuildGenreCollections { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether collections are also built
    /// per release year (e.g. "Year: 2024").
    /// </summary>
    public bool BuildYearCollections { get; set; }

    /// <summary>
    /// Gets or sets the minimum number of videos a person must appear in
    /// before a collection is created for them.
    /// </summary>
    public int MinVideosPerCollection { get; set; }

    /// <summary>
    /// Gets or sets the language prefix used for site URLs ("en", "ja",
    /// "zh", "tw"); affects scraped titles and genres.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JavDB is scraped in addition
    /// to JavLibrary. JavDB provides gender-separated actor credits.
    /// </summary>
    public bool UseJavDb { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether scraping should fall back to
    /// JavBus when the other sites have no match. Placeholder for future work.
    /// </summary>
    public bool UseJavBusFallback { get; set; }

    /// <summary>
    /// Gets or sets how many hours a successful scrape stays fresh before
    /// the next scan re-fetches it. Default is 30 days. Zero disables expiry.
    /// </summary>
    public int CacheHours { get; set; }

    /// <summary>
    /// Gets or sets how many hours a "not found" result is remembered, so a
    /// library of unmatched files does not re-query the sites on every scan.
    /// Zero disables negative caching.
    /// </summary>
    public int NegativeCacheHours { get; set; }

    /// <summary>
    /// Gets or sets the pause between page fetches in milliseconds, to avoid
    /// rate-limiting by the sites. Zero disables the delay.
    /// </summary>
    public int RequestDelayMs { get; set; }

    /// <summary>
    /// Gets or sets the base URL of a FlareSolverr instance used to bypass
    /// Cloudflare challenges. Empty string disables the fallback.
    /// </summary>
    public string FlareSolverrUrl { get; set; }

    /// <summary>
    /// Gets or sets the path to a FlareSolverr executable the plugin should
    /// run alongside the server (started with Jellyfin, stopped with
    /// Jellyfin). Empty means the plugin manages no process itself.
    /// </summary>
    public string FlaresolverrExecutablePath { get; set; }
}
