using ActivityPub.Domain;

namespace ActivityPub.Application;

public sealed record AudienceAddress(string Iri, AudienceField Field);

public sealed record VerifiedInboundActivity(
    string ActivityIri,
    string ActorIri,
    string ActivityType,
    string? ObjectIri,
    string? ObjectOwnerIri,
    string Origin,
    IReadOnlyList<AudienceAddress> Audience,
    string? RequiredLocalActorIri,
    byte[] RawBody,
    string PayloadHash,
    SignatureProfile SignatureProfile,
    string KeyIri,
    DateTimeOffset SignatureCreatedAt,
    string ReplayFingerprint,
    string? NonceHash,
    DateTimeOffset ReceivedAt);

public enum InboxAcceptanceStatus
{
    Accepted = 0,
    Duplicate = 1,
    ConflictQuarantined = 2,
    NoLocalRecipient = 3,
    RejectedByPolicy = 4
}

public sealed record InboxAcceptance(InboxAcceptanceStatus Status, Guid? InboxItemId, string? Reason);

public sealed record OutboundRecipient(string ActorIri, string InboxIri, string? SharedInboxIri);

public sealed record OutboundCommit(
    ActivityRecord Activity,
    FederatedObject? FederatedObject,
    ObjectRevision? ObjectRevision,
    FollowRelation? FollowRelation,
    IReadOnlyList<MediaAttachment>? MediaAttachments,
    ClientIdempotencyRecord? ClientIdempotency,
    IReadOnlyList<ActivityRecipient> Recipients,
    IReadOnlyList<Delivery> Deliveries,
    CollectionMembership? CollectionMembership = null,
    LikeRelation? LikeRelation = null,
    AnnounceRelation? AnnounceRelation = null,
    ActorMove? ActorMove = null,
    IReadOnlyList<DeliveryTarget>? DeliveryTargets = null,
    LikeRelation? ReplacedLikeRelation = null,
    EmojiReactionRelation? EmojiReactionRelation = null,
    UserBlock? UserBlock = null,
    QuestionPoll? QuestionPoll = null,
    IReadOnlyList<PollOption>? PollOptions = null,
    PollVote? PollVote = null);

public sealed record EndpointRediscoveryPlan(
    IReadOnlyList<DeliveryTarget> ReplacementTargets,
    IReadOnlyList<Delivery> AdditionalDeliveries,
    IReadOnlyList<DeliveryTarget> AdditionalTargets,
    IReadOnlyList<DeliveryEndpointChange> Changes);

public sealed record OutboundCommitResult(bool WasExisting, ClientOutboxResult? ExistingResult);

public sealed record RemoteActorEndpoint(
    string ActorIri,
    string InboxIri,
    string? SharedInboxIri);

public sealed record RemoteActorSnapshot(
    string ActorIri,
    string Type,
    string? PreferredUsername,
    string RawJson,
    string InboxIri,
    string? SharedInboxIri,
    string? ETag,
    DateTimeOffset? LastModified,
    DateTimeOffset FetchedAt);

public sealed record ClientOutboxResult(
    string ActivityIri,
    string? ObjectIri,
    byte[] ResponseBody);

public sealed record DeadLetterSummary(
    Guid Id,
    string SourceType,
    Guid SourceId,
    string ReasonCode,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReplayedAt,
    string? ReplayedBy);

public sealed record ReportSummary(
    Guid Id,
    string? Iri,
    string ReporterIri,
    string TargetIri,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy);

public sealed record OperationalControlState(
    bool OutboundDeliveryPaused,
    string? Reason,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt);

public sealed record FederationQueueStats(
    long ProcessedDeliveriesRecently,
    long Waiting,
    long Active,
    long Delayed,
    long Stalled,
    long DeadLettered,
    long Cancelled,
    long ProcessedInboxItemsRecently,
    long InboxWaiting,
    long InboxActive,
    long InboxDelayed,
    long InboxStalled,
    long InboxDeadLettered,
    DateTimeOffset? OldestQueuedAt,
    DateTimeOffset? NextAvailableAt,
    bool RedisWakeupEnabled,
    IReadOnlyList<FederationQueueDomainCount> DelayedByDomain,
    IReadOnlyList<FederationQueueDomainCount> InboxDelayedByDomain);

public sealed record FederationQueueDomainCount(string Domain, long Count);

