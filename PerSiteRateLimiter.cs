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
/// <b>Human-like jitter</b>: the enforced spacing follows a human
/// distribution — most gaps are short (65–125% of the base interval) and
/// roughly one in eight is a longer "reading" pause (up to ~2.2×) — so the
/// request pattern looks like a person browsing, never machine-regular.
/// Sites that fingerprint traffic by its clockwork regularity see an
/// organic-looking stream instead.</para>
/// <para>
/// <b>Adaptive pacing</b>: the spacing is additionally stretched by
/// <see cref="AdaptiveThrottle.DelayMultiplier"/> (1× at rest up to ~4×
/// under sustained pushback), so when any site starts rate-limiting, the
/// engine-wide pacing eases off and then relaxes back automatically as
/// pressure decays. A shared <see cref="Random"/> is used under a lock
/// because <see cref="Random"/> is not thread-safe.</para>
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
    /// with human-like random jitter, stretched by the global adaptive
    /// multiplier when sites have recently pushed back. The result is never
    /// perfectly regular and never aggressive when the engine is under
    /// pressure.
    /// </summary>
    /// <returns>The jittered spacing interval.</returns>
    private TimeSpan NextSpacing()
    {
        var interval = _minInterval();
        if (interval <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        double factor;
        lock (_jitter)
        {
            // Human distribution: mostly quick, occasionally a longer
            // "reading" pause — never clockwork-regular.
            factor = _jitter.NextDouble() < 0.125
                ? 1.5 + (_jitter.NextDouble() * 0.7)   // ~12.5% longer pause.
                : 0.65 + (_jitter.NextDouble() * 0.6); // quick hop.
        }

        var spacing = interval.TotalMilliseconds * factor;

        // Global adaptive pacing: stretch when sites are pushing back.
        spacing *= AdaptiveThrottle.DelayMultiplier;
        return TimeSpan.FromMilliseconds(spacing);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
