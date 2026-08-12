using ActivityPub.Application;
using ActivityPub.Workers.Delivery;
using ActivityPub.Workers.Inbox;
using ActivityPub.Workers.Retention;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Workers;

public static class WorkersServiceCollectionExtensions
{
    public static IServiceCollection AddActivityPubWorkers(this IServiceCollection services, WorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddScoped<IInboxItemProcessor, InboxItemProcessor>();
        services.AddSingleton<DeliveryPolicy>();
        services.AddHostedService<InboxWorker>();
        services.AddHostedService<DeliveryWorker>();
        services.AddHostedService<RawJsonRetentionWorker>();
        return services;
    }
}
