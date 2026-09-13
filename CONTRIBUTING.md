# Contributing to JavOrganizer

Thanks for your interest in improving JavOrganizer! This guide covers everything you need to know.

## Ways to contribute

- 🐛 **Report bugs** — broken site scrapers, wrong metadata, crashes. Open an issue with your Jellyfin version, the plugin version, the affected file name and (Debug-level) log lines.
- 🌐 **Fix a broken site scraper** — sites change their markup constantly. The generic sites are defined declaratively in `JavSiteCatalog.cs`; most fixes are a one-line selector change.
- ⚡ **Add a new site** — see "Adding a site" below.
- 📝 **Improve documentation** — typo fixes and clarifications are always welcome.
- 🧪 **Extend the test suite** — `tools/SmokeTest` runs offline against captured page markup.

## Development setup

1. Clone the repository.
2. Install the .NET SDK matching the Jellyfin line you target (see the table below).
3. Build and run the offline test suite:

```powershell
.\build.ps1                          # build every Jellyfin line into dist\
dotnet run --project tools/SmokeTest -c Release   # 80-check offline suite
```

| Jellyfin line | .NET SDK | Target framework |
|---|---|---|
| 10.8 | SDK 6 | net6.0 |
| 10.9 / 10.10 | SDK 8 | net8.0 |
| 10.11 | SDK 9 | net9.0 |
| 12.0 | SDK 10 | net10.0 |

## Adding a site

Almost every site can be added **without writing a scraper class**. Open
`JavSiteCatalog.cs` and append a `SiteDefinition`:

```csharp
new SiteDefinition
{
    Key = "mysite",                          // unique; used for the config toggle
    DisplayName = "MySite",
    BaseUrl = "https://mysite.example/",
    Mode = SiteUrlMode.Canonical,             // or Search for ?s=CODE-style sites
    Priority = 100,                           // lower = scraped first
    Cookie = "age_ok=1",                     // optional age-gate cookie
    CanonicalPath = code => code,             // how the URL is built from the code
    Selectors = new SiteSelectors
    {
        Title = "//h1",
        Date = "//time",                      // plus DateAttribute = "datetime"
        Genres = "//a[contains(@href,'/tag/')]",
        Cover = "//meta[@property='og:image']", // og:image is the safe default
        // ...see SiteSelectors for every supported field
    }
}
```

Then:

1. Add a `UseMySite` toggle to `PluginConfiguration.cs` (default it in the constructor).
2. Wire the toggle in `JavSiteCatalog.IsEnabled`.
3. Add the checkbox to `Configuration/configPage.html`.
4. Add a test case to `tools/SmokeTest/Program.cs` using a sanitized snippet of the site's real markup.

**Ground rules for selectors:**

- Prefer `og:image` / `og:title` meta tags — they survive theme redesigns.
- Never add a selector that could match an error page's title (see `SiteScraper.IsErrorTitle` — error pages must never parse into records).
- Every field is optional; partial data is fine, the merge fills gaps from other sites.

## Testing your changes

- `dotnet run --project tools/SmokeTest -c Release` must pass with **0 failures**.
- `.\build.ps1` must produce **0 warnings, 0 errors** on all five targets.
- For scraper changes, verify against a **live server** if you can: install the matching build, add a test file named `ABC-123.mp4` to a movies library with JavOrganizer as the sole metadata fetcher, and confirm the item fills correctly.
- Never commit personal data: library file names, user names, real API tokens, or captured pages containing them.

## Pull request checklist

- [ ] `.\build.ps1` succeeds on all targets with 0 warnings
- [ ] `tools/SmokeTest` passes 100%
- [ ] New sites have a config toggle + config-page checkbox + test
- [ ] `README.md` site table updated (if you added/changed a site)
- [ ] No personal data in the diff

## Code style

- File-scoped namespaces, latest C# for the target line (mind .NET 6 / C# 10 limits — no `required` members).
- XML doc comments on public members (the project ships a doc XML).
- Every network-touching change must keep the anti-ban guarantees: per-site pacing, jitter, backoff and circuit breaking.

## Legal note

JavOrganizer is a metadata scraper. It is not affiliated with Jellyfin or any
scraped website, and it hosts no content. Use it in compliance with your local
laws and each site's terms of service.
