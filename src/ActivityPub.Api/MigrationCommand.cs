using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Server;

internal static class MigrationCommand
{
    private const long MigrationAdvisoryLock = 4_165_550_803_371_912_002;

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock({MigrationAdvisoryLock})",
                cancellationToken).ConfigureAwait(false);
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            if (db.Database.HasPendingModelChanges())
            {
                throw new InvalidOperationException("The EF model differs from the latest checked-in migration.");
            }

            LocalIdentityDbContext identity = scope.ServiceProvider.GetRequiredService<LocalIdentityDbContext>();
            await identity.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            if (identity.Database.HasPendingModelChanges())
            {
                throw new InvalidOperationException("The local Identity EF model differs from the latest checked-in migration.");
            }
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_unlock({MigrationAdvisoryLock})",
                CancellationToken.None).ConfigureAwait(false);
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
