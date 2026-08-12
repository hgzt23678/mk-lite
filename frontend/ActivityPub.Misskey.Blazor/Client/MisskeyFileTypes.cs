namespace ActivityPub.Misskey.Blazor.Client;

/// <summary>
/// The browser-safe file media types from Misskey v12. SVG is intentionally absent because
/// serving it inline would cross the content-security boundary.
/// </summary>
public static class MisskeyFileTypes
{
    public static IReadOnlySet<string> BrowserSafe { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/gif", "image/jpeg", "image/webp", "image/apng", "image/bmp", "image/tiff", "image/x-icon",
        "audio/opus", "video/ogg", "audio/ogg", "application/ogg",
        "video/quicktime", "video/mp4", "audio/mp4", "video/x-m4v", "audio/x-m4a", "video/3gpp", "video/3gpp2",
        "video/mpeg", "audio/mpeg", "video/webm", "audio/webm", "audio/aac", "audio/x-flac", "audio/vnd.wave"
    };

    public static bool IsBrowserSafe(string? mediaType) =>
        !string.IsNullOrWhiteSpace(mediaType) && BrowserSafe.Contains(mediaType.Trim());
}
