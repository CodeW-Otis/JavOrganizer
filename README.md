<div align="center">

<img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" alt="JavOrganizer Logo" width="200">

# JavOrganizer

**🎬 JAV metadata plugin for Jellyfin. Scrapes up to 19 sites concurrently with built-in rate limiting and retries.**

<br>

[![Build](https://github.com/CodeW-Otis/JavOrganizer/actions/workflows/build.yml/badge.svg)](https://github.com/CodeW-Otis/JavOrganizer/actions/workflows/build.yml)
[![Release](https://img.shields.io/badge/Release-v1.5.0-blue.svg)](https://github.com/CodeW-Otis/JavOrganizer/releases)
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

**[→ Full install guide below](#-installation)** · [Quick Start (5 min)](QUICKSTART.md) · [The 19 sites](#the-19-sites) · [Configuration](#configuration) · [Troubleshooting](#troubleshooting)

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

JavOrganizer identifies Japanese adult video files by looking for the product code in the file name (e.g., `SSIS-406.mp4`). It can scrape up to 19 websites at the same time for each code, including JavLibrary, JavDB, JavBus, MissAV, OneJAV, and FANZA. It then merges the data from these sites into Jellyfin.

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
3. [The 19 sites](#the-19-sites)
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
        JavLibrary     JavDB        JavBus    MissAV family   15 catalog sites
        (baseline)   (genders)   (fast, direct) (genders)   (OneJAV, FANZA ×2,
                                               JavLand, 123AV, SEXTB, SupJav,
                                               JavGG, JavSeen, JavMix, JavQuick,
                                               JavTube, jav.guru, MGStage)
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
| 18 | jav.guru | 85 | Gender-labeled cast (Actress/Actor) and English titles | on |
| 19 | MGStage | 150 | Amateur labels | on |

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
| **FlareSolverr URL** | `http://localhost:8191` or `http://localhost:8191/v1` (both work; or your Docker host IP) |
| **FlareSolverr Executable Path** | (Windows only) The full path to `flaresolverr.exe`. Leave empty if using Docker. |

Save your changes. The **FlareSolverr** panel on the same page shows a live health badge and a **Test FlareSolverr Connection** button — click it to confirm the instance answers (works for the plugin-managed local instance and for a remote/Docker one alike; a 404 on `/health` just means that build lacks the route — the solver still works).

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

On Windows, the plugin also puts its FlareSolverr child into a **kill-on-close Job Object**, so FlareSolverr (and everything it spawned) is terminated by the operating system the moment Jellyfin exits — even when the server is force-killed (`taskkill /F`, End Task, a closed console window) or crashes, where no plugin shutdown callback runs at all. FlareSolverr therefore lives and dies with your Jellyfin server.

## 🛡️ Anti-ban, anti-detect and adaptive pacing

The scraping engine behaves like a fast, careful human — and proves it in the UI.

### Anti-detect (browser realism)

- **One coherent browser per session**: each site scraper pins a single internally-consistent profile — user agent, matching `sec-ch-ua` client hints, platform, `Accept-Language` weighting, realistic header order — for its whole lifetime, exactly like one person using one browser. A Cloudflare clearance re-pins the exact solving browser (Cloudflare validates the pairing).
- **Realistic Referers**: search pages refer from the site root; detail pages refer from the search that led to them. A root Referer on every request is a bot tell.
- **Staggered fan-out**: when many sites are scraped for one code, each request leaves after a short randomized offset — never a perfectly synchronized burst.

### Anti-ban (polite pressure)

- **Per-site pacing with human jitter**: most gaps are short, roughly one in eight is a longer "reading" pause — the request stream never looks machine-regular, at any speed.
- **Exponential backoff with Retry-After**: 429/503 responses are retried after the site's own hint (capped at 8 s) plus jitter — far cheaper than a browser solve.
- **Ban cooling-off**: an explicit ban page stops all requests to that site for 6 hours.
- **Unreachable circuit breaker**: three consecutive transport failures skip a site for 30–60 minutes, so scans never waste time on a dead endpoint.
- **Adaptive engine-wide pacing**: every pushback (429, 503, ban) raises a global pressure signal that stretches request spacing everywhere, up to 4×; pressure decays automatically (90-second half-life) and successes bleed it off, so the engine returns to full speed on its own. Scans pause briefly between items only while pressure is high.

### Watch it live

- The **Scan** page and the plugin settings page show the current **pacing multiplier** while a scan runs (`pacing 2.5× (easing off)`) and relax back to `1×` when sites are happy.
- The **Site status** panel on the settings page lists every site as `active`, `banned — cooling off` or `unreachable — retrying later`, refreshing every 10 seconds.

## 🖼️ Cover art and backdrops

Images are downloaded during the plugin's metadata pass and cached locally. Jellyfin then requests them via the plugin, which uses appropriate Referer headers to ensure the CDNs actually serve the images.

If you ever need to refresh just the images for an item, use the *Replace all metadata* option in Jellyfin.

## 💾 How metadata is cached

Data is saved as JSON in `{data}/plugins/Jellyfin.Plugin.JavOrganizer/cache/`. By default, successful scrapes are kept for 30 days, and failed lookups are cached for 7 days.

If you delete the cache folder or run a Deep Re-scrape, it will force fresh network requests.

## 📚 Collections

The plugin provides a scheduled task (**Update JavOrganizer Collections**,
runs daily, also runnable on demand) that builds:

### 🎭 Gender browse cards (nested: card → performer → videos)

| Collection | What it holds |
|---|---|
| **Female Actresses (JavOrganizer)** | One card per actress — ordered by **total views** across their titles — and each card opens that performer's whole filmography |
| **Male Actors (JavOrganizer)** | The same for male actors |

Open either card in the library and you see the **performer cards**
(actress/actor collections, each with the performer's photo as its
poster, most-viewed first). Open one performer card and you see exactly
that performer's videos, newest release first. It's a one-click "browse
everyone" entry point per gender:

```
Female Actresses (JavOrganizer)
├── Actress: <most-viewed actress>     ← her poster is her photo
│   └── her videos, newest first
├── Actress: <next actress>
│   └── …
Male Actors (JavOrganizer)
├── Actor: <most-viewed male actor>
│   └── his videos, newest first
└── …
```

### 👤 Per-person collections

- **"Actress: Name"** / **"Actor: Name"** — one collection per performer
  (when they appear in at least *Min videos per person collection*, default 2),
  ordered by release date, with the performer's photo as the poster.
  Performer photos are collected from the sites' cast portraits
  (JavBus star photos and similar) during scrapes and served through the
  plugin's person image provider, so both the performer card in the
  library and the collection poster show a real face.
- **If no site published a portrait for a performer**, the card falls back
  to the cover of one of their own titles rather than staying blank — so a
  performer with videos always has an image.
- Photos are matched to the right performer through the star id shared with
  their credit link, the performer's name inside the portrait file name, or
  the image's label. Records scraped before this matching existed still
  yield their photos, so no re-scrape is needed after upgrading.

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
| **Inside any collection / gender card** | The same native sort menu, plus the plugin's built-in ordering: gender cards list performers by total views, and each performer card holds that performer's titles newest-first. |
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

We regularly test the plugin on real Jellyfin setups (e.g., v1.4.0 on Jellyfin 12.0.0). We verify that auto-scanning, scheduled tasks, FlareSolverr integration, and metadata fetching work correctly on large libraries.

The 1.4.0 collection rebuild and cover fix were verified end-to-end on a live
Jellyfin 12.0.0 server with 1,204 scraped videos: 191 actress cards and 7
actor cards nested under the two browse cards, 198/198 performer collections
carrying a poster, and all three levels (card → performer → videos) browsing
correctly.

## 📋 Changelog

### 1.5.0

This release came out of an exhaustive end-to-end test of every function
against the live sites. Six real defects were found and fixed, and
**jav.guru** joined the site list.

- **New site: jav.guru** — labels its cast by gender (*Actress:* /
  *Actor:*) and serves English titles, so it both fills gaps and refines the
  gender split the performer collections rely on. Verified live: it supplies
  the male actor for titles where JavBus alone has none.
- **Zero-padded codes now match everywhere.** Sites disagree about padding:
  the canonical code is zero-free (`ADN-29`) while permalinks and search
  indexes use the padded spelling (`adn-029`). Three separate places compared
  them literally, so a correct page was downloaded and then thrown away.
  Result-link matching, the search keyword, and page-code verification all
  accept every spelling now.
- **Ban detection no longer fires on innocent text.** The ban pattern matched
  the bare phrases `access denied` and `too many requests`, which sites embed
  in JavaScript localisation tables. jav.guru ships `"Too many requests.
  Please slow down."` for its comment widget, so it was marked banned and
  skipped for six hours on *every* scrape. The pattern now requires an
  explicit statement that this address was blocked, and a regression test
  pins both directions (8 real ban phrasings accepted, 5 innocent strings
  rejected).
- **OneJAV and javquick returned nothing at all.** Both had result-link
  selectors that could not match their real markup: OneJAV serves
  `<div class="card">` tiles with no `<article>` element anywhere, and
  javquick wraps results in an `<article>` whose anchor is a bare child
  rather than nested in an `<h2>`. Each matched zero links and reported
  "no match" for every code while still looking healthy.
- **A bare product code no longer beats a real title.** OneJAV publishes no
  descriptive title — its `og:title` is the literal string `OneJAV` and its
  only heading is the code — so `MIAB492` could win the merge over a real
  English title from JavDB. Titles that are only the code now lose to any
  real title, and a page whose only title is the site's own name is rejected.
- **Bracketed codes with edition suffixes.** Sites head pages with
  `[ADN-029-MR] Title`; the name builder added the code again, producing
  `ADN-029 [ADN-029-MR] Title`. A leading bracket that denotes the code —
  including a suffixed edition — is now stripped, while a *different*
  video's code in brackets (`[ADN-0299]`) is left alone.
- **Test suite grown from 117 to 212 checks**, adding ban-detection
  boundaries, result-link extraction for the markup shapes that broke, code
  normalisation edge cases (null, empty, malformed, 15-digit padding), the
  bracket/edition cases, and zero-padding agreement.
- **New live-network harness** (`tools/LiveScrape`) that scrapes real sites
  and reports per-site reachability and parsed fields. It reads the server's
  own FlareSolverr setting, so it reproduces what the plugin actually does
  rather than reporting every Cloudflare site as blocked.

Verified on a live Jellyfin 12.0.0 server: 18 of 19 sites reachable and 9
returning data for a sample code, with the collections (191 actress cards,
7 actor cards, 198/198 performer posters) and all three nesting levels
intact after the changes.

### 1.4.0

- **Actor and actress cards now actually show their photos.** The cover fix
  in full:
  - Portraits are collected from *every* image a detail page embeds, not
    just `<img title="…">` — lazy-loaded `data-src`, `srcset`, CSS
    backgrounds, and portraits linked from the star block are all
    harvested.
  - Each image is matched to the right performer through the star id shared
    with their credit link (`star/uly` ↔ `actress/uly_a.jpg`), the
    performer's own name inside the portrait file name, or the image's
    label.
  - Records scraped *before* this feature existed now yield photos too: the
    match is replayed at read time, so no re-scrape is needed.
  - The person image provider caches its index (5-minute window) instead of
    re-reading the whole scrape cache on every request — a library-wide
    image refresh is no longer hundreds of full cache scans.
  - **No card is ever blank**: a performer with no published portrait falls
    back to a cover from one of their own titles.
- **Nested collections rebuilt as a real Jellyfin hierarchy.**
  *Female Actresses (JavOrganizer)* and *Male Actors (JavOrganizer)* hold
  **one card per performer** — not loose videos — and each performer card
  opens that performer's videos, newest first. The old flat build had left
  907 and 744 stray videos inside the two cards; those are removed
  automatically on the next run, along with performer collections that no
  longer meet the minimum. (Technically: membership is written as linked
  children, because the collection manager's add path only accepts movies
  and silently flattened the nesting.)
- **UI polish.** Site names are escaped before rendering; the FlareSolverr
  check no longer fires on page load (it is what the button is for);
  polling timers are torn down when leaving a page so navigating back and
  forth can no longer stack intervals; and every button restores itself
  with a toast when a request fails, instead of leaving the page stuck
  behind a loading overlay or a button permanently disabled.
- **Repository hygiene.** Release archives and checksums are no longer
  committed (they are build output), and the tree carries no absolute
  paths from a developer machine.

### 1.3.2

- **Performer photos** — actor and actress cards (and their collections)
  now show real photos. Portraits are collected from the sites' cast
  images during scrapes, cached with each record, and served through a
  new **person image provider**, so both the performer card in the library
  and the collection poster carry a face.
- **Nested gender collections** — *Female Actresses (JavOrganizer)* and
  *Male Actors (JavOrganizer)* now contain each performer's own
  collection as a card: gender card → performer cards (poster = the
  performer's photo, ordered by total views) → that performer's videos
  (newest first).
- **Adaptive human-like pacing engine** — request spacing stretches
  politely (up to 4×) when sites push back with 429/503/bans and relaxes
  back to full speed automatically; a live pacing indicator is shown in
  the Scan page and plugin settings.
- **Anti-detect upgrades** — one coherent browser profile per site
  session (UA + client hints + platform), realistic per-page Referers,
  staggered parallel fan-out, Retry-After-honoring backoff.
- **Site status panel + FlareSolverr test** — the settings page shows
  every site's live state (active / banned cooling off / unreachable) and
  a button that verifies the FlareSolverr connection.
- **Cross-platform FlareSolverr** — URL handling unified for Windows,
  Linux, macOS and Docker; health checks work everywhere, and on Windows
  a kill-on-close job object keeps the managed FlareSolverr tied to the
  server even on force-kill.

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
