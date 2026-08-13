using ActivityPub.Application;
using ActivityPub.Identity;
using ActivityPub.Media;
using ActivityPub.Moderation;
using Npgsql;

namespace ActivityPub.Server;

internal static class ConfigurationReader
{
    public static FederationOptions ReadFederation(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetRequiredSection(FederationOptions.SectionName);
        return new FederationOptions
        {
            PublicBaseUri = RequiredUri(section, "PublicBaseUri"),
            ClientToServerEnabled = section.GetValue("ClientToServerEnabled", true),
            RequireHttps = section.GetValue("RequireHttps", true),
            AllowDevelopmentLoopback = section.GetValue("AllowDevelopmentLoopback", false),
            DevelopmentRestrictToAllowedHosts = section.GetValue("DevelopmentRestrictToAllowedHosts", false),
            DevelopmentAllowedHosts = section.GetSection("DevelopmentAllowedHosts").Get<string[]>() ?? [],
            MaximumInboxBodyBytes = section.GetValue("MaximumInboxBodyBytes", 2_000_000),
            MaximumRemoteDocumentBytes = section.GetValue("MaximumRemoteDocumentBytes", 2_000_000),
            MaximumRedirects = section.GetValue("MaximumRedirects", 3),
            MaximumFetchDepth = section.GetValue("MaximumFetchDepth", 4),
            MaximumFetchesPerOperation = section.GetValue("MaximumFetchesPerOperation", 16),
            MaximumRecipientsPerActivity = section.GetValue("MaximumRecipientsPerActivity", 10_000),
            ClientIdempotencyRetention = section.GetValue("ClientIdempotencyRetention", TimeSpan.FromHours(24)),
            ConnectTimeout = section.GetValue("ConnectTimeout", TimeSpan.FromSeconds(5)),
            RequestTimeout = section.GetValue("RequestTimeout", TimeSpan.FromSeconds(15)),
            SignatureClockSkew = section.GetValue("SignatureClockSkew", TimeSpan.FromMinutes(5)),
            RemoteKeyCacheDuration = section.GetValue("RemoteKeyCacheDuration", TimeSpan.FromHours(6)),
            RemoteKeyRefreshCooldown = section.GetValue("RemoteKeyRefreshCooldown", TimeSpan.FromMinutes(5))
        };
    }

    public static WorkerOptions ReadWorkers(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(WorkerOptions.SectionName);
        return new WorkerOptions
        {
            InboxEnabled = section.GetValue("InboxEnabled", true),
            DeliveryEnabled = section.GetValue("DeliveryEnabled", true),
            BatchSize = section.GetValue("BatchSize", 32),
            PollInterval = section.GetValue("PollInterval", TimeSpan.FromMilliseconds(500)),
            LeaseDuration = section.GetValue("LeaseDuration", TimeSpan.FromMinutes(2)),
            HeartbeatInterval = section.GetValue("HeartbeatInterval", TimeSpan.FromSeconds(30)),
            MaximumInboxAttempts = section.GetValue("MaximumInboxAttempts", 12),
            MaximumDeliveryAttempts = section.GetValue("MaximumDeliveryAttempts", 12),
            InitialRetryDelay = section.GetValue("InitialRetryDelay", TimeSpan.FromSeconds(30)),
            MaximumRetryDelay = section.GetValue("MaximumRetryDelay", TimeSpan.FromHours(24)),
            MaximumConcurrentDeliveriesPerDomain = section.GetValue("MaximumConcurrentDeliveriesPerDomain", 2),
            DomainCircuitFailureThreshold = section.GetValue("DomainCircuitFailureThreshold", 5),
            DomainCircuitBreakDuration = section.GetValue("DomainCircuitBreakDuration", TimeSpan.FromMinutes(5)),
            RawJsonRetentionEnabled = section.GetValue("RawJsonRetentionEnabled", true),
            ActivityRawJsonRetention = section.GetValue("ActivityRawJsonRetention", TimeSpan.FromDays(90)),
            ObjectRawJsonRetention = section.GetValue("ObjectRawJsonRetention", TimeSpan.FromDays(180)),
            RawJsonPurgeInterval = section.GetValue("RawJsonPurgeInterval", TimeSpan.FromHours(1)),
            RawJsonPurgeBatchSize = section.GetValue("RawJsonPurgeBatchSize", 500)
        };
    }

