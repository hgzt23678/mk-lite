namespace ActivityPub.Domain;

public sealed class MediaResource : Entity
{
    private MediaResource()
    {
    }

    private MediaResource(
        Guid id,
        string ownerActorIri,
        string storageKey,
        string contentHash,
        string detectedMediaType,
        string originalFileName,
        long length,
        Visibility visibility,
        DateTimeOffset now)
        : base(id)
    {
        OwnerActorIri = CanonicalIri.RequireAbsoluteHttp(ownerActorIri, nameof(ownerActorIri));
        StorageKey = DomainText.Required(storageKey, nameof(storageKey), 1_024);
        ContentHash = DomainText.Required(contentHash, nameof(contentHash), 128);
        DetectedMediaType = DomainText.Required(detectedMediaType, nameof(detectedMediaType), 256);
        OriginalFileName = DomainText.Required(originalFileName, nameof(originalFileName), 512);
        if (length <= 0)
        {
            throw new DomainException("Media length must be positive.");
        }

        Length = length;
        Visibility = visibility;
        State = MediaState.PendingScan;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string OwnerActorIri { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentHash { get; private set; } = string.Empty;
    public string DetectedMediaType { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public long Length { get; private set; }
    public Guid? FolderId { get; private set; }
    public bool IsSensitive { get; private set; }
    public string? Comment { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public long? DurationMilliseconds { get; private set; }
    public string? ThumbnailStorageKey { get; private set; }
    public Visibility Visibility { get; private set; }
    public MediaState State { get; private set; }
    public string? QuarantineReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? PurgedAt { get; private set; }

    public static MediaResource Create(
        string ownerActorIri,
        string storageKey,
        string contentHash,
        string detectedMediaType,
        string originalFileName,
        long length,
        Visibility visibility,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerActorIri, storageKey, contentHash, detectedMediaType, originalFileName, length, visibility, now);

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
        {
            throw new DomainException("A drive file name must contain 1 to 256 characters.");
        }

        OriginalFileName = name;
    }

    public void AssignDriveMetadata(Guid? folderId, bool isSensitive, string? comment, DateTimeOffset now)
    {
        if (folderId == Guid.Empty)
        {
            throw new DomainException("A drive folder identifier cannot be empty.");
        }

        FolderId = folderId;
        IsSensitive = isSensitive;
        Comment = DomainText.Optional(comment, nameof(comment), 512);
        UpdatedAt = now;
    }

    public void MarkAvailable(
        string storageKey,
        string contentHash,
        string detectedMediaType,
        long length,
        int? width,
        int? height,
        long? durationMilliseconds,
        string? thumbnailStorageKey,
        DateTimeOffset now)
    {
        if (length <= 0)
        {
            throw new DomainException("Media length must be positive.");
        }

        StorageKey = DomainText.Required(storageKey, nameof(storageKey), 1_024);
        ContentHash = DomainText.Required(contentHash, nameof(contentHash), 128);
        DetectedMediaType = DomainText.Required(detectedMediaType, nameof(detectedMediaType), 256);
        Length = length;
        Width = width;
        Height = height;
        DurationMilliseconds = durationMilliseconds;
        ThumbnailStorageKey = DomainText.Optional(thumbnailStorageKey, nameof(thumbnailStorageKey), 1_024);
        State = MediaState.Available;
        QuarantineReason = null;
        UpdatedAt = now;
    }

    public void Quarantine(string reason, DateTimeOffset now)
    {
        State = MediaState.Quarantined;
        QuarantineReason = DomainText.Required(reason, nameof(reason), 2_000);
        UpdatedAt = now;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        State = MediaState.Rejected;
        QuarantineReason = DomainText.Required(reason, nameof(reason), 2_000);
        UpdatedAt = now;
    }

    public void Delete(DateTimeOffset now)
    {
        State = MediaState.Deleted;
        DeletedAt ??= now;
        UpdatedAt = now;
    }

    public void SetVisibility(Visibility visibility, DateTimeOffset now)
    {
        if (State != MediaState.Available)
        {
            throw new DomainException("Only available media can change visibility.");
        }

        if (now < UpdatedAt)
        {
            throw new DomainException("Media visibility cannot be changed in the past.");
        }

        Visibility = visibility;
        UpdatedAt = now;
    }

    public void RefreshCacheReference(DateTimeOffset now)
    {
        if (State != MediaState.Available)
        {
            throw new DomainException("Only available media can refresh a cache reference.");
        }

        if (now < UpdatedAt)
        {
            throw new DomainException("A media cache reference cannot be refreshed in the past.");
        }

        UpdatedAt = now;
    }

    public void MarkPurged(DateTimeOffset now)
    {
        if (State != MediaState.Deleted)
        {
            throw new DomainException("Only deleted media can be marked as purged.");
        }

        PurgedAt = now;
        UpdatedAt = now;
    }
}

public sealed class MediaAttachment : Entity
{
    private MediaAttachment()
    {
    }

    private MediaAttachment(Guid id, Guid mediaId, Guid objectId)
        : base(id)
    {
        if (mediaId == Guid.Empty || objectId == Guid.Empty)
        {
            throw new DomainException("Media attachment identifiers cannot be empty.");
        }

        MediaId = mediaId;
        ObjectId = objectId;
    }

    public Guid MediaId { get; private set; }
    public Guid ObjectId { get; private set; }

    public static MediaAttachment Create(Guid mediaId, Guid objectId) => new(Guid.NewGuid(), mediaId, objectId);
}
