using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Scrapes missav.ws (and its mirror domains) for a given JAV product
/// code. MissAV labels cast with explicit <c>Actress:</c> and <c>Actor:</c>
/// fields, making it a reliable source for gender-separated credits; its
/// pages sit behind a Cloudflare challenge that the shared
/// clearance-adopt mechanism handles automatically.
/// </summary>
public sealed partial class MissAvScraper(ILogger logger, string lang = "en", string baseUrl = "https://missav.ws/") : SiteScraper(
        logger,
        $"{baseUrl.TrimEnd('/')}/{lang}/")
{
    /// <summary>
    /// Gets the domain this scraper instance is bound to (main site or
    /// mirror), used as the rate-limiter key so the two domains are paced
    /// independently.
    /// </summary>
    public string Domain { get; } = new Uri(baseUrl).Host;
#if NET7_0_OR_GREATER
    [GeneratedRegex(@"missav\.[a-z]+/([a-z0-9]+)/[a-z]{2}/", RegexOptions.IgnoreCase)]
    private static partial Regex AvIdRegex();
#else
    private static readonly Regex AvIdRegexInstance = new(@"missav\.[a-z]+/([a-z0-9]+)/[a-z]{2}/", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex AvIdRegex() => AvIdRegexInstance;
#endif

    protected override string SiteName => Domain;

    /// <summary>
    /// Finds a video on MissAV by its product code. MissAV exposes a
    /// canonical URL per code, so no search-result walking is needed.
    /// </summary>
    /// <param name="code">Product code, e.g. "MIAB-492".</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matched video, or <c>null</c> when nothing matches.</returns>
    public override async Task<JavVideo?> FindByCodeAsync(string code, CancellationToken ct)
    {
        BeginScrapeRound();
        var keyword = JavCodeParser.ToSearchKeyword(code);
        if (keyword.Length == 0)
        {
            return null;
        }

        var html = await GetHtmlAsync($"{keyword}", ct).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return ParseVideoPage(doc, html);
    }

    /// <summary>
    /// Extracts video metadata from a MissAV page, including the labeled
    /// Actress/Actor fields that give gender-separated cast credits.
    /// </summary>
    /// <param name="doc">Parsed HTML document of the page.</param>
    /// <param name="html">Raw HTML of the page.</param>
    /// <returns>The parsed video, or <c>null</c> when the page is not a video page.</returns>
    internal JavVideo? ParseVideoPage(HtmlDocument doc, string html)
    {
        // MissAV pages show the code in a font-medium span near the top.
        var code = SelectText(doc, "//span[contains(@class,'font-medium')][1]");
        code = ExtractCode(code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var video = new JavVideo
        {
            Code = code,
            Title = SelectText(doc, "//h1[contains(@class,'title')]|//div[contains(@class,'title')]//span[contains(@class,'font-medium')][1]"),
            Maker = SelectText(doc, "//span[text()='Maker:']/following-sibling::a[1]"),
            Director = SelectText(doc, "//span[text()='Director:']/following-sibling::a[1]"),
            Label = SelectText(doc, "//span[text()='Label:']/following-sibling::a[1]"),
        };

        // "Release Date:" is rendered as plain text in the meta block.
        var dateText = SelectText(doc, "//span[contains(@id,'release_date')]|//time");
        video.ReleaseDate = ParseDate(dateText);
        if (video.ReleaseDate is null)
        {
            var m = RegexDate().Match(html);
            if (m.Success)
            {
                video.ReleaseDate = ParseDate(m.Groups[1].Value);
            }
        }

        // Gender-separated cast: explicit "Actress:" and "Actor:" labels.
        video.Actresses = SelectTexts(doc, "//span[text()='Actress:']/following-sibling::a[1]");
        video.MaleActors = SelectTexts(doc, "//span[text()='Actor:']/following-sibling::a[1]");

        // Cast portraits (img title=name) feed the person image provider.
        CollectPersonImages(doc, video);

        video.Genres = SelectTexts(doc, "//span[text()='Genre:']/following-sibling::a");

        video.CoverUrl = ToAbsoluteImageUrl(SelectAttribute(doc, "//img[contains(@src,'/okcdn') or contains(@class,'object-cover')]", "src"));

        var idMatch = AvIdRegex().Match(html);
        if (idMatch.Success)
        {
            video.VideoId = idMatch.Groups[1].Value;
        }

        return video;
    }

    private static string ExtractCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return JavCodeParser.ExtractCode(text) ?? string.Empty;
    }

#if NET7_0_OR_GREATER
    [GeneratedRegex(@"Release Date[^<]*<[^>]*>\s*([0-9]{4}-[0-9]{2}-[0-9]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex RegexDate();
#else
    private static readonly Regex RegexDateInstance = new(@"Release Date[^<]*<[^>]*>\s*([0-9]{4}-[0-9]{2}-[0-9]{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex RegexDate() => RegexDateInstance;
#endif
}
