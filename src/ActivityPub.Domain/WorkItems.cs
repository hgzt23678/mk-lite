namespace ActivityPub.Domain;

public abstract class DurableWorkItem : Entity
{
    protected DurableWorkItem()
    {
    }

    protected DurableWorkItem(Guid id, DateTimeOffset now)
        : base(id)
    {
        State = WorkItemState.Pending;
        AvailableAt = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public WorkItemState State { get; protected set; }
    public DateTimeOffset AvailableAt { get; protected set; }
    public string? LeaseOwner { get; protected set; }
    public DateTimeOffset? LeaseExpiresAt { get; protected set; }
    public int AttemptCount { get; protected set; }
    public string? LastErrorCode { get; protected set; }
    public string? LastError { get; protected set; }
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset UpdatedAt { get; protected set; }
    public DateTimeOffset? CompletedAt { get; protected set; }
    public long Version { get; protected set; }

    public bool IsClaimable(DateTimeOffset now) =>
        (State == WorkItemState.Pending && AvailableAt <= now) ||
        (State == WorkItemState.Leased && LeaseExpiresAt <= now);

    public void AcquireLease(string owner, DateTimeOffset now, TimeSpan duration)
    {
        if (!IsClaimable(now))
        {
            throw new DomainException("Work item is not available for leasing.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new DomainException("Lease duration must be positive.");
        }

        LeaseOwner = DomainText.Required(owner, nameof(owner), 256);
        LeaseExpiresAt = now.Add(duration);
        State = WorkItemState.Leased;
        AttemptCount++;
        UpdatedAt = now;
        Version++;
    }

    public void ExtendLease(string owner, DateTimeOffset now, TimeSpan duration)
    {
        EnsureLeaseOwner(owner, now);
        LeaseExpiresAt = now.Add(duration);
        UpdatedAt = now;
        Version++;
    }

    public void Succeed(string owner, DateTimeOffset now)
    {
        EnsureLeaseOwner(owner, now);
        State = WorkItemState.Succeeded;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        LastErrorCode = null;
        LastError = null;
        CompletedAt = now;
        UpdatedAt = now;
        Version++;
    }

    public void ScheduleRetry(string owner, DateTimeOffset now, DateTimeOffset availableAt, string code, string error)
    {
        EnsureLeaseOwner(owner, now);
        if (availableAt <= now)
        {
            throw new DomainException("Retry time must be in the future.");
        }

        State = WorkItemState.Pending;
        AvailableAt = availableAt;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        LastErrorCode = DomainText.Required(code, nameof(code), 128);
        LastError = DomainText.Required(error, nameof(error), 4_096);
        UpdatedAt = now;
        Version++;
    }

    public void DeadLetter(string owner, DateTimeOffset now, string code, string error)
    {
        EnsureLeaseOwner(owner, now);
        State = WorkItemState.DeadLettered;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        LastErrorCode = DomainText.Required(code, nameof(code), 128);
        LastError = DomainText.Required(error, nameof(error), 4_096);
        CompletedAt = now;
        UpdatedAt = now;
        Version++;
    }

    public void Cancel(DateTimeOffset now, string reason)
    {
        if (State is WorkItemState.Succeeded or WorkItemState.DeadLettered)
        {
            throw new DomainException("A terminal work item cannot be cancelled.");
        }

        State = WorkItemState.Cancelled;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        LastErrorCode = "cancelled";
        LastError = DomainText.Required(reason, nameof(reason), 4_096);
        CompletedAt = now;
        UpdatedAt = now;
        Version++;
    }

    public void RequeueFromDeadLetter(DateTimeOffset now)
    {
        if (State != WorkItemState.DeadLettered)
        {
            throw new DomainException("Only dead-lettered work can be manually requeued.");
        }

        State = WorkItemState.Pending;
        AvailableAt = now;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        LastErrorCode = null;
        LastError = null;
        CompletedAt = null;
        UpdatedAt = now;
        Version++;
    }

    public void ReleaseLeaseWithoutAttempt(string owner, DateTimeOffset now, DateTimeOffset availableAt)
    {
        EnsureLeaseOwner(owner, now);
        if (availableAt <= now)
        {
            throw new DomainException("Released work must become available in the future.");
        }

        State = WorkItemState.Pending;
        AvailableAt = availableAt;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        AttemptCount = Math.Max(0, AttemptCount - 1);
        UpdatedAt = now;
        Version++;
    }

    protected void EnsureLeaseOwner(string owner, DateTimeOffset now)
    {
        if (State != WorkItemState.Leased || LeaseExpiresAt <= now ||
            !string.Equals(LeaseOwner, owner, StringComparison.Ordinal))
        {
            throw new DomainException("The active lease is not owned by this worker.");
        }
    }
}

public sealed class InboxItem : DurableWorkItem
{
    private InboxItem()
    {
    }

    private InboxItem(
        Guid id,
        string activityIri,
        string actorIri,
        string activityType,
        byte[] rawBody,
        string payloadHash,
        SignatureProfile signatureProfile,
        string keyIri,
        DateTimeOffset signatureCreatedAt,
        DateTimeOffset now)
        : base(id, now)
    {
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        ActivityType = DomainText.Required(activityType, nameof(activityType), 256);
        RawBody = rawBody.Length is > 0 and <= 2_000_000
            ? rawBody.ToArray()
            : throw new DomainException("Inbox payload size is outside the accepted range.");
        PayloadHash = DomainText.Required(payloadHash, nameof(payloadHash), 128);
        SignatureProfile = signatureProfile;
        KeyIri = CanonicalIri.RequireAbsoluteHttp(keyIri, nameof(keyIri));
        SignatureCreatedAt = signatureCreatedAt;
    }

    public string ActivityIri { get; private set; } = string.Empty;
    public string ActorIri { get; private set; } = string.Empty;
    public string ActivityType { get; private set; } = string.Empty;
    public byte[] RawBody { get; private set; } = [];
    public string PayloadHash { get; private set; } = string.Empty;
    public SignatureProfile SignatureProfile { get; private set; }
    public string KeyIri { get; private set; } = string.Empty;
    public DateTimeOffset SignatureCreatedAt { get; private set; }
    public bool IsQuarantined { get; private set; }
    public string? QuarantineReason { get; private set; }

    public static InboxItem Accept(
        string activityIri,
        string actorIri,
        string activityType,
        byte[] rawBody,
        SignatureProfile signatureProfile,
        string keyIri,
        DateTimeOffset signatureCreatedAt,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            activityIri,
            actorIri,
            activityType,
            rawBody,
            PayloadDigest.Sha256Hex(rawBody),
            signatureProfile,
            keyIri,
            signatureCreatedAt,
            now);

    public void Quarantine(string owner, DateTimeOffset now, string reason)
    {
        EnsureLeaseOwner(owner, now);
        IsQuarantined = true;
        QuarantineReason = DomainText.Required(reason, nameof(reason), 4_096);
        DeadLetter(owner, now, "quarantined", reason);
    }

    public void RequeueInboxFromDeadLetter(DateTimeOffset now)
    {
        RequeueFromDeadLetter(now);
        IsQuarantined = false;
        QuarantineReason = null;
    }
}

public sealed class Delivery : DurableWorkItem
{
    private Delivery()
    {
    }

    private Delivery(
        Guid id,
        Guid activityId,
        string activityIri,
        string endpointIri,
        string actorIri,
        byte[] payload,
        SignatureProfile signatureProfile,
        DateTimeOffset now)
        : base(id, now)
    {
        ActivityId = activityId;
        ActivityIri = CanonicalIri.RequireAbsoluteHttp(activityIri, nameof(activityIri));
        EndpointIri = CanonicalIri.RequireAbsoluteHttp(endpointIri, nameof(endpointIri));
        RemoteDomain = new Uri(EndpointIri).IdnHost.ToLowerInvariant();
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        Payload = payload.Length is > 0 and <= 2_000_000
            ? payload.ToArray()
            : throw new DomainException("Delivery payload size is outside the accepted range.");
        PayloadHash = PayloadDigest.Sha256Hex(payload);
        SignatureProfile = signatureProfile;
    }

    public Guid ActivityId { get; private set; }
    public string ActivityIri { get; private set; } = string.Empty;
    public string EndpointIri { get; private set; } = string.Empty;
    public string RemoteDomain { get; private set; } = string.Empty;
    public string ActorIri { get; private set; } = string.Empty;
    public byte[] Payload { get; private set; } = [];
    public string PayloadHash { get; private set; } = string.Empty;
    public SignatureProfile SignatureProfile { get; private set; }
    public int? LastStatusCode { get; private set; }
    public DateTimeOffset? EndpointRediscoveryAt { get; private set; }

    public static Delivery Create(
        Guid activityId,
        string activityIri,
        string endpointIri,
        string actorIri,
        byte[] payload,
        SignatureProfile signatureProfile,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), activityId, activityIri, endpointIri, actorIri, payload, signatureProfile, now);

    public void RecordStatusCode(int? statusCode)
    {
        LastStatusCode = statusCode;
    }

    public void SelectSignatureProfile(SignatureProfile signatureProfile)
    {
        SignatureProfile = signatureProfile;
    }

    public void MarkEndpointRediscovered(DateTimeOffset now)
    {
        EndpointRediscoveryAt = now;
    }

    public void ReplaceEndpoint(string owner, string endpointIri, DateTimeOffset now)
    {
        EnsureLeaseOwner(owner, now);
        EndpointIri = CanonicalIri.RequireAbsoluteHttp(endpointIri, nameof(endpointIri));
        RemoteDomain = new Uri(EndpointIri).IdnHost.ToLowerInvariant();
        EndpointRediscoveryAt = now;
    }

    public Delivery ForkForEndpoint(string endpointIri, DateTimeOffset now) =>
        Create(ActivityId, ActivityIri, endpointIri, ActorIri, Payload, SignatureProfile, now);
}

public sealed class DeliveryTarget : Entity
{
    private DeliveryTarget()
    {
    }

