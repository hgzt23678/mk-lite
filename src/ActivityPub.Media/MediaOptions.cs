namespace ActivityPub.Media;

public enum MediaObjectStoreProvider
{
    S3Compatible,
    CloudflareR2
}

public enum CloudflareR2Jurisdiction
{
    Default,
    Eu,
    FedRamp
}

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public bool Enabled { get; init; }
    public MediaObjectStoreProvider Provider { get; init; } = MediaObjectStoreProvider.S3Compatible;
    public string Bucket { get; init; } = string.Empty;
    public string? ServiceUrl { get; init; }
    public string Region { get; init; } = "us-east-1";
    public bool ForcePathStyle { get; init; } = true;
    public bool UseServerSideEncryption { get; init; } = true;
    public string? CloudflareAccountId { get; init; }
    public CloudflareR2Jurisdiction CloudflareJurisdiction { get; init; } = CloudflareR2Jurisdiction.Default;
    public long MaximumUploadBytes { get; init; } = 100 * 1024 * 1024;
    public int MaximumImageWidth { get; init; } = 16_384;
    public int MaximumImageHeight { get; init; } = 16_384;
    public TimeSpan MaximumMediaDuration { get; init; } = TimeSpan.FromHours(4);
    public string FfmpegPath { get; init; } = "/usr/bin/ffmpeg";
    public string FfprobePath { get; init; } = "/usr/bin/ffprobe";
    public TimeSpan ProcessorTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public string ClamAvHost { get; init; } = "clamav";
    public int ClamAvPort { get; init; } = 3310;
    public TimeSpan ScanTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public bool GarbageCollectionEnabled { get; init; } = true;
    public TimeSpan UnreferencedRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan GarbageCollectionInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan GarbageRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
    public int GarbageCollectionBatchSize { get; init; } = 100;
    public int MaximumRemoteMediaBytes { get; init; } = 10 * 1024 * 1024;
    public TimeSpan RemoteMediaCacheRetention { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan RemoteMediaFetchLeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan RemoteMediaFetchLeaseRenewalInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RemoteMediaFetchWaitTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan RemoteMediaFailureRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate(bool isProduction)
    {
        if (!Enabled)
        {
            if (isProduction)
            {
                throw new InvalidOperationException("Media must be enabled for a production deployment.");
            }

            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(Region);
        ArgumentException.ThrowIfNullOrWhiteSpace(FfmpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(FfprobePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClamAvHost);
        if (!Enum.IsDefined(Provider) || !Enum.IsDefined(CloudflareJurisdiction))
        {
            throw new InvalidOperationException("Media provider or Cloudflare jurisdiction is unsupported.");
        }

        if (MaximumUploadBytes is < 1 or > 2_147_483_647 || MaximumImageWidth < 1 || MaximumImageHeight < 1 ||
            MaximumMediaDuration <= TimeSpan.Zero || ProcessorTimeout <= TimeSpan.Zero || ScanTimeout <= TimeSpan.Zero ||
            ClamAvPort is < 1 or > 65_535 || MaximumRemoteMediaBytes is < 1 or > 100 * 1024 * 1024 ||
            RemoteMediaCacheRetention < TimeSpan.FromHours(1) ||
            RemoteMediaFetchLeaseDuration < TimeSpan.FromSeconds(30) ||
            RemoteMediaFetchLeaseRenewalInterval < TimeSpan.FromSeconds(5) ||
            RemoteMediaFetchLeaseRenewalInterval >= RemoteMediaFetchLeaseDuration ||
            RemoteMediaFetchWaitTimeout < TimeSpan.FromSeconds(1) ||
            RemoteMediaFetchWaitTimeout > TimeSpan.FromMinutes(2) ||
            RemoteMediaFailureRetryDelay < TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException("Media limits and timeouts must be positive and within supported bounds.");
        }

        if (GarbageCollectionEnabled &&
            (UnreferencedRetention < TimeSpan.FromDays(1) || GarbageCollectionInterval < TimeSpan.FromMinutes(1) ||
             GarbageRetryDelay < TimeSpan.FromMinutes(1) || GarbageCollectionBatchSize is < 1 or > 1_000 ||
             RemoteMediaCacheRetention > UnreferencedRetention))
        {
            throw new InvalidOperationException("Media garbage collection limits are outside the supported range.");
        }

        if (isProduction && !string.IsNullOrWhiteSpace(ServiceUrl) &&
            (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out Uri? serviceUri) || serviceUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Media:ServiceUrl must be an absolute HTTPS S3-compatible endpoint URI in Production.");
        }

        if (Provider == MediaObjectStoreProvider.CloudflareR2)
        {
            if (!string.IsNullOrWhiteSpace(ServiceUrl))
            {
                throw new InvalidOperationException(
                    "Media:ServiceUrl must be omitted for CloudflareR2; the official account endpoint is derived from Media:CloudflareAccountId.");
            }

            if (!IsCloudflareAccountId(CloudflareAccountId))
            {
                throw new InvalidOperationException("Media:CloudflareAccountId must be the 32-character hexadecimal Cloudflare account ID.");
            }

            if (!string.Equals(Region, "auto", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Media:Region must be 'auto' for CloudflareR2.");
            }

            if (!ForcePathStyle)
            {
                throw new InvalidOperationException("Media:ForcePathStyle must be enabled for CloudflareR2.");
            }
        }

        if (isProduction && !UseServerSideEncryption)
        {
            throw new InvalidOperationException("Media:UseServerSideEncryption must be enabled in Production.");
        }
    }

    internal Uri ResolveServiceUri()
    {
        if (Provider != MediaObjectStoreProvider.CloudflareR2)
        {
            if (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out Uri? serviceUri))
            {
                throw new InvalidOperationException("Media:ServiceUrl does not contain an absolute URI.");
            }

            return serviceUri;
        }

        if (!IsCloudflareAccountId(CloudflareAccountId))
        {
            throw new InvalidOperationException("Media:CloudflareAccountId is invalid.");
        }

        string jurisdiction = CloudflareJurisdiction switch
        {
            CloudflareR2Jurisdiction.Default => string.Empty,
            CloudflareR2Jurisdiction.Eu => ".eu",
            CloudflareR2Jurisdiction.FedRamp => ".fedramp",
            _ => throw new InvalidOperationException("Media:CloudflareJurisdiction is unsupported.")
        };
        return new Uri($"https://{CloudflareAccountId}{jurisdiction}.r2.cloudflarestorage.com", UriKind.Absolute);
    }

    private static bool IsCloudflareAccountId(string? value) =>
        value is { Length: 32 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
