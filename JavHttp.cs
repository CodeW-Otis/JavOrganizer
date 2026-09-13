using System.Net;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Shared HTTP plumbing for the plugin's outbound requests.
/// </summary>
/// <remarks>
/// <see cref="HttpClient"/> instances are deliberately long-lived and reused.
/// Creating one per request (the previous behaviour of the image and metadata
/// providers) leaves sockets in <c>TIME_WAIT</c> and can exhaust the ephemeral
/// port range on a server that fetches covers for a large library.
/// </remarks>
internal static class JavHttp
{
    /// <summary>
    /// Browser-like user agent; the sites reject default client agents.
    /// </summary>
    internal const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    /// <summary>
    /// Gets the shared client for page and cover-image requests.
    /// </summary>
    /// <remarks>
    /// Accept-encoding gzip/br plus automatic decompression: the sites serve
    /// compressed HTML and images, which cuts bandwidth roughly fourfold.
    /// No Referer is pinned on the client itself — image CDNs validate the
    /// Referer against the image's own host, so it is supplied per request
    /// by <see cref="GetImageResponse"/>.
    /// </remarks>
    internal static HttpClient JavLibrary { get; } = CreateImageClient();

    /// <summary>
    /// Gets the shared client for FlareSolverr, which needs a longer timeout
    /// than page fetches because it drives a real browser session.
    /// </summary>
    internal static HttpClient FlareSolverr { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(90),
    };

    /// <summary>
    /// Downloads an image with the headers its CDN expects: a browser user
    /// agent and a Referer matching the image's own host (javbus.com covers
    /// return 403 when the Referer points at a different site).
    /// </summary>
    /// <param name="url">Absolute image URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response containing the image bytes.</returns>
    internal static Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Referer", RefererFor(url));
        return JavLibrary.SendAsync(request, ct);
    }

    /// <summary>
    /// Derives a Referer the image's own CDN accepts: the site root of the
    /// image host (javbus.com images want a javbus.com Referer, DMM images
    /// a dmm.co.jp one, and so on).
    /// </summary>
    /// <param name="url">Absolute image URL.</param>
    /// <returns>The Referer to send, or the JavLibrary root for unknown hosts.</returns>
    private static string RefererFor(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Host}/";
            }
        }
        catch
        {
            // Fall through to the default.
        }

        return "https://www.javlibrary.com/";
    }

    private static HttpClient CreateImageClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}
