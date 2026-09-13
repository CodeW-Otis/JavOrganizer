<div align="center">
  <img src="https://raw.githubusercontent.com/CodeW-Otis/JavOrganizer/main/Logo.png" width="100" alt="JavOrganizer Logo" />
  <h1>🛡️ Security Policy</h1>
</div>

---

## 📦 Supported Versions

Only the latest release line receives security updates. If you are experiencing a security issue on an older version, please upgrade first before reporting.

| Version | Status | Description |
| :---: | :---: | :--- |
| **1.3.x** | ✅ | **Supported:** Actively receiving security updates and fixes. |
| **< 1.3** | ❌ | **Unsupported:** Please upgrade to the latest version. |

## 🚨 Reporting a Vulnerability

> [!CAUTION]
> Please **do not open a public issue** for security vulnerabilities. This helps protect other users while we work on a fix.

If you believe you have found a security vulnerability in JavOrganizer, please report it to us by opening a private security advisory:

1. Go to the **Security** tab of this repository.
2. Click **Report a vulnerability**.

When reporting, please include:
- The affected component or feature.
- A minimal, reproducible example.
- Affected Jellyfin versions.
- Your assessment of the impact.

> [!TIP]
> ⏱️ **Response Time:** You will receive a response acknowledging your report within **72 hours**.

## 🔍 Scope Notes

> [!IMPORTANT]
> JavOrganizer is a **read-only metadata scraper**. It fetches public web pages, stores the parsed metadata locally, and never modifies your media files.

Because of its read-only nature, the "attack surface" is limited to:

- **Outbound HTTP requests** to metadata sites (bounded by per-site rate limiting).
- **Local cache files** stored securely under the plugin data folder.
- **The plugin API surface** (`/JavOrganizer/*`), which requires the same administrator authentication as the rest of the Jellyfin dashboard.

If you find a metadata site that serves malicious markup designed to exploit our parser, that is **in scope**. Please include a sanitized sample of the page in your report.

---

<div align="center">
  <p>Return to <a href="README.md">README</a></p>
</div>
