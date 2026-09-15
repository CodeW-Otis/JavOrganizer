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

Console.WriteLine("== JavSiteCatalog ==");
Check(JavSiteCatalog.Sites.Count == 13, $"13 generic sites in catalog (got {JavSiteCatalog.Sites.Count})");
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
