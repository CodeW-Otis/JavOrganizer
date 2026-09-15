namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Global congestion controller for the scraping engine: fast when the
/// sites are happy, politely slower the moment they push back.
/// </summary>
/// <remarks>
/// <para>
/// Rate-limit responses (429/503) and ban detections feed <b>pressure</b>
/// into this controller from every scraper. Pressure decays exponentially
/// (90-second half-life) so a brief hiccup does not slow the engine for
/// long, while sustained pushback stretches request spacing up to four
/// times until sites relax.</para>
/// <para>
/// This is the "human-like but fast" balance: when nothing complains, the
/// engine runs at full speed with organic jitter; as soon as a site
/// signals overload, the whole engine eases off — exactly like a person
/// slowing down when pages start refusing to load — and speeds back up
/// automatically once the pressure decays.</para>
/// <para>
/// All members are thread-safe; state is a single double guarded by a
/// lock, so reporting and reading costs are negligible even with many
/// parallel workers.</para>
/// </remarks>
internal static class AdaptiveThrottle
{
    /// <summary>Half-life of the pressure signal: how quickly the engine
    /// relaxes back to full speed after pushback stops.</summary>
    private static readonly TimeSpan PressureHalfLife = TimeSpan.FromSeconds(90);

    /// <summary>Maximum stretch applied to request spacing at full
    /// pressure (4× together with the per-site jitter).</summary>
    private const double MaxStretch = 3.0;

    private static readonly object Gate = new();
    private static double _pressure;
    private static DateTime _lastUpdate = DateTime.UtcNow;

    /// <summary>
    /// Reports site pushback (a rate-limit response, a ban page) and raises
    /// the global pressure. Callers pass the severity: minor signals such
    /// as a single 429 contribute a little; explicit bans contribute a lot.
    /// </summary>
    /// <param name="severity">How strong the signal is, roughly 0.3–1.0.</param>
    internal static void ReportPressure(double severity)
    {
        if (severity <= 0)
        {
            return;
        }

        lock (Gate)
        {
            var current = DecayedPressureLocked();
            _pressure = Math.Min(1.0, current + (severity * 0.5));
            _lastUpdate = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Reports a successful fetch; nudges pressure down slightly so a
    /// healthy run recovers a bit faster than pure time decay alone.
    /// </summary>
    internal static void ReportSuccess()
    {
        lock (Gate)
        {
            _pressure = Math.Max(0, DecayedPressureLocked() - 0.02);
            _lastUpdate = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Gets the current pressure in the range 0–1 after applying time decay.
    /// </summary>
    internal static double Pressure
    {
        get
        {
            lock (Gate)
            {
                return DecayedPressureLocked();
            }
        }
    }

    /// <summary>
    /// Gets the multiplier request spacing is stretched by right now:
    /// 1.0 (no pressure) up to <c>1 + MaxStretch</c> (full pressure).
    /// </summary>
    internal static double DelayMultiplier => 1.0 + (Pressure * MaxStretch);

    /// <summary>
    /// Pauses a scan worker when the engine is under pressure. With no or
    /// little pressure this returns immediately (full speed); as pressure
    /// rises the pause grows into the seconds range, spacing whole videos
    /// further apart until the sites relax.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task MaybePauseAsync(CancellationToken ct)
    {
        const double PauseThreshold = 0.3;
        var pressure = Pressure;
        if (pressure < PauseThreshold)
        {
            return;
        }

        // 0.6–3.2 s at full pressure, jittered so parallel workers do not
        // resume in lockstep.
        var ms = 3200 * pressure * (0.75 + (Random.Shared.NextDouble() * 0.5));
        await Task.Delay(TimeSpan.FromMilliseconds(ms), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies exponential decay for the time elapsed since the last
    /// update and returns the effective pressure. Must be called under
    /// <see cref="Gate"/>.
    /// </summary>
    private static double DecayedPressureLocked()
    {
        var elapsed = DateTime.UtcNow - _lastUpdate;
        if (elapsed <= TimeSpan.Zero)
        {
            return _pressure;
        }

        _pressure *= Math.Exp(-elapsed.TotalSeconds / PressureHalfLife.TotalSeconds);
        _lastUpdate = DateTime.UtcNow;
        return _pressure;
    }
}
