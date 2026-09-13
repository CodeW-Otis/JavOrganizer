using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Scrapes javlibrary.com for a given JAV product code and turns the video
/// page into a <see cref="JavVideo"/> record.
/// </summary>
/// <remarks>
/// JavLibrary lists all cast members in one block without genders, so actors
/// scraped here are treated as actresses unless a gender-aware site
/// (JavDB) is also enabled. Falls back to FlareSolverr automatically when
/// Cloudflare blocks direct requests.
/// </remarks>
public sealed partial class JavLibraryScraper : SiteScraper
{
#if NET7_0_OR_GREATER
    [GeneratedRegex(@"\?v=([a-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex VideoIdRegex();
#else
    private static readonly Regex VideoIdRegexInstance = new(@"\?v=([a-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex VideoIdRegex() => VideoIdRegexInstance;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="JavLibraryScraper"/> class.
    /// </summary>
    /// <param name="logger">Logger for scraping diagnostics.</param>
    /// <param name="lang">JavLibrary language segment, e.g. "en".</param>
    public JavLibraryScraper(ILogger logger, string lang = "en")
        : base(logger, $"https://www.javlibrary.com/{lang}/")
    {
    }

    protected override string SiteName => "JavLibrary";

    /// <summary>
    /// Finds a video on JavLibrary by its product code.
    /// </summary>
    /// <param name="code">Product code, e.g. "ABP-123" (padded or not).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matched video, or <c>null</c> when nothing matches.</returns>
    public override async Task<JavVideo?> FindByCodeAsync(string code, CancellationToken ct)
    {
        BeginScrapeRound();
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = JavCodeParser.Normalize(code);
        if (normalized.Length == 0)
        {
            Logger.LogDebug("FindByCodeAsync: '{Code}' is not a parseable code", code);
            return null;
        }

        // Search the canonical (zero-free) form: the site index keys on
        // "ABP-123", so a zero-padded variant such as "ABP-00123" finds nothing.
        var keyword = JavCodeParser.ToSearchKeyword(code);
        var path = $"vl_searchbyid.php?keyword={Uri.EscapeDataString(keyword)}";
        var html = await GetHtmlAsync(path, ct).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Direct hit: the search redirected to the video page itself.
        if (html.Contains("id=\"video_title\"", StringComparison.Ordinal))
        {
            var direct = ParseVideoPage(doc, html);
            if (direct is not null && JavCodeParser.Normalize(direct.Code) == normalized)
            {
                return direct;
            }

            Logger.LogDebug("Unique page for '{Code}' had a mismatching code '{Actual}'", code, direct?.Code);
        }

        // Otherwise walk the search-result thumbnails.
        var candidates = doc.DocumentNode.SelectNodes("//div[contains(@class,'videothumblist')]//a[@href]");
        if (candidates is null || candidates.Count == 0)
        {
            Logger.LogDebug("No search results for '{Code}'", code);
            return null;
        }

        var inspected = 0;
        foreach (var link in candidates)
        {
            if (inspected >= 5 || ct.IsCancellationRequested)
            {
                break;
            }

            var href = link.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            // Search tiles link two ways depending on page variant:
            // "./?v=javXXXX" and "./javXXXX.html". Both must be followed.
            var isVideoLink = href.Contains("?v=", StringComparison.Ordinal)
                || (href.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(href.TrimStart('.', '/')).StartsWith("jav", StringComparison.OrdinalIgnoreCase));
            if (!isVideoLink)
            {
                continue;
            }

            // Be polite between page fetches so a large scan does not
            // rate-limit the site (or get the server's IP banned).
            await ThrottleAsync(ct).ConfigureAwait(false);

            var pageHtml = await GetHtmlAsync(href, ct).ConfigureAwait(false);
            if (pageHtml is null)
            {
                continue;
            }

            inspected++;

            var pageDoc = new HtmlDocument();
            pageDoc.LoadHtml(pageHtml);
            var video = ParseVideoPage(pageDoc, pageHtml);
            if (video is not null && JavCodeParser.Normalize(video.Code) == normalized)
            {
                return video;
            }
        }

        Logger.LogDebug("No candidate page matched code '{Code}'", code);
        return null;
    }

    /// <summary>
    /// Extracts video metadata from a JavLibrary video page.
    /// </summary>
    /// <param name="doc">Parsed HTML document of the page.</param>
    /// <param name="html">Raw HTML of the page (used for the video-id regex).</param>
    /// <returns>The parsed video, or <c>null</c> when the page is not a video page.</returns>
    internal JavVideo? ParseVideoPage(HtmlDocument doc, string html)
    {
        var code = SelectText(doc, "//div[@id='video_id']//td[@class='text']");
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var video = new JavVideo
        {
            Code = code,
            // JavLibrary nests the title link inside an <h3 class="title">;
            // try the direct child first, then any descendant anchor.
            Title = FirstNonEmpty(
                SelectText(doc, "//div[@id='video_title']/a"),
                SelectText(doc, "//div[@id='video_title']//a")),
            ReleaseDate = ParseDate(SelectText(doc, "//div[@id='video_date']//td[@class='text']")),
            RuntimeMinutes = ParseRuntime(SelectText(doc, "//div[@id='video_length']//span")),
            Maker = SelectText(doc, "//div[@id='video_maker']//a"),
            Director = SelectText(doc, "//div[@id='video_director']//a"),
            Label = SelectText(doc, "//div[@id='video_label']//a"),
            // Current JavLibrary markup puts the cover in <img id="video_jacket_img">;
            // older builds used a video_cover div. Try both.
            CoverUrl = FirstNonEmptyNullable(
                ToAbsoluteImageUrl(SelectAttribute(doc, "//img[@id='video_jacket_img']", "src")),
                ToAbsoluteImageUrl(SelectAttribute(doc, "//div[@id='video_cover']//img", "src"))),
        };

        video.Genres = SelectTexts(doc, "//div[@id='video_genres']//a");

        // JavLibrary does not tag cast genders; every member is listed the
        // same way. They land in Actresses and a gender-aware site can
        // override them with separated lists.
        video.Actresses = SelectTexts(doc, "//div[@id='video_cast']//a");

        // Scene screenshots offered as backdrops; the anchor hrefs point to
        // the full-resolution versions while the img srcs are thumbnails.
        video.PreviewUrls = SelectAttributes(doc, "//div[contains(@class,'previewthumbs')]//a", "href")
            .Select(ToAbsoluteImageUrl)
            .Where(u => u is not null)
            .Select(u => u!)
            .ToList();

        var idMatch = VideoIdRegex().Match(html);
        if (idMatch.Success)
        {
            video.VideoId = idMatch.Groups[1].Value;
        }

        return video;
    }
}
