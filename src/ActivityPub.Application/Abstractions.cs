using ActivityPub.Domain;

namespace ActivityPub.Application;

public interface IInboxRepository
{
    Task<InboxAcceptance> AcceptAsync(VerifiedInboundActivity activity, CancellationToken cancellationToken);
    Task<IReadOnlyList<InboxItem>> ClaimAsync(string workerId, int count, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken);
    Task ExtendLeaseAsync(Guid itemId, string workerId, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken);
    Task<FederatedObject?> FindObjectAsync(string objectIri, CancellationToken cancellationToken);
    Task<FollowRelation?> FindFollowByActivityAsync(string followActivityIri, CancellationToken cancellationToken);
    Task<FollowRelation?> FindFollowByPairAsync(string followerIri, string followedIri, CancellationToken cancellationToken);
    Task<CollectionMembership?> FindCollectionMembershipByActivityAsync(string activityIri, CancellationToken cancellationToken);
    Task<CollectionMembership?> FindActiveCollectionMembershipAsync(string collectionIri, string objectIri, CancellationToken cancellationToken);
    Task<LikeRelation?> FindLikeByActivityAsync(string activityIri, CancellationToken cancellationToken);
    Task<LikeRelation?> FindActiveLikeAsync(string actorIri, string objectIri, CancellationToken cancellationToken);
    Task<EmojiReactionRelation?> FindEmojiReactionByActivityAsync(string activityIri, CancellationToken cancellationToken);
    Task<EmojiReactionRelation?> FindActiveEmojiReactionAsync(string actorIri, string objectIri, string reaction, CancellationToken cancellationToken);
    Task<AnnounceRelation?> FindAnnounceByActivityAsync(string activityIri, CancellationToken cancellationToken);
    Task<AnnounceRelation?> FindActiveAnnounceAsync(string actorIri, string objectIri, CancellationToken cancellationToken);
    Task<ActorMove?> FindMoveByActivityAsync(string activityIri, CancellationToken cancellationToken);
    Task<UserBlock?> FindBlockByActivityAsync(string activityIri, CancellationToken cancellationToken);
    Task<UserBlock?> FindActiveBlockAsync(string ownerActorIri, string targetActorIri, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> FindAcceptedRecipientsAsync(Guid inboxItemId, CancellationToken cancellationToken);
    Task SaveProcessedAsync(InboxItem item, InboxSideEffects effects, CancellationToken cancellationToken);
    Task SaveFailureAsync(InboxItem item, DeadLetter? deadLetter, CancellationToken cancellationToken);
}

public interface IRelayRepository
{
    Task<Relay?> FindByInboxAsync(string inbox, CancellationToken cancellationToken);
    Task<IReadOnlyList<Relay>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Relay>> ListAcceptedAsync(CancellationToken cancellationToken);
    Task AddAsync(Relay relay, CancellationToken cancellationToken);
    Task UpdateStatusAsync(Guid relayId, RelayStatus status, CancellationToken cancellationToken);
    Task DeleteAsync(Guid relayId, CancellationToken cancellationToken);
}

public interface IRelayCommandService
{
    Task<Relay> AddAsync(string inbox, CancellationToken cancellationToken);
    Task RemoveAsync(string inbox, CancellationToken cancellationToken);
    Task<IReadOnlyList<Relay>> ListAsync(CancellationToken cancellationToken);
    Task AcceptAsync(string followActivityIri, CancellationToken cancellationToken);
    Task RejectAsync(string followActivityIri, CancellationToken cancellationToken);
    Task DeliverToAcceptedRelaysAsync(
        Guid activityId,
        string activityIri,
        string actorIri,
        byte[] activityPayload,
        CancellationToken cancellationToken);
}

public sealed record HashtagTrend(string Tag, long UsersCount, IReadOnlyList<long> Chart);

public interface IHashtagRepository
{
    Task RecordUsageAsync(
        IReadOnlyList<string> names,
        string ownerIri,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SearchAsync(
        string query,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HashtagTrend>> TrendAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record UrlPreviewResult(
    string Title,
    string? Description,
    string? Thumbnail,
    string? Icon,
    string? SiteName,
    string? PlayerUrl,
    int? PlayerWidth,
    int? PlayerHeight);

public sealed record ClientDriveFileView(
    Guid Id,
    string Name,
    string MediaType,
    string Md5,
    long Size,
    string Url,
    bool IsSensitive,
    string? Blurhash,
    int? Width,
    int? Height,
    Guid? FolderId,
    string? Comment,
    DateTimeOffset CreatedAt);

public sealed record ClientDriveFolderView(
    Guid Id,
    string Name,
    Guid? ParentId,
    DateTimeOffset CreatedAt);

public interface IClientDriveService
{
    Task<IReadOnlyList<ClientDriveFileView>> ListFilesAsync(
        string ownerActorIri,
        Guid? folderId,
        Guid? sinceId,
        Guid? untilId,
        int limit,
        CancellationToken cancellationToken);

    Task<ClientDriveFileView> UploadFileAsync(
        string ownerActorIri,
        Guid? folderId,
        string? name,
        bool isSensitive,
        string? comment,
        string? declaredType,
        string fileName,
        Stream content,
        CancellationToken cancellationToken);

    Task<ClientDriveFileView?> ShowFileAsync(
        string ownerActorIri,
        Guid fileId,
        CancellationToken cancellationToken);

    Task DeleteFileAsync(
        string ownerActorIri,
        Guid fileId,
        CancellationToken cancellationToken);

    Task<ClientDriveFileView?> UpdateFileAsync(
        string ownerActorIri,
        Guid fileId,
        string? name,
        Guid? folderId,
        string? comment,
        bool? isSensitive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientDriveFolderView>> ListFoldersAsync(
        string ownerActorIri,
        Guid? parentId,
        int limit,
        CancellationToken cancellationToken);

    Task<ClientDriveFolderView> CreateFolderAsync(
        string ownerActorIri,
        string name,
        Guid? parentId,
        CancellationToken cancellationToken);

    Task DeleteFolderAsync(
        string ownerActorIri,
        Guid folderId,
        CancellationToken cancellationToken);

    Task<ClientDriveFolderView?> UpdateFolderAsync(
        string ownerActorIri,
        Guid folderId,
        string? name,
        Guid? parentId,
        CancellationToken cancellationToken);

    Task<(long Usage, long Capacity)> GetUsageAsync(
        string ownerActorIri,
        CancellationToken cancellationToken);
}

public sealed record ProfileUpdateCommand(
    string? Name,
    string? Description,
    bool? IsLocked,
    bool? Discoverable,
    bool? Indexable);

public interface IProfileUpdateService
{
    Task<bool> UpdateAsync(string username, ProfileUpdateCommand command, CancellationToken cancellationToken);
}

public interface IAnnounceChainGuard
{
    Task<bool> IsWithinChainLimitAsync(string objectIri, CancellationToken cancellationToken);
}

public interface IUrlPreviewRepository
{
    Task<UrlPreview?> FindByUrlAsync(string url, CancellationToken cancellationToken);

    Task SaveAsync(UrlPreview preview, CancellationToken cancellationToken);
}

public interface IUrlPreviewService
{
    Task<UrlPreviewResult?> GetAsync(string url, string? lang, CancellationToken cancellationToken);
}

public interface IDeliveryRepository
{
    Task<OutboundCommitResult> CommitOutboundAsync(OutboundCommit commit, CancellationToken cancellationToken);
    Task CommitRelayDeliveriesAsync(IReadOnlyList<Delivery> deliveries, CancellationToken cancellationToken);
    Task<ClientIdempotencyRecord?> FindClientIdempotencyAsync(string subject, string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<Delivery>> ClaimAsync(string workerId, int count, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken);
    Task ExtendLeaseAsync(Guid deliveryId, string workerId, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken);
    Task ReleaseWithoutAttemptAsync(Delivery delivery, CancellationToken cancellationToken);
    Task SaveAttemptAsync(
        Delivery delivery,
        DeliveryAttempt attempt,
        DeadLetter? deadLetter,
        CancellationToken cancellationToken,
        EndpointRediscoveryPlan? endpointRediscovery = null);
    Task<IReadOnlyList<string>> FindRecipientActorsAsync(Guid deliveryId, CancellationToken cancellationToken);
    Task<bool> RequeueDeadLetterAsync(Guid deadLetterId, string operatorId, DateTimeOffset now, CancellationToken cancellationToken);
    Task CancelPendingForDomainAsync(string domain, string reason, DateTimeOffset now, CancellationToken cancellationToken);
    Task<long> CountPendingAsync(CancellationToken cancellationToken);
    Task<TimeSpan?> GetOldestPendingAgeAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IFederationQueueSignal
{
    bool IsEnabled { get; }

    Task NotifyDeliveryAvailableAsync(CancellationToken cancellationToken);

    Task WaitForDeliveryAsync(TimeSpan timeout, CancellationToken cancellationToken);

    Task NotifyInboxAvailableAsync(CancellationToken cancellationToken);

    Task WaitForInboxAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IFederationQueueAdministration
{
    Task<FederationQueueStats> GetStatsAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<FederationQueueJobSummary>> ListAsync(
        WorkItemState? state,
        bool? delayed,
        string? remoteDomain,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FederationInboxJobSummary>> ListInboxAsync(
        WorkItemState? state,
        bool? delayed,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken);
}

public interface IRemoteDomainExecutionStore
{
    Task<DomainLeaseToken?> TryAcquireAsync(string domain, string owner, Guid deliveryId, int maximumSlots, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken);
    Task ExtendAsync(DomainLeaseToken token, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken);
    Task ReleaseAsync(DomainLeaseToken token, CancellationToken cancellationToken);
    Task<DateTimeOffset?> GetCircuitOpenUntilAsync(string domain, DateTimeOffset now, CancellationToken cancellationToken);
    Task RecordCircuitSuccessAsync(string domain, DateTimeOffset now, CancellationToken cancellationToken);
    Task RecordCircuitFailureAsync(string domain, DateTimeOffset now, int threshold, TimeSpan breakDuration, CancellationToken cancellationToken);
}

public interface IFederationQueryStore
{
    Task<ActorDocument?> FindLocalActorByUsernameAsync(string username, CancellationToken cancellationToken);
    Task<ActorDocument?> FindLocalActorByIriAsync(string actorIri, CancellationToken cancellationToken);
    Task<StoredDocument?> FindObjectAsync(string iri, CancellationToken cancellationToken);
    Task<StoredDocument?> FindActivityAsync(string iri, CancellationToken cancellationToken);
    Task<CursorPage<CollectionEntry>> ReadCollectionAsync(string actorIri, string collection, PageRequest request, CancellationToken cancellationToken);
    Task<bool> IsAuthorizedRecipientAsync(string resourceIri, string requesterActorIri, CancellationToken cancellationToken);
    Task<bool> ContainsLocalRecipientAsync(IEnumerable<string> recipientIris, CancellationToken cancellationToken);
    Task<NodeInfoCounts> GetNodeInfoCountsAsync(CancellationToken cancellationToken);
}

public interface IRegistrationAvailabilityService
{
    Task<bool> IsUsernameAvailableAsync(string username, CancellationToken cancellationToken);

    Task<RegistrationEmailAvailability> CheckEmailAvailabilityAsync(
        string email,
        CancellationToken cancellationToken);
}

public enum RegistrationEmailAvailabilityReason
{
    None = 0,
    InvalidFormat = 1
}

public sealed record RegistrationEmailAvailability(
    bool Available,
    RegistrationEmailAvailabilityReason Reason);

public interface IRemoteKeyResolver
{
    Task<RemotePublicKey> ResolveAsync(string keyIri, bool forceRefresh, CancellationToken cancellationToken);
}

public interface IRemoteKeyCacheStore
{
    Task<RemoteKeyCacheEntry?> FindAsync(string keyIri, CancellationToken cancellationToken);
    Task SaveAsync(RemoteKeyCacheEntry entry, string sourceDocumentHash, DateTimeOffset fetchedAt, TimeSpan refreshCooldown, CancellationToken cancellationToken);
}

public interface IPrivateKeyStore
{
    Task<KeyMaterial> GetSigningKeyAsync(string actorIri, CancellationToken cancellationToken);
}

public interface IKeySigner
{
    Task<byte[]> SignAsync(string privateKeyHandle, string algorithm, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}

public interface IOutboundTransport
{
    Task<DeliveryTransportResult> DeliverAsync(Delivery delivery, KeyMaterial key, CancellationToken cancellationToken);
}

public interface IRemoteActorDirectory
{
    Task<RemoteActorEndpoint?> FindEndpointAsync(string actorIri, CancellationToken cancellationToken);
    Task<IReadOnlyList<RemoteActorEndpoint>> FindAcceptedFollowerEndpointsAsync(string localActorIri, CancellationToken cancellationToken);
    Task SaveAsync(RemoteActorSnapshot actor, CancellationToken cancellationToken);
    Task MarkEndpointGoneAsync(string endpointIri, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IRemoteRecipientResolver
{
    Task<IReadOnlyList<RemoteActorEndpoint>> ResolveAsync(string localActorIri, IReadOnlyList<AudienceAddress> audience, CancellationToken cancellationToken);
    Task<IReadOnlyList<RemoteActorEndpoint>> ResolveIncludingBlockedAsync(
        string localActorIri,
        IReadOnlyList<AudienceAddress> audience,
        CancellationToken cancellationToken) =>
        ResolveAsync(localActorIri, audience, cancellationToken);
    Task<RemoteActorEndpoint> RediscoverAsync(string actorIri, CancellationToken cancellationToken);
}

public interface IActorMoveValidator
{
    Task ValidateAsync(string sourceActorIri, string targetActorIri, CancellationToken cancellationToken);
}

public interface IClientOutboxService
{
    Task<ClientOutboxResult> SubmitAsync(string username, string idempotencyKey, byte[] requestBody, CancellationToken cancellationToken);
}

public interface IDomainPolicyService
{
    Task<FederationPolicyKind> GetEffectivePolicyAsync(string domain, string? actorIri, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> FindRejectedActorsAsync(IReadOnlyCollection<string> actorIris, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> FindRejectedActorsForLocalAsync(
        string localActorIri,
        IReadOnlyCollection<string> actorIris,
        CancellationToken cancellationToken) =>
        FindRejectedActorsAsync(actorIris, cancellationToken);
}

public interface IWorkerHeartbeatStore
{
    Task RecordAsync(string workerId, string workerType, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> HasRecentHeartbeatAsync(string workerType, DateTimeOffset threshold, CancellationToken cancellationToken);
}

public interface IAuditLog
{
    Task AppendAsync(string category, string action, string actor, string target, string detailsJson, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IModerationAdministration
{
    Task<IReadOnlyList<DeadLetterSummary>> ListDeadLettersAsync(DateTimeOffset? before, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportSummary>> ListReportsAsync(DateTimeOffset? before, int limit, bool unresolvedOnly, CancellationToken cancellationToken);
    Task<Guid> CreateDomainPolicyAsync(string domain, FederationPolicyKind kind, string reason, string operatorId, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
    Task<bool> RevokeDomainPolicyAsync(Guid policyId, string operatorId, CancellationToken cancellationToken);
    Task<Guid> CreateActorPolicyAsync(string actorIri, ModerationActionKind kind, string reason, string operatorId, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
    Task<bool> RevokeActorPolicyAsync(Guid policyId, string operatorId, CancellationToken cancellationToken);
    Task<bool> ResolveReportAsync(Guid reportId, string operatorId, CancellationToken cancellationToken);
    Task<bool> RequeueDeadLetterAsync(Guid deadLetterId, string operatorId, CancellationToken cancellationToken);
    Task<OperationalControlState> GetOperationalControlAsync(CancellationToken cancellationToken);
    Task SetOutboundDeliveryPausedAsync(bool paused, string reason, string operatorId, CancellationToken cancellationToken);
    Task<int> CancelPendingDeliveriesForDomainAsync(string domain, string reason, string operatorId, CancellationToken cancellationToken);
}

public interface IMediaRepository
{
    Task AddAsync(MediaResource media, CancellationToken cancellationToken);
    Task UpdateAsync(MediaResource media, CancellationToken cancellationToken);
    Task<MediaResource?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> IsAuthorizedAsync(Guid id, string requesterActorIri, CancellationToken cancellationToken);
    Task<IReadOnlyList<MediaGarbageCandidate>> ClaimGarbageAsync(
        DateTimeOffset unreferencedBefore,
        DateTimeOffset deletedRetryBefore,
        DateTimeOffset now,
        int count,
        CancellationToken cancellationToken);
    Task MarkPurgedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IMediaService
{
    Task<MediaUploadResult> UploadAsync(MediaUploadCommand command, CancellationToken cancellationToken);
    Task<MediaDownload?> OpenReadAsync(Guid id, string? requesterActorIri, CancellationToken cancellationToken);
}

public interface IRemoteMediaCacheRepository
{
    Task<RemoteMediaSource?> ResolveAuthorizedSourceAsync(
        Guid objectId,
        string sourceToken,
        string? requesterActorIri,
        CancellationToken cancellationToken);
    Task<RemoteMediaCacheEntry?> FindFreshAsync(Guid objectId, string sourceToken, DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveAsync(RemoteMediaCacheEntry entry, CancellationToken cancellationToken);
    Task<int> ExpireAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);
}

public interface IRemoteActorMediaCacheRepository
{
    Task<RemoteActorMediaSource?> ResolveSourceAsync(
        Guid remoteActorId,
        string sourceToken,
        CancellationToken cancellationToken);

    Task<RemoteActorMediaCacheClaim?> ClaimFetchAsync(
        RemoteActorMediaSource source,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    Task<RemoteActorMediaCacheClaim?> ReadAsync(
        Guid remoteActorId,
        string sourceToken,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        Guid entryId,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        Guid entryId,
        string leaseOwner,
        Guid mediaId,
        string? remoteETag,
        DateTimeOffset? remoteLastModified,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<bool> FailAsync(
        Guid entryId,
        string leaseOwner,
        RemoteMediaCacheFailureKind failureKind,
        DateTimeOffset now,
        DateTimeOffset retryAfter,
        CancellationToken cancellationToken);

    Task<int> ExpireAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);
}

public interface IRemoteMediaProxyService
{
    Task<MediaDownload?> OpenReadAsync(
        Guid objectId,
        string sourceToken,
        string? requesterActorIri,
        CancellationToken cancellationToken);

    Task<RemoteMediaOpenResult> OpenActorReadAsync(
        Guid remoteActorId,
        string sourceToken,
        CancellationToken cancellationToken);
}

public interface IRawJsonRetentionStore
{
    Task<RawJsonPurgeResult> PurgeBatchAsync(
        DateTimeOffset activityBefore,
        DateTimeOffset objectBefore,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken);
    Task<Guid> PlaceLegalHoldAsync(
        RawJsonResourceKind resourceKind,
        Guid resourceId,
        string reason,
        string operatorId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken);
    Task<bool> ReleaseLegalHoldAsync(Guid holdId, string operatorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LegalHoldSummary>> ListLegalHoldsAsync(bool activeOnly, int limit, CancellationToken cancellationToken);
}

public interface IMediaMalwareScanner
{
    Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken);
}

public interface IExternalKeyProvisioner
{
    Task<ExternalKeyProvision> CreateRsaKeyAsync(string handle, CancellationToken cancellationToken);
}

public interface ILocalActorAdministration
{
    Task<LocalActorAdministrationResult> CreateAsync(
        string username,
        ActorKind kind,
        string displayName,
        string summaryHtml,
        bool manuallyApprovesFollowers,
        bool discoverable,
        bool indexable,
        string operatorId,
        CancellationToken cancellationToken);

    Task<LocalActorAdministrationResult?> RotateKeyAsync(
        string username,
        TimeSpan overlap,
        string operatorId,
        CancellationToken cancellationToken);
}

public interface IFederationInstrumentation
{
    void InboxAccepted(InboxAcceptanceStatus status);
    void SignatureVerified(SignatureProfile profile);
    void SignatureFailed(string profile);
    void ActivityProcessed(string activityType, TimeSpan delay);
    void RemoteRequest(string domain, int statusCode, TimeSpan duration);
    void PublicKeyCache(bool hit);
    void SsrfRejected();
    void DeliveryCompleted(string domain, int? statusCode, DeliveryAttemptOutcome outcome);
    void LeaseDelta(string workerType, int delta);
}

public interface ISchemaCompatibilityStore
{
    Task<SchemaCompatibilityResult> CheckAsync(string applicationVersion, CancellationToken cancellationToken);
}

public interface IExternalEntityIdService
{
    Task<string> GetOrCreateAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        Guid internalId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken);

    Task<Guid?> ResolveAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        string externalId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetOrCreateManyAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        IReadOnlyCollection<(Guid InternalId, DateTimeOffset Timestamp)> entities,
        CancellationToken cancellationToken);
}

public sealed record MisskeyTokenPrincipal(
    Guid TokenId,
    string ActorIri,
    string Username,
    IReadOnlyList<string> Permissions,
    DateTimeOffset ExpiresAt);

public sealed record MisskeyIssuedToken(
    string Token,
    Guid TokenId,
    string SessionKey,
    string ActorIri,
    string Username,
    IReadOnlyList<string> Permissions,
    DateTimeOffset ExpiresAt);

public sealed record MisskeyTokenSummary(
    Guid Id,
    string Name,
    string? Description,
    string? IconUri,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public interface IMisskeyAuthenticationService
{
    Task<MisskeyIssuedToken> IssueDirectAsync(
        string username,
        string clientName,
        string? description,
        string? iconUri,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);

    Task<MisskeyIssuedToken> IssueAsync(
        string username,
        string sessionKey,
        string clientName,
        string? description,
        string? iconUri,
        string? callbackUri,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);

    Task<MisskeyIssuedToken?> ConsumeSessionAsync(
        string sessionKey,
        CancellationToken cancellationToken);

    Task<MisskeyTokenPrincipal?> ValidateAsync(
        string token,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MisskeyTokenSummary>> ListAsync(
        string actorIri,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        string actorIri,
        Guid tokenId,
        CancellationToken cancellationToken);
}

public enum InitialAdministratorSetupStatus
{
    Created = 0,
    AlreadyInitialized = 1,
    Disabled = 2,
    ValidationFailed = 3,
    ProvisioningFailed = 4
}

public sealed record InitialAdministratorSetupResult(
    InitialAdministratorSetupStatus Status,
    Guid? UserId,
    string? Username,
    string? ActorIri,
    IReadOnlyList<string> SafeErrorCodes);

public interface IInitialSetupState
{
    Task<bool> IsRequiredAsync(CancellationToken cancellationToken);
}

public interface IInitialAdministratorSetupService
{
    Task<InitialAdministratorSetupResult> CreateAsync(
        string username,
        string password,
        CancellationToken cancellationToken);
}

public sealed record StreamEventPage(
    IReadOnlyList<StreamEvent> Events,
    long? OldestAvailableCursor,
    long? LatestCursor,
    bool RequestedCursorExpired);

public interface IStreamEventStore
{
    Task<StreamEventPage> ReadAfterAsync(
        long afterCursor,
        int limit,
        CancellationToken cancellationToken);
}

public interface IStreamEventNotifier
{
    bool IsEnabled { get; }

    Task PublishAsync(IReadOnlyList<long> cursors, CancellationToken cancellationToken);

    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IClientProjectionCache
{
    bool IsEnabled { get; }

    Task<IReadOnlyList<Guid>?> GetTimelineCandidatesAsync(
        string timeline,
        string? viewerActorIri,
        Guid? beforeId,
        int candidateLimit,
        CancellationToken cancellationToken);

    Task SetTimelineCandidatesAsync(
        string timeline,
        string? viewerActorIri,
        Guid? beforeId,
        int candidateLimit,
        IReadOnlyList<Guid> objectIds,
        CancellationToken cancellationToken);

    Task<long?> GetUnreadNotificationCountAsync(
        string recipientActorIri,
        CancellationToken cancellationToken);

    Task SetUnreadNotificationCountAsync(
        string recipientActorIri,
        long count,
        CancellationToken cancellationToken);

    Task InvalidateNotificationsAsync(
        string recipientActorIri,
        CancellationToken cancellationToken);
}

public interface IDurableStreamEventPump
{
    IAsyncEnumerable<StreamEvent> SubscribeAsync(
        long afterCursor,
        int bufferCapacity,
        TimeSpan pollInterval,
        CancellationToken cancellationToken);
}

public sealed class StreamCursorExpiredException(long requestedCursor, long? oldestAvailableCursor)
    : Exception("The requested stream cursor is no longer retained.")
{
    public long RequestedCursor { get; } = requestedCursor;
    public long? OldestAvailableCursor { get; } = oldestAvailableCursor;
}

public sealed class StreamSlowConsumerException()
    : Exception("The streaming consumer did not keep up with the bounded event buffer.");

public sealed record StreamConnectionLeaseToken(Guid Id, string InstanceId);

public interface IStreamConnectionLeaseStore
{
    Task<StreamConnectionLeaseToken?> TryAcquireAsync(
        string? subject,
        string remoteAddress,
        string instanceId,
        int maximumPerSubject,
        int maximumPerAddress,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task<bool> ExtendAsync(
        StreamConnectionLeaseToken token,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task ReleaseAsync(StreamConnectionLeaseToken token, CancellationToken cancellationToken);
}

public sealed record StreamingRuntimeIdentity(string InstanceId);

public interface IInboundSpamEvaluator
{
    ValueTask<SpamAssessment> EvaluateAsync(
        string actorIri,
        string activityType,
        ReadOnlyMemory<byte> rawJson,
        CancellationToken cancellationToken);
}

public sealed class NullFederationInstrumentation : IFederationInstrumentation
{
    public static NullFederationInstrumentation Instance { get; } = new();

    private NullFederationInstrumentation()
    {
    }

    public void InboxAccepted(InboxAcceptanceStatus status) { }
    public void SignatureVerified(SignatureProfile profile) { }
    public void SignatureFailed(string profile) { }
    public void ActivityProcessed(string activityType, TimeSpan delay) { }
    public void RemoteRequest(string domain, int statusCode, TimeSpan duration) { }
    public void PublicKeyCache(bool hit) { }
    public void SsrfRejected() { }
    public void DeliveryCompleted(string domain, int? statusCode, DeliveryAttemptOutcome outcome) { }
    public void LeaseDelta(string workerType, int delta) { }
}
