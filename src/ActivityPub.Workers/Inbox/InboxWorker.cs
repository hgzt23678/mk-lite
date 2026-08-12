using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Workers.Inbox;

public sealed class InboxWorker(
    IServiceScopeFactory scopeFactory,
    WorkerOptions options,
    ILogger<InboxWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> PollFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(2_001, nameof(PollFailed)),
        "Inbox worker poll failed; leased work remains recoverable");

    private static readonly Action<ILogger, Guid, Exception?> ItemFailed = LoggerMessage.Define<Guid>(
        LogLevel.Error,
        new EventId(2_002, nameof(ItemFailed)),
        "Inbox item {InboxItemId} failed with a transient infrastructure error");

    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:inbox:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.InboxEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecordHeartbeatAsync(stoppingToken).ConfigureAwait(false);
                IReadOnlyList<InboxItem> items = await ClaimAsync(stoppingToken).ConfigureAwait(false);
                foreach (InboxItem item in items)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    UpdateLeases(1);
                    try
                    {
                        await ProcessAsync(item, stoppingToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        UpdateLeases(-1);
                    }
                }

                if (items.Count == 0)
                {
                    await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false);
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

    private async Task<IReadOnlyList<InboxItem>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IInboxRepository repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        return await repository.ClaimAsync(
            _workerId,
            options.BatchSize,
            options.LeaseDuration,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessAsync(InboxItem item, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IInboxItemProcessor processor = scope.ServiceProvider.GetRequiredService<IInboxItemProcessor>();
            await processor.ProcessAsync(item, _workerId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ItemFailed(logger, item.Id, exception);
            await RecordFailureAsync(item, exception, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordFailureAsync(
        InboxItem item,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string errorCode = exception.GetType().Name.Length <= 128
            ? exception.GetType().Name
            : "inbox_infrastructure_failure";
        const string safeMessage = "Inbox processing failed because an infrastructure dependency was unavailable.";
        DeadLetter? deadLetter = null;
        if (item.AttemptCount < options.MaximumInboxAttempts)
        {
            DateTimeOffset retryAt = services.GetRequiredService<DeliveryPolicy>()
                .NextRetryAt(item.AttemptCount, now, Random.Shared);
            item.ScheduleRetry(_workerId, now, retryAt, errorCode, safeMessage);
        }
        else
        {
            item.DeadLetter(_workerId, now, "attempts_exhausted", safeMessage);
            deadLetter = DeadLetter.Create("inbox", item.Id, "attempts_exhausted", safeMessage, now);
        }

        await services.GetRequiredService<IInboxRepository>()
            .SaveFailureAsync(item, deadLetter, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RecordHeartbeatAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IWorkerHeartbeatStore store = scope.ServiceProvider.GetRequiredService<IWorkerHeartbeatStore>();
        await store.RecordAsync(_workerId, "inbox", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    private void UpdateLeases(int delta)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IFederationInstrumentation>().LeaseDelta("inbox", delta);
    }
}