    private DeliveryTarget(Guid id, Guid deliveryId, string actorIri)
        : base(id)
    {
        DeliveryId = deliveryId;
        ActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
    }

    public Guid DeliveryId { get; private set; }
    public string ActorIri { get; private set; } = string.Empty;

    public static DeliveryTarget Create(Guid deliveryId, string actorIri) =>
        new(Guid.NewGuid(), deliveryId, actorIri);
}

public sealed class DeliveryEndpointChange : Entity
{
    private DeliveryEndpointChange()
    {
    }

    private DeliveryEndpointChange(
        Guid id,
        Guid deliveryId,
        string previousEndpointIri,
        string replacementEndpointIri,
        int recipientCount,
        DateTimeOffset discoveredAt)
        : base(id)
    {
        DeliveryId = deliveryId;
        PreviousEndpointIri = CanonicalIri.RequireAbsoluteHttp(previousEndpointIri, nameof(previousEndpointIri));
        ReplacementEndpointIri = CanonicalIri.RequireAbsoluteHttp(replacementEndpointIri, nameof(replacementEndpointIri));
        if (recipientCount < 1)
        {
            throw new DomainException("Endpoint replacement must cover at least one recipient.");
        }

        RecipientCount = recipientCount;
        DiscoveredAt = discoveredAt;
    }

