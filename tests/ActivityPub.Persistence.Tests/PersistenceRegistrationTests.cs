using ActivityPub.Identity;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence.Tests;

public sealed class PersistenceRegistrationTests
{
    [Fact]
    public async Task MaintenanceRegistrationBuildsTheVersionThreeIdentityModelWithoutUserManager()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivityPub"] =
                    "Host=127.0.0.1;Port=1;Database=model_only;Username=model_only;Password=model_only"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddActivityPubPersistence(configuration);

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IAtomicLocalAccountRegistration));
        await using ServiceProvider provider = services.BuildServiceProvider();
        IDbContextFactory<LocalIdentityDbContext> factory =
            provider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext context = await factory.CreateDbContextAsync();
        IEntityType passkeyData = Assert.Single(
            context.Model.GetEntityTypes(),
            entity => entity.ClrType == typeof(IdentityPasskeyData));
        Assert.NotNull(passkeyData.FindPrimaryKey());
    }

    [Fact]
    public void InteractiveRegistrationAddsTheAtomicRegistrationBoundaryOnlyWhenEnabled()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivityPub"] =
                    "Host=127.0.0.1;Port=1;Database=model_only;Username=model_only;Password=model_only"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddActivityPubPersistence(configuration, localAccountRegistrationEnabled: true);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAtomicLocalAccountRegistration) &&
            descriptor.ImplementationType == typeof(AtomicLocalAccountRegistration) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
