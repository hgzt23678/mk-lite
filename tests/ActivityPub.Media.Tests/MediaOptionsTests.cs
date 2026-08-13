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

    [Fact]
    public void CloudflareR2ResolvesTheOfficialAccountEndpoint()
    {
        var options = new MediaOptions
        {
            Enabled = true,
            Provider = MediaObjectStoreProvider.CloudflareR2,
            Bucket = "activitypub-media",
            Region = "auto",
            ForcePathStyle = true,
            CloudflareAccountId = "0123456789abcdef0123456789abcdef",
            CloudflareJurisdiction = CloudflareR2Jurisdiction.Eu
        };

        options.Validate(isProduction: true);

        Assert.Equal(
            "https://0123456789abcdef0123456789abcdef.eu.r2.cloudflarestorage.com/",
            options.ResolveServiceUri().AbsoluteUri);
    }

    [Theory]
    [InlineData("not-an-account-id")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("")]
    public void CloudflareR2RejectsInvalidAccountIds(string accountId)
    {
        var options = new MediaOptions
        {
            Enabled = true,
            Provider = MediaObjectStoreProvider.CloudflareR2,
            Bucket = "activitypub-media",
            Region = "auto",
            CloudflareAccountId = accountId
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(isProduction: false));
    }

    [Fact]
    public void CloudflareR2RejectsAnOperatorSuppliedEndpoint()
    {
        var options = new MediaOptions
        {
            Enabled = true,
            Provider = MediaObjectStoreProvider.CloudflareR2,
            Bucket = "activitypub-media",
            ServiceUrl = "https://attacker.example",
            Region = "auto",
            CloudflareAccountId = "0123456789abcdef0123456789abcdef"
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(isProduction: true));
    }
}
