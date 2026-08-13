using System.Collections.Concurrent;
using System.Security.Cryptography;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ActivityPub.Persistence.Tests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("activitypub_tests")
        .WithUsername("activitypub")
        .WithPassword("test-only-password")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .Build();

    public ServiceProvider Services { get; private set; } = null!;

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ActivityPub"] = container.GetConnectionString()
            })
            .Build();
        var localAccounts = new LocalAccountOptions
        {
            Enabled = true,
            RegistrationEnabled = true,
            RequireConfirmedEmail = false,
            RequiredPasswordLength = 8,
            MaximumFailedAccessAttempts = 5,
            LockoutDuration = TimeSpan.FromMinutes(15),
            SessionLifetime = TimeSpan.FromHours(8)
        };
        var federation = new FederationOptions
        {
            PublicBaseUri = new Uri("https://identity-tests.example", UriKind.Absolute)
        };
        Services = new ServiceCollection()
            .AddLogging()
            .AddHttpContextAccessor()
            .AddDataProtection()
            .Services
            .AddAuthentication()
            .AddCookie(OAuthAuthorizationServerExtensions.ExternalSessionScheme)
            .Services
            .AddSingleton<TestClock>()
            .AddSingleton<IClock>(provider => provider.GetRequiredService<TestClock>())
            .AddSingleton(federation)
            .AddSingleton<PublicIriFactory>()
            .AddSingleton<IExternalKeyProvisioner, TestExternalKeyProvisioner>()
            .AddActivityPubPersistence(configuration, localAccountRegistrationEnabled: true)
            .AddLocalActorAdministration()
            .AddActivityPubLocalAccounts<LocalIdentityDbContext>(localAccounts, federation.PublicBaseUri)
            .AddActivityPubPasswordReset(new PasswordResetOptions
            {
                Enabled = true,
                SenderAddress = "no-reply@identity-tests.example",
                SenderName = "Identity tests",
                SmtpHost = "smtp.identity-tests.example",
                TokenLifetime = TimeSpan.FromMinutes(30),
                RequestCooldown = TimeSpan.FromMinutes(20)
            })
            .AddSingleton<TestPasswordResetEmailSender>()
            .AddSingleton<IPasswordResetEmailSender>(provider => provider.GetRequiredService<TestPasswordResetEmailSender>())
            .AddSingleton<IEmailConfirmationSender>(provider => provider.GetRequiredService<TestPasswordResetEmailSender>())
            .BuildServiceProvider(validateScopes: true);
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        IDbContextFactory<LocalIdentityDbContext> identityFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        await identity.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await container.DisposeAsync();
    }

    public async Task<string> VerifyBackupRestoreAsync(string marker, CancellationToken cancellationToken)
    {
        string restoreDatabase = "restore_" + Guid.NewGuid().ToString("N");
        const string backupPath = "/tmp/activitypub-backup.dump";
        await container.ExecScriptAsync($"""
            CREATE TABLE IF NOT EXISTS public.backup_restore_markers (marker text PRIMARY KEY);
            INSERT INTO public.backup_restore_markers(marker) VALUES ('{marker}');
            """, cancellationToken);

        await EnsureSuccessAsync(["pg_dump", "--format=custom", "--no-owner", "--username=activitypub", "--dbname=activitypub_tests", $"--file={backupPath}"], cancellationToken);
        await EnsureSuccessAsync(["createdb", "--username=activitypub", restoreDatabase], cancellationToken);
        try
        {
            await EnsureSuccessAsync(["pg_restore", "--no-owner", "--username=activitypub", $"--dbname={restoreDatabase}", backupPath], cancellationToken);
            ExecResult result = await EnsureSuccessAsync(
                ["psql", "--tuples-only", "--no-align", "--username=activitypub", $"--dbname={restoreDatabase}", "--command=SELECT marker FROM public.backup_restore_markers LIMIT 1"],
                cancellationToken);
            ExecResult schema = await EnsureSuccessAsync(
                ["psql", "--tuples-only", "--no-align", "--username=activitypub", $"--dbname={restoreDatabase}", "--command=SELECT to_regclass('activitypub.deliveries') IS NOT NULL"],
                cancellationToken);
            if (!string.Equals(schema.Stdout.Trim(), "t", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Restored database does not contain the ActivityPub schema.");
            }

            return result.Stdout.Trim();
        }
        finally
        {
            _ = await container.ExecAsync(["dropdb", "--if-exists", "--username=activitypub", restoreDatabase], cancellationToken);
        }
    }

    public Task TerminateBackendAsync(int backendProcessId, CancellationToken cancellationToken) =>
        container.ExecScriptAsync($"SELECT pg_terminate_backend({backendProcessId});", cancellationToken);

    private async Task<ExecResult> EnsureSuccessAsync(IList<string> command, CancellationToken cancellationToken)
    {
        ExecResult result = await container.ExecAsync(command, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Container command failed with exit code {result.ExitCode}: {result.Stderr}");
        }

        return result;
    }
}

internal sealed class TestExternalKeyProvisioner : IExternalKeyProvisioner
{
    public Task<ExternalKeyProvision> CreateRsaKeyAsync(string handle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RSA rsa = RSA.Create(2048);
        return Task.FromResult(new ExternalKeyProvision(handle, rsa.ExportSubjectPublicKeyInfoPem()));
    }
}

public sealed class TestClock : IClock
{
    private long ticks = DateTimeOffset.UtcNow.UtcTicks;

    public DateTimeOffset UtcNow => new(Interlocked.Read(ref ticks), TimeSpan.Zero);

    public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
}

public sealed class TestPasswordResetEmailSender : IPasswordResetEmailSender, IEmailConfirmationSender
{
    private readonly ConcurrentQueue<PasswordResetEmail> messages = new();
    private readonly ConcurrentQueue<EmailConfirmationEmail> confirmations = new();
    private int failNextConfirmation;

    public IReadOnlyList<PasswordResetEmail> Messages => messages.ToArray();

    public IReadOnlyList<EmailConfirmationEmail> Confirmations => confirmations.ToArray();

    public bool FailNextConfirmation
    {
        set => Interlocked.Exchange(ref failNextConfirmation, value ? 1 : 0);
    }

    public Task SendAsync(PasswordResetEmail email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        messages.Enqueue(email);
        return Task.CompletedTask;
    }

    public Task SendAsync(EmailConfirmationEmail email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref failNextConfirmation, 0) == 1)
        {
            throw new IOException("Simulated SMTP outage without credential data.");
        }

        confirmations.Enqueue(email);
        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlFixtureDefinition : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "postgresql";
}
