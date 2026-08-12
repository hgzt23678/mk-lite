using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class UserNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void OnlyRecipientCanReadOrDismissNotification()
    {
        UserNotification notification = UserNotification.Create(
            "https://local.example/users/alice",
            "https://remote.example/users/bob",
            UserNotificationKind.Mention,
            "https://remote.example/activities/1",
            "https://remote.example/objects/1",
            null,
            Now);

        Assert.Throws<DomainException>(() => notification.MarkRead("https://local.example/users/mallory", Now));
        notification.MarkRead("https://local.example/users/alice", Now.AddMinutes(1));
        notification.Dismiss("https://local.example/users/alice", Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(1), notification.ReadAt);
        Assert.Equal(Now.AddMinutes(2), notification.DismissedAt);
    }
}
