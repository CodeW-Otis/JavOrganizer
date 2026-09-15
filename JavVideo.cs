using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Plain data record for one scraped video, merged from all configured
/// sites. Serialized to JSON for the plugin's on-disk metadata cache.
/// </summary>
public sealed class JavVideo
{
    /// <summary>
    /// Gets or sets the primary site's internal video id (JavLibrary
    /// <c>?v=</c> value or JavDB <c>/v/…</c> code).
    /// </summary>
    [JsonPropertyName("videoId")]
    public string? VideoId { get; set; }

    /// <summary>
    /// Gets or sets the product code ("ABC-123").
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full page title, including the code prefix.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release date, when a page exposes one.
    /// </summary>
    [JsonPropertyName("releaseDate")]
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the runtime in minutes, when a page exposes one.
    /// </summary>
    [JsonPropertyName("runtimeMinutes")]
    public int? RuntimeMinutes { get; set; }

    /// <summary>
    /// Gets or sets the maker (studio) name.
    /// </summary>
    [JsonPropertyName("maker")]
    public string Maker { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the director name.
    /// </summary>
    [JsonPropertyName("director")]
    public string Director { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the label name.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the genre tags of the video.
    /// </summary>
    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = [];

    /// <summary>
    /// Gets or sets the female actress names of the video.
    /// </summary>
    [JsonPropertyName("actresses")]
    public List<string> Actresses { get; set; } = [];

    /// <summary>
    /// Gets or sets the male actor names of the video, as credited by
    /// sites that separate actor genders (JavDB).
    /// </summary>
    [JsonPropertyName("maleActors")]
    public List<string> MaleActors { get; set; } = [];

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    /// <summary>
    /// Gets or sets the scene preview (screenshot) image URLs, offered as
    /// backdrop images.
    /// </summary>
    [JsonPropertyName("previewUrls")]
    public List<string> PreviewUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets the photo URL of each cast member, keyed by the
    /// member's exact scraped name (actresses and male actors together).
    /// Sites that embed per-performer photos (JavBus star portraits,
    /// FANZA actress images) populate this; the person image provider
    /// and the collections task use it to give every performer card a
    /// real photo.
    /// </summary>
    /// <remarks>
    /// Records written before this field existed deserialize to an empty
    /// map, which is why <see cref="PersonImageNameHints"/> also exists:
    /// the collections task can re-derive photos for those records from the
    /// image URLs the page embedded once it knows who was in the cast.
    /// </remarks>
    [JsonPropertyName("personImages")]
    public Dictionary<string, string> PersonImageUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets the name hints that map a cast member onto one of the
    /// page's embedded images, used as a fallback when
    /// <see cref="PersonImageUrls"/> is empty for a name.
    /// </summary>
    /// <remarks>
    /// Scrapers record an image-URL fragment and a performer-name fragment
    /// that were found on the same element — for example the star id in
    /// <c>star/qq9</c> next to the portrait <c>actress/qq9_a.jpg</c>, or a
    /// romanized performer name inside the portrait's file name. Matching
    /// those fragments at read time lets a record written before this
    /// feature existed still yield a performer photo, without re-scraping.
    /// </remarks>
    [JsonPropertyName("personImageHints")]
    public Dictionary<string, string> PersonImageNameHints { get; set; } = [];

    /// <summary>
    /// Gets or sets the site's own id for each cast member, keyed by the
    /// performer's name, as read from their credit link
    /// (<c>star/uly</c> → <c>uly</c>, keyed by the link text).
    /// </summary>
    /// <remarks>
    /// Cast portraits on these sites are named after that id rather than
    /// after the performer (".../actress/uly_a.jpg"), so this map is what
    /// joins a portrait to the person it belongs to when the page carries no
    /// usable <c>title</c> attribute.
    /// </remarks>
    [JsonPropertyName("castStarIds")]
    public Dictionary<string, string> CastStarIds { get; set; } = [];
}
