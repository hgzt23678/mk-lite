namespace ActivityPub.Media;

public static class MediaTypeSniffer
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly byte[] WebmSignature = [0x1a, 0x45, 0xdf, 0xa3];

    public static string Detect(ReadOnlySpan<byte> value)
    {
        if (value.Length >= 3 && value[0] == 0xff && value[1] == 0xd8 && value[2] == 0xff)
        {
            return "image/jpeg";
        }

        if (value.StartsWith(PngSignature))
        {
            return "image/png";
        }

        if (value.StartsWith("GIF87a"u8) || value.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (value.Length >= 12 && value[..4].SequenceEqual("RIFF"u8) && value[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (value.Length >= 12 && value[4..8].SequenceEqual("ftyp"u8))
        {
            return "video/mp4";
        }

        if (value.StartsWith(WebmSignature))
        {
            return "video/webm";
        }

        if (value.StartsWith("OggS"u8))
        {
            return "audio/ogg";
        }

        if (value.StartsWith("ID3"u8) || value.Length >= 2 && value[0] == 0xff && (value[1] & 0xe0) == 0xe0)
        {
            return "audio/mpeg";
        }

        throw new InvalidDataException("The uploaded file type is not allowed or could not be identified.");
    }

    public static string Extension(string mediaType) => mediaType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "video/mp4" => ".mp4",
        "video/webm" => ".webm",
        "audio/mpeg" => ".mp3",
        "audio/ogg" => ".ogg",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType))
    };
}
