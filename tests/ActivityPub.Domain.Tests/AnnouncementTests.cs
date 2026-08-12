using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class AnnouncementTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PublicVisibilityHonorsPublicationWindowAndDeletion()
    {
        Announcement announcement = Announcement.Create(
            "Maintenance",
            "Service work is scheduled.",
            "/media/maintenance.png",
            AnnouncementAudience.Public,
            Now.AddMinutes(1),
            Now.AddHours(1),
            "admin",
            Now);

        Assert.False(announcement.IsVisibleTo(null, Now));
        Assert.True(announcement.IsVisibleTo(null, Now.AddMinutes(1)));
        Assert.False(announcement.IsVisibleTo(null, Now.AddHours(1)));

        announcement.Delete("admin", Now.AddMinutes(2));

        Assert.False(announcement.IsVisibleTo("https://local.example/users/alice", Now.AddMinutes(3)));
    }

    [Fact]
    public void AuthenticatedAnnouncementNeverLeaksToAnonymousViewer()
    {
        Announcement announcement = Announcement.Create(
            "Members",
            "Signed-in users only.",
            null,
            AnnouncementAudience.Authenticated,
            Now,
            null,
            "admin",
            Now);

        Assert.False(announcement.IsVisibleTo(null, Now));
        Assert.True(announcement.IsVisibleTo("https://local.example/users/alice", Now));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//tracker.example/image.png")]
    [InlineData("https://user:secret@example.test/image.png")]
    [InlineData("https://example.test/image.png#fragment")]
    [InlineData("/media\\image.png")]
    public void UnsafeImageUrlsAreRejected(string imageUrl)
    {
        Assert.Throws<DomainException>(() => Announcement.Create(
            "Unsafe",
            "Unsafe image URL.",
            imageUrl,
            AnnouncementAudience.Public,
            Now,
            null,
            "admin",
            Now));
    }

    [Fact]
    public void UpdateRetainsAuditActorsAndRejectsInvalidWindow()
    {
        Announcement announcement = Announcement.Create(
            "Original",
            "Original text.",
            "https://cdn.example.test/image.png",
            AnnouncementAudience.Public,
            Now,
            null,
            "creator",
            Now);

        announcement.Update(
            "Updated",
            "Updated text.",
            null,
            AnnouncementAudience.Authenticated,
            Now.AddHours(1),
            Now.AddHours(2),
            "editor",
            Now.AddMinutes(1));

        Assert.Equal("creator", announcement.CreatedBy);
        Assert.Equal("editor", announcement.UpdatedBy);
        Assert.Equal(1, announcement.Version);
        Assert.Throws<DomainException>(() => announcement.Update(
            "Invalid",
            "Invalid window.",
            null,
            AnnouncementAudience.Public,
            Now.AddHours(2),
            Now.AddHours(1),
            "editor",
            Now.AddMinutes(2)));
    }
}
