using ActivityPub.Application;
using ActivityPub.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace ActivityPub.Persistence.Tests;

[CollectionDefinition(Name)]
public sealed class RedisNotifierFixtureDefinition : ICollectionFixture<RedisNotifierFixture>
{
    public const string Name = "redis-notifier";
}

public sealed class RedisNotifierFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

[Collection(RedisNotifierFixtureDefinition.Name)]
public sealed class RedisStreamEventNotifierTests(RedisNotifierFixture fixture)
{
    [Fact]
    public async Task PublishWakesAWaitingSubscriberWithoutPostgres()
    {
        using var notifier = new RedisStreamEventNotifier(fixture.ConnectionString, "test-stream-events");
        Assert.True(notifier.IsEnabled);

        Task wait = notifier.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(250);
        await notifier.PublishAsync([42], CancellationToken.None);
        await wait.WaitAsync(TimeSpan.FromSeconds(5));

        using var second = new RedisStreamEventNotifier(fixture.ConnectionString, "test-stream-events");
        Task secondWait = second.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(250);
        await second.PublishAsync([43], CancellationToken.None);
        await secondWait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisabledNotifierDoesNotConnectAndTimesOut()
    {
        using var notifier = new RedisStreamEventNotifier(null, "unused-channel");
        Assert.False(notifier.IsEnabled);

        long started = Environment.TickCount64;
        await notifier.WaitAsync(TimeSpan.FromMilliseconds(120), CancellationToken.None);
        long elapsed = Environment.TickCount64 - started;
        Assert.True(elapsed >= 100, $"Expected the disabled notifier to delay, elapsed {elapsed}ms");
    }

    [Fact]
    public async Task PublishSurvivesConnectionFailureAsAnEnhancement()
    {
        using var notifier = new RedisStreamEventNotifier("127.0.0.1:1,abortConnect=false", "test-channel");
        await notifier.PublishAsync([7], CancellationToken.None);
    }

    [Fact]
    public void NotifierIsRegisteredWithConfigurationBinding()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivityPub"] = "Host=localhost;Database=none;Username=none;Password=none",
                ["Streaming:Redis:ConnectionString"] = fixture.ConnectionString
            })
            .Build();
        services.AddSingleton(configuration);
        services.AddActivityPubPersistence(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        IStreamEventNotifier notifier = provider.GetRequiredService<IStreamEventNotifier>();
        Assert.True(notifier.IsEnabled);
    }

    [Fact]
    public void NotifierFallsBackToPollingWhenRedisIsNotConfigured()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivityPub"] = "Host=localhost;Database=none;Username=none;Password=none"
            })
            .Build();
        services.AddSingleton(configuration);
        services.AddActivityPubPersistence(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        IStreamEventNotifier notifier = provider.GetRequiredService<IStreamEventNotifier>();
        Assert.False(notifier.IsEnabled);
    }
}
