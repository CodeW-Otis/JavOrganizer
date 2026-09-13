<div align="center">
  <img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" width="100" alt="JavOrganizer Logo" />
  <h1>Contributing to JavOrganizer</h1>
  <p><em>We'd love your help making JavOrganizer better!</em></p>
</div>

> [!NOTE]
> Thanks for your interest in improving JavOrganizer! This guide covers everything you need to know to get started.

## 🤝 Ways to contribute

| Contribution | Description |
| :---: | :--- |
| 🐛 **Report bugs** | Broken site scrapers, wrong metadata, crashes. Open an issue with your Jellyfin version, the plugin version, the affected file name and (Debug-level) log lines. |
| 🌐 **Fix a broken site scraper** | Sites change their markup constantly. Generic sites are defined declaratively in `JavSiteCatalog.cs`; most fixes are a one-line selector change. |
| ⚡ **Add a new site** | See "Adding a site" below to add a new metadata source. |
| 📝 **Improve documentation** | Typo fixes and clarifications are always welcome. |
| 🧪 **Extend the test suite** | `tools/SmokeTest` runs offline against captured page markup. |

## 💻 Development setup

### Prerequisites
- Git installed on your system.
- The .NET SDK matching the Jellyfin line you target (see the table below).
- A code editor like Visual Studio, VS Code, or Rider.

### Build and Test

1. Clone the repository.
2. Build and run the offline test suite:

```powershell
.\build.ps1                                       # build every Jellyfin line into dist\
dotnet run --project tools/SmokeTest -c Release   # 80-check offline suite
```

| Jellyfin line | .NET SDK | Target framework |
|---|---|---|
| 10.8 | SDK 6 | net6.0 |
| 10.9 / 10.10 | SDK 8 | net8.0 |
| 10.11 | SDK 9 | net9.0 |
| 12.0 | SDK 10 | net10.0 |

## ⚡ Adding a site

Almost every site can be added **without writing a scraper class**. 

### Workflow
1. **Define the Site**: Open `JavSiteCatalog.cs` and append a `SiteDefinition`.
2. **Add Toggle**: Add a `UseMySite` toggle to `PluginConfiguration.cs` (default it in the constructor).
3. **Wire Toggle**: Wire the toggle in `JavSiteCatalog.IsEnabled`.
4. **Update UI**: Add the checkbox to `Configuration/configPage.html`.
5. **Add Test**: Add a test case to `tools/SmokeTest/Program.cs` using a sanitized snippet of the site's real markup.

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
> **Ground rules for selectors:**
> - Prefer `og:image` / `og:title` meta tags — they survive theme redesigns.
> - Never add a selector that could match an error page's title.
> - Every field is optional; partial data is fine, the merge fills gaps from other sites.

## 🧪 Testing your changes

> [!WARNING]
> Never commit personal data!

- `dotnet run --project tools/SmokeTest -c Release` must pass with **0 failures**.
- `.\build.ps1` must produce **0 warnings, 0 errors** on all targets.
- For scraper changes, verify against a **live server** if you can.

## ✅ Pull request checklist

> **Before submitting a PR, please make sure you've completed the following:**
> 
> - [ ] `.\build.ps1` succeeds on all targets with 0 warnings
> - [ ] `tools/SmokeTest` passes 100%
> - [ ] New sites have a config toggle + config-page checkbox + test
> - [ ] `README.md` site table updated (if you added/changed a site)
> - [ ] No personal data in the diff

## 📐 Code style

- File-scoped namespaces, latest C# for the target line.
- XML doc comments on public members.
- Every network-touching change must keep the anti-ban guarantees.

> [!IMPORTANT]
> **Legal note**: JavOrganizer is a metadata scraper. It is not affiliated with Jellyfin or any scraped website, and it hosts no content. Use it in compliance with your local laws and each site's terms of service.

---

<div align="center">
  <a href="README.md">README</a> •
  <a href="CODE_OF_CONDUCT.md">Code of Conduct</a> •
  <a href="SECURITY.md">Security Policy</a>
</div>
