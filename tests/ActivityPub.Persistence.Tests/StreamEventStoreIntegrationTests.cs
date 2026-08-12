using System.Text;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence.Tests;

[Collection(PostgreSqlFixtureDefinition.Name)]
public sealed class StreamEventStoreIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 13, 50, 0, TimeSpan.Zero);

    [Fact]
    public async Task ObjectMutationAndStreamEventCommitAtomicallyAndReplayDoesNotDuplicate()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository deliveries = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        IStreamEventStore events = scope.ServiceProvider.GetRequiredService<IStreamEventStore>();
        string key = "stream-" + Guid.NewGuid().ToString("N");
        OutboundCommit first = CreateCommit(key, new string('a', 64));

        OutboundCommitResult committed = await deliveries.CommitOutboundAsync(first, CancellationToken.None);
        OutboundCommitResult existing = await deliveries.CommitOutboundAsync(first, CancellationToken.None);
        StreamEventPage page = await events.ReadAfterAsync(0, 500, CancellationToken.None);

        Assert.False(committed.WasExisting);
        Assert.True(existing.WasExisting);
        StreamEvent persisted = Assert.Single(page.Events, item => item.ResourceId == first.FederatedObject!.Id);
        Assert.Equal(StreamEventKind.PostCreated, persisted.Kind);
        Assert.True(persisted.Cursor > 0);
    }

    [Fact]
    public async Task CursorIsMonotonicAndExpiredCursorIsReportedAfterRetention()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository deliveries = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        IStreamEventStore events = scope.ServiceProvider.GetRequiredService<IStreamEventStore>();
        OutboundCommit first = CreateCommit("cursor-" + Guid.NewGuid().ToString("N"), new string('b', 64));
        OutboundCommit second = CreateCommit("cursor-" + Guid.NewGuid().ToString("N"), new string('c', 64));
        await deliveries.CommitOutboundAsync(first, CancellationToken.None);
        await deliveries.CommitOutboundAsync(second, CancellationToken.None);
        StreamEventPage beforeRetention = await events.ReadAfterAsync(0, 500, CancellationToken.None);
        StreamEvent firstEvent = Assert.Single(beforeRetention.Events, item => item.ResourceId == first.FederatedObject!.Id);
        StreamEvent secondEvent = Assert.Single(beforeRetention.Events, item => item.ResourceId == second.FederatedObject!.Id);
        Assert.True(secondEvent.Cursor > firstEvent.Cursor);

        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext db = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            await db.StreamEvents.Where(item => item.Cursor < secondEvent.Cursor)
                .ExecuteDeleteAsync(CancellationToken.None);
        }

        StreamEventPage expired = await events.ReadAfterAsync(secondEvent.Cursor - 2, 100, CancellationToken.None);
        Assert.True(expired.RequestedCursorExpired);
        Assert.Equal(secondEvent.Cursor, expired.OldestAvailableCursor);
        Assert.Empty(expired.Events);
    }

    [Fact]
    public async Task BoundedPumpDisconnectsSlowConsumerWithoutDroppingDurableEvents()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository deliveries = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        IStreamEventStore store = scope.ServiceProvider.GetRequiredService<IStreamEventStore>();
        IDurableStreamEventPump pump = scope.ServiceProvider.GetRequiredService<IDurableStreamEventPump>();
        long cursor = (await store.ReadAfterAsync(0, 1, CancellationToken.None)).LatestCursor ?? 0;
        OutboundCommit first = CreateCommit("slow-a-" + Guid.NewGuid().ToString("N"), new string('d', 64));
        OutboundCommit second = CreateCommit("slow-b-" + Guid.NewGuid().ToString("N"), new string('e', 64));
        await deliveries.CommitOutboundAsync(first, CancellationToken.None);
        await deliveries.CommitOutboundAsync(second, CancellationToken.None);

        await using IAsyncEnumerator<StreamEvent> enumerator = pump.SubscribeAsync(
            cursor,
            1,
            TimeSpan.FromMilliseconds(25),
            CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        await Assert.ThrowsAsync<StreamSlowConsumerException>(() => enumerator.MoveNextAsync().AsTask());

        StreamEventPage replay = await store.ReadAfterAsync(cursor, 100, CancellationToken.None);
        Assert.Contains(replay.Events, item => item.ResourceId == first.FederatedObject!.Id);
        Assert.Contains(replay.Events, item => item.ResourceId == second.FederatedObject!.Id);
    }

    [Fact]
    public async Task ConnectionLimitIsGlobalInPostgresAndExpiredLeaseIsRecovered()
    {
        await using AsyncServiceScope scopeA = fixture.Services.CreateAsyncScope();
        await using AsyncServiceScope scopeB = fixture.Services.CreateAsyncScope();
        IStreamConnectionLeaseStore storeA = scopeA.ServiceProvider.GetRequiredService<IStreamConnectionLeaseStore>();
        IStreamConnectionLeaseStore storeB = scopeB.ServiceProvider.GetRequiredService<IStreamConnectionLeaseStore>();
        string subject = "https://local.example/users/lease-" + Guid.NewGuid().ToString("N");
        string address = "192.0.2.42";
        Task<StreamConnectionLeaseToken?> acquireA = storeA.TryAcquireAsync(
            subject, address, "instance-a", 1, 10, Now, TimeSpan.FromMinutes(1), CancellationToken.None);
        Task<StreamConnectionLeaseToken?> acquireB = storeB.TryAcquireAsync(
            subject, address, "instance-b", 1, 10, Now, TimeSpan.FromMinutes(1), CancellationToken.None);
        StreamConnectionLeaseToken?[] acquired = await Task.WhenAll(acquireA, acquireB);
        StreamConnectionLeaseToken winner = Assert.Single(acquired, item => item is not null)!;
        Assert.Single(acquired, item => item is null);

        Assert.True(await storeA.ExtendAsync(winner, Now.AddSeconds(30), TimeSpan.FromMinutes(1), CancellationToken.None));
        StreamConnectionLeaseToken? recovered = await storeB.TryAcquireAsync(
            subject,
            address,
            "instance-c",
            1,
            10,
            Now.AddMinutes(2),
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.NotNull(recovered);
        await storeB.ReleaseAsync(recovered!, CancellationToken.None);
    }

    private static OutboundCommit CreateCommit(string idempotencyKey, string requestHash)
    {
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://local.example/users/{suffix}";
        string objectIri = $"https://local.example/objects/{suffix}";
        string activityIri = $"https://local.example/activities/{suffix}";
        byte[] objectBytes = Encoding.UTF8.GetBytes($"{{\"id\":\"{objectIri}\",\"type\":\"Note\"}}");
        byte[] activityBytes = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Create\"}}");
        FederatedObject item = FederatedObject.Create(
            objectIri,
            actorIri,
            "Note",
            Visibility.Public,
            Encoding.UTF8.GetString(objectBytes),
            PayloadDigest.Sha256Hex(objectBytes),
            Now,
            Now);
        ActivityRecord activity = ActivityRecord.Create(
            activityIri,
            actorIri,
            "Create",
            objectIri,
            ActivityDirection.Outbound,
            Visibility.Public,
            Encoding.UTF8.GetString(activityBytes),
            PayloadDigest.Sha256Hex(activityBytes),
            false,
            Now,
            Now);
        ClientIdempotencyRecord idempotency = ClientIdempotencyRecord.Create(
            actorIri,
            idempotencyKey,
            requestHash,
            activityIri,
            objectIri,
            activityBytes,
            Now,
            Now.AddDays(1));
        return new(activity, item, null, null, null, idempotency, [], []);
    }
}
