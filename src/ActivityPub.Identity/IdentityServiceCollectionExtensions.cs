using ActivityPub.Application;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace ActivityPub.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddExternalKeySigning(
        this IServiceCollection services,
        VaultTransitOptions options,
        bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(isProduction);
        services.AddSingleton(options);
        services.AddHttpClient<IKeySigner, VaultTransitKeySigner>(client =>
        {
            client.BaseAddress = options.Address;
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler(resilience =>
        {
            resilience.Retry.MaxRetryAttempts = 2;
            resilience.Retry.BackoffType = DelayBackoffType.Exponential;
            resilience.Retry.UseJitter = true;
            resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
        });
        services.AddHttpClient<IExternalKeyProvisioner, VaultTransitKeyProvisioner>(client =>
        {
            client.BaseAddress = options.Address;
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler(resilience =>
        {
            resilience.Retry.MaxRetryAttempts = 2;
            resilience.Retry.BackoffType = DelayBackoffType.Exponential;
            resilience.Retry.UseJitter = true;
            resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
        });
        return services;
    }
}
