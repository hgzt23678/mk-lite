using System.Text.RegularExpressions;

namespace ActivityPub.Domain;

public sealed partial class LocalActor : Entity
{
    private LocalActor()
    {
    }

    private LocalActor(Guid id, string iri, string username, ActorKind kind, DateTimeOffset now)
        : base(id)
    {
        Iri = CanonicalIri.RequireAbsoluteHttp(iri, nameof(iri));
        Username = NormalizeUsername(username);
        NormalizedUsername = Username.ToUpperInvariant();
        Kind = kind;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Iri { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public string NormalizedUsername { get; private set; } = string.Empty;
    public ActorKind Kind { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string SummaryHtml { get; private set; } = string.Empty;
    public bool ManuallyApprovesFollowers { get; private set; }
    public bool Discoverable { get; private set; }
    public bool Indexable { get; private set; }
    public bool IsSuspended { get; private set; }
    public Guid? ActiveKeyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static LocalActor Create(string iri, string username, ActorKind kind, DateTimeOffset now) =>
        new(Guid.NewGuid(), iri, username, kind, now);

    public void UpdateProfile(
        string displayName,
        string summaryHtml,
        bool manuallyApprovesFollowers,
        bool discoverable,
        bool indexable,
        DateTimeOffset now)
    {
        DisplayName = DomainText.Optional(displayName, nameof(displayName), 200) ?? string.Empty;
        SummaryHtml = DomainText.Optional(summaryHtml, nameof(summaryHtml), 20_000) ?? string.Empty;
        ManuallyApprovesFollowers = manuallyApprovesFollowers;
        Discoverable = discoverable;
        Indexable = indexable;
        Touch(now);
    }

    public void SetActiveKey(Guid keyId, DateTimeOffset now)
    {
        if (keyId == Guid.Empty)
        {
            throw new DomainException("Active key identifier cannot be empty.");
        }

        ActiveKeyId = keyId;
        Touch(now);
    }

    public void Suspend(DateTimeOffset now)
    {
        IsSuspended = true;
        Touch(now);
    }

    public void Restore(DateTimeOffset now)
    {
        IsSuspended = false;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static string NormalizeUsername(string username)
    {
        string normalized = DomainText.Required(username, nameof(username), 64).ToLowerInvariant();
        if (!UsernamePattern().IsMatch(normalized))
        {
            throw new DomainException("Username must contain only lowercase ASCII letters, digits, underscores, periods, or hyphens.");
        }

        return normalized;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}

public sealed class RemoteActor : Entity
{
    private RemoteActor()
    {
    }

    private RemoteActor(Guid id, string iri, string type, string? preferredUsername, string rawJson, DateTimeOffset now)
        : base(id)
    {
        Iri = CanonicalIri.RequireAbsoluteHttp(iri, nameof(iri));
        Origin = new Uri(Iri).GetLeftPart(UriPartial.Authority);
        Type = DomainText.Required(type, nameof(type), 128);
        PreferredUsername = DomainText.Optional(preferredUsername, nameof(preferredUsername), 256);
        RawJson = DomainText.Required(rawJson, nameof(rawJson), 2_000_000);
        FetchedAt = now;
        UpdatedAt = now;
    }

    public string Iri { get; private set; } = string.Empty;
    public string Origin { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string? PreferredUsername { get; private set; }
    public string RawJson { get; private set; } = string.Empty;
    public string? ETag { get; private set; }
    public DateTimeOffset? LastModified { get; private set; }
    public DateTimeOffset FetchedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? GoneAt { get; private set; }

    public static RemoteActor Create(string iri, string type, string? preferredUsername, string rawJson, DateTimeOffset now) =>
        new(Guid.NewGuid(), iri, type, preferredUsername, rawJson, now);

    public void Refresh(string type, string? preferredUsername, string rawJson, string? etag, DateTimeOffset? lastModified, DateTimeOffset now)
    {
        Type = DomainText.Required(type, nameof(type), 128);
        PreferredUsername = DomainText.Optional(preferredUsername, nameof(preferredUsername), 256);
        RawJson = DomainText.Required(rawJson, nameof(rawJson), 2_000_000);
        ETag = DomainText.Optional(etag, nameof(etag), 512);
        LastModified = lastModified;
        FetchedAt = now;
        UpdatedAt = now;
        GoneAt = null;
    }

    public void MarkGone(DateTimeOffset now)
    {
        GoneAt = now;
        UpdatedAt = now;
    }
}

public sealed class ActorKey : Entity
{
    private ActorKey()
    {
    }

    private ActorKey(
        Guid id,
        string keyIri,
        string ownerIri,
        string publicKeyPem,
        string algorithm,
        bool isLocal,
        string? privateKeyHandle,
        DateTimeOffset now)
        : base(id)
    {
        KeyIri = CanonicalIri.RequireAbsoluteHttp(keyIri, nameof(keyIri));
        OwnerIri = CanonicalIri.RequireAbsoluteHttp(ownerIri, nameof(ownerIri));
        PublicKeyPem = DomainText.Required(publicKeyPem, nameof(publicKeyPem), 16_384);
        Algorithm = DomainText.Required(algorithm, nameof(algorithm), 128);
        IsLocal = isLocal;
        PrivateKeyHandle = DomainText.Optional(privateKeyHandle, nameof(privateKeyHandle), 2_048);
        State = ActorKeyState.Pending;
        CreatedAt = now;
    }

    public string KeyIri { get; private set; } = string.Empty;
    public string OwnerIri { get; private set; } = string.Empty;
    public string PublicKeyPem { get; private set; } = string.Empty;
    public string Algorithm { get; private set; } = string.Empty;
    public bool IsLocal { get; private set; }
    public string? PrivateKeyHandle { get; private set; }
    public ActorKeyState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public static ActorKey CreateLocal(
        string keyIri,
        string ownerIri,
        string publicKeyPem,
        string privateKeyHandle,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), keyIri, ownerIri, publicKeyPem, "rsa-v1_5-sha256", true, privateKeyHandle, now);

    public static ActorKey CreateRemote(
        string keyIri,
        string ownerIri,
        string publicKeyPem,
        string algorithm,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), keyIri, ownerIri, publicKeyPem, algorithm, false, null, now);

    public void Activate(DateTimeOffset now)
    {
        if (State is ActorKeyState.Revoked)
        {
            throw new DomainException("A revoked key cannot be activated.");
        }

        State = ActorKeyState.Active;
        ActivatedAt = now;
        RetiredAt = null;
    }

    public void Retire(DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (expiresAt <= now)
        {
            throw new DomainException("Retired key overlap must expire in the future.");
        }

        State = ActorKeyState.Retired;
        RetiredAt = now;
        ExpiresAt = expiresAt;
    }

    public void Revoke(DateTimeOffset now)
    {
        State = ActorKeyState.Revoked;
        RevokedAt = now;
        ExpiresAt = now;
    }
}