    public static ApiAuthenticationOptions ReadAuthentication(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetRequiredSection(ApiAuthenticationOptions.SectionName);
        return new ApiAuthenticationOptions
        {
            Authority = RequiredUri(section, "Authority"),
            Audience = section["Audience"] ?? throw new InvalidOperationException("Authentication:Audience is required."),
            RequireHttpsMetadata = section.GetValue("RequireHttpsMetadata", true)
        };
    }

    public static MisskeyAuthenticationOptions ReadMisskeyAuthentication(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(MisskeyAuthenticationOptions.SectionName);
        var options = new MisskeyAuthenticationOptions
        {
            SessionLifetime = section.GetValue("SessionLifetime", TimeSpan.FromMinutes(15)),
            AccessTokenLifetime = section.GetValue("AccessTokenLifetime", TimeSpan.FromDays(90))
        };
        options.Validate();
        return options;
    }

    public static OAuthAuthorizationServerOptions ReadOAuth(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(OAuthAuthorizationServerOptions.SectionName);
        return new OAuthAuthorizationServerOptions
        {
            Enabled = section.GetValue("Enabled", false),
            AccessTokenLifetime = section.GetValue("AccessTokenLifetime", TimeSpan.FromHours(1)),
            RefreshTokenLifetime = section.GetValue("RefreshTokenLifetime", TimeSpan.FromDays(30)),
            RefreshTokenReuseLeeway = section.GetValue("RefreshTokenReuseLeeway", TimeSpan.Zero),
            InteractiveClientId = section["InteractiveClientId"],
            CallbackPath = section["CallbackPath"] ?? "/auth/callback",
            SigningCertificatePath = section["SigningCertificatePath"],
            SigningCertificatePasswordFile = section["SigningCertificatePasswordFile"],
            EncryptionCertificatePath = section["EncryptionCertificatePath"],
            EncryptionCertificatePasswordFile = section["EncryptionCertificatePasswordFile"]
        };
    }

    public static LocalAccountOptions ReadLocalAccounts(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(LocalAccountOptions.SectionName);
        return new LocalAccountOptions
        {
            Enabled = section.GetValue("Enabled", false),
            RegistrationEnabled = section.GetValue("RegistrationEnabled", false),
            RequireConfirmedEmail = section.GetValue("RequireConfirmedEmail", false),
            RequiredPasswordLength = section.GetValue("RequiredPasswordLength", 8),
            MaximumFailedAccessAttempts = section.GetValue("MaximumFailedAccessAttempts", 5),
            LockoutDuration = section.GetValue("LockoutDuration", TimeSpan.FromMinutes(15)),
            SessionLifetime = section.GetValue("SessionLifetime", TimeSpan.FromHours(8))
        };
    }

    public static RegistrationProtectionOptions ReadRegistrationProtection(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(RegistrationProtectionOptions.SectionName);
        return new RegistrationProtectionOptions
        {
            InvitationRequired = section.GetValue("InvitationRequired", false),
            InvitationLifetime = section.GetValue("InvitationLifetime", TimeSpan.FromDays(7)),
            InvitationReservationLifetime = section.GetValue("InvitationReservationLifetime", TimeSpan.FromMinutes(5)),
            CaptchaProvider = section.GetValue("CaptchaProvider", RegistrationCaptchaProvider.None),
            CaptchaSiteKey = section["CaptchaSiteKey"] ?? string.Empty,
            CaptchaSecretFile = section["CaptchaSecretFile"],
            CaptchaExpectedHostname = section["CaptchaExpectedHostname"] ?? string.Empty,
            CaptchaExpectedAction = section["CaptchaExpectedAction"] ?? "signup",
            CaptchaExpectedCdata = section["CaptchaExpectedCdata"] ?? "activitypub_signup",
            CaptchaVerificationTimeout = section.GetValue("CaptchaVerificationTimeout", TimeSpan.FromSeconds(10))
        };
    }

