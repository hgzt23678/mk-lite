using System.Text;

namespace ActivityPub.Misskey.Blazor.Client;

public static class MisskeyTwoFactorUtilities
{
    public static byte[] Byteify(string value, string encoding)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoding);
        return encoding.ToLowerInvariant() switch
        {
            "ascii" => Encoding.ASCII.GetBytes(value),
            "base64" => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + Padding(value)),
            "hex" => ParseHex(value),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown byte encoding."),
        };
    }

    public static string Hexify(ReadOnlySpan<byte> value) => Convert.ToHexString(value).ToLowerInvariant();

    public static string Stringify(ReadOnlySpan<byte> value) => Encoding.Latin1.GetString(value);

    private static byte[] ParseHex(string value)
    {
        if (value.Length % 2 != 0 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new FormatException("Hex input must contain an even number of hexadecimal characters.");
        }

        return Convert.FromHexString(value);
    }

    private static string Padding(string value) => (value.Length % 4) switch
    {
        0 => string.Empty,
        2 => "==",
        3 => "=",
        _ => throw new FormatException("Invalid base64url length."),
    };
}
