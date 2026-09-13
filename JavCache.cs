using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// File-backed JSON cache of scraped <see cref="JavVideo"/> records, stored
/// under the plugin data folder. Failures are logged at debug level and never
/// thrown, so a broken cache never breaks a metadata refresh.
/// </summary>
/// <remarks>
/// <para>An in-memory <c>videoId → file</c> index is built lazily on first use
/// and refreshed when a write adds a record the index has not seen. This keeps
/// the image provider's per-request lookup at O(1) instead of deserializing
/// the whole cache directory each time.</para>
/// <para>Successful scrapes stay fresh for <see cref="PluginConfiguration.CacheHours"/>
/// (default 30 days). "Not found" results are remembered in marker files for
/// <see cref="PluginConfiguration.NegativeCacheHours"/> (default 7 days), so
/// a library full of unmatched files does not hammer the sites on every
/// library scan — only genuinely new files trigger new lookups.</para>
/// </remarks>
internal static class JavCache
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);
    private static readonly object IndexLock = new();
    private static Dictionary<string, string> _videoIdIndex = [];

    private const string NegativeMarker = "-";

    /// <summary>
    /// Gets the directory holding cached <see cref="JavVideo"/> JSON files.
    /// </summary>
    internal static string CacheDir => Plugin.Instance?.CacheDirectory
        ?? Path.Combine(Path.GetTempPath(), "JavOrganizer", "cache");

    /// <summary>
    /// Reads a fresh cached record for the code; stale entries are removed.
    /// </summary>
    /// <param name="normalizedCode">Cache-key form of the product code.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The cached record, or <c>null</c> when missing or stale.</returns>
    internal static JavVideo? TryRead(string normalizedCode, ILogger logger)
    {
        try
        {
            var file = CacheFile(normalizedCode);
            if (!File.Exists(file))
            {
                return null;
            }

            var maxAge = MaxAge;
            if (maxAge > TimeSpan.Zero && File.GetLastWriteTimeUtc(file) + maxAge < DateTime.UtcNow)
            {
                File.Delete(file);
                return null;
            }

            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize<JavVideo>(stream, ReadOptions);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed reading cache entry for '{Code}'", normalizedCode);
            return null;
        }
    }

    /// <summary>
    /// Persists a scraped record to the cache directory as indented JSON and
    /// records its video id in the lookup index.
    /// </summary>
    /// <param name="normalizedCode">Cache-key form of the product code.</param>
    /// <param name="video">The record to store.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    internal static void TryWrite(string normalizedCode, JavVideo video, ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(CacheFile(normalizedCode), JsonSerializer.Serialize(video, options));
            DeleteNegativeMarker(normalizedCode);

            if (!string.IsNullOrWhiteSpace(video.VideoId))
            {
                lock (IndexLock)
                {
                    _videoIdIndex[video.VideoId] = CacheFile(normalizedCode);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed writing cache entry for '{Code}'", normalizedCode);
        }
    }

    /// <summary>
    /// Remembers that no site had data for the code, so repeated library
    /// scans skip it until the negative entry expires.
    /// </summary>
    /// <param name="normalizedCode">Cache-key form of the product code.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    internal static void TryWriteNegative(string normalizedCode, ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(NegativeFile(normalizedCode), DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed writing negative cache entry for '{Code}'", normalizedCode);
        }
    }

    /// <summary>
    /// Reports whether a recent "not found" marker exists for the code.
    /// Expired markers are removed.
    /// </summary>
    /// <param name="normalizedCode">Cache-key form of the product code.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns><c>true</c> when the code was recently looked up and not found.</returns>
    internal static bool IsRecentlyNotFound(string normalizedCode, ILogger logger)
    {
        try
        {
            var file = NegativeFile(normalizedCode);
            if (!File.Exists(file))
            {
                return false;
            }

            var maxAge = NegativeMaxAge;
            if (maxAge > TimeSpan.Zero && File.GetLastWriteTimeUtc(file) + maxAge < DateTime.UtcNow)
            {
                File.Delete(file);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed reading negative cache entry for '{Code}'", normalizedCode);
            return false;
        }
    }

    /// <summary>
    /// Finds the cached record with the given site video id, used by the
    /// image provider to resolve cover URLs. O(1) via the in-memory index,
    /// with a one-time directory scan as fallback.
    /// </summary>
    /// <param name="videoId">Site internal video id.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The cached record, or <c>null</c> when not present.</returns>
    internal static JavVideo? FindByVideoId(string? videoId, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        try
        {
            // Fast path: the id is already indexed.
            lock (IndexLock)
            {
                if (_videoIdIndex.TryGetValue(videoId, out var indexed))
                {
                    var cached = TryReadFileFresh(indexed, logger);
                    if (cached is not null)
                    {
                        return cached;
                    }
                }
            }

            // Slow path (first lookup after a server restart): build the index
            // by scanning the directory once, then retry.
            var file = FindFileByVideoId(videoId, logger);
            if (file is null)
            {
                return null;
            }

            return TryReadFileFresh(file, logger);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed cache lookup for video id '{Id}'", videoId);
            return null;
        }
    }

    /// <summary>
    /// Scans the cache directory once and builds the videoId → file index.
    /// Only the header of each record is deserialized; expired records are
    /// skipped so a cover is never served from a stale entry.
    /// </summary>
    /// <param name="videoId">The video id being looked up.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The file holding the record, or <c>null</c> when absent.</returns>
    private static string? FindFileByVideoId(string videoId, ILogger logger)
    {
        if (!Directory.Exists(CacheDir))
        {
            return null;
        }

        var maxAge = MaxAge;
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? found = null;

        foreach (var file in Directory.EnumerateFiles(CacheDir, "*.json"))
        {
            try
            {
                if (maxAge > TimeSpan.Zero && File.GetLastWriteTimeUtc(file) + maxAge < DateTime.UtcNow)
                {
                    continue;
                }

                using var stream = File.OpenRead(file);
                var candidate = JsonSerializer.Deserialize<JavVideo>(stream, ReadOptions);
                if (candidate?.VideoId is null)
                {
                    continue;
                }

                index[candidate.VideoId] = file;
                if (string.Equals(candidate.VideoId, videoId, StringComparison.OrdinalIgnoreCase))
                {
                    found = file;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skipping unreadable cache file '{File}'", file);
            }
        }

        lock (IndexLock)
        {
            _videoIdIndex = index;
        }

        return found;
    }

    /// <summary>
    /// Reads one cache file if it exists and is not expired.
    /// </summary>
    /// <param name="file">Absolute path of the cache file.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The record, or <c>null</c> when missing or stale.</returns>
    private static JavVideo? TryReadFileFresh(string file, ILogger logger)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            var maxAge = MaxAge;
            if (maxAge > TimeSpan.Zero && File.GetLastWriteTimeUtc(file) + maxAge < DateTime.UtcNow)
            {
                return null;
            }

            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize<JavVideo>(stream, ReadOptions);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed reading cache file '{File}'", file);
            return null;
        }
    }

    /// <summary>
    /// Deletes negative "not found" markers belonging to the given items, so
    /// those codes are retried on the next scrape. Used by the auto-scan at
    /// startup, where stale markers (from rate-limit races or older bugs)
    /// would otherwise keep items unscraped forever.
    /// </summary>
    /// <param name="items">Library items still missing metadata.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The number of markers removed.</returns>
    internal static int SweepNegativeMarkersFor(IEnumerable<MediaBrowser.Controller.Entities.BaseItem> items, ILogger logger)
    {
        var swept = 0;
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                return 0;
            }

            // Collect the normalized codes of every pending item once.
            var pendingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var code = JavCodeParser.ExtractCode(item.Path) ?? JavCodeParser.ExtractCode(item.Name);
                if (code is null)
                {
                    continue;
                }

                var normalized = JavCodeParser.Normalize(code);
                if (normalized.Length > 0)
                {
                    pendingCodes.Add(normalized);
                }
            }

            foreach (var file in Directory.EnumerateFiles(CacheDir, "-*.missing"))
            {
                var code = Path.GetFileNameWithoutExtension(file).TrimStart('-');
                if (pendingCodes.Contains(code))
                {
                    try
                    {
                        File.Delete(file);
                        swept++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed sweeping negative marker '{File}'", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed sweeping negative markers");
        }

        return swept;
    }

    /// <summary>
    /// Builds a person-name → gender index ("female"/"male") from every
    /// cached scrape record, in one directory pass. Used by the collections
    /// task, which needs gender knowledge for the entire library at once.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>Person name to gender mapping; names of unclear gender are absent.</returns>
    internal static Dictionary<string, string> LoadGenderIndex(ILogger logger)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                return index;
            }

            foreach (var file in Directory.EnumerateFiles(CacheDir, "*.json"))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var record = JsonSerializer.Deserialize<JavVideo>(stream, ReadOptions);
                    if (record is null)
                    {
                        continue;
                    }

                    foreach (var actress in record.Actresses)
                    {
                        index.TryAdd(actress, "female");
                    }

                    foreach (var actor in record.MaleActors)
                    {
                        index.TryAdd(actor, "male");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Skipping unreadable cache file '{File}' while building the gender index", file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed building the gender index");
        }

        return index;
    }

    /// <summary>
    /// Reads the cached scrape record for a library item's product code
    /// (extracted from the item's path). Used by the collections task to
    /// look up per-video cast without knowing cache keys up front.
    /// </summary>
    /// <param name="itemPath">The library item's file path or name.</param>
    /// <param name="itemName">Fallback name when the path has no code.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The record, or <c>null</c> when the item has no cached scrape.</returns>
    internal static JavVideo? TryReadForItem(string? itemPath, string? itemName, ILogger logger)
    {
        var code = JavCodeParser.ExtractCode(itemPath) ?? JavCodeParser.ExtractCode(itemName);
        if (code is null)
        {
            return null;
        }

        var normalized = JavCodeParser.Normalize(code);
        return normalized.Length == 0 ? null : TryRead(normalized, logger);
    }

    /// <summary>
    /// Reports whether the cache record for the code was written within the
    /// given age window — used to throttle re-scrapes of thin records.
    /// Missing records count as not fresh.
    /// </summary>
    /// <param name="normalizedCode">Cache-key form of the product code.</param>
    /// <param name="window">Maximum age that counts as fresh.</param>
    /// <returns><c>true</c> when a record exists and is at most that old.</returns>
    internal static bool IsFreshFor(string normalizedCode, TimeSpan window)
    {
        try
        {
            var file = CacheFile(normalizedCode);
            return File.Exists(file)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) <= window;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes the cached record and any negative marker for the code, so
    /// the next scrape goes back to the sites. Used by the deep scan to
    /// force a fresh, complete multi-site scrape.
    /// </summary>
    /// <param name="normalizedCode">Cache-key form of the product code.</param>
    /// <returns><c>true</c> when anything was removed.</returns>
    internal static bool Purge(string normalizedCode)
    {
        var removed = false;
        try
        {
            var file = CacheFile(normalizedCode);
            if (File.Exists(file))
            {
                File.Delete(file);
                removed = true;
            }

            var marker = NegativeFile(normalizedCode);
            if (File.Exists(marker))
            {
                File.Delete(marker);
                removed = true;
            }

            if (removed)
            {
                lock (IndexLock)
                {
                    foreach (var key in _videoIdIndex
                        .Where(kv => string.Equals(kv.Value, file, StringComparison.OrdinalIgnoreCase))
                        .Select(kv => kv.Key)
                        .ToList())
                    {
                        _videoIdIndex.Remove(key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort purge only.
            System.Diagnostics.Debug.WriteLine($"JavCache.Purge failed: {ex.Message}");
        }
        return removed;
    }

    private static void DeleteNegativeMarker(string normalizedCode)
    {
        try
        {
            var file = NegativeFile(normalizedCode);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// Deletes every expired cache record and expired negative marker,
    /// regardless of library content. Used by the scheduled cleanup task.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The number of expired records and markers removed.</returns>
    internal static (int Records, int Negatives) RemoveExpired(ILogger logger)
        => RemoveExpired(CacheDir, logger);

    /// <summary>
    /// Deletes expired records and markers inside an explicit directory.
    /// The production path uses the plugin cache folder; tests pass their own.
    /// </summary>
    /// <param name="cacheDir">Directory holding the cache files.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The number of expired records and markers removed.</returns>
    internal static (int Records, int Negatives) RemoveExpired(string cacheDir, ILogger logger)
    {
        var removedRecords = 0;
        var removedNegatives = 0;

        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return (0, 0);
            }

            var now = DateTime.UtcNow;
            var maxAge = MaxAge;
            var negativeMaxAge = NegativeMaxAge;

            foreach (var file in Directory.EnumerateFiles(cacheDir))
            {
                try
                {
                    var age = now - File.GetLastWriteTimeUtc(file);

                    if (file.EndsWith(".missing", StringComparison.OrdinalIgnoreCase))
                    {
                        if (negativeMaxAge > TimeSpan.Zero && age > negativeMaxAge)
                        {
                            File.Delete(file);
                            removedNegatives++;
                        }
                    }
                    else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        // A zero TTL means records never expire on their own;
                        // they are then only removed by orphan cleanup.
                        if (maxAge > TimeSpan.Zero && age > maxAge)
                        {
                            File.Delete(file);
                            removedRecords++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed cleaning cache file '{File}'", file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed scanning cache directory for expired entries");
        }

        if (removedRecords + removedNegatives > 0)
        {
            logger.LogInformation("Removed {Records} expired records and {Negatives} expired not-found markers", removedRecords, removedNegatives);
        }

        return (removedRecords, removedNegatives);
    }

    /// <summary>
    /// Deletes records whose normalized code is not present in
    /// <paramref name="liveCodes"/> — cached metadata left behind by videos
    /// that were renamed, moved or deleted from the library.
    /// </summary>
    /// <param name="liveCodes">Normalized codes of every video currently in the library.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The number of orphaned cache files removed.</returns>
    internal static int RemoveOrphans(HashSet<string> liveCodes, ILogger logger)
        => RemoveOrphans(CacheDir, liveCodes, logger);

    /// <summary>
    /// Deletes orphaned records inside an explicit directory. The production
    /// path uses the plugin cache folder; tests pass their own.
    /// </summary>
    /// <param name="cacheDir">Directory holding the cache files.</param>
    /// <param name="liveCodes">Normalized codes of every video currently in the library.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The number of orphaned cache files removed.</returns>
    internal static int RemoveOrphans(string cacheDir, HashSet<string> liveCodes, ILogger logger)
    {
        var removed = 0;

        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return 0;
            }

            foreach (var file in Directory.EnumerateFiles(cacheDir, "*.json"))
            {
                try
                {
                    var code = Path.GetFileNameWithoutExtension(file);
                    if (!liveCodes.Contains(code))
                    {
                        File.Delete(file);
                        removed++;

                        lock (IndexLock)
                        {
                            _videoIdIndex = _videoIdIndex
                                .Where(kv => !string.Equals(kv.Value, file, StringComparison.OrdinalIgnoreCase))
                                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed orphan-checking cache file '{File}'", file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed scanning cache directory for orphaned entries");
        }

        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} orphaned cache records (videos no longer in the library)", removed);
        }

        return removed;
    }

    private static TimeSpan MaxAge =>
        TimeSpan.FromHours(Math.Max(0, Plugin.EffectiveConfiguration.CacheHours));

    private static TimeSpan NegativeMaxAge =>
        TimeSpan.FromHours(Math.Max(0, Plugin.EffectiveConfiguration.NegativeCacheHours));

    private static string CacheFile(string normalizedCode) => Path.Combine(CacheDir, $"{normalizedCode}.json");

    private static string NegativeFile(string normalizedCode) => Path.Combine(CacheDir, $"{NegativeMarker}{normalizedCode}.missing");
}
