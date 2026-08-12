using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using ActivityPub.Federation.Protocol;

namespace ActivityPub.Federation.Signatures;

public sealed class RemoteKeyResolver(
    IRemoteKeyCacheStore cache,
    ISafeFederationHttpClient httpClient,
    IFederationInstrumentation instrumentation,
    FederationOptions options,
    IClock clock) : IRemoteKeyResolver
{
    private static readonly IReadOnlySet<string> AcceptedMediaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/activity+json",
        "application/ld+json",
        "application/json"
    };

    public async Task<RemotePublicKey> ResolveAsync(
        string keyIri,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        string canonicalKeyIri = CanonicalIri.RequireAbsoluteHttp(keyIri, nameof(keyIri));
        DateTimeOffset now = clock.UtcNow;
        RemoteKeyCacheEntry? cached = await cache.FindAsync(canonicalKeyIri, cancellationToken).ConfigureAwait(false);
        if (!forceRefresh && cached is not null && cached.ExpiresAt > now)
        {
            instrumentation.PublicKeyCache(hit: true);
            return ToPublicKey(cached);
        }

        instrumentation.PublicKeyCache(hit: false);

        if (forceRefresh && cached?.RefreshBlockedUntil > now)
        {
            throw new HttpSignatureException("Remote key refresh is inside its cooldown window.");
        }

        Uri fetchUri = WithoutFragment(new Uri(canonicalKeyIri));
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            fetchUri,
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AcceptedMediaTypes,
            options.MaximumRemoteDocumentBytes);
        SafeFederationResponse response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpSignatureException($"Remote key document returned HTTP {(int)response.StatusCode}.");
        }

        RemoteKeyCacheEntry fetched = ParseKeyDocument(canonicalKeyIri, response.Body, now.Add(options.RemoteKeyCacheDuration));
        await cache.SaveAsync(
            fetched,
            PayloadDigest.Sha256Hex(response.Body),
            now,
            options.RemoteKeyRefreshCooldown,
            cancellationToken).ConfigureAwait(false);
        return ToPublicKey(fetched);
    }

    private static RemoteKeyCacheEntry ParseKeyDocument(string keyIri, byte[] body, DateTimeOffset expiresAt)
    {
        JsonSafetyValidator.Validate(body);
        using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new HttpSignatureException("Remote key document must be a JSON object.");
        }

        foreach (JsonElement candidate in EnumerateKeyCandidates(root))
        {
            if (!TryGetString(candidate, "id", out string? id) || !string.Equals(id, keyIri, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryGetString(candidate, "owner", out string? owner) ||
                !TryGetString(candidate, "publicKeyPem", out string? publicKeyPem))
            {
                throw new HttpSignatureException("Remote key is missing owner or publicKeyPem.");
            }

            string canonicalOwner = CanonicalIri.RequireAbsoluteHttp(owner, "owner");
            EnsureSameOrigin(keyIri, canonicalOwner);
            if (publicKeyPem.Length > 16_384)
            {
                throw new HttpSignatureException("Remote public key is too large.");
            }

            return new(keyIri, canonicalOwner, publicKeyPem, "rsa-v1_5-sha256", expiresAt, null);
        }

        throw new HttpSignatureException("Requested keyId was not present in the remote document.");
    }

    private static IEnumerable<JsonElement> EnumerateKeyCandidates(JsonElement root)
    {
        if (root.TryGetProperty("publicKeyPem", out _))
        {
            yield return root;
        }

        if (!root.TryGetProperty("publicKey", out JsonElement publicKey))
        {
            yield break;
        }

        if (publicKey.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in publicKey.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }
        }
        else if (publicKey.ValueKind == JsonValueKind.Object)
        {
            yield return publicKey;
        }
    }

    private static bool TryGetString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
    {
        if (root.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }

    private static void EnsureSameOrigin(string keyIri, string ownerIri)
    {
        var key = new Uri(keyIri);
        var owner = new Uri(ownerIri);
        if (!string.Equals(key.Scheme, owner.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(key.IdnHost, owner.IdnHost, StringComparison.OrdinalIgnoreCase) || key.Port != owner.Port)
        {
            throw new HttpSignatureException("Remote keyId and owner have different origins.");
        }
    }

    private static Uri WithoutFragment(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri;
    }

    private static RemotePublicKey ToPublicKey(RemoteKeyCacheEntry entry) =>
        new(entry.KeyIri, entry.OwnerIri, entry.PublicKeyPem, entry.Algorithm, entry.ExpiresAt);
}
