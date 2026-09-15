<div align="center">
  <img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" width="120" alt="JavOrganizer Logo" />
  <h1>Quick Start Guide</h1>
  <p><em>Get your library organized in a few minutes.</em></p>
  <img src="https://img.shields.io/badge/Time-5%20minutes-brightgreen" alt="Estimated Time: 5 minutes" />
</div>

---

This quick guide covers setting up JavOrganizer in Jellyfin. For full details, check out the [main README](README.md).

> [!NOTE]
> **Prerequisites**
> - Jellyfin server installed and running.
> - .NET runtime (provided directly by Jellyfin, no extra installs needed).

---

## 1️⃣ Install the plugin

**Quickest method (via repository):**

Copy this URL:

```text
https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/manifest.json
```

> 📋 **Click the copy button** (top-right of the box above) to copy the URL to your clipboard.

In Jellyfin: go to **Dashboard → Plugins → Repositories → ➕**, paste the URL, and click Save. Then go to **Dashboard → Plugins → Catalog**, find **JavOrganizer**, install the version matching your Jellyfin install, and restart when prompted.

**Manual installation (requires matching your Jellyfin version):**

| Your Jellyfin | Download |
|---|---|
| 12.0.x | `JavOrganizer-120-v1.5.1.0.zip` |
| 10.11.x | `JavOrganizer-1011-v1.5.1.0.zip` |
| 10.10.x | `JavOrganizer-1010-v1.5.1.0.zip` |
| 10.9.x | `JavOrganizer-109-v1.5.1.0.zip` |
| 10.8.x | `JavOrganizer-108-v1.5.1.0.zip` |

Grab the correct zip from the [Releases](../../releases/latest) page, then:

1. Extract the zip **contents** directly into `{Jellyfin data}/plugins/JavOrganizer/`
   (Windows: `%LocalAppData%\Jellyfin\plugins\JavOrganizer\` — Linux: `/var/lib/jellyfin/plugins/JavOrganizer/`).
2. Restart Jellyfin.
3. Verify in **Dashboard → Plugins**: *JavOrganizer 1.5.1* is listed.

> [!CAUTION]
> **Critical Step:** Make sure you extract the zip contents *into* the `JavOrganizer` folder, not a subfolder inside it.

✅ **Success check:** JavOrganizer 1.5.1 should now show up in **Dashboard → Plugins**.

---

## 2️⃣ Create the library

1. Go to **Dashboard → Libraries → Add Media Library**.
2. Set Content type to **Movies** and point it at your folder.
3. Under **Library settings → Metadata → Metadata downloaders**:
   - Move **JavOrganizer** to the top.
   - **Disable TMDb and other fetchers** (they won't match these files and just slow things down).
4. Under **Images**, keep only the fetchers you want. JavOrganizer handles covers (Primary) and scene screenshots (Backdrop).
5. Save and scan the library.

✅ **Success check:** The new library will appear on your Home screen and start scanning.

---

## 3️⃣ (Recommended) Set up Cloudflare bypass

Several metadata sources sit behind Cloudflare. Without a bypass, they will be skipped, which might result in missing data.

> **FlareSolverr is a separate program — it is NOT included in the plugin zip.** For full step-by-step install instructions, see the **[FlareSolverr install guide](README.md#️-flaresolverr--what-it-is-and-how-to-install-it-step-by-step)** in the main README.

**Short version:**

1. Install [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr):
   - **Windows**: download `flaresolverr_windows_x64.zip` from its [releases page](https://github.com/FlareSolverr/FlareSolverr/releases), extract to somewhere like `C:\Users\<you>\AppData\Local\FlareSolverr\`. Do **not** run it manually; the plugin can manage it.
   - **Docker**: `docker run -d --name flaresolverr -p 8191:8191 --restart unless-stopped ghcr.io/flaresolverr/flaresolverr:latest`
2. Configure in **Dashboard → Plugins → JavOrganizer**:
   - **FlareSolverr URL**: `http://localhost:8191/v1`
   - **FlareSolverr Executable Path**: the full path to `flaresolverr.exe` (leave empty if using Docker). The plugin will start and stop it alongside Jellyfin.

> [!TIP]
> If you're on Windows, setting the executable path lets JavOrganizer manage FlareSolverr for you.

✅ **Success check:** Jellyfin logs shouldn't show FlareSolverr connection errors during a scan.

---

## 4️⃣ Check file naming

The plugin uses product codes in the filename to identify videos:

- ✅ **Works**: `SSIS-406.mp4`, `abp982.mp4`, `[IPX-177] Title.mp4`, `Movie.FHD1080.ABP-123.mp4`, `IPX-1234_uncensored.mp4`
- ❌ **Won't scrape**: `FC2-PPV-1234567.mp4` (unsupported source), files without a standard letter+digits code.

Zero-padded variants (`ABP-00123`) are handled automatically.

✅ **Success check:** Your filenames contain clear product codes.

---

## 5️⃣ Run the first scan

Go to **Dashboard → Plugins → JavOrganizer**:

1. Click **Scan Library Now** to scrape missing metadata. Progress is shown on the page.
2. If anything is missing (like localized titles or covers), try **Deep Re-scrape Everything** once to force a fresh scrape across all sources.

After this:

- New files are scraped automatically during normal library scans.
- A scheduled task checks for unscraped or failed items every 6 hours.
- Actor collections and rankings update daily via the Scheduled Task **Update JavOrganizer Collections**.

✅ **Success check:** Covers, titles, metadata, and actors will start populating in your Jellyfin library.

### Where to find everyone

After the collections task has run once, your library gets two browse cards
that nest properly:

```
Female Actresses (JavOrganizer)   ← every actress, most-watched first
└── Actress: <name>               ← her card shows her photo
    └── her videos, newest first

Male Actors (JavOrganizer)
└── Actor: <name>
    └── his videos, newest first
```

Open a card to see the performer cards; open a performer to see their videos.
Each performer card uses the performer's photo, or a cover from one of their
own titles when no site published a portrait.

---

## 6️⃣ Tune settings (optional)

| Setting | Default | When to increase |
|---|---|---|
| Parallel Scrapes | 4 | To speed up the initial library scan |
| Max Sites Scraped at Once | 8 | To fetch more metadata per video |
| Delay Between Requests | 500 ms | Only lower this if you accept the risk of IP bans |

✅ **Success check:** Scrapes run at a speed that works for your setup.

---

## 🛠️ Troubleshooting

- **Plugin won't load** → You might have the wrong build for your Jellyfin version (see step 1).
- **Japanese titles / missing covers** → FlareSolverr might not be running or configured (see step 3). Try a Deep Re-scrape afterward.
- **A few items never scrape** → The code might not exist on the enabled sites. Check Debug logs for `No search results for '<code>'`.
- **Everything else** → Check the [Troubleshooting](README.md#troubleshooting) section of the README.

---

<div align="center">
  <p>
    <strong>Need more details?</strong> Check out the <a href="README.md">Main README</a>.<br/>
    <strong>Want to help?</strong> Read our <a href="CONTRIBUTING.md">Contributing Guide</a>.
  </p>
</div>
