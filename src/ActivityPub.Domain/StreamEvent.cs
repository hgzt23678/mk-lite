namespace ActivityPub.Domain;

public enum StreamEventKind
{
    PostCreated,
    PostUpdated,
    PostDeleted,
    ReactionChanged,
    PollVoted,
    RelationshipChanged,
    NotificationCreated
}

public sealed class StreamEvent : Entity
{
    private StreamEvent()
    {
    }

    private StreamEvent(
        Guid id,
        string deduplicationKey,
        StreamEventKind kind,
        Guid? resourceId,
        string? resourceIri,
        string actorIri,
        Visibility visibility,
        bool isLocal,
        DateTimeOffset occurredAt,
        string? reaction,
        bool reactionRemoved,
        int? pollChoiceIndex = null)
        : base(id)
    {
        DeduplicationKey = DomainText.Required(deduplicationKey, nameof(deduplicationKey), 2_048);
        if (resourceId == Guid.Empty)
        {
            throw new DomainException("A stream event resource identifier cannot be empty.");
        }

        ResourceId = resourceId;
        ResourceIri = resourceIri is null ? null : DomainText.RequiredIri(resourceIri, nameof(resourceIri));
        ActorIri = DomainText.RequiredIri(actorIri, nameof(actorIri));
        Kind = kind;
        Visibility = visibility;
        IsLocal = isLocal;
        OccurredAt = occurredAt;
        Reaction = DomainText.Optional(reaction, nameof(reaction), 512);
        ReactionRemoved = reactionRemoved;
        if (pollChoiceIndex is < 0 or > 99)
        {
            throw new DomainException("A stream poll choice is outside the supported range.");
        }

        PollChoiceIndex = pollChoiceIndex;
    }

    public long Cursor { get; private set; }
    public string DeduplicationKey { get; private set; } = string.Empty;
    public StreamEventKind Kind { get; private set; }
    public Guid? ResourceId { get; private set; }
    public string? ResourceIri { get; private set; }
    public string ActorIri { get; private set; } = string.Empty;
    public Visibility Visibility { get; private set; }
    public bool IsLocal { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Reaction { get; private set; }
    public bool? ReactionRemoved { get; private set; }
    public int? PollChoiceIndex { get; private set; }
    public string? RecipientActorIri { get; private set; }

    public static StreamEvent? FromObjectMutation(
        ActivityRecord activity,
        FederatedObject? resource,
        bool isLocal)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (resource is null || activity.Type is not ("Create" or "Update" or "Delete"))
        {
            return null;
        }

        StreamEventKind kind = activity.Type switch
        {
            "Create" => StreamEventKind.PostCreated,
            "Update" => StreamEventKind.PostUpdated,
            "Delete" => StreamEventKind.PostDeleted,
            _ => throw new DomainException("Unsupported object mutation stream event.")
        };
        return new(
            Guid.NewGuid(),
            "activity:" + activity.Iri + ":post",
            kind,
            resource.Id,
            resource.Iri,
            activity.ActorIri,
            activity.Visibility,
            isLocal,
            activity.OccurredAt,
            null,
            false);
    }

    public static StreamEvent FromReactionMutation(
        ActivityRecord activity,
        FederatedObject resource,
        string reaction,
        bool removed,
        bool isLocal)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(resource);
        return new(
            Guid.NewGuid(),
            "activity:" + activity.Iri + ":reaction",
            StreamEventKind.ReactionChanged,
            resource.Id,
            resource.Iri,
            activity.ActorIri,
            resource.Visibility,
            isLocal,
            activity.OccurredAt,
            reaction,
                removed);
    }

    public static StreamEvent FromNotification(UserNotification notification, Visibility visibility, bool isLocal)
    {
        ArgumentNullException.ThrowIfNull(notification);
        StreamEvent item = new(
            Guid.NewGuid(),
            "notification:" + notification.Id.ToString("N"),
            StreamEventKind.NotificationCreated,
            notification.Id,
            notification.ObjectIri,
            notification.SourceActorIri,
            visibility,
            isLocal,
            notification.CreatedAt,
            notification.Reaction,
            false);
        item.RecipientActorIri = notification.RecipientActorIri;
        return item;
    }

    public static StreamEvent FromRelationshipMutation(
        ActivityRecord activity,
        FollowRelation relationship,
        Guid targetActorId,
        string recipientActorIri,
        bool isLocal)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(relationship);
        if (targetActorId == Guid.Empty)
        {
            throw new DomainException("A relationship stream target identifier cannot be empty.");
        }

        string recipient = CanonicalIri.RequireAbsoluteHttp(recipientActorIri, nameof(recipientActorIri));
        string targetIri;
        string role;
        if (string.Equals(recipient, relationship.FollowerIri, StringComparison.Ordinal))
        {
            targetIri = relationship.FollowedIri;
            role = "follower";
        }
        else if (string.Equals(recipient, relationship.FollowedIri, StringComparison.Ordinal))
        {
            targetIri = relationship.FollowerIri;
            role = "followed";
        }
        else
        {
            throw new DomainException("A relationship stream recipient must participate in the Follow relation.");
        }

        StreamEvent item = new(
            Guid.NewGuid(),
            $"activity:{activity.Id:N}:relationship:{role}:{targetActorId:N}",
            StreamEventKind.RelationshipChanged,
            targetActorId,
            targetIri,
            activity.ActorIri,
            Visibility.MentionedOnly,
            isLocal,
            activity.OccurredAt,
            null,
            false);
        item.RecipientActorIri = recipient;
        return item;
    }

    public static StreamEvent FromPollVote(
        ActivityRecord activity,
        FederatedObject question,
        int choiceIndex,
        bool isLocal)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(question);
        if (question.Type != "Question")
        {
            throw new DomainException("A poll vote stream event requires a Question object.");
        }

        return new(
            Guid.NewGuid(),
            "activity:" + activity.Iri + ":poll-vote",
            StreamEventKind.PollVoted,
            question.Id,
            question.Iri,
            activity.ActorIri,
            question.Visibility,
            isLocal,
            activity.OccurredAt,
            null,
            false,
            choiceIndex);
    }
}
