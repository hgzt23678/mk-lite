using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace ActivityPub.Media.Tests;

public sealed class CloudflareR2ObjectStoreTests
{
    private static readonly MediaOptions R2Options = new()
    {
        Enabled = true,
        Provider = MediaObjectStoreProvider.CloudflareR2,
        Bucket = "activitypub-media",
        Region = "auto",
        ForcePathStyle = true,
        UseServerSideEncryption = true,
        CloudflareAccountId = "0123456789abcdef0123456789abcdef"
    };

    [Fact]
    public void ClientUsesR2SigningAndChecksumCompatibilitySettings()
    {
        Amazon.S3.AmazonS3Config configuration = MediaServiceCollectionExtensions.CreateAmazonS3Config(R2Options);

        Assert.Equal("https://0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com/", configuration.ServiceURL);
        Assert.Equal("auto", configuration.AuthenticationRegion);
        Assert.True(configuration.ForcePathStyle);
        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, configuration.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, configuration.ResponseChecksumValidation);
    }

    [Fact]
    public void UploadOmitsUnsupportedSseAndSdkChecksumHeadersForR2()
    {
        using var content = new MemoryStream([1, 2, 3]);

        PutObjectRequest request = S3MediaObjectStore.CreatePutObjectRequest(
            "media/example/original.png",
            content,
            "image/png",
            R2Options);

        Assert.Null(request.ServerSideEncryptionMethod);
        Assert.True(request.DisablePayloadSigning);
        Assert.True(request.DisableDefaultChecksumValidation);
        Assert.Equal("private, no-store", request.Headers.CacheControl);
    }

    [Fact]
    public void GenericS3RetainsExplicitServerSideEncryption()
    {
        using var content = new MemoryStream([1, 2, 3]);
        var options = new MediaOptions
        {
            Enabled = true,
            Provider = MediaObjectStoreProvider.S3Compatible,
            Bucket = "activitypub-media",
            UseServerSideEncryption = true
        };

        PutObjectRequest request = S3MediaObjectStore.CreatePutObjectRequest("object", content, "image/png", options);

        Assert.Equal(ServerSideEncryptionMethod.AES256, request.ServerSideEncryptionMethod);
        Assert.False(request.DisablePayloadSigning);
        Assert.False(request.DisableDefaultChecksumValidation);
    }
}
