using System.Collections.Concurrent;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Runs the "scrape everything" pass on demand. The startup auto-scan, the
/// periodic scheduled scan and the manual Scan buttons all go through this
/// one service, so only one pass ever runs at a time and all of them report
/// the same progress state.
/// </summary>
/// <remarks>
/// Two modes:
/// <list type="bullet">
/// <item><b>Normal</b> — refreshes videos that are missing JavOrganizer
/// metadata (no provider id), or whose scrape produced too little (no
/// date and no cover — the "title only" state), retrying negatively cached
/// codes. Cached codes cost nothing.</item>
/// <item><b>Deep</b> — purges the cache for every video with a parseable
/// code and re-scrapes it from the sites, with full metadata and image
/// replacement. The manual "Deep Re-scrape Everything" button.</item>
/// </list>
/// </remarks>
public sealed class JavScanTrigger
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDirectoryService _directoryService;
    private readonly ILogger<JavScanTrigger> _logger;
    private readonly object _runLock = new();
    private Task? _running;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavScanTrigger"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager used to find videos.</param>
    /// <param name="directoryService">Directory service required by refresh options.</param>
    /// <param name="logger">Logger scoped to this service.</param>
    public JavScanTrigger(ILibraryManager libraryManager, IDirectoryService directoryService, ILogger<JavScanTrigger> logger)
    {
        _libraryManager = libraryManager;
        _directoryService = directoryService;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether a scan pass is currently running.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_runLock)
            {
                return _running is { IsCompleted: false };
            }
        }
    }

    /// <summary>
    /// Gets the UTC time the current or last scan started, when known.
    /// </summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the current/last pass was a deep
    /// re-scrape (cache-busting full pass).
    /// </summary>
    public bool IsDeepScan { get; private set; }

    /// <summary>
    /// Gets the number of videos this pass set out to refresh.
    /// </summary>
    public int TotalPending { get; private set; }

    /// <summary>
    /// Gets how many videos this pass has finished so far (any outcome).
    /// </summary>
    public int Completed => Volatile.Read(ref _completedField);

    /// <summary>
    /// Gets how many of the finished videos received metadata.
    /// </summary>
    public int Refreshed => Volatile.Read(ref _refreshedField);

    /// <summary>
    /// Gets how many of the finished videos still ended without JavOrganizer
    /// metadata after the refresh attempt.
    /// </summary>
    public int StillMissing => Volatile.Read(ref _stillMissingField);

    private int _completedField;
    private int _refreshedField;
    private int _stillMissingField;

    /// <summary>
    /// Requests a normal scan pass: refreshes videos missing JavOrganizer
    /// metadata or stuck with title-only data. Returns false when a pass is
    /// already running.
    /// </summary>
    /// <returns><c>true</c> when this call started a new scan.</returns>
    public bool RequestScan() => RequestScan(deep: false);

    /// <summary>
    /// Requests a scan pass. Returns false when one is already running.
    /// The deep pass purges the cache for every video with a parseable code
    /// first, so every video is re-scraped from the sites with fresh data.
    /// </summary>
    /// <param name="deep"><c>true</c> to re-scrape everything, cache-busting.</param>
    /// <returns><c>true</c> when this call started a new scan.</returns>
    public bool RequestScan(bool deep)
    {
        lock (_runLock)
        {
            if (_running is { IsCompleted: false })
            {
                return false;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            StartedAt = DateTime.UtcNow;
            IsDeepScan = deep;
            TotalPending = 0;
            Volatile.Write(ref _completedField, 0);
            Volatile.Write(ref _refreshedField, 0);
            Volatile.Write(ref _stillMissingField, 0);
            _running = Task.Run(() => RunAsync(deep, token), token);
            return true;
        }
    }

    /// <summary>
    /// Cancels the running scan, if any.
    /// </summary>
    public void Cancel()
    {
        lock (_runLock)
        {
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// Lists every video in the library together with its parseable
    /// product code (path first, item name as fallback — items without a
    /// usable path were silently skipped before, which is one reason some
    /// videos never scraped).
    /// </summary>
    /// <returns>Videos with their extracted codes; videos without codes are absent.</returns>
    private List<(BaseItem Item, string Code)> ListVideosWithCodes()
    {
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IsFolder = false,
            IncludeItemTypes = [BaseItemKind.Video, BaseItemKind.Movie]
        };

        var items = _libraryManager.GetItemList(query) ?? [];
        var result = new List<(BaseItem, string)>(items.Count);
        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var code = JavCodeParser.ExtractCode(item.Path) ?? JavCodeParser.ExtractCode(item.Name);
            if (code is not null)
            {
                result.Add((item, code));
            }
        }

        return result;
    }

    /// <summary>
    /// Scans the library for videos that need a metadata refresh and
    /// refreshes them in parallel. Mirrors the startup auto-scan exactly.
    /// </summary>
    /// <param name="deep"><c>true</c> to purge caches and re-scrape everything.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task RunAsync(bool deep, CancellationToken ct)
    {
        try
        {
            var videos = ListVideosWithCodes();
            if (videos.Count == 0)
            {
                _logger.LogInformation("JavOrganizer scan: no videos with parseable JAV codes found in the library");
                return;
            }

            List<BaseItem> pending;
            if (deep)
            {
                // Deep pass: every video with a code. Each item's cache is
                // purged individually right before its refresh (not upfront),
                // so cancelling the pass keeps the cache — and the library —
                // fully intact for everything not yet reached.
                pending = videos.Select(v => v.Item).ToList();
                _logger.LogInformation(
                    "JavOrganizer deep scan: re-scraping all {Count} videos (per-item cache purge)",
                    pending.Count);
            }
            else
            {
                // Normal pass: no provider id yet, the item is stuck with
                // title-only data (no date and no cover), a sparse scrape that
                // never got its genres/studio/overview, or — when the language
                // is English — its title came back Japanese-heavy, so one
                // refresh can upgrade it. Each condition heals automatically
                // on the scheduled scans.
                var wantEnglish = Plugin.EffectiveConfiguration.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
                pending = videos
                    .Where(v => !v.Item.ProviderIds.TryGetValue(JavMetadataProvider.ProviderIdKey, out _)
                        || IsTitleOnly(v.Item)
                        || IsSparselyScraped(v.Item)
                        || (wantEnglish && IsJapaneseTitled(v.Item)))
                    .Select(v => v.Item)
                    .ToList();

                _logger.LogInformation(
                    "JavOrganizer scan: {Total} videos in the library, {Pending} need JavOrganizer metadata",
                    videos.Count,
                    pending.Count);
            }

            TotalPending = pending.Count;
            if (TotalPending == 0)
            {
                return;
            }

            // Retry codes that were previously marked "not found" — stale
            // markers from rate-limit races or older bugs self-heal here.
            var swept = JavCache.SweepNegativeMarkersFor(pending, _logger);
            if (swept > 0)
            {
                _logger.LogInformation("JavOrganizer scan: cleared {Count} stale not-found markers for another attempt", swept);
            }

            // Deep pass: map each item to its normalized code so the worker
            // can purge exactly that item's cache just before refreshing it.
            var codesByItem = new Dictionary<Guid, string>();
            if (deep)
            {
                foreach (var (item, code) in videos)
                {
                    codesByItem[item.Id] = JavCodeParser.Normalize(code);
                }
            }

            var options = new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = true,
                ReplaceAllImages = true,
                IsAutomated = true,
                ForceSave = false
            };

            var maxConcurrent = Math.Max(1, Plugin.EffectiveConfiguration.MaxConcurrentScrapes);
            _logger.LogInformation(
                "JavOrganizer {Mode}scan running with {Workers} parallel workers, up to {Sites} sites per code",
                deep ? "deep" : "normal",
                maxConcurrent,
                Math.Clamp(Plugin.EffectiveConfiguration.MaxSitesPerScrape, 1, 20));

            var queue = new ConcurrentQueue<BaseItem>(pending);
            var workers = Enumerable.Range(0, Math.Min(maxConcurrent, pending.Count))
                .Select(_ => WorkerAsync(queue, options, ct, deep ? codesByItem : null))
                .ToArray();

            var completed = await Task.WhenAll(workers).ConfigureAwait(false);
            _logger.LogInformation(
                "JavOrganizer {Mode}scan finished: {Total} items processed, {Refreshed} refreshed with metadata, {Missing} still without",
                deep ? "deep" : "normal",
                Completed,
                Refreshed,
                StillMissing);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("JavOrganizer scan cancelled after {Completed}/{Total} items", Completed, TotalPending);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JavOrganizer scan stopped unexpectedly");
        }
    }

    /// <summary>
    /// Reports whether an item carries a JavOrganizer scrape that produced
    /// nothing but a name — the "shows only title" state. Such items are
    /// re-scraped by the normal scan so they heal automatically.
    /// </summary>
    private static bool IsTitleOnly(BaseItem item)
    {
        // Items without the provider id are already pending by the first
        // condition; this is about scraped-but-empty items.
        if (!item.ProviderIds.TryGetValue(JavMetadataProvider.ProviderIdKey, out _))
        {
            return false;
        }

        try
        {
            return !item.PremiereDate.HasValue && !item.HasImage(ImageType.Primary);
        }
        catch (Exception)
        {
            // Image inspection may fail for exotic items; treat as fine.
            return false;
        }
    }

    /// <summary>
    /// Reports whether an item carries a JavOrganizer scrape that is missing
    /// the fields a complete scrape always produces — genres, studios and the
    /// overview block. A scrape can come back partial (a site answers with a
    /// title and cast but nothing else), and because such an item already has
    /// a provider id neither of the other conditions ever revisits it, so the
    /// gaps persisted forever even though the sites still hold the data.
    /// </summary>
    /// <remarks>
    /// Re-scraping these is cheap and self-limiting: the metadata provider
    /// serves a substantive cached record without touching the network, so an
    /// item the sites genuinely have no genres for is looked up once and then
    /// answered from cache on every later pass.
    /// </remarks>
    /// <param name="item">The library item to inspect.</param>
    /// <returns><c>true</c> when the scraped metadata is incomplete.</returns>
    internal static bool IsSparselyScraped(BaseItem item)
    {
        // Items without the provider id are already pending by the first
        // condition; this is about items that were scraped but thinly.
        if (!item.ProviderIds.TryGetValue(JavMetadataProvider.ProviderIdKey, out _))
        {
            return false;
        }

        try
        {
            // Genres are the reliable signal: the plugin always writes the
            // overview and a studio when a scrape provides them, but a site
            // that only knows a title and a cast yields none of the three.
            return item.Genres.Length == 0
                || item.Studios.Length == 0
                || string.IsNullOrWhiteSpace(item.Overview);
        }
        catch (Exception)
        {
            // Metadata inspection may fail for exotic items; treat as fine.
            return false;
        }
    }

    /// <summary>
    /// Reports whether an item's display name contains any Japanese (CJK)
    /// characters while the plugin language is English. A fully-English
    /// library has none, so even one CJK character — a short "同棲 LOVE
    /// STORY" style title is mostly Latin — marks the item for one English
    /// retry on the scheduled scans.
    /// </summary>
    private static bool IsJapaneseTitled(BaseItem item)
    {
        var name = item.Name;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var ch in name)
        {
            if (ch is >= '\u3040' and <= '\u30FF' or >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One worker of the parallel scan pool: pulls items off the shared
    /// queue and refreshes them until the queue is empty or cancelled,
    /// updating the shared progress counters as it goes. Between items the
    /// worker honors the global adaptive pressure: when sites have been
    /// pushing back (429s, bans), the worker pauses briefly so the engine
    /// as a whole eases off; when everything is healthy it moves at full
    /// speed with no added delay. In a deep pass each item's cache is
    /// purged immediately before its refresh, so a cancelled pass leaves
    /// everything not yet reached untouched.
    /// </summary>
    /// <param name="queue">Shared queue of items awaiting refresh.</param>
    /// <param name="options">Refresh options shared by all workers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="codesByItem">Deep pass only: item id → normalized code, purged per item.</param>
    /// <returns>The number of items this worker refreshed.</returns>
    private async Task<int> WorkerAsync(ConcurrentQueue<BaseItem> queue, MetadataRefreshOptions options, CancellationToken ct, Dictionary<Guid, string>? codesByItem = null)
    {
        var refreshed = 0;
        while (!ct.IsCancellationRequested && queue.TryDequeue(out var item))
        {
            try
            {
                // Ease off when the sites have recently pushed back; a no-op
                // at full speed when everything is healthy.
                await AdaptiveThrottle.MaybePauseAsync(ct).ConfigureAwait(false);

                if (codesByItem is not null
                    && codesByItem.TryGetValue(item.Id, out var deepCode)
                    && deepCode.Length > 0)
                {
                    JavCache.Purge(deepCode);
                }

                await item.RefreshMetadata(options, ct).ConfigureAwait(false);
                refreshed++;

                // A successful refresh leaves the provider id behind;
                // anything without it stays counted as missing.
                if (item.ProviderIds.TryGetValue(JavMetadataProvider.ProviderIdKey, out _))
                {
                    Interlocked.Increment(ref _refreshedField);
                }
                else
                {
                    Interlocked.Increment(ref _stillMissingField);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Scan refresh failed for '{Name}'", item.Name);
                Interlocked.Increment(ref _stillMissingField);
            }
            finally
            {
                Interlocked.Increment(ref _completedField);
            }
        }

        return refreshed;
    }
}
