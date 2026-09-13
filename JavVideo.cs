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
}
