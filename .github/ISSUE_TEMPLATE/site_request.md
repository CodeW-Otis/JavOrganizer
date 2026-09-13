---
name: Site support / scraper fix
about: Request a new site or report a changed markup that broke an existing one
title: "[Site] "
labels: enhancement, scraper
assignees: ""
---

**Which site?**
Name + base URL, e.g. `example.com`.

**Is it a new site or a broken scraper?**
- [ ] New site
- [ ] Existing scraper stopped working (which site key: e.g. `sextb`)

**How does the site locate a video by code?**
- Canonical URL pattern (paste a real URL with the code replaced by `ABC-123`), or
- Search URL pattern.

```
https://example.com/en/ABC-123
```

**What data does the page contain?**
- [ ] Title
- [ ] Cover
- [ ] Release date
- [ ] Runtime
- [ ] Maker / studio
- [ ] Director
- [ ] Genres / tags
- [ ] Cast (actresses)
- [ ] Cast (male actors)
- [ ] Preview images

**Anything else?**
Age gate? Cloudflare? Login required? (Login-walled sites will not be added.)

---

💡 Sites that can be added declaratively (canonical/search URL + standard HTML fields) get picked up fastest — see `CONTRIBUTING.md` → "Adding a site".
