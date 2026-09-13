<div align="center">
  <img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" width="120" alt="JavOrganizer Logo" />
  <h1>Quick Start Guide</h1>
  <p><em>5 minutes to a fully scraped and organized library.</em></p>
  <img src="https://img.shields.io/badge/Time-5%20minutes-brightgreen" alt="Estimated Time: 5 minutes" />
</div>

---

This guide takes you from zero to a complete, organized JAV library in Jellyfin. For the full feature reference, see the [main README](README.md).

> [!NOTE]
> **Prerequisites**
> - Jellyfin server installed and running.
> - .NET runtime (provided directly by Jellyfin, no extra installs needed).

---

## 1️⃣ Install the plugin

**Fastest — add the plugin repository (30 seconds):**

Copy this URL:

```text
https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/manifest.json
```

> 📋 **Click the copy button** (top-right of the box above) to copy the URL to your clipboard.

In Jellyfin: **Dashboard → Plugins → Repositories → ➕** → paste → Save → **Dashboard → Plugins → Catalog** → **Install** *JavOrganizer* (pick the entry matching your Jellyfin version) → restart when prompted.

**Alternative — manual download** from [Releases](../../releases/latest), picking the zip matching your Jellyfin version (this matters, a mismatched build will not load):

| Your Jellyfin | Download |
|---|---|
| 12.0.x | `JavOrganizer-120-v1.3.0.0.zip` |
| 10.11.x | `JavOrganizer-1011-v1.3.0.0.zip` |
| 10.10.x | `JavOrganizer-1010-v1.3.0.0.zip` |
| 10.9.x | `JavOrganizer-109-v1.3.0.0.zip` |
| 10.8.x | `JavOrganizer-108-v1.3.0.0.zip` |

Grab it from the [Releases](../../releases) page, then:

1. Extract the zip **contents** into `{Jellyfin data}/plugins/JavOrganizer/`
   (Windows: `%LocalAppData%\Jellyfin\plugins\JavOrganizer\` — Linux: `/var/lib/jellyfin/plugins/JavOrganizer/`).
2. Restart Jellyfin.
3. Verify in **Dashboard → Plugins**: *JavOrganizer 1.3.0* is listed.

> [!CAUTION]
> **Critical Step:** Make sure you extract the zip contents *into* the `JavOrganizer` folder, not a subfolder inside it.

✅ **You should see:** In **Dashboard → Plugins**, *JavOrganizer 1.3.0* is listed and active.

---

## 2️⃣ Create the library

1. **Dashboard → Libraries → Add Media Library**.
2. Content type: **Movies**. Point it at your folder.
3. Under **Library settings → Metadata → Metadata downloaders**:
   - Move **JavOrganizer** to the top.
   - **Disable TMDb and every other fetcher** (they can't match JAV files and only slow the scan down).
4. Under **Images**, keep only the fetchers you want — JavOrganizer supplies the cover (Primary) and scene screenshots (Backdrop).
5. Save and scan the library.

✅ **You should see:** A new library in your Home screen starting to scan your media files.

---

## 3️⃣ (Recommended) Set up Cloudflare bypass

Several sources (JavLibrary — the main English-title source — JavDB, MissAV) sit behind Cloudflare. Without a bypass they will be skipped and your titles may fall back to other sites' data.

1. Install [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr) (Windows zip, or `docker run -d -p 8191:8191 ghcr.io/flaresolverr/flaresolverr:latest`).
2. **Dashboard → Plugins → JavOrganizer**:
   - **FlareSolverr URL**: `http://localhost:8191/v1`
   - **FlareSolverr Executable Path**: the path to `flaresolverr.exe` (leave empty if you run it in Docker) — the plugin then starts and stops it together with Jellyfin.

> [!TIP]
> If you are on Windows, providing the executable path lets JavOrganizer manage FlareSolverr for you automatically!

✅ **You should see:** A successful connection to FlareSolverr without errors in the Jellyfin logs when testing or scraping.

---

## 4️⃣ Check the file naming

The plugin identifies videos by the product code in the file name:

- ✅ **Works**: `SSIS-406.mp4`, `abp982.mp4`, `[IPX-177] Title.mp4`, `Movie.FHD1080.ABP-123.mp4`, `IPX-1234_uncensored.mp4`
- ❌ **Won't scrape**: `FC2-PPV-1234567.mp4` (unsupported), files with no letter+digits code at all.

Zero-padded variants (`ABP-00123`) are normalized automatically.

✅ **You should see:** Your video filenames containing their respective product codes clearly separated or easily identifiable.

---

## 5️⃣ Run the first scan

Open **Dashboard → Plugins → JavOrganizer** and press:

1. **Scan Library Now** — scrapes everything missing metadata. Live progress is shown right on the page.
2. If anything looks incomplete (Japanese-only title, missing cover), press **Deep Re-scrape Everything** once — it re-scrapes every video through all 18 sites with fresh data.

That's it. From now on:

- New files are scraped automatically when the library scans.
- A scheduled task re-checks for unscraped/failed items every 6 hours.
- One collection per actress/actor builds itself daily (plus Most Viewed / Most Liked rankings) under **Dashboard → Scheduled Tasks → Update JavOrganizer Collections**.

✅ **You should see:** Covers, localized titles, metadata, and actors magically appearing in your Jellyfin library!

---

## 6️⃣ Tune it (optional)

| Setting | Default | Raise it when… |
|---|---|---|
| Parallel Scrapes | 4 | you want a faster first fill |
| Max Sites Scraped at Once | 8 | you want maximum metadata per video |
| Delay Between Requests | 500 ms | — lower only if you accept ban risk |

✅ **You should see:** Performance matching your preferences and hardware limits.

---

## 🛠️ Troubleshooting

- **Plugin won't load** → wrong build for your Jellyfin line (step 1).
- **Japanese titles / missing covers** → FlareSolverr not running or not configured (step 3), then press Deep Re-scrape.
- **A few items never scrape** → the code genuinely doesn't exist on any enabled site; check Debug logs for `No search results for '<code>'`.
- **Everything else** → the [Troubleshooting](README.md#troubleshooting) section of the README.

---

<div align="center">
  <p>
    <strong>Need more details?</strong> Check out the <a href="README.md">Main README</a>.<br/>
    <strong>Want to help?</strong> Read our <a href="CONTRIBUTING.md">Contributing Guide</a>.
  </p>
</div>
