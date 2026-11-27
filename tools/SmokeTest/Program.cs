using System.Reflection;
using HtmlAgilityPack;
using Jellyfin.Plugin.JavOrganizer;

// Smoke test for the JavOrganizer v1.3.0 core logic. Runs against the
// built assembly without a Jellyfin server: code parsing, catalog URL
// building, generic-scraper parsing and the merge rules.
// Usage: dotnet run --project tools/SmokeTest -p:TargetFramework=net8.0

var pass = 0;
var fail = 0;
void Check(bool ok, string label)
{
    if (ok)
    {
        pass++;
        Console.WriteLine($"  ok   {label}");
    }
    else
    {
        fail++;
        Console.WriteLine($"  FAIL {label}");
    }
}

/// <summary>
/// Builds a Movie with a JavOrganizer provider id and the given metadata, so
/// the scan-predicate tests can shape an item's scrape state precisely.
/// </summary>
/// <param name="name">Display name (also the code source when needed).</param>
/// <param name="code">Product code the provider id is derived from.</param>
/// <param name="g">Genres to set.</param>
/// <param name="s">Studios to set.</param>
/// <param name="o">Overview text to set.</param>
/// <returns>The constructed item.</returns>
static MediaBrowser.Controller.Entities.Movies.Movie MakeMovie(string name, string code, string[] g, string[] s, string o)
{
    var movie = new MediaBrowser.Controller.Entities.Movies.Movie
    {
        Name = name,
        Overview = o,
        Genres = g,
        Studios = s,
        // A real scraped item always carries a release date; without one the
        // item would read as title-only rather than as the state under test.
        PremiereDate = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc)
    };
    movie.ProviderIds[JavMetadataProvider.ProviderIdKey] = code.ToLowerInvariant();
    return movie;
}

Console.WriteLine("== JavCodeParser ==");
Check(JavCodeParser.ExtractCode("SSIS-406.mp4") == "SSIS-406", "SSIS-406");
Check(JavCodeParser.ExtractCode("abp982.mp4") == "ABP-982", "abp982 glued");
Check(JavCodeParser.ExtractCode("Movie.FHD1080.ABP-123.mp4") == "ABP-123", "noise + code");
Check(JavCodeParser.ExtractCode("Movie.FHD1080.mp4") is null, "noise only rejected");
Check(JavCodeParser.ExtractCode("FC2-PPV-1234567.mp4") is null, "FC2 rejected");
Check(JavCodeParser.ExtractCode("IPX-1234_uncensored.mp4") == "IPX-1234", "underscore boundary");
Check(JavCodeParser.ExtractCode("T28-597.mp4") == "T28-597", "digit-suffixed label");
// ExtractCode keeps zero-padded display form; Normalize canonicalizes it.
Check(JavCodeParser.ExtractCode("abp-00123.mp4") == "ABP-00123", "zero-padded kept in display form");
Check(JavCodeParser.Normalize("ABP-00123") == "abp-123", "normalize");
Check(JavCodeParser.ToSearchKeyword("abp00123") == "ABP-123", "search keyword");

Console.WriteLine("== JavCodeParser edge cases ==");
// Boundary and malformed inputs must never crash or invent a code.
foreach (var (input, label) in new (string?, string)[]
{
    (null, "null"),
    ("", "empty"),
    ("   ", "whitespace"),
    ("-", "dash only"),
    ("---", "dashes"),
    ("123", "digits only"),
    ("ABC", "letters only"),
    ("ABC-", "trailing dash"),
    ("-123", "leading dash"),
    ("A-1", "single letter one digit"),
    ("ABCDEFGHIJKLMNOP-1", "very long label"),
    ("ABC-000000000000001", "zero padded to 15 digits"),
})
{
    var extracted = JavCodeParser.ExtractCode(input);
    var normalized = JavCodeParser.Normalize(input);
    Check(normalized.Length >= 0 && extracted is null or { Length: > 0 },
        $"malformed input handled: {label}");
    Check(!normalized.Contains("--", StringComparison.Ordinal),
        $"normalized form never doubles a dash: {label}");
}

// Zero-padding is stripped to the canonical form, but a genuinely
// zero-containing code keeps its interior zeros.
Check(JavCodeParser.Normalize("ABC-100") == "abc-100", "interior zeros preserved");
Check(JavCodeParser.Normalize("ABC-010") == "abc-10", "leading zero stripped");
Check(JavCodeParser.Normalize("ABC-000") == string.Empty, "all-zero digits rejected");
Check(JavCodeParser.Normalize("abc-123") == JavCodeParser.Normalize("ABC-00123"),
    "padded and plain forms normalize identically");

// Unicode / whitespace robustness: a code must still be found inside noise.
Check(JavCodeParser.ExtractCode("日本語 ABC-123 タイトル.mp4") == "ABC-123", "code found amid CJK text");
Check(JavCodeParser.ExtractCode("  ABC-123  ") == "ABC-123", "surrounding whitespace tolerated");

Console.WriteLine("== JavSiteCatalog ==");
Check(JavSiteCatalog.Sites.Count == 14, $"14 generic sites in catalog (got {JavSiteCatalog.Sites.Count})");
Check(JavSiteCatalog.ToDmmCid("ABP-123") == "abp00123", "DMM cid padding");
Check(JavSiteCatalog.ToDmmCid("T28-597") == "t2800597", "DMM cid digit label");
Check(JavSiteCatalog.ToDmmCid("MIDE-100") == "mide00100", "DMM cid 5-digit");
var fanza = JavSiteCatalog.Sites.First(s => s.Key == "fanza");
Check(fanza.CanonicalPath("ABP-123") == "digital/videoa/-/detail/=/cid=abp00123/", "FANZA canonical URL");
var wpSupjav = JavSiteCatalog.Sites.First(s => s.Key == "supjav");
Check(wpSupjav.SearchPath("ABP-123") == "?s=ABP-123", "SupJav WP search URL");
var javland = JavSiteCatalog.Sites.First(s => s.Key == "javland");
Check(javland.BaseUrl == "https://javland.com/", "JavLand base");

Console.WriteLine("== GenericSiteScraper.ParseVideoPage (WordPress template) ==");
var logger = new StubLogger();
var supjav = new GenericSiteScraper(wpSupjav, logger);
var wpHtml = """
<html>
<head>
  <meta property="og:title" content="ABP-123 Some Movie - SupJav" />
  <meta property="og:image" content="https://supjav.com/wp-content/uploads/abp123.jpg" />
</head>
<body>
  <article>
    <h1 class="entry-title">ABP-123 Some Movie</h1>
    <time datetime="2023-05-01T10:00:00+09:00">May 1, 2023</time>
    <a rel="category tag" href="https://supjav.com/category/creampie/">Creampie</a>
    <a rel="category tag" href="https://supjav.com/category/big-tits/">Big Tits</a>
  </article>
</body>
</html>
""";
var wpDoc = new HtmlDocument();
wpDoc.LoadHtml(wpHtml);
var wpVideo = supjav.ParseVideoPage(wpDoc, wpHtml, "ABP-123");
Check(wpVideo is not null, "WP page parses");
Check(wpVideo?.Title == "ABP-123 Some Movie", $"WP title cleaned (got '{wpVideo?.Title}')");
Check(wpVideo?.Code == "ABP-123", $"WP code extracted (got '{wpVideo?.Code}')");
Check(wpVideo?.ReleaseDate == new DateTime(2023, 5, 1), $"WP date (got '{wpVideo?.ReleaseDate:yyyy-MM-dd}')");
Check(wpVideo?.CoverUrl == "https://supjav.com/wp-content/uploads/abp123.jpg", "WP og:image cover");
Check(wpVideo?.Genres.Count == 2 && wpVideo.Genres[0] == "Creampie", "WP categories as genres");

Console.WriteLine("== Placeholder image URLs are not downloaded ==");
Check(!JavHttp.IsUsableImageUrl("data:image/png;base64,iVBORw0KGgo="), "data: image rejected");
Check(!JavHttp.IsUsableImageUrl("javascript:alert(1)"), "javascript: URL rejected");
Check(!JavHttp.IsUsableImageUrl(""), "empty URL rejected");
Check(!JavHttp.IsUsableImageUrl(null), "null URL rejected");
Check(JavHttp.IsUsableImageUrl("https://x/y.jpg"), "https URL accepted");
Check(JavHttp.IsUsableImageUrl("http://x/y.jpg"), "http URL accepted");

var placeholderHtml = """
<html>
<head><meta property="og:image" content="data:image/png;base64,iVBORw0KGgo=" /></head>
<body><h1 class="entry-title">ABP-123 Placeholder</h1></body>
</html>
""";
var placeholderDoc = new HtmlDocument();
placeholderDoc.LoadHtml(placeholderHtml);
var placeholderVideo = supjav.ParseVideoPage(placeholderDoc, placeholderHtml, "ABP-123");
Check(placeholderVideo?.CoverUrl is null, "data: cover rejected by the scraper");

Console.WriteLine("== Result-link extraction (regression: silent 'no match') ==");
// A result-link selector that matches nothing makes a site silently report
// "no match" for every code while still looking healthy in the site panel —
// which is exactly how OneJAV and javquick broke. These fixtures are trimmed
// copies of the real markup each site family serves.
var onejavSite = JavSiteCatalog.Sites.First(s => s.Key == "onejav");
var onejavScraper = new GenericSiteScraper(onejavSite, logger);
// OneJAV serves <div class="card"> tiles; there is no <article> anywhere.
var onejavSearchHtml = """
<html><head><title>MIAB-492 - OneJAV</title></head><body>
<div class="card mb-3">
  <div class="container"><div class="columns"><div class="column is-5">
    <div class="card-content"><h5 class="title is-4 is-spaced">
      <a href="/torrent/miab492">MIAB492</a><span>5.0 GB</span>
    </h5></div>
  </div></div></div>
</div>
<div class="card mb-3">
  <div class="container"><div class="columns"><div class="column is-5">
    <div class="card-content"><h5 class="title is-4 is-spaced">
      <a href="/torrent/ssis448">SSIS448</a>
    </h5></div>
  </div></div></div>
</div>
</body></html>
""";
var onejavDoc = new HtmlDocument();
onejavDoc.LoadHtml(onejavSearchHtml);
var onejavLinks = onejavDoc.DocumentNode
    .SelectNodes(onejavSite.Selectors.ResultLinks)?
    .Select(n => n.GetAttributeValue("href", string.Empty))
    .Where(h => h.Length > 0)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList() ?? [];
Check(onejavLinks.Count > 0,
    $"OneJAV result links found in article-less markup (got {onejavLinks.Count})");
Check(onejavLinks.Any(h => h.Contains("miab492", StringComparison.OrdinalIgnoreCase)),
    $"OneJAV finds the wanted code's tile (got [{string.Join(", ", onejavLinks)}])");

// javquick wraps <article> but its anchor is a bare child, not nested in an h2.
var javquickSite = JavSiteCatalog.Sites.First(s => s.Key == "javquick");
var javquickSearchHtml = """
<html><head><title>Search</title></head><body>
<article class="bg-gray-700 rounded-lg shadow-md overflow-hidden">
  <a href="/movie/BLDSegsrU/juy-682-my-sister-in-law" rel="bookmark" class="block">
    <div class="image-wrapper relative"><img class="movie-image" src="https://x/y.jpg" alt="t"></div>
  </a>
  <div class="p-4"><h2 title="JUY-682 My Sister In Law">JUY-682</h2></div>
</article>
</body></html>
""";
var javquickDoc = new HtmlDocument();
javquickDoc.LoadHtml(javquickSearchHtml);
var javquickLinks = javquickDoc.DocumentNode
    .SelectNodes(javquickSite.Selectors.ResultLinks)?
    .Select(n => n.GetAttributeValue("href", string.Empty))
    .Where(h => h.Length > 0)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList() ?? [];