    public static PasswordResetOptions ReadPasswordReset(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(PasswordResetOptions.SectionName);
        return new PasswordResetOptions
        {
            Enabled = section.GetValue("Enabled", false),
            SenderAddress = section["SenderAddress"] ?? string.Empty,
            SenderName = section["SenderName"] ?? string.Empty,
            SmtpHost = section["SmtpHost"] ?? string.Empty,
            SmtpPort = section.GetValue("SmtpPort", 587),
            TlsMode = section.GetValue("TlsMode", PasswordResetTlsMode.StartTls),
            SmtpUsername = section["SmtpUsername"],
            SmtpPasswordFile = section["SmtpPasswordFile"],
            TokenLifetime = section.GetValue("TokenLifetime", TimeSpan.FromMinutes(30)),
            RequestCooldown = section.GetValue("RequestCooldown", TimeSpan.FromMinutes(20)),
            EmailConfirmationTokenLifetime = section.GetValue("EmailConfirmationTokenLifetime", TimeSpan.FromHours(24)),
            EmailConfirmationRequestCooldown = section.GetValue("EmailConfirmationRequestCooldown", TimeSpan.FromMinutes(20)),
            SendTimeout = section.GetValue("SendTimeout", TimeSpan.FromSeconds(15))
        };
    }

    public static FrontendOptions ReadFrontend(
        IConfiguration configuration,
        Uri federationPublicBaseUri,
        Uri authenticationAuthority)
    {
        IConfigurationSection section = configuration.GetSection(FrontendOptions.SectionName);
        string? sourceUrl = section["SourceUrl"];
        string? publicBaseUri = section["PublicBaseUri"];
        string? authority = section["Authority"];
        return new FrontendOptions
        {
            Enabled = section.GetValue("Enabled", false),
            ClientId = section["ClientId"] ?? string.Empty,
            Scopes = section.GetSection("Scopes").Get<string[]>() ??
                ["openid", "profile", "offline_access", "activitypub.read", "activitypub.write"],
            PublicBaseUri = string.IsNullOrWhiteSpace(publicBaseUri)
                ? federationPublicBaseUri
                : RequiredUri(section, "PublicBaseUri"),
            Authority = string.IsNullOrWhiteSpace(authority)
                ? authenticationAuthority
                : RequiredUri(section, "Authority"),
            SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? null : RequiredUri(section, "SourceUrl")
        };
    }

    public static VaultTransitOptions ReadVaultTransit(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetRequiredSection(VaultTransitOptions.SectionName);
        return new VaultTransitOptions
        {
            Address = RequiredUri(section, "Address"),
            Mount = section["Mount"] ?? "transit",
            TokenFile = section["TokenFile"] ?? throw new InvalidOperationException("VaultTransit:TokenFile is required."),
            Namespace = section["Namespace"]
        };
    }

