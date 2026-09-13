<div align="center">

<img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" alt="JavOrganizer Logo" width="200">

# JavOrganizer

**🎬 JAV metadata for Jellyfin from up to 18 websites, scraped in parallel with an advanced anti-ban system.**

<br>

[![Build](https://github.com/CodeW-Otis/JavOrganizer/actions/workflows/build.yml/badge.svg)](https://github.com/CodeW-Otis/JavOrganizer/actions/workflows/build.yml)
[![Release](https://img.shields.io/badge/Release-v1.3.0-blue.svg)](https://github.com/CodeW-Otis/JavOrganizer/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Jellyfin 10.8–12.0](https://img.shields.io/badge/Jellyfin-10.8%20%7C%2010.9%20%7C%2010.10%20%7C%2010.11%20%7C%2012.0-00a4dc.svg)](#build-matrix--pick-the-build-matching-your-server)
[![.NET 6–10](https://img.shields.io/badge/.NET-6%20%7C%208%20%7C%209%20%7C%2010-512bd4.svg)](#build-matrix--pick-the-build-matching-your-server)
[![Stars](https://img.shields.io/github/stars/CodeW-Otis/JavOrganizer.svg?style=social)](https://github.com/CodeW-Otis/JavOrganizer/stargazers)

<br>

### ⚡ One-line install — copy this into Jellyfin:

```
https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/manifest.json
```

> 📋 **Click the copy button** (top-right of the box above) then paste into **Dashboard → Plugins → Repositories → ➕**

**[→ Full install guide below](#-installation)** · [Quick Start (5 min)](QUICKSTART.md) · [The 18 sites](#the-18-sites) · [Configuration](#configuration) · [Troubleshooting](#troubleshooting)

**⭐ If this plugin organizes your library, star the repo — it really helps others find it!**

</div>

---

### ✨ Key Features

<table>
  <tr>
    <td align="center" width="33%">🌐 <strong>18 Sources</strong><br>Scrapes sites in parallel and merges the best data</td>
    <td align="center" width="33%">🛡️ <strong>Anti-Ban</strong><br>Rotating fingerprints, jitter, backoff, circuit breakers</td>
    <td align="center" width="33%">🌍 <strong>English First</strong><br>Latin-script titles win over Japanese-only variants</td>
  </tr>
  <tr>
    <td align="center">🖼️ <strong>Rich Media</strong><br>Cover art + 10–20 scene backdrops per video</td>
    <td align="center">👥 <strong>Gender-Aware</strong><br>Separate actress/actor credits with collections</td>
    <td align="center">☁️ <strong>Cloudflare Bypass</strong><br>Auto-managed FlareSolverr lifecycle</td>
  </tr>
  <tr>
    <td align="center">⏰ <strong>Auto Scanning</strong><br>Startup + 6-hour scheduled + on-demand scans</td>
    <td align="center">💾 <strong>Smart Cache</strong><br>30-day TTL with thin-record self-healing</td>
    <td align="center">🔧 <strong>Jellyfin 10.8–12.0</strong><br>One optimized build per server line</td>
  </tr>
</table>

JavOrganizer identifies Japanese adult video files by the product code in
their file name (for example `SSIS-406.mp4`), scrapes up to **18 websites
at the same time** for each code — JavLibrary, JavDB, JavBus, MissAV
(+mirror), OneJAV, FANZA/DMM digital, FANZA DVD, JavLand, 123AV, SEXTB,
SupJav, JavGG, JavSeen, JavMix, JavQuick, JavTube and MGStage — and merges
the best of each into Jellyfin:

| What you get | How |
|---|---|
| 📝 **English titles** | The merge prefers Latin-script text from any site, so Japanese-only pages never win the title |
| 👥 **Actresses and male actors** | Gender-separated cast from JavDB / MissAV / 123AV, with romaji names preferred |
| 🖼️ **Cover art + 10–20 scene backdrops** | Per-host Referer downloads that the CDNs actually accept |
| 🏷️ Studio, genres, release date, runtime | Gap-filled from whichever of the 18 sites has each field |
| 📚 **Gender-aware collections** | "Actress: …" / "Actor: …" plus Most Viewed / Most Liked rankings |

Cloudflare protection is bypassed through an optionally bundled
[FlareSolverr](https://github.com/FlareSolverr/FlareSolverr) that the plugin
can start with the server and stop with the server.

Supports **Jellyfin 10.8, 10.9, 10.10, 10.11 and 12.0** with one build per
Jellyfin line.

> [!NOTE]
> **Live-tested end to end** on a real Jellyfin 12.0.0 server (Windows,
> 1204-video library, managed FlareSolverr): plugin load, startup auto-scan,
> all API endpoints, config hot-reload, a full deep re-scrape, multi-site
> merging with Cloudflare fallbacks, image downloads, and all three
> scheduled tasks. Details in [Verified on a live server](#verified-on-a-live-server).

---

## 📖 Contents

<details>
<summary><strong>Click to expand full table of contents</strong></summary>

1. [Why some videos showed only a title — and how it is fixed](#why-some-videos-showed-only-a-title--and-how-it-is-fixed)
2. [How it works](#how-it-works)
3. [The 18 sites](#the-18-sites)
4. [Advanced anti-ban & anti-detect system](#advanced-anti-ban--anti-detect-system)
5. [Scanning: automatic and manual](#scanning-automatic-and-manual)
6. [Filename conventions](#filename-conventions)
7. [🚀 Installation](#-installation)
8. [☁️ FlareSolverr — what it is and how to install it (step by step)](#️-flaresolverr--what-it-is-and-how-to-install-it-step-by-step)
9. [📂 Library setup](#-library-setup)
10. [Configuration](#configuration)
11. [☁️ FlareSolverr lifecycle (auto start/stop) — **the most important step**](#️-flaresolverr-lifecycle-auto-startstop--the-most-important-step-to-unlock-the-full-plugin)
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

## 🔍 Why some videos showed only a title — and how it is fixed

Previous versions could leave a video showing nothing but its file name, a
Japanese-only title, or no cover. Six separate causes, all fixed:

1. **The auto-scan only ran once, 30 seconds after server start.** A file
   added while the server kept running — or one whose first scrape failed
   transiently — was never retried until the next restart. **Fix:** a new
   scheduled task, *Scan JavOrganizer Library*, runs the same pass every
   6 hours (configurable in Dashboard → Scheduled Tasks).
2. **A partial scrape was never repaired.** If every site was blocked at
   scan time, the item ended with no provider id but a "not found" marker;
   if a site answered with almost nothing, a thin record was cached and
   then served forever — and items that did get a provider id were skipped
   by every later scan. **Fix:** the normal scan now also re-scrapes items
   stuck at "title only" (provider id present but no date and no cover),
   and thin cache records (no cover, genres, cast, date or runtime) are
   re-scraped from the sites instead of being served — throttled to at
   most once every 6 hours per code.
3. **Items without a usable path were skipped.** Code extraction looked
   only at the file path. **Fix:** the item name is now the fallback, the
   same as in the metadata provider.
4. **Wrong-site data could stick.** The old JavBus id regex matched CSS
   cache-buster query strings (`css-slider.css?v=9.3`), producing bogus
   provider ids like `9` that collided across items and misrouted cover
   art. **Fix:** JavBus items are identified by their product code, the
   merge keeps only sane ids, and the image provider also resolves
   records by product code as a fallback.
5. **Error pages were parsed as videos.** A CDN's "403 ERROR" page
   (CloudFront) has an `<h1>` that satisfied the generic title selector,
   so 201 records ended up with the *title* "403 ERROR", no cover, no
   cast — and were cached as if they were real matches. **Fix:** error
   and interstitial pages ("403 ERROR", "Access denied", "Redirecting…",
   "Just a moment…", CloudFront refusals) are recognized and never parse
   into records, and the disk cache self-heals by re-scraping such records.
6. **Japanese titles won even when English existed.** Two causes: (a) the
   challenge detector matched `challenge-platform` — a script tag present
   on *every* Cloudflare-protected page — so the plugin threw away every
   successfully solved JavLibrary page (the main English source) and fell
   back to Japanese-only pages; (b) the merge kept whichever title came
   from the highest-priority site, regardless of language. **Fix:** the
   challenge markers now match only real interstitials, FlareSolverr gets
   a 60-second solve budget, and the merge prefers Latin-script titles,
   cast names and studios whenever any site provides them.

> [!IMPORTANT]
> **If you are upgrading from 1.2.0:** open the plugin configuration and
press **Deep Re-scrape Everything** once. It re-scrapes every video
through the full 18-site stack, repairing old partial data. The pass is
cancel-safe: it purges each item's cache only when that item's turn comes.

## ⚙️ How it works

```
filename ──► JavCodeParser.ExtractCode ──► "ABP-123"
                                               │
                    ┌──────────────────────────┴─────────────────────────┐
                    ▼                                                      ▼
              1. JavCache (substantive fresh record? use it, done)   thin record
                                               │ missing                 older than 6h?
                                               ▼                              │ re-scrape
              2. Negative cache (recent miss? skip, done)                    ▼
                                               │ miss
                                               ▼
        Up to MaxSitesPerScrape sites in parallel, ranked by priority,
        banned/unreachable sites skipped and replaced by the next site:
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
                    merge ──► JavVideo (priority order wins)
                                │
              JavCache.TryWrite (30-day TTL) ──► Jellyfin item:
                Name "CODE Title", Overview, Year, PremiereDate,
                Runtime, Studio, Genres, People (actresses + male
                actors + director), ProviderIds["JavLibrary"],
                Primary cover + scene Backdrops
```

## 🌐 The 18 sites

Every enabled site is scraped **simultaneously** per code and the results
are merged in priority order (richer sources first, later sites fill the
gaps). The **Max Sites Scraped at Once** setting caps how many sites one
code uses — banned or unreachable sites are skipped and the next site
takes their slot, so the cap is always spent on sites that can answer.

| # | Site | Priority | Strengths | Toggle |
|---|---|---|---|---|
| 1 | JavLibrary | 10 | Baseline: titles, genres, 10+ scene previews | always on |
| 2 | JavDB | 20 | Gender-separated cast, tags | on |
| 3 | JavBus | 30 | Direct pages with age cookie, rarely blocks | on |
| 4 | MissAV | 40 | Gender-labeled Actress/Actor fields | on |
| 5 | MissAV mirror (missav.ai) | 41 | Same site, second domain for resilience | on |
| 6 | OneJAV | 50 | Covers, release dates, cast | on |
| 7 | FANZA (DMM digital) | 60 | Official catalog, richest data | on |
| 8 | FANZA DVD (DMM mono) | 61 | Independent second FANZA page | on |
| 9 | JavLand | 70 | JavBus-family direct pages | on |
| 10 | 123AV | 75 | MissAV-template, gender-labeled cast | on |
| 11 | SEXTB | 80 | Per-code streaming pages | on |
| 12 | SupJav | 90 | WordPress search; title, cover, date, categories | on |
| 13 | JavGG | 90 | WordPress search | on |
| 14 | JavSeen | 90 | WordPress search | on |
| 15 | JavMix | 90 | WordPress search | on |
| 16 | JavQuick | 90 | WordPress search | on |
| 17 | JavTube | 90 | WordPress search | on |
| 18 | MGStage | 150 | Official amateur-label store (SIRO…) | on |

All 18 have individual on/off toggles in the configuration page. Sites are
independent: a dead, blocked or re-marked-up site simply contributes
nothing and never breaks the merge — after repeated failures the circuit
breaker skips it for 30–60 minutes so scans stay fast. JavLibrary is the
always-on baseline source.

**Suggested profiles:**

- **Maximum completeness** (default): all sites on, cap 8–10. Every site
  sees very little traffic because requests are spread across 18 domains.
- **Fast + polite**: JavLibrary + JavBus + OneJAV only, cap 3, delay 1000 ms.
- **Ban-recovery**: turn a banned site off, raise the cap — the remaining
  sites absorb the work.

## 🛡️ Advanced anti-ban & anti-detect system

Scraping 18 sites is itself the first line of defense: each individual
site sees a small fraction of your traffic. On top of that:

1. **Rotating coherent browser fingerprints.** Every request presents a
   randomly chosen, internally consistent browser profile: user agent plus
   matching `sec-ch-ua` client hints, `sec-ch-ua-platform`, and an
   `Accept-Language` variation — 8 real-world profiles (Chrome, Edge,
   Firefox, Safari across Windows/macOS/Linux). The plugin never presents
   the same fingerprint twice in a row while undisguised, and it never
   mixes headers a real browser would never mix. When FlareSolverr solves
   a Cloudflare challenge, the solving browser's exact user agent is
   pinned (Cloudflare validates the pairing) until it stops working.
2. **Per-site rate limiting with randomized jitter.** Every site has its
   own pacing gate; parallel workers queue politely per site. The spacing
   is jittered randomly (60–140% of the configured delay) so the traffic
   pattern never looks machine-regular. Multi-page walks (search results)
   add their own jittered pauses.
3. **Exponential backoff.** HTTP 429/503 responses are retried with
   exponential, jittered delays instead of hammering through.
4. **Ban detection with 6-hour cooldown.** Ban pages (English and Chinese)
   are recognized; the site is skipped for six hours — no requests are
   wasted against a wall.
5. **Unreachable circuit breaker.** Three consecutive total failures
   (direct + FlareSolverr) mark a site unreachable for a randomized
   30–60 minutes. Dead sites cost one attempt, then nothing.
6. **Clearance reuse.** The first Cloudflare challenge is solved once by
   FlareSolverr's real browser; its `cf_clearance` cookie and user agent
   are adopted so every following request goes directly at full speed.
   Concurrent workers share a single in-flight solve.
7. **Polite failure semantics.** A site that never actually answered
   (banned, blocked, dead) never contributes a "not found" verdict, so
   transient failures never poison the negative cache.

## 🔄 Scanning: automatic and manual

**Automatic:**

- **On server start** (30 s after boot): scrapes everything missing
  metadata. Disable with *Auto-scan library on server start*.
- **Every 6 hours** (scheduled task *Scan JavOrganizer Library*): picks
  up files added while the server runs and retries previously failed
  codes. Cached codes cost nothing, so recurring runs over a scraped
  library are one cheap library query.
- **On Jellyfin's own library scans**: the plugin is a normal metadata
  provider, so freshly discovered files are scraped during any library
  scan when JavOrganizer is the enabled downloader.

**Manual (plugin configuration page):**

- **Scan Library Now** — one pass over everything missing metadata or
  stuck at title-only. Shows live progress: videos processed, scraped,
  still missing.
- **Deep Re-scrape Everything** — re-scrapes every video from the sites
  with full metadata and image replacement, purging each item's cache
  only when its turn comes (cancel-safe). The heavyweight fix-it button,
  and the recommended one-time action after upgrading from 1.2.0.
- **Cancel Scan** — stops a running pass at any time.

## 📁 Filename conventions

The parser looks for the pattern **2–6 letters + optional hyphen + 2–5 digits**,
case-insensitive, anywhere in the file name:

| File name | Parsed code |
|---|---|
| `[ABC-123] Some Title.mp4` | `ABC-123` |
| `abp982.mp4` | `ABP-982` |
| `SSIS-406.mp4` | `SSIS-406` |
| `IPX-1234_uncensored.mp4` | `IPX-1234` |
| `Movie.FHD1080.ABP-123.mp4` | `ABP-123` (noise tokens skipped) |
| `ipx-177 4K.mp4` | `IPX-177` |
| `T28-597.mp4` | `T28-597` (digit-suffixed labels keep their digits) |

Rejected on purpose:

- **FC2 titles** (`FC2-PPV-1234567`) — not supported.
- **Noise tokens** that look like codes — resolution/quality (`FHD`, `UHD`,
  `HD`, `QHD`), codecs (`HEVC`, `H264`, `X265`), containers (`MP4`, `MKV`),
  source markers (`WEB`, `BLURAY`, `REMUX`) and disc/part markers (`CD`,
  `PART`, `VOL`, `EP`) — so `Movie.FHD1080.mp4` triggers **no** scrape.
- Names without any letter-digit pair.

Zero-padded variants are normalized automatically: `abp-00123` is searched
as the canonical `ABP-123` (the site index keys on the zero-free form), and
the cache stores it under `abp-123`.

## 🚀 Installation

Two ways to install. **Method 1 is a 30-second copy-paste** and gets you
automatic updates; Method 2 is a manual download.

### Method 1 — Plugin catalog ⭐ (recommended, 30 seconds)

**Copy this repository URL:**

```
https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/manifest.json
```

Then in Jellyfin:

1. Open **Dashboard → Plugins → Repositories**.
2. Click **➕**, paste the URL above, and **Save**.
3. Open **Dashboard → Plugins → Catalog**, find **JavOrganizer** and click
   **Install** on the entry whose *targetAbi* matches your server
   (the catalog lists one per Jellyfin line — pick yours).
4. **Restart Jellyfin** when prompted.

That's it — future releases arrive through the same catalog with one click.

> [!TIP]
> **Which Jellyfin do I have?** Look at the bottom of the dashboard
> sidebar, or **Dashboard → Plugins** after install — the plugin shows its
> version there. The catalog entry matching your server is the right one.

### Method 2 — Manual download

Grab the zip matching your Jellyfin version from the
[**Releases**](https://github.com/CodeW-Otis/JavOrganizer/releases/latest)
page (all five are attached to every release, each with a `.sha256`
checksum):

| Your Jellyfin version | Download this zip |
|---|---|
| **12.0.x** | `JavOrganizer-120-v1.3.0.0.zip` |
| **10.11.x** | `JavOrganizer-1011-v1.3.0.0.zip` |
| **10.10.x** | `JavOrganizer-1010-v1.3.0.0.zip` |
| **10.9.x** | `JavOrganizer-109-v1.3.0.0.zip` |
| **10.8.x** | `JavOrganizer-108-v1.3.0.0.zip` |

1. Extract the zip **contents** into your plugins folder:
   - **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\JavOrganizer\`
     or `%LocalAppData%\Jellyfin\plugins\JavOrganizer\`
   - **Linux**: `/var/lib/jellyfin/plugins/JavOrganizer/`
   - **Docker**: mount the folder into the container's
     `/config/plugins/JavOrganizer/`

   The folder must end up containing `Jellyfin.Plugin.JavOrganizer.dll`,
   `HtmlAgilityPack.dll`, the `.deps.json`, the `.runtimeconfig.json` and
   the `.dll.meta` sidecar.
2. Restart Jellyfin.
3. Verify under **Dashboard → Plugins**: **JavOrganizer 1.3.0** is listed.

### Build matrix — why one zip per Jellyfin line

| Jellyfin version | Build | .NET runtime required |
|---|---|---|
| 10.8.x | `…-108-…` | .NET 6 |
| 10.9.x | `…-109-…` | .NET 8 |
| 10.10.x | `…-1010-…` | .NET 8 |
| 10.11.x | `…-1011-…` | .NET 9 |
| 12.0.x | `…-120-…` | .NET 10 |

Jellyfin's own assemblies change between minor versions, so a build only
loads on its matching line — using the wrong one fails with a
`FileNotFound`/`TypeLoad` error at startup. (The runtime is provided by the
Jellyfin server itself; you do not need to install anything extra.)

> ☁️ **Also install [FlareSolverr](#️-flaresolverr--what-it-is-and-how-to-install-it-step-by-step)**
> (separate project, ~2 minutes, step-by-step guide below). It is **not
> bundled** with the plugin — but without it the Cloudflare-walled sites
> (JavLibrary, JavDB, MissAV — the main English-title sources) are skipped
> and your metadata will be thinner. Once installed, the plugin manages its
> whole lifecycle (start with Jellyfin, stop with Jellyfin).

**Next step:** the [5-minute Quick Start](QUICKSTART.md) — library
settings, file naming and the FlareSolverr setup.

---

## ☁️ FlareSolverr — what it is, and how to install it (step by step)

> **Important:** FlareSolverr is **NOT included in the plugin zip.** It is a
> separate open-source project that the plugin calls when a site challenges
> the server. **JavOrganizer works without it** — but the Cloudflare-walled
> sites (JavLibrary, JavDB, MissAV — the main *English-title* sources) get
> skipped, so titles may fall back to other sites' data. Installing it takes
> ~2 minutes and unlocks the full 18-site stack.

### What it does

Several metadata sites sit behind Cloudflare's bot check. When the plugin
gets challenged, FlareSolverr solves the check in a real (headless) browser,
hands the clearance back to the plugin, and every following request goes
direct at full speed. One solve covers many requests, and concurrent workers
share a single in-flight solve — it is not one browser round-trip per request.

### Step 1 — Install FlareSolverr

**Windows (no Docker):**

1. Go to <https://github.com/FlareSolverr/FlareSolverr/releases>.
2. Download `flaresolverr_windows_x64.zip` from the latest release.
3. Extract it anywhere you like, e.g.
   `C:\Users\<you>\AppData\Local\FlareSolverr\`.
   You should now have `C:\Users\<you>\AppData\Local\FlareSolverr\flaresolverr.exe`.
4. **Don't run it manually** — the plugin will manage it (next steps).

**Docker (any OS):**

```bash
docker run -d \
  --name flaresolverr \
  -p 8191:8191 \
  -e LOG_LEVEL=info \
  --restart unless-stopped \
  ghcr.io/flaresolverr/flaresolverr:latest
```

**Linux (manual):** download `flaresolverr_linux_x64.tar.gz` from the same
releases page, extract, and either run `./flaresolverr` yourself or let the
plugin manage it via the executable path.

### Step 2 — Point the plugin at it

Open **Dashboard → Plugins → JavOrganizer** and set:

| Field | What to enter |
|---|---|
| **FlareSolverr URL** | `http://localhost:8191/v1` (Docker/remote: `http://<host>:8191/v1`) |
| **FlareSolverr Executable Path** | **Windows/non-Docker only:** the full path to `flaresolverr.exe`, e.g. `C:\Users\<you>\AppData\Local\FlareSolverr\flaresolverr.exe`. **Leave empty if you use Docker** — the container already runs it. |

Save. All settings take effect immediately — no restart needed.

### Step 3 — That's it. Here's what happens now

| Event | What the plugin does |
|---|---|
| **Jellyfin starts** | Kills any orphaned FlareSolverr left from a crashed run → launches a fresh instance → waits for its health endpoint |
| **A site challenges a request** | One shared solve through FlareSolverr → the clearance cookie + matching user agent are adopted → all following requests go direct at full speed |
| **Jellyfin stops** | Kills the FlareSolverr process tree — nothing keeps running |

**Verify it works:** start a scan (or restart Jellyfin) and check the log at
Debug level for:

```
JavOrganizer.Plugin: "JavLibrary": FlareSolverr fetched '…' and granted direct access
```

That line means a challenge was solved and direct access was granted.

### Troubleshooting FlareSolverr

- **`FlareSolverr dies instantly, chromedriver permission error`** — a
  previous crashed run left a locked `chromedriver.exe` under
  `%AppData%\undetected_chromedriver\`. Kill any orphaned
  `flaresolverr`/`chromedriver` processes in Task Manager, delete that
  folder's contents, restart the server (the plugin's orphan reaper also
  handles this on boot).
- **`FlareSolverr could not solve the challenge`** occasionally — some
  challenges are genuinely unsolvable at that moment; the site is retried
  on the next scan and temporarily circuit-broken so scans stay fast.
- **Docker users:** make sure the container is on the same network so
  `localhost:8191` resolves, or use the container's hostname.
- **Prefer to run it yourself?** Leave the executable path empty, run
  FlareSolverr as your own service/container, and only set the URL —
  the plugin detects it on demand either way.

## 📂 Library setup

1. Create a library with content type **Movies** pointing at your collection.
2. Open **Library settings → Metadata → Metadata downloaders**:
   - Move **JavOrganizer** to the top and make it the **only** enabled
     downloader (disable TMDb etc. — they will not match these files and only
     slow the scan down).
3. Under **Images**, keep only the fetchers you want; JavOrganizer supplies
   the cover as Primary (and Backdrop) image.
4. Keep the file name conventions above; folder-per-title
   (`SSIS-406/SSIS-406.mp4`) works, as does a flat folder.
5. Scan the library. Items whose code is not found keep their file name and
   are simply left without metadata — check the log at Debug level for
   `No search results for '<code>'`. The scheduled scan retries them every
   6 hours.

## ⚙️ Configuration

Open **Dashboard → Plugins → JavOrganizer** in the web client. All settings
take effect **immediately** — no server restart needed (the FlareSolverr
executable path applies on the next server start).

| Setting | Default | Meaning |
|---|---|---|
| **Scan Library Now** | — | Manual pass over videos missing metadata, with live progress. |
| **Deep Re-scrape Everything** | — | Re-scrape every video from the sites (cancel-safe). |
| **Cancel Scan** | — | Stop the running pass. |
| **Language** | `en` | Language used on the sites (`en`, `ja`, `zh`, `tw`). Affects scraped titles and genres. |
| **Max Sites Scraped at Once** | `8` | How many of the enabled websites are scraped simultaneously per video (1–20). Banned/unreachable sites are replaced by the next in the ranking. |
| **Parallel Scrapes** | `4` | How many videos are scraped at the same time. |
| **Delay Between Requests (ms)** | `500` | Minimum spacing between requests to the same site (randomly jittered). `0` disables. |
| **Auto-scan library on server start** | on | Startup scrape of everything missing metadata. |
| **18 site toggles** | on | Individual on/off switch per site (see [The 18 sites](#the-18-sites)). |
| **FlareSolverr URL** | *(empty)* | Base URL of a FlareSolverr instance used when Cloudflare blocks the server. Empty disables the fallback. |
| **FlareSolverr executable path** | *(empty)* | When set, the plugin **starts this FlareSolverr with the Jellyfin server and stops it when the server stops**. Orphaned instances from a crashed server are reaped on the next boot. |
| **Successful-scrape cache (hours)** | `720` (30 days) | How long a scraped video is kept before the next scan re-fetches it. `0` disables expiry. |
| **Not-found cache (hours)** | `168` (7 days) | How long an unmatched code is remembered so library re-scans skip it. |
| **Build collections** (+studio/genre/year) | on | Per-person, Most Viewed/Liked, and optional studio/genre/year collections. |
| **Min videos per person collection** | `2` | Minimum appearances before a person gets their own collection. |

Settings are persisted in
`{data}/plugins/configurations/Jellyfin.Plugin.JavOrganizer.xml` and can also
be edited by hand while the server is stopped.

## ☁️ FlareSolverr lifecycle (auto start/stop) — **the most important step to unlock the full plugin**

> [!IMPORTANT]
> **This is the step that decides whether the plugin reaches its full
> potential.** Without FlareSolverr the plugin still works — but the
> Cloudflare-walled sites (**JavLibrary, JavDB, MissAV** — the main
> *English-title* sources) get skipped, and your items end up with thinner
> metadata and possibly non-English titles. **Installing it takes ~2
> minutes.** Full instructions:
> **[FlareSolverr — what it is, and how to install it](#️-flaresolverr--what-it-is-and-how-to-install-it-step-by-step)**.

### The two settings that matter

Open **Dashboard → Plugins → JavOrganizer** and set **both**:

| Setting | Value | Why |
|---|---|---|
| **FlareSolverr URL** | `http://localhost:8191/v1` | Tells the scrapers where to send challenge requests. **Required for the bypass to work.** |
| **FlareSolverr Executable Path** | e.g. `C:\Users\<you>\AppData\Local\FlareSolverr\flaresolverr.exe` | Lets the plugin **own the lifecycle** — start with Jellyfin, stop with Jellyfin. **Leave empty if FlareSolverr runs in Docker** (the container already manages it). |

### What happens automatically once both are set

| Event | The plugin does this — you do nothing |
|---|---|
| 🟢 **Jellyfin starts** | Kills any orphaned FlareSolverr left over from a crashed run → launches a fresh instance → waits for its health endpoint before scans need it |
| 🔥 **A site challenges a request** | One shared solve through FlareSolverr's real browser → the clearance cookie + matching user agent are adopted → every following request goes **direct at full speed** |
| 🔴 **Jellyfin stops** | Kills the FlareSolverr process tree — nothing keeps running on your machine |

### Verify it is working

Start a scan (or restart Jellyfin) and check the log at Debug level for:

```
JavOrganizer.Plugin: "JavLibrary": FlareSolverr fetched '…' and granted direct access
```

That line means a Cloudflare challenge was solved and direct access was
granted — English titles are now flowing in.

### Prefer to run FlareSolverr yourself?

No problem — as a Windows service, in Docker, on another machine: leave the
**Executable Path empty**, just set the **URL** (e.g.
`http://192.168.1.50:8191/v1` for a remote host). The plugin detects and
uses it on demand either way.

> [!TIP]
> Crashed FlareSolverr with a `chromedriver` permission error? Kill orphaned
> `flaresolverr`/`chromedriver` processes in Task Manager, delete the
> contents of `%AppData%\undetected_chromedriver\`, and restart — the
> plugin's orphan reaper also handles this automatically on boot.

## 🖼️ Cover art and backdrops

Images are delivered by the plugin's image provider in two steps:

1. **Metadata first.** During a scan the metadata provider scrapes the
   sites and stores the record — including the absolute cover URL and scene
   screenshot URLs — in the cache
   (`…/Jellyfin.Plugin.JavOrganizer/cache/<code>.json`). The item also
   receives a provider id.
2. **Image download.** The image provider looks the record up by that id
   (or by the item's product code as fallback) and offers the cover as the
   **Primary** image and the scene screenshots as **Backdrop** images.
   Jellyfin downloads them through the plugin with browser-like headers
   (the cover CDNs reject plain client agents).

Notes:

- **Covers live on CDNs** (`pics.dmm.co.jp`, `www.javbus.com`,
  `cdn.onejav.com`) that are usually *not* behind the Cloudflare block that
  protects the HTML pages — so images generally download even when scraping
  itself needs FlareSolverr.
- To re-fetch images for one item: refresh it with *Replace all metadata*.
  To re-fetch everything: run **Deep Re-scrape Everything**.

## 💾 How metadata is cached

Scraped records are stored as one JSON file per product code under
`{data}/plugins/Jellyfin.Plugin.JavOrganizer/cache/` (for example
`ssis-406.json`), kept fresh for **Successful-scrape cache** hours — the
default **720 hours (30 days)** means every video is scraped once a month at
most, and a *new* file added to the library is scraped exactly once, on the
first scan that sees it.

Thin records — a scrape that produced only a title and nothing else — are
re-scraped (rather than served) once they are 6 hours old, so partial data
heals automatically as sites recover.

Codes that no site could match get a `.missing` marker (for example
`-zzzz-999.missing`), kept for **Not-found cache** hours (default 7 days).
During that window library re-scans skip the sites for that code entirely —
unmatched files cost nothing on repeated scans. Scans clear stale markers
for codes they are about to retry.

On a cache hit no network request is made at all. An in-memory
`videoId → file` index makes the image provider's per-request lookup O(1); it
rebuilds itself lazily after a server restart. Delete the cache folder (or
run **Deep Re-scrape Everything**) to force a full re-scrape.

## 📚 Collections

Collections (daily task, also runnable on demand — *Update JavOrganizer
Collections* in Dashboard → Scheduled Tasks):

- **Gender-tagged people collections** — every actress gets
  "Actress: Name" and every male actor "Actor: Name" (gender knowledge from
  the sites' gender-separated cast), with the person's photo as the
  collection poster and a role + video-count overview. Old un-prefixed
  collections are retired automatically.
- **"All Videos (JavOrganizer)"** — the entire scraped library, newest
  release first.
- **"Newest Releases (JavOrganizer)"** — the 100 most recently released.
- **"Most Viewed (JavOrganizer)"** — top 100 by play count, all users.
- **"Most Liked (JavOrganizer)"** — top 100 by likes/favorites, all users.
- **Optional groups** (toggleable): per studio ("Studio: MOODYZ"), per genre
  ("Genre: Creampie"), per release year ("Year: 2024").

Every collection is ordered by release date (newest first) and refreshes
its membership on each run. People need at least *Min videos per person
collection* (default 2) appearances to get their own collection.

## 🧹 Automatic cache cleanup

The plugin registers a scheduled task — **"Clean JavOrganizer Cache"**
(Dashboard → Scheduled Tasks, category *Library*) — that runs daily at 04:00
by default and can also be run on demand. It removes:

1. **Expired scrape records** — older than the *Successful-scrape cache* TTL.
2. **Expired not-found markers** — older than the *Not-found cache* TTL.
3. **Orphaned records** — cached metadata whose product code no longer
   matches any video file name in the library.

The library query is only an optimization: if it fails for any reason, the
task skips orphan removal that run rather than risk deleting live records.

## 🔨 Building from source and running tests

Requires the **.NET SDK** for the target line (SDK 6 for the 10.8 build, 8 for
10.9/10.10, 9 for 10.11, 10 for 12.0). The simplest path is the helper script,
which builds every line into `dist/` with the `.meta` sidecars and release
zips (with SHA256 checksums) included:

```powershell
.\build.ps1                        # build all five
.\build.ps1 -JellyfinVersion 12.0  # build only the Jellyfin 12 target
```

Or manually:

```powershell
dotnet publish -c Release -p:JellyfinVersion=10.8
dotnet publish -c Release -p:JellyfinVersion=10.9
dotnet publish -c Release -p:JellyfinVersion=10.10
dotnet publish -c Release -p:JellyfinVersion=10.11
dotnet publish -c Release -p:JellyfinVersion=12.0   # default when omitted
```

A core-logic test suite — **82 checks**: code parsing (including zero-padded,
underscore-boundary and digit-label cases), catalog URL building (DMM cid
padding), generic-scraper HTML parsing against the **real JavBus 2026 page
markup** (bare-text-node dates, `span.genre` links, `sample-box` previews),
WordPress-og-meta parsing, FANZA Japanese-label parsing, the multi-site
merge rules and name/overview building — lives in `tools/SmokeTest`:

```powershell
dotnet run --project tools/SmokeTest -c Release
```

Package versions are pinned to the exact Jellyfin releases each target was
verified against (see `Jellyfin.Plugin.JavOrganizer.csproj`).

## 🗂️ Project layout

| File | Role |
|---|---|
| `Plugin.cs` | Entry point; plugin id, config page registration |
| `PluginConfiguration.cs` | Settings model (XML-serialized by Jellyfin) |
| `Configuration/configPage.html` | Web-UI settings page with Scan/Deep Scan/Cancel buttons and live progress |
| `PluginServiceRegistrator.cs` | DI registration of providers + lifecycle + scan services |
| `FlareSolverrHostedService.cs` | Starts/stops FlareSolverr with the server; reaps orphans |
| `JavAutoScanService.cs` | On startup, scrapes library videos that lack JavOrganizer metadata |
| `JavScanScheduledTask.cs` | Periodic (6 h) scan so files added later are scraped automatically |
| `JavScanTrigger.cs` | One shared scan engine: normal + deep passes, live progress, cancel-safe purges |
| `JavOrganizerController.cs` | Plugin API: Scan, Scan/Deep, Scan/Cancel, Scan/State |
| `JavCodeParser.cs` | Product-code extraction, normalization, noise filtering |
| `SiteScraper.cs` | Shared scraper base: rotating browser profiles, HTTP, Cloudflare fallback, ban/unreachable tracking, flexible date parsing |
| `PerSiteRateLimiter.cs` | Jittered per-site pacing shared by all workers |
| `JavSiteCatalog.cs` | Declarative definitions of the 14 generic sites |
| `GenericSiteScraper.cs` | Selector engine executing the catalog definitions |
| `JavScraperFactory.cs` | Cached scraper instances per site+language, live-config limiter |
| `JavLibraryScraper.cs` | JavLibrary scraping (baseline metadata, cover, previews) |
| `JavDbScraper.cs` | JavDB scraping (gender-separated cast, tags, cover) |
| `JavBusScraper.cs` | JavBus scraping (real 2026 markup, covers, DMM sample previews) |
| `MissAvScraper.cs` | MissAV + mirror scraping (gender-labeled cast) |
| `JavMetadataProvider.cs` | `IRemoteMetadataProvider<Movie, MovieInfo>`; capped multi-site fan-out + merge |
| `JavImageProvider.cs` | `IRemoteImageProvider` for cover (Primary) + scene Backdrops |
| `JavCache.cs` | JSON cache with TTL, negative caching, purge, in-memory id index |
| `JavCacheCleanupTask.cs` | Scheduled task: auto-clean expired + unused cache files |
| `JavCollectionsTask.cs` | Scheduled task: people + ranking collections |
| `JavHttp.cs` | Shared long-lived `HttpClient`s |
| `JavVideo.cs` | Scraped-record DTO |
| `tools/SmokeTest` | Core-logic test suite (82 checks) |
| `build.ps1` | Multi-target build helper |
| `manifest.json` | Plugin-repository manifest |
| `dist/` | Release builds: one folder + zip + `.sha256` per Jellyfin line |

## ❓ Troubleshooting

**Plugin does not load / `Loaded plugin` line missing.**
The build does not match the server line — use the matching folder from the
[build matrix](#build-matrix--pick-the-build-matching-your-server). Also verify
the `.meta` sidecar sits next to the DLL and that `HtmlAgilityPack.dll` was
copied along.

**Some videos show only the title.**
Press **Scan Library Now**; if they still do not fill, press **Deep
Re-scrape Everything**. Check the log (Debug level) for the failing code:
`No search results for '<code>'` means no site has the code (it will be
retried every 6 hours by the scheduled scan); Cloudflare warnings mean you
should configure FlareSolverr.

**No metadata, log shows Cloudflare/Forbidden warnings.**
Cloudflare is blocking the server. Configure
[FlareSolverr](#flaresolverr-lifecycle-auto-startstop) — ideally via the
managed lifecycle.

**FlareSolverr dies instantly, log shows a chromedriver permission error.**
A previous crashed run left a locked `chromedriver.exe` under
`%AppData%\undetected_chromedriver\`. Kill any orphaned
`flaresolverr`/`chromedriver` processes in Task Manager, delete that folder's
contents, and restart the server — the plugin's orphan reaper also handles
this automatically on boot.

**Male actors missing on an item.** Male credits come from gender-aware
sites (JavDB, MissAV family, 123AV). Make sure at least one is enabled,
then press **Deep Re-scrape Everything**.

**A specific site never returns data.** It is probably banned (6-hour
cooldown, see the log) or unreachable (30–60-minute cooldown). Both states
clear themselves. Persistent failure means the site changed its markup or
your IP is blocked by it — toggle it off and rely on the other 17.

**Titles in the wrong language.** The merge already prefers Latin-script
titles from any site. If an item is still Japanese-only, no enabled site
had an English title for it at scrape time — press **Deep Re-scrape
Everything** (JavLibrary, the main English source, is Cloudflare-walled
and needs a working FlareSolverr). Changing **Language** (`en`/`ja`/`zh`/
`tw`) also switches which page variants are requested.

**Metadata is stale.** Records live for *Successful-scrape cache* hours;
either lower the value, delete `…/Jellyfin.Plugin.JavOrganizer/cache/`, or
run **Deep Re-scrape Everything**.

**Covers missing.** Covers download through the plugin with a browser
user agent and a Referer matching the image's own host (the CDNs reject
mismatched requests). If covers are still missing, the record was
scraped from a site that had none — run **Deep Re-scrape Everything**
with more sites enabled so another source fills the gap. Check the
cache folder for the record's `coverUrl` field and the item's *Refresh*
dialog → images → `JavOrganizer`.

**Scanning is slow.** Raise *Parallel Scrapes*, raise *Max Sites Scraped at
Once*, and enable more sites — spreading the work over more sites is the
fastest safe speedup. Or lower *Delay Between Requests* (at your own risk
of bans).

## 🔗 Compatibility notes

- One build per Jellyfin line is **required**: Jellyfin's
  `MediaBrowser.Controller/Model/Common` assemblies carry a distinct
  `AssemblyVersion` per release and .NET resolves them exactly, so a 10.10
  build will not load in a 10.9 server.
- Jellyfin **10.8** has two API differences the build handles internally:
  `PersonInfo.Type` is a plain string (an enum everywhere else) and the
  plugin service-registrator interface lives in `MediaBrowser.Common.Plugins`
  with a single parameter. Both are selected via the `JF_LEGACY_PERSON`
  compile flag.
- **`GeneratedRegex`** source generation is used on .NET 7+; the .NET 6 build
  (Jellyfin 10.8) falls back to a cached compiled `Regex` with identical
  behavior.
- The provider registers for `IRemoteMetadataProvider<Movie, MovieInfo>` —
  Jellyfin 12's generic constraint (`TItemType : IHasLookupInfo<TLookupInfoType>`)
  only permits the `Movie` subclass of `Video` to pair with `MovieInfo`.
- The web-UI configuration page uses only `emby-*` components and the stable
  `ApiClient.getPluginConfiguration`/`updatePluginConfiguration` calls, which
  work on every supported Jellyfin web client. On Jellyfin 12 the page is
  served at `/web/ConfigurationPage?name=JavOrganizer`.

## ✅ Verified on a live server

v1.3.0 was tested end to end against a real Jellyfin **12.0.0** server on
Windows (data dir `%LocalAppData%\Jellyfin`, managed FlareSolverr at
`localhost:8191`) with a real **1204-video** library:

| Function | Result |
|---|---|
| Plugin load on Jellyfin 12.0 (net10.0) | `Loaded plugin: "JavOrganizer" "1.3.0.0"` |
| Startup auto-scan | 6 pending found, 3 refreshed, stale markers swept, finished in ~2 min |
| Scheduled tasks | All three registered; *Scan JavOrganizer Library* fires every 6 h (IntervalTrigger verified) |
| Scan state endpoint | Live counters (total/completed/refreshed/missing) update correctly |
| Scan while running / cancel | Second trigger refused; cancel stopped the pass mid-flight and reported state |
| Deep re-scrape | 1204-item queue, per-item cache purge (cancel-safe), full metadata + image replacement |
| Config hot-reload | `MaxSitesPerScrape` / site toggles take effect on the next request without restart |
| Config page | Served at `/web/ConfigurationPage?name=JavOrganizer` with the new buttons and 18 site toggles |
| Multi-site scraping | JavBus direct (dv=1), JavLibrary/MissAV via FlareSolverr clearance adoption; unreachable sites circuit-broken |
| Scraped record quality (live pages) | Date, runtime, maker, label, 7–12 genres, cast, cover, 10–19 DMM sample previews per title |
| Item metadata | Name/Year/PremiereDate/Genres/People (actress + male actor + director)/Overview/ProviderIds verified on items |
| Images | Primary (800×538 JPEG) and Backdrop served through the plugin's image provider, bytes verified |
| Collections task | 337 gender-tagged people collections + All Videos (1201) + Most Viewed built on demand |
| Cache cleanup task | Completed; expired/negative/orphan sweep verified |

Bugs found **by** the live test and fixed before release:

- the bogus JavBus provider ids (CSS `?v=` strings),
- the destructive upfront cache purge in the deep scan,
- CDN "403 ERROR" pages parsing into cached records (201 items on the
  test library showed exactly that title),
- the challenge detector matching `challenge-platform` (present on every
  Cloudflare-protected page), which made the plugin discard every
  successfully solved JavLibrary page — the root cause of Japanese-only
  titles,
- cover downloads without a per-host Referer (javbus.com covers return
  403 for foreign Referers).

The JavBus selectors were also rewritten against the actual 2026 page
markup (bare text-node dates, `span.genre` labels, `sample-box` preview
anchors), and `ParseDate` now extracts dates from labelled lines like
`Release Date: 2020-06-05`.

All five targets (10.8/net6.0, 10.9, 10.10, 10.11/net9.0, 12.0/net10.0)
build with **0 warnings / 0 errors**, and the **82-check** test suite
passes.

## 📋 Changelog

### 1.3.0

- **18-site scraping** with per-code site cap (1–20, default 8) and
  priority ranking; banned/unreachable sites are skipped and replaced.
- **English-first merging**: Latin-script titles, cast names and studios
  win over Japanese-only variants from any site.
- **Error pages never parse into records**: CDN 403/interstitial pages
  ("403 ERROR", "Redirecting…", "Just a moment…") are detected and
  discarded instead of being cached as matches.
- **Fixed Cloudflare detection**: only true interstitial markers are
  treated as challenges (`challenge-platform` is a normal page tag),
  so solved pages from the English sources are actually used; solves
  get a 60-second budget; CloudFront edge errors are skipped instead
  of triggering pointless browser solves.
- **Per-host Referer image downloads**: covers from javbus.com and other
  Referer-validating CDNs now download (previously 403).
- **Advanced anti-ban / anti-detect**: rotating coherent browser
  fingerprints, jittered per-site pacing, exponential backoff, 6-hour ban
  cooldown, unreachable circuit breaker, shared FlareSolverr solves.
- **Automatic scraping fixed**: 6-hourly scheduled scan task, thin-record
  self-healing (re-scrape after 6 h), item-name fallback for code
  extraction, title-only item retry, stale negative-marker sweeping.
- **Manual controls**: Scan Library Now, Deep Re-scrape Everything, Cancel
  Scan — all with live progress counters.
- **JavBus rewritten against the real 2026 markup**: bare-text dates and
  runtimes, `span.genre` label structure, `sample-box` preview anchors
  (10–19 DMM samples per title), stable code-based provider ids.
- **Deep scan made cancel-safe**: per-item cache purge instead of upfront
  purge.
- **Sites added**: MissAV mirror (missav.ai), OneJAV, FANZA digital + DVD,
  JavLand, 123AV, SEXTB, SupJav, JavGG, JavSeen, JavMix, JavQuick, JavTube,
  MGStage — each individually toggleable.
- **Tests**: 82-check offline suite covering parsers, catalog URLs, merge
  rules, error-page rejection and the real JavBus page markup.

### 1.2.0

- Configuration page (language, FlareSolverr, cache TTL, request delay);
  O(1) cache index for image lookups; hot-reload of settings without
  restart; polite inter-request delay; gzip decompression; cover offered as
  Primary and Backdrop. Multi-target builds for Jellyfin 10.8-12.0.

---

<div align="center">

<img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" alt="JavOrganizer" width="60">

**[📖 Quick Start](QUICKSTART.md)** · **[🤝 Contributing](CONTRIBUTING.md)** · **[🛡️ Security](SECURITY.md)** · **[📜 Code of Conduct](CODE_OF_CONDUCT.md)** · **[⚖️ License](LICENSE)**

<sub>JavOrganizer is a metadata scraper and is not affiliated with Jellyfin or any of the scraped sites.<br>Scrape responsibly: keep the request delay enabled for large libraries, and respect each site's terms.</sub>

<sub>Made with ❤️ for the Jellyfin community</sub>

<a href="#javorganizer">⬆ Back to top</a>

</div>
