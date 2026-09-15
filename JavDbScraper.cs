using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Scrapes javdb.com for a given JAV product code. JavDB is the source used
/// for gender-separated cast credits: female members carry the
/// <c>actor-female</c> CSS class and male members are plain links in the
/// same Actor(s) block.
/// </summary>
/// <remarks>
/// JavDB serves localized pages through the <c>locale</c> query parameter;
/// the configured plugin language is applied per request. Falls back to
/// FlareSolverr automatically when Cloudflare blocks direct requests.
/// </remarks>
public sealed partial class JavDbScraper : SiteScraper
{
#if NET7_0_OR_GREATER
    [GeneratedRegex(@"/v/([A-Za-z0-9]+)")]
    private static partial Regex VideoIdRegex();
#else
    private static readonly Regex VideoIdRegexInstance = new(@"/v/([A-Za-z0-9]+)", RegexOptions.Compiled);

    private static Regex VideoIdRegex() => VideoIdRegexInstance;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="JavDbScraper"/> class.
    /// </summary>
    /// <param name="logger">Logger for scraping diagnostics.</param>
    public JavDbScraper(ILogger logger)
        : base(logger, "https://javdb.com/")
    {
    }

    protected override string SiteName => "JavDB";

    private static string Locale => Plugin.EffectiveConfiguration.Language switch
    {
        "ja" => "ja",
        "zh" => "zh",
        "tw" => "zh",
        _ => "en"
    };

    /// <summary>
    /// Finds a video on JavDB by its product code.
    /// </summary>
    /// <param name="code">Product code, e.g. "MIAB-492" (padded or not).</param>
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
            return null;
        }

        var keyword = JavCodeParser.ToSearchKeyword(code);
        var searchPath = $"/search?q={Uri.EscapeDataString(keyword)}&f=all";
        if (!string.IsNullOrEmpty(Locale))
        {
            searchPath += $"&locale={Locale}";
        }

        var html = await GetHtmlAsync(searchPath, ct).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Walk the result tiles and open the first whose title starts with
        // the wanted code; each tile carries "CODE-123 Title…" text.
        var tiles = doc.DocumentNode.SelectNodes("//a[contains(@class,'box')][@href]");
        if (tiles is not null)
        {
            foreach (var tile in tiles)
            {
                var href = tile.GetAttributeValue("href", string.Empty);
                var title = tile.GetAttributeValue("title", string.Empty);
                if (string.IsNullOrWhiteSpace(href) || !href.Contains("/v/", StringComparison.Ordinal))
                {
                    continue;
                }

                // The tile title starts with the video title, but the code
                // lives in the strong element inside the tile.
                var codeText = tile.SelectSingleNode(".//div[contains(@class,'video-title')]/strong")?.InnerText?.Trim();
                if (!string.Equals(codeText, keyword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await ThrottleAsync(ct).ConfigureAwait(false);
                var pageHtml = await GetHtmlAsync(href, ct).ConfigureAwait(false);
                if (pageHtml is null)
                {
                    return null;
                }

                var pageDoc = new HtmlDocument();
                pageDoc.LoadHtml(pageHtml);
                return ParseVideoPage(pageDoc, pageHtml);
            }
        }

        Logger.LogDebug("JavDB had no result tile for code '{Code}'", code);
        return null;
    }

    /// <summary>
    /// Extracts video metadata from a JavDB video page, including the
    /// gender-separated actor list.
    /// </summary>
    /// <param name="doc">Parsed HTML document of the page.</param>
    /// <param name="html">Raw HTML of the page (used for the video-id regex).</param>
    /// <returns>The parsed video, or <c>null</c> when the page is not a video page.</returns>
    internal JavVideo? ParseVideoPage(HtmlDocument doc, string html)
    {
        // The ID block renders as "MIAB-492" split across an anchor and text
        // ("<a>MIAB</a>-492"); concatenate and normalize separators.
        var idText = SelectText(doc, "//strong[text()='ID:']/following-sibling::span[contains(@class,'value')]");
        var code = idText.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var video = new JavVideo
        {
            Code = code,
            Title = FirstNonEmpty(
                SelectText(doc, "//strong[contains(@class,'current-title')]"),
                SelectText(doc, "//h2[contains(@class,'title')]")),
            ReleaseDate = ParseDate(SelectText(doc, "//strong[text()='Released Date:']/following-sibling::span[contains(@class,'value')]")),
            RuntimeMinutes = ParseRuntime(SelectText(doc, "//strong[text()='Duration:']/following-sibling::span[contains(@class,'value')]")),
            Director = SelectText(doc, "//strong[text()='Director:']/following-sibling::span//a"),
            Maker = SelectText(doc, "//strong[text()='Maker:']/following-sibling::span//a"),
            Label = SelectText(doc, "//strong[text()='Series:']/following-sibling::span//a"),
            CoverUrl = ToAbsoluteImageUrl(SelectAttribute(doc, "//img[contains(@class,'video-cover')]", "src")),
        };

        // Tags act as genres on JavDB.
        video.Genres = SelectTexts(doc, "//strong[text()='Tags:']/following-sibling::span//a");

        // Gender-separated cast: female members carry the actor-female class,
        // plain anchors in the same block are male actors.
        var actorBlock = doc.DocumentNode.SelectSingleNode("//strong[text()='Actor(s):']/following-sibling::span[contains(@class,'value')][1]");
        if (actorBlock is not null)
        {
            video.Actresses = actorBlock
                .SelectNodes(".//a[contains(@class,'actor-female')]")
                ?.Select(a => System.Net.WebUtility.HtmlDecode(a.InnerText).Trim())
                .Where(t => t.Length > 0)
                .ToList() ?? [];

            video.MaleActors = actorBlock
                .SelectNodes(".//a[not(contains(@class,'actor-female'))]")
                ?.Select(a => System.Net.WebUtility.HtmlDecode(a.InnerText).Trim())
                .Where(t => t.Length > 0)
                .ToList() ?? [];
        }

        // Cast portraits (img title=name) feed the person image provider.
        CollectPersonImages(doc, video);

        // Sample/preview images offered as backdrops; JavDB puts them behind
        // "sample" thumbnails that link to full-size images.
        video.PreviewUrls = SelectAttributes(doc, "//a[@class='tile-menu-item' or contains(@href,'sample')][@href]", "href")
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
