using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Scheduled task that prunes the plugin's cache of unused files. It runs
/// daily (default trigger: 04:00) and removes:
/// <list type="bullet">
/// <item>scrape records older than the configured cache TTL (they would be
/// re-fetched on the next scan anyway),</item>
/// <item>expired negative "not found" markers,</item>
/// <item>records for videos that no longer exist anywhere in the library —
/// covers left behind by files that were renamed, moved or deleted.</item>
/// </list>
/// The task is also runnable on demand from Dashboard → Scheduled Tasks.
/// </summary>
public sealed class JavCacheCleanupTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<JavCacheCleanupTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavCacheCleanupTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager used to list current video paths.</param>
    /// <param name="logger">Logger scoped to this task.</param>
    public JavCacheCleanupTask(ILibraryManager libraryManager, ILogger<JavCacheCleanupTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Clean JavOrganizer Cache";

    /// <inheritdoc />
    public string Key => "JavOrganizerCacheCleanup";

    /// <inheritdoc />
    public string Description => "Removes expired JavOrganizer scrape records, expired not-found markers, and cached metadata for videos that are no longer in the library.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var removedExpired = 0;
        var removedOrphans = 0;
        var removedNegatives = 0;

        // Phase 1: time-based cleanup — expired records and markers.
        progress.Report(5);
        (removedExpired, removedNegatives) = JavCache.RemoveExpired(_logger);

        // Phase 2: library-aware cleanup — records whose code no longer
        // matches any file name in the library are orphans.
        progress.Report(40);
        removedOrphans = await RemoveLibraryOrphansAsync(progress, cancellationToken).ConfigureAwait(false);

        var total = removedExpired + removedOrphans + removedNegatives;
        _logger.LogInformation(
            "JavOrganizer cache cleanup finished: {Expired} expired records, {Negatives} expired not-found markers, {Orphans} orphaned records removed ({Total} files total)",
            removedExpired,
            removedNegatives,
            removedOrphans,
            total);

        progress.Report(100);
    }

    /// <summary>
    /// Deletes cached records whose product code cannot be extracted from any
    /// video file name currently in the library.
    /// </summary>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of orphaned cache files removed.</returns>
    private async Task<int> RemoveLibraryOrphansAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Collect the codes of every video currently in the library.
        var liveCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var query = new InternalItemsQuery
            {
                Recursive = true,
                IsFolder = false,
                IncludeItemTypes = [BaseItemKind.Video, BaseItemKind.Movie]
            };

            var items = _libraryManager.GetItemList(query);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var code = JavCodeParser.ExtractCode(item.Path) ?? JavCodeParser.ExtractCode(item.Name);
                if (code is not null)
                {
                    liveCodes.Add(JavCodeParser.Normalize(code));
                }
            }
        }
        catch (Exception ex)
        {
            // The library query is an optimization, never a hard requirement;
            // on failure keep every record rather than risk deleting live ones.
            _logger.LogWarning(ex, "Could not enumerate library items; skipping orphan removal this run");
            return 0;
        }

        progress.Report(70);
        return await Task.Run(() => JavCache.RemoveOrphans(liveCodes, _logger), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Daily at 04:00, when library scans are unlikely to be running.
        yield return new TaskTriggerInfo
        {
#if JF_LEGACY_TASK_TRIGGER
            Type = "DailyTrigger",
#else
            Type = TaskTriggerInfoType.DailyTrigger,
#endif
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }
}
