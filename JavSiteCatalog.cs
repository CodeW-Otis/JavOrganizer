namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// How a generic site locates the detail page for a product code.
/// </summary>
public enum SiteUrlMode
{
    /// <summary>
    /// The code's detail page has a canonical URL built directly from the
    /// code (for example javbus.com/en/ABP-123).
    /// </summary>
    Canonical,

    /// <summary>
    /// The site exposes a search page; the scraper follows the first result
    /// links whose URL or text mentions the code, then parses the detail
    /// page (WordPress-style sites).
    /// </summary>
    Search,

    /// <summary>
    /// The site is one of the four hand-written scrapers with bespoke
    /// search-and-walk logic (JavLibrary, JavDB, JavBus, MissAV family).
    /// </summary>
    Custom
}

/// <summary>
/// Declarative selector set describing how to parse one generic site's
/// detail page into a <see cref="JavVideo"/>. Every selector is an XPath
/// evaluated against the detail page; empty selectors mean "this site does
/// not provide the field".
/// </summary>
public sealed class SiteSelectors
{
    /// <summary>Page title selector (may include the site's own suffix).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>og:image meta fallback is used when this is empty.</summary>
    public string Cover { get; init; } = string.Empty;

    /// <summary>Selector reading the page's own product code, for verification.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Release-date selector (text or datetime attribute via <see cref="DateAttribute"/>).</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>Optional attribute holding the date value (for example "datetime").</summary>
    public string? DateAttribute { get; init; }

    /// <summary>Runtime selector.</summary>
    public string Runtime { get; init; } = string.Empty;

    /// <summary>Maker / studio selector.</summary>
    public string Maker { get; init; } = string.Empty;

    /// <summary>Director selector.</summary>
    public string Director { get; init; } = string.Empty;

    /// <summary>Label selector.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Genre links selector (multiple).</summary>
    public string Genres { get; init; } = string.Empty;

    /// <summary>Female cast links selector (multiple).</summary>
    public string Actresses { get; init; } = string.Empty;

    /// <summary>Male cast links selector (multiple).</summary>
    public string MaleActors { get; init; } = string.Empty;

    /// <summary>Preview/screenshot image links selector (multiple).</summary>
    public string Previews { get; init; } = string.Empty;

    /// <summary>Attribute that holds the preview URL (default "href").</summary>
    public string PreviewAttribute { get; init; } = "href";

    /// <summary>Regex over the raw HTML capturing the site's internal video id.</summary>
    public string? IdPattern { get; init; }

    /// <summary>Search-mode only: selector for the result links to follow.</summary>
    public string ResultLinks { get; init; } = string.Empty;

    /// <summary>Optional regex extracting the runtime from free text (for example FANZA's Japanese labels).</summary>
    public string? RuntimePattern { get; init; }

    /// <summary>Optional regex extracting a date from free text, used when the structured selectors find nothing.</summary>
    public string? DatePattern { get; init; }
}

/// <summary>
/// One generic scraping site: where the page lives and how to read it.
/// </summary>
public sealed class SiteDefinition
{
    /// <summary>Stable key used for the config toggle and the rate limiter.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Human-readable name shown in the configuration page and logs.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Base URL of the site, with trailing slash.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>How the detail page is located.</summary>
    public SiteUrlMode Mode { get; init; }

    /// <summary>Lower number = scraped first; the per-code cap fills slots by this order.</summary>
    public int Priority { get; init; }

    /// <summary>Static cookie header (age-verification tokens).</summary>
    public string Cookie { get; init; } = string.Empty;

    /// <summary>Builds the canonical path from the canonical keyword ("ABP-123").</summary>
    public Func<string, string> CanonicalPath { get; init; } = code => code;

    /// <summary>Builds the search path from the canonical keyword.</summary>
    public Func<string, string> SearchPath { get; init; } = code => $"search/{Uri.EscapeDataString(code)}";

    /// <summary>How to parse the detail page.</summary>
    public SiteSelectors Selectors { get; init; } = new();

    /// <summary>Maximum result links to open from a search page.</summary>
    public int MaxResultHops { get; init; } = 2;
}