    public static MediaOptions ReadMedia(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(MediaOptions.SectionName);
        return new MediaOptions
        {
            Enabled = section.GetValue("Enabled", false),
            Provider = section.GetValue("Provider", MediaObjectStoreProvider.S3Compatible),
            Bucket = section["Bucket"] ?? string.Empty,
            ServiceUrl = section["ServiceUrl"],
            Region = section["Region"] ?? "us-east-1",
            ForcePathStyle = section.GetValue("ForcePathStyle", true),
            UseServerSideEncryption = section.GetValue("UseServerSideEncryption", true),
            CloudflareAccountId = section["CloudflareAccountId"],
            CloudflareJurisdiction = section.GetValue("CloudflareJurisdiction", CloudflareR2Jurisdiction.Default),
            MaximumUploadBytes = section.GetValue("MaximumUploadBytes", 100L * 1024 * 1024),
            MaximumImageWidth = section.GetValue("MaximumImageWidth", 16_384),
            MaximumImageHeight = section.GetValue("MaximumImageHeight", 16_384),
            MaximumMediaDuration = section.GetValue("MaximumMediaDuration", TimeSpan.FromHours(4)),
            FfmpegPath = section["FfmpegPath"] ?? "/usr/bin/ffmpeg",
            FfprobePath = section["FfprobePath"] ?? "/usr/bin/ffprobe",
            ProcessorTimeout = section.GetValue("ProcessorTimeout", TimeSpan.FromMinutes(10)),
            ClamAvHost = section["ClamAvHost"] ?? "clamav",
            ClamAvPort = section.GetValue("ClamAvPort", 3310),
            ScanTimeout = section.GetValue("ScanTimeout", TimeSpan.FromMinutes(2)),
            GarbageCollectionEnabled = section.GetValue("GarbageCollectionEnabled", true),
            UnreferencedRetention = section.GetValue("UnreferencedRetention", TimeSpan.FromDays(30)),
            GarbageCollectionInterval = section.GetValue("GarbageCollectionInterval", TimeSpan.FromHours(1)),
            GarbageRetryDelay = section.GetValue("GarbageRetryDelay", TimeSpan.FromMinutes(5)),
            GarbageCollectionBatchSize = section.GetValue("GarbageCollectionBatchSize", 100),
            MaximumRemoteMediaBytes = section.GetValue("MaximumRemoteMediaBytes", 10 * 1024 * 1024),
            RemoteMediaCacheRetention = section.GetValue("RemoteMediaCacheRetention", TimeSpan.FromDays(7)),
            RemoteMediaFetchLeaseDuration = section.GetValue("RemoteMediaFetchLeaseDuration", TimeSpan.FromMinutes(2)),
            RemoteMediaFetchLeaseRenewalInterval = section.GetValue("RemoteMediaFetchLeaseRenewalInterval", TimeSpan.FromSeconds(30)),
            RemoteMediaFetchWaitTimeout = section.GetValue("RemoteMediaFetchWaitTimeout", TimeSpan.FromSeconds(15)),
            RemoteMediaFailureRetryDelay = section.GetValue("RemoteMediaFailureRetryDelay", TimeSpan.FromMinutes(5))
        };
    }

    public static void ValidateProductionConfiguration(
        IConfiguration configuration,
        FederationOptions federation,
        ApiAuthenticationOptions authentication,
        bool isProduction)
    {
        if (!isProduction)
        {
            return;
        }

        if (IsPlaceholderHost(federation.PublicBaseUri.IdnHost))
        {
            throw new InvalidOperationException("Federation:PublicBaseUri uses a placeholder or local hostname.");
        }

        if (IsPlaceholderHost(authentication.Authority.IdnHost))
        {
            throw new InvalidOperationException("Authentication:Authority uses a placeholder or local hostname.");
        }

        string connection = configuration.GetConnectionString("ActivityPub")
            ?? throw new InvalidOperationException("ConnectionStrings:ActivityPub is required.");
        if (connection.Contains("design-time-only", StringComparison.OrdinalIgnoreCase) ||
            connection.Contains("example.invalid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The production database connection string contains a known placeholder.");
        }

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connection);
        if (connectionBuilder.SslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException("The production database connection must use SSL Mode=VerifyFull.");
        }

        foreach (string origin in configuration.GetSection("Http:AllowedCorsOrigins").Get<string[]>() ?? [])
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.PathAndQuery) && uri.PathAndQuery != "/")
            {
                throw new InvalidOperationException("Production CORS origins must be HTTPS origins without paths.");
            }
        }
    }

    public static SpamEvaluationOptions ReadSpamEvaluation(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Moderation:Spam");
        return new SpamEvaluationOptions
        {
            QuarantineScore = section.GetValue("QuarantineScore", 100),
            MaximumLinks = section.GetValue("MaximumLinks", 40),
            MaximumMentions = section.GetValue("MaximumMentions", 100),
            MaximumHashtags = section.GetValue("MaximumHashtags", 100)
        };
    }

    private static bool IsPlaceholderHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".example", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("example.com", StringComparison.OrdinalIgnoreCase);

    private static Uri RequiredUri(IConfigurationSection section, string name)
    {
        string value = section[name] ?? throw new InvalidOperationException($"{section.Path}:{name} is required.");
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException($"{section.Path}:{name} must be an absolute URI.");
        }

        return uri;
    }
}
