using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence;

public sealed class LocalIdentityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<LocalIdentityDbContext>
{
    private static readonly ServiceProvider DesignServices = CreateDesignServices();

    public LocalIdentityDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ACTIVITYPUB_MIGRATION_CONNECTION")
            ?? "Host=127.0.0.1;Database=activitypub_design;Username=activitypub_design;Password=design-time-only";
        var builder = new DbContextOptionsBuilder<LocalIdentityDbContext>();
        builder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(LocalIdentityDbContext).Assembly.FullName);
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity");
        });
        builder.UseApplicationServiceProvider(DesignServices);
        return new LocalIdentityDbContext(builder.Options);
    }

    private static ServiceProvider CreateDesignServices()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        return services.BuildServiceProvider();
    }
}
