<div align="center">
  <img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" width="100" alt="JavOrganizer Logo" />
  <h1>🛡️ Security Policy</h1>
</div>

---

## 📦 Supported Versions

We only provide security updates for our latest releases. If you run into a security issue on an older version, please try upgrading first to see if it's already fixed before reporting it.

| Version | Status | Description |
| :---: | :---: | :--- |
| **1.3.x** | ✅ | **Supported:** Actively receiving security updates and fixes. |
| **< 1.3** | ❌ | **Unsupported:** Please upgrade to the latest version. |

## 🚨 Reporting a Vulnerability

> [!CAUTION]
> Please **do not open a public issue** for security vulnerabilities. Keeping it private helps protect other users while we work out a fix.

If you spot a security vulnerability in JavOrganizer, we'd appreciate it if you could report it to us through a private security advisory:

1. Head over to the **Security** tab of this repository.
2. Click **Report a vulnerability**.

When you report something, it helps us a lot if you include:
- Which component or feature is affected.
- Steps to reproduce it (the simpler, the better).
- The versions of Jellyfin you're seeing this on.
- A quick summary of what you think the impact could be.

> [!TIP]
> ⏱️ **Response Time:** We'll do our best to get back to you and acknowledge your report within **72 hours**.

## 🔍 Scope Notes

> [!IMPORTANT]
> JavOrganizer is just a **read-only metadata scraper**. It fetches info from public websites, caches that metadata locally, and won't ever mess with your media files.

Since the plugin only reads data, there are pretty limited ways it could be exploited. Mostly, we're looking at:

- **Outbound HTTP requests** to metadata sites (we use rate limiting here).
- **Local cache files** saved in the plugin's data folder.
- **The plugin API** (`/JavOrganizer/*`), though this already requires the same admin auth as the rest of your Jellyfin dashboard.

One thing we do care about: if a metadata site serves up malicious HTML intended to break or exploit our parser, that's definitely **in scope**. If you find something like that, please drop a sanitized sample of the page in your report so we can test it.

---

<div align="center">
  <p>Return to <a href="README.md">README</a></p>
</div>
