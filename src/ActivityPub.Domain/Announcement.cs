namespace ActivityPub.Domain;

public enum AnnouncementAudience
{
    Public,
    Authenticated
}

public sealed class Announcement : Entity
{
    private Announcement()
    {
    }

    private Announcement(
        Guid id,
        string title,
        string text,
        string? imageUrl,
        AnnouncementAudience audience,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        string operatorId,
        DateTimeOffset now)
        : base(id)
    {
        ApplyContent(title, text, imageUrl, audience, publishedAt, expiresAt);
        CreatedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        CreatedAt = now;
    }

    public long SortOrdinal { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public AnnouncementAudience Audience { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? DeletedBy { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public long Version { get; private set; }

    public static Announcement Create(
        string title,
        string text,
        string? imageUrl,
        AnnouncementAudience audience,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        string operatorId,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), title, text, imageUrl, audience, publishedAt, expiresAt, operatorId, now);

    public bool IsVisibleTo(string? viewerActorIri, DateTimeOffset now) =>
        DeletedAt is null && PublishedAt <= now && (ExpiresAt is null || ExpiresAt > now) &&
        (Audience == AnnouncementAudience.Public || !string.IsNullOrWhiteSpace(viewerActorIri));

    public void Update(
        string title,
        string text,
        string? imageUrl,
        AnnouncementAudience audience,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        string operatorId,
        DateTimeOffset now)
    {
        EnsureNotDeleted();
        ApplyContent(title, text, imageUrl, audience, publishedAt, expiresAt);
        UpdatedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        UpdatedAt = now;
        Version++;
    }

    public void Delete(string operatorId, DateTimeOffset now)
    {
        EnsureNotDeleted();
        DeletedBy = DomainText.Required(operatorId, nameof(operatorId), 256);
        DeletedAt = now;
        Version++;
    }

    private void ApplyContent(
        string title,
        string text,
        string? imageUrl,
        AnnouncementAudience audience,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt)
    {
        if (!Enum.IsDefined(audience))
        {
            throw new DomainException("Announcement audience is invalid.");
        }

        if (expiresAt is not null && expiresAt <= publishedAt)
        {
            throw new DomainException("Announcement expiration must be later than its publication time.");
        }

        Title = DomainText.Required(title, nameof(title), 256);
        Text = DomainText.Required(text, nameof(text), 64_000);
        ImageUrl = NormalizeImageUrl(imageUrl);
        Audience = audience;
        PublishedAt = publishedAt;
        ExpiresAt = expiresAt;
    }

    private static string? NormalizeImageUrl(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = DomainText.Required(value, nameof(value), 2_048).Trim();
        if (normalized.Any(char.IsControl) || normalized.Contains('\\'))
        {
            throw new DomainException("Announcement image URL contains unsafe characters.");
        }

        if (normalized.StartsWith('/') &&
            !normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return normalized;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new DomainException("Announcement image URL must be a rooted path or an absolute HTTP(S) URL without user information or a fragment.");
        }

        return uri.AbsoluteUri;
    }

    private void EnsureNotDeleted()
    {
        if (DeletedAt is not null)
        {
            throw new DomainException("A deleted announcement cannot be changed.");
        }
    }
}

public sealed class AnnouncementRead : Entity
{
    private AnnouncementRead()
    {
    }

    private AnnouncementRead(Guid id, Guid announcementId, string readerActorIri, DateTimeOffset now)
        : base(id)
    {
        if (announcementId == Guid.Empty)
        {
            throw new DomainException("Announcement identifiers cannot be empty.");
        }

        AnnouncementId = announcementId;
        ReaderActorIri = CanonicalIri.RequireAbsoluteHttp(readerActorIri, nameof(readerActorIri));
        CreatedAt = now;
    }

    public Guid AnnouncementId { get; private set; }
    public string ReaderActorIri { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static AnnouncementRead Create(Guid announcementId, string readerActorIri, DateTimeOffset now) =>
        new(Guid.NewGuid(), announcementId, readerActorIri, now);
}
