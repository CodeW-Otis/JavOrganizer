using System.Collections.Concurrent;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// A per-key rate limiter: each site gets its own independent pacing
/// so hammering one site never impacts the others, and no site is ever hit
/// faster than its own configured interval regardless of how many workers
/// are scraping in parallel.
/// </summary>
/// <remarks>
/// <para>
/// Requests wait (asynchronously) for their turn instead of failing, so a
/// burst of parallel scrapes is smoothed into a polite per-site stream —
/// the core of the anti-ban strategy.</para>
/// <para>
/// <b>Anti-detect jitter</b>: the enforced spacing is jittered randomly
/// (between 60% and 140% of the configured interval) so the request pattern
/// never looks machine-regular. Sites that fingerprint traffic by its
/// clockwork regularity see an organic-looking stream instead. A shared
/// <see cref="Random"/> is used under a lock because <see cref="Random"/>
/// is not thread-safe.</para>
/// <para>
/// The interval is supplied as a function and re-read on every acquisition,
/// so changing the request delay in the configuration takes effect
/// immediately without a restart.</para>
/// </remarks>
internal sealed class PerSiteRateLimiter
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastRequest = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<TimeSpan> _minInterval;
    private readonly Random _jitter = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PerSiteRateLimiter"/> class.
    /// </summary>
    /// <param name="minIntervalPerSite">Supplies the minimum spacing between
    /// two requests to the same site; re-read live so configuration changes
    /// apply without a restart. Different sites are never throttled against
    /// each other.</param>
    public PerSiteRateLimiter(Func<TimeSpan> minIntervalPerSite)
    {
        _minInterval = minIntervalPerSite;
    }

    /// <summary>
    /// Acquires the given site's slot, waiting until at least the jittered
    /// spacing has passed since that site's previous request.
    /// </summary>
    /// <param name="siteKey">Site identifier (usually the scraper's name).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A disposable releasing the site slot.</returns>
    public async Task<IDisposable> AcquireAsync(string siteKey, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(siteKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Hold the site gate while waiting the spacing interval, so the
            // next request for the same site queues behind this one.
            if (_lastRequest.TryGetValue(siteKey, out var last))
            {
                var wait = NextSpacing() - (DateTime.UtcNow - last);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                }
            }

            _lastRequest[siteKey] = DateTime.UtcNow;
        }
        catch
        {
            gate.Release();
            throw;
        }

        return new Releaser(gate);
    }

    /// <summary>
    /// Produces the spacing for the next request: the configured interval
    /// with random jitter, so the traffic pattern is not perfectly regular.
    /// </summary>
    /// <returns>The jittered spacing interval.</returns>
    private TimeSpan NextSpacing()
    {
        var interval = _minInterval();
        if (interval <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        lock (_jitter)
        {
            // Between 60% and 140% of the base interval.
            var factor = 0.6 + (_jitter.NextDouble() * 0.8);
            return TimeSpan.FromMilliseconds(interval.TotalMilliseconds * factor);
        }
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
