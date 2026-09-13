<div align="center">

<img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" alt="JavOrganizer Logo" width="200">

# JavOrganizer

**🎬 JAV metadata plugin for Jellyfin. Scrapes up to 18 sites concurrently with built-in rate limiting and retries.**

<br>

[![Build](https://github.com/CodeW-Otis/JavOrganizer/actions/workflows/build.yml/badge.svg)](https://github.com/CodeW-Otis/JavOrganizer/actions/workflows/build.yml)
[![Release](https://img.shields.io/badge/Release-v1.3.1-blue.svg)](https://github.com/CodeW-Otis/JavOrganizer/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Jellyfin 10.8–12.0](https://img.shields.io/badge/Jellyfin-10.8%20%7C%2010.9%20%7C%2010.10%20%7C%2010.11%20%7C%2012.0-00a4dc.svg)](#build-matrix--pick-the-build-matching-your-server)
[![.NET 6–10](https://img.shields.io/badge/.NET-6%20%7C%208%20%7C%209%20%7C%2010-512bd4.svg)](#build-matrix--pick-the-build-matching-your-server)
[![Stars](https://img.shields.io/github/stars/CodeW-Otis/JavOrganizer.svg?style=social)](https://github.com/CodeW-Otis/JavOrganizer/stargazers)

<br>

### ⚡ Quick install — copy this into Jellyfin:

```
https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/manifest.json
```

> 📋 **Click the copy button** (top-right of the box above) then paste into **Dashboard → Plugins → Repositories → ➕**

**[→ Full install guide below](#-installation)** · [Quick Start (5 min)](QUICKSTART.md) · [The 18 sites](#the-18-sites) · [Configuration](#configuration) · [Troubleshooting](#troubleshooting)

**⭐ If you find this plugin helpful, please star the repo!**

</div>

---

### ✨ Features

<table>
  <tr>
    <td align="center" width="33%">🌐 <strong>Multiple Sources</strong><br>Scrapes sites concurrently and merges the results</td>
    <td align="center" width="33%">🛡️ <strong>Anti-Ban Measures</strong><br>Browser fingerprinting, jitter, and backoffs</td>
    <td align="center" width="33%">🌍 <strong>English First</strong><br>Prioritizes Latin-script titles over Japanese variants</td>
  </tr>
  <tr>
    <td align="center">🖼️ <strong>Covers & Backdrops</strong><br>Downloads cover art and scene backdrops directly from CDNs</td>
    <td align="center">👥 <strong>Cast & Crew</strong><br>Separates actors/actresses and builds collections</td>
    <td align="center">☁️ <strong>Cloudflare Support</strong><br>Optionally manages FlareSolverr to handle challenges</td>
  </tr>
  <tr>
    <td align="center">⏰ <strong>Scanning Options</strong><br>Supports startup, scheduled, and on-demand scans</td>
    <td align="center">💾 <strong>Local Cache</strong><br>Caches metadata with a 30-day TTL to save requests</td>
    <td align="center">🔧 <strong>Jellyfin 10.8–12.0</strong><br>Version-specific builds for compatibility</td>
  </tr>
</table>

JavOrganizer identifies Japanese adult video files by looking for the product code in the file name (e.g., `SSIS-406.mp4`). It can scrape up to 18 websites at the same time for each code, including JavLibrary, JavDB, JavBus, MissAV, OneJAV, and FANZA. It then merges the data from these sites into Jellyfin.

| What you get | How it works |
|---|---|
| 📝 **English titles** | The plugin prioritizes Latin-script text when merging data, so you get English titles when available. |
| 👥 **Cast members** | Separates actresses and actors using data from sites like JavDB and MissAV, preferring romaji names. |
| 🖼️ **Cover art & backdrops** | Downloads images directly using appropriate Referer headers so CDNs don't block the request. |
| 🏷️ **Metadata** | Fills in studios, genres, release dates, and runtimes from whichever site has the info. |
| 📚 **Collections** | Automatically groups videos by actress/actor, and generates "Most Viewed" or "Most Liked" lists. |

To handle sites protected by Cloudflare, you can run [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr). The plugin can manage the FlareSolverr process for you, starting and stopping it along with your Jellyfin server.

It supports Jellyfin 10.8 through 12.0, with a dedicated build for each version line.

> [!NOTE]
> **Tested on a live server:** We test these features on a real Jellyfin 12.0.0 instance (Windows, 1200+ items, using FlareSolverr). This covers loading, auto-scanning, API usage, multi-site merging, and scheduled tasks. See [Verified on a live server](#verified-on-a-live-server) for details.

---

## 📖 Contents

<details>
<summary><strong>Click to expand full table of contents</strong></summary>

1. [Fixing missing metadata issues](#fixing-missing-metadata-issues)
2. [How it works](#how-it-works)
3. [The 18 sites](#the-18-sites)
4. [Rate limiting and scraping behavior](#rate-limiting-and-scraping-behavior)
5. [Scanning: automatic and manual](#scanning-automatic-and-manual)
6. [Filename conventions](#filename-conventions)
7. [🚀 Installation](#-installation)
8. [☁️ Setting up FlareSolverr](#️-setting-up-flaresolverr)
9. [📂 Library setup](#-library-setup)
10. [Configuration](#configuration)
11. [☁️ FlareSolverr lifecycle (auto start/stop)](#️-flaresolverr-lifecycle-auto-startstop)
12. [🖼️ Cover art and backdrops](#️-cover-art-and-backdrops)
13. [How metadata is cached](#how-metadata-is-cached)
14. [Collections](#collections)
15. [Automatic cache cleanup](#automatic-cache-cleanup)
16. [Building from source and running tests](#building-from-source-and-running-tests)
17. [Project layout](#project-layout)
18. [Troubleshooting](#troubleshooting)
19. [Compatibility notes](#compatibility-notes)
20. [Verified on a live server](#verified-on-a-live-server)
21. [Changelog](#changelog)

</details>

---

## 🔍 Fixing missing metadata issues

If you're upgrading or noticing that some videos only show a filename or a Japanese title, you might have run into issues from older versions. Here's what we fixed recently:

1. **Scans now run regularly.** The auto-scan used to run just once at startup. If a file was added later or a scrape failed, it wouldn't retry until you restarted Jellyfin. Now, a scheduled task runs every 6 hours.
2. **Partial data gets repaired.** We now re-scrape items if they got stuck with just a title and no cover art. Thin cache records are automatically re-fetched (throttled to every 6 hours).
3. **Better code extraction.** We look at the item name too, not just the file path.
4. **No more bogus IDs.** We fixed a bug where CSS version strings (like `?v=9.3`) were accidentally treated as provider IDs on JavBus.
5. **Ignoring error pages.** CDN error pages (like "403 ERROR") are now ignored instead of being parsed as actual video titles.
6. **Prioritizing English titles.** We tweaked the logic so FlareSolverr doesn't throw away successful JavLibrary pages (which have English titles) due to false-positive Cloudflare detection.

> [!IMPORTANT]
> **Upgrading from 1.2.0?** Head to the plugin settings and click **Deep Re-scrape Everything**. This will safely re-scrape your library through all available sites and fill in any missing data.

## ⚙️ How it works

```
filename ──► JavCodeParser.ExtractCode ──► "ABP-123"
                                               │
                    ┌──────────────────────────┴─────────────────────────┐
                    ▼                                                      ▼
              1. Local Cache (recent? use it, done)                 Thin record
                                               │ missing                older than 6h?
                                               ▼                             │ re-scrape
              2. Negative cache (recent miss? skip)                          ▼
                                               │ miss
                                               ▼
        Requests up to MaxSitesPerScrape sites concurrently.
        Skips unreachable sites and tries the next one:
                                               │
             ┌────────────┬────────────┬──────┴─────┬──────────────┐
             ▼            ▼            ▼            ▼              ▼
        JavLibrary     JavDB        JavBus    MissAV family   14 catalog sites
        (baseline)   (genders)   (fast, direct) (genders)   (OneJAV, FANZA ×2,
                                               JavLand, 123AV, SEXTB, SupJav,
                                               JavGG, JavSeen, JavMix, JavQuick,
                                               JavTube, MGStage)
             │            │            │            │              │
             └────────────┴─────┬──────┴────────────┴──────────────┘
                                ▼
                    merge ──► JavVideo (prioritizes richer data)
                                │
              Cache.Write (30-day TTL) ──► Jellyfin item:
                Title, Overview, Year, PremiereDate, Runtime,
                Studio, Genres, Cast, Cover and Backdrops
```

## 🌐 The 18 sites

When scanning a video, the plugin queries multiple enabled sites concurrently. The **Max Sites Scraped at Once** setting limits how many requests happen at once. If a site is down or blocks the request, it skips it and tries the next one on the list.

| # | Site | Priority | What it provides | Default |
|---|---|---|---|---|
| 1 | JavLibrary | 10 | Good baseline for titles, genres, and previews | always on |
| 2 | JavDB | 20 | Cast and tags | on |
| 3 | JavBus | 30 | Fast pages, rarely blocks | on |
| 4 | MissAV | 40 | Labeled cast | on |
| 5 | MissAV mirror | 41 | Backup domain | on |
| 6 | OneJAV | 50 | Covers, dates, cast | on |
| 7 | FANZA (digital) | 60 | Rich catalog data | on |
| 8 | FANZA DVD | 61 | Separate DVD catalog | on |
| 9 | JavLand | 70 | Direct pages | on |
| 10 | 123AV | 75 | Cast metadata | on |
| 11 | SEXTB | 80 | Streaming pages | on |
| 12-17 | WordPress sites | 90 | SupJav, JavGG, JavSeen, JavMix, JavQuick, JavTube | on |
| 18 | MGStage | 150 | Amateur labels | on |

You can toggle each site on or off in the plugin settings.

**Suggested setups:**

- **Standard (default):** Leave all sites on with a cap of 8–10. The load is distributed nicely.
- **Lightweight:** Turn on only JavLibrary, JavBus, and OneJAV. Set the cap to 3, with a 1000ms delay.
- **Handling bans:** If you notice a site is blocking your IP, just toggle it off and let the others fill the gaps.

## 🛡️ Rate limiting and scraping behavior

To avoid hammering sites and getting banned, the plugin uses a few strategies:

1. **Browser profiles:** It rotates through different User-Agent strings and related headers (Chrome, Edge, Firefox, Safari) so traffic looks a bit more normal.
2. **Jittered pacing:** Requests to the same site are paced out with a random delay (60–140% of your configured delay setting).
3. **Exponential backoff:** If a site returns a 429 or 503 error, the plugin backs off before retrying.
4. **Cooldowns:** If a site sends a clear ban or block page, the plugin skips that site for 6 hours. If a site is totally unreachable, it skips it for 30–60 minutes.
5. **FlareSolverr reuse:** If FlareSolverr solves a Cloudflare check, the plugin reuses that session's cookie and User-Agent for subsequent requests to speed things up.

## 🔄 Scanning: automatic and manual

**Automatic:**

- **On startup:** Runs a pass over items missing metadata a few seconds after Jellyfin boots.
- **Scheduled task:** A task named *Scan JavOrganizer Library* runs every 6 hours to catch newly added files or retry failures.
- **Standard library scans:** When Jellyfin does its normal scanning, JavOrganizer acts as a metadata provider.

**Manual (in plugin settings):**

- **Scan Library Now** — Does a quick pass over items that are missing data or only have a title.
- **Deep Re-scrape Everything** — Forces a full scrape for every video, clearing the old cache as it goes. Good for major updates.
- **Cancel Scan** — Stops any currently running scan.

## 📁 Filename conventions

The parser expects standard product codes like **2-6 letters, an optional hyphen, and 2-5 digits**.

| File name | Extracted code |
|---|---|
| `[ABC-123] Some Title.mp4` | `ABC-123` |
| `abp982.mp4` | `ABP-982` |
| `SSIS-406.mp4` | `SSIS-406` |
| `IPX-1234_uncensored.mp4` | `IPX-1234` |
| `Movie.FHD1080.ABP-123.mp4` | `ABP-123` (skips noise words) |

It intentionally ignores FC2 titles, and skips common noise words (FHD, UHD, HEVC, MP4, BLURAY, CD, EP). Zero-padded numbers are normalized (e.g., `abp-00123` becomes `ABP-123`).

## 🚀 Installation

### Method 1 — Jellyfin Catalog (recommended)

**Copy this manifest URL:**

```
https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/manifest.json
```

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**.
2. Click **➕**, paste the URL, and click **Save**.
3. Go to **Catalog**, find **JavOrganizer**, and install the version that matches your Jellyfin server.
4. **Restart Jellyfin**.

> [!TIP]
> **Not sure which version you have?** Check the bottom of the dashboard sidebar. Pick the plugin build that matches your server version.

### Method 2 — Manual download

Download the zip for your Jellyfin version from the [Releases page](https://github.com/CodeW-Otis/JavOrganizer/releases/latest).

1. Extract the contents into your Jellyfin plugins folder:
   - **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\JavOrganizer\`
   - **Linux**: `/var/lib/jellyfin/plugins/JavOrganizer/`
   - **Docker**: Mount into `/config/plugins/JavOrganizer/`
2. Ensure the folder contains the DLLs and `.json` files.
3. Restart Jellyfin.

> ☁️ **Next up: [Setting up FlareSolverr](#️-setting-up-flaresolverr)**. It's optional but highly recommended to access sites behind Cloudflare.

---

## ☁️ Setting up FlareSolverr

> **Note:** FlareSolverr is a separate tool and is not included in the plugin. You don't strictly need it, but without it, you might miss out on metadata from sites like JavLibrary and JavDB.

### What it is

FlareSolverr runs a headless browser to solve Cloudflare challenges. When the plugin hits a roadblock, it asks FlareSolverr to solve it, and then reuses the clearance cookie for future requests.

### Step 1 — Install FlareSolverr

**Windows (Standalone):**

1. Download the latest `flaresolverr_windows_x64.zip` from [their releases](https://github.com/FlareSolverr/FlareSolverr/releases).
2. Extract it somewhere (e.g., `C:\Users\<you>\AppData\Local\FlareSolverr\`).
3. **Don't run it.** The plugin can start it for you.

**Docker:**

```bash
docker run -d \
  --name flaresolverr \
  -p 8191:8191 \
  -e LOG_LEVEL=info \
  --restart unless-stopped \
  ghcr.io/flaresolverr/flaresolverr:latest
```

### Step 2 — Configure the plugin

Go to **Dashboard → Plugins → JavOrganizer** and fill in:

| Field | Value |
|---|---|
| **FlareSolverr URL** | `http://localhost:8191/v1` (or your Docker host IP) |
| **FlareSolverr Executable Path** | (Windows only) The full path to `flaresolverr.exe`. Leave empty if using Docker. |

Save your changes.

### Step 3 — How it works

When Jellyfin starts, the plugin launches FlareSolverr (if you provided the executable path). When it needs to bypass Cloudflare, it uses it transparently. When Jellyfin shuts down, it kills the FlareSolverr process.

You can verify it works by looking at your Debug logs for:
`"JavLibrary": FlareSolverr fetched '…' and granted direct access`

## 📂 Library setup

1. Create a library in Jellyfin with the content type set to **Movies**.
2. In **Library settings → Metadata**, make **JavOrganizer** the top and only enabled downloader.
3. For **Images**, let JavOrganizer handle the Primary and Backdrop images.
4. Scan the library.

## ⚙️ Configuration

Available in **Dashboard → Plugins → JavOrganizer**. Settings apply immediately.

| Setting | Description |
|---|---|
| **Language** | Preferred language for metadata (`en`, `ja`, `zh`, `tw`). |
| **Max Sites Scraped at Once** | Limits parallel requests per video. |
| **Parallel Scrapes** | How many videos to process simultaneously. |
| **Delay Between Requests** | Time to wait between requests to the same site (in ms). |
| **Successful-scrape cache** | How long to keep data before re-scraping (default 30 days). |
| **Not-found cache** | How long to remember a failed lookup so we don't spam the sites (default 7 days). |
| **Build collections** | Automatically create collections for cast, studios, genres, etc. |

## ☁️ FlareSolverr lifecycle (auto start/stop)

> [!IMPORTANT]
> If you want the plugin to manage FlareSolverr for you (Windows only), make sure you fill out both the **URL** and the **Executable Path** in the plugin settings. If you're running it in Docker, just provide the URL.

When configured this way, the plugin handles starting and stopping the process, and will even clean up leftover `chromedriver` instances if Jellyfin crashes.

## 🖼️ Cover art and backdrops

Images are downloaded during the plugin's metadata pass and cached locally. Jellyfin then requests them via the plugin, which uses appropriate Referer headers to ensure the CDNs actually serve the images.

If you ever need to refresh just the images for an item, use the *Replace all metadata* option in Jellyfin.

## 💾 How metadata is cached

Data is saved as JSON in `{data}/plugins/Jellyfin.Plugin.JavOrganizer/cache/`. By default, successful scrapes are kept for 30 days, and failed lookups are cached for 7 days.

If you delete the cache folder or run a Deep Re-scrape, it will force fresh network requests.

## 📚 Collections

The plugin provides a scheduled task (**Update JavOrganizer Collections**,
runs daily, also runnable on demand) that builds:

### 🎭 Gender browse cards (one entry point per gender)

| Collection | What it holds |
|---|---|
| **Female Actresses (JavOrganizer)** | Every actress's whole filmography chained — performers ordered by **total views** across their titles, each performer's block by **release date** (newest first) |
| **Male Actors (JavOrganizer)** | The same for male actors |

Open either card in the library and browse top-down: you walk performer by
performer — most-viewed actress first, her newest title first, then the next
performer. It's a one-click "browse everyone" entry point per gender.

### 👤 Per-person collections

- **"Actress: Name"** / **"Actor: Name"** — one collection per performer
  (when they appear in at least *Min videos per person collection*, default 2),
  ordered by release date, with the performer's photo as the poster.

### 🏆 Rankings and groups

- **Most Viewed (JavOrganizer)** — top 100 by play count across all users.
- **Most Liked (JavOrganizer)** — top 100 by likes/favorites across all users.
- **All Videos (JavOrganizer)** — the entire scraped library, newest release first.
- **Newest Releases (JavOrganizer)** — the 100 most recently released.
- Optional: per studio ("Studio: …"), per genre ("Genre: …"), per release
  year ("Year: …") — toggle in the plugin settings.

**Sorting inside Jellyfin:** every collection carries a defined order
(newest release first / most-viewed first), and Jellyfin's own sort options
(**Sort by: Release Date / Play Count / Community Rating / Name** and the
**Favorites** filter) are available on every collection and library view —
per-person collections, gender cards, Most Viewed and Most Liked give you
release-date, total-view and total-like browsing out of the box.


### 🔀 Where the sort options live (exact UI paths)

| Page | How to sort |
|---|---|
| **Home** | Home is a landing page (libraries + Latest). Open a library or collection to sort its contents. |
| **Movies library** | Open the library → **⋮ menu (top right) → Sort by**: Name, Community Rating, Critic Rating, Date Added, Date Played, Parental Rating, **Play Count (total views)**, **Release Date**, Runtime. Works with the ↑/↓ toggle for direction. |
| **Collections view** | Same **⋮ → Sort by** menu. Type in the search box to jump to a card (e.g. "Female"). |
| **Inside any collection / gender card** | The same native sort menu, plus the plugin's built-in ordering: gender cards are pre-sorted performers-by-total-views with each performer's titles newest-first. |
| **"Total likes"** | Jellyfin has no server-side aggregate-likes sort, so the plugin provides it as content instead: the **Most Liked (JavOrganizer)** collection is your library ranked by likes/favorites across all users, and per-user favorites sort via **Filters → Favorites** anywhere. |
## 🧹 Automatic cache cleanup

A scheduled task runs daily (default 04:00) to delete expired cache files and clean up orphaned records for files that are no longer in your library.

## 🔨 Building from source and running tests

You'll need the .NET SDK matching your target version. Use the included script:

```powershell
.\build.ps1                        # Build all versions
.\build.ps1 -JellyfinVersion 12.0  # Build a specific version
```

To run the offline test suite:

```powershell
dotnet run --project tools/SmokeTest -c Release
```

## 🗂️ Project layout

The source code is organized logically. Key files include:

- `Plugin.cs`: Entry point.
- `JavCodeParser.cs`: Handles product code extraction.
- `SiteScraper.cs`: Base class for scraping logic, rate limiting, and FlareSolverr integration.
- `JavMetadataProvider.cs`: The core Jellyfin integration.

## ❓ Troubleshooting

**Manual scan buttons are in two places.**
- **Main menu → "Scan"** — the dedicated scan page (one click from anywhere)
- **Dashboard → Plugins → JavOrganizer** — the same buttons at the top of the settings page

**Plugin doesn't show up in Jellyfin.**
Make sure you downloaded the right zip for your Jellyfin version. The `.meta` sidecar file and `HtmlAgilityPack.dll` must be present.

**Videos only show the title.**
Try running **Scan Library Now** or **Deep Re-scrape Everything**. Check your logs for Cloudflare errors—you might need to set up FlareSolverr.

**Lots of Forbidden/Cloudflare logs.**
Configure FlareSolverr in the settings so the plugin can get past the checks.

**FlareSolverr crashes immediately.**
You might have an orphaned `chromedriver.exe` process. Kill it in Task Manager, clear out `%AppData%\undetected_chromedriver\`, and try again.

**Wrong language for titles.**
The plugin tries to grab English titles if available. If it can't find one across the enabled sites, it will fall back to Japanese. Make sure JavLibrary is enabled and FlareSolverr is working. Any Japanese-titled items are also picked up and re-scraped automatically by the scheduled scans until an English variant is found.

## 🔗 Compatibility notes

- You must use the build compiled for your specific Jellyfin version.
- Jellyfin 10.8 has some minor API differences which are handled via compiler flags in the source.

## ✅ Verified on a live server

We regularly test the plugin on real Jellyfin setups (e.g., v1.3.0 on Jellyfin 12.0.0). We verify that auto-scanning, scheduled tasks, FlareSolverr integration, and metadata fetching work correctly on large libraries.

## 📋 Changelog

### 1.3.1

- **Main-menu "Scan" page** — the manual scan buttons (Scan / Deep
  Re-scrape / Cancel with live progress bar, elapsed time and ETA) are now
  one click from anywhere in Jellyfin, not only inside the plugin settings.
- **Gender browse cards** — two new collections, *Female Actresses
  (JavOrganizer)* and *Male Actors (JavOrganizer)*, each chaining every
  performer of that gender: performers ordered by **total views**, each
  performer's titles by **release date**. One "browse everyone" entry
  point per gender.
- **Japanese-title self-healing** — items whose scraped title came back
  Japanese-heavy are automatically re-scraped by the normal scan (and by
  the 6-hourly scheduled pass) until an English variant replaces them.

### 1.3.0

- Added multi-site concurrent scraping.
- Improved title selection (prioritizes English/Latin text).
- Better Cloudflare handling and FlareSolverr management.
- Fixed CDN image download issues.
- Added scheduled scanning tasks and deep re-scrape options.
- Included 82 offline tests.

### 1.2.0

- Added UI configuration page.
- Hot-reload of settings.
- Jellyfin 10.8–12.0 support.

---

<div align="center">

<img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" alt="JavOrganizer" width="60">

**[📖 Quick Start](QUICKSTART.md)** · **[🤝 Contributing](CONTRIBUTING.md)** · **[🛡️ Security](SECURITY.md)** · **[📜 Code of Conduct](CODE_OF_CONDUCT.md)** · **[⚖️ License](LICENSE)**

<sub>JavOrganizer is a metadata scraper and is not affiliated with Jellyfin or any of the scraped sites.<br>Scrape responsibly: keep the request delay enabled for large libraries, and respect each site's terms.</sub>

<sub>Made with ❤️ for the Jellyfin community</sub>

<a href="#javorganizer">⬆ Back to top</a>

</div>