Check(javquickLinks.Count > 0,
    $"javquick result links found with a bare anchor in <article> (got {javquickLinks.Count})");

// Every catalog site must be able to surface *some* link shape; a selector
// that cannot match any of the known families is a silent dead site.
Check(!string.IsNullOrWhiteSpace(onejavSite.Selectors.ResultLinks)
    && !string.IsNullOrWhiteSpace(javquickSite.Selectors.ResultLinks),
    "every search-mode site defines result links");
Check(onejavSite.Mode == SiteUrlMode.Search && onejavSite.Selectors.ResultLinks.Contains("/torrent/", StringComparison.Ordinal),
    "OneJAV keeps its /torrent/ link anchor");

Console.WriteLine("== Search-mode sites all expose a usable selector ==");
foreach (var s in JavSiteCatalog.Sites.Where(s => s.Mode == SiteUrlMode.Search))
{
    Check(!string.IsNullOrWhiteSpace(s.Selectors.ResultLinks),
        $"{s.Key} has result links");
}

Console.WriteLine("== GenericSiteScraper.ParseVideoPage (JavBus template / JavLand) ==");
var javlandScraper = new GenericSiteScraper(javland, logger);
var busHtml = """
<html>
<head><title>ABP-123</title></head>
<body>
  <h3>ABP-123 Some Movie</h3>
  <a class="bigImage" href="https://javland.com/pics/abp123.jpg"><img src="https://javland.com/pics/abp123.jpg" /></a>
  <span class="info">
    <span class="header">ID:</span><span>ABP-123</span>
    <span class="header">Release Date:</span><span>2023-05-01</span>
    <span class="header">Length:</span><span>120<span>min</span></span>
    <span class="header">Studio:</span><span><a href="https://javland.com/studio/x">Prestige</a></span>
  </span>
  <span class="genre"><a href="https://javland.com/genre/1">Creampie</a></span>
  <a href="https://javland.com/star/a">Yua Mikami</a>
  <a class="sample" href="https://javland.com/samples/1.jpg">1</a>
</body>
</html>
""";
var busDoc = new HtmlDocument();
busDoc.LoadHtml(busHtml);
var busVideo = javlandScraper.ParseVideoPage(busDoc, busHtml, "ABP-123");
Check(busVideo is not null, "JavBus template parses");
Check(busVideo?.Title == "ABP-123 Some Movie", "JavBus title");
Check(busVideo?.Code == "ABP-123", "JavBus ID");
Check(busVideo?.ReleaseDate == new DateTime(2023, 5, 1), "JavBus release date");
Check(busVideo?.RuntimeMinutes == 120, $"JavBus length (got {busVideo?.RuntimeMinutes})");
Check(busVideo?.Maker == "Prestige", "JavBus studio");
Check(busVideo?.Genres.Count == 1, "JavBus genres");
Check(busVideo?.Actresses.Count == 1 && busVideo.Actresses[0] == "Yua Mikami", "JavBus star");
Check(busVideo?.CoverUrl == "https://javland.com/pics/abp123.jpg", "JavBus cover");
Check(busVideo?.PreviewUrls.Count == 1, "JavBus sample preview");

Console.WriteLine("== GenericSiteScraper.ParseVideoPage (FANZA template) ==");
var fanzaScraper = new GenericSiteScraper(fanza, logger);
var fanzaHtml = """
<html>
<head><title>ABP-123</title></head>
<body>
  <h1 id="title">ABP-123 Some Movie</h1>
  <a name="package-image"><img src="https://pics.dmm.co.jp/digital/video/abp00123/abp00123ps.jpg" /></a>
  <a name="sample-image1" href="https://pics.dmm.co.jp/digital/video/abp00123/abp00123-1.jpg">1</a>
  <a name="sample-image2" href="https://pics.dmm.co.jp/digital/video/abp00123/abp00123-2.jpg">2</a>
  <table><tr><td>配信開始日: 2023/05/01</td></tr><tr><td>収録時間: 120分</td></tr></table>
  <a href="https://www.dmm.co.jp/digital/videoa/-/article=maker/abp/">Prestige</a>
  <a href="https://www.dmm.co.jp/digital/videoa/-/article=genre/1/">Creampie</a>
  <a href="https://www.dmm.co.jp/digital/videoa/-/article=/actress/abc/">Yua Mikami</a>
</body>
</html>
""";
var fanzaDoc = new HtmlDocument();
fanzaDoc.LoadHtml(fanzaHtml);
var fanzaVideo = fanzaScraper.ParseVideoPage(fanzaDoc, fanzaHtml, "ABP-123");
Check(fanzaVideo is not null, "FANZA page parses");
Check(fanzaVideo?.Title == "ABP-123 Some Movie", "FANZA title");
Check(fanzaVideo?.CoverUrl?.Contains("abp00123ps.jpg") == true, "FANZA cover");
Check(fanzaVideo?.PreviewUrls.Count == 2, $"FANZA samples (got {fanzaVideo?.PreviewUrls.Count})");
Check(fanzaVideo?.Maker == "Prestige", "FANZA maker");
Check(fanzaVideo?.Genres.Count == 1, "FANZA genres");
Check(fanzaVideo?.Actresses.Count == 1 && fanzaVideo.Actresses[0] == "Yua Mikami", "FANZA actress");
Check(fanzaVideo?.ReleaseDate == new DateTime(2023, 5, 1), $"FANZA date from free text (got '{fanzaVideo?.ReleaseDate:yyyy-MM-dd}')");
Check(fanzaVideo?.RuntimeMinutes == 120, $"FANZA runtime (got {fanzaVideo?.RuntimeMinutes})");

Console.WriteLine("== Merge ==");
var primary = new JavVideo { Code = "ABP-123", Title = "Primary Movie Title" };
var secondary = new JavVideo
{
    Code = "ABP-123",
    Title = "Secondary Movie Title",
    Actresses = ["A"],
    MaleActors = ["B"],
    Genres = ["G"],
    CoverUrl = "https://x/c.jpg",
    ReleaseDate = new DateTime(2023, 1, 1),
    RuntimeMinutes = 90
};
var merged = JavMetadataProvider.Merge(primary, secondary)!;
Check(merged.Title == "Primary Movie Title", "primary title wins");
Check(merged.Actresses.Count == 1 && merged.Actresses[0] == "A", "gender-separated cast applied");
Check(merged.MaleActors.Count == 1 && merged.MaleActors[0] == "B", "male cast applied");
Check(merged.CoverUrl == "https://x/c.jpg", "cover gap filled");
Check(merged.ReleaseDate == new DateTime(2023, 1, 1), "date gap filled");
Check(merged.RuntimeMinutes == 90, "runtime gap filled");
Check(JavMetadataProvider.Merge(null, secondary)?.Code == "ABP-123", "null primary");
Check(JavMetadataProvider.Merge(primary, null)?.Title == "Primary Movie Title", "null secondary");

Console.WriteLine("== JavBusScraper.ParseVideoPage (real 2026 markup, dv=1 direct page) ==");
var busScraper = new JavBusScraper(logger, "en");
var realBusHtml = """
<html><head><title>ABP-982 - JavBus</title></head><body>
<h3>ABP-982 Some Movie Title</h3>
<div class="row movie">
  <div class="col-md-9 screencap">
    <a class="bigImage" href="/pics/cover/7pqc_b.jpg"><img src="/pics/cover/7pqc_b.jpg" title="x"></a>
  </div>
  <div class="col-md-3 info">
    <p><span class="header">ID:</span> <span style="color:#CC0000;">ABP-982</span></p>
    <p><span class="header">Release Date:</span> 2020-06-05</p>
    <p><span class="header">Length:</span> 221min</p>
    <p><span class="header">Director:</span> <a href="https://www.javbus.com/en/director/421">白いTシャツ屋さん</a></p>
    <p><span class="header">Studio:</span> <a href="https://www.javbus.com/en/studio/75">Prestige</a></p>
    <p><span class="header">Label:</span> <a href="https://www.javbus.com/en/label/xo">ABSOLUTELY PERFECT</a></p>
    <p class="header">Genre:<span id="genre-toggle"></span></p>
    <p><span class="genre"><label><input type="checkbox"><a href="https://www.javbus.com/en/genre/4o">Hi-Def</a></label></span>
       <span class="genre"><label><a href="https://www.javbus.com/en/genre/f">Featured Actress</a></label></span></p>
    <p class="star-show"><span class="header">JAV Idols</span>:</p>
    <ul><div id="star_qq9" class="star-box"><li>
      <a href="https://www.javbus.com/en/star/qq9"><img src="/pics/actress/qq9_a.jpg" title="Ai sound Maria"></a>
      <div class="star-name"><a href="https://www.javbus.com/en/star/qq9" title="Ai sound Maria">Ai sound Maria</a></div>
    </li></div></ul>
  </div>
</div>
<h4>Sample Images</h4>
<div id="sample-waterfall">
  <a class="sample-box" href="https://pics.dmm.co.jp/digital/video/118abp00982/118abp00982jp-1.jpg"><div class="photo-frame"><img src="/pics/sample/7pqc_1.jpg"></div></a>
  <a class="sample-box" href="https://pics.dmm.co.jp/digital/video/118abp00982/118abp00982jp-2.jpg"><div class="photo-frame"><img src="/pics/sample/7pqc_2.jpg"></div></a>
</div>
</body></html>
""";
var realBusDoc = new HtmlAgilityPack.HtmlDocument();
realBusDoc.LoadHtml(realBusHtml);
var realBusVideo = busScraper.ParseVideoPage(realBusDoc, realBusHtml);
Check(realBusVideo is not null, "JavBus real page parses");
Check(realBusVideo?.Code == "ABP-982", $"JavBus code (got '{realBusVideo?.Code}')");
Check(realBusVideo?.Title.Contains("ABP-982") == true, "JavBus h3 title");
Check(realBusVideo?.ReleaseDate == new DateTime(2020, 6, 5), $"JavBus release date from <p> text (got '{realBusVideo?.ReleaseDate:yyyy-MM-dd}')");
Check(realBusVideo?.RuntimeMinutes == 221, $"JavBus length from <p> text (got {realBusVideo?.RuntimeMinutes})");
Check(realBusVideo?.Director == "白いTシャツ屋さん", "JavBus director");
Check(realBusVideo?.Maker == "Prestige", "JavBus studio");
Check(realBusVideo?.Label == "ABSOLUTELY PERFECT", "JavBus label");
Check(realBusVideo?.Genres.Count == 2 && realBusVideo.Genres.Contains("Hi-Def"), $"JavBus genres via span.genre (got {realBusVideo?.Genres.Count})");
Check(realBusVideo?.Actresses.Count == 1 && realBusVideo.Actresses[0] == "Ai sound Maria", "JavBus actress via star link");
Check(realBusVideo?.CoverUrl == "https://www.javbus.com/pics/cover/7pqc_b.jpg", $"JavBus cover absolutized (got '{realBusVideo?.CoverUrl}')");
Check(realBusVideo?.PreviewUrls.Count == 2 && realBusVideo.PreviewUrls[0].Contains("dmm.co.jp"), $"JavBus sample-box previews (got {realBusVideo?.PreviewUrls.Count})");
Check(realBusVideo?.VideoId == "ABP-982", $"JavBus stable videoId=code (got '{realBusVideo?.VideoId}')");

