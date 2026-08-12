using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Media;

internal sealed class MediaGarbageCollectionWorker(
    IServiceScopeFactory scopeFactory,
    IMediaObjectStore objectStore,
    MediaOptions options,
    IClock clock,
    ILogger<MediaGarbageCollectionWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogCollectionFailure = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(7001, nameof(LogCollectionFailure)),
        "Media garbage collection cycle failed.");

    private static readonly Action<ILogger, Guid, Exception?> LogObjectDeletionFailure = LoggerMessage.Define<Guid>(
        LogLevel.Warning,
        new EventId(7002, nameof(LogObjectDeletionFailure)),
        "Media object deletion failed for {MediaId}; it will be retried.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogCollectionFailure(logger, exception);
            }

            await Task.Delay(options.GarbageCollectionInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        using IServiceScope scope = scopeFactory.CreateScope();
        IRemoteMediaCacheRepository remoteCache = scope.ServiceProvider.GetRequiredService<IRemoteMediaCacheRepository>();
        _ = await remoteCache.ExpireAsync(now, options.GarbageCollectionBatchSize, cancellationToken).ConfigureAwait(false);
        IRemoteActorMediaCacheRepository remoteActorCache = scope.ServiceProvider.GetRequiredService<IRemoteActorMediaCacheRepository>();
        _ = await remoteActorCache.ExpireAsync(now, options.GarbageCollectionBatchSize, cancellationToken).ConfigureAwait(false);
        IMediaRepository repository = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        IReadOnlyList<MediaGarbageCandidate> candidates = await repository.ClaimGarbageAsync(
            now - options.UnreferencedRetention,
            now - options.GarbageRetryDelay,
            now,
            options.GarbageCollectionBatchSize,
            cancellationToken).ConfigureAwait(false);

        foreach (MediaGarbageCandidate candidate in candidates)
        {
            try
            {
                await objectStore.DeleteAsync(candidate.StorageKey, cancellationToken).ConfigureAwait(false);
                if (candidate.ThumbnailStorageKey is not null)
                {
                    await objectStore.DeleteAsync(candidate.ThumbnailStorageKey, cancellationToken).ConfigureAwait(false);
                }

                await repository.MarkPurgedAsync(candidate.Id, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogObjectDeletionFailure(logger, candidate.Id, exception);
            }
        }
    }
}
