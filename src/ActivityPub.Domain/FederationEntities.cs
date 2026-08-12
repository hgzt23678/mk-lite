namespace ActivityPub.Domain;

public sealed class FederatedObject : Entity
{
    private FederatedObject()
    {
    }

    private FederatedObject(
        Guid id,
        string iri,
        string ownerIri,
        string type,
        Visibility visibility,
        string rawJson,
        string payloadHash,
        DateTimeOffset publishedAt,
        DateTimeOffset now,
        string? auditRawJson)
        : base(id)
    {
        Iri = CanonicalIri.RequireAbsoluteHttp(iri, nameof(iri));
        OwnerIri = CanonicalIri.RequireAbsoluteHttp(ownerIri, nameof(ownerIri));
        Type = DomainText.Required(type, nameof(type), 256);
        Visibility = visibility;
        RawJson = DomainText.Required(rawJson, nameof(rawJson), 2_000_000);
        AuditRawJson = DomainText.Optional(auditRawJson, nameof(auditRawJson), 2_000_000) ?? RawJson;
        PayloadHash = DomainText.Required(payloadHash, nameof(payloadHash), 128);
        PublishedAt = publishedAt;
        UpdatedAt = now;
    }

    public string Iri { get; private set; } = string.Empty;
    public string OwnerIri { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public Visibility Visibility { get; private set; }
    public string RawJson { get; private set; } = string.Empty;
    public string? AuditRawJson { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? RawJsonPurgedAt { get; private set; }
    public long Version { get; private set; }

    public static FederatedObject Create(
        string iri,
        string ownerIri,
        string type,
        Visibility visibility,
        string rawJson,
        string payloadHash,
        DateTimeOffset publishedAt,
        DateTimeOffset now,
        string? auditRawJson = null) =>
        new(Guid.NewGuid(), iri, ownerIri, type, visibility, rawJson, payloadHash, publishedAt, now, auditRawJson);

    public ObjectRevision Replace(
        string actorIri,
        string type,
        Visibility visibility,
        string rawJson,
        string payloadHash,
        DateTimeOffset now,
        string? auditRawJson = null)
    {
        EnsureOwner(actorIri);
        var revision = ObjectRevision.Capture(this, now);
        Type = DomainText.Required(type, nameof(type), 256);
        Visibility = visibility;
        RawJson = DomainText.Required(rawJson, nameof(rawJson), 2_000_000);
        AuditRawJson = DomainText.Optional(auditRawJson, nameof(auditRawJson), 2_000_000) ?? RawJson;
        PayloadHash = DomainText.Required(payloadHash, nameof(payloadHash), 128);
        UpdatedAt = now;
        IsDeleted = false;
        DeletedAt = null;
        Version++;
        return revision;
    }

    public ObjectRevision Delete(string actorIri, string tombstoneJson, string payloadHash, DateTimeOffset now)
    {
        EnsureOwner(actorIri);
        var revision = ObjectRevision.Capture(this, now);
        Type = "Tombstone";
        RawJson = DomainText.Required(tombstoneJson, nameof(tombstoneJson), 2_000_000);
        AuditRawJson = RawJson;
        PayloadHash = DomainText.Required(payloadHash, nameof(payloadHash), 128);
        IsDeleted = true;
        DeletedAt = now;
        UpdatedAt = now;
        Version++;
        return revision;
    }

    private void EnsureOwner(string actorIri)
    {
        if (!string.Equals(OwnerIri, CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri)), StringComparison.Ordinal))
        {
            throw new DomainException("An actor cannot mutate an object owned by another actor.");
        }
    }
}

public sealed class ObjectRevision : Entity
{
    private ObjectRevision()
    {
    }

    private ObjectRevision(Guid id, FederatedObject source, DateTimeOffset capturedAt)
        : base(id)
    {
        ObjectId = source.Id;
        Version = source.Version;
        Type = source.Type;
        Visibility = source.Visibility;
        RawJson = source.RawJson;
        AuditRawJson = source.AuditRawJson;
        PayloadHash = source.PayloadHash;
        CapturedAt = capturedAt;
    }