Console.WriteLine("== JavBus age gate is a miss, not a record ==");
// A browser without the age cookie gets JavBus's "Age Verification JavBus"
// page instead of a detail page - the shape users report as "the site is a
// shell, it moved to another domain". The scraper sends dv=1, which skips
// the gate; should the gate ever answer anyway, it must be a clean miss.
var gateScraper = new JavBusScraper(logger, "en");
var gateHandler = HtmlInjector.SetPage(gateScraper, """
<html><head><title>Age Verification JavBus - JavBus</title>
<link rel="canonical" href="https://www.javbus.com/doc/driver-verify?referer=https%3A%2F%2Fwww.javbus.com%2Fen" /></head>
<body><p>你是否已經成年?</p><form class="form1" method="post" action=""></form></body></html>
""");
var gateVideo = await gateScraper.FindByCodeAsync("MIAB-492", CancellationToken.None);
Check(gateVideo is null, "JavBus age-gate page yields no record");
Check(gateHandler.LastRequestCookie?.Contains("dv=1", StringComparison.Ordinal) == true,
    $"JavBus requests carry the age cookie (got '{gateHandler.LastRequestCookie ?? "none"}')");
gateScraper.Dispose();

Console.WriteLine("== MissAvScraper cast parsing (localized labels, every performer) ==");
// Issue #5: zh pages label cast 女優 / 男優, and one title can credit several
// performers. The old XPath knew only "Actress:" and kept a single link, so a
// zh library produced no performers at all and cast never became collections.
var missav = new MissAvScraper(logger, "zh");
var missavZhHtml = """
<html><body>
<h1 class="title">MIAB-492 Some Movie</h1>
<span class="font-medium">MIAB-492</span>
<div>
  <span>女優:</span>
  <a href="/actresses/a">Performer A</a>
  <a href="/actresses/b">Performer B</a>
  <span>男優:</span>
  <a href="/actors/c">Actor C</a>
  <span>メーカー:</span>
  <a href="/makers/m">Studio M</a>
  <span>ジャンル:</span>
  <a href="/genres/1">Drama</a>
  <a href="/genres/2">Romance</a>
  <p><span>發行日期:</span> 2024-05-01</p>
</div>
</body></html>
""";
var missavZhDoc = new HtmlDocument();
missavZhDoc.LoadHtml(missavZhHtml);
var missavZhVideo = missav.ParseVideoPage(missavZhDoc, missavZhHtml);
Check(missavZhVideo?.Actresses.Count == 2, $"zh cast keeps both actresses (got {missavZhVideo?.Actresses.Count})");
Check(missavZhVideo?.Actresses.Contains("Performer A") == true
    && missavZhVideo.Actresses.Contains("Performer B"), "zh cast names");
Check(missavZhVideo?.MaleActors.Count == 1 && missavZhVideo.MaleActors[0] == "Actor C",
    $"zh cast keeps 男優 separate (got {missavZhVideo?.MaleActors.Count})");
Check(missavZhVideo?.Maker == "Studio M", $"zh maker label parses (got '{missavZhVideo?.Maker}')");
Check(missavZhVideo?.Genres.Count == 2 && missavZhVideo.Genres.Contains("Romance"),
    $"zh genre label keeps every genre (got {missavZhVideo?.Genres.Count})");
Check(missavZhVideo?.ReleaseDate == new DateTime(2024, 5, 1),
    $"zh release-date label parses (got '{missavZhVideo?.ReleaseDate:yyyy-MM-dd}')");

var missavEnHtml = """
<html><body>
<h1 class="title">MIAB-492 Some Movie</h1>
<span class="font-medium">MIAB-492</span>
<div>
  <span>Actress:</span>
  <a href="/actresses/a">Performer A</a>
  <a href="/actresses/b">Performer B</a>
</div>
</body></html>
""";
var missavEnDoc = new HtmlDocument();
missavEnDoc.LoadHtml(missavEnHtml);
var missavEnVideo = missav.ParseVideoPage(missavEnDoc, missavEnHtml);
Check(missavEnVideo?.Actresses.Count == 2,
    $"english cast keeps every link, not just the first (got {missavEnVideo?.Actresses.Count})");

Console.WriteLine("== Error-title rejection (CloudFront 403 pages etc.) ==");
Check(Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle("403 ERROR"), "403 ERROR rejected");
Check(Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle("The request could not be satisfied."), "cloudfront message rejected");
Check(Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle("Redirecting..."), "redirecting rejected");
Check(Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle("Just a moment..."), "challenge rejected");
Check(!Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle("ABP-123 The Popular Work Obedient Girl"), "real title accepted");
Check(!Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle("SSIS-406 涼森れむ"), "real CJK title accepted");
Check(Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle(""), "empty rejected");

// MGStage CloudFront 403 page must never parse into a record.
var mgstageDef = JavSiteCatalog.Sites.First(s => s.Key == "mgstage");
var mgstageScraper = new GenericSiteScraper(mgstageDef, logger);
var cf403 = "<HTML><HEAD><TITLE>ERROR: The request could not be satisfied</TITLE></HEAD><BODY><H1>403 ERROR</H1><H2>The request could not be satisfied.</H2></BODY></HTML>";
var cf403Doc = new HtmlAgilityPack.HtmlDocument();
cf403Doc.LoadHtml(cf403);
var cf403Video = mgstageScraper.ParseVideoPage(cf403Doc, cf403, "DASS-15");
Check(cf403Video is null, "CloudFront 403 page produces no record");

Console.WriteLine("== Merge: Latin-script preference ==");
var jpPrimary = new JavVideo { Code = "ABF-125", Title = "ABF-125 いいなりっ娘 人気作を実写化 涼森れむ", Maker = "SOD", Actresses = ["涼森れむ"] };
var enSecondary = new JavVideo { Code = "ABF-125", Title = "ABF-125 The Popular Work Obedient Girl Adapted Into A Live-action Version", Maker = "SOD create", Actresses = ["Remu Suzumori"] };
var langMerged = JavMetadataProvider.Merge(jpPrimary, enSecondary)!;
Check(!langMerged.Title.Contains('い'), $"Japanese title upgraded to English (got '{langMerged.Title.Substring(0, Math.Min(50, langMerged.Title.Length))}...')");
Check(langMerged.Actresses[0] == "Remu Suzumori", "Japanese cast name upgraded to romaji");
Check(langMerged.Maker == "SOD", "both-Latin maker keeps primary ('SOD')");
// Primary Latin + secondary Japanese: primary must win.
var latKeep = JavMetadataProvider.Merge(enSecondary, jpPrimary)!;
Check(latKeep.Title.StartsWith("ABF-125 The Popular"), "Latin primary title kept over Japanese secondary");
Check(latKeep.Actresses[0] == "Remu Suzumori", "Latin primary cast kept");

Console.WriteLine("== BuildName guards ==");
Check(JavMetadataProvider.BuildName(new JavVideo { Code = "DASS-15", Title = "403 ERROR" }) == "DASS-15", "403 ERROR title dropped to bare code");
Check(JavMetadataProvider.BuildName(new JavVideo { Code = "ABP-982", Title = "ABP-982 Real Movie" }) == "ABP-982 Real Movie", "code-prefixed title kept");

Console.WriteLine("== BuildName / BuildOverview ==");
var named = JavMetadataProvider.BuildName(new JavVideo { Code = "ABP-123", Title = "Some Movie" });
Check(named == "ABP-123 Some Movie", "name = CODE Title");
var bare = JavMetadataProvider.BuildName(new JavVideo { Code = "ABP-123", Title = "" });
Check(bare == "ABP-123", "bare code when no title");

Console.WriteLine("== Cloudflare solved-page regression ==");
// A solved JavLibrary page legitimately contains the challenge-platform
// script tag; it must NOT be treated as an unsolved challenge (that
// discarded every successfully solved page and forced endless re-solves).
Check(!Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle("ADN-115 Real Title"), "solved page title is not an error");
Check(Jellyfin.Plugin.JavOrganizer.SiteScraper.IsErrorTitle("Just a moment..."), "interstitial title is an error");


Console.WriteLine("== JavBus padded-code retry ==");
Check(JavBusScraper_Tests.ToPaddedKeywordPublic("SDMF-010") == "SDMF-010", "padded keyword for SDMF-010");
Check(JavBusScraper_Tests.ToPaddedKeywordPublic("SDMF-10") is null, "no padded variant for SDMF-10");
Check(JavBusScraper_Tests.ToPaddedKeywordPublic("ABP-00123") == "ABP-0123", "padded keyword for ABP-00123");
Check(JavBusScraper_Tests.ToPaddedKeywordPublic("T28-597") is null, "no padded variant for T28-597");

Console.WriteLine("== AdaptiveThrottle ==");
// At rest: no pressure, multiplier exactly 1, pauses are a no-op.
Check(AdaptiveThrottle.DelayMultiplier >= 1.0 && AdaptiveThrottle.DelayMultiplier < 1.05, $"rest multiplier ~1.0 (got {AdaptiveThrottle.DelayMultiplier:0.00})");
// A strong pushback raises pressure and the multiplier but stays bounded.
AdaptiveThrottle.ReportPressure(1.0);
var pressured = AdaptiveThrottle.Pressure;
Check(pressured > 0.25, $"ban signal raises pressure (got {pressured:0.00})");
Check(AdaptiveThrottle.DelayMultiplier > 1.3, $"multiplier stretches under pressure (got {AdaptiveThrottle.DelayMultiplier:0.00})");
Check(AdaptiveThrottle.DelayMultiplier <= 4.01, "multiplier bounded at 4x");
// Successes bleed pressure off.
for (var i = 0; i < 200; i++)
{
    AdaptiveThrottle.ReportSuccess();
}
Check(AdaptiveThrottle.Pressure < pressured, "successes reduce pressure");
AdaptiveThrottle.ReportPressure(0); // zero severity must be a safe no-op
Check(true, "zero-severity report is a no-op (no crash)");

Console.WriteLine("== FlareSolverrUrls ==");
// No configuration → no URL. With configuration (via the test's own
// instance defaults) the /v1 handling must never double-append.
Check(FlareSolverrUrls.ApiUrl is null, "no configuration means no API URL (unit-test defaults)");
var probe = FlareSolverrUrls.HealthUrl;
Check(probe is null, "no configuration means no health URL");
Check(FlareSolverrUrls_Tests.NormalizeForTest("http://localhost:8191/v1") == "http://localhost:8191/v1", "URL already ending in /v1 unchanged");
Check(FlareSolverrUrls_Tests.NormalizeForTest("http://localhost:8191/v1/") == "http://localhost:8191/v1", "trailing slash /v1 URL normalized");
Check(FlareSolverrUrls_Tests.NormalizeForTest("http://localhost:8191") == "http://localhost:8191/v1", "bare URL gets /v1 appended");
Check(FlareSolverrUrls_Tests.NormalizeForTest("http://localhost:8191/") == "http://localhost:8191/v1", "bare URL with slash gets /v1 appended");
Check(FlareSolverrUrls_Tests.NormalizeForTest("http://flaresolverr:8191/v1") == "http://flaresolverr:8191/v1", "Docker-style URL unchanged");

