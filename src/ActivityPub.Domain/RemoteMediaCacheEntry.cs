namespace ActivityPub.Domain;

public sealed class RemoteMediaCacheEntry : Entity
{
    private RemoteMediaCacheEntry()
    {
    }

    private RemoteMediaCacheEntry(
        Guid id,
        Guid objectId,
        string sourceIri,
        string sourceToken,
        Guid mediaId,
        string? etag,
        DateTimeOffset? lastModified,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
        : base(id)
    {
        if (objectId == Guid.Empty || mediaId == Guid.Empty)
        {
            throw new DomainException("Remote media cache identifiers cannot be empty.");
        }

        ObjectId = objectId;
        SourceIri = CanonicalIri.RequireAbsoluteHttp(sourceIri, nameof(sourceIri));
        SourceToken = DomainText.Required(sourceToken, nameof(sourceToken), 128);
        MediaId = mediaId;
        ETag = DomainText.Optional(etag, nameof(etag), 512);
        LastModified = lastModified;
        CreatedAt = now;
        RefreshedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid ObjectId { get; private set; }
    public string SourceIri { get; private set; } = string.Empty;
    public string SourceToken { get; private set; } = string.Empty;
    public Guid MediaId { get; private set; }
    public string? ETag { get; private set; }
    public DateTimeOffset? LastModified { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset RefreshedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static RemoteMediaCacheEntry Create(
        Guid objectId,
        string sourceIri,
        string sourceToken,
        Guid mediaId,
        string? etag,
        DateTimeOffset? lastModified,
        DateTimeOffset now,
        DateTimeOffset expiresAt) =>
        new(Guid.NewGuid(), objectId, sourceIri, sourceToken, mediaId, etag, lastModified, now, expiresAt);

    public void Refresh(Guid mediaId, string? etag, DateTimeOffset? lastModified, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (mediaId == Guid.Empty)
        {
            throw new DomainException("Remote media identifier cannot be empty.");
        }

        MediaId = mediaId;
        ETag = DomainText.Optional(etag, nameof(etag), 512);
        LastModified = lastModified;
        RefreshedAt = now;
        ExpiresAt = expiresAt;
    }
}
