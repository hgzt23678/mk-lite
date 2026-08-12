using ActivityPub.Media;

namespace ActivityPub.Media.Tests;

public sealed class MediaOptionsTests
{
    [Fact]
    public void EnabledDefaultsAreInternallyConsistent()
    {
        var options = new MediaOptions
        {
            Enabled = true,
            Bucket = "activitypub-media"
        };

        options.Validate(isProduction: false);
    }

    [Fact]
    public void CacheRetentionCannotOutliveOldBinaryGarbageCollectionProtection()
    {
        var options = new MediaOptions
        {
            Enabled = true,
            Bucket = "activitypub-media",
            GarbageCollectionEnabled = true,
            UnreferencedRetention = TimeSpan.FromDays(1),
            RemoteMediaCacheRetention = TimeSpan.FromDays(2)
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(isProduction: false));
    }

    [Fact]
    public void LeaseRenewalMustBeShorterThanLeaseDuration()
    {
        var options = new MediaOptions
        {
            Enabled = true,
            Bucket = "activitypub-media",
            RemoteMediaFetchLeaseDuration = TimeSpan.FromMinutes(1),
            RemoteMediaFetchLeaseRenewalInterval = TimeSpan.FromMinutes(1)
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(isProduction: false));
    }
}
