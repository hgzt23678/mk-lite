namespace ActivityPub.Domain;

public sealed class UserMute : Entity
{
    private UserMute()
    {
    }

    private UserMute(Guid id, string ownerActorIri, string targetActorIri, bool hideNotifications, DateTimeOffset now, DateTimeOffset? expiresAt)
        : base(id)
    {
        OwnerActorIri = CanonicalIri.RequireAbsoluteHttp(ownerActorIri, nameof(ownerActorIri));
        TargetActorIri = CanonicalIri.RequireAbsoluteHttp(targetActorIri, nameof(targetActorIri));
        if (string.Equals(OwnerActorIri, TargetActorIri, StringComparison.Ordinal))
        {
            throw new DomainException("An actor cannot mute itself.");
        }

        HideNotifications = hideNotifications;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public string OwnerActorIri { get; private set; } = string.Empty;
    public string TargetActorIri { get; private set; } = string.Empty;
    public bool HideNotifications { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static UserMute Create(string ownerActorIri, string targetActorIri, bool hideNotifications, DateTimeOffset now, DateTimeOffset? expiresAt)
    {
        if (expiresAt is not null && expiresAt <= now)
        {
            throw new DomainException("Mute expiration must be in the future.");
        }

        return new(Guid.NewGuid(), ownerActorIri, targetActorIri, hideNotifications, now, expiresAt);
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
