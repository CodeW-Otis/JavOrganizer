using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Serves JavLibrary cover art as remote images for items whose metadata
/// this plugin provided.
/// </summary>
public sealed class JavImageProvider : IRemoteImageProvider
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavImageProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger scoped to this provider.</param>
    public JavImageProvider(ILogger<JavImageProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Gets the provider name shown in the image-provider list.
    /// </summary>
    public string Name => "JavOrganizer";

    /// <summary>
    /// Specifies whether the provider can supply images for the item.
    /// </summary>
    /// <param name="item">The item being scanned.</param>
    /// <returns><c>true</c> for any <see cref="Video"/> item.</returns>
    public bool Supports(BaseItem item)
    {
        return item is Video;
    }

    /// <summary>
    /// Enumerates the image types this provider can deliver.
    /// </summary>
    /// <param name="item">The item being scanned.</param>
    /// <returns>Primary and backdrop image types.</returns>
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
        yield return ImageType.Backdrop;
    }

    /// <summary>
    /// Looks up the cached record for the item and returns its cover as a
    /// primary remote image. The record is found by provider id first, and
    /// by the item's own product code as fallback — so items whose scrape
    /// came entirely from secondary sites (no JavLibrary id) still get
    /// their cover.
    /// </summary>
    /// <param name="item">The item being scanned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Remote image candidates for the item.</returns>
    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken ct)
    {
        JavVideo? video = null;
        if (item.ProviderIds.TryGetValue(JavMetadataProvider.ProviderIdKey, out var videoId)
            && !string.IsNullOrWhiteSpace(videoId))
        {
            video = JavCache.FindByVideoId(videoId, _logger);
        }

        if (video is null)
        {
            // Fallback: locate the record by the item's own product code —
            // works even when the merged scrape carried no video id.
            var code = JavCodeParser.ExtractCode(item.Path) ?? JavCodeParser.ExtractCode(item.Name);
            if (code is not null)
            {
                var normalized = JavCodeParser.Normalize(code);
                if (normalized.Length > 0)
                {
                    video = JavCache.TryRead(normalized, _logger);
                }
            }
        }

        if (video?.CoverUrl is null)
        {
            _logger.LogDebug("No cached cover for '{Name}'", item.Name);
            return Task.FromResult<IEnumerable<RemoteImageInfo>>([]);
        }

        // The JavLibrary cover is the primary poster; scene screenshots from
        // the same page are offered as backdrop images.
        var images = new List<RemoteImageInfo>
        {
            new()
            {
                ProviderName = Name,
                Url = video.CoverUrl,
                ThumbnailUrl = video.CoverUrl,
                Type = ImageType.Primary
            }
        };

        foreach (var preview in video.PreviewUrls)
        {
            images.Add(new RemoteImageInfo
            {
                ProviderName = Name,
                Url = preview,
                ThumbnailUrl = preview,
                Type = ImageType.Backdrop
            });
        }

        return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
    }

    /// <summary>
    /// Downloads an image with the headers its CDN expects: a browser user
    /// agent and a Referer matching the image's own host (javbus.com covers
    /// reject requests Referered elsewhere).
    /// </summary>
    /// <param name="url">Absolute image URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response containing the image bytes.</returns>
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken ct)
    {
        return JavHttp.GetImageResponse(url, ct);
    }
}
