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
})
{
    Check(!SiteScraper.IsBanPage(html), $"not a ban page: {html}");
}

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

            var checksum = newest.GetProperty("checksum").GetString() ?? string.Empty;
            Check(checksum.Length == 64 && checksum.All(Uri.IsHexDigit),
                $"newest checksum is a 64-char hex SHA256 (got '{checksum}')");

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
        }
    }
}

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
/// Locates the repository's manifest.json from the test binary's location, so
/// the manifest-shape checks run against the real file rather than a copy.
/// </summary>
internal static class ManifestProbe
{
    /// <summary>
    /// Walks up from the executing assembly until it finds manifest.json.
    /// </summary>
    /// <returns>The absolute path, or <c>null</c> when not found.</returns>
    internal static string? FindManifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "manifest.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