/// <summary>
/// The catalog of generic (declaratively described) scraping sites. The
/// four hand-written scrapers — JavLibrary, JavDB, JavBus and the
/// MissAV-template family — live outside this catalog and always take the
/// four highest priorities; everything here extends the fan-out towards the
/// 15–20-site goal with gap-filling data.
/// </summary>
internal static class JavSiteCatalog
{
    /// <summary>
    /// WordPress-family shared selectors: detail pages are blog posts whose
    /// theme reliably exposes og:image (cover), an h1 title, time/@datetime
    /// (date) and category/tag links. Cast is not gender-separated on these
    /// sites, so members land in the unseparated cast list and gender-aware
    /// sites refine them during the merge.
    /// </summary>
    private static SiteSelectors WordPressSelectors { get; } = new()
    {
        Title = "//h1[contains(@class,'entry-title')]|//h1|//h2[contains(@class,'entry-title')]",
        Date = "//time",
        DateAttribute = "datetime",
        Genres = "//a[@rel='category tag']|//a[contains(@href,'/category/')]",
        Cover = "//meta[@property='og:image']",
        Code = "//meta[@property='og:title']",
        ResultLinks = "//article//h2//a[@href]|//article//h3//a[@href]|//h2[@class='entry-title']//a[@href]"
    };

    /// <summary>WordPress-family search sites.</summary>
    private static SiteDefinition WpSite(string key, string name, string baseUrl, string searchPrefix) => new()
    {
        Key = key,
        DisplayName = name,
        BaseUrl = baseUrl,
        Mode = SiteUrlMode.Search,
        Priority = 90,
        SearchPath = code => searchPrefix + Uri.EscapeDataString(code),
        Selectors = WordPressSelectors
    };

