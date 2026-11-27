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
            Maker = TextAfterLabel(doc, MakerLabels),
            Director = TextAfterLabel(doc, DirectorLabels),
            Label = TextAfterLabel(doc, LabelLabels),
        };

        // "Release Date:" is rendered as plain text in the meta block, and the
        // label is translated with the page for the same reason as the cast
        // labels above.
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

        // Gender-separated cast. MissAV translates the label with the page
        // (zh/tw pages say 女優 / 男優), and one title can credit several
        // performers, so every link after the label counts until the next
        // field's label ends the run.
        video.Actresses = TextsAfterLabels(doc, ActressLabels);
        video.MaleActors = TextsAfterLabels(doc, ActorLabels);

        // Cast portraits (img title=name) feed the person image provider.
        CollectPersonImages(doc, video);

        video.Genres = TextsAfterLabels(doc, GenreLabels);

        video.CoverUrl = ToAbsoluteImageUrl(SelectAttribute(doc, "//img[contains(@src,'/okcdn') or contains(@class,'object-cover')]", "src"));

        var idMatch = AvIdRegex().Match(html);
        if (idMatch.Success)
        {
            video.VideoId = idMatch.Groups[1].Value;
        }

        return video;
    }

    /// <summary>Cast labels MissAV uses for women, English and CJK pages.</summary>
    private static readonly string[] ActressLabels = ["Actress:", "女優:", "女優："];

    /// <summary>Cast labels MissAV uses for men, English and CJK pages.</summary>
    private static readonly string[] ActorLabels = ["Actor:", "男優:", "男優："];

    /// <summary>
    /// Maker labels. MissAV translates the detail-page labels with the page
    /// language, so the English label alone leaves maker/studio empty on a
    /// Chinese or Japanese page even though the page carries it.
    /// </summary>
    private static readonly string[] MakerLabels =
        ["Maker:", "メーカー:", "メーカー：", "廠商:", "片商:", "发行商:", "發行商:", "發行:", "发行:"];

    /// <summary>Director labels, English and CJK.</summary>
    private static readonly string[] DirectorLabels = ["Director:", "監督:", "監督：", "导演:", "導演:"];

    /// <summary>Label/publisher labels, English and CJK.</summary>
    private static readonly string[] LabelLabels = ["Label:", "レーベル:", "レーベル：", "厂牌:", "廠牌:", "品牌:"];

    /// <summary>Genre labels, English and CJK.</summary>
    private static readonly string[] GenreLabels =
        ["Genre:", "ジャンル:", "ジャンル：", "類別:", "类别:", "類型:", "类型:", "題材:", "题材:"];

    /// <summary>
    /// Reads the first link that follows the given label — the shape every
    /// single-value field (maker, director, label) uses.
    /// </summary>
    /// <param name="doc">Parsed detail page.</param>
    /// <param name="labels">Accepted labels for the field, colon included.</param>
    /// <returns>The link text, or an empty string when the label is absent.</returns>
    private static string TextAfterLabel(HtmlDocument doc, string[] labels)
    {
        var values = TextsAfterLabels(doc, labels);
        return values.Count > 0 ? values[0] : string.Empty;
    }

    /// <summary>
    /// Collects the links that belong to the given label: each contiguous
    /// link after it is one value (several performers, several genres), and
    /// the next element (the following field's label) ends the run.
    /// </summary>
    /// <param name="doc">Parsed detail page.</param>
    /// <param name="labels">Accepted labels for the field, colon included.</param>
    /// <returns>Link texts in page order, without blanks or duplicates.</returns>
    private static List<string> TextsAfterLabels(HtmlDocument doc, string[] labels)
    {
        var values = new List<string>();
        foreach (var label in doc.DocumentNode.Descendants("span"))
        {
            if (!IsLabel(label, labels))
            {
                continue;
            }

            for (var node = label.NextSibling; node is not null; node = node.NextSibling)
            {
                if (node.NodeType != HtmlNodeType.Element)
                {
                    continue;
                }

                if (!node.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var value = node.InnerText.Trim();
                if (value.Length > 0 && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    values.Add(value);
                }
            }

            break;
        }

        return values;
    }

    /// <summary>Reports whether a span holds one of the given labels.</summary>
    /// <param name="span">Candidate label element.</param>
    /// <param name="labels">Accepted labels for the field, colon included.</param>
    /// <returns><c>true</c> when the span's own text is one of the labels.</returns>
    private static bool IsLabel(HtmlNode span, string[] labels)
    {
        var text = span.InnerText.Trim();
        if (text.Length == 0 || span.ChildNodes.Any(n => n.NodeType == HtmlNodeType.Element))
        {
            // A label span holds text only; a span wrapping links is a value.
            return false;
        }

        return labels.Any(l => text.Equals(l, StringComparison.OrdinalIgnoreCase));
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
    [GeneratedRegex(@"(?:Release Date|発売日|配信開始日|發售日期|发售日期|發行日期|发行日期)[^<]*<[^>]*>[^0-9]*(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex RegexDate();
#else
    private static readonly Regex RegexDateInstance = new(
        @"(?:Release Date|発売日|配信開始日|發售日期|发售日期|發行日期|发行日期)[^<]*<[^>]*>[^0-9]*(\d{4}-\d{2}-\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex RegexDate() => RegexDateInstance;
#endif
}
