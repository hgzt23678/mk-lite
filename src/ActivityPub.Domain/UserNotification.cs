namespace ActivityPub.Domain;

public enum UserNotificationKind
{
    Mention,
    Follow,
    Favourite,
    Reaction,
    Reblog,
    Poll,
    Update,
    Application
}

public sealed class UserNotification : Entity
{
    private UserNotification()
    {
    }

    private UserNotification(
        Guid id,
        string recipientActorIri,
        string sourceActorIri,
        UserNotificationKind kind,
        string? activityIri,
        string? objectIri,
        string? reaction,
        DateTimeOffset createdAt)
        : base(id)
    {
        RecipientActorIri = DomainText.RequiredIri(recipientActorIri, nameof(recipientActorIri));
        SourceActorIri = DomainText.RequiredIri(sourceActorIri, nameof(sourceActorIri));
        Kind = kind;
        ActivityIri = activityIri is null ? null : DomainText.RequiredIri(activityIri, nameof(activityIri));
        ObjectIri = objectIri is null ? null : DomainText.RequiredIri(objectIri, nameof(objectIri));
        Reaction = DomainText.Optional(reaction, nameof(reaction), 512);
        CreatedAt = createdAt;
    }

    public string RecipientActorIri { get; private set; } = string.Empty;
    public string SourceActorIri { get; private set; } = string.Empty;
    public UserNotificationKind Kind { get; private set; }
    public string? ActivityIri { get; private set; }
    public string? ObjectIri { get; private set; }
    public string? Reaction { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? DismissedAt { get; private set; }

    public static UserNotification Create(
        string recipientActorIri,
        string sourceActorIri,
        UserNotificationKind kind,
        string? activityIri,
        string? objectIri,
        string? reaction,
        DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), recipientActorIri, sourceActorIri, kind, activityIri, objectIri, reaction, createdAt);

    public void MarkRead(string recipientActorIri, DateTimeOffset now)
    {
        EnsureRecipient(recipientActorIri);
        ReadAt ??= now;
    }

    public void Dismiss(string recipientActorIri, DateTimeOffset now)
    {
        EnsureRecipient(recipientActorIri);
        ReadAt ??= now;
        DismissedAt ??= now;
    }

    private void EnsureRecipient(string actorIri)
    {
        if (!string.Equals(RecipientActorIri, DomainText.RequiredIri(actorIri, nameof(actorIri)), StringComparison.Ordinal))
        {
            throw new DomainException("A notification can only be changed by its recipient.");
        }
    }
}
