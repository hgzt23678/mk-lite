using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Workers.Delivery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Persistence.Tests;

public sealed class DeliveryWorkerLeaseHeartbeatTests
{
    [Fact]
    public async Task LongRunningTransportRenewsDeliveryAndDomainLeasesWithoutReclaim()
    {
        var options = new WorkerOptions
        {
            InboxEnabled = false,
            DeliveryEnabled = true,
            BatchSize = 1,
            PollInterval = TimeSpan.FromMilliseconds(10),
            LeaseDuration = TimeSpan.FromSeconds(2),
            HeartbeatInterval = TimeSpan.FromMilliseconds(200)
        };
        var repository = new RecordingDeliveryRepository();
        var domainStore = new RecordingDomainExecutionStore();
        var transport = new SlowSuccessfulTransport(TimeSpan.FromSeconds(5));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton<IDeliveryRepository>(repository);
        services.AddSingleton<IRemoteDomainExecutionStore>(domainStore);
        services.AddSingleton<IPrivateKeyStore, FixturePrivateKeyStore>();
        services.AddSingleton<IOutboundTransport>(transport);
        services.AddSingleton<IWorkerHeartbeatStore, FixtureWorkerHeartbeatStore>();
        services.AddSingleton<IFederationQueueSignal, PollingQueueSignal>();
        services.AddSingleton<IFederationInstrumentation>(NullFederationInstrumentation.Instance);
        services.AddSingleton<DeliveryPolicy>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        var worker = new DeliveryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            provider.GetRequiredService<IFederationQueueSignal>(),
            provider.GetRequiredService<ILogger<DeliveryWorker>>());

