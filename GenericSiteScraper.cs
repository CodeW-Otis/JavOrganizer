using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Executes one <see cref="SiteDefinition"/>: locates the detail page for a
/// product code (canonical URL or search page) and parses it into a
/// <see cref="JavVideo"/> using the definition's declarative selectors.
/// A site whose markup moved simply parses to <c>null</c> and is skipped —
/// it never breaks the multi-site merge.
/// </summary>
/// <remarks>
/// Anti-ban plumbing (rate limiting, user-agent rotation, Cloudflare
/// fallback, ban and unreachable tracking) is inherited from
/// <see cref="SiteScraper"/>.
/// </remarks>
public sealed partial class GenericSiteScraper : SiteScraper
{
    private readonly SiteDefinition _site;

#if NET7_0_OR_GREATER
    [GeneratedRegex(@"\b((?:19|20)\d{2})[-/.](\d{1,2})[-/.](\d{1,2})\b")]
    private static partial Regex AnyDateRegex();
#else
    private static readonly Regex AnyDateRegexInstance = new(@"\b((?:19|20)\d{2})[-/.](\d{1,2})[-/.](\d{1,2})\b", RegexOptions.Compiled);

    private static Regex AnyDateRegex() => AnyDateRegexInstance;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericSiteScraper"/> class.
    /// </summary>
    /// <param name="site">The declarative site definition.</param>
    /// <param name="logger">Logger for scraping diagnostics.</param>
    public GenericSiteScraper(SiteDefinition site, ILogger logger)
        : base(logger, site.BaseUrl, site.Cookie.Length > 0 ? site.Cookie : null)
    {
        _site = site;
    }

    /// <inheritdoc />
    protected override string SiteName => _site.DisplayName;

