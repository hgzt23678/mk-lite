using System.Net;
using ActivityPub.Domain;

namespace ActivityPub.Application;

public sealed class FederationOptions
{
    public const string SectionName = "Federation";

    public required Uri PublicBaseUri { get; init; }
    public bool ClientToServerEnabled { get; init; } = true;
    public bool RequireHttps { get; init; } = true;
    public bool AllowDevelopmentLoopback { get; init; }
    public bool DevelopmentRestrictToAllowedHosts { get; init; }
    public string[] DevelopmentAllowedHosts { get; init; } = [];
    public int MaximumInboxBodyBytes { get; init; } = 2_000_000;
    public int MaximumRemoteDocumentBytes { get; init; } = 2_000_000;
    public int MaximumRedirects { get; init; } = 3;
    public int MaximumFetchDepth { get; init; } = 4;
    public int MaximumFetchesPerOperation { get; init; } = 16;
    public int MaximumRecipientsPerActivity { get; init; } = 10_000;
    public TimeSpan ClientIdempotencyRetention { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan SignatureClockSkew { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RemoteKeyCacheDuration { get; init; } = TimeSpan.FromHours(6);
    public TimeSpan RemoteKeyRefreshCooldown { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate(bool isProduction)
    {
        string origin = CanonicalIri.RequireWebOrigin(
            PublicBaseUri.AbsoluteUri,
            nameof(PublicBaseUri),
            requireHttps: isProduction || RequireHttps);
        if (!string.Equals(origin, PublicBaseUri.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Federation:PublicBaseUri must be a canonical origin.");
        }

        if (isProduction && (!RequireHttps || AllowDevelopmentLoopback || DevelopmentRestrictToAllowedHosts ||
            DevelopmentAllowedHosts.Length > 0))
        {
            throw new InvalidOperationException("Development federation network exceptions cannot be enabled in Production.");
        }

        var uniqueDevelopmentHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string host in DevelopmentAllowedHosts)
        {
            if (string.IsNullOrWhiteSpace(host) || host != host.Trim() || host.Contains('*', StringComparison.Ordinal) ||
                Uri.CheckHostName(host) != UriHostNameType.Dns || IPAddress.TryParse(host, out _) ||
                !uniqueDevelopmentHosts.Add(host))
            {
                throw new InvalidOperationException(
                    "Federation:DevelopmentAllowedHosts must contain unique, exact DNS hostnames without wildcards or IP literals.");
            }
        }

        if (DevelopmentRestrictToAllowedHosts && DevelopmentAllowedHosts.Length == 0 && !AllowDevelopmentLoopback)
        {
            throw new InvalidOperationException(
                "Federation:DevelopmentRestrictToAllowedHosts requires at least one development host or loopback.");
        }

        if (MaximumInboxBodyBytes is < 1_024 or > 10_000_000 ||
            MaximumRemoteDocumentBytes is < 1_024 or > 10_000_000)
        {
            throw new InvalidOperationException("Federation document limits are outside the supported range.");
        }

        if (MaximumRedirects is < 0 or > 10 || MaximumFetchDepth is < 1 or > 16 ||
            MaximumFetchesPerOperation is < 1 or > 128)
        {
            throw new InvalidOperationException("Federation fetch limits are outside the supported range.");
        }


        if (MaximumRecipientsPerActivity is < 1 or > 100_000)
        {
            throw new InvalidOperationException("Federation recipient limit is outside the supported range.");
        }


        if (ClientIdempotencyRetention < TimeSpan.FromHours(1) || ClientIdempotencyRetention > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException("Client idempotency retention must be between one hour and thirty days.");
        }
    }

    public bool IsDevelopmentHostAllowed(string host) =>
        DevelopmentAllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkerOptions
{
    public const string SectionName = "Workers";

    public bool InboxEnabled { get; init; } = true;
    public bool DeliveryEnabled { get; init; } = true;
    public int BatchSize { get; init; } = 32;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);
    public int MaximumInboxAttempts { get; init; } = 12;
    public int MaximumDeliveryAttempts { get; init; } = 12;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromHours(24);
    public int MaximumConcurrentDeliveriesPerDomain { get; init; } = 2;
    public int DomainCircuitFailureThreshold { get; init; } = 5;
    public TimeSpan DomainCircuitBreakDuration { get; init; } = TimeSpan.FromMinutes(5);
    public bool RawJsonRetentionEnabled { get; init; } = true;
    public TimeSpan ActivityRawJsonRetention { get; init; } = TimeSpan.FromDays(90);
    public TimeSpan ObjectRawJsonRetention { get; init; } = TimeSpan.FromDays(180);
    public TimeSpan RawJsonPurgeInterval { get; init; } = TimeSpan.FromHours(1);
    public int RawJsonPurgeBatchSize { get; init; } = 500;

    public void Validate()
    {
        if (BatchSize is < 1 or > 1_000 || MaximumInboxAttempts is < 1 or > 100 || MaximumDeliveryAttempts is < 1 or > 100)
        {
            throw new InvalidOperationException("Worker batch or attempt configuration is outside the supported range.");
        }

        if (LeaseDuration <= TimeSpan.Zero || HeartbeatInterval <= TimeSpan.Zero ||
            HeartbeatInterval >= LeaseDuration / 2)
        {
            throw new InvalidOperationException("Worker heartbeat must be positive and less than half of the lease duration.");
        }

        if (InitialRetryDelay <= TimeSpan.Zero || MaximumRetryDelay < InitialRetryDelay)
        {
            throw new InvalidOperationException("Worker retry delays are invalid.");
        }


        if (MaximumConcurrentDeliveriesPerDomain is < 1 or > 64 || DomainCircuitFailureThreshold is < 1 or > 1_000 ||
            DomainCircuitBreakDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker domain concurrency or circuit configuration is invalid.");
        }

        if (RawJsonRetentionEnabled &&
            (ActivityRawJsonRetention < TimeSpan.FromDays(1) || ObjectRawJsonRetention < TimeSpan.FromDays(1) ||
             RawJsonPurgeInterval < TimeSpan.FromMinutes(1) || RawJsonPurgeBatchSize is < 1 or > 10_000))
        {
            throw new InvalidOperationException("Raw JSON retention configuration is outside the supported range.");
        }
    }
}
