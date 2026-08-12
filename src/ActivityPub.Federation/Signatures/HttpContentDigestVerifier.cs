using System.Security.Cryptography;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ActivityPub.Federation.Signatures;

public static class HttpContentDigestVerifier
{
    public static void VerifyRequired(HttpRequest request, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool contentDigestPresent = request.Headers.TryGetValue("Content-Digest", out StringValues contentDigest);
        bool digestPresent = request.Headers.TryGetValue("Digest", out StringValues digest);
        if (!contentDigestPresent && !digestPresent)
        {
            throw new HttpSignatureException("POST requests require Digest or Content-Digest.");
        }

        if (contentDigestPresent)
        {
            VerifyRfc9530(SingleHeader(contentDigest, "Content-Digest"), body);
        }

        if (digestPresent)
        {
            VerifyLegacy(SingleHeader(digest, "Digest"), body);
        }
    }

    public static string CreateLegacy(ReadOnlySpan<byte> body) => "SHA-256=" + PayloadDigest.Sha256Base64(body);

    public static string CreateRfc9530(ReadOnlySpan<byte> body) => "sha-256=:" + PayloadDigest.Sha256Base64(body) + ":";

    private static void VerifyRfc9530(string header, ReadOnlySpan<byte> body)
    {
        string? encoded = FindAlgorithmValue(header, "sha-256", structuredBinary: true);
        if (encoded is null)
        {
            throw new HttpSignatureException("Content-Digest does not contain sha-256.");
        }

        VerifyBytes(encoded, body, "Content-Digest");
    }

    private static void VerifyLegacy(string header, ReadOnlySpan<byte> body)
    {
        string? encoded = FindAlgorithmValue(header, "sha-256", structuredBinary: false);
        if (encoded is null)
        {
            throw new HttpSignatureException("Digest does not contain SHA-256.");
        }

        VerifyBytes(encoded, body, "Digest");
    }

    private static string? FindAlgorithmValue(string header, string algorithm, bool structuredBinary)
    {
        foreach (string field in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = field.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || !string.Equals(field[..equals].Trim(), algorithm, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = field[(equals + 1)..].Trim();
            int parameter = value.IndexOf(';', StringComparison.Ordinal);
            if (parameter >= 0)
            {
                value = value[..parameter].Trim();
            }

            if (structuredBinary)
            {
                if (value.Length < 2 || value[0] != ':' || value[^1] != ':')
                {
                    throw new HttpSignatureException("Content-Digest sha-256 value is not an RFC 9530 byte sequence.");
                }

                value = value[1..^1];
            }

            return value;
        }

        return null;
    }

    private static void VerifyBytes(string encoded, ReadOnlySpan<byte> body, string headerName)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new HttpSignatureException($"{headerName} contains invalid Base64.", exception);
        }

        byte[] computed = SHA256.HashData(body);
        if (supplied.Length != computed.Length || !CryptographicOperations.FixedTimeEquals(supplied, computed))
        {
            throw new HttpSignatureException($"{headerName} does not match the received bytes.");
        }
    }

    private static string SingleHeader(StringValues values, string name)
    {
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 8_192)
        {
            throw new HttpSignatureException($"{name} must occur exactly once and have a bounded value.");
        }

        return values[0]!;
    }
}