Console.WriteLine("== SiteScraper session profile coherence ==");
// Each scraper session pins one internally-coherent browser profile; two
// sessions may differ (random), but a single session's UA always matches
// its own client hints.
var stubForProfile = new StubLogger();
var busForProfile = new JavBusScraper(stubForProfile, "en");
var profileInfo = SiteScraper_Tests.SessionProfileOf(busForProfile);
Check(profileInfo is not null, "session profile exists");
if (profileInfo is not null)
{
    Check(!string.IsNullOrWhiteSpace(profileInfo.Value.UserAgent), "profile has a user agent");
    // Windows UA must come with Windows platform hints.
    if (profileInfo.Value.UserAgent.Contains("Windows", StringComparison.Ordinal))
    {
        Check(profileInfo.Value.SecChUaPlatform == "\"Windows\"", $"Windows UA pairs with Windows platform (got '{profileInfo.Value.SecChUaPlatform}')");
    }
}

Console.WriteLine("== Merge: person photos ==");
var photoPrimary = new JavVideo { Code = "ABP-123", Title = "Primary Movie Title" };
var photoSecondary = new JavVideo
{
    Code = "ABP-123",
    Title = "Secondary Movie Title",
    Actresses = ["A"],
    PersonImageUrls = new Dictionary<string, string> { ["A"] = "https://x/a.jpg", ["B"] = "https://x/b.jpg" }
};
var photoMerged = JavMetadataProvider.Merge(photoPrimary, photoSecondary)!;
Check(photoMerged.PersonImageUrls.Count == 2, "person photos merged from secondary");
var photoReversed = JavMetadataProvider.Merge(photoSecondary, new JavVideo { Code = "ABP-123", Title = "T", PersonImageUrls = new Dictionary<string, string> { ["A"] = "https://x/other.jpg" } })!;
Check(photoReversed.PersonImageUrls["A"] == "https://x/a.jpg", "primary person photo wins over secondary");

Console.WriteLine("== JavBus star portrait collection ==");
// JavBus detail pages carry star portraits as <img title="Name">; the
// scraper must map the cast names to those photo URLs.
var portraitScraper = new JavBusScraper(logger, "en");
var portraitHtml = """
<html><head><title>ABP-982 - JavBus</title></head><body>
<h3>ABP-982 Some Movie Title</h3>
<span class="info">
  <span class="header">ID:</span> <span>ABP-982</span>
</span>
<ul><div id="star_qq9" class="star-box"><li>
  <a href="https://www.javbus.com/en/star/qq9"><img src="/pics/actress/qq9_a.jpg" title="Ai sound Maria"></a>
  <div class="star-name"><a href="https://www.javbus.com/en/star/qq9" title="Ai sound Maria">Ai sound Maria</a></div>
</li></div></ul>
</body></html>
""";
var portraitDoc = new HtmlAgilityPack.HtmlDocument();
portraitDoc.LoadHtml(portraitHtml);
var portraitVideo = portraitScraper.ParseVideoPage(portraitDoc, portraitHtml);
Check(portraitVideo is not null, "portrait page parses");
Check(portraitVideo?.Actresses.Contains("Ai sound Maria") == true, "cast name from star link");
Check(portraitVideo?.PersonImageUrls.TryGetValue("Ai sound Maria", out var photoUrl) == true
    && photoUrl == "https://www.javbus.com/pics/actress/qq9_a.jpg",
    $"star portrait mapped to absolute URL (got '{portraitVideo?.PersonImageUrls.Values.FirstOrDefault()}')");

Console.WriteLine("== Performer photo matching (cover fix) ==");
// The label match is the reliable path, but real pages also expose portraits
// through a shared star id and through the performer's name inside the file
// name. Every one of those must still put a face on the card.
Check(portraitVideo?.PersonImageNameHints.TryGetValue("Ai sound Maria", out var hint) == true && hint == "qq9",
    $"star id recorded as a photo hint (got '{portraitVideo?.PersonImageNameHints.Values.FirstOrDefault()}')");

// Star-id path: the portrait file carries the id, the credit link carries the
// spelling — the two must still be joined.
var starIdHtml = """
<html><head><title>ABP-100 - Site</title></head><body>
<span class="header">ID:</span> <span>ABP-100</span>
<div class="star-box"><a href="/en/star/uly"><img data-src="/pics/actress/uly_a.jpg" alt=""></a></div>
<a href="/en/star/uly" class="star-name">Suzumori remu</a>
</body></html>
""";
var starIdDoc = new HtmlAgilityPack.HtmlDocument();
starIdDoc.LoadHtml(starIdHtml);
var starIdVideo = portraitScraper.ParseVideoPage(starIdDoc, starIdHtml);
Check(starIdVideo?.PersonImageUrls.TryGetValue("Suzumori remu", out var starPhoto) == true
    && starPhoto == "https://www.javbus.com/pics/actress/uly_a.jpg",
    $"star id joins a lazy-loaded portrait to its credit (got '{starIdVideo?.PersonImageUrls.Values.FirstOrDefault()}')");

// Name-in-URL path: no title, no star link — only the performer's own name
// inside the portrait file name identifies the photo.
var nameUrlHtml = """
<html><head><title>ABP-200 - Site</title></head><body>
<span class="header">ID:</span> <span>ABP-200</span>
<div class="cast"><img data-src="https://cdn.example.com/actress/mina_kitano_a.jpg"></div>
<a href="/star/mina-kitano">Mina Kitano</a>
</body></html>
""";
var nameUrlDoc = new HtmlAgilityPack.HtmlDocument();
nameUrlDoc.LoadHtml(nameUrlHtml);
var nameUrlVideo = portraitScraper.ParseVideoPage(nameUrlDoc, nameUrlHtml);
Check(nameUrlVideo?.PersonImageUrls.TryGetValue("Mina Kitano", out var namePhoto) == true,
    $"performer name inside the portrait URL resolves the photo (got '{nameUrlVideo?.PersonImageUrls.Values.FirstOrDefault()}')");

// Read-time fallback: a cache record written before performer photos existed
// still yields a photo through the shared matcher.
var legacyRecord = new JavVideo
{
    Code = "ABP-300",
    Title = "Legacy",
    Actresses = ["Suzumori remu"],
    PersonImageUrls = new Dictionary<string, string> { ["snq"] = "https://www.javbus.com/pics/actress/snq_a.jpg" },
    PersonImageNameHints = new Dictionary<string, string> { ["Suzumori remu"] = "snq" }
};
Check(SiteScraper.PersonImageFor(legacyRecord, "Suzumori remu") == "https://www.javbus.com/pics/actress/snq_a.jpg",
    "hinted photo resolves for a legacy record");

// Spelling drift between sibling sites must not cost a performer a photo.
Check(SiteScraper.PersonImageFor(portraitVideo, "Ai sound  Maria")
        == "https://www.javbus.com/pics/actress/qq9_a.jpg",
    "normalized name matches across spelling differences");

// A performer the page never portrayed gets nothing, rather than a wrong face.
Check(SiteScraper.PersonImageFor(portraitVideo, "Someone Else") is null,
    "no photo is invented for an unportrayed performer");

// Filler markers must never be mistaken for a performer name.
Check(SiteScraper.PersonImageFor(
        new JavVideo
        {
            Code = "ABP-400",
            Actresses = ["A"],
            PersonImageUrls = new Dictionary<string, string> { ["x"] = "https://cdn/actress/a_a.jpg" }
        },
        "A") is null,
    "single-letter filler names never match an image");

Console.WriteLine("== Nested collection name prefixes ==");
// The gender cards and the per-performer collections find each other by the
// "Actress: " / "Actor: " name prefixes, so the two must stay in sync. The
// collection task itself needs a live server, so this pins the contract the
// task and the cards are both written against.
var personPrefixes = new[] { "Actress: ", "Actor: " };
Check(personPrefixes.All(p => p.EndsWith(' ')), "performer prefixes keep their trailing space");
Check(personPrefixes.Distinct(StringComparer.Ordinal).Count() == 2, "performer prefixes are distinct");
Check("Actress: Mina Kitano".StartsWith(personPrefixes[0], StringComparison.OrdinalIgnoreCase),
    "an actress collection name carries its prefix");

Console.WriteLine("== Ban-page detection (regression: false positives disable a site) ==");
// A false positive here costs a site for six hours on every scrape — which
// is exactly what jav.guru's comment-widget strings caused. A false
// negative means the plugin keeps hammering an address the site banned.
foreach (var html in new[]
{
    "Your access to this site has been banned.",
    "We have banned your access due to abuse.",
    "Your IP has been blocked.",
    "Access to this page has been denied.",
    "Access to this site has been blocked.",
    "禁止了你的訪問",
    "Your address has been banned from this server.",
    "Your IP is blocked.",
})
{
    Check(SiteScraper.IsBanPage(html), $"ban page detected: {html}");
}

foreach (var html in new[]
{
    "\"wc_rate_limit_exceeded\":\"Too many requests. Please slow down.\"",
    "Some content may be access denied in your region",
    "Runtime error: request failed with 429 too many requests",
    "This site uses cookies. Please accept to continue.",
    "<a href=\"/banned\">Banned users list</a>",
    "The admin has banned this user from commenting.",
    "This member has banned nobody.",
})
{
    Check(!SiteScraper.IsBanPage(html), $"not a ban page: {html}");
}

Console.WriteLine("== Page classification: a Cloudflare challenge is never an IP ban ==");
// A failed FlareSolverr solve returns the challenge page, which can carry
// ban-like wording. It must classify as a transient challenge, not a ban —
// otherwise a reachable site (JavLibrary, the English-title source) gets
// locked out for six hours while it keeps serving pages.
Check(SiteScraper.ClassifyPage("Just a moment... Your IP address has been blocked. Enable JavaScript and cookies to continue") == SiteScraper.PageKind.Challenge,
    "a challenge carrying ban wording is a challenge, not a ban");
Check(SiteScraper.ClassifyPage("Attention Required! | Cloudflare") == SiteScraper.PageKind.Challenge,
    "a Cloudflare challenge page is a challenge, not a ban");
Check(SiteScraper.ClassifyPage("Your IP address has been banned from this site.") == SiteScraper.PageKind.Ban,
    "a genuine ban page with no challenge is a ban");
Check(SiteScraper.ClassifyPage("<title>ADN-138 Forbidden Tantrums - JAVLibrary</title>") == SiteScraper.PageKind.Content,
    "a normal video page is content");

Console.WriteLine("== Bracket-wrapped code in titles ==");
// Several sites head their pages with "[CODE] Title"; the built name must
// not repeat the code. Sites also append edition suffixes to that code
// ("[ADN-029-MR]", "[SSIS-448-SUB]"), which must still be recognised as the
// code rather than a different video.
foreach (var (title, code, want, label) in new (string, string, string, string)[]
{
    ("[MIAB-492] My Practice Dummy", "MIAB-492", "MIAB-492 My Practice Dummy", "plain bracketed code"), 
    ("(miab492) My Practice Dummy", "MIAB-492", "MIAB-492 My Practice Dummy", "glued parenthesised code"),
    ("[MIAB-492]", "MIAB-492", "MIAB-492", "title that is only the bracketed code"),
    ("[ABP-901] Plain Title", "ABP-901", "ABP-901 Plain Title", "bracketed code with digits"),
    ("[ABC-12] Short", "ABC-12", "ABC-12 Short", "short digit group"),
    ("【MIAB-492】 CJK brackets", "MIAB-492", "MIAB-492 CJK brackets", "CJK bracket pair"),
    ("[ADN-029-MR] Honey, Forgive Me", "ADN-029", "ADN-029 Honey, Forgive Me", "edition suffix stripped"),
    ("[SSIS-448-SUB] Subbed Title", "SSIS-448", "SSIS-448 Subbed Title", "subtitle suffix stripped"),
    ("[MIAB-492-4K] Both", "MIAB-492", "MIAB-492 Both", "digit-leading suffix stripped"),
    ("[4K] My Practice Dummy", "MIAB-492", "MIAB-492 [4K] My Practice Dummy", "non-code bracket kept"),
    ("[ADN-0299] Different Video", "ADN-029", "ADN-029 [ADN-0299] Different Video", "longer number is a different video"),
    ("[XYZ-999] Unrelated", "MIAB-492", "MIAB-492 [XYZ-999] Unrelated", "another code is kept"),
    ("[MIAB-4920] Longer number", "MIAB-492", "MIAB-492 [MIAB-4920] Longer number", "extended number is a different video"),
})
{
    var built = JavMetadataProvider.BuildName(new JavVideo { Code = code, Title = title });
    Check(built == want, $"{label} (got '{built}')");
}

