using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace ActivityPub.Persistence.Tests;

[Collection(PostgreSqlFixtureDefinition.Name)]
public sealed class RedisStreamWakeupIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CommittedStreamEventWakesThePumpImmediatelyThroughRedis()
    {
        await using var redis = new RedisBuilder("redis:7-alpine").Build();
        await redis.StartAsync();
        string marker = "redis-wake-" + Guid.NewGuid().ToString("N");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivityPub"] = fixture.ConnectionString,
                ["Streaming:Redis:ConnectionString"] = redis.GetConnectionString(),
                ["Streaming:Redis:Channel"] = "activitypub:stream-events"
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(configuration)
            .AddSingleton<TestClock>()
            .AddSingleton<IClock>(provider => provider.GetRequiredService<TestClock>())
            .AddSingleton<IExternalKeyProvisioner, TestExternalKeyProvisioner>()
            .AddActivityPubPersistence(configuration, localAccountRegistrationEnabled: false);
        await using ServiceProvider provider = services.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IDeliveryRepository deliveries = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        IDurableStreamEventPump pump = scope.ServiceProvider.GetRequiredService<IDurableStreamEventPump>();
        IStreamEventNotifier notifier = scope.ServiceProvider.GetRequiredService<IStreamEventNotifier>();
        Assert.True(notifier.IsEnabled, "The Redis notifier must be enabled for this test.");

        string actorIri = "https://local.example/users/alice";
        string objectIri = "https://local.example/objects/" + Guid.NewGuid().ToString("N");
        string activityIri = "https://local.example/activities/" + Guid.NewGuid().ToString("N");
        var noteNode = new JsonObject
        {
            ["type"] = "Note",
            ["id"] = objectIri,
            ["attributedTo"] = actorIri,
            ["content"] = marker
        };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(noteNode);
        var activity = ActivityRecord.Create(
            activityIri,
            actorIri,
            "Create",
            objectIri,
            ActivityDirection.Outbound,
            Visibility.Public,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            isTransient: false,
            Now,
            Now);
        var federated = FederatedObject.Create(
            objectIri,
            actorIri,
            "Note",
            Visibility.Public,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            Now,
            Now);
        var commit = new OutboundCommit(
            activity,
            federated,
            null,
            null,
            null,
            null,
            [],
            [ActivityPub.Domain.Delivery.Create(
                activity.Id,
                activityIri,
                "https://remote.example/inbox",
                actorIri,
                payload,
                SignatureProfile.LegacyCavage,
                Now)]);
        OutboundCommitResult result = await deliveries.CommitOutboundAsync(commit, CancellationToken.None);
        Assert.False(result.WasExisting);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (StreamEvent streamEvent in pump.SubscribeAsync(
                           afterCursor: 0,
                           bufferCapacity: 64,
                           pollInterval: TimeSpan.FromSeconds(30),
                           cts.Token))
        {
            if (string.Equals(streamEvent.ResourceIri, objectIri, StringComparison.Ordinal))
            {
                Assert.Equal(StreamEventKind.PostCreated, streamEvent.Kind);
                return;
            }
        }

        Assert.Fail("The pump did not deliver the committed stream event within the timeout.");
    }
}
