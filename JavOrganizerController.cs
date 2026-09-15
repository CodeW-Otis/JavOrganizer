using MediaBrowser.Controller;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Plugin API surface. Exposes the manual scan triggers for the Scan
/// buttons: a normal scan (videos missing metadata) and a deep re-scrape
/// (every video, cache purged first), plus the live progress state both
/// buttons display.
/// </summary>
[ApiController]
[Route("JavOrganizer")]
public sealed class JavOrganizerController : ControllerBase
{
    private readonly JavScanTrigger _scanTrigger;
    private readonly ILogger<JavOrganizerController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavOrganizerController"/> class.
    /// </summary>
    /// <param name="scanTrigger">The shared scan trigger service.</param>
    /// <param name="logger">Logger scoped to this controller.</param>
    public JavOrganizerController(JavScanTrigger scanTrigger, ILogger<JavOrganizerController> logger)
    {
        _scanTrigger = scanTrigger;
        _logger = logger;
    }

    /// <summary>
    /// Starts a library-wide scrape of every video missing JavOrganizer
    /// metadata (or stuck at "title only"), exactly like the startup
    /// auto-scan. Safe to call while a scan is already running: the request
    /// just returns "already running".
    /// </summary>
    /// <returns>200 with the started/running state.</returns>
    [HttpPost("Scan")]
    public IActionResult ScanNow()
    {
        var started = _scanTrigger.RequestScan();
        return Ok(new { started, message = started ? "Scan started." : "A scan is already running." });
    }

    /// <summary>
    /// Starts a deep re-scrape: the cache is purged for every video with a
    /// JAV code and every video is scraped fresh from the sites, with full
    /// metadata and image replacement. The heavyweight fix-it button.
    /// </summary>
    /// <returns>200 with the started/running state.</returns>
    [HttpPost("Scan/Deep")]
    public IActionResult ScanDeep()
    {
        var started = _scanTrigger.RequestScan(deep: true);
        return Ok(new { started, message = started ? "Deep re-scrape started." : "A scan is already running." });
    }

    /// <summary>
    /// Cancels the running scan, if any.
    /// </summary>
    /// <returns>200 with the cancelled state.</returns>
    [HttpPost("Scan/Cancel")]
    public IActionResult CancelScan()
    {
        _scanTrigger.Cancel();
        return Ok(new { cancelled = true, message = "Scan cancelled." });
    }

    /// <summary>
    /// Gets whether a scan is currently running and its live progress —
    /// how many videos the pass set out to refresh, how many are done and
    /// how many received metadata — plus the engine's current adaptive
    /// pressure, for the button's state display.
    /// </summary>
    /// <returns>200 with the scan state.</returns>
    [HttpGet("Scan/State")]
    public IActionResult ScanState()
    {
        return Ok(new
        {
            running = _scanTrigger.IsRunning,
            startedAt = _scanTrigger.StartedAt,
            deep = _scanTrigger.IsDeepScan,
            totalPending = _scanTrigger.TotalPending,
            completed = _scanTrigger.Completed,
            refreshed = _scanTrigger.Refreshed,
            stillMissing = _scanTrigger.StillMissing,
            enginePressure = Math.Round(AdaptiveThrottle.Pressure, 2),
            pacingMultiplier = Math.Round(AdaptiveThrottle.DelayMultiplier, 2)
        });
    }

    /// <summary>
    /// Gets the live health of every scraping site: enabled state and
    /// whether the engine is currently skipping it (temporary ban or
    /// repeated failures) with the reason. Powers the configuration
    /// page's site status panel. Cheap: in-memory state only, no network.
    /// </summary>
    /// <returns>200 with the site health list.</returns>
    [HttpGet("Sites/State")]
    public IActionResult SitesState()
    {
        return Ok(new { sites = JavScraperFactory.GetSiteHealth() });
    }

    /// <summary>
    /// Probes the configured FlareSolverr health endpoint once (works for
    /// the plugin-managed local instance and for a remote/Docker instance
    /// alike). Any HTTP answer proves the server is reachable; a 404 just
    /// means this build does not expose /health — the solver still works
    /// for requests. Used by the configuration page's FlareSolverr status
    /// badge.
    /// </summary>
    /// <returns>200 with the health result.</returns>
    [HttpGet("FlareSolverr/Health")]
    public async Task<IActionResult> FlareSolverrHealth()
    {
        var healthUrl = FlareSolverrUrls.HealthUrl;
        if (healthUrl is null)
        {
            return Ok(new { configured = false, healthy = false, message = "No FlareSolverr URL is configured." });
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync(healthUrl, HttpContext.RequestAborted).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return Ok(new { configured = true, healthy = true, status = (int)response.StatusCode, message = "FlareSolverr is up and ready to bypass Cloudflare." });
            }

            // A 404/405 answer still proves the FlareSolverr process is
            // alive and listening — only its /health route is missing
            // (older builds). The solver remains fully usable.
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed)
            {
                return Ok(new
                {
                    configured = true,
                    healthy = true,
                    status = (int)response.StatusCode,
                    message = "FlareSolverr is reachable (this build does not expose /health, but the solver is running)."
                });
            }

            return Ok(new
            {
                configured = true,
                healthy = false,
                status = (int)response.StatusCode,
                message = $"FlareSolverr answered HTTP {(int)response.StatusCode} on the health endpoint."
            });
        }
        catch (Exception ex)
        {
            return Ok(new { configured = true, healthy = false, message = $"FlareSolverr did not answer: {ex.Message}" });
        }
    }
}
