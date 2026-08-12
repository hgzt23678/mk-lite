using ActivityPub.Application;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Moderation;

public static class ModerationServiceCollectionExtensions
{
    public static IServiceCollection AddActivityPubModeration(
        this IServiceCollection services,
        SpamEvaluationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<IInboundSpamEvaluator, HeuristicSpamEvaluator>();
        return services;
    }
}