Console.WriteLine("== Search-keyword variants (zero-padded catalogues) ==");
// The canonical keyword is zero-free ("ADN-29"), but some sites only index
// the padded spelling ("ADN-029"). Both must find the same video's URL.
var paddedUrl = "https://jav.guru/119571/adn-029-honey-forgive-me-the-rekindling-of-love-kaori/";
Check(GenericSiteScraper.UrlMentionsCode(paddedUrl, "adn-29"), "padded URL matches a zero-free code");
Check(GenericSiteScraper.UrlMentionsCode("https://x/adn029", "adn-29"), "glued URL matches a zero-free code");
Check(GenericSiteScraper.UrlMentionsCode("https://x/adn-29", "adn-29"), "canonical URL matches");
Check(!GenericSiteScraper.UrlMentionsCode("https://x/adn-030", "adn-29"), "a different code does not match");
Check(!GenericSiteScraper.UrlMentionsCode("https://x/abp-901", "adn-29"), "an unrelated code does not match");

Console.WriteLine("== Title merge: a bare code never beats a real title ==");
var realTitle = new JavVideo { Code = "MIAB-492", Title = "MIAB-492 My Practice Dummy" };
var codeOnlyTitle = new JavVideo { Code = "MIAB-492", Title = "MIAB492" };
var preferReal = JavMetadataProvider.Merge(codeOnlyTitle, realTitle)!;
Check(preferReal.Title == "MIAB-492 My Practice Dummy",
    $"real title wins over a bare code (got '{preferReal.Title}')");
var preferRealReversed = JavMetadataProvider.Merge(realTitle, codeOnlyTitle)!;
Check(preferRealReversed.Title == "MIAB-492 My Practice Dummy",
    $"bare code never replaces a real title (got '{preferRealReversed.Title}')");

Console.WriteLine("== Site-name titles are rejected ==");
// OneJAV's og:title is the literal string "OneJAV"; merging that would put
// the site's name in the library as a video title.
var onejavSiteForTitle = JavSiteCatalog.Sites.First(s => s.Key == "onejav");
var onejavTitleScraper = new GenericSiteScraper(onejavSiteForTitle, logger);
var siteNameHtml = """
<html><head>
  <meta property="og:title" content="OneJAV" />
  <meta property="og:image" content="https://onejav.com/x.jpg" />
</head><body><h1>nothing here</h1></body></html>
""";
var siteNameDoc = new HtmlDocument();
siteNameDoc.LoadHtml(siteNameHtml);
var siteNameVideo = onejavTitleScraper.ParseVideoPage(siteNameDoc, siteNameHtml, "MIAB-492");
Check(siteNameVideo is null,
    "a page whose only title is the site's own name is rejected");

Console.WriteLine("== jav.guru catalog entry ==");
var javGuru = JavSiteCatalog.Sites.FirstOrDefault(s => s.Key == "javguru");
Check(javGuru is not null, "jav.guru is in the catalog");
Check(javGuru?.BaseUrl == "https://jav.guru/", $"jav.guru base URL (got '{javGuru?.BaseUrl}')");
Check(javGuru?.Mode == SiteUrlMode.Search, "jav.guru is a search-mode site");
Check(javGuru?.SearchPath("MIAB-492") == "?s=MIAB-492", $"jav.guru search URL (got '{javGuru?.SearchPath("MIAB-492")}')");
Check(!string.IsNullOrWhiteSpace(javGuru?.Selectors.ResultLinks), "jav.guru defines result links");
Check(!string.IsNullOrWhiteSpace(javGuru?.Selectors.MaleActors), "jav.guru separates male actors");
Check(!string.IsNullOrWhiteSpace(javGuru?.Selectors.Actresses), "jav.guru separates actresses");
Check(PluginConfiguration_Probe.HasJavGuruToggle(), "jav.guru has a configuration toggle");

Console.WriteLine("== Page-code verification (regression: variant suffixes) ==");
// Sites append edition/source suffixes to the code they display
// ("[ADN-029-MR]"), which normalizes to "adn-029-mr". A strict equality
// check discarded those pages as mismatches even though they are the right
// video — jav.guru's whole catalogue is published this way.
foreach (var (pageCode, requested, expected, label) in new (string, string, bool, string)[]
{
    ("adn-029-mr", "adn-029", true, "MR edition suffix accepted"),
    ("adn-029", "adn-029-mr", true, "requested variant accepted"),
    ("abp-901", "abp-901", true, "identical codes accepted"),
    ("", "abp-901", true, "page with no code is not rejected"),
    ("abp-901", "", true, "no requested code is not rejected"),
    ("adn-029", "adn-030", false, "different number rejected"),
    ("adn-029", "adn-0299", false, "longer number rejected"),
    ("abp-901", "abp-9011", false, "padded-lookalike rejected"),
    ("abc-12", "abc-123", false, "shorter number rejected"),
    ("ssis-448", "ssis-44", false, "truncated number rejected"),
})
{
    var actual = GenericSiteScraper.CodesAgree(pageCode, requested);
    Check(actual == expected,
        $"{label} (page='{pageCode}' req='{requested}' -> {actual})");
}

Console.WriteLine("== FlareSolverr URL override (test harness seam) ==");
Check(FlareSolverrUrls.UrlOverrideVariable.Length > 0, "the override variable is named");

Console.WriteLine("== Language-retry bound (regression: endless futile re-scrapes) ==");
// A title with no English release anywhere is re-scraped across every
// enabled site on each scan unless the retry count is bounded and carried
// forward across the rewrite. This is what kept one item re-scraping 10
// sites every 6 hours forever.
var jpTitle = "SDMF-010 妹にコスプレを着させて毎日、性欲処理をしています。";
var firstRetry = new JavVideo { Code = "SDMF-010", Title = jpTitle, CoverUrl = "https://x/c.jpg" };
Check(firstRetry.LanguageRetries == 0, "a fresh record starts with no language retries");

// The counter must survive a round trip through the cache format.
var json = System.Text.Json.JsonSerializer.Serialize(firstRetry);
var roundTripped = System.Text.Json.JsonSerializer.Deserialize<JavVideo>(json)!;
Check(roundTripped.LanguageRetries == 0, "retry count serializes and deserializes");

var counted = new JavVideo { Code = "SDMF-010", Title = jpTitle, LanguageRetries = 2 };
var countedJson = System.Text.Json.JsonSerializer.Serialize(counted);
var countedBack = System.Text.Json.JsonSerializer.Deserialize<JavVideo>(countedJson)!;
Check(countedBack.LanguageRetries == 2, $"retry count round-trips (got {countedBack.LanguageRetries})");

// Records written before the field existed must deserialize to zero rather
// than throwing, so old caches keep working.
var legacyJson = """{"code":"ABC-123","title":"Legacy"}""";
var legacy = System.Text.Json.JsonSerializer.Deserialize<JavVideo>(legacyJson)!;
Check(legacy.LanguageRetries == 0, "a record without the field defaults to zero");

// A retry only counts when JavLibrary — the English-title source — actually
// answered. Re-scraping while it is banned must not spend the budget, or a
// temporary ban locks in a Japanese title that an English release exists for.
var bannedRound = new JavVideo { Code = "ADN-138", Title = jpTitle, CoverUrl = "https://x/c.jpg" };
Check(JavMetadataProvider.NextLanguageRetries(bannedRound, javLibraryReached: false) == 0,
    "a re-scrape while JavLibrary is banned does not consume a retry");
Check(JavMetadataProvider.NextLanguageRetries(bannedRound, javLibraryReached: true) == 1,
    "a re-scrape that reached JavLibrary consumes one retry");
var nearlySpent = new JavVideo { Code = "ADN-138", Title = jpTitle, LanguageRetries = 2 };
Check(JavMetadataProvider.NextLanguageRetries(nearlySpent, javLibraryReached: false) == 2,
    "a ban late in the budget preserves the remaining attempts");
Check(JavMetadataProvider.NextLanguageRetries(nearlySpent, javLibraryReached: true) == 3,
    "a reached round late in the budget advances to the bound");
var englishTitle = new JavVideo { Code = "ADN-115", Title = "ADN-115 An English Release", LanguageRetries = 1 };
Check(JavMetadataProvider.NextLanguageRetries(englishTitle, javLibraryReached: true) == 1,
    "an already-English title never consumes a retry");

// A fresh, substantive Japanese record must NOT be served from cache: the
// rescan queues it (NeedsScrape) and the provider has to actually re-scrape
// it. Serving it within the six-hour thin-record grace made every rescan a
// no-op and left the Japanese title in place, which is exactly the "English
// naming still not fixed" symptom.
var freshJapanese = new JavVideo { Code = "JUR-716", Title = jpTitle, CoverUrl = "https://x/c.jpg", Actresses = ["Someone"] };
Check(!JavMetadataProvider.ShouldServeCached(freshJapanese, withinThinGrace: true),
    "a fresh Japanese-titled record is re-scraped instead of served from cache");

var freshEnglish = new JavVideo { Code = "JUR-716", Title = "JUR-716 An English Release", CoverUrl = "https://x/c.jpg", Actresses = ["Someone"] };
Check(JavMetadataProvider.ShouldServeCached(freshEnglish, withinThinGrace: true),
    "a fresh English-titled record is served from cache");

var spentJapanese = new JavVideo { Code = "SDMF-010", Title = jpTitle, CoverUrl = "https://x/c.jpg", Actresses = ["Someone"], LanguageRetries = 3 };
Check(JavMetadataProvider.ShouldServeCached(spentJapanese, withinThinGrace: false),
    "a Japanese title that spent its retries is served instead of scraped forever");

var thinFresh = new JavVideo { Code = "ABC-123", Title = "ABC-123", LanguageRetries = 3 };
Check(JavMetadataProvider.ShouldServeCached(thinFresh, withinThinGrace: true),
    "a thin record within its grace window is served");
Check(!JavMetadataProvider.ShouldServeCached(thinFresh, withinThinGrace: false),
    "a thin record past its grace window is re-scraped");

Console.WriteLine("== Repository manifest shape (regression: Jellyfin cannot read it) ==");
// Jellyfin deserializes a plugin repository manifest into PackageInfo[] — an
// ARRAY of plugins. This file was hand-written as a single JSON OBJECT, which
// Jellyfin rejects with "The JSON value could not be converted to
// MediaBrowser.Model.Updates.PackageInfo[]" on every server start, leaving
// the plugin invisible in the catalog and impossible to install or update.
// The shape is now pinned here so it cannot silently regress.
var manifestPath = ManifestProbe.FindManifest();
Check(manifestPath is not null, "manifest.json is reachable from the test run");

