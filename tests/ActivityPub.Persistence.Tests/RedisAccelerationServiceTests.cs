using ActivityPub.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActivityPub.Persistence.Tests;

[Collection(RedisNotifierFixtureDefinition.Name)]
public sealed class RedisAccelerationServiceTests(RedisNotifierFixture fixture)
{
    [Fact]
    public async Task DeliveryWakeupCrossesProcessBoundaryWithoutOwningTheJob()
    {
        string channel = "test-delivery-" + Guid.NewGuid().ToString("N");
        using var waiting = Create(channel);
        using var publishing = Create(channel);

        Task wait = waiting.WaitForDeliveryAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(250);
        await publishing.NotifyDeliveryAvailableAsync(CancellationToken.None);

        await wait.WaitAsync(TimeSpan.FromSeconds(5));

        Task inboxWait = waiting.WaitForInboxAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(100);
        await publishing.NotifyInboxAvailableAsync(CancellationToken.None);
        await inboxWait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EnabledAccelerationPropagatesWorkerShutdownWhileWaiting()
    {
        using RedisAccelerationService cache = Create("test-cancellation");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.WaitForDeliveryAsync(TimeSpan.FromSeconds(30), cancellation.Token));
    }

    [Fact]
    public async Task TimelineCacheStoresOnlyCandidateIdsAndSeparatesViewers()
    {
        using RedisAccelerationService cache = Create("test-delivery");
        Guid[] aliceIds = [Guid.NewGuid(), Guid.NewGuid()];
        Guid[] bobIds = [Guid.NewGuid()];

        await cache.SetTimelineCandidatesAsync(
            "home", "https://local.example/users/alice", null, 121, aliceIds, CancellationToken.None);
        await cache.SetTimelineCandidatesAsync(
            "home", "https://local.example/users/bob", null, 121, bobIds, CancellationToken.None);

        Assert.Equal(aliceIds, await cache.GetTimelineCandidatesAsync(
            "home", "https://local.example/users/alice", null, 121, CancellationToken.None));
        Assert.Equal(bobIds, await cache.GetTimelineCandidatesAsync(
            "home", "https://local.example/users/bob", null, 121, CancellationToken.None));
    }

    [Fact]
    public async Task NotificationCountCanBeInvalidatedAfterThePostgresMutation()
    {
        using RedisAccelerationService cache = Create("test-delivery");
        const string actor = "https://local.example/users/alice";

        await cache.SetUnreadNotificationCountAsync(actor, 7, CancellationToken.None);
        Assert.Equal(7, await cache.GetUnreadNotificationCountAsync(actor, CancellationToken.None));

        await cache.InvalidateNotificationsAsync(actor, CancellationToken.None);
        Assert.Null(await cache.GetUnreadNotificationCountAsync(actor, CancellationToken.None));
    }

    [Fact]
    public async Task DisabledAccelerationFallsBackWithoutCreatingCacheState()
    {
        using var cache = new RedisAccelerationService(
            null,
            "activitypub",
            "unused",
            "unused-inbox",
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(10),
            NullLogger<RedisAccelerationService>.Instance);

        Assert.False(cache.IsEnabled);
        await cache.SetUnreadNotificationCountAsync("https://local.example/users/alice", 3, CancellationToken.None);
        Assert.Null(await cache.GetUnreadNotificationCountAsync(
            "https://local.example/users/alice", CancellationToken.None));
    }

    [Fact]
    public async Task UnavailableRedisDoesNotPreventPostgresFallback()
    {
        using var cache = new RedisAccelerationService(
            "127.0.0.1:1,abortConnect=false,connectTimeout=100",
            "activitypub",
            "unavailable-delivery",
            "unavailable-inbox",
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(10),
            NullLogger<RedisAccelerationService>.Instance);

        await cache.NotifyDeliveryAvailableAsync(CancellationToken.None);
        await cache.SetTimelineCandidatesAsync(
            "home",
            "https://local.example/users/alice",
            null,
            10,
            [Guid.NewGuid()],
            CancellationToken.None);
        Assert.Null(await cache.GetTimelineCandidatesAsync(
            "home",
            "https://local.example/users/alice",
            null,
            10,
            CancellationToken.None));
    }

    private RedisAccelerationService Create(string channel) => new(
        fixture.ConnectionString,
        "test-" + Guid.NewGuid().ToString("N"),
        channel,
        channel + "-inbox",
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
        NullLogger<RedisAccelerationService>.Instance);
}
