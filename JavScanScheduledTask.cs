using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Scheduled task that periodically runs the JavOrganizer library scan —
/// the same pass the server runs at startup and the Scan button runs on
/// demand. This is what makes scraping automatic for files that are added
/// while the server keeps running: without it, a video that failed its
/// first scrape (a transient Cloudflare block, a site being down) would
/// sit with just its file name until the next server restart.
/// </summary>
/// <remarks>
/// Runs every 6 hours by default (configurable like any scheduled task via
/// Dashboard → Scheduled Tasks). The pass itself only touches videos that
/// still lack JavOrganizer metadata or are stuck at "title only"; cached
/// videos are skipped, so a recurring run over a fully scraped library
/// costs a single cheap library query.
/// </remarks>
public sealed class JavScanScheduledTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly JavScanTrigger _scanTrigger;
    private readonly ILogger<JavScanScheduledTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavScanScheduledTask"/> class.
    /// </summary>
    /// <param name="scanTrigger">The shared scan trigger service.</param>
    /// <param name="logger">Logger scoped to this task.</param>
    public JavScanScheduledTask(JavScanTrigger scanTrigger, ILogger<JavScanScheduledTask> logger)
    {
        _scanTrigger = scanTrigger;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Scan JavOrganizer Library";

    /// <inheritdoc />
    public string Key => "JavOrganizerLibraryScan";

    /// <inheritdoc />
    public string Description => "Scrapes videos that are still missing JavOrganizer metadata — the same pass as the startup auto-scan and the Scan button, so files added or failed since the last pass are filled automatically.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (_scanTrigger.RequestScan())
        {
            _logger.LogInformation("JavOrganizer scheduled scan started");
        }
        else
        {
            _logger.LogInformation("JavOrganizer scheduled scan skipped: a scan is already running");
        }

        // The scan trigger reports its own progress through the state
        // endpoint; the scheduled task itself returns immediately so the
        // scheduler is not blocked for the duration of a large pass.
        progress.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Every 6 hours: newly added or previously failed videos get
        // picked up without waiting for a server restart.
        yield return new TaskTriggerInfo
        {
#if JF_LEGACY_TASK_TRIGGER
            Type = "IntervalTrigger",
            IntervalTicks = TimeSpan.FromHours(6).Ticks
#else
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(6).Ticks
#endif
        };
    }
}
