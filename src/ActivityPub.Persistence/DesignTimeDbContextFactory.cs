using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActivityPub.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FederationDbContext>
{
    public FederationDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ACTIVITYPUB_MIGRATION_CONNECTION")
            ?? "Host=127.0.0.1;Database=activitypub_design;Username=activitypub_design;Password=design-time-only";
        var builder = new DbContextOptionsBuilder<FederationDbContext>();
        builder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(FederationDbContext).Assembly.FullName);
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "activitypub");
        });
        builder.UseOpenIddict();
        return new FederationDbContext(builder.Options);
    }
}
