using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Resolves the FlareSolverr base URL and health endpoint from the plugin
/// configuration, on every platform.
/// </summary>
/// <remarks>
/// <para>
/// Docker and non-Windows deployments point the plugin at an externally
/// run FlareSolverr (a container, a NAS service). Windows users can
/// additionally let the plugin manage a local executable; in that mode the
/// <b>same</b> configured URL must be used for API calls and health
/// checks, so there is exactly one source of truth. When the URL is empty
/// but a Windows executable is configured, the plugin assumes the
/// standard local instance on port 8191.</para>
/// <para>
/// This makes every FlareSolverr interaction — API posts, health polling,
/// the UI health badge — work identically on Windows, Linux, macOS and
/// Docker.</para>
/// </remarks>
internal static partial class FlareSolverrUrls
{
    /// <summary>The standard FlareSolverr port.</summary>
    internal const int DefaultPort = 8191;

    /// <summary>
    /// Environment variable that supplies the FlareSolverr URL when the
    /// plugin has no loaded configuration. It exists for the offline test
    /// harnesses, which run the scrapers outside Jellyfin: without it every
    /// Cloudflare-protected site there reports "blocked" no matter how the
    /// server is actually configured, which makes the harness useless for
    /// answering "does scraping work?". A real server always has
    /// <see cref="Plugin.Instance"/> and never consults this.
    /// </summary>
    internal const string UrlOverrideVariable = "JAVORGANIZER_FLARESOLVERR_URL";

    /// <summary>
    /// Gets the base API URL derived from the configuration, or
    /// <c>null</c> when no FlareSolverr is configured. A URL that already
    /// ends in <c>/v1</c> (the format the settings page suggests) is used
    /// as-is; a bare server URL gets the <c>/v1</c> suffix appended.
    /// </summary>
    internal static string? ApiUrl
    {
        get
        {
            var configured = Plugin.EffectiveConfiguration.FlareSolverrUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return NormalizeApiUrl(configured);
            }

            // No loaded configuration (test harness / design-time tool):
            // honour the explicit override when one is present.
            if (Plugin.Instance is null)
            {
                var fromEnvironment = Environment.GetEnvironmentVariable(UrlOverrideVariable)?.Trim();
                if (!string.IsNullOrWhiteSpace(fromEnvironment))
                {
                    return NormalizeApiUrl(fromEnvironment);
                }
            }

            // No URL configured: on Windows, a managed executable implies
            // the standard local instance. Other platforms have no default
            // — FlareSolverr must be configured explicitly.
            var exe = Plugin.EffectiveConfiguration.FlaresolverrExecutablePath?.Trim();
            if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(exe))
            {
                return $"http://localhost:{DefaultPort}/v1";
            }

            return null;
        }
    }

    /// <summary>
    /// Normalizes a configured FlareSolverr URL to its API form: trailing
    /// slashes are trimmed, and exactly one <c>/v1</c> suffix is present —
    /// never two (a URL configured as <c>…/v1</c> is kept as-is).
    /// </summary>
    /// <param name="configured">The raw configured URL.</param>
    /// <returns>The normalized API URL.</returns>
    private static string? NormalizeApiUrl(string configured)
    {
        var trimmed = configured.Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/v1";
    }

    /// <summary>
    /// Gets the health endpoint URL derived from the API URL, or
    /// <c>null</c> when FlareSolverr is not configured. FlareSolverr
    /// exposes <c>/health</c> at the server root (not under /v1), so the
    /// /v1 suffix is stripped.
    /// </summary>
    internal static string? HealthUrl
    {
        get
        {
            var api = ApiUrl;
            if (api is null)
            {
                return null;
            }

            // Both http://host:8191/v1 and http://host:8191 roots must
            // resolve to http://host:8191/health.
            var root = ApiRootRegex().Replace(api, string.Empty);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            return root.TrimEnd('/') + "/health";
        }
    }

#if NET7_0_OR_GREATER
    [GeneratedRegex("/v1$", RegexOptions.IgnoreCase)]
    private static partial Regex ApiRootRegex();
#else
    private static readonly Regex ApiRootRegexInstance = new("/v1$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex ApiRootRegex() => ApiRootRegexInstance;
#endif
}
