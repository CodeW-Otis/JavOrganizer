using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Extracts and normalizes JAV product codes (for example "ABP-123") from
/// arbitrary file names and free-text titles.
/// </summary>
public static partial class JavCodeParser
{
    /// <summary>
    /// Product-code pattern with two branches:
    /// <list type="bullet">
    /// <item>label (1–6 letters + up to 3 digits, for labels like "ABP",
    /// "T28", "H46", "M9") followed by a hyphen or space, then 2–5
    /// digits;</item>
    /// <item>label glued directly to the digits, for names like
    /// "abp982".</item>
    /// </list>
    /// Lookarounds replace <c>\b</c> because <c>\b</c> does not break on an
    /// underscore, which made codes in names such as
    /// "IPX-1234_uncensored" unmatchable. A separator is required before
    /// the number unless the letters run straight into it, so movie noise
    /// like "1080p" or "4K" never matches.
    /// </summary>
    private const string CodePattern = @"(?<![A-Za-z0-9])([A-Za-z]{1,6}\d{0,3})[- ](\d{2,5})(?![0-9])|(?<![A-Za-z0-9])([A-Za-z]{2,6})(\d{2,5})(?![0-9])";

#if NET7_0_OR_GREATER
    /// <summary>
    /// Matches a JAV product code, capturing the letters and digits separately.
    /// </summary>
    [GeneratedRegex(CodePattern, RegexOptions.IgnoreCase)]
    private static partial Regex CodeRegex();
#else
    // .NET 6 (Jellyfin 10.8) has no GeneratedRegex attribute in the BCL;
    // fall back to a cached compiled regex with identical semantics.
    private static readonly Regex CodeRegexInstance = new(CodePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex CodeRegex() => CodeRegexInstance;
#endif

    /// <summary>
    /// Letter tokens that look like product codes but are release noise —
    /// resolution, codec, container and disc markers common in media file
    /// names. Matching one of these means the "code" is a false positive and
    /// must not trigger a scrape.
    /// </summary>
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // Resolution and quality markers.
        "FHD", "UHD", "QHD", "HD", "SD", "HQ", "LQ",

        // Video codecs.
        "HEVC", "AVC", "H264", "H265", "X264", "X265", "MPEG", "DIVX", "XVID",

        // Containers.
        "MP4", "MKV", "AVI", "WMV", "MOV", "FLV", "M2TS", "VOB", "ISO",

        // Source and release markers.
        "WEB", "WEBRIP", "BLURAY", "BDRIP", "BRRIP", "HDRIP", "DVDRIP", "REMUX", "HDTV", "TS", "CAM",

        // Disc, volume and part markers.
        "CD", "DVD", "DISC", "PART", "VOL", "EP",
    };

    /// <summary>
    /// Extracts a normalized product code ("ABC-123" — uppercase letters,
    /// digits padded to three) from a file name or title.
    /// </summary>
    /// <param name="filename">Raw file name or display name; may contain an extension.</param>
    /// <returns>
    /// The normalized code, or <c>null</c> when nothing matches or every
    /// candidate is a known non-code token (FC2 titles, resolution or codec markers).
    /// </returns>
    public static string? ExtractCode(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(filename);

        // Walk every candidate so noise earlier in the name cannot hide a real
        // code later in it, for example "Movie.FHD1080.ABP-123.mp4".
        foreach (Match match in CodeRegex().Matches(stem))
        {
            // The pattern has two branches; the label/digits pair lives in
            // groups (1,2) or (3,4) depending on which branch matched.
            var rawLabel = match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : match.Groups[3].Value;
            var rawDigits = match.Groups[2].Value.Length > 0 ? match.Groups[2].Value : match.Groups[4].Value;

            var upper = rawLabel.ToUpperInvariant();
            var letters = new string(upper.Where(char.IsLetter).ToArray());
            var labelDigits = new string(upper.Where(char.IsDigit).ToArray());

            if (letters.Equals("FC2", StringComparison.OrdinalIgnoreCase) || NoiseTokens.Contains(letters))
            {
                continue;
            }

            // Digit-suffixed labels (T28, H46, CD2) keep their digits before
            // the number: "T28-597", not "T-28597".
            var digits = rawDigits.PadLeft(3, '0');
            return $"{letters}{labelDigits}-{digits}";
        }

        return null;
    }

    /// <summary>
    /// Normalizes any product-code spelling to a canonical lowercase,
    /// zero-free form ("abc-123") suitable for cache keys and comparisons.
    /// </summary>
    /// <param name="code">A raw code such as "ABP-123", "abp123" or "ABP-00123".</param>
    /// <returns>The normalized form, or an empty string when the input is not a code.</returns>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var match = CodeRegex().Match(code);
        if (!match.Success)
        {
            return string.Empty;
        }

        var rawLabel = match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : match.Groups[3].Value;
        var rawDigits = match.Groups[2].Value.Length > 0 ? match.Groups[2].Value : match.Groups[4].Value;

        var lower = rawLabel.ToLowerInvariant();
        var letters = new string(lower.Where(char.IsLetter).ToArray());
        var labelDigits = new string(lower.Where(char.IsDigit).ToArray());

        var digits = rawDigits.TrimStart('0');
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        return $"{letters}{labelDigits}-{digits}";
    }

    /// <summary>
    /// Builds the keyword to send to a site search from any code spelling.
    /// The zero-free canonical form is used because site indexes key on the
    /// canonical code, so searching a zero-padded variant ("ABP-00123") finds
    /// nothing where the canonical form ("ABP-123") succeeds.
    /// </summary>
    /// <param name="code">A raw code in any casing or padding.</param>
    /// <returns>The uppercase canonical keyword, or an empty string when the input is not a code.</returns>
    public static string ToSearchKeyword(string? code)
    {
        var normalized = Normalize(code);
        return normalized.Length == 0 ? string.Empty : normalized.ToUpperInvariant();
    }
}
