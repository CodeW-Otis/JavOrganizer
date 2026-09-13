using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Starts a library scrape a moment after the server boots, so a freshly
/// installed or updated plugin begins filling the library automatically.
/// The actual scan logic lives in <see cref="JavScanTrigger"/>, shared
/// with the manual Scan button.
/// </summary>
public sealed class JavAutoScanService(JavScanTrigger scanTrigger, ILogger<JavAutoScanService> logger) : IHostedService
{
    private Task? _startupTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!Plugin.EffectiveConfiguration.AutoScanOnStartup)
        {
            logger.LogInformation("JavOrganizer auto-scan on startup is disabled");
            return Task.CompletedTask;
        }

        // Give the server a moment to finish its own startup work before
        // piling metadata refreshes on top of it.
        _startupTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                if (scanTrigger.RequestScan())
                {
                    logger.LogInformation("JavOrganizer auto-scan started after server startup");
                }
            }
            catch (OperationCanceledException)
            {
                // Server shutting down during the delay; nothing to do.
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _startupTask = null;
        return Task.CompletedTask;
    }
}
