namespace ActivityPub.Domain;

public enum RemoteActorMediaKind
{
    Avatar,
    Banner
}

public enum RemoteMediaCacheFailureKind
{
    NotFound,
    Unsafe,
    Unavailable
}

public sealed class RemoteActorMediaCacheEntry : Entity
{
    private RemoteActorMediaCacheEntry()
    {
    }

    private RemoteActorMediaCacheEntry(
        Guid id,
        Guid remoteActorId,
        RemoteActorMediaKind kind,
        string sourceIri,
        string sourceToken,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt)
        : base(id)
    {
        if (remoteActorId == Guid.Empty)
        {
            throw new DomainException("Remote actor media cache actor identifier cannot be empty.");
        }

        RemoteActorId = remoteActorId;
        Kind = kind;
        SourceIri = CanonicalIri.RequireAbsoluteHttp(sourceIri, nameof(sourceIri));
        SourceToken = RemoteMediaSourceToken.Require(sourceToken);
        if (!string.Equals(RemoteMediaSourceToken.Create(SourceIri), SourceToken, StringComparison.Ordinal))
        {
            throw new DomainException("Remote actor media cache source token does not match its source IRI.");
        }

        LeaseOwner = DomainText.Required(leaseOwner, nameof(leaseOwner), 128);
        if (leaseExpiresAt <= now)
        {
            throw new DomainException("Remote actor media cache lease must expire in the future.");
        }

        CreatedAt = now;
        ExpiresAt = now;
        LeaseExpiresAt = leaseExpiresAt;
    }

    public Guid RemoteActorId { get; private set; }
    public RemoteActorMediaKind Kind { get; private set; }
    public string SourceIri { get; private set; } = string.Empty;
    public string SourceToken { get; private set; } = string.Empty;
    public Guid? MediaId { get; private set; }
    public string? RemoteETag { get; private set; }
    public DateTimeOffset? RemoteLastModified { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RefreshedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public RemoteMediaCacheFailureKind? FailureKind { get; private set; }
    public DateTimeOffset? RetryAfter { get; private set; }

    public bool IsFresh(DateTimeOffset now) => MediaId is not null && ExpiresAt > now;

    public bool HasActiveLease(DateTimeOffset now) =>
        LeaseOwner is not null && LeaseExpiresAt is not null && LeaseExpiresAt > now;

    public bool HasActiveFailure(DateTimeOffset now) => FailureKind is not null && RetryAfter > now;

    public static RemoteActorMediaCacheEntry CreateClaimed(
        Guid remoteActorId,
        RemoteActorMediaKind kind,
        string sourceIri,
        string sourceToken,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt) =>
        new(Guid.NewGuid(), remoteActorId, kind, sourceIri, sourceToken, leaseOwner, now, leaseExpiresAt);

    public bool TryClaim(string leaseOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt)
    {
        if (IsFresh(now) || HasActiveLease(now) || HasActiveFailure(now))
        {
            return false;
        }

        if (leaseExpiresAt <= now)
        {
            throw new DomainException("Remote actor media cache lease must expire in the future.");
        }

        LeaseOwner = DomainText.Required(leaseOwner, nameof(leaseOwner), 128);
        LeaseExpiresAt = leaseExpiresAt;
        FailureKind = null;
        RetryAfter = null;
        return true;
    }

    public bool RenewLease(string leaseOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt)
    {
        if (!OwnsActiveLease(leaseOwner, now))
        {
            return false;
        }

        if (leaseExpiresAt <= now)
        {
            throw new DomainException("Remote actor media cache lease must expire in the future.");
        }

        LeaseExpiresAt = leaseExpiresAt;
        return true;
    }

    public bool Complete(
        string leaseOwner,
        Guid mediaId,
        string? remoteETag,
        DateTimeOffset? remoteLastModified,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        if (!OwnsActiveLease(leaseOwner, now))
        {
            return false;
        }

        if (mediaId == Guid.Empty || expiresAt <= now)
        {
            throw new DomainException("Completed remote actor media cache data is invalid.");
        }

        MediaId = mediaId;
        RemoteETag = DomainText.Optional(remoteETag, nameof(remoteETag), 512);
        RemoteLastModified = remoteLastModified;
        RefreshedAt = now;
        ExpiresAt = expiresAt;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        FailureKind = null;
        RetryAfter = null;
        return true;
    }

    public bool Fail(
        string leaseOwner,
        RemoteMediaCacheFailureKind failureKind,
        DateTimeOffset now,
        DateTimeOffset retryAfter)
    {
        if (!OwnsActiveLease(leaseOwner, now))
        {
            return false;
        }

        if (retryAfter <= now)
        {
            throw new DomainException("Remote actor media cache failure retry must be in the future.");
        }

        FailureKind = failureKind;
        RetryAfter = retryAfter;
        ExpiresAt = retryAfter;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        return true;
    }

    private bool OwnsActiveLease(string leaseOwner, DateTimeOffset now) =>
        LeaseOwner is not null &&
        string.Equals(LeaseOwner, leaseOwner, StringComparison.Ordinal) &&
        LeaseExpiresAt is not null &&
        LeaseExpiresAt > now;
}
