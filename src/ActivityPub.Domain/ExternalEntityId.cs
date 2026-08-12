namespace ActivityPub.Domain;

public enum ApiDialect
{
    Mastodon,
    Misskey
}

public enum ExternalEntityType
{
    Actor,
    Post,
    Activity,
    Media,
    Notification,
    Poll,
    List,
    Conversation,
    FollowRequest,
    ScheduledPost,
    Filter,
    Announcement,
    Report,
    Application,
    AccessToken,
    Channel,
    Clip,
    Page,
    GalleryPost,
    DriveFolder,
    Antenna,
    Webhook,
    FederationInstance,
    FollowRelation
}

public sealed class ExternalEntityId : Entity
{
    private ExternalEntityId()
    {
    }

    private ExternalEntityId(
        Guid id,
        ApiDialect dialect,
        ExternalEntityType entityType,
        Guid internalId,
        string externalId,
        long sortOrdinal,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (internalId == Guid.Empty)
        {
            throw new DomainException("The internal entity identifier cannot be empty.");
        }

        Dialect = dialect;
        EntityType = entityType;
        InternalId = internalId;
        ExternalId = DomainText.Required(externalId, nameof(externalId), 128);
        SortOrdinal = sortOrdinal > 0
            ? sortOrdinal
            : throw new DomainException("The external identifier sort ordinal must be positive.");
        CreatedAt = createdAt;
    }

    public ApiDialect Dialect { get; private set; }
    public ExternalEntityType EntityType { get; private set; }
    public Guid InternalId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public long SortOrdinal { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }

    public static ExternalEntityId Create(
        ApiDialect dialect,
        ExternalEntityType entityType,
        Guid internalId,
        string externalId,
        long sortOrdinal,
        DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), dialect, entityType, internalId, externalId, sortOrdinal, createdAt);

    public void Retire(DateTimeOffset retiredAt)
    {
        if (RetiredAt is not null)
        {
            return;
        }

        if (retiredAt < CreatedAt)
        {
            throw new DomainException("An external identifier cannot be retired before it was created.");
        }

        RetiredAt = retiredAt;
    }
}