        await worker.StartAsync(CancellationToken.None);
        await repository.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(WorkItemState.Succeeded, repository.Delivery.State);
        Assert.Equal(1, repository.Delivery.AttemptCount);
        Assert.True(repository.ExtensionCount >= 3, $"Expected at least three delivery lease heartbeats, observed {repository.ExtensionCount}.");
        Assert.True(domainStore.ExtensionCount >= 3, $"Expected at least three domain lease heartbeats, observed {domainStore.ExtensionCount}.");
    }

    [Fact]
    public async Task ClaimedBatchStartsAllTransportsBeforeAnEarlierTransportCompletes()
    {
        var options = new WorkerOptions
        {
            InboxEnabled = false,
            DeliveryEnabled = true,
            BatchSize = 2,
            PollInterval = TimeSpan.FromMilliseconds(10),
            LeaseDuration = TimeSpan.FromSeconds(2),
            HeartbeatInterval = TimeSpan.FromMilliseconds(200),
            MaximumConcurrentDeliveriesPerDomain = 2
        };
        var repository = new RecordingDeliveryRepository(2);
        var domainStore = new RecordingDomainExecutionStore();
        var transport = new SlowSuccessfulTransport(TimeSpan.FromSeconds(3));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton<IDeliveryRepository>(repository);
        services.AddSingleton<IRemoteDomainExecutionStore>(domainStore);
        services.AddSingleton<IPrivateKeyStore, FixturePrivateKeyStore>();
        services.AddSingleton<IOutboundTransport>(transport);
        services.AddSingleton<IWorkerHeartbeatStore, FixtureWorkerHeartbeatStore>();
        services.AddSingleton<IFederationQueueSignal, PollingQueueSignal>();
        services.AddSingleton<IFederationInstrumentation>(NullFederationInstrumentation.Instance);
        services.AddSingleton<DeliveryPolicy>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        var worker = new DeliveryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            provider.GetRequiredService<IFederationQueueSignal>(),
            provider.GetRequiredService<ILogger<DeliveryWorker>>());

        await worker.StartAsync(CancellationToken.None);
        await transport.TwoConcurrentCalls.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await repository.Completed.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, transport.MaximumConcurrency);
        Assert.All(repository.Deliveries, delivery =>
        {
            Assert.Equal(WorkItemState.Succeeded, delivery.State);
            Assert.Equal(1, delivery.AttemptCount);
        });
    }

    private sealed class RecordingDeliveryRepository : IDeliveryRepository
    {
        private int claimed;

        public RecordingDeliveryRepository(int deliveryCount = 1)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Deliveries = Enumerable.Range(0, deliveryCount)
                .Select(index => ActivityPub.Domain.Delivery.Create(
                    Guid.NewGuid(),
                    $"https://local.example/activities/heartbeat-{index}",
                    "https://remote.example/inbox",
                    "https://local.example/users/alice",
                    "{}"u8.ToArray(),
                    SignatureProfile.LegacyCavage,
                    now))
                .ToArray();
        }

        public ActivityPub.Domain.Delivery[] Deliveries { get; }
        public ActivityPub.Domain.Delivery Delivery => Deliveries[0];
        public int ExtensionCount => extensionCount;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int extensionCount;
        private int completedCount;

        public Task<IReadOnlyList<ActivityPub.Domain.Delivery>> ClaimAsync(
            string workerId,
            int count,
            TimeSpan leaseDuration,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref claimed, 1) != 0)
            {
                return Task.FromResult<IReadOnlyList<ActivityPub.Domain.Delivery>>([]);
            }

            foreach (ActivityPub.Domain.Delivery delivery in Deliveries)
            {
                delivery.AcquireLease(workerId, now, leaseDuration);
            }

            return Task.FromResult<IReadOnlyList<ActivityPub.Domain.Delivery>>(Deliveries);
        }

        public Task ExtendLeaseAsync(
            Guid deliveryId,
            string workerId,
            TimeSpan leaseDuration,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ActivityPub.Domain.Delivery delivery = Deliveries.Single(item => item.Id == deliveryId);
            delivery.ExtendLease(workerId, now, leaseDuration);
            Interlocked.Increment(ref extensionCount);
            return Task.CompletedTask;
        }

        public Task SaveAttemptAsync(
            ActivityPub.Domain.Delivery delivery,
            DeliveryAttempt attempt,
            DeadLetter? deadLetter,
            CancellationToken cancellationToken,
            EndpointRediscoveryPlan? endpointRediscovery = null)
        {
            Assert.Contains(delivery, Deliveries);
            Assert.Equal(DeliveryAttemptOutcome.Succeeded, attempt.Outcome);
            if (Interlocked.Increment(ref completedCount) == Deliveries.Length)
            {
                Completed.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public Task<OutboundCommitResult> CommitOutboundAsync(OutboundCommit commit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task CommitRelayDeliveriesAsync(IReadOnlyList<ActivityPub.Domain.Delivery> deliveries, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ClientIdempotencyRecord?> FindClientIdempotencyAsync(string subject, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task ReleaseWithoutAttemptAsync(ActivityPub.Domain.Delivery delivery, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindRecipientActorsAsync(Guid deliveryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> RequeueDeadLetterAsync(Guid deadLetterId, string operatorId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task CancelPendingForDomainAsync(string domain, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<long> CountPendingAsync(CancellationToken cancellationToken) => Task.FromResult(0L);
        public Task<TimeSpan?> GetOldestPendingAgeAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<TimeSpan?>(null);
    }

    private sealed class PollingQueueSignal : IFederationQueueSignal
    {
        public bool IsEnabled => false;

        public Task NotifyDeliveryAvailableAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForDeliveryAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.Delay(timeout, cancellationToken);

        public Task NotifyInboxAvailableAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForInboxAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.Delay(timeout, cancellationToken);
    }

    private sealed class RecordingDomainExecutionStore : IRemoteDomainExecutionStore
    {
        public int ExtensionCount { get; private set; }

        public Task<DomainLeaseToken?> TryAcquireAsync(
            string domain,
            string owner,
            Guid deliveryId,
            int maximumSlots,
            DateTimeOffset now,
            TimeSpan duration,
            CancellationToken cancellationToken) =>
            Task.FromResult<DomainLeaseToken?>(new(domain, 0, owner, deliveryId, now.Add(duration)));

        public Task ExtendAsync(
            DomainLeaseToken token,
            DateTimeOffset now,
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            ExtensionCount++;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(DomainLeaseToken token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<DateTimeOffset?> GetCircuitOpenUntilAsync(string domain, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);
        public Task RecordCircuitSuccessAsync(string domain, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordCircuitFailureAsync(
            string domain,
            DateTimeOffset now,
            int threshold,
            TimeSpan breakDuration,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SlowSuccessfulTransport(TimeSpan duration) : IOutboundTransport
    {
        private int activeCalls;
        private int maximumConcurrency;

        public int MaximumConcurrency => maximumConcurrency;
        public TaskCompletionSource TwoConcurrentCalls { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DeliveryTransportResult> DeliverAsync(
            ActivityPub.Domain.Delivery delivery,
            KeyMaterial key,
            CancellationToken cancellationToken)
        {
            int concurrency = Interlocked.Increment(ref activeCalls);
            int observed;
            while (concurrency > (observed = Volatile.Read(ref maximumConcurrency)))
            {
                _ = Interlocked.CompareExchange(ref maximumConcurrency, concurrency, observed);
            }

            if (concurrency >= 2)
            {
                TwoConcurrentCalls.TrySetResult();
            }

            try
            {
                await Task.Delay(duration, cancellationToken);
                return new(202, duration, null, null, null);
            }
            finally
            {
                _ = Interlocked.Decrement(ref activeCalls);
            }
        }
    }

    private sealed class FixturePrivateKeyStore : IPrivateKeyStore
    {
        public Task<KeyMaterial> GetSigningKeyAsync(string actorIri, CancellationToken cancellationToken) =>
            Task.FromResult(new KeyMaterial(
                actorIri + "#main-key",
                actorIri,
                "fixture-public-key",
                "fixture-private-key-handle",
                "rsa-sha256"));
    }

    private sealed class FixtureWorkerHeartbeatStore : IWorkerHeartbeatStore
    {
        public Task RecordAsync(string workerId, string workerType, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> HasRecentHeartbeatAsync(
            string workerType,
            DateTimeOffset threshold,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
