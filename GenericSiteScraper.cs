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
        // The canonical keyword is zero-free ("ADN-29"), but some sites only
        // index the zero-padded spelling of the same code ("ADN-029") — a
        // search for the canonical form then returns their whole catalogue
        // and the code is never found. Each spelling is tried until one
        // yields a page that verifies.
        foreach (var searchKeyword in SearchKeywords(keyword))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var video = await SearchAndFollowAsync(searchKeyword, normalized, code, ct).ConfigureAwait(false);
            if (video is not null)
            {
                return video;
            }
        }

        Logger.LogDebug("{Site}: no result page matched code '{Code}'", SiteName, code);
        return null;
    }

    /// <summary>
    /// Enumerates the spellings to send to a site's search box: the canonical
    /// zero-free keyword first (the form most indexes use), then the
    /// zero-padded variants when the code's digits are short enough to have
    /// one.
    /// </summary>
    /// <param name="keyword">Canonical uppercase keyword, e.g. "ADN-29".</param>
    /// <returns>Distinct keywords to try, in order.</returns>
    private static IEnumerable<string> SearchKeywords(string keyword)
    {
        yield return keyword;

        var dash = keyword.LastIndexOf('-');
        if (dash <= 0 || dash == keyword.Length - 1)
        {
            yield break;
        }

        var label = keyword[..dash];
        var digits = keyword[(dash + 1)..];
        if (!digits.All(char.IsDigit))
        {
            yield break;
        }

        // Pad to the widths these catalogs actually publish (3 and 5).
        foreach (var width in new[] { 3, 5 })
        {
            if (digits.Length < width)
            {
                yield return $"{label}-{digits.PadLeft(width, '0')}";
            }
        }
    }

    /// <summary>
    /// Runs one search query and follows the result links that mention the
    /// code, returning the first page that parses and verifies.
    /// </summary>
    /// <param name="searchKeyword">Keyword to send to the site.</param>
    /// <param name="normalized">Canonical code being searched for.</param>
    /// <param name="requestedCode">Original code spelling, for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verified record, or <c>null</c>.</returns>
    private async Task<JavVideo?> SearchAndFollowAsync(
        string searchKeyword,
        string normalized,
        string requestedCode,
        CancellationToken ct)
    {
        var searchHtml = await GetHtmlAsync(_site.SearchPath(searchKeyword), ct).ConfigureAwait(false);
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

            var video = ParseAndVerify(html, normalized, requestedCode);
            if (video is not null)
            {
                return video;
            }
        }

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
        return CodesAgree(pageCode, normalized) ? video : null;
    }

    /// <summary>
    /// Reports whether a page's own code denotes the requested one. Sites
    /// append edition and source suffixes to the code they display
    /// ("[ADN-029-MR]"), which the parser reads as part of the number — a
    /// strict comparison discarded those pages even though they are the
    /// right video. The match therefore also accepts the page code whose
    /// leading label-and-number equals the requested code.
    /// </summary>
    /// <param name="pageCode">Normalized code read from the page.</param>
    /// <param name="requested">Normalized code being searched for.</param>
    /// <returns><c>true</c> when the page is the requested video.</returns>
    internal static bool CodesAgree(string pageCode, string requested)
    {
        if (pageCode.Length == 0 || requested.Length == 0)
        {
            // Nothing to compare against; the caller's title check already
            // rejected error pages.
            return true;
        }

        if (string.Equals(pageCode, requested, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Same label and leading digits, extra suffix on the page side:
        // "adn-029-mr" is the "-MR" edition of "adn-029".
        return pageCode.StartsWith(requested + "-", StringComparison.OrdinalIgnoreCase)
            || requested.StartsWith(pageCode + "-", StringComparison.OrdinalIgnoreCase);
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

        // A bare site name is never a video title. Several sites set
        // og:title to their own brand ("OneJAV"), and merging that into the
        // library would replace a real scraped title with the site's name —
        // so such a value is treated as "this site has no title" and the
        // remaining sites supply one.
        if (IsSiteNameTitle(title))
        {
            Logger.LogDebug("{Site}: title '{Title}' is the site's own name; ignoring it", SiteName, title);
            title = string.Empty;
        }

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
    /// Reports whether a scraped title is really the site's own brand name
    /// rather than a video title. Sites commonly set <c>og:title</c> to their
    /// brand, and a record whose "title" is the site name would overwrite a
    /// genuine title during the merge.
    /// </summary>
    /// <param name="title">Candidate title.</param>
    /// <returns><c>true</c> when the value is just the site's name.</returns>
    private bool IsSiteNameTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var value = title.Trim();

        // Compare against the display name, the key, the base host and the
        // host's first label, all with punctuation and spacing removed so
        // "Jav GG", "javgg", "JavGG.net" and "JavGG" all collapse together.
        var candidates = new[]
        {
            _site.DisplayName,
            _site.Key,
            _site.BaseUrl
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var host = candidate;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                host = uri.Host;
                var dot = host.IndexOf('.');
                if (dot > 0)
                {
                    host = host[..dot];
                }
            }

            var normalizedTitle = NormalizeForComparison(value);
            if (normalizedTitle.Length == 0)
            {
                continue;
            }

            if (normalizedTitle == NormalizeForComparison(candidate)
                || normalizedTitle == NormalizeForComparison(host))
            {
                return true;
            }
        }

        return false;

        static string NormalizeForComparison(string value)
            => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
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
    /// Reports whether a result URL mentions the requested code. Sites spell
    /// codes with and without the dash and with and without zero padding
    /// ("adn-029", "adn029", "adn-29"), so every combination is accepted.
    /// </summary>
    /// <param name="url">Candidate result URL.</param>
    /// <param name="normalized">Canonical (zero-free) code being searched for.</param>
    /// <returns><c>true</c> when the URL names the code.</returns>
    internal static bool UrlMentionsCode(string url, string normalized)
    {
        if (normalized.Length == 0)
        {
            return false;
        }

        var dash = normalized.LastIndexOf('-');
        var label = dash > 0 ? normalized[..dash] : normalized;
        var digits = dash > 0 ? normalized[(dash + 1)..] : string.Empty;

        foreach (var candidate in CodeSpellings(label, digits))
        {
            if (candidate.Length > 0 && url.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates the spellings a code can appear in inside a URL: the
    /// canonical form, the glued form, and both again with the digits
    /// zero-padded (sites and their permalinks disagree about padding —
    /// jav.guru links "adn-029" for the canonical code "adn-29").
    /// </summary>
    /// <param name="label">Code letters.</param>
    /// <param name="digits">Canonical digit group (zero-free).</param>
    /// <returns>Distinct spellings to look for.</returns>
    private static IEnumerable<string> CodeSpellings(string label, string digits)
    {
        if (digits.Length == 0)
        {
            yield return label;
            yield break;
        }

        yield return $"{label}-{digits}";
        yield return $"{label}{digits}";

        // Zero-padded to the widths these sites actually use (3 and 5 are
        // the common DMM/catalog widths).
        foreach (var width in new[] { 3, 5 })
        {
            if (digits.Length < width)
            {
                var padded = digits.PadLeft(width, '0');
                yield return $"{label}-{padded}";
                yield return $"{label}{padded}";
            }
        }
    }
}
