using ActivityPub.Application;
using ActivityPub.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddActivityPubPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool localAccountRegistrationEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("ActivityPub")
            ?? throw new InvalidOperationException("ConnectionStrings:ActivityPub is required.");

        // LocalIdentityDbContext always maps ASP.NET Core Identity's passkey entities.
        // Keep that model invariant available to maintenance commands even when the
        // interactive local-account feature (and therefore UserManager) is disabled.
        services.Configure<IdentityOptions>(identity =>
            identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        services.AddPooledDbContextFactory<FederationDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(FederationDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "activitypub");
                npgsql.CommandTimeout(30);
            });
            options.UseOpenIddict();
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, poolSize: 128);
        services.AddScoped(provider => provider
            .GetRequiredService<IDbContextFactory<FederationDbContext>>()
            .CreateDbContext());
        services.AddPooledDbContextFactory<LocalIdentityDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(LocalIdentityDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity");
                npgsql.CommandTimeout(30);
            });
        }, poolSize: 64);
        services.AddScoped(provider => provider
            .GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>()
            .CreateDbContext());

        services.AddScoped<IInboxRepository, InboxRepository>();
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IRelayRepository, RelayStore>();
        services.AddScoped<IHashtagRepository, HashtagStore>();
        services.AddScoped<IUrlPreviewRepository, UrlPreviewStore>();
        services.AddScoped<IAnnounceChainGuard, AnnounceChainGuard>();
        services.AddScoped<IClientDriveService, DriveService>();
        services.AddScoped<IProfileUpdateService, ProfileUpdateService>();
        services.AddScoped<IFederationQueryStore, FederationQueryStore>();
        services.AddScoped<IDomainPolicyService, DomainPolicyService>();
        services.AddScoped<IRemoteKeyCacheStore, RemoteKeyCacheStore>();
        services.AddScoped<IPrivateKeyStore, PrivateKeyStore>();
        services.AddScoped<IRemoteDomainExecutionStore, RemoteDomainExecutionStore>();
        services.AddScoped<IRemoteActorDirectory, RemoteActorDirectory>();
        services.AddScoped<IWorkerHeartbeatStore, WorkerHeartbeatStore>();
        services.AddScoped<IAuditLog, PostgreSqlAuditLog>();
        services.AddScoped<IModerationAdministration, ModerationAdministration>();
        services.AddScoped<IFederationQueueAdministration, FederationQueueAdministration>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IRemoteMediaCacheRepository, RemoteMediaCacheRepository>();
        services.AddScoped<IRemoteActorMediaCacheRepository, RemoteActorMediaCacheRepository>();
        services.AddScoped<IRawJsonRetentionStore, RawJsonRetentionStore>();
        services.AddScoped<ISchemaCompatibilityStore, SchemaCompatibilityStore>();
        services.AddScoped<IExternalEntityIdService, ExternalEntityIdService>();
        services.AddScoped<IClientApiQueryService, ClientApiQueryService>();
        services.AddScoped<IRemoteInstanceQueryService, RemoteInstanceQueryService>();
        services.AddScoped<IClientApiCommandService, ClientApiCommandService>();
        services.AddScoped<IMisskeyAuthenticationService, MisskeyAuthenticationService>();
        services.AddScoped<IInitialSetupState, InitialSetupState>();
        services.AddScoped<IInitialAdministratorSetupService, InitialAdministratorSetupService>();
        services.AddScoped<IStreamEventStore, StreamEventStore>();
        services.AddScoped<IDurableStreamEventPump, DurableStreamEventPump>();
        services.AddSingleton<RedisAccelerationService>(provider =>
        {
            string? redisConnection = configuration["Redis:ConnectionString"] ??
                configuration["Streaming:Redis:ConnectionString"];
            return new RedisAccelerationService(
                redisConnection,
                configuration["Redis:KeyPrefix"],
                configuration["Redis:DeliveryQueueChannel"],
                configuration["Redis:InboxQueueChannel"],
                configuration.GetValue("Redis:TimelineCacheTtl", TimeSpan.FromSeconds(3)),
                configuration.GetValue("Redis:NotificationCountTtl", TimeSpan.FromSeconds(10)),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RedisAccelerationService>>());
        });
        services.AddSingleton<IFederationQueueSignal>(provider =>
            provider.GetRequiredService<RedisAccelerationService>());
        services.AddSingleton<IClientProjectionCache>(provider =>
            provider.GetRequiredService<RedisAccelerationService>());
        services.AddSingleton<IStreamEventNotifier>(provider =>
        {
            string? redisConnection = configuration["Redis:ConnectionString"] ??
                configuration["Streaming:Redis:ConnectionString"];
            return string.IsNullOrWhiteSpace(redisConnection)
                ? new NullStreamEventNotifier()
                : new RedisStreamEventNotifier(redisConnection, configuration["Streaming:Redis:Channel"]);
        });
        services.AddScoped<IStreamConnectionLeaseStore, StreamConnectionLeaseStore>();
        services.AddScoped<IClientNotificationService, ClientNotificationService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddScoped<IPasswordResetStore, PasswordResetStore>();
        services.AddScoped<IEmailConfirmationStore, EmailConfirmationStore>();
        services.AddScoped<IRegistrationInvitationStore, RegistrationInvitationStore>();
        if (localAccountRegistrationEnabled)
        {
            services.AddScoped<IAtomicLocalAccountRegistration, AtomicLocalAccountRegistration>();
        }

        return services;
    }

    public static IServiceCollection AddLocalActorAdministration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ILocalActorAdministration, LocalActorAdministration>();
        return services;
    }
}
