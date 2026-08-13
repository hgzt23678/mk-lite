using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeliveryEntity = ActivityPub.Domain.Delivery;

namespace ActivityPub.Workers.Delivery;

public sealed class DeliveryWorker(
    IServiceScopeFactory scopeFactory,
    WorkerOptions options,
    IFederationQueueSignal queueSignal,
    ILogger<DeliveryWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> PollFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(3_001, nameof(PollFailed)),
        "Delivery worker poll failed; leased deliveries remain recoverable");

    private static readonly Action<ILogger, Guid, string, Exception?> DeliveryFailed = LoggerMessage.Define<Guid, string>(
        LogLevel.Error,
        new EventId(3_002, nameof(DeliveryFailed)),
        "Delivery {DeliveryId} to domain {RemoteDomain} failed before an attempt result was committed");

    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:delivery:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.DeliveryEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecordHeartbeatAsync(stoppingToken).ConfigureAwait(false);
                IReadOnlyList<DeliveryEntity> deliveries = await ClaimAsync(stoppingToken).ConfigureAwait(false);
                if (deliveries.Count > 0)
                {
                    await Task.WhenAll(deliveries.Select(delivery => ProcessClaimedAsync(delivery, stoppingToken)))
                        .ConfigureAwait(false);
                }
                else
                {
                    await queueSignal.WaitForDeliveryAsync(options.PollInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                PollFailed(logger, exception);
                await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessClaimedAsync(DeliveryEntity delivery, CancellationToken cancellationToken)
    {
        UpdateLeases(1);
        try
        {
            await ProcessAsync(delivery, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            UpdateLeases(-1);
        }
    }

    private async Task<IReadOnlyList<DeliveryEntity>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        return await repository.ClaimAsync(
            _workerId,
            options.BatchSize,
            options.LeaseDuration,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessAsync(DeliveryEntity delivery, CancellationToken cancellationToken)
    {
        DomainLeaseToken? domainLease = null;
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IServiceProvider services = scope.ServiceProvider;
            IDeliveryRepository repository = services.GetRequiredService<IDeliveryRepository>();
            IRemoteDomainExecutionStore domainStore = services.GetRequiredService<IRemoteDomainExecutionStore>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset? circuitOpenUntil = await domainStore
                .GetCircuitOpenUntilAsync(delivery.RemoteDomain, now, cancellationToken)
                .ConfigureAwait(false);
            if (circuitOpenUntil is not null)
            {
                delivery.ReleaseLeaseWithoutAttempt(_workerId, now, circuitOpenUntil.Value);
                await repository.ReleaseWithoutAttemptAsync(delivery, cancellationToken).ConfigureAwait(false);
                return;
            }

            domainLease = await domainStore.TryAcquireAsync(
                delivery.RemoteDomain,
                _workerId,
                delivery.Id,
                options.MaximumConcurrentDeliveriesPerDomain,
                now,
                options.LeaseDuration,
                cancellationToken).ConfigureAwait(false);
            if (domainLease is null)
            {
                delivery.ReleaseLeaseWithoutAttempt(_workerId, now, now.Add(options.PollInterval + TimeSpan.FromMilliseconds(100)));
                await repository.ReleaseWithoutAttemptAsync(delivery, cancellationToken).ConfigureAwait(false);
                return;
            }

            await repository.ExtendLeaseAsync(delivery.Id, _workerId, options.LeaseDuration, now, cancellationToken).ConfigureAwait(false);
            await domainStore.ExtendAsync(domainLease, now, options.LeaseDuration, cancellationToken).ConfigureAwait(false);
            IPrivateKeyStore keyStore = services.GetRequiredService<IPrivateKeyStore>();
            IOutboundTransport transport = services.GetRequiredService<IOutboundTransport>();
            DeliveryPolicy policy = services.GetRequiredService<DeliveryPolicy>();
            KeyMaterial key = await keyStore.GetSigningKeyAsync(delivery.ActorIri, cancellationToken).ConfigureAwait(false);
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            using var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task leaseHeartbeat = MaintainLeaseAsync(
                delivery.Id,
                domainLease,
                deliveryCancellation,
                cancellationToken);
            DeliveryTransportResult transportResult;
            try
            {
                transportResult = await transport
                    .DeliverAsync(delivery, key, deliveryCancellation.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                await deliveryCancellation.CancelAsync().ConfigureAwait(false);
                await leaseHeartbeat.ConfigureAwait(false);
            }

            DateTimeOffset completedAt = DateTimeOffset.UtcNow;
            DeliveryDisposition disposition = policy.Classify(transportResult, delivery.AttemptCount, completedAt, Random.Shared);
            delivery.RecordStatusCode(transportResult.StatusCode);
            if (disposition.Classification == DeliveryFailureClass.EndpointGone && delivery.EndpointRediscoveryAt is null)
            {
                await services.GetRequiredService<IRemoteActorDirectory>()
                    .MarkEndpointGoneAsync(delivery.EndpointIri, completedAt, cancellationToken)
                    .ConfigureAwait(false);
            }

            EndpointRediscoveryPlan? rediscovery = null;
            if ((disposition.Classification is DeliveryFailureClass.EndpointGone or DeliveryFailureClass.AuthenticationRecheck) &&
                delivery.EndpointRediscoveryAt is null)
            {
                rediscovery = await BuildEndpointRediscoveryPlanAsync(
                    services,
                    repository,
                    delivery,
                    completedAt,
                    cancellationToken).ConfigureAwait(false);
            }

            DeliveryAttemptOutcome outcome;
            DeadLetter? deadLetter;
            if (rediscovery is not null)
            {
                if (disposition.Classification == DeliveryFailureClass.AuthenticationRecheck)
                {
                    delivery.SelectSignatureProfile(delivery.SignatureProfile == SignatureProfile.LegacyCavage
                        ? SignatureProfile.Rfc9421
                        : SignatureProfile.LegacyCavage);
                }

                DateTimeOffset retryAt = disposition.Classification == DeliveryFailureClass.EndpointGone
                    ? completedAt.AddSeconds(5)
                    : completedAt.AddMinutes(1);
                delivery.ScheduleRetry(_workerId, completedAt, retryAt, disposition.Code, disposition.Message);
                outcome = DeliveryAttemptOutcome.RetryScheduled;
                deadLetter = null;
            }
            else
            {
                (outcome, deadLetter) = ApplyDisposition(delivery, disposition, completedAt);
            }

            var attempt = DeliveryAttempt.Create(
                delivery.Id,
                delivery.AttemptCount,
                outcome,
                transportResult.StatusCode,
                disposition.Code,
                disposition.Message,
                completedAt - startedAt,
                startedAt,
                completedAt);
            await repository.SaveAttemptAsync(
                delivery,
                attempt,
                deadLetter,
                cancellationToken,
                rediscovery).ConfigureAwait(false);
            services.GetRequiredService<IFederationInstrumentation>().DeliveryCompleted(
                delivery.RemoteDomain,
                transportResult.StatusCode,
                outcome);

            if (disposition.Classification == DeliveryFailureClass.Retryable)
            {
                await domainStore.RecordCircuitFailureAsync(
                    delivery.RemoteDomain,
                    completedAt,
                    options.DomainCircuitFailureThreshold,
                    options.DomainCircuitBreakDuration,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await domainStore.RecordCircuitSuccessAsync(delivery.RemoteDomain, completedAt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DeliveryFailed(logger, delivery.Id, delivery.RemoteDomain, exception);
        }
        finally
        {
            if (domainLease is not null)
            {
                try
                {
                    await using AsyncServiceScope releaseScope = scopeFactory.CreateAsyncScope();
                    IRemoteDomainExecutionStore domainStore = releaseScope.ServiceProvider.GetRequiredService<IRemoteDomainExecutionStore>();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await domainStore.ReleaseAsync(domainLease, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    DeliveryFailed(logger, delivery.Id, delivery.RemoteDomain, exception);
                }
            }
        }
    }

    private async Task MaintainLeaseAsync(
        Guid deliveryId,
        DomainLeaseToken domainLease,
        CancellationTokenSource deliveryCancellation,
        CancellationToken stoppingToken)
    {
        try
        {
            while (!deliveryCancellation.IsCancellationRequested)
            {
                await Task.Delay(options.HeartbeatInterval, deliveryCancellation.Token).ConfigureAwait(false);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                await using AsyncServiceScope heartbeatScope = scopeFactory.CreateAsyncScope();
                IDeliveryRepository repository = heartbeatScope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
                IRemoteDomainExecutionStore domainStore = heartbeatScope.ServiceProvider
                    .GetRequiredService<IRemoteDomainExecutionStore>();
                await repository
                    .ExtendLeaseAsync(deliveryId, _workerId, options.LeaseDuration, now, deliveryCancellation.Token)
                    .ConfigureAwait(false);
                await domainStore
                    .ExtendAsync(domainLease, now, options.LeaseDuration, deliveryCancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deliveryCancellation.IsCancellationRequested || stoppingToken.IsCancellationRequested)
        {
        }
        catch
        {
            await deliveryCancellation.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<EndpointRediscoveryPlan?> BuildEndpointRediscoveryPlanAsync(
        IServiceProvider services,
        IDeliveryRepository repository,
        DeliveryEntity delivery,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> recipientActors = await repository
            .FindRecipientActorsAsync(delivery.Id, cancellationToken)
            .ConfigureAwait(false);
        if (recipientActors.Count == 0)
        {
            return null;
        }

        IRemoteRecipientResolver resolver = services.GetRequiredService<IRemoteRecipientResolver>();
        var endpoints = new List<RemoteActorEndpoint>(recipientActors.Count);
        foreach (string recipientActor in recipientActors)
        {
            endpoints.Add(await resolver.RediscoverAsync(recipientActor, cancellationToken).ConfigureAwait(false));
        }

        IGrouping<string, RemoteActorEndpoint>[] groups = endpoints
            .GroupBy(endpoint => endpoint.SharedInboxIri ?? endpoint.InboxIri, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        if (groups.Length == 0)
        {
            return null;
        }

        string previousEndpoint = delivery.EndpointIri;
        IGrouping<string, RemoteActorEndpoint> replacementGroup = groups[0];
        delivery.ReplaceEndpoint(_workerId, replacementGroup.Key, now);
        DeliveryTarget[] replacementTargets = replacementGroup
            .Select(endpoint => DeliveryTarget.Create(delivery.Id, endpoint.ActorIri))
            .ToArray();
        var additionalDeliveries = new List<DeliveryEntity>();
        var additionalTargets = new List<DeliveryTarget>();
        var changes = new List<DeliveryEndpointChange>
        {
            DeliveryEndpointChange.Create(
                delivery.Id,
                previousEndpoint,
                replacementGroup.Key,
                replacementTargets.Length,
                now)
        };

        foreach (IGrouping<string, RemoteActorEndpoint> group in groups.Skip(1))
        {
            DeliveryEntity fork = delivery.ForkForEndpoint(group.Key, now);
            DeliveryTarget[] forkTargets = group
                .Select(endpoint => DeliveryTarget.Create(fork.Id, endpoint.ActorIri))
                .ToArray();
            additionalDeliveries.Add(fork);
            additionalTargets.AddRange(forkTargets);
            changes.Add(DeliveryEndpointChange.Create(
                fork.Id,
                previousEndpoint,
                group.Key,
                forkTargets.Length,
                now));
        }

        return new(replacementTargets, additionalDeliveries, additionalTargets, changes);
    }

    private (DeliveryAttemptOutcome Outcome, DeadLetter? DeadLetter) ApplyDisposition(
        DeliveryEntity delivery,
        DeliveryDisposition disposition,
        DateTimeOffset now)
    {
        switch (disposition.Classification)
        {
            case DeliveryFailureClass.Success:
                delivery.Succeed(_workerId, now);
                return (DeliveryAttemptOutcome.Succeeded, null);
            case DeliveryFailureClass.Retryable:
                delivery.ScheduleRetry(_workerId, now, disposition.RetryAt!.Value, disposition.Code, disposition.Message);
                return (DeliveryAttemptOutcome.RetryScheduled, null);
            case DeliveryFailureClass.AuthenticationRecheck:
                if (delivery.EndpointRediscoveryAt is null)
                {
                    delivery.MarkEndpointRediscovered(now);
                    delivery.SelectSignatureProfile(delivery.SignatureProfile == SignatureProfile.LegacyCavage
                        ? SignatureProfile.Rfc9421
                        : SignatureProfile.LegacyCavage);
                    delivery.ScheduleRetry(_workerId, now, now.AddMinutes(1), disposition.Code, disposition.Message);
                    return (DeliveryAttemptOutcome.RetryScheduled, null);
                }

                break;
            case DeliveryFailureClass.EndpointGone:
                if (delivery.EndpointRediscoveryAt is null)
                {
                    delivery.MarkEndpointRediscovered(now);
                    delivery.ScheduleRetry(_workerId, now, now.AddHours(1), disposition.Code, disposition.Message);
                    return (DeliveryAttemptOutcome.RetryScheduled, null);
                }

                break;
            case DeliveryFailureClass.Permanent:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        delivery.DeadLetter(_workerId, now, disposition.Code, disposition.Message);
        return (
            DeliveryAttemptOutcome.TerminalFailure,
            DeadLetter.Create("delivery", delivery.Id, disposition.Code, disposition.Message, now));
    }

    private async Task RecordHeartbeatAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IWorkerHeartbeatStore store = scope.ServiceProvider.GetRequiredService<IWorkerHeartbeatStore>();
        await store.RecordAsync(_workerId, "delivery", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    private void UpdateLeases(int delta)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IFederationInstrumentation>().LeaseDelta("delivery", delta);
    }
}
