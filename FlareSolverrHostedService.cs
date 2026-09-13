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
    /// Polls the FlareSolverr health endpoint until it answers or the token
    /// is cancelled. Gives up after roughly 60 seconds.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the endpoint answered; <c>false</c> on timeout.</returns>
    private static async Task<bool> WaitForHealthyAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                using var response = await http.GetAsync("http://localhost:8191/health", ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
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
