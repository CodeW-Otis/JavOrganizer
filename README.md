<div align="center">

<img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" alt="JavOrganizer Logo" width="200">

# JavOrganizer

**🎬 JAV metadata plugin for Jellyfin. Scrapes up to 19 sites concurrently with built-in rate limiting and retries.**

<br>

[![Build](https://github.com/CodeW-Otis/JavOrganizer/actions/workflows/build.yml/badge.svg)](https://github.com/CodeW-Otis/JavOrganizer/actions/workflows/build.yml)
[![Release](https://img.shields.io/badge/Release-v1.5.6-blue.svg)](https://github.com/CodeW-Otis/JavOrganizer/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Jellyfin 10.8 and later](https://img.shields.io/badge/Jellyfin-10.8%2B-00a4dc.svg)](#build-matrix--pick-the-build-matching-your-server)
[![.NET 6 | 8](https://img.shields.io/badge/.NET-6%20%7C%208-512bd4.svg)](#build-matrix--pick-the-build-matching-your-server)
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
    <td align="center">🔧 <strong>Jellyfin 10.8 and later</strong><br>Two builds cover every version</td>
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

It supports Jellyfin 10.8 and every later version with just two builds: one for 10.8, and one that covers 10.9 onward including releases that have not been published yet. See [Build matrix](#build-matrix--pick-the-build-matching-your-server).

> [!NOTE]
> **Tested on a live server:** We test these features on a real Jellyfin 12.1.0 instance (Windows, 1200+ items, using FlareSolverr). This covers loading, auto-scanning, API usage, multi-site merging, and scheduled tasks. See [Verified on a live server](#verified-on-a-live-server) for details.

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
        JavLibrary     JavDB        JavBus    MissAV family   14 catalog sites
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

## 🌐 The 19 sites

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
   - **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\JavOrganizer\` by default. If the server was started with a custom `--datadir` (for example `%LocalAppData%\Jellyfin`), use `<that datadir>\plugins\JavOrganizer\`.
   - **macOS**: `~/.local/share/jellyfin/plugins/JavOrganizer/` (or the server's configured data directory)
   - **Linux**: `/var/lib/jellyfin/plugins/JavOrganizer/` (or the server's configured data directory)
   - **Docker**: Mount into `/config/plugins/JavOrganizer/`
2. Ensure the folder contains the DLLs and `.json` files.
3. Restart Jellyfin.

### Build matrix — pick the build matching your server

Only two builds exist; every release ships one zip per Jellyfin version line,
each carrying that line's `targetAbi`. A server accepts a plugin whose
`targetAbi` is not newer than itself, so the newest build is offered to every
line from 10.9 up — including releases that have not come out yet.

| Your Jellyfin | Zip to download | Build inside |
|---|---|---|
| 10.8.x | `JavOrganizer-108-v<version>.zip` | net6.0 |
| 10.9.x | `JavOrganizer-109-v<version>.zip` | net8.0 |
| 10.10.x | `JavOrganizer-1010-v<version>.zip` | net8.0 |
| 10.11.x | `JavOrganizer-1011-v<version>.zip` | net8.0 |
| 12.0.x | `JavOrganizer-120-v<version>.zip` | net8.0 |
| 12.1.x | `JavOrganizer-121-v<version>.zip` | net8.0 |
| 12.2.x | `JavOrganizer-122-v<version>.zip` | net8.0 |
| 12.3.x | `JavOrganizer-123-v<version>.zip` | net8.0 |
| 12.4.x | `JavOrganizer-124-v<version>.zip` | net8.0 |
| Anything later | the newest zip | net8.0 (forward-compatible) |

> ☁️ **Next up: [Setting up FlareSolverr](#️-setting-up-flaresolverr)**. It's optional but highly recommended to access sites behind Cloudflare.

---

## 💻 Platform support

| Platform | Role | Support |
|---|---|---|
| **Windows** | Jellyfin server | Full, incl. managed FlareSolverr + kill-on-close Job Object |
| **macOS** | Jellyfin server | Full, incl. managed FlareSolverr (software process-tree kill) |
| **Linux** | Jellyfin server | Full, incl. managed FlareSolverr (software process-tree kill); Docker also supported |
| **iOS / iPadOS** | Jellyfin client | The settings page is plain web UI and runs in the Jellyfin app's web view / Safari (WebKit). The server itself does not run on iOS. |
| **Android** | Jellyfin client | The same web settings UI in the Jellyfin app / Chrome. |

The plugin ships two builds (net6.0 for Jellyfin 10.8, net8.0 for 10.9+); both are
platform-neutral and run anywhere the matching Jellyfin server runs. The settings
page uses only native HTML controls and `-webkit-`-prefixed glass effects, so it
renders on Blink (Chrome/Edge), WebKit (Safari/iOS) and Gecko (Firefox) alike, in
light and dark.

## ☁️ Setting up FlareSolverr

> **Note:** FlareSolverr is a separate program and is **not** bundled with the plugin. It is optional, but several sources — JavLibrary, JavDB, OneJAV and MissAV — sit behind Cloudflare. Without a solver those sites are skipped, so covers, cast and English titles can be incomplete.

### What it is

FlareSolverr runs a headless browser, solves the Cloudflare challenge once, and returns the clearance cookie. The plugin reuses that cookie for later requests. You can either run FlareSolverr yourself (Docker, NAS, another machine) and point the plugin at it, or let the plugin start and stop a local executable alongside Jellyfin.

### Step 1 — Get FlareSolverr

**Windows (standalone):**

1. Download `flaresolverr_windows_x64.zip` from the [FlareSolverr releases page](https://github.com/FlareSolverr/FlareSolverr/releases).
2. Extract it somewhere permanent (e.g. `C:\Users\<you>\AppData\Local\FlareSolverr\`).
3. **Don't start it.** The plugin can manage it (Step 2).

**Linux (standalone):**

1. Download `flaresolverr_linux_x64.tar.gz` from the same releases page.
2. Extract it (e.g. `tar -xzf flaresolverr_linux_x64.tar.gz -C ~/flaresolverr`).
3. Make it executable: `chmod +x ~/flaresolverr/flaresolverr`.
4. **Don't start it.** The plugin can manage it (Step 2).

**macOS:**

FlareSolverr publishes no native macOS build. Use **Docker** (recommended), or build it from source, `chmod +x` the resulting binary, and let the plugin manage that binary.

**Docker (any OS):**

```bash
docker run -d \
  --name flaresolverr \
  -p 8191:8191 \
  -e LOG_LEVEL=info \
  --restart unless-stopped \
  ghcr.io/flaresolverr/flaresolverr:latest
```

> [!TIP]
> If Jellyfin itself runs in a container, `localhost` inside that container is not your host. Publish port 8191 and use the host's LAN IP (or `host.docker.internal` on Docker Desktop) as the plugin's **FlareSolverr URL**.

### Step 2 — Configure the plugin

Open **Dashboard → Plugins → JavOrganizer** and use the **FlareSolverr** section:

| Field | What to enter |
|---|---|
| **FlareSolverr URL** | `http://localhost:8191` or `http://localhost:8191/v1` — both are accepted; the plugin normalizes the value to exactly one `/v1`. Use the host IP when FlareSolverr runs on another machine or in Docker. **Leave empty and set only the executable path** to use the plugin-managed local instance on port 8191. |
| **FlareSolverr Executable Path** | Optional, any OS. Full path to the binary the plugin should start and stop with the server — `…\flaresolverr.exe` on Windows, `…/flaresolverr` on Linux/macOS (execute bit required). Leave empty when using Docker or a remote instance. An empty URL plus a valid path implies `http://localhost:8191/v1`. |

**Choose one mode:**

- **Managed local process** — set the executable path (URL optional). The plugin starts it with Jellyfin, waits up to about 60 seconds for `/health` to answer, and kills the whole process tree on shutdown. On Windows the child is additionally bound to a kill-on-close Job Object, so it also dies when Jellyfin is force-killed or crashes.
- **External instance** — set only the URL (Docker, NAS, another host). The plugin probes `/health` at startup and logs whether it answered; it never starts or stops that instance.

Save the page. The **FlareSolverr** panel shows a live health badge (green = healthy) and a **Test FlareSolverr Connection** button. The test works for both modes. A `404`/`405` on `/health` only means that FlareSolverr build has no health route — the solver itself still works.

### Step 3 — Verify

Click **Test FlareSolverr Connection** and confirm the success message. During a scan, the plugin log shows lines such as:

```text
"JavLibrary": FlareSolverr fetched '…' and granted direct access
```

**Troubleshooting**

| Symptom | Fix |
|---|---|
| Health badge red, `connection refused` | Wrong host/port, container not running, or Docker networking — use the host IP / `host.docker.internal`, not `localhost`, from inside a Jellyfin container. |
| Managed process exits immediately | The path must point at the binary itself, not the folder. On Linux/macOS run `chmod +x` and start it once manually to see the error; FlareSolverr needs a Chrome/Chromium installation. |
| Health test returns `404` | Old FlareSolverr build without `/health`. The API still works; the badge may report it as reachable rather than healthy. |
| Sites still blocked | The challenge could not be solved (IP reputation, rate limiting). Check FlareSolverr's own log; retry later or lower parallel scrapes. |
| FlareSolverr missing after a crash | Restart Jellyfin: on Windows the Job Object kills leftovers; on Linux/macOS the hosted service reaps orphans before launching a new instance. |

## 📂 Library setup

1. Create a library in Jellyfin with the content type set to **Movies**.
2. In **Library settings → Metadata**, make **JavOrganizer** the top and only enabled downloader.
3. For **Images**, let JavOrganizer handle the Primary and Backdrop images.
4. Scan the library.

## ⚙️ Configuration

Available in **Dashboard → Plugins → JavOrganizer**. The page is split into **Scan · Sites · Engine · Cache · Collections · FlareSolverr**; every setting below applies immediately after **Save**.

| Setting | What it does |
|---|---|
| **Auto-scan on startup** | Scrape items that still lack JavOrganizer metadata when the server starts (default on). |
| **Language** | Preferred language for metadata (`en`, `ja`, `zh`, `tw`). English is preferred when a release has one. |
| **Sites** | One switch per source: JavLibrary, JavDB, JavBus, MissAV + mirror, OneJAV, FANZA digital + DVD, JavLand, 123AV, SEXTB, SupJav, JavGG, JavSeen, JavMix, JavQuick, JavTube, jav.guru and MGStage. Disabled sites are never contacted. |
| **Max Sites Scraped at Once** | How many enabled sites are queried concurrently for one code (1–20, default 8). Lower it if sites push back. |
| **Parallel Scrapes** | How many videos are processed at the same time (1–32, default 4; stored values are clamped). |
| **Delay Between Requests** | Pause between page fetches to the same site, in milliseconds (default 500). |
| **Successful-scrape cache** | How long a successful scrape stays fresh before a normal scan re-fetches it, in hours (default 720 = 30 days; `0` disables expiry). |
| **Not-found cache** | How long a failed lookup is remembered, in hours (default 168 = 7 days; `0` disables). |
| **Build collections** | Create the performer, Most Viewed / Most Liked, All Videos and Newest Releases collections. |
| **Studio / Genre / Year collections** | Optional extra groupings built by the collections task when their switch is on. |
| **All Videos / Newest Releases collections** | Build the two library-wide cards, All Videos (JavOrganizer) and Newest Releases (JavOrganizer) (default on). |
| **Female performer collections** | Build the Female Actresses (JavOrganizer) card and one collection per actress (default on). |
| **Male performer collections** | Build the Male Actors (JavOrganizer) card and one collection per male actor (default on). |
| **Min Videos per Collection** | A performer/studio/genre needs at least this many titles before a collection is created (default 2). |
| **FlareSolverr URL** | Base URL of the solver, with or without `/v1`. See [Setting up FlareSolverr](#️-setting-up-flaresolverr). |
| **FlareSolverr Executable Path** | Optional managed binary the plugin starts/stops with the server. Leave empty for Docker/remote. |

The same page also hosts **Scan Library Now**, **Deep Re-scrape Everything** and **Cancel**, plus a live progress bar, the site-status board and the FlareSolverr test. The main-menu **JavOrganizer** entry opens this same page.

## ☁️ FlareSolverr lifecycle (auto start/stop)

> [!IMPORTANT]
> If you want the plugin to manage FlareSolverr for you (Windows, macOS or Linux), set the **Executable Path** (the URL then defaults to the local instance on port 8191). If you're running it in Docker or remotely, just provide the URL.

When configured this way, the plugin handles starting and stopping the process on every platform, and will even clean up leftover `chromedriver` instances if Jellyfin crashes.

On Windows, the plugin additionally puts its FlareSolverr child into a **kill-on-close Job Object**, so FlareSolverr (and everything it spawned) is terminated by the operating system the moment Jellyfin exits — even when the server is force-killed (`taskkill /F`, End Task, a closed console window) or crashes, where no plugin shutdown callback runs at all. On macOS and Linux the same guarantee is provided in software: the hosted service kills the whole process tree on graceful shutdown and reaps orphans on the next start. FlareSolverr therefore lives and dies with your Jellyfin server on every platform.

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

- The plugin settings page shows the current **pacing multiplier** while a scan runs (`pacing 2.5× (easing off)`) and relaxes back to `1×` when sites are happy.
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
| **Studios (JavOrganizer)** | The same shape for studios, built when **Studio collections** is on: card → studio → that studio's videos |

Each wall has its own switch: **Female performer collections** and **Male
performer collections** in the plugin settings turn the two cards — and the
per-performer collections nested under them — on or off independently.
**All Videos / Newest Releases collections** and the studio, genre and year
groupings have their own switches too, and switching a group off removes the
collections it used to build.

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
- **Spellings of one name are merged** when the sites' own star ids tie them
  together, so "Yua Mikami" and "Yua Mikami（三上悠亜）" become one card.
  Names that merely share a romanization but have different star ids stay
  separate — the ids are what tell "Aoi（葵）" and "Aoi（あおい）" apart.
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
- **All Videos / Newest Releases** is switched separately from the performer
  walls, so you can keep the two library-wide cards without the per-person
  ones, or the other way round.

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

You'll need the .NET SDK matching your target version (6.0 for the 10.8 line, 8.0 for 10.9+). Use the included script for your platform:

**Windows (PowerShell):**

```powershell
.\build.ps1                        # Build all versions
.\build.ps1 -JellyfinVersion 10.9  # Build the 10.9-and-later build
```

**macOS / Linux (bash):**

```bash
./build.sh                         # Build all versions
./build.sh 10.9                    # Build the 10.9-and-later build
```

Both scripts produce the same `dist/` layout: a publish folder per build and one release zip per Jellyfin version line, each with its `.meta` sidecar, a `.md5` checksum and a `.sha256` checksum. The **MD5** is the value that goes into the catalog manifest's `checksum` field — Jellyfin verifies downloaded packages with MD5, so a SHA-256 there makes every catalog install fail. The bash script needs `zip` plus `md5sum`/`sha256sum` (Linux) or `md5`/`shasum` (macOS).

To run the offline test suite (any platform):

```bash
dotnet run --project tools/SmokeTest -c Release
```

## 🗂️ Project layout

The source code is organized logically. Key files include:

- `Plugin.cs`: Entry point.
- `JavCodeParser.cs`: Handles product code extraction.
- `SiteScraper.cs`: Base class for scraping logic, rate limiting, and FlareSolverr integration.
- `JavMetadataProvider.cs`: The core Jellyfin integration.

## ❓ Troubleshooting

**Where are the manual scan buttons?**
- **Dashboard → Plugins → JavOrganizer** — Scan Library Now, Deep Re-scrape
  Everything and Cancel live at the top of the settings page, with a live
  progress bar. The main-menu entry opens this same page.

**Plugin doesn't show up in Jellyfin.**
Make sure you downloaded the right zip for your Jellyfin version. The `.meta` sidecar file and `HtmlAgilityPack.dll` must be present.

**Videos only show the title.**
Try running **Scan Library Now** or **Deep Re-scrape Everything**. Check your logs for Cloudflare errors—you might need to set up FlareSolverr.

**Lots of Forbidden/Cloudflare logs.**
Configure FlareSolverr in the settings so the plugin can get past the checks.

**JavBus looks empty or shows "Age Verification" in a browser.**
That is JavBus's age gate, not a dead site. A browser that has not answered it
gets the verification page instead of the database; the plugin sends the age
cookie (`dv=1`) and reads the real pages directly, so JavBus keeps working. If
the debug log reports `age/driver gate`, the gate answered despite the cookie —
the address is being throttled, not the site retired. Lookalike domains such as
`javbus.sbs` are a different website (a streaming CMS with no per-code pages),
so the plugin deliberately does not scrape them.

**FlareSolverr crashes immediately.**
You might have an orphaned `chromedriver.exe` process. Kill it in Task Manager, clear out `%AppData%\undetected_chromedriver\`, and try again.

**Wrong language for titles.**
The plugin tries to grab English titles if available. If it can't find one across the enabled sites, it will fall back to Japanese. Make sure JavLibrary is enabled and FlareSolverr is working. Any Japanese-titled items are also picked up and re-scraped automatically by the scheduled scans until an English variant is found.

**Movies show a title and cover but no performers, so no actress collections appear.**
An actress collection needs the performers in each movie's metadata first. The plugin groups the cast each site credits — MissAV labels it per gender, in the page's own language — and a cached record that came back without any cast is re-scraped automatically by the scheduled scans, twice per code, so a title no site credits does not get re-fetched forever. If collections are still empty, check that a cast-carrying source (MissAV or JavDB) is enabled, then run the collections task from the settings page. Actress and male-actor cards are separate, and male performers are never mixed into the actress wall.

## 🔗 Compatibility notes

- Two builds cover every supported Jellyfin: one for 10.8, one for 10.9 and
  every later version. Pick the zip whose line matches your server (see the
  [build matrix](#build-matrix--pick-the-build-matching-your-server)).
- Jellyfin 10.8 has some minor API differences which are handled via compiler flags in the source.
- Everything that changed after 10.9 is resolved at runtime (`JavApiCompat`),
  so the 10.9 build keeps working on new Jellyfin releases without a rebuild.

## ✅ Verified on a live server

Every release is exercised on a real Jellyfin server, not just compiled.

**1.5.5 on Jellyfin 12.1.0 (Windows, FlareSolverr managed by the plugin):**

- The plugin loads as `JavOrganizer 1.5.5.0`; the managed FlareSolverr child starts with the server and reports healthy.
- The settings page is served by the web client (`/web/configurationpage?name=JavOrganizer`, HTTP 200).
- Config, scan-state, site-state and FlareSolverr-health endpoints answer for an administrator; anonymous requests are rejected with `401` because every `/JavOrganizer/*` route now carries `RequiresElevation`.
- A startup scan found 74 videos, queued the 29 that still need JavOrganizer metadata, and ran with 10 parallel workers and up to 8 sites per code; the log shows the sites being fetched through FlareSolverr.
- The offline suite passes **408/408** checks, and the source compiles with **0 warnings / 0 errors** against the Jellyfin 12.3.0 and 12.4.0 assemblies.

Earlier verification: the 1.4.0 collection rebuild ran end-to-end on a Jellyfin
12.0.0 server with 1,204 scraped videos — 191 actress cards and 7 actor cards
nested under the two browse cards, 198/198 performer collections carrying a
poster, and all three levels (card → performer → videos) browsing correctly.

## 📋 Changelog

### 1.5.6

**Localized cast, castless-record repair, and collections that survive Jellyfin 12.**

**Localized metadata.** MissAV labels cast — and maker, label, genre and release
date — in the page's own language, so a `zh`/`tw` library used to come back with a
title and cover but no performers. The parser now matches the localized labels
(女優 / 男優 and friends) and keeps every credited performer, not just the first.

**Castless records heal themselves.** A cached scrape that came back with a title
and cover but no cast counted as complete, so the scan never revisited it and the
actress groups stayed empty. Those records are re-scraped automatically — up to
twice per code, and a retry only counts when a site actually answered — so an
existing library fixes itself on a normal scan, without a manual re-scrape.

**Collections on Jellyfin 12.** The collections task wrote each member's parent
back to the repository, which makes Jellyfin 12 delete the member box set, so a
performer card could log "9 performers linked" and still open empty. That write is
gone, retired collections are deleted durably (folder included, so they no longer
come back after a restart), and retirement matches by id as well as by name.

**New collection options.** All Videos / Newest Releases, actress collections and
male-actor collections each have their own switch next to the existing studio,
genre and year ones, and switching a group off removes what it used to build.
Studio collections are grouped by a **Studios (JavOrganizer)** card with the same
card → studio → videos shape as the performer walls. Performer spellings that share
a site star id are merged onto one card ("Yua Mikami" and "Yua Mikami（三上悠亜）"),
while different star ids are never merged on name similarity alone.

**Faster collections.** One cache pass builds the gender, alias and photo indexes
(three full passes before), so the task finishes in about a second on a 74-title
library.

### 1.5.5

**A redesigned settings page, a batch of correctness fixes, and Jellyfin
12.3 / 12.4 support.**

**The settings page, rebuilt.** The page is now a clean, sectioned console:
a sticky section nav (Scan · Sites · Engine · Cache · Collections ·
FlareSolverr) with scroll-spy, custom toggle switches, a live site-status
board mirrored into a header pill, a slim animated progress bar with a
"now scraping" ticker, and a sticky save bar that flags unsaved changes.
Every control is a native element styled with page-scoped CSS that adapts
to dark and light themes — no `emby-*` web component is required, so the
page keeps working on future web clients that drop those components. All
fields, endpoints and polling behavior are unchanged.

**Correctness fixes.**
- The scan endpoints now require an **administrator** session
  (`RequiresElevation`), like Jellyfin's own scheduled-task endpoints —
  previously any signed-in user could start a deep re-scrape or cancel a
  running pass.
- A video that a user both **favorited and liked** now scores 3 + 2 = 5 in
  the Most Liked ranking; the like used to overwrite the favorite.
- The "site was reached" flag is now **per scrape call** instead of per
  scraper instance, so one code's successful fetch can no longer vouch for
  a concurrent code's failed one and get its miss cached as "not found".
- Parallel scrapes are clamped to 1–32, and the All Videos collection diff
  is now set-based instead of quadratic in the library size.
- English-title retries are no longer wasted while JavLibrary is banned. A
  Japanese-titled item is re-scraped up to three times to find an English
  release, but an attempt now only counts when JavLibrary — the primary
  English-title source — actually answered. Re-scrapes during a temporary
  ban keep the budget intact, so a ban can no longer lock in a Japanese
  title that an English release does exist for.
- **A rescan now fixes a Japanese title immediately.** The provider used to
  serve a fresh Japanese-titled cache record for up to six hours even when
  the scan had just queued that item, so every rescan was a no-op and the
  Japanese title stayed. The bounded three-attempt retry counter is now the
  only limiter: a queued item is re-scraped on that pass, and only a title
  with no English release anywhere is accepted after the third attempt.
- **Placeholder image URLs no longer fail the image refresh.** Lazy-loaded
  pages can expose a `data:` pixel (or `javascript:` stub) where the real
  cover belongs. Jellyfin cannot download those, and one such URL aborted
  the whole image pass for that item with *The 'data' scheme is not
  supported*. Scrapers now reject non-HTTP(S) image URLs, and the metadata,
  person-image and collection paths ignore any such URL already in the cache.

**Jellyfin 12.3 and 12.4** join the supported range. The same two builds
still cover everything; the source was compiled against each server
version's own assemblies (now through 12.4.0) and the CI compile matrix
runs every one of them on each push.

### 1.5.4

**The settings page is reachable again, and Jellyfin 12.2 is supported.**

**What was wrong.** The plugin registered two configuration pages: the
settings page and a separate Scan page. The Jellyfin 12.x web client picks
the page that is enabled in the main menu when a plugin has more than one —
so *Plugins → JavOrganizer → Settings* opened the **Scan** page, and the
settings (FlareSolverr URL, language, parallel scrapes, per-site switches,
collection options) were never shown at all. The page itself was fine; it
simply was not the page the client linked to.

**The fix.** The scan controls now live on the settings page and the plugin
registers that one page only. The main-menu entry and the Settings button
open the same page on every Jellyfin version, old and new.

**Jellyfin 12.2** (released October 2026) joins the supported range. The
same two builds as before still cover everything:

- **10.8** — its own build, on net6.0.
- **10.9 and every later version** — one net8.0 build that covers 10.9,
  10.10, 10.11, 12.0, 12.1, 12.2 **and any future release**.

Every member the shipped assembly binds to was compared against each
server version's own `MediaBrowser.Controller` / `MediaBrowser.Model`
assemblies — 72 bindings, all present with identical signatures on 10.9.11,
10.10.7, 10.11.11, 12.0.0, 12.1.0 and 12.2.0. A compile job now runs
against every one of those versions on each push, so a new Jellyfin release
is caught in CI instead of on a user's server.

### 1.5.3

**Universal version support — two builds instead of six, and no rebuild for
new Jellyfin releases.**

Until now every Jellyfin line needed its own build: 10.8, 10.9, 10.10,
10.11, 12.0 and 12.1 each produced a separate archive, and a new Jellyfin
release meant adding a target before anyone on it could install anything.

Now there are two builds:

- **10.8** — its own build, on net6.0.
- **10.9 and every later version** — one net8.0 build that covers 10.9, 10.10,
  10.11, 12.0, 12.1, 12.2 **and any future release**, with no rebuild needed.

**Why two and not one.** Jellyfin 10.9 moved `IPluginServiceRegistrator` from
`MediaBrowser.Common.Plugins` to `MediaBrowser.Controller.Plugins` and added
the host parameter to `RegisterServices`. A plugin has to implement whichever
interface the running server exposes, and that choice is made at compile time,
so 10.8 cannot share a build with anything later. Everything else that changed
afterwards is resolved at runtime instead:

- `TaskTriggerInfo.Type` — a string through 10.10, the
  `TaskTriggerInfoType` enum from 10.11.
- `IUserManager.Users` — a property through 10.10, replaced by `GetUsers()`.
- `ILibraryManager.GetItemList` and `GetPeople` — returned `List<T>` through
  10.11 and `IReadOnlyList<T>` from 12.0. A return-type change compiles fine
  against the old assemblies but throws `MissingMethodException` at runtime.
- `MetadataResult.People` — same collection-type change.
- `PersonKind` — the enum itself moved namespaces again in 12.1, so it is
  never named directly.

All of these go through `JavApiCompat`, which reads or writes the member
through reflection and accepts whichever shape the running server has. A
net8.0 assembly also loads on the net9.0 and net10.0 runtimes later Jellyfin
versions use, which is what lets one build span them.

**Verified on a live Jellyfin 12.1.0 server running the 10.9 build**: the
plugin loads and registers, FlareSolverr starts alongside the server, both
scheduled tasks arm, the startup auto-scan runs, and a metadata scrape
completes with no `MissingMethodException`. Both builds compile with zero
warnings and the offline suite passes.

### 1.5.2

Two changes, both driven by testing against the live library.

- **The normal scan no longer re-fetches metadata that has already been
  scraped.** An item whose cached scrape is still within its cache TTL is
  now skipped outright. Before this, an item that came back sparse — a site
  answered with a title and a cast but no genres, studio or overview, which
  is what the item kept showing — was re-queued on **every** scan, because
  it already had a provider id and therefore matched no other heal
  condition. On a library where the sites simply have no genres for a code,
  that meant re-scraping already-fetched metadata on every pass, forever.
  Queued now: never-scraped items, title-only items, sparse items *only
  once their cached scrape has aged out*, and Japanese-titled items (when
  the plugin language is English, bounded by the provider's retry counter).
  Left alone: everything whose cached scrape is still valid. The deep
  pass remains the explicit escape hatch that ignores all of this.

- **Removed a dead asset**: `docs/logo_optimized.png` was tracked but
  referenced nowhere.

Verified on a live Jellyfin 12.0.0 server mid-way through a large heal
pass: the scan skips completed work and only touches what is genuinely
unscraped. Offline suite: **251 checks**, all five Jellyfin targets build
with zero warnings.

### 1.5.1

Two automation defects found by testing every background path end-to-end.

- **The collections task re-downloaded every poster on every run.** A guard
  that skipped the download when a collection already carried the right image
  was lost in the 1.4.0 rewrite, so each daily pass re-fetched all ~198
  performer portraits from the remote CDNs. Measured on a live server: a run
  took **over five minutes** instead of seconds, for bytes already on disk.
  The download is now skipped when the item already holds the image that
  source would produce (matched by the stored file name, or by the remote URL
  when only a URL could be attached). Repeat runs measured at **5.1 seconds**,
  with all 197 posters intact.

- **A title with no English release was re-scraped forever.** When the
  configured language is English, a cached record whose title is still
  Japanese is retried so an English variant can replace it — but nothing
  bounded that retry, and a *substantive* record was excluded from the
  6-hour grace gate. Any title with no English release anywhere (for example
  `SDMF-010`) therefore triggered a full multi-site scrape on every scan,
  indefinitely, for no possible gain. The retry is now capped at **3
  attempts**, and the count is carried across the cache rewrite — without
  that, the counter reset each time and the bound would never have held.

Verified on a live Jellyfin 12.0.0 server: startup auto-scan fires on every
boot; FlareSolverr starts as a child of Jellyfin and reports healthy; all
three scheduled tasks carry the expected triggers (6-hourly scan, daily
cache clean at 04:00, daily collections at 04:30); all three manual buttons
work and the concurrency guard refuses a second scan while one runs; and
cache cleanup removed exactly one expired record, one expired not-found
marker and one orphan, with no over-deletion.

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
  the plugin settings.
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
