namespace ActivityPub.Domain;

public sealed class LegalHold : Entity
{
    private LegalHold()
    {
    }

    private LegalHold(Guid id, RawJsonResourceKind resourceKind, Guid resourceId, string reason, string placedBy, DateTimeOffset now, DateTimeOffset? expiresAt)
        : base(id)
    {
        if (resourceId == Guid.Empty)
        {
            throw new DomainException("Legal hold resource id cannot be empty.");
        }

        ResourceKind = resourceKind;
        ResourceId = resourceId;
        Reason = DomainText.Required(reason, nameof(reason), 2_000);
        PlacedBy = DomainText.Required(placedBy, nameof(placedBy), 256);
        PlacedAt = now;
        ExpiresAt = expiresAt;
    }

    public RawJsonResourceKind ResourceKind { get; private set; }
    public Guid ResourceId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string PlacedBy { get; private set; } = string.Empty;
    public DateTimeOffset PlacedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public string? ReleasedBy { get; private set; }

    public static LegalHold Place(RawJsonResourceKind resourceKind, Guid resourceId, string reason, string placedBy, DateTimeOffset now, DateTimeOffset? expiresAt)
    {
        if (expiresAt is not null && expiresAt <= now)
        {
            throw new DomainException("Legal hold expiration must be in the future.");
        }

        return new(Guid.NewGuid(), resourceKind, resourceId, reason, placedBy, now, expiresAt);
    }

    public void Release(string operatorId, DateTimeOffset now)
    {
        if (ReleasedAt is not null)
        {
            throw new DomainException("Legal hold was already released.");
        }

        ReleasedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        ReleasedAt = now;
    }
}
