namespace ActivityPub.Domain;

public enum ActorKind
{
    Person = 0,
    Service = 1,
    Application = 2,
    Group = 3,
    Organization = 4
}

public enum ActorKeyState
{
    Pending = 0,
    Active = 1,
    Retired = 2,
    Revoked = 3
}

public enum ActivityDirection
{
    Inbound = 0,
    Outbound = 1,
    Local = 2
}

public enum Visibility
{
    Public = 0,
    Unlisted = 1,
    FollowersOnly = 2,
    MentionedOnly = 3
}

public enum AudienceField
{
    To = 0,
    Cc = 1,
    Bto = 2,
    Bcc = 3,
    Audience = 4
}

public enum FollowState
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Cancelled = 3
}

public enum FederatedRelationState
{
    Active = 0,
    Reversed = 1
}

public enum WorkItemState
{
    Pending = 0,
    Leased = 1,
    Succeeded = 2,
    DeadLettered = 3,
    Cancelled = 4
}

public enum DeliveryAttemptOutcome
{
    Succeeded = 0,
    RetryScheduled = 1,
    TerminalFailure = 2,
    Cancelled = 3
}

public enum SignatureProfile
{
    LegacyCavage = 0,
    Rfc9421 = 1,
    LocalClient = 2
}

public enum EndpointKind
{
    Inbox = 0,
    SharedInbox = 1,
    Outbox = 2,
    Followers = 3,
    Following = 4,
    Featured = 5
}

public enum FederationPolicyKind
{
    Allow = 0,
    Limit = 1,
    Reject = 2,
    Silence = 3,
    RejectMedia = 4,
    PauseOutbound = 5
}

public enum ModerationActionKind
{
    BlockActor = 0,
    MuteActor = 1,
    LimitDomain = 2,
    RejectDomain = 3,
    RejectMedia = 4,
    PauseOutbound = 5,
    QuarantineActivity = 6,
    AllowDomain = 7,
    SilenceDomain = 8
}

public enum MediaState
{
    PendingScan = 0,
    Available = 1,
    Quarantined = 2,
    Rejected = 3,
    Deleted = 4
}

public enum RawJsonResourceKind
{
    Activity = 0,
    FederatedObject = 1
}