    /// <summary>
    /// Finds a video on the site by its product code, following the
    /// definition's URL mode (canonical detail URL or search page).
    /// </summary>
    /// <param name="code">Product code, e.g. "MIAB-492".</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed video, or <c>null</c> when nothing matches.</returns>
    public override async Task<JavVideo?> FindByCodeAsync(string code, CancellationToken ct)
    {
        BeginScrapeRound();
        var keyword = JavCodeParser.ToSearchKeyword(code);
        if (keyword.Length == 0)
        {
            return null;
        }

        var normalized = JavCodeParser.Normalize(code);
        if (_site.Mode == SiteUrlMode.Canonical)
        {
            var html = await GetHtmlAsync(_site.CanonicalPath(keyword), ct).ConfigureAwait(false);
            return html is null ? null : ParseAndVerify(html, normalized, code);
        }

        // Search mode: query, then follow result links mentioning the code.
        var searchHtml = await GetHtmlAsync(_site.SearchPath(keyword), ct).ConfigureAwait(false);
        if (searchHtml is null)
        {
            return null;
        }

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(searchHtml);

        var candidates = SelectAttributes(doc, _site.Selectors.ResultLinks, "href")
            .Select(ToAbsoluteUrl)
            .Where(u => u is not null)
            .Select(u => u!)
            .Where(u => UrlMentionsCode(u, normalized))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, _site.MaxResultHops))
            .ToList();

        foreach (var candidate in candidates)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            await ThrottleAsync(ct).ConfigureAwait(false);
            var html = await GetHtmlAsync(candidate, ct).ConfigureAwait(false);
            if (html is null)
            {
                continue;
            }

            var video = ParseAndVerify(html, normalized, code);
            if (video is not null)
            {
                return video;
            }
        }

        Logger.LogDebug("{Site}: no result page matched code '{Code}'", SiteName, code);
        return null;
    }

    /// <summary>
    /// Parses the detail page and rejects it when the page's own code (when
    /// the site exposes one) disagrees with the requested code, so a wrong
    /// search result never poisons the merge.
    /// </summary>
    private JavVideo? ParseAndVerify(string html, string normalized, string requestedCode)
    {
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);
        var video = ParseVideoPage(doc, html, requestedCode);
        if (video is null)
        {
            return null;
        }

        var pageCode = JavCodeParser.Normalize(video.Code);
        return pageCode.Length == 0 || pageCode == normalized ? video : null;
    }

    /// <summary>
    /// Extracts video metadata from the site's detail page using the
    /// definition's selectors. The title must come from somewhere (node or
    /// og:title meta) and must not be an error page; everything else is
    /// optional and simply leaves gaps for other sites to fill.
    /// </summary>
    /// <param name="doc">Parsed HTML document of the page.</param>
    /// <param name="html">Raw HTML of the page.</param>
    /// <param name="requestedCode">The code being searched, for diagnostics.</param>
    /// <returns>The parsed video, or <c>null</c> when the page is not a detail page.</returns>
    internal JavVideo? ParseVideoPage(HtmlAgilityPack.HtmlDocument doc, string html, string requestedCode)
    {
        var selectors = _site.Selectors;

        var title = FirstNonEmpty(
            SelectNodeText(doc, selectors.Title),
            SelectMetaContent(doc, "og:title"));
        if (string.IsNullOrWhiteSpace(title) || IsErrorTitle(title))
        {
            // An error/interstitial page (CloudFront "403 ERROR", a parked
            // domain's "Redirecting…", a bot wall) must never parse into a
            // record — it would poison the merge and the cache.
            Logger.LogDebug("{Site}: page for '{Code}' is an error page ('{Title}'), skipping", SiteName, requestedCode, title);
            return null;
        }

        title = CleanTitle(title);
        var video = new JavVideo
        {
            Title = title,
            Maker = SelectNodeText(doc, selectors.Maker),
            Director = SelectNodeText(doc, selectors.Director),
            Label = SelectNodeText(doc, selectors.Label)
        };

        // Code: structured selector first, then the title text.
        var codeText = FirstNonEmpty(
            SelectNodeText(doc, selectors.Code),
            SelectMetaContent(doc, "og:title"),
            title);
        video.Code = JavCodeParser.ExtractCode(codeText) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(video.Code))
        {
            video.Code = JavCodeParser.ExtractCode(title) ?? string.Empty;
        }

        // Date: structured selector/attribute first, then a regex sweep of
        // the raw HTML for the first date-like string.
        var dateText = selectors.DateAttribute is null
            ? SelectNodeText(doc, selectors.Date)
            : SelectAttributeValue(doc, selectors.Date, selectors.DateAttribute);
        video.ReleaseDate = ParseDateFlexible(dateText) ?? FindFirstDate(html);

        // Runtime: structured selector first, then a digits+unit sweep.
        video.RuntimeMinutes = ParseRuntime(SelectNodeText(doc, selectors.Runtime));
        if (video.RuntimeMinutes is null)
        {
            var runtime = FindFirstRuntime(html);
            if (runtime is > 0)
            {
                video.RuntimeMinutes = runtime;
            }
        }

        video.Genres = SelectNodeTexts(doc, selectors.Genres);
        video.Actresses = SelectNodeTexts(doc, selectors.Actresses);
        video.MaleActors = SelectNodeTexts(doc, selectors.MaleActors);

        // Per-performer portraits (img title=name) feed the person image
        // provider, so every performer card can carry a real photo.
        CollectPersonImages(doc, video);

        var cover = string.IsNullOrWhiteSpace(selectors.Cover)
            ? SelectMetaContent(doc, "og:image")
            : FirstNonEmpty(
                SelectAttributeValue(doc, selectors.Cover, "content"),
                SelectAttributeValue(doc, selectors.Cover, "src"),
                SelectAttributeValue(doc, selectors.Cover, "href"),
                SelectNodeText(doc, selectors.Cover));
        video.CoverUrl = ToAbsoluteImageUrl(cover.Length == 0 ? null : cover);

        if (selectors.Previews.Length > 0)
        {
            video.PreviewUrls = SelectAttributes(doc, selectors.Previews, selectors.PreviewAttribute)
                .Select(ToAbsoluteImageUrl)
                .Where(u => u is not null)
                .Select(u => u!)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(selectors.IdPattern))
        {
            var idMatch = Regex.Match(html, selectors.IdPattern, RegexOptions.IgnoreCase);
            if (idMatch.Success)
            {
                video.VideoId = idMatch.Groups[1].Value;
            }
        }

        return video;
    }

    /// <summary>
    /// Strips common site suffixes and prefixes (" - SiteName", "Watch …")
    /// from a scraped title.
    /// </summary>
    private static string CleanTitle(string title)
    {
        var value = title.Trim();
        if (value.Length == 0)
        {
            return value;
        }

        // Trim " - SiteName" / " | SiteName" tails (keep short tails that
        // are part of real JAV titles).
        var separators = new[] { " - ", " | ", " » ", " – " };
        foreach (var sep in separators)
        {
            var idx = value.LastIndexOf(sep, StringComparison.Ordinal);
            if (idx > 10 && idx < value.Length - 3)
            {
                value = value[..idx].Trim();
                break;
            }
        }

        if (value.StartsWith("Watch ", StringComparison.OrdinalIgnoreCase) && value.Length > 6)
        {
            value = value[6..].Trim();
        }

        return value;
    }

    /// <summary>
    /// Parses dates in the formats the catalog sites actually emit: ISO
    /// (2024-05-01), slashed (2024/05/01), dotted (2024.05.01), compact
    /// (20240501) and ISO with time.
    /// </summary>
    private static DateTime? ParseDateFlexible(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        var m = AnyDateRegex().Match(value);
        if (m.Success
            && int.TryParse(m.Groups[1].Value, out var year)
            && int.TryParse(m.Groups[2].Value, out var month)
            && int.TryParse(m.Groups[3].Value, out var day))
        {
            return BuildDate(year, month, day);
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : null;
    }

    /// <summary>
    /// Finds the first date-like string in raw HTML (used when structured
    /// selectors find nothing — FANZA's Japanese labels and WP themes vary).
    /// </summary>
    private static DateTime? FindFirstDate(string html)
    {
        var m = AnyDateRegex().Match(html);
        return m.Success
            && int.TryParse(m.Groups[1].Value, out var year)
            && int.TryParse(m.Groups[2].Value, out var month)
            && int.TryParse(m.Groups[3].Value, out var day)
            ? BuildDate(year, month, day)
            : null;
    }

    private static int? FindFirstRuntime(string html)
    {
        // "120 min", "120min", "120分" (Japanese pages) — only inside sane bounds.
        var m = RuntimeRegex().Match(html);
        return m.Success
            && int.TryParse(m.Groups[1].Value, out var minutes)
            && minutes is > 5 and < 24 * 60
            ? minutes
            : null;
    }

#if NET7_0_OR_GREATER
    [GeneratedRegex(@"(\d{2,3})\s*(?:min|分)", RegexOptions.IgnoreCase)]
    private static partial Regex RuntimeRegex();
#else
    private static readonly Regex RuntimeRegexInstance = new(@"(\d{2,3})\s*(?:min|分)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex RuntimeRegex() => RuntimeRegexInstance;
#endif

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

    // ---- Selector helpers (attribute- and text-oriented, empty-selector safe) ----

    private static string SelectMetaContent(HtmlAgilityPack.HtmlDocument doc, string metaName)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{metaName}' or @name='{metaName}']");
        var value = node?.GetAttributeValue("content", null);
        return string.IsNullOrWhiteSpace(value) ? string.Empty : System.Net.WebUtility.HtmlDecode(value).Trim();
    }

    private static string SelectNodeText(HtmlAgilityPack.HtmlDocument doc, string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            return string.Empty;
        }

        var node = doc.DocumentNode.SelectSingleNode(xpath);
        return node is null ? string.Empty : System.Net.WebUtility.HtmlDecode(node.InnerText).Trim();
    }

    private static List<string> SelectNodeTexts(HtmlAgilityPack.HtmlDocument doc, string xpath)
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
            .Select(n => System.Net.WebUtility.HtmlDecode(n.InnerText).Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static string SelectAttributeValue(HtmlAgilityPack.HtmlDocument doc, string xpath, string attribute)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            return string.Empty;
        }

        var node = doc.DocumentNode.SelectSingleNode(xpath);
        var value = node?.GetAttributeValue(attribute, null);
        return string.IsNullOrWhiteSpace(value) ? string.Empty : System.Net.WebUtility.HtmlDecode(value).Trim();
    }

    /// <summary>
    /// Reports whether a result URL mentions the requested code, in either
    /// the "abp-123" or glued "abp123" spelling.
    /// </summary>
    private static bool UrlMentionsCode(string url, string normalized)
    {
        var glued = normalized.Replace("-", string.Empty, StringComparison.Ordinal);
        return url.Contains(normalized, StringComparison.OrdinalIgnoreCase)
            || url.Contains(glued, StringComparison.OrdinalIgnoreCase);
    }
}
