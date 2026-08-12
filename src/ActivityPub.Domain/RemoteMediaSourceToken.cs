using System.Text;

namespace ActivityPub.Domain;

public static class RemoteMediaSourceToken
{
    public const int Length = 32;

    public static string Create(string sourceIri)
    {
        string canonical = CanonicalIri.RequireAbsoluteHttp(sourceIri, nameof(sourceIri));
        return PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(canonical))[..Length];
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        if (value is not null && value.Length == Length && value.All(char.IsAsciiHexDigit))
        {
            normalized = value.ToLowerInvariant();
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static string Require(string value) => TryNormalize(value, out string normalized)
        ? normalized
        : throw new DomainException("Remote media source token is invalid.");
}