if (manifestPath is not null)
{
    var manifestText = File.ReadAllText(manifestPath);
    using var manifestDoc = System.Text.Json.JsonDocument.Parse(manifestText);
    var root = manifestDoc.RootElement;

    Check(root.ValueKind == System.Text.Json.JsonValueKind.Array,
        $"manifest root is a JSON array (got {root.ValueKind})");

    if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
        Check(root.GetArrayLength() >= 1, "manifest lists at least one plugin");

        var plugin = root[0];

        // The exact key set Jellyfin's PackageInfo binds to.
        foreach (var key in new[] { "guid", "name", "description", "overview", "owner", "category", "versions" })
        {
            Check(plugin.TryGetProperty(key, out _), $"manifest plugin has '{key}'");
        }

        Check(plugin.TryGetProperty("versions", out var versions)
            && versions.ValueKind == System.Text.Json.JsonValueKind.Array
            && versions.GetArrayLength() > 0,
            "manifest plugin has a non-empty versions array");

        if (versions.ValueKind == System.Text.Json.JsonValueKind.Array && versions.GetArrayLength() > 0)
        {
            var newest = versions[0];
            foreach (var key in new[] { "version", "changelog", "targetAbi", "sourceUrl", "checksum", "timestamp" })
            {
                Check(newest.TryGetProperty(key, out _), $"newest version has '{key}'");
            }

            // Jellyfin's InstallationManager verifies the downloaded zip with
            // MD5 (MD5.HashDataAsync + Convert.ToHexString) and compares it to
            // `checksum` case-insensitively. A SHA-256 here makes EVERY catalog
            // install fail with "The checksum of the received data doesn't
            // match" (issue #1), so every entry must carry a 32-char hex MD5.
            foreach (var entry in versions.EnumerateArray())
            {
                var checksum = entry.GetProperty("checksum").GetString() ?? string.Empty;
                Check(checksum.Length == 32 && checksum.All(Uri.IsHexDigit),
                    $"version {entry.GetProperty("version").GetString()} checksum is a 32-char hex MD5 (got '{checksum}')");
            }

            var sourceUrl = newest.GetProperty("sourceUrl").GetString() ?? string.Empty;
            Check(sourceUrl.Contains("/releases/download/", StringComparison.Ordinal),
                "newest sourceUrl points at a release asset");

            // The advertised file name must contain the version it claims,
            // otherwise the manifest points at a build that does not exist.
            var version = newest.GetProperty("version").GetString() ?? string.Empty;
            Check(sourceUrl.Contains($"v{version}", StringComparison.OrdinalIgnoreCase),
                $"sourceUrl names version {version}");

            // Versions must be ordered newest-first, which is what Jellyfin
            // shows in the catalog.
            var parsed = versions.EnumerateArray()
                .Select(v => Version.TryParse(v.GetProperty("version").GetString(), out var p) ? p : null)
                .ToList();
            var ordered = parsed.Where(v => v is not null)
                .Select(v => v!)
                .ToList();
            Check(ordered.Count == parsed.Count, "every version string is a valid version number");
            Check(ordered.SequenceEqual(ordered.OrderByDescending(v => v)),
                "versions are ordered newest first");

            // Universal version support: the newest version must offer a
            // build for every Jellyfin line, each with its own targetAbi, so
            // a server on any of those lines is offered a matching build
            // instead of being told the plugin is unavailable.
            var newestVersion = newest.GetProperty("version").GetString() ?? string.Empty;
            var newestAbis = versions.EnumerateArray()
                .Where(v => (v.GetProperty("version").GetString() ?? string.Empty) == newestVersion)
                .Select(v => v.GetProperty("targetAbi").GetString() ?? string.Empty)
                .ToList();
            Check(newestAbis.Distinct().Count() == newestAbis.Count,
                "every build of the newest version carries its own targetAbi");
            foreach (var abi in new[] { "12.2.0.0", "12.1.0.0", "12.0.0.0", "10.11.11.0", "10.10.7.0", "10.9.11.0", "10.8.13.0" })
            {
                Check(newestAbis.Contains(abi), $"the newest version covers Jellyfin {abi}");
            }
        }
    }
}

Console.WriteLine("== Plugin settings page (regression: the settings page was unreachable) ==");
// The Jellyfin 12.x web client picks the configuration page with
// EnableInMainMenu set when a plugin registers more than one. The plugin used
// to register a second "Scan" page, so the Plugins -> Settings button led to
// the scan page and the settings were never reachable. One page, and it must
// be the one the client links to.
var pluginInstance = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
var pluginPages = pluginInstance.GetPages().ToList();
Check(pluginPages.Count == 1, $"the plugin registers exactly one page (got {pluginPages.Count})");

