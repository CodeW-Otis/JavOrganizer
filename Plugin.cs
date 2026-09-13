using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Entry point of the JavOrganizer plugin. Discovers JAV product codes in
/// file names and fills <see cref="MediaBrowser.Controller.Entities.Video"/>
/// metadata by scraping JavLibrary.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Gets the unique plugin id used by the Jellyfin plugin infrastructure
    /// and by the configuration page to address this plugin's API.
    /// </summary>
    public const string PluginId = "e9e8bfe2-5e0f-4d1a-9a3c-7b7ea4e7581c";

    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// Called by the Jellyfin DI container at startup.
    /// </summary>
    /// <param name="applicationPaths">Server application paths service.</param>
    /// <param name="xmlSerializer">Server XML serializer, used for the configuration file.</param>
    /// <param name="logger">Logger scoped to the plugin entry point.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        Instance = this;

        // Factory-built scrapers (outside DI) log through the plugin's
        // logger once it exists.
        PluginLogger.Initialize(logger);

        logger.LogInformation("JavOrganizer plugin loaded. Data folder: {Path}", DataFolderPath);
    }

    /// <summary>
    /// Gets the singleton plugin instance, or <c>null</c> before DI construction.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets the display name of the plugin.
    /// </summary>
    public override string Name => "JavOrganizer";

    /// <summary>
    /// Gets the description shown in the plugin catalog.
    /// </summary>
    public override string Description => "JAV metadata from JavLibrary";

    /// <summary>
    /// Gets the stable GUID identifying this plugin across installations.
    /// </summary>
    public override Guid Id => Guid.Parse(PluginId);

    /// <summary>
    /// Gets the effective configuration, falling back to defaults before
    /// the plugin instance exists (unit tests, design-time tools).
    /// </summary>
    internal static PluginConfiguration EffectiveConfiguration => Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the absolute directory used for cached scraper responses.
    /// </summary>
    public string CacheDirectory => Path.Combine(DataFolderPath, "cache");

    /// <summary>
    /// Gets the plugin's pages: the settings page under Plugins →
    /// JavOrganizer, plus a main-menu "Scan" page that puts the manual
    /// scan buttons one click from the library.
    /// </summary>
    /// <returns>The page descriptors.</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            DisplayName = "JavOrganizer",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
            EnableInMainMenu = false
        };

        yield return new PluginPageInfo
        {
            Name = "JavOrganizerScan",
            DisplayName = "Scan",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.scanPage.html", GetType().Namespace),
            EnableInMainMenu = true
        };
    }
}
