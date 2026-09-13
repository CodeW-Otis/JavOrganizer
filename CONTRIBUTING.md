<div align="center">
  <img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" width="100" alt="JavOrganizer Logo" />
  <h1>Contributing to JavOrganizer</h1>
  <p><em>We'd love your help making JavOrganizer better!</em></p>
</div>

> [!NOTE]
> Thanks for considering contributing! This guide covers the basics of how to jump in.

## 🤝 Ways to contribute

| Contribution | Description |
| :---: | :--- |
| 🐛 **Report bugs** | Site scraper broke, metadata is off, or the plugin crashed? Open an issue. Include your Jellyfin version, plugin version, file name, and debug logs. |
| 🌐 **Fix a broken site scraper** | Site markup changes a lot. Sites are defined declaratively in `JavSiteCatalog.cs`, so fixing one usually just means updating a selector string. |
| ⚡ **Add a new site** | Check out "Adding a site" below to add a new metadata source. |
| 📝 **Improve documentation** | Found a typo or something confusing? Docs PRs are always welcome. |
| 🧪 **Extend the test suite** | Add to `tools/SmokeTest` to help us catch issues without hitting live sites. |

## 💻 Development setup

### Prerequisites
- Git
- The .NET SDK matching the Jellyfin version you're targeting (see the table below).
- Your favorite C# editor (Visual Studio, VS Code, Rider, etc.)

### Build and Test

1. Clone the repository.
2. Build and run the offline test suite:

```powershell
.\build.ps1                                       # builds for every Jellyfin version into dist\
dotnet run --project tools/SmokeTest -c Release   # runs the offline smoke tests
```

| Jellyfin version | .NET SDK | Target framework |
|---|---|---|
| 10.8 | SDK 6 | net6.0 |
| 10.9 / 10.10 | SDK 8 | net8.0 |
| 10.11 | SDK 9 | net9.0 |
| 12.0 | SDK 10 | net10.0 |

## ⚡ Adding a site

Most sites can be added without writing any custom scraper logic. 

### Workflow
1. **Define the Site**: Add a new `SiteDefinition` to `JavSiteCatalog.cs`.
2. **Add Toggle**: Add a `UseMySite` property to `PluginConfiguration.cs` (set it to true by default in the constructor).
3. **Wire Toggle**: Check your toggle in `JavSiteCatalog.IsEnabled`.
4. **Update UI**: Add the toggle checkbox to `Configuration/configPage.html`.
5. **Add Test**: Add a test case to `tools/SmokeTest/Program.cs` with a snippet of the site's HTML.

<details>
<summary><strong>View SiteDefinition Example</strong></summary>

```csharp
new SiteDefinition
{
    Key = "mysite",
    DisplayName = "MySite",
    BaseUrl = "https://mysite.example/",
    Mode = SiteUrlMode.Canonical,
    Priority = 100,
    Cookie = "age_ok=1",
    CanonicalPath = code => code,
    Selectors = new SiteSelectors
    {
        Title = "//h1",
        Date = "//time",
        Genres = "//a[contains(@href,'/tag/')]",
        Cover = "//meta[@property='og:image']",
    }
}
```
</details>

> [!TIP]
> **A few tips on selectors:**
> - Try to use `og:image` or `og:title` meta tags if they exist — they don't break as often when a site redesigns.
> - Make sure your selector won't accidentally grab an error page title (like "404 Not Found").
> - Everything is optional. If you can only scrape partial data, that's fine. The plugin merges data across sites.

## 🧪 Testing your changes

> [!WARNING]
> Please double-check that you aren't committing any personal info or API keys!

- `dotnet run --project tools/SmokeTest -c Release` should pass with **0 failures**.
- `.\build.ps1` should run cleanly with **0 warnings and 0 errors** across all targets.
- If you're updating a scraper, it helps to test against a live server if you can.

## ✅ Pull request checklist

> **Quick checklist before you open a PR:**
> 
> - [ ] `.\build.ps1` runs without warnings
> - [ ] `tools/SmokeTest` passes
> - [ ] New sites have a config toggle, config UI checkbox, and test
> - [ ] `README.md` is updated (if you added or changed a site)
> - [ ] Diff is free of personal data

## 📐 Code style

- Use file-scoped namespaces.
- Stick to the latest C# features supported by the target framework.
- Add XML doc comments to public members.
- If you're changing how we make requests, make sure we still aren't hammering sites (keep rate limits/anti-ban in mind).

> [!IMPORTANT]
> **Legal note**: JavOrganizer is just a metadata scraper. It's not affiliated with Jellyfin or any of the sites it scrapes, and it doesn't host content. Please use it responsibly and follow local laws and site terms of service.

---

<div align="center">
  <a href="README.md">README</a> •
  <a href="CODE_OF_CONDUCT.md">Code of Conduct</a> •
  <a href="SECURITY.md">Security Policy</a>
</div>
