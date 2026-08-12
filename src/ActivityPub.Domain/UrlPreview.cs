namespace ActivityPub.Domain;

public sealed class UrlPreview : Entity
{
    private UrlPreview()
    {
    }

    private UrlPreview(
        Guid id,
        string url,
        string title,
        string? description,
        string? thumbnail,
        string? icon,
        string? siteName,
        string? playerUrl,
        int? playerWidth,
        int? playerHeight,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt)
        : base(id)
    {
        Url = DomainText.Required(url, nameof(url), 2_048);
        Title = DomainText.Optional(title, nameof(title), 1_024) ?? string.Empty;
        Description = DomainText.Optional(description, nameof(description), 2_048);
        Thumbnail = DomainText.Optional(thumbnail, nameof(thumbnail), 2_048);
        Icon = DomainText.Optional(icon, nameof(icon), 2_048);
        SiteName = DomainText.Optional(siteName, nameof(siteName), 256);
        PlayerUrl = DomainText.Optional(playerUrl, nameof(playerUrl), 2_048);
        PlayerWidth = playerWidth;
        PlayerHeight = playerHeight;
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
    }

    public string Url { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Thumbnail { get; private set; }
    public string? Icon { get; private set; }
    public string? SiteName { get; private set; }
    public string? PlayerUrl { get; private set; }
    public int? PlayerWidth { get; private set; }
    public int? PlayerHeight { get; private set; }
    public DateTimeOffset FetchedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public void Replace(UrlPreview updated)
    {
        Title = updated.Title;
        Description = updated.Description;
        Thumbnail = updated.Thumbnail;
        Icon = updated.Icon;
        SiteName = updated.SiteName;
        PlayerUrl = updated.PlayerUrl;
        PlayerWidth = updated.PlayerWidth;
        PlayerHeight = updated.PlayerHeight;
        FetchedAt = updated.FetchedAt;
        ExpiresAt = updated.ExpiresAt;
    }

    public static UrlPreview Create(
        string url,
        string title,
        string? description,
        string? thumbnail,
        string? icon,
        string? siteName,
        string? playerUrl,
        int? playerWidth,
        int? playerHeight,
        DateTimeOffset fetchedAt,
        TimeSpan lifetime) =>
        new(
            Guid.NewGuid(),
            url,
            title,
            description,
            thumbnail,
            icon,
            siteName,
            playerUrl,
            playerWidth,
            playerHeight,
            fetchedAt,
            fetchedAt + lifetime);
}
