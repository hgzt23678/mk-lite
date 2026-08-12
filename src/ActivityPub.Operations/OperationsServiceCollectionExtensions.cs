using ActivityPub.Application;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ActivityPub.Operations;

public static class OperationsServiceCollectionExtensions
{
    public static IServiceCollection AddActivityPubOperations(
        this IServiceCollection services,
        WorkerOptions workerOptions,
        string serviceVersion)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(workerOptions);
        services.AddSingleton<IFederationInstrumentation, FederationInstrumentation>();
        services.AddSingleton(new ServiceReleaseVersion(serviceVersion));
        services.AddHostedService<QueueMetricsReporter>();
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("activitypub-server", serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .AddSource(FederationTelemetry.SourceName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(FederationTelemetry.SourceName)
                .AddMeter("Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        services.AddSingleton(workerOptions);
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready", "startup"])
            .AddCheck<WorkerReadinessHealthCheck>("workers", tags: ["ready"]);
        return services;
    }

    public static WebApplication MapActivityPubHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") });
        app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("startup") });
        return app;
    }
}
