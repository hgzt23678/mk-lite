using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ActivityPub.Persistence.Tests;

public sealed class InitialAdministratorSetupIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("initial_setup_tests")
        .WithUsername("activitypub")
        .WithPassword("test-only-password")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .Build();
    private ServiceProvider services = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivityPub"] = container.GetConnectionString()
            })
            .Build();
        var accounts = new LocalAccountOptions
        {
            Enabled = true,
            RegistrationEnabled = false,
            RequireConfirmedEmail = true,
            RequiredPasswordLength = 8
        };
        var federation = new FederationOptions
        {
            PublicBaseUri = new Uri("https://setup-tests.example", UriKind.Absolute)
        };
        services = new ServiceCollection()
            .AddLogging()
            .AddHttpContextAccessor()
            .AddDataProtection()
            .Services
            .AddAuthentication()
            .AddCookie(OAuthAuthorizationServerExtensions.ExternalSessionScheme)
            .Services
            .AddSingleton<IClock>(new TestClock())
            .AddSingleton(federation)
            .AddSingleton<PublicIriFactory>()
            .AddSingleton<IExternalKeyProvisioner, TestExternalKeyProvisioner>()
            .AddActivityPubPersistence(configuration, localAccountRegistrationEnabled: true)
            .AddLocalActorAdministration()
            .AddActivityPubLocalAccounts<LocalIdentityDbContext>(accounts, federation.PublicBaseUri)
            .BuildServiceProvider(validateScopes: true);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> federationFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext federationDb = await federationFactory.CreateDbContextAsync();
        await federationDb.Database.MigrateAsync();
        IDbContextFactory<LocalIdentityDbContext> identityFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        await identity.Database.MigrateAsync();
    }

    [Fact]
    public async Task ConcurrentFirstRunCreatesExactlyOneSignedInAdministrator()
    {
        await using (AsyncServiceScope stateScope = services.CreateAsyncScope())
        {
            IInitialSetupState state = stateScope.ServiceProvider.GetRequiredService<IInitialSetupState>();
            Assert.True(await state.IsRequiredAsync(CancellationToken.None));
        }

        var candidates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["first_admin"] = "first test-only password",
            ["second_admin"] = "second test-only password"
        };
        Task<InitialAdministratorSetupResult>[] attempts = candidates
            .Select(candidate => CreateAsync(candidate.Key, candidate.Value))
            .ToArray();
        InitialAdministratorSetupResult[] results = await Task.WhenAll(attempts);

        InitialAdministratorSetupResult created = Assert.Single(
            results,
            result => result.Status == InitialAdministratorSetupStatus.Created);
        Assert.Single(results, result => result.Status == InitialAdministratorSetupStatus.AlreadyInitialized);
        string username = Assert.IsType<string>(created.Username);

        await using AsyncServiceScope verificationScope = services.CreateAsyncScope();
        IInitialSetupState finalState = verificationScope.ServiceProvider.GetRequiredService<IInitialSetupState>();
        Assert.False(await finalState.IsRequiredAsync(CancellationToken.None));
        UserManager<LocalIdentityUser> users =
            verificationScope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        LocalIdentityUser user = Assert.IsType<LocalIdentityUser>(await users.FindByNameAsync(username));
        Assert.Equal(LocalAccountProvisioningState.Active, user.ProvisioningState);
        Assert.True(user.EmailConfirmed);
        Assert.Contains("activitypub-admin", await users.GetRolesAsync(user));

        ILocalAccountService localAccounts =
            verificationScope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        LocalAccountAuthenticationResult authentication = await localAccounts.AuthenticatePasswordAsync(
            username,
            candidates[username],
            authenticatorCode: null,
            CancellationToken.None);
        Assert.Equal(LocalAccountAuthenticationStatus.Succeeded, authentication.Status);

        IDbContextFactory<FederationDbContext> federationFactory =
            verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext federation = await federationFactory.CreateDbContextAsync();
        Assert.Single(await federation.LocalActors.Where(actor => actor.Username == username).ToArrayAsync());
        Assert.Single(await federation.ActorKeys.Where(key => key.OwnerIri == created.ActorIri).ToArrayAsync());
    }

    private async Task<InitialAdministratorSetupResult> CreateAsync(string username, string password)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IInitialAdministratorSetupService setup =
            scope.ServiceProvider.GetRequiredService<IInitialAdministratorSetupService>();
        return await setup.CreateAsync(username, password, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        await container.DisposeAsync();
    }
}
