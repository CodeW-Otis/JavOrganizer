using Jellyfin.Plugin.JavOrganizer;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;
#if JF_LEGACY_REGISTRATOR
using MediaBrowser.Common.Plugins;
#else
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
#endif

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Registers the plugin's services (metadata and image providers plus the
/// FlareSolverr lifecycle service) with the Jellyfin dependency-injection
/// container at startup.
/// </summary>
/// <remarks>
/// This is the one place the plugin must be built per Jellyfin version.
/// Jellyfin 10.9 moved <see cref="IPluginServiceRegistrator"/> from
/// MediaBrowser.Common.Plugins to MediaBrowser.Controller.Plugins and added
/// the host parameter to <see cref="IPluginServiceRegistrator.RegisterServices"/>.
/// Because the interface has to be implemented against whichever the running
/// server exposes, 10.8 needs its own build; 10.9 onward share one, and that
/// single build covers every later Jellyfin release too.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
#if JF_LEGACY_REGISTRATOR
    /// <summary>
    /// Adds the plugin's providers to the server's service collection.
    /// </summary>
    /// <param name="services">The server service collection.</param>
    public void RegisterServices(IServiceCollection services)
#else
    /// <summary>
    /// Adds the plugin's providers to the server's service collection.
    /// </summary>
    /// <param name="services">The server service collection.</param>
    /// <param name="applicationHost">The running server host.</param>
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
#endif
    {
        services.AddSingleton<JavMetadataProvider>();
        services.AddSingleton<JavImageProvider>();
        services.AddSingleton<JavPersonImageProvider>();

        // Manual + startup + scheduled scan trigger, shared by the
        // controller, the auto-scan hosted service and the periodic task.
        services.AddSingleton<JavScanTrigger>();
        services.AddSingleton<JavOrganizerController>();

        // Periodic library scan: newly added or previously failed videos
        // are scraped automatically without waiting for a restart.
        services.AddSingleton<JavScanScheduledTask>();

        // Scheduled auto-clean of unused cache files.
        services.AddSingleton<JavCacheCleanupTask>();

        // Per-person + Most Viewed / Most Liked collections.
        services.AddSingleton<JavCollectionsTask>();

        // Keeps the bundled FlareSolverr alive exactly as long as the server.
        services.AddHostedService<FlareSolverrHostedService>();

        // Scrape library items that lack JavOrganizer metadata after startup.
        services.AddHostedService<JavAutoScanService>();
    }
}
