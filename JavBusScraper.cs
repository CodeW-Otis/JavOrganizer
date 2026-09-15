using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Scrapes javbus.com for a given JAV product code. JavBus serves pages
/// directly once the age-verification cookie (<c>dv=1</c>) is set, so it
/// normally needs no Cloudflare solving at all — a fast, reliable source.
/// </summary>
/// <remarks>
/// JavBus lists female idols through star links; male actors are usually
/// not credited, so members land in the unseparated cast list and a
/// gender-aware site (JavDB, MissAV) refines them during the merge.
/// </remarks>
public sealed partial class JavBusScraper(ILogger logger, string lang = "en") : SiteScraper(
        logger,
        lang.Equals("zh", StringComparison.OrdinalIgnoreCase) || lang.Equals("tw", StringComparison.OrdinalIgnoreCase)
            ? "https://www.javbus.com/"
            : $"https://www.javbus.com/{lang}/",
        "dv=1")
{
    protected override string SiteName => "JavBus";

    /// <summary>
    /// Finds a video on JavBus by its product code. JavBus exposes a
    /// canonical URL per code, so no search-result walking is needed.
    /// Codes whose canonical form dropped a leading zero (SDMF-010 →
    /// SDMF-10) are additionally retried in their padded spelling, because
    /// JavBus indexes some labels only under the padded form.
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

        // Canonical detail page: https://www.javbus.com/<lang>/<CODE>
        var video = await TryCodeAsync(keyword, ct).ConfigureAwait(false);
        if (video is not null)
        {
            return video;
        }

        // Zero-padded retry: some labels are only indexed as "SDMF-010".
        var padded = ToPaddedKeyword(code);
        if (padded is not null && !padded.Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogDebug("JavBus: '{Keyword}' not found; retrying padded form '{Padded}'", keyword, padded);
            video = await TryCodeAsync(padded, ct).ConfigureAwait(false);
            if (video is not null)
            {
                return video;
            }
        }

        return null;
    }

    /// <summary>
    /// Fetches and parses a JavBus detail page for one spelling of the code,
    /// treating age/bot walls and ID mismatches as misses.
    /// </summary>
    /// <param name="keyword">Code spelling to fetch ("SDMF-10" or "SDMF-010").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed video, or <c>null</c> when the page is not a match.</returns>
    private async Task<JavVideo?> TryCodeAsync(string keyword, CancellationToken ct)
    {
        var html = await GetHtmlAsync($"{keyword}", ct).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Age gate or bot wall comes back as HTML without the info panel.
        if (html.Contains("Age Verification", StringComparison.OrdinalIgnoreCase) || html.Contains("driver-verify", StringComparison.Ordinal))
        {
            Logger.LogDebug("JavBus: age/driver gate for '{Code}'", keyword);
            return null;
        }

        var video = ParseVideoPage(doc, html);
        if (video is not null)
        {
            // The page's own ID must match what we asked for — a soft-404
            // page with generic content must not count as a hit.
            var pageCode = JavCodeParser.Normalize(video.Code);
            var wanted = JavCodeParser.Normalize(keyword);
            if (pageCode.Length > 0 && wanted.Length > 0
                && !pageCode.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return video;
    }

    /// <summary>
    /// Builds the zero-padded spelling of a code when canonicalization
    /// stripped a leading zero (user code "SDMF-010" → canonical keyword
    /// "SDMF-10" → padded retry "SDMF-010"). Returns <c>null</c> when the
    /// code has no such variant.
    /// </summary>
    /// <param name="code">Code in any spelling.</param>
    /// <returns>The padded keyword, or <c>null</c> when there is none.</returns>
    private static string? ToPaddedKeyword(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        var match = Regex.Match(code, @"^([A-Za-z]+[0-9]*)-0+(\d+)$");
        return match.Success
            ? match.Groups[1].Value.ToUpperInvariant() + "-0" + match.Groups[2].Value
            : null;
    }

    /// <summary>
    /// Extracts video metadata from a JavBus video page.
    /// </summary>
    /// <param name="doc">Parsed HTML document of the page.</param>
    /// <param name="html">Raw HTML of the page.</param>
    /// <returns>The parsed video, or <c>null</c> when the page is not a video page.</returns>
    internal JavVideo? ParseVideoPage(HtmlDocument doc, string html)
    {
        var idText = SelectText(doc, "//span[contains(@class,'header') and text()='ID:']/following-sibling::span[1]");
        var code = idText.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        // The info panel renders "<span class='header'>Release Date:</span> 2020-06-05" —
        // the value is a bare text node inside the same <p> as the label span
        // (older builds wrapped it in a span; both shapes are covered by
        // reading the parent <p> text and parsing the value out of it).
        var video = new JavVideo
        {
            Code = code,
            Title = FirstNonEmpty(
                SelectText(doc, "//h3"),
                SelectText(doc, "//div[contains(@class,'col-md-9')]/h3"),
                SelectText(doc, "//title")),
            ReleaseDate = ParseDate(FirstNonEmpty(
                SelectText(doc, "//p[span[contains(@class,'header') and text()='Release Date:']]"),
                SelectText(doc, "//span[contains(@class,'header') and text()='Release Date:']/following-sibling::span[1]"))),
            RuntimeMinutes = ParseRuntime(FirstNonEmpty(
                SelectText(doc, "//p[span[contains(@class,'header') and text()='Length:']]"),
                SelectText(doc, "//span[contains(@class,'header') and text()='Length:']/following-sibling::span[1]"))),
            Director = FirstNonEmpty(
                SelectText(doc, "//p[span[contains(@class,'header') and text()='Director:']]//a"),
                SelectText(doc, "//span[contains(@class,'header') and text()='Director:']/following-sibling::span[1]//a")),
            Maker = FirstNonEmpty(
                SelectText(doc, "//p[span[contains(@class,'header') and text()='Studio:']]//a"),
                SelectText(doc, "//span[contains(@class,'header') and text()='Studio:']/following-sibling::span[1]//a")),
            Label = FirstNonEmpty(
                SelectText(doc, "//p[span[contains(@class,'header') and text()='Label:']]//a"),
                SelectText(doc, "//span[contains(@class,'header') and text()='Label:']/following-sibling::span[1]//a")),
        };

        // Genre links live inside span.genre in the info panel; also accept
        // the plain absolute links older markup used.
        video.Genres = SelectTexts(doc, "//span[contains(@class,'genre')]//a[contains(@href,'/genre/')]")
            .Concat(SelectTexts(doc, "//a[starts-with(@href,'https://www.javbus.com/genre')]"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // JavBus star links are female idols; there is no male section, so
        // they populate the unseparated cast (merge refines with genders).
        video.Actresses = SelectTexts(doc, "//a[contains(@href,'/star/')]")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Star portraits (img title=name) feed the person image provider.
        CollectPersonImages(doc, video);

        video.CoverUrl = ToAbsoluteImageUrl(FirstNonEmptyNullable(
            SelectAttribute(doc, "//a[contains(@class,'bigImage')]//img", "src"),
            SelectAttribute(doc, "//a[contains(@class,'bigImage')]", "href")));

        // Sample thumbnails ("sample-box" anchors) link to full-size previews
        // on the DMM CDN; older builds used class "sample".
        video.PreviewUrls = SelectAttributes(doc, "//a[contains(@class,'sample')]", "href")
            .Select(ToAbsoluteImageUrl)
            .Where(u => u is not null)
            .Select(u => u!)
            .ToList();

        // JavBus pages carry no per-video id: the only [?&]v= matches on the
        // page are CSS cache-buster query strings ("css-slider.css?v=9.3"),
        // which produced bogus ids like "9" that collided across items and
        // misrouted the image provider. The product code itself is the stable
        // identifier for this site's canonical URL pages.
        video.VideoId = JavCodeParser.ToSearchKeyword(code);
        return video;
    }
}
