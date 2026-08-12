using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ActivityPub.Domain;

public static class CanonicalIri
{
    public static string RequireAbsoluteHttp(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new DomainException($"{parameterName} must be an absolute HTTP(S) IRI without user information.");
        }

        return uri.AbsoluteUri;
    }

    public static string RequireHttpsOrigin(string value, string parameterName)
        => RequireWebOrigin(value, parameterName, requireHttps: true);

    public static string RequireWebOrigin(string value, string parameterName, bool requireHttps)
    {
        string normalized = RequireAbsoluteHttp(value, parameterName);
        var uri = new Uri(normalized);
        if ((requireHttps && uri.Scheme != Uri.UriSchemeHttps) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            string scheme = requireHttps ? "HTTPS" : "HTTP(S)";
            throw new DomainException($"{parameterName} must be an {scheme} origin with no path, query, or fragment.");
        }

        return normalized.TrimEnd('/');
    }

    public static string RequireSameOrigin(string value, string owner, string parameterName)
    {
        string normalized = RequireAbsoluteHttp(value, parameterName);
        var valueUri = new Uri(normalized);
        var ownerUri = new Uri(RequireAbsoluteHttp(owner, nameof(owner)));
        if (!Uri.Compare(valueUri, ownerUri, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase).Equals(0))
        {
            throw new DomainException($"{parameterName} must have the same origin as its owner.");
        }

        return normalized;
    }
}

public static class DomainText
{
    public static string RequiredIri(string value, string parameterName)
    {
        string normalized = Required(value, parameterName, 2_048);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? iri) ||
            iri.Scheme != Uri.UriSchemeHttp && iri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(iri.UserInfo))
        {
            throw new DomainException($"{parameterName} must be an absolute HTTP(S) IRI without userinfo.");
        }

        return iri.AbsoluteUri;
    }

    public static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Normalize(NormalizationForm.FormC);
        if (normalized.Length > maximumLength)
        {
            throw new DomainException($"{parameterName} exceeds {maximumLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        return normalized;
    }

    public static string? Optional(string? value, string parameterName, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = value.Normalize(NormalizationForm.FormC);
        if (normalized.Length > maximumLength)
        {
            throw new DomainException($"{parameterName} exceeds {maximumLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        return normalized;
    }
}

public static class PayloadDigest
{
    public static string Sha256Hex(ReadOnlySpan<byte> payload) =>
        Convert.ToHexStringLower(SHA256.HashData(payload));

    public static string Sha256Base64(ReadOnlySpan<byte> payload) =>
        Convert.ToBase64String(SHA256.HashData(payload));
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public abstract class Entity
{
    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Entity identifiers cannot be empty.");
        }

        Id = id;
    }

    public Guid Id { get; protected set; }
}
