using Amazon.S3;
using Amazon.S3.Model;

namespace ActivityPub.Media;

internal interface IMediaObjectStore
{
    Task PutAsync(string key, Stream content, string mediaType, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

internal sealed class S3MediaObjectStore(IAmazonS3 client, MediaOptions options) : IMediaObjectStore
{
    public async Task PutAsync(string key, Stream content, string mediaType, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        PutObjectRequest request = CreatePutObjectRequest(key, content, mediaType, options);
        await client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal static PutObjectRequest CreatePutObjectRequest(
        string key,
        Stream content,
        string mediaType,
        MediaOptions options)
    {
        var request = new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = mediaType,
            AutoCloseStream = false,
            // R2 encrypts every object at rest, but rejects this generic S3 SSE header.
            ServerSideEncryptionMethod = options.Provider == MediaObjectStoreProvider.S3Compatible &&
                options.UseServerSideEncryption
                    ? ServerSideEncryptionMethod.AES256
                    : null,
            // Cloudflare's AWS SDK for .NET guidance requires unsigned HTTPS payloads and
            // disables the default SDK checksum that R2 does not implement.
            DisablePayloadSigning = options.Provider == MediaObjectStoreProvider.CloudflareR2,
            DisableDefaultChecksumValidation = options.Provider == MediaObjectStoreProvider.CloudflareR2
        };
        request.Headers.CacheControl = "private, no-store";
        return request;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        GetObjectResponse response = await client.GetObjectAsync(options.Bucket, key, cancellationToken).ConfigureAwait(false);
        return new ResponseOwnedStream(response);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        _ = await client.DeleteObjectAsync(options.Bucket, key, cancellationToken).ConfigureAwait(false);

    private sealed class ResponseOwnedStream(GetObjectResponse response) : Stream
    {
        private readonly Stream inner = response.ResponseStream;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            response.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
