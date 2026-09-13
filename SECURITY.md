# Security policy

## Supported versions

Only the latest release line (currently **1.3.x**) receives fixes.

| Version | Supported |
|---|---|
| 1.3.x | ✅ |
| < 1.3 | ❌ — upgrade first |

## Reporting a vulnerability

Please **do not open a public issue** for security problems.

Email the maintainers by opening a private security advisory:

1. Go to the **Security** tab of this repository.
2. Click **Report a vulnerability**.

Include: the affected component, a minimal reproduction, affected Jellyfin
lines, and your assessment of impact. You will receive a response within
72 hours.

## Scope notes

JavOrganizer is a **read-only metadata scraper**: it fetches public web
pages, stores the parsed metadata locally, and never modifies your media
files. Its "attack surface" is therefore:

- **Outbound HTTP** to metadata sites (bounded by per-site rate limiting).
- **Local cache files** under the plugin data folder.
- **The plugin API surface** (`/JavOrganizer/*`), which requires the same
  administrator authentication as the rest of the Jellyfin dashboard.

If you find a site that serves malicious markup designed to exploit the
parser, that is also in scope — include a sanitized sample of the page.