    public Guid ObjectId { get; private set; }
    public long Version { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public Visibility Visibility { get; private set; }
    public string RawJson { get; private set; } = string.Empty;
    public string? AuditRawJson { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset? RawJsonPurgedAt { get; private set; }

    public static ObjectRevision Capture(FederatedObject source, DateTimeOffset capturedAt) =>
        new(Guid.NewGuid(), source, capturedAt);
}

public sealed class ActivityRecord : Entity
{
    private ActivityRecord()
    {
    }

    private ActivityRecord(
        Guid id,
        string iri,
        string actorIri,
        string type,
        string? objectIri,
        ActivityDirection direction,
        Visibility visibility,
        string rawJson,
        string payloadHash,
        bool isTransient,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        string? auditRawJson)
        : base(id)
    {
        Iri = CanonicalIri.RequireAbsoluteHttp(iri, nameof(iri));
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        Type = DomainText.Required(type, nameof(type), 256);
        ObjectIri = objectIri is null ? null : CanonicalIri.RequireAbsoluteHttp(objectIri, nameof(objectIri));
        Direction = direction;
        Visibility = visibility;
        RawJson = DomainText.Required(rawJson, nameof(rawJson), 2_000_000);
        AuditRawJson = DomainText.Optional(auditRawJson, nameof(auditRawJson), 2_000_000) ?? RawJson;
        PayloadHash = DomainText.Required(payloadHash, nameof(payloadHash), 128);
        IsTransient = isTransient;
        OccurredAt = occurredAt;
        ReceivedAt = receivedAt;
    }

    public string Iri { get; private set; } = string.Empty;
    public string ActorIri { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string? ObjectIri { get; private set; }
    public ActivityDirection Direction { get; private set; }
    public Visibility Visibility { get; private set; }
    public string RawJson { get; private set; } = string.Empty;
    public string? AuditRawJson { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public bool IsTransient { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? RawJsonPurgedAt { get; private set; }

    public static ActivityRecord Create(
        string iri,
        string actorIri,
        string type,
        string? objectIri,
        ActivityDirection direction,
        Visibility visibility,
        string rawJson,
        string payloadHash,
        bool isTransient,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        string? auditRawJson = null) =>
        new(Guid.NewGuid(), iri, actorIri, type, objectIri, direction, visibility, rawJson, payloadHash, isTransient, occurredAt, receivedAt, auditRawJson);
}

public sealed class ActivityRecipient : Entity
{
    private ActivityRecipient()
    {
    }

    private ActivityRecipient(Guid id, Guid activityId, string recipientIri, AudienceField field)
        : base(id)
    {
        ActivityId = activityId;
        RecipientIri = CanonicalIri.RequireAbsoluteHttp(recipientIri, nameof(recipientIri));
        Field = field;
    }

    public Guid ActivityId { get; private set; }
    public string RecipientIri { get; private set; } = string.Empty;
    public AudienceField Field { get; private set; }

    public static ActivityRecipient Create(Guid activityId, string recipientIri, AudienceField field) =>
        new(Guid.NewGuid(), activityId, recipientIri, field);
}

public sealed class InboxItemRecipient : Entity
{
    private InboxItemRecipient()
    {
    }

    private InboxItemRecipient(Guid id, Guid inboxItemId, string actorIri)
        : base(id)
    {
        InboxItemId = inboxItemId;
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
    }

    public Guid InboxItemId { get; private set; }
    public string ActorIri { get; private set; } = string.Empty;

    public static InboxItemRecipient Create(Guid inboxItemId, string actorIri) =>
        new(Guid.NewGuid(), inboxItemId, actorIri);
}

public sealed class FollowRelation : Entity
{
    private FollowRelation()
    {
    }

    private FollowRelation(Guid id, string followerIri, string followedIri, string followActivityIri, DateTimeOffset now)
        : base(id)
    {
        FollowerIri = CanonicalIri.RequireAbsoluteHttp(followerIri, nameof(followerIri));
        FollowedIri = CanonicalIri.RequireAbsoluteHttp(followedIri, nameof(followedIri));
        FollowActivityIri = CanonicalIri.RequireAbsoluteHttp(followActivityIri, nameof(followActivityIri));
        State = FollowState.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string FollowerIri { get; private set; } = string.Empty;
    public string FollowedIri { get; private set; } = string.Empty;
    public string FollowActivityIri { get; private set; } = string.Empty;
    public string? DecisionActivityIri { get; private set; }
    public FollowState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static FollowRelation Request(string followerIri, string followedIri, string followActivityIri, DateTimeOffset now)
    {
        if (string.Equals(followerIri, followedIri, StringComparison.Ordinal))
        {
            throw new DomainException("An actor cannot follow itself.");
        }

        return new FollowRelation(Guid.NewGuid(), followerIri, followedIri, followActivityIri, now);
    }

    public void RequestAgain(string followerIri, string followActivityIri, DateTimeOffset now)
    {
        string follower = CanonicalIri.RequireAbsoluteHttp(followerIri, nameof(followerIri));
        if (!string.Equals(follower, FollowerIri, StringComparison.Ordinal))
        {
            throw new DomainException("Only the follower can renew a Follow request.");
        }

        FollowActivityIri = CanonicalIri.RequireAbsoluteHttp(followActivityIri, nameof(followActivityIri));
        DecisionActivityIri = null;
        State = FollowState.Pending;
        UpdatedAt = now;
    }

    public void Accept(string decisionActorIri, string activityIri, DateTimeOffset now)
    {
        EnsureDecisionActor(decisionActorIri);
        State = FollowState.Accepted;
        DecisionActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        UpdatedAt = now;
    }

    public void Reject(string decisionActorIri, string activityIri, DateTimeOffset now)
    {
        EnsureDecisionActor(decisionActorIri);
        State = FollowState.Rejected;
        DecisionActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        UpdatedAt = now;
    }

    public void Undo(string actorIri, DateTimeOffset now)
    {
        string actor = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        if (!string.Equals(actor, FollowerIri, StringComparison.Ordinal))
        {
            throw new DomainException("Only the follower can undo a Follow activity.");
        }

        State = FollowState.Cancelled;
        UpdatedAt = now;
    }

    public void CancelBecauseBlocked(string actorIri, DateTimeOffset now)
    {
        string actor = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        if (!string.Equals(actor, FollowerIri, StringComparison.Ordinal) &&
            !string.Equals(actor, FollowedIri, StringComparison.Ordinal))
        {
            throw new DomainException("Only an actor in the Follow relation can cancel it by blocking.");
        }

        State = FollowState.Cancelled;
        UpdatedAt = now;
    }

    private void EnsureDecisionActor(string actorIri)
    {
        if (!string.Equals(FollowedIri, CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri)), StringComparison.Ordinal))
        {
            throw new DomainException("Only the followed actor can accept or reject a follow request.");
        }
    }
}
