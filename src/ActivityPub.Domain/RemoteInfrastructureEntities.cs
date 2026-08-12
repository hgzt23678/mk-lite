namespace ActivityPub.Domain;

public sealed class RemoteEndpoint : Entity
{
    private RemoteEndpoint()
    {
    }

    private RemoteEndpoint(Guid id, string actorIri, EndpointKind kind, string endpointIri, DateTimeOffset now)
        : base(id)
    {
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        Kind = kind;
        EndpointIri = CanonicalIri.RequireAbsoluteHttp(endpointIri, nameof(endpointIri));
        RemoteDomain = new Uri(EndpointIri).IdnHost.ToLowerInvariant();
        FetchedAt = now;
        UpdatedAt = now;
    }

    public string ActorIri { get; private set; } = string.Empty;
    public EndpointKind Kind { get; private set; }
    public string EndpointIri { get; private set; } = string.Empty;
    public string RemoteDomain { get; private set; } = string.Empty;
    public DateTimeOffset FetchedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? GoneAt { get; private set; }

    public static RemoteEndpoint Create(string actorIri, EndpointKind kind, string endpointIri, DateTimeOffset now) =>
        new(Guid.NewGuid(), actorIri, kind, endpointIri, now);

    public void Refresh(string endpointIri, DateTimeOffset now)
    {
        EndpointIri = CanonicalIri.RequireAbsoluteHttp(endpointIri, nameof(endpointIri));
        RemoteDomain = new Uri(EndpointIri).IdnHost.ToLowerInvariant();
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

public sealed class RemoteKeyCache : Entity
{
    private RemoteKeyCache()
    {
    }

    private RemoteKeyCache(
        Guid id,
        string keyIri,
        string ownerIri,
        string publicKeyPem,
        string algorithm,
        string sourceDocumentHash,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        KeyIri = CanonicalIri.RequireAbsoluteHttp(keyIri, nameof(keyIri));
        OwnerIri = CanonicalIri.RequireAbsoluteHttp(ownerIri, nameof(ownerIri));
        PublicKeyPem = DomainText.Required(publicKeyPem, nameof(publicKeyPem), 16_384);
        Algorithm = DomainText.Required(algorithm, nameof(algorithm), 128);
        SourceDocumentHash = DomainText.Required(sourceDocumentHash, nameof(sourceDocumentHash), 128);
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
    }

    public string KeyIri { get; private set; } = string.Empty;
    public string OwnerIri { get; private set; } = string.Empty;
    public string PublicKeyPem { get; private set; } = string.Empty;
    public string Algorithm { get; private set; } = string.Empty;
    public string SourceDocumentHash { get; private set; } = string.Empty;
    public DateTimeOffset FetchedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RefreshBlockedUntil { get; private set; }

    public static RemoteKeyCache Create(
        string keyIri,
        string ownerIri,
        string publicKeyPem,
        string algorithm,
        string sourceDocumentHash,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt) =>
        new(Guid.NewGuid(), keyIri, ownerIri, publicKeyPem, algorithm, sourceDocumentHash, fetchedAt, expiresAt);

    public void Refresh(
        string ownerIri,
        string publicKeyPem,
        string algorithm,
        string sourceDocumentHash,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt,
        TimeSpan refreshCooldown)
    {
        OwnerIri = CanonicalIri.RequireAbsoluteHttp(ownerIri, nameof(ownerIri));
        PublicKeyPem = DomainText.Required(publicKeyPem, nameof(publicKeyPem), 16_384);
        Algorithm = DomainText.Required(algorithm, nameof(algorithm), 128);
        SourceDocumentHash = DomainText.Required(sourceDocumentHash, nameof(sourceDocumentHash), 128);
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
        RefreshBlockedUntil = fetchedAt.Add(refreshCooldown);
    }
}
