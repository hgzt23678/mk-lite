using ActivityPub.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Workers.Retention;

internal sealed class RawJsonRetentionWorker(
    IServiceScopeFactory scopeFactory,
    WorkerOptions options,
    ILogger<RawJsonRetentionWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, int, int, Exception?> Purged = LoggerMessage.Define<int, int, int>(
        LogLevel.Information,
        new EventId(5_001, nameof(Purged)),
        "Purged retained raw JSON: activities={ActivityCount}, objects={ObjectCount}, revisions={RevisionCount}");

    private static readonly Action<ILogger, Exception?> Failed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(5_002, nameof(Failed)),
        "Raw JSON retention pass failed; no uncommitted purge is retained");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.RawJsonRetentionEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IRawJsonRetentionStore store = scope.ServiceProvider.GetRequiredService<IRawJsonRetentionStore>();
                RawJsonPurgeResult result = await store.PurgeBatchAsync(
                    now.Subtract(options.ActivityRawJsonRetention),
                    now.Subtract(options.ObjectRawJsonRetention),
                    now,
                    options.RawJsonPurgeBatchSize,
                    stoppingToken).ConfigureAwait(false);
                if (result.Activities + result.Objects + result.ObjectRevisions > 0)
                {
                    Purged(logger, result.Activities, result.Objects, result.ObjectRevisions, null);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Failed(logger, exception);
            }

            await Task.Delay(options.RawJsonPurgeInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
