using ActivityPub.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Operations;

internal sealed class QueueMetricsReporter(
    IServiceScopeFactory scopeFactory,
    ILogger<QueueMetricsReporter> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> CollectionFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(4_001, nameof(CollectionFailed)),
        "Delivery queue metric collection failed");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
                long count = await repository.CountPendingAsync(stoppingToken).ConfigureAwait(false);
                TimeSpan? oldest = await repository.GetOldestPendingAgeAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
                FederationTelemetry.SetDeliveryQueue(count, oldest);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                CollectionFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