    /// <summary>
    /// The generic site catalog, ordered by priority. Priorities 1–40 are
    /// the hand-written scrapers (not listed here); generic sites run 50+.
    /// </summary>
    public static readonly IReadOnlyList<SiteDefinition> Sites =
    [
        // ---------------- OneJAV (torrent metadata; covers, dates, cast) ---------------
        new SiteDefinition
        {
            Key = "onejav",
            DisplayName = "OneJAV",
            BaseUrl = "https://onejav.com/",
            Mode = SiteUrlMode.Search,
            Priority = 50,
            SearchPath = code => $"search/{Uri.EscapeDataString(code)}",
            Selectors = new SiteSelectors
            {
                Title = "//h1[@class='title']|//a[contains(@class,'title')]",
                Cover = "//meta[@property='og:image']",
                Date = "//time",
                DateAttribute = "datetime",
                Genres = "//a[contains(@href,'/tag/')]",
                Actresses = "//a[contains(@href,'/actor/')]",
                ResultLinks = "//article//a[contains(@class,'title')][@href]|//article//a[contains(@href,'/torrent/')]",
                IdPattern = @"onejav\.com/torrent/([a-z0-9]+)"
            }
        },

        // ---------------- FANZA / DMM digital (official Japanese catalog) ---------------
        new SiteDefinition
        {
            Key = "fanza",
            DisplayName = "FANZA (DMM digital)",
            BaseUrl = "https://www.dmm.co.jp/",
            Mode = SiteUrlMode.Canonical,
            Priority = 60,
            Cookie = "age_check_done=1",
            // cid = lowercase letters + digits zero-padded to five: abp00123.
            CanonicalPath = code => $"digital/videoa/-/detail/=/cid={ToDmmCid(code)}/",
            Selectors = new SiteSelectors
            {
                Title = "//h1[@id='title']|//h1",
                Cover = "//a[@name='package-image']/img/@src|//img[contains(@src,'pics.dmm.co.jp')][1]",
                // Japanese labels: 配信開始日 = release date, 収録時間 = runtime.
                Date = "//td[preceding-sibling::th[contains(.,'配信開始日') or contains(.,'Release Date')] or contains(text(),'配信開始日') or contains(text(),'Release Date')]",
                Runtime = "//td[preceding-sibling::th[contains(.,'収録時間') or contains(.,'Duration')] or contains(text(),'収録時間') or contains(text(),'Duration') or contains(text(),'min')]",
                Maker = "//a[contains(@href,'article=maker')]",
                Genres = "//a[contains(@href,'article=genre')]",
                Actresses = "//a[contains(@href,'actress')]",
                Previews = "//a[starts-with(@name,'sample-image')]",
                IdPattern = @"cid=([a-z0-9]+)"
            }
        },

        // ---------------- FANZA / DMM DVD (mono) catalog --------------------------------
        new SiteDefinition
        {
            Key = "fanzadvd",
            DisplayName = "FANZA (DMM DVD)",
            BaseUrl = "https://www.dmm.co.jp/",
            Mode = SiteUrlMode.Canonical,
            Priority = 61,
            Cookie = "age_check_done=1",
            CanonicalPath = code => $"mono/dvd/-/detail/=/cid={ToDmmCid(code)}/",
            Selectors = new SiteSelectors
            {
                Title = "//h1[@id='title']|//h1",
                Cover = "//a[@name='package-image']/img/@src|//img[contains(@src,'pics.dmm.co.jp')][1]",
                Maker = "//a[contains(@href,'article=maker')]",
                Genres = "//a[contains(@href,'article=genre')]",
                Actresses = "//a[contains(@href,'actress')]",
                Previews = "//a[starts-with(@name,'sample-image')]",
                IdPattern = @"cid=([a-z0-9]+)"
            }
        },

        // ---------------- JavLand (JavBus-family per-code pages) -----------------------
        new SiteDefinition
        {
            Key = "javland",
            DisplayName = "JavLand",
            BaseUrl = "https://javland.com/",
            Mode = SiteUrlMode.Canonical,
            Priority = 70,
            Cookie = "dv=1",
            CanonicalPath = code => code,
            Selectors = new SiteSelectors
            {
                Title = "//h3",
                Code = "//span[contains(@class,'header') and text()='ID:']/following-sibling::span[1]",
                Date = "//span[contains(@class,'header') and text()='Release Date:']/following-sibling::span[1]",
                Runtime = "//span[contains(@class,'header') and text()='Length:']/following-sibling::span[1]",
                Director = "//span[contains(@class,'header') and text()='Director:']/following-sibling::span[1]//a",
                Maker = "//span[contains(@class,'header') and text()='Studio:']/following-sibling::span[1]//a",
                Label = "//span[contains(@class,'header') and text()='Label:']/following-sibling::span[1]//a",
                Genres = "//span[contains(@class,'genre')]//a",
                Actresses = "//a[starts-with(@href,'https://javland.com/star/') or starts-with(@href,'/star/')]",
                Cover = "//a[contains(@class,'bigImage')]//img",
                Previews = "//a[contains(@href,'/samples/') or @class='sample']",
                IdPattern = @"[?&]v=([a-z0-9]+)"
            }
        },

        // ---------------- 123AV (MissAV-template family, gender-labeled cast) -----------
        new SiteDefinition
        {
            Key = "123av",
            DisplayName = "123AV",
            BaseUrl = "https://123av.com/",
            Mode = SiteUrlMode.Canonical,
            Priority = 75,
            CanonicalPath = code => code.ToLowerInvariant(),
            Selectors = new SiteSelectors
            {
                Title = "//h1[contains(@class,'title')]|//div[contains(@class,'title')]//span[contains(@class,'font-medium')][1]",
                Code = "//span[contains(@class,'font-medium')][1]",
                Date = "//span[contains(@id,'release_date')]|//time",
                Maker = "//span[text()='Maker:']/following-sibling::a[1]",
                Director = "//span[text()='Director:']/following-sibling::a[1]",
                Label = "//span[text()='Label:']/following-sibling::a[1]",
                Genres = "//span[text()='Genre:']/following-sibling::a",
                Actresses = "//span[text()='Actress:']/following-sibling::a[1]",
                MaleActors = "//span[text()='Actor:']/following-sibling::a[1]",
                Cover = "//img[contains(@src,'/okcdn') or contains(@class,'object-cover')]",
                IdPattern = @"123av\.com/([a-z0-9]+)/[a-z]{2}/"
            }
        },

        // ---------------- SEXTB (per-code streaming pages) ------------------------------
        new SiteDefinition
        {
            Key = "sextb",
            DisplayName = "SEXTB",
            BaseUrl = "https://sextb.net/",
            Mode = SiteUrlMode.Canonical,
            Priority = 80,
            CanonicalPath = code => code,
            Selectors = new SiteSelectors
            {
                Title = "//h1|//meta[@property='og:title']",
                Cover = "//meta[@property='og:image']",
                Code = "//meta[@property='og:title']",
                Genres = "//a[contains(@href,'/tag/')]",
                Actresses = "//a[contains(@href,'/actor/')]",
                IdPattern = @"sextb\.net/([a-z0-9-]+)"
            }
        },

        // ---------------- SupJav ---------------------------------------------------------
        WpSite("supjav", "SupJav", "https://supjav.com/", "?s="),

        // ---------------- JavGG ---------------------------------------------------------
        WpSite("javgg", "JavGG", "https://javgg.net/", "?s="),

        // ---------------- JavSeen -------------------------------------------------------
        WpSite("javseen", "JavSeen", "https://javseen.com/", "?s="),

        // ---------------- JavMix -------------------------------------------------------
        WpSite("javmix", "JavMix", "https://javmix.tv/", "?s="),

        // ---------------- JavQuick ------------------------------------------------------
        WpSite("javquick", "JavQuick", "https://javquick.com/", "?s="),

        // ---------------- JavTube ------------------------------------------------------
        WpSite("javtube", "JavTube", "https://javtube.com/", "?s="),

        // ---------------- MGStage (official amateur-label store) ------------------------
        new SiteDefinition
        {
            Key = "mgstage",
            DisplayName = "MGStage",
            BaseUrl = "https://www.mgstage.com/",
            Mode = SiteUrlMode.Canonical,
            Priority = 150,
            Cookie = "adc=1",
            CanonicalPath = code => $"product/product_detail/{code}/",
            Selectors = new SiteSelectors
            {
                Title = "//h1",
                Cover = "//a[contains(@href,'pics.dmm.co.jp')][1]/img|//img[contains(@src,'pics.dmm.co.jp')][1]",
                Date = "//th[text()='Release Date:']/following-sibling::td[1]|//td[contains(@class,'date')]",
                Runtime = "//th[text()='Play Time:']/following-sibling::td[1]",
                Maker = "//th[text()='Maker:']/following-sibling::td[1]//a",
                Genres = "//a[contains(@href,'article=genre')]",
                Actresses = "//a[contains(@href,'article=actress')]|//a[contains(@href,'/actor/')]",
                IdPattern = @"product_detail/([A-Za-z0-9-]+)"
            }
        }
    ];

