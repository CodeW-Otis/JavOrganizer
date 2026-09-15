using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Serves performer photos (actresses and male actors) as remote images
/// for person items, so every actor and actress card in the library — and
/// the per-person collections poster — carries a real photo.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin itself carries no gender on people and no photos for names
/// that only this plugin contributed. The photo URLs are collected during
/// scrapes (JavBus star portraits, catalog-site cast images) and cached
/// with every record, so this provider is a pure cache lookup — no network
/// scraping happens here.</para>
/// <para>
/// The provider is registered for <see cref="Person"/> items and answers
/// with the first cached photo for the person's name. The download itself
/// runs through the shared image pipeline with the CDN-expected Referer,
/// exactly like covers.</para>
/// </remarks>
public sealed class JavPersonImageProvider : IRemoteImageProvider
{
    /// <summary>
    /// How long the in-memory photo index is reused before it is rebuilt,
    /// so a library-wide image refresh does not read the whole cache once
    /// per person while still picking up photos from a scrape that ran in
    /// between.
    /// </summary>
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromMinutes(5);

    private readonly ILogger _logger;
    private readonly object _indexLock = new();
    private Dictionary<string, string>? _index;
    private DateTime _indexBuiltUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavPersonImageProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger scoped to this provider.</param>
    public JavPersonImageProvider(ILogger<JavPersonImageProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Gets the provider name shown in the image-provider list.
    /// </summary>
    public string Name => "JavOrganizer";

    /// <summary>
    /// Specifies whether the provider can supply images for the item:
    /// person items only.
    /// </summary>
    /// <param name="item">The item being scanned.</param>
    /// <returns><c>true</c> for any <see cref="Person"/> item.</returns>
    public bool Supports(BaseItem item) => item is Person;

    /// <summary>
    /// Enumerates the image types this provider can deliver: a person's
    /// primary photo.
    /// </summary>
    /// <param name="item">The item being scanned.</param>
    /// <returns>The primary image type.</returns>
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
    }

    /// <summary>
    /// Looks up the person's cached photo URL and returns it as a remote
    /// primary image. The photo comes from the scrape cache (collected
    /// from the sites' cast portraits); when no photo is cached for the
    /// name, no image is offered and other providers get their turn.
    /// </summary>
    /// <param name="item">The person item being scanned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Remote image candidates for the person.</returns>
    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken ct)
    {
        var name = item.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult<IEnumerable<RemoteImageInfo>>([]);
        }

        var url = FindPersonPhoto(name);
        if (url is null)
        {
            _logger.LogDebug("No cached photo for person '{Name}'", name);
            return Task.FromResult<IEnumerable<RemoteImageInfo>>([]);
        }

        var images = new List<RemoteImageInfo>
        {
            new()
            {
                ProviderName = Name,
                Url = url,
                ThumbnailUrl = url,
                Type = ImageType.Primary
            }
        };

        return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
    }

    /// <summary>
    /// Resolves a person's photo URL from the scrape cache. The full cache
    /// index is built once and reused for a short window: building it means
    /// reading every cached record, and a library-wide image refresh asks
    /// for hundreds of people back to back.
    /// </summary>
    /// <param name="personName">The person to find a photo for.</param>
    /// <returns>The photo URL, or <c>null</c> when no cached record has one.</returns>
    private string? FindPersonPhoto(string personName)
    {
        try
        {
            var index = PersonPhotoIndex();
            if (index.TryGetValue(personName, out var url) && !string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            // Spelling drift between sites ("Yuzuru Yuki" / "Yuzuru Yuuki")
            // is common; fall back to a normalized-name comparison before
            // deciding this person has no photo.
            var wanted = SiteScraper.LabelFragment(personName);
            if (wanted.Length == 0)
            {
                return null;
            }

            foreach (var (name, photo) in index)
            {
                if (string.Equals(SiteScraper.LabelFragment(name), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return photo;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Person photo lookup failed for '{Name}'", personName);
        }

        return null;
    }

    /// <summary>
    /// Gets the cached person-name → photo map, rebuilding it when the
    /// cached copy has gone stale.
    /// </summary>
    /// <returns>The index.</returns>
    private Dictionary<string, string> PersonPhotoIndex()
    {
        var cached = _index;
        if (cached is not null && DateTime.UtcNow - _indexBuiltUtc < IndexLifetime)
        {
            return cached;
        }

        lock (_indexLock)
        {
            cached = _index;
            if (cached is not null && DateTime.UtcNow - _indexBuiltUtc < IndexLifetime)
            {
                return cached;
            }

            var built = JavCache.LoadPersonImageIndex(_logger);
            _index = built;
            _indexBuiltUtc = DateTime.UtcNow;
            _logger.LogDebug("Person photo index rebuilt: {Count} performers have a cached photo", built.Count);
            return built;
        }
    }

    /// <summary>
    /// Downloads a person photo with the headers its CDN expects — the
    /// same pipeline as covers (browser user agent, Referer matching the
    /// image's own host).
    /// </summary>
    /// <param name="url">Absolute image URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response containing the image bytes.</returns>
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken ct)
    {
        return JavHttp.GetImageResponse(url, ct);
    }
}