public sealed record FederationQueueJobSummary(
    Guid Id,
    Guid ActivityId,
    string EndpointIri,
    string RemoteDomain,
    WorkItemState State,
    DateTimeOffset AvailableAt,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    int AttemptCount,
    int? LastStatusCode,
    string? LastErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record FederationInboxJobSummary(
    Guid Id,
    string ActivityIri,
    string ActorIri,
    string ActivityType,
    WorkItemState State,
    DateTimeOffset AvailableAt,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    int AttemptCount,
    string? LastErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record MediaUploadCommand(
    string OwnerActorIri,
    string OriginalFileName,
    string? DeclaredMediaType,
    Visibility Visibility,
    Stream Content);

public sealed record MediaUploadResult(
    Guid Id,
    string MediaType,
    long Length,
    int? Width,
    int? Height,
    long? DurationMilliseconds);

public sealed record MediaDownload(
    Stream Content,
    string MediaType,
    long Length,
    string FileName,
    bool IsPublic,
    string? EntityTag = null,
    DateTimeOffset? LastModified = null);

public sealed record MediaGarbageCandidate(
    Guid Id,
    string StorageKey,
    string? ThumbnailStorageKey);

public sealed record RemoteMediaSource(
    Guid ObjectId,
    string OwnerActorIri,
    string SourceIri,
    string SourceToken,
    Visibility Visibility);

public sealed record RemoteActorMediaSource(
    Guid RemoteActorId,
    string ActorIri,
    RemoteActorMediaKind Kind,
    string SourceIri,
    string SourceToken);

public enum RemoteActorMediaCacheClaimState
{
    Fresh,
    Acquired,
    Busy,
    Failed
}

public sealed record RemoteActorMediaCacheClaim(
    Guid EntryId,
    RemoteActorMediaCacheClaimState State,
    RemoteActorMediaSource Source,
    string? LeaseOwner,
    Guid? MediaId,
    string? RemoteETag,
    DateTimeOffset? RemoteLastModified,
    RemoteMediaCacheFailureKind? FailureKind,
    DateTimeOffset? RetryAfter);

public enum RemoteMediaOpenStatus
{
    Success,
    NotFound,
    Unavailable
}

public sealed record RemoteMediaOpenResult(
    RemoteMediaOpenStatus Status,
    MediaDownload? Download,
    DateTimeOffset? RetryAfter = null)
{
    public static RemoteMediaOpenResult Success(MediaDownload download) =>
        new(RemoteMediaOpenStatus.Success, download);

    public static RemoteMediaOpenResult NotFound() =>
        new(RemoteMediaOpenStatus.NotFound, null);

    public static RemoteMediaOpenResult Unavailable(DateTimeOffset? retryAfter = null) =>
        new(RemoteMediaOpenStatus.Unavailable, null, retryAfter);
}

public sealed record RawJsonPurgeResult(int Activities, int Objects, int ObjectRevisions);

public sealed record LegalHoldSummary(
    Guid Id,
    RawJsonResourceKind ResourceKind,
    Guid ResourceId,
    string Reason,
    string PlacedBy,
    DateTimeOffset PlacedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ReleasedAt,
    string? ReleasedBy);

public sealed record MalwareScanResult(bool IsClean, string? ThreatName);

public sealed record InboxSideEffects(
    ActivityRecord Activity,
    FederatedObject? FederatedObject,
    ObjectRevision? ObjectRevision,
    FollowRelation? FollowRelation,
    Report? Report,
    ActorPolicy? ActorPolicy,
    DeadLetter? DeadLetter,
    OutboundCommit? OutboundResponse,
    IReadOnlyList<ActivityRecipient>? Recipients = null,
    CollectionMembership? CollectionMembership = null,
    LikeRelation? LikeRelation = null,
    AnnounceRelation? AnnounceRelation = null,
    ActorMove? ActorMove = null,
    LikeRelation? ReplacedLikeRelation = null,
    EmojiReactionRelation? EmojiReactionRelation = null,
    UserBlock? UserBlock = null);

public sealed record ActorDocument(
    Guid Id,
    string Iri,
    string Username,
    ActorKind Kind,
    string DisplayName,
    string SummaryHtml,
    bool ManuallyApprovesFollowers,
    bool Discoverable,
    bool Indexable,
    string PublicKeyIri,
    string PublicKeyPem,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ActorPublicKeyDocument> RetiredPublicKeys);

public sealed record ActorPublicKeyDocument(string KeyIri, string PublicKeyPem, DateTimeOffset ExpiresAt);

public sealed record StoredDocument(
    string Iri,
    string MediaType,
    byte[] Body,
    string ETag,
    DateTimeOffset LastModified,
    Visibility Visibility,
    string OwnerIri);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, DateTimeOffset LastModified);

public sealed record CollectionEntry(string Iri, byte[] Json, DateTimeOffset PublishedAt);

public sealed record NodeInfoCounts(
    long LocalUsers,
    long LocalPosts,
    long RemoteDomains,
    long LocalMediaBytes,
    long RemoteMediaBytes);

public sealed record PageRequest(string? Cursor, int Limit)
{
    public int ValidatedLimit => Limit is >= 1 and <= 80
        ? Limit
        : throw new ArgumentOutOfRangeException(nameof(Limit), "Collection page size must be between 1 and 80.");
}

public sealed record RemotePublicKey(
    string KeyIri,
    string OwnerIri,
    string PublicKeyPem,
    string Algorithm,
    DateTimeOffset ExpiresAt);

public sealed record RemoteKeyCacheEntry(
    string KeyIri,
    string OwnerIri,
    string PublicKeyPem,
    string Algorithm,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RefreshBlockedUntil);

public enum DeliveryFailureClass
{
    Success = 0,
    Retryable = 1,
    AuthenticationRecheck = 2,
    EndpointGone = 3,
    Permanent = 4
}

public sealed record DeliveryDisposition(
    DeliveryFailureClass Classification,
    DateTimeOffset? RetryAt,
    string Code,
    string Message);

public sealed record DeliveryTransportResult(
    int? StatusCode,
    TimeSpan Duration,
    DateTimeOffset? RetryAfter,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record KeyMaterial(
    string KeyIri,
    string OwnerIri,
    string PublicKeyPem,
    string PrivateKeyHandle,
    string Algorithm);

public sealed record DomainLeaseToken(
    string Domain,
    int Slot,
    string Owner,
    Guid DeliveryId,
    DateTimeOffset ExpiresAt);

public sealed record ExternalKeyProvision(string Handle, string PublicKeyPem);

public sealed record LocalActorAdministrationResult(
    Guid ActorId,
    string ActorIri,
    Guid KeyId,
    string KeyIri);

public sealed record SchemaCompatibilityResult(
    string MinimumApplicationVersion,
    string MaximumApplicationVersion,
    bool IsCompatible);

public sealed record ServiceReleaseVersion(string Value);

public enum SpamDisposition
{
    Allow = 0,
    Quarantine = 1
}

public sealed record SpamAssessment(SpamDisposition Disposition, string Reason, int Score);