    public Guid DeliveryId { get; private set; }
    public string PreviousEndpointIri { get; private set; } = string.Empty;
    public string ReplacementEndpointIri { get; private set; } = string.Empty;
    public int RecipientCount { get; private set; }
    public DateTimeOffset DiscoveredAt { get; private set; }

    public static DeliveryEndpointChange Create(
        Guid deliveryId,
        string previousEndpointIri,
        string replacementEndpointIri,
        int recipientCount,
        DateTimeOffset discoveredAt) =>
        new(Guid.NewGuid(), deliveryId, previousEndpointIri, replacementEndpointIri, recipientCount, discoveredAt);
}

public sealed class DeliveryAttempt : Entity
{
    private DeliveryAttempt()
    {
    }

    private DeliveryAttempt(
        Guid id,
        Guid deliveryId,
        int attemptNumber,
        DeliveryAttemptOutcome outcome,
        int? statusCode,
        string? errorCode,
        string? error,
        TimeSpan duration,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
        : base(id)
    {
        DeliveryId = deliveryId;
        AttemptNumber = attemptNumber;
        Outcome = outcome;
        StatusCode = statusCode;
        ErrorCode = DomainText.Optional(errorCode, nameof(errorCode), 128);
        Error = DomainText.Optional(error, nameof(error), 4_096);
        DurationMilliseconds = checked((long)duration.TotalMilliseconds);
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }

    public Guid DeliveryId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DeliveryAttemptOutcome Outcome { get; private set; }
    public int? StatusCode { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? Error { get; private set; }
    public long DurationMilliseconds { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    public static DeliveryAttempt Create(
        Guid deliveryId,
        int attemptNumber,
        DeliveryAttemptOutcome outcome,
        int? statusCode,
        string? errorCode,
        string? error,
        TimeSpan duration,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt) =>
        new(Guid.NewGuid(), deliveryId, attemptNumber, outcome, statusCode, errorCode, error, duration, startedAt, completedAt);
}

public sealed class DeadLetter : Entity
{
    private DeadLetter()
    {
    }

    private DeadLetter(Guid id, string sourceType, Guid sourceId, string reasonCode, string reason, DateTimeOffset now)
        : base(id)
    {
        SourceType = DomainText.Required(sourceType, nameof(sourceType), 128);
        SourceId = sourceId;
        ReasonCode = DomainText.Required(reasonCode, nameof(reasonCode), 128);
        Reason = DomainText.Required(reason, nameof(reason), 4_096);
        CreatedAt = now;
    }

    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReplayedAt { get; private set; }
    public string? ReplayedBy { get; private set; }

    public static DeadLetter Create(string sourceType, Guid sourceId, string reasonCode, string reason, DateTimeOffset now) =>
        new(Guid.NewGuid(), sourceType, sourceId, reasonCode, reason, now);

    public void MarkReplayed(string operatorId, DateTimeOffset now)
    {
        if (ReplayedAt is not null)
        {
            throw new DomainException("Dead letter was already replayed.");
        }

        ReplayedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        ReplayedAt = now;
    }
}