var pluginPage = pluginPages.FirstOrDefault();
if (pluginPage is not null)
{
    Check(pluginPage.EnableInMainMenu,
        "the page is in the main menu, so the menu entry and the Settings button are the same page");

    var pageAssembly = typeof(Plugin).Assembly;
    var pageResource = pageAssembly.GetManifestResourceStream(pluginPage.EmbeddedResourcePath);
    Check(pageResource is not null,
        $"the advertised page is embedded in the assembly ('{pluginPage.EmbeddedResourcePath}')");

    if (pageResource is not null)
    {
        using var pageReader = new StreamReader(pageResource);
        var pageHtml = pageReader.ReadToEnd();

        var controlIds = System.Text.RegularExpressions.Regex
            .Matches(pageHtml, "id=\"([A-Za-z0-9_]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Every setting needs a control, otherwise it is invisible in the UI.
        foreach (var property in typeof(PluginConfiguration).GetProperties())
        {
            Check(controlIds.Contains(property.Name),
                $"setting '{property.Name}' has a control on the page");
        }

        // The scan controls live on the settings page now; they must survive.
        foreach (var id in new[] { "ScanNowButton", "DeepScanButton", "CancelScanButton", "ScanProgressBar", "ScanState" })
        {
            Check(controlIds.Contains(id), $"the page still has the '{id}' control");
        }

        // The Jellyfin 12.x web client has no AMD loader: loading a module
        // through it throws there and swallows the rest of the handler, which
        // left the scan stuck on "Starting..." and the save without feedback.
        Check(!System.Text.RegularExpressions.Regex.IsMatch(pageHtml, @"require\s*\(\s*\["),
            "the page does not load modules through the AMD loader, which the 12.x client does not have");

        // A browser that refuses to submit the form (a field outside its
        // validation range) blocks the save with no visible error.
        Check(pageHtml.Contains("novalidate", StringComparison.Ordinal),
            "the form cannot be blocked by the browser's own validation");
        foreach (var id in new[] { "ScanStatus", "ConfigStatus" })
        {
            Check(controlIds.Contains(id), $"the page has a '{id}' line for messages");
        }

        // One failed read must never end the progress indicator, and an
        // expired session has to be named as such - "reload the page" does
        // not help when the session is gone.
        Check(!pageHtml.Contains("Reload the page to try again", StringComparison.Ordinal),
            "the page never tells the user to reload, which does not fix an expired session");
        Check(pageHtml.Contains("still retrying", StringComparison.Ordinal),
            "a failed scan-state read keeps the indicator retrying");
    }
}

Console.WriteLine("== Sparse-scrape detection (regression: metadata never healed) ==");
// An item that was scraped thinly keeps its provider id, so the "no provider
// id" and "title-only" conditions never revisit it. Its genres, studio and
// overview stayed empty forever even though the sites still held the data.
// These cases pin exactly when such an item is picked up again.
var fullyScraped = new MediaBrowser.Controller.Entities.Movies.Movie
{
    Name = "ABC-123 A Complete Title",
    Overview = "Provider: JavOrganizer\nCode: ABC-123\nMaker: Studio",
    Genres = ["Drama", "Romance"],
    Studios = ["Studio"]
};
fullyScraped.ProviderIds[JavMetadataProvider.ProviderIdKey] = "abc123";
Check(!JavScanTrigger.IsSparselyScraped(fullyScraped), "a complete scrape is not pending");

var noGenres = new MediaBrowser.Controller.Entities.Movies.Movie
{
    Name = "ABC-123 A Title",
    Overview = "Provider: JavOrganizer\nCode: ABC-123",
    Genres = [],
    Studios = ["Studio"]
};
noGenres.ProviderIds[JavMetadataProvider.ProviderIdKey] = "abc123";
Check(JavScanTrigger.IsSparselyScraped(noGenres), "no genres is pending");

var noStudios = new MediaBrowser.Controller.Entities.Movies.Movie
{
    Name = "ABC-123 A Title",
    Overview = "Provider: JavOrganizer\nCode: ABC-123",
    Genres = ["Drama"],
    Studios = []
};
noStudios.ProviderIds[JavMetadataProvider.ProviderIdKey] = "abc123";
Check(JavScanTrigger.IsSparselyScraped(noStudios), "no studios is pending");

var noOverview = new MediaBrowser.Controller.Entities.Movies.Movie
{
    Name = "ABC-123 A Title",
    Overview = string.Empty,
    Genres = ["Drama"],
    Studios = ["Studio"]
};
noOverview.ProviderIds[JavMetadataProvider.ProviderIdKey] = "abc123";
Check(JavScanTrigger.IsSparselyScraped(noOverview), "no overview is pending");

// An item the plugin never touched is handled by the "no provider id"
// condition instead, so this check must not claim it.
var untouched = new MediaBrowser.Controller.Entities.Movies.Movie
{
    Name = "ABC-123 A Title",
    Genres = [],
    Studios = []
};
Check(!JavScanTrigger.IsSparselyScraped(untouched),
    "an item with no provider id is not reported as sparsely scraped");

Console.WriteLine("== Normal scan never re-scrapes already-scraped metadata ==");
// The requirement: metadata that was already fetched and is still valid in
// the cache must not be fetched again. A complete item is never queued, and
// even a sparse item whose cached scrape is still within its TTL is left
// alone — only a sparse item with no (or an expired) cached scrape is
// revisited. These cases use a code that has no cache record on disk, which
// is the state of an item that has never been scraped.
const string freshCode = "ZZZ-001";
var complete = MakeMovie("ZZZ-001 A Complete Title", freshCode, g: ["Drama"], s: ["Studio"], o: "Provider: JavOrganizer");
Check(!JavScanTrigger.NeedsScrape(complete, freshCode, wantEnglish: true),
    "a complete item is never queued, whatever the language setting");

var sparse = MakeMovie("ZZZ-001 A Sparse Title", freshCode, g: [], s: [], o: "");
Check(JavScanTrigger.NeedsScrape(sparse, freshCode, wantEnglish: true),
    "a sparse item with no cached scrape is queued once, to heal");

var neverScrapedItem = MakeMovie("ZZZ-001 An Untouched Title", freshCode, g: [], s: [], o: "");
neverScrapedItem.ProviderIds.Remove(JavMetadataProvider.ProviderIdKey);
Check(JavScanTrigger.NeedsScrape(neverScrapedItem, freshCode, wantEnglish: true),
    "an item the plugin never touched is always queued");

// A sparse item whose cached scrape is still valid is left alone: the sites
// were already asked for this code and the record has not aged out.
var cacheProbe = MakeMovie("ZZZ-002 Probe Title", freshCode, g: [], s: [], o: "");
Check(!JavCache.IsStillValid(JavCodeParser.Normalize("ZZZ-002")),
    "a code with no cache record is not treated as already-scraped");
Check(JavScanTrigger.NeedsScrape(cacheProbe, "ZZZ-002", wantEnglish: true),
    "so that item is queued for its first scrape");

// Language upgrade is bounded and only applies when English is configured.
var japanese = MakeMovie("ZZZ-003 日本語のタイトル", freshCode, g: ["Drama"], s: ["Studio"], o: "Provider: JavOrganizer");
Check(JavScanTrigger.NeedsScrape(japanese, freshCode, wantEnglish: true),
    "a Japanese-titled item is queued when the language is English");
Check(!JavScanTrigger.NeedsScrape(japanese, freshCode, wantEnglish: false),
    "a Japanese-titled item is left alone when the language is not English");

// Issue #5: a cached record with a title and cover but no cast counts as
// complete, so the item was never queued again and the library's actress
// collections stayed empty. It is re-queued until the bounded cast-repair
// budget is spent; after that the record is served again instead of churning.
var castlessRecord = new JavVideo
{
    Code = "ZZZ-020",
    Title = "ZZZ-020 Cover Only",
    CoverUrl = "https://x/c.jpg",
    Genres = ["Drama"]
};
Check(JavMetadataProvider.NeedsCastRepair(castlessRecord), "a record with no cast needs repair");
Check(JavMetadataProvider.CastRepairPending(castlessRecord), "and its repair budget is unspent");
Check(!JavMetadataProvider.ShouldServeCached(castlessRecord, withinThinGrace: true),
    "a castless record is not served from cache while repairs remain");
castlessRecord.CastRetries = JavMetadataProvider.MaxCastRetries;
Check(!JavMetadataProvider.CastRepairPending(castlessRecord), "a spent cast budget stops the retries");
Check(JavMetadataProvider.ShouldServeCached(castlessRecord, withinThinGrace: false),
    "so the castless record is served instead of re-scraped forever");
Check(JavMetadataProvider.ShouldServeCached(
        new JavVideo { Code = "ZZZ-021", Title = "T", Actresses = ["Someone"] },
        withinThinGrace: false),
    "a record with cast is served normally");

// The counter only advances when a site actually answered: a round where
// every site timed out could not have produced cast, so it must not spend
// the budget.
var castlessCached = new JavVideo { Code = "ZZZ-020", Title = "T" };
Check(JavMetadataProvider.NextCastRetries(castlessCached, anySiteReached: true) == 1,
    "a cast repair counts when a site answered");
Check(JavMetadataProvider.NextCastRetries(castlessCached, anySiteReached: false) == 0,
    "a cast repair nobody answered does not spend the budget");
Check(JavMetadataProvider.NextCastRetries(
        new JavVideo { Code = "ZZZ-021", Title = "T", MaleActors = ["Someone"] },
        anySiteReached: true) == 0,
    "a record that already has cast never spends the budget");

// Wiring: the normal scan's queue decision reads that budget from the cached
// record, so the repair is bounded on disk and not just in memory.
const string castlessCode = "ZZZ-020";
var castlessItem = MakeMovie("ZZZ-020 A Cover Only Title", castlessCode, g: ["Drama"], s: ["Studio"], o: "Provider: JavOrganizer");
var castlessProbe = new JavVideo
{
    Code = castlessCode,
    Title = "ZZZ-020 A Cover Only Title",
    CoverUrl = "https://x/c.jpg",
    Genres = ["Drama"]
};
try
{
    JavCache.TryWrite(JavCodeParser.Normalize(castlessCode), castlessProbe, new StubLogger());
    Check(JavScanTrigger.NeedsScrape(castlessItem, castlessCode, wantEnglish: false),
        "an item whose cached scrape has no cast is queued again");

    castlessProbe.CastRetries = JavMetadataProvider.MaxCastRetries;
    JavCache.TryWrite(JavCodeParser.Normalize(castlessCode), castlessProbe, new StubLogger());
    Check(!JavScanTrigger.NeedsScrape(castlessItem, castlessCode, wantEnglish: false),
        "and is left alone once the repair budget is spent");

    // A fresh not-found marker means the sites were asked recently and had
    // nothing at all, so the repair waits for it to expire instead of making
    // every scan re-read the marker through a pointless refresh.
    castlessProbe.CastRetries = 0;
    JavCache.TryWrite(JavCodeParser.Normalize(castlessCode), castlessProbe, new StubLogger());
    JavCache.TryWriteNegative(JavCodeParser.Normalize(castlessCode), new StubLogger());
    Check(!JavScanTrigger.NeedsScrape(castlessItem, castlessCode, wantEnglish: false),
        "a fresh not-found marker holds the repair until it expires");
}
finally
{
    JavCache.Purge(JavCodeParser.Normalize(castlessCode));
}

Console.WriteLine("== Collections like scoring (regression: favorite overwritten by like) ==");
// A user who favorited AND liked a video contributes 3 + 2 = 5. The two
// signals used to be written one after the other, so the like overwrote the
// favorite and the video scored 2 — ranking it below videos only liked.
Check(JavCollectionsTask.LikeScore(true, true) == 5, "favorite + like sums to 5");
Check(JavCollectionsTask.LikeScore(true, false) == 3, "favorite alone scores 3");
Check(JavCollectionsTask.LikeScore(true, null) == 3, "favorite with unset like scores 3");
Check(JavCollectionsTask.LikeScore(false, true) == 2, "like alone scores 2");
Check(JavCollectionsTask.LikeScore(false, false) == 0, "neither scores 0");
Check(JavCollectionsTask.LikeScore(false, null) == 0, "unset like scores 0");

Console.WriteLine("== Collection retirement safety (regression: cards left pointing at nothing) ==");
// A retirement pass whose name check disagreed with what the server had
// stored deleted the performer collections the same run had just linked into
// the browse cards: the card logged "9 performers linked" while every link
// pointed at an item that no longer existed, so the actress wall opened empty.
var retireWantedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Actress: Iioka Kanako" };
var keeperId = Guid.NewGuid();
var retireWantedIds = new HashSet<Guid> { keeperId };
var knownPerformers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Inugami Ryou" };
Check(!JavCollectionsTask.ShouldRetireCollection("Actress: Iioka Kanako", keeperId, retireWantedNames, retireWantedIds, knownPerformers),
    "a collection this run linked is kept");
Check(!JavCollectionsTask.ShouldRetireCollection("Actress: Renamed On The Server", keeperId, retireWantedNames, retireWantedIds, knownPerformers),
    "an id this run rebuilt is kept even when the stored name differs");
Check(JavCollectionsTask.ShouldRetireCollection("Actress: Below The Minimum", Guid.NewGuid(), retireWantedNames, retireWantedIds, knownPerformers),
    "an untouched performer collection below the minimum is retired");
Check(JavCollectionsTask.ShouldRetireCollection("Inugami Ryou", Guid.NewGuid(), retireWantedNames, retireWantedIds, knownPerformers),
    "a prefix-less collection named after a credited person is retired");
Check(!JavCollectionsTask.ShouldRetireCollection("My Own Collection", Guid.NewGuid(), retireWantedNames, retireWantedIds, knownPerformers),
    "a collection that matches no credited name is never retired");

Console.WriteLine("== Performer name merging (regression: one actress, several cards) ==");
// The sites' own star ids decide which spellings mean one performer, so
// "Yua Mikami" and "Yua Mikami（三上悠亜）" share a card. Names that merely
// share a romanization but have different ids stay apart — "Aoi（葵）" and
// "Aoi（あおい）" is the case text similarity would get wrong.
var sameStarRecord = new JavVideo { Code = "AAA-001", Title = "t" };
sameStarRecord.CastStarIds["Yua Mikami"] = "uly";
sameStarRecord.CastStarIds["Yua Mikami（三上悠亜）"] = "uly";
var differentStarRecord = new JavVideo { Code = "AAA-002", Title = "t" };
differentStarRecord.CastStarIds["Aoi（葵）"] = "aoi1";
differentStarRecord.CastStarIds["Aoi（あおい）"] = "aoi2";
var mergeAliases = JavCache.BuildAliasIndex([sameStarRecord, differentStarRecord]);
Check(mergeAliases.TryGetValue("Yua Mikami（三上悠亜）", out var mergedName) && mergedName == "Yua Mikami",
    $"an alternate-script spelling merges onto the Latin name (got '{mergedName}')");
Check(!mergeAliases.ContainsKey("Yua Mikami"), "the kept spelling is not an alias of itself");
Check(!mergeAliases.ContainsKey("Aoi（葵）") && !mergeAliases.ContainsKey("Aoi（あおい）"),
    "different star ids are never merged on name similarity alone");
Check(JavCache.BuildAliasIndex([sameStarRecord]).Count == 1,
    "one merged pair produces exactly one alias");
Check(JavCache.BuildAliasIndex([differentStarRecord]).Count == 0,
    "records whose names have distinct star ids produce no aliases");

Console.WriteLine("== Collection group switches ==");
// Disabling a group must stop its per-performer collections: with the switch
// off the collections task skips that gender, and the retirement pass then
// removes the cards it used to build.
var sw = new PluginConfiguration
{
    BuildActressCollections = false,
    BuildActorCollections = true
};
Check(!JavCollectionsTask.IsPerformerGroupEnabled("female", sw), "actress switch off skips female performers");
Check(JavCollectionsTask.IsPerformerGroupEnabled("male", sw), "a switched-on actor group is still built");
Check(!JavCollectionsTask.IsPerformerGroupEnabled(string.Empty, sw), "a credit with no resolvable gender gets no collection");

Console.WriteLine("== Scrape-round reach isolation (regression: cross-code contamination) ==");
// Scraper instances are shared by every parallel worker. One code's
// successful fetch must not vouch for another code's failed scrape: the
// reached flag used to live on the instance, so a concurrent success made
// an all-failed round report "reached" and its miss was cached as a real
// "not found" for a week.
var reachScraper = new ReachTestScraper(new StubLogger());

reachScraper.Behavior = async ct => { await Task.Delay(30, ct); reachScraper.SimulatePageReceived(); return null; };
var markingRound = reachScraper.ScrapeWithReachAsync("AAA-111", CancellationToken.None);
reachScraper.Behavior = async ct => { await Task.Delay(60, ct); return null; };
var silentRound = reachScraper.ScrapeWithReachAsync("BBB-222", CancellationToken.None);
var concurrent = await Task.WhenAll(markingRound, silentRound);
Check(concurrent[0].SiteReached, "the round that received a page reports reached");
Check(!concurrent[1].SiteReached, "a concurrent round that received nothing stays unreached");

// The other direction: a round that finishes BEFORE another round's success
// must not inherit that later success when its result is read.
reachScraper.Behavior = async ct => { await Task.Delay(30, ct); return null; };
var earlyRound = reachScraper.ScrapeWithReachAsync("CCC-333", CancellationToken.None);
reachScraper.Behavior = async ct => { await Task.Delay(60, ct); reachScraper.SimulatePageReceived(); return null; };
var lateRound = reachScraper.ScrapeWithReachAsync("DDD-444", CancellationToken.None);
var earlyResult = await earlyRound;
var lateResult = await lateRound;
Check(!earlyResult.SiteReached, "a round finished before another's success stays unreached");
Check(lateResult.SiteReached, "the later round's own success is reported");

// Sequential rounds on the same instance start clean.
reachScraper.Behavior = ct => { reachScraper.SimulatePageReceived(); return Task.FromResult<JavVideo?>(null); };
var firstSequential = await reachScraper.ScrapeWithReachAsync("EEE-555", CancellationToken.None);
reachScraper.Behavior = ct => Task.FromResult<JavVideo?>(null);
var secondSequential = await reachScraper.ScrapeWithReachAsync("FFF-666", CancellationToken.None);
Check(firstSequential.SiteReached, "a reached sequential round reports reached");
Check(!secondSequential.SiteReached, "the next sequential round starts unreached");

Console.WriteLine("== Direct ban page defers to FlareSolverr (regression: English source locked out) ==");
// A direct request returning a ban-like page used to mark a six-hour ban
// before FlareSolverr ever tried — even though the browser can solve that
// page. That skipped JavLibrary (the primary English-title source) for hours
// and cached Japanese titles instead. With a solver configured the ban must
// be deferred to it; only without a solver is a direct ban page a hard ban.
var banOverrideVar = FlareSolverrUrls.UrlOverrideVariable;
var savedBanOverride = Environment.GetEnvironmentVariable(banOverrideVar);
try
{
    // Solver configured, pointed at a dead port so the solve fails fast and
    // offline: the direct ban page must not lock the site out.
    Environment.SetEnvironmentVariable(banOverrideVar, "http://127.0.0.1:1/v1");
    var withSolver = new BanTestScraper(new StubLogger());
    BanTestScraper.InjectBanPage(withSolver);
    await withSolver.ScrapeWithReachAsync("BAN-001", CancellationToken.None);
    Check(!withSolver.IsBanned, "a direct ban page with FlareSolverr configured does not lock the site out");

    // No solver: the direct ban page is all there is, so it is a real ban.
    Environment.SetEnvironmentVariable(banOverrideVar, null);
    var noSolver = new BanTestScraper(new StubLogger());
    BanTestScraper.InjectBanPage(noSolver);
    await noSolver.ScrapeWithReachAsync("BAN-002", CancellationToken.None);
    Check(noSolver.IsBanned, "a direct ban page without FlareSolverr backs off");
}
finally
{
    Environment.SetEnvironmentVariable(banOverrideVar, savedBanOverride);
}

Console.WriteLine("== Scan endpoints require an administrator (regression: any user could deep-scan) ==");
// Starting a deep re-scrape purges cached metadata and drives the server's
// traffic against the sites; cancelling interrupts a running pass. Both are
// admin operations, gated by the server's RequiresElevation policy — the
// same policy Jellyfin's own scheduled-task endpoints use.
var controllerPath = ManifestProbe.FindRepoFile("JavOrganizerController.cs");
Check(controllerPath is not null, "the controller source is reachable from the test run");
if (controllerPath is not null)
{
    var controllerText = File.ReadAllText(controllerPath);
    Check(controllerText.Contains("[Authorize(Policy = \"RequiresElevation\")]", StringComparison.Ordinal),
        "the controller requires the RequiresElevation (admin) policy");
}

Console.WriteLine("== Settings page exposes every configuration field (regression: a redesign drops a setting) ==");
// The page is hand-written HTML; a redesign can silently drop a field or its
// save mapping. Every configuration property must have a matching control id
// AND an assignment in the submit handler, so nothing can be lost in between.
var pagePath = ManifestProbe.FindRepoFile(Path.Combine("Configuration", "configPage.html"));
Check(pagePath is not null, "the configuration page is reachable from the test run");
if (pagePath is not null)
{
    var pageText = File.ReadAllText(pagePath);
    var properties = typeof(PluginConfiguration).GetProperties(
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    Check(properties.Length > 0, "the configuration type exposes properties to check");
    foreach (var property in properties)
    {
        Check(pageText.Contains($"id=\"{property.Name}\"", StringComparison.Ordinal),
            $"the page has a control for {property.Name}");
        Check(pageText.Contains($"config.{property.Name} =", StringComparison.Ordinal),
            $"the save handler writes {property.Name}");
    }
}

Console.WriteLine("== Cache stores compact JSON and round-trips ==");
// The cache is read back by machines, so indentation is pure overhead: it
// inflates every file and the IO to write/read it. Writes must be compact,
// must still deserialize to the same record, and must stay readable for
// records written before the change (the reader ignores whitespace).
var fmtSample = new JavVideo
{
    Code = "FMT-999",
    VideoId = "fmt999",
    Title = "FMT-999 A Sample Title",
    Maker = "Sample Studio",
    Genres = ["Drama", "Solo Work"],
    Actresses = ["Jane Doe", "John Roe"],
    PreviewUrls = ["https://example.com/1.jpg", "https://example.com/2.jpg"],
    PersonImageUrls = { ["Jane Doe"] = "https://example.com/j.jpg" }
};
var fmtCompact = System.Text.Json.JsonSerializer.Serialize(fmtSample, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
var fmtIndented = System.Text.Json.JsonSerializer.Serialize(fmtSample, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
var fmtCode = JavCodeParser.Normalize("FMT-999");
JavCache.TryWrite(fmtCode, fmtSample, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
var fmtFile = Path.Combine(JavCache.CacheDir, $"{fmtCode}.json");
var fmtOnDisk = File.ReadAllText(fmtFile);
Check(!fmtOnDisk.Contains('\n'), "the cache file is written as a single compact line");
Check(fmtOnDisk.Length == fmtCompact.Length, "the on-disk bytes match compact serialization");
var fmtBack = JavCache.TryRead(fmtCode, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
Check(fmtBack is not null && fmtBack.Title == fmtSample.Title && fmtBack.Actresses.Count == 2
    && fmtBack.PersonImageUrls["Jane Doe"] == "https://example.com/j.jpg",
    "a compact cache record round-trips to the same data");
Check(fmtIndented.Length > fmtCompact.Length, "indentation would have inflated the record");
Console.WriteLine($"  info compact {fmtCompact.Length} B vs indented {fmtIndented.Length} B " +
    $"({100.0 * (fmtIndented.Length - fmtCompact.Length) / fmtIndented.Length:F1}% smaller)");
JavCache.Purge(fmtCode);

Console.WriteLine("== Site health serializes camelCase for the settings page ==");
// The settings page reads s.enabled / s.name / s.reason. A record serializes
// PascalCase by default, which silently rendered every site as disabled in
// the live status panel on a real server.
var health = new JavScraperFactory.SiteHealth("javdb", "JavDB", true, 20, true, null, null);
var healthJson = System.Text.Json.JsonSerializer.Serialize(health);
Check(healthJson.Contains("\"enabled\":true") && healthJson.Contains("\"name\":\"JavDB\""),
    "site health uses camelCase field names");
Check(!healthJson.Contains("\"Enabled\"") && !healthJson.Contains("\"Name\""),
    "site health does not leak PascalCase names the page cannot read");

Console.WriteLine($"\n{pass} passed, {fail} failed");



return fail == 0 ? 0 : 1;

/// <summary>Minimal logger stub so scrapers can be constructed off-server.</summary>
internal sealed class StubLogger : Microsoft.Extensions.Logging.ILogger<GenericSiteScraper>
{
    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}




/// <summary>Exposes private JavBus padded-keyword logic for the test suite.</summary>
internal static class JavBusScraper_Tests
{
    internal static string? ToPaddedKeywordPublic(string code)
    {
        var method = typeof(JavBusScraper).GetMethod("ToPaddedKeyword", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return method?.Invoke(null, [code]) as string;
    }
}

/// <summary>Exposes the private session-profile field for coherence tests.</summary>
internal static class SiteScraper_Tests
{
    internal static (string UserAgent, string? SecChUa, string? SecChUaPlatform, string AcceptLanguage)? SessionProfileOf(SiteScraper scraper)
    {
        var field = typeof(SiteScraper).GetField("_sessionProfile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(scraper) as (string, string?, string?, string)?;
    }
}

/// <summary>Exposes the URL normalization logic for /v1 regression tests.</summary>
internal static class FlareSolverrUrls_Tests
{
    internal static string? NormalizeForTest(string configured)
    {
        var method = typeof(FlareSolverrUrls).GetMethod("NormalizeApiUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return method?.Invoke(null, [configured]) as string;
    }
}

/// <summary>Checks the jav.guru configuration toggle is wired up.</summary>
internal static class PluginConfiguration_Probe
{
    /// <summary>
    /// Reports whether the configuration exposes a jav.guru toggle that the
    /// site catalog reads.
    /// </summary>
    /// <returns><c>true</c> when the toggle exists and defaults to on.</returns>
    internal static bool HasJavGuruToggle()
    {
        var property = typeof(PluginConfiguration).GetProperty("UseJavGuru");
        if (property is null || property.PropertyType != typeof(bool))
        {
            return false;
        }

        return new PluginConfiguration().UseJavGuru;
    }
}

/// <summary>
/// A scraper whose network behavior is scripted, so the reach-round
/// isolation can be exercised without touching the network.
/// </summary>
internal sealed class ReachTestScraper : SiteScraper
{
    public ReachTestScraper(Microsoft.Extensions.Logging.ILogger logger)
        : base(logger, "https://example.invalid/")
    {
    }

    /// <summary>Gets or sets the scripted behavior of each scrape call.</summary>
    public Func<CancellationToken, Task<JavVideo?>> Behavior { get; set; } = _ => Task.FromResult<JavVideo?>(null);

    protected override string SiteName => "ReachTest";

    public override Task<JavVideo?> FindByCodeAsync(string code, CancellationToken ct) => Behavior(ct);

    /// <summary>Simulates the site answering a page load in the current round.</summary>
    public void SimulatePageReceived() => MarkSiteReached();
}

/// <summary>
/// A scraper that runs the real fetch flow against a scripted ban page, so
/// the direct-ban deferral policy can be exercised without the network.
/// </summary>
internal sealed class BanTestScraper : SiteScraper
{
    public BanTestScraper(Microsoft.Extensions.Logging.ILogger logger)
        : base(logger, "https://example.invalid/")
    {
    }

    protected override string SiteName => "BanTest";

    public override async Task<JavVideo?> FindByCodeAsync(string code, CancellationToken ct)
    {
        await GetHtmlAsync("/video", ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>Replaces the HTTP client with one that always answers a ban page.</summary>
    public static void InjectBanPage(SiteScraper scraper)
        => HtmlInjector.SetPage(scraper, "<html><body>Your IP address has been banned from this site.</body></html>");
}

/// <summary>
/// Swaps a scraper's HTTP client for one that answers every request with a
/// fixed page, so fetch-path behavior (ban pages, age gates) can be exercised
/// without touching the network.
/// </summary>
internal static class HtmlInjector
{
    /// <summary>Replaces the scraper's HTTP client with a scripted-response one.</summary>
    /// <param name="scraper">Scraper whose client is replaced.</param>
    /// <param name="html">HTML the scripted client returns for every request.</param>
    /// <returns>The handler, so the test can assert on the request it received.</returns>
    internal static ScriptedHtmlHandler SetPage(SiteScraper scraper, string html)
    {
        var field = typeof(SiteScraper).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("SiteScraper._http not found; the scripted client cannot be injected.");
        var previous = (HttpClient?)field.GetValue(scraper);
        var handler = new ScriptedHtmlHandler(html);
        field.SetValue(scraper, new HttpClient(handler));
        previous?.Dispose();
        return handler;
    }

    /// <summary>Answers every request with one fixed page.</summary>
    internal sealed class ScriptedHtmlHandler(string html) : HttpMessageHandler
    {
        /// <summary>Cookie header of the most recent request, for header assertions.</summary>
        internal string? LastRequestCookie { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : null;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            });
        }
    }
}

/// <summary>
/// Locates the repository's manifest.json from the test binary's location, so
/// the manifest-shape checks run against the real file rather than a copy.
/// </summary>
internal static class ManifestProbe
{
    /// <summary>
    /// Walks up from the executing assembly until it finds manifest.json.
    /// </summary>
    /// <returns>The absolute path, or <c>null</c> when not found.</returns>
    internal static string? FindManifest() => FindRepoFile("manifest.json");

    /// <summary>
    /// Walks up from the executing assembly until it finds the named file
    /// at the repository root.
    /// </summary>
    /// <param name="fileName">The repository-root file to locate.</param>
    /// <returns>The absolute path, or <c>null</c> when not found.</returns>
    internal static string? FindRepoFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