    /// <summary>
    /// Maps a catalog key to the configuration toggle controlling it.
    /// </summary>
    public static bool IsEnabled(string key) => key switch
    {
        "onejav" => Plugin.EffectiveConfiguration.UseOneJav,
        "fanza" => Plugin.EffectiveConfiguration.UseFanza,
        "fanzadvd" => Plugin.EffectiveConfiguration.UseFanzaDvd,
        "javland" => Plugin.EffectiveConfiguration.UseJavLand,
        "123av" => Plugin.EffectiveConfiguration.Use123Av,
        "sextb" => Plugin.EffectiveConfiguration.UseSextb,
        "supjav" => Plugin.EffectiveConfiguration.UseSupJav,
        "javgg" => Plugin.EffectiveConfiguration.UseJavGg,
        "javseen" => Plugin.EffectiveConfiguration.UseJavSeen,
        "javmix" => Plugin.EffectiveConfiguration.UseJavMix,
        "javquick" => Plugin.EffectiveConfiguration.UseJavQuick,
        "javtube" => Plugin.EffectiveConfiguration.UseJavTube,
        "mgstage" => Plugin.EffectiveConfiguration.UseMgstage,
        _ => false
    };

    /// <summary>
    /// Converts a canonical code ("ABP-123") into a DMM cid token:
    /// lowercase letters with the digits zero-padded to five ("abp00123").
    /// </summary>
    /// <param name="code">Canonical keyword form.</param>
    /// <returns>The cid token used in DMM detail URLs.</returns>
    internal static string ToDmmCid(string code)
    {
        var normalized = JavCodeParser.Normalize(code);
        if (normalized.Length == 0)
        {
            return code.ToLowerInvariant().Replace("-", string.Empty);
        }

        var parts = normalized.Split('-');
        var digits = parts.Length > 1 ? parts[^1].TrimStart('0') : string.Empty;
        if (digits.Length == 0 && parts.Length > 1)
        {
            digits = parts[^1];
        }

        return parts[0] + digits.PadLeft(5, '0');
    }
}
