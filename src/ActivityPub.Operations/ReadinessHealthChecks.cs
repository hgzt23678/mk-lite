using ActivityPub.Application;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActivityPub.Operations;

internal sealed class DatabaseReadinessHealthCheck(
    IDbContextFactory<FederationDbContext> contextFactory,
    IDbContextFactory<LocalIdentityDbContext> identityContextFactory,
    ISchemaCompatibilityStore compatibilityStore,
    ServiceReleaseVersion releaseVersion) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            if (!await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.");
            }

            IEnumerable<string> pending = await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            if (pending.Any())
            {
                return HealthCheckResult.Unhealthy("Database migrations are pending; run the migration command before deployment.");
            }

            await using LocalIdentityDbContext identity = await identityContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            IEnumerable<string> pendingIdentity = await identity.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (pendingIdentity.Any())
            {
                return HealthCheckResult.Unhealthy("Local Identity database migrations are pending; run the migration command before deployment.");
            }

            SchemaCompatibilityResult compatibility = await compatibilityStore
                .CheckAsync(releaseVersion.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!compatibility.IsCompatible)
            {
                return HealthCheckResult.Unhealthy(
                    $"Application {releaseVersion.Value} is outside database compatibility range {compatibility.MinimumApplicationVersion}..{compatibility.MaximumApplicationVersion}.");
            }

            return HealthCheckResult.Healthy("Database is reachable, migrated, and compatible with this application version.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Database readiness check failed.", exception);
        }
    }
}

internal sealed class WorkerReadinessHealthCheck(
    IWorkerHeartbeatStore heartbeatStore,
    WorkerOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset threshold = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(2));
        if (options.InboxEnabled && !await heartbeatStore.HasRecentHeartbeatAsync("inbox", threshold, cancellationToken).ConfigureAwait(false))
        {
            return HealthCheckResult.Unhealthy("Inbox worker has no recent heartbeat.");
        }

        if (options.DeliveryEnabled && !await heartbeatStore.HasRecentHeartbeatAsync("delivery", threshold, cancellationToken).ConfigureAwait(false))
        {
            return HealthCheckResult.Unhealthy("Delivery worker has no recent heartbeat.");
        }

        return HealthCheckResult.Healthy("Enabled workers have recent heartbeats.");
    }
}
