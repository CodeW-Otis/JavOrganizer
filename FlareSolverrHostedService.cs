using System.Diagnostics;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Keeps a FlareSolverr instance running exactly as long as the Jellyfin
/// server does. It launches the bundled FlareSolverr executable when the
/// server starts (if an executable path is configured), waits for its HTTP
/// endpoint to become healthy, and kills the process when the server stops
/// so no stray browser service is left on the machine.
/// </summary>
/// <remarks>
/// When no executable is configured the service is inert: the plugin then
/// uses whatever FlareSolverr the user runs themselves (or none).
/// </remarks>
public sealed class FlareSolverrHostedService : IHostedService
{
    private readonly ILogger<FlareSolverrHostedService> _logger;
    private readonly IServerApplicationHost _appHost;
    private Process? _process;
    private WindowsKillOnCloseJob? _job;
    private CancellationTokenSource? _healthPollCancel;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlareSolverrHostedService"/> class.
    /// </summary>
    /// <param name="logger">Logger scoped to this service.</param>
    /// <param name="appHost">The running server host.</param>
    public FlareSolverrHostedService(ILogger<FlareSolverrHostedService> logger, IServerApplicationHost appHost)
    {
        _logger = logger;
        _appHost = appHost;
    }

    /// <summary>
    /// Gets the configured FlareSolverr executable path, or <c>null</c> when
    /// the plugin should not manage a process of its own.
    /// </summary>
    private static string? ExecutablePath
    {
        get
        {
            var path = Plugin.EffectiveConfiguration.FlaresolverrExecutablePath;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var exe = ExecutablePath;
        if (exe is null || !File.Exists(exe))
        {
            if (exe is not null)
            {
                _logger.LogWarning("Configured FlareSolverr executable not found: {Path}", exe);
            }

            // Nothing to manage here (Docker and non-Windows users point at
            // an external FlareSolverr URL instead); still probe the health
            // endpoint so the log states clearly whether it answers.
            await ProbeConfiguredHealthAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            // A previous server run that was force-killed can leave a
            // FlareSolverr (and its chromedriver) behind; the orphan locks
            // profile files and breaks new launches. Reap orphans first.
            KillOrphanFlareSolverrs();

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _process = Process.Start(psi);
            if (_process is null)
            {
                _logger.LogWarning("Failed to start FlareSolverr process");
                return;
            }

            // Bind the child to a kill-on-close job object so the OS kills
            // it even when this server is force-killed (StopAsync never
            // runs in that case). Software kill + job object together
            // cover graceful and abrupt termination.
            try
            {
                _job = WindowsKillOnCloseJob.TryCreate(_process);
                if (_job is null)
                {
                    _logger.LogDebug("FlareSolverr is not in a job object (non-Windows); relying on software kill only");
                }
            }
            catch (Exception ex)
            {
                // The child is already running; fall back to software-only
                // lifecycle management rather than aborting.
                _job = null;
                _logger.LogWarning(ex, "Failed to bind FlareSolverr to a kill-on-close job object; abrupt server termination may leave it running");
            }

            _logger.LogInformation("FlareSolverr started (pid {Pid}) alongside the server", _process.Id);

            // Wait for the HTTP endpoint to answer before scans begin.
            _healthPollCancel = new CancellationTokenSource();
            var healthy = await WaitForHealthyAsync(_healthPollCancel.Token).ConfigureAwait(false);
            if (healthy)
            {
                _logger.LogInformation("FlareSolverr is healthy and ready to bypass Cloudflare");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch FlareSolverr");
        }
    }

    /// <summary>
    /// One-shot probe of the configured FlareSolverr health endpoint,
    /// used when the plugin manages no process itself (Docker and other
    /// external deployments). Logs the outcome so operators can see at a
    /// glance whether their external FlareSolverr answers.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    private async Task ProbeConfiguredHealthAsync(CancellationToken ct)
    {
        var healthUrl = FlareSolverrUrls.HealthUrl;
        if (healthUrl is null)
        {
            _logger.LogInformation("FlareSolverr is not configured; Cloudflare-protected sites will only work without challenge");
            return;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await http.GetAsync(healthUrl, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("External FlareSolverr at {Url} is healthy and ready to bypass Cloudflare", FlareSolverrUrls.ApiUrl);
            }
            else if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed)
            {
                _logger.LogInformation("External FlareSolverr at {Url} is reachable (no /health route on this build; the solver works)", FlareSolverrUrls.ApiUrl);
            }
            else
            {
                _logger.LogWarning("External FlareSolverr at {Url} answered {Status} on the health endpoint", FlareSolverrUrls.ApiUrl, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("External FlareSolverr at {Url} did not answer the health endpoint: {Message}", FlareSolverrUrls.ApiUrl, ex.Message);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _healthPollCancel?.Cancel();
        _healthPollCancel?.Dispose();
        _healthPollCancel = null;

        if (_process is { HasExited: false } proc)
        {
            try
            {
                proc.Kill(entireProcessTree: true);
                _logger.LogInformation("FlareSolverr stopped (pid {Pid}) because the server is stopping", proc.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop FlareSolverr process");
            }
        }

        _process?.Dispose();
        _process = null;

        // Belt-and-braces: closing the job handle makes the OS kill
        // anything that survived the software kill above (and is what
        // takes care of a force-killed server, where StopAsync never
        // runs).
        _job?.Dispose();
        _job = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Kills any FlareSolverr process that is still running from a previous
    /// server lifetime, so the plugin's own instance starts clean.
    /// </summary>
    private void KillOrphanFlareSolverrs()
    {
        foreach (var orphan in Process.GetProcessesByName("flaresolverr"))
        {
            try
            {
                orphan.Kill(entireProcessTree: true);
                _logger.LogInformation("Stopped orphaned FlareSolverr (pid {Pid}) from a previous server run", orphan.Id);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to stop orphaned FlareSolverr (pid {Pid})", orphan.Id);
            }
        }
    }

    /// <summary>
    /// Polls the FlareSolverr health endpoint (derived from the configured
    /// URL — works for the plugin-managed local instance on Windows and
    /// for a remote/Docker FlareSolverr alike) until it answers or the
    /// token is cancelled. Gives up after roughly 60 seconds.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the endpoint answered; <c>false</c> on timeout.</returns>
    private static async Task<bool> WaitForHealthyAsync(CancellationToken ct)
    {
        var healthUrl = FlareSolverrUrls.HealthUrl;
        if (healthUrl is null)
        {
            // No FlareSolverr configured at all; nothing to wait for. The
            // plugin simply runs without Cloudflare fallback.
            return false;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                using var response = await http.GetAsync(healthUrl, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode
                    || response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    // Any HTTP answer proves the process is up. 404/405
                    // just mean this FlareSolverr build has no /health
                    // route — the solver itself still works.
                    return true;
                }
            }
            catch (Exception)
            {
                // Not up yet; keep polling.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }
}
