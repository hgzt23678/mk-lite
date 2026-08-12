using System.Net;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using ActivityPub.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActivityPub.Media.Tests;

public sealed class RemoteMediaProxyServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RejectMediaPolicyPreventsAnyRemoteFetch()
    {
        var cache = new SourceCache();
        var http = new RecordingHttpClient();
        var service = new RemoteMediaProxyService(
            cache,
            new NoopActorCache(),
            new NoopMediaService(),
            http,
            new RejectMediaPolicy(),
            new MediaOptions
            {
                MaximumRemoteMediaBytes = 1_024,
                RemoteMediaCacheRetention = TimeSpan.FromDays(1)
            },
            new FixedClock(),
            NullLogger<RemoteMediaProxyService>.Instance);

        MediaDownload? result = await service.OpenReadAsync(
            Guid.NewGuid(),
            new string('a', 32),
            null,
            CancellationToken.None);

        Assert.Null(result);
        Assert.False(http.WasCalled);
    }

    [Fact]
    public async Task ActorRejectMediaPolicyPreventsClaimAndRemoteFetch()
    {
        string sourceIri = "https://blocked.example/media/avatar.png";
        var actorCache = new ActorCache(sourceIri);
        var http = new RecordingHttpClient();
        var service = CreateService(actorCache, new NoopMediaService(), http, new RejectMediaPolicy());

        RemoteMediaOpenResult result = await service.OpenActorReadAsync(
            actorCache.ActorId,
            RemoteMediaSourceToken.Create(sourceIri),
            CancellationToken.None);

        Assert.Equal(RemoteMediaOpenStatus.NotFound, result.Status);
        Assert.Equal(0, actorCache.ClaimCount);
        Assert.False(http.WasCalled);
    }

    [Fact]
    public async Task MalformedActorSourceTokenIsRejectedBeforeCacheLookup()
    {
        var actorCache = new ActorCache("https://remote.example/media/avatar.png");
        var http = new RecordingHttpClient();
        var service = CreateService(actorCache, new NoopMediaService(), http, new AllowPolicy());

        RemoteMediaOpenResult result = await service.OpenActorReadAsync(
            actorCache.ActorId,
            "not-a-sha256-token",
            CancellationToken.None);

        Assert.Equal(RemoteMediaOpenStatus.NotFound, result.Status);
        Assert.Equal(0, actorCache.ResolveCount);
        Assert.False(http.WasCalled);
    }

    [Fact]
    public async Task SourceCdnRejectMediaPolicyPreventsClaimAndRemoteFetch()
    {
        string sourceIri = "https://cdn.blocked.example/media/avatar.png";
        var actorCache = new ActorCache(sourceIri);
        var http = new RecordingHttpClient();
        var service = CreateService(actorCache, new NoopMediaService(), http, new RejectCdnPolicy());

        RemoteMediaOpenResult result = await service.OpenActorReadAsync(
            actorCache.ActorId,
            RemoteMediaSourceToken.Create(sourceIri),
            CancellationToken.None);

        Assert.Equal(RemoteMediaOpenStatus.NotFound, result.Status);
        Assert.Equal(0, actorCache.ClaimCount);
        Assert.False(http.WasCalled);
    }

    [Fact]
    public async Task ActorFetchUsesImageOnlyContractAndPersistsDurableCacheMetadata()
    {
        string sourceIri = "https://remote.example/media/avatar.png";
        var actorCache = new ActorCache(sourceIri);
        var media = new RecordingMediaService();
        var response = new SafeFederationResponse(
            HttpStatusCode.OK,
            new Uri(sourceIri),
            "image/png",
            [0x89, 0x50, 0x4e, 0x47],
            "\"remote-etag\"",
            Now.AddHours(-1),
            null);
        var http = new RecordingHttpClient(response);
        var service = CreateService(actorCache, media, http, new AllowPolicy());

        RemoteMediaOpenResult result = await service.OpenActorReadAsync(
            actorCache.ActorId,
            RemoteMediaSourceToken.Create(sourceIri),
            CancellationToken.None);

        Assert.Equal(RemoteMediaOpenStatus.Success, result.Status);
        Assert.NotNull(result.Download);
        Assert.Equal(1, actorCache.CompleteCount);
        Assert.Equal(media.UploadedId, actorCache.MediaId);
        Assert.Equal("\"remote-etag\"", actorCache.RemoteETag);
        Assert.Equal(Now.AddHours(-1), actorCache.RemoteLastModified);
        Assert.Equal("https://remote.example/users/alice", media.UploadCommand?.OwnerActorIri);
        Assert.Equal(Visibility.Public, media.UploadCommand?.Visibility);
        Assert.Equal(1_024, http.Request?.MaximumResponseBytes);
        Assert.NotNull(http.Request?.TargetValidator);
        Assert.True(http.Request?.AcceptedMediaTypes.SetEquals(
            ["image/jpeg", "image/png", "image/gif", "image/webp"]));
    }

    [Fact]
    public async Task StaleActorCacheIsRevalidatedWithRemoteValidators()
    {
        string sourceIri = "https://remote.example/media/avatar.png";
        var actorCache = new ActorCache(sourceIri, staleMediaId: Guid.NewGuid());
        var media = new RecordingMediaService();
        var response = new SafeFederationResponse(
            HttpStatusCode.NotModified,
            new Uri(sourceIri),
            null,
            [],
            null,
            null,
            null);
        var http = new RecordingHttpClient(response);
        var service = CreateService(actorCache, media, http, new AllowPolicy());

        RemoteMediaOpenResult result = await service.OpenActorReadAsync(
            actorCache.ActorId,
            RemoteMediaSourceToken.Create(sourceIri),
            CancellationToken.None);

        Assert.Equal(RemoteMediaOpenStatus.Success, result.Status);
        Assert.Equal("\"old-etag\"", http.Request?.Headers["If-None-Match"]);
        Assert.Equal("Sat, 02 Aug 2025 12:00:00 GMT", http.Request?.Headers["If-Modified-Since"]);
        Assert.Equal(0, media.UploadCount);
        Assert.Equal(1, actorCache.CompleteCount);
    }

    [Fact]
    public async Task UnsafeActorMediaTargetIsNegativeCachedWithoutLeakingTheTarget()
    {
        string sourceIri = "https://remote.example/media/avatar.png";
        var actorCache = new ActorCache(sourceIri);
        var service = CreateService(
            actorCache,
            new NoopMediaService(),
            new UnsafeTargetHttpClient(),
            new AllowPolicy());

        RemoteMediaOpenResult result = await service.OpenActorReadAsync(
            actorCache.ActorId,
            RemoteMediaSourceToken.Create(sourceIri),
            CancellationToken.None);

        Assert.Equal(RemoteMediaOpenStatus.NotFound, result.Status);
        Assert.Equal(1, actorCache.FailureCount);
        Assert.Equal(RemoteMediaCacheFailureKind.Unsafe, actorCache.FailureKind);
    }

    [Fact]
    public async Task ConcurrentActorFirstFetchUploadsOnlyOnce()
    {
        string sourceIri = "https://remote.example/media/avatar.png";
        var actorCache = new ActorCache(sourceIri);
        var media = new RecordingMediaService();
        var http = new BlockingHttpClient(new SafeFederationResponse(
            HttpStatusCode.OK,
            new Uri(sourceIri),
            "image/png",
            [0x89, 0x50, 0x4e, 0x47],
            null,
            null,
            null));
        var service = CreateService(actorCache, media, http, new AllowPolicy());

        Task<RemoteMediaOpenResult> first = service.OpenActorReadAsync(
            actorCache.ActorId,
            RemoteMediaSourceToken.Create(sourceIri),
            CancellationToken.None);
        await http.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<RemoteMediaOpenResult> second = service.OpenActorReadAsync(
            actorCache.ActorId,
            RemoteMediaSourceToken.Create(sourceIri),
            CancellationToken.None);
        await Task.Delay(150);
        http.Release.TrySetResult();

        RemoteMediaOpenResult[] results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(RemoteMediaOpenStatus.Success, result.Status));
        Assert.Equal(1, http.CallCount);
        Assert.Equal(1, media.UploadCount);
        Assert.True(actorCache.ClaimCount >= 2);
    }

    private static RemoteMediaProxyService CreateService(
        IRemoteActorMediaCacheRepository actorCache,
        IMediaService media,
        ISafeFederationHttpClient http,
        IDomainPolicyService policy) =>
        new(
            new SourceCache(),
            actorCache,
            media,
            http,
            policy,
            new MediaOptions
            {
                MaximumRemoteMediaBytes = 1_024,
                RemoteMediaCacheRetention = TimeSpan.FromDays(1),
                RemoteMediaFetchLeaseDuration = TimeSpan.FromMinutes(2),
                RemoteMediaFetchLeaseRenewalInterval = TimeSpan.FromSeconds(30),
                RemoteMediaFetchWaitTimeout = TimeSpan.FromSeconds(1),
                RemoteMediaFailureRetryDelay = TimeSpan.FromMinutes(1)
            },
            new FixedClock(),
            NullLogger<RemoteMediaProxyService>.Instance);

    private sealed class SourceCache : IRemoteMediaCacheRepository
    {
        public Task<RemoteMediaSource?> ResolveAuthorizedSourceAsync(
            Guid objectId,
            string sourceToken,
            string? requesterActorIri,
            CancellationToken cancellationToken) =>
            Task.FromResult<RemoteMediaSource?>(new(
                objectId,
                "https://blocked.example/users/alice",
                "https://blocked.example/media/image.png",
                sourceToken,
                Visibility.Public));

        public Task<RemoteMediaCacheEntry?> FindFreshAsync(
            Guid objectId,
            string sourceToken,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult<RemoteMediaCacheEntry?>(null);

        public Task SaveAsync(RemoteMediaCacheEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> ExpireAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class NoopActorCache : IRemoteActorMediaCacheRepository
    {
        public Task<RemoteActorMediaSource?> ResolveSourceAsync(
            Guid remoteActorId,
            string sourceToken,
            CancellationToken cancellationToken) => Task.FromResult<RemoteActorMediaSource?>(null);

        public Task<RemoteActorMediaCacheClaim?> ClaimFetchAsync(
            RemoteActorMediaSource source,
            DateTimeOffset now,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken) => Task.FromResult<RemoteActorMediaCacheClaim?>(null);

        public Task<RemoteActorMediaCacheClaim?> ReadAsync(
            Guid remoteActorId,
            string sourceToken,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult<RemoteActorMediaCacheClaim?>(null);

        public Task<bool> RenewLeaseAsync(
            Guid entryId,
            string leaseOwner,
            DateTimeOffset now,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> CompleteAsync(
            Guid entryId,
            string leaseOwner,
            Guid mediaId,
            string? remoteETag,
            DateTimeOffset? remoteLastModified,
            DateTimeOffset now,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> FailAsync(
            Guid entryId,
            string leaseOwner,
            RemoteMediaCacheFailureKind failureKind,
            DateTimeOffset now,
            DateTimeOffset retryAfter,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> ExpireAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class ActorCache : IRemoteActorMediaCacheRepository
    {
        private readonly object gate = new();
        private readonly RemoteActorMediaSource source;
        private readonly Guid entryId = Guid.NewGuid();
        private readonly string leaseOwner = "actor-cache-worker";
        private bool claimed;
        private bool fresh;

        public ActorCache(string sourceIri, Guid? staleMediaId = null)
        {
            ActorId = Guid.NewGuid();
            source = new(
                ActorId,
                "https://remote.example/users/alice",
                RemoteActorMediaKind.Avatar,
                sourceIri,
                RemoteMediaSourceToken.Create(sourceIri));
            MediaId = staleMediaId;
            if (staleMediaId is not null)
            {
                RemoteETag = "\"old-etag\"";
                RemoteLastModified = new DateTimeOffset(2025, 8, 2, 12, 0, 0, TimeSpan.Zero);
            }
        }

        public Guid ActorId { get; }
        public int ClaimCount { get; private set; }
        public int ResolveCount { get; private set; }
        public int CompleteCount { get; private set; }
        public int FailureCount { get; private set; }
        public Guid? MediaId { get; private set; }
        public string? RemoteETag { get; private set; }
        public DateTimeOffset? RemoteLastModified { get; private set; }
        public RemoteMediaCacheFailureKind? FailureKind { get; private set; }

        public Task<RemoteActorMediaSource?> ResolveSourceAsync(
            Guid remoteActorId,
            string sourceToken,
            CancellationToken cancellationToken)
        {
            ResolveCount++;
            return Task.FromResult<RemoteActorMediaSource?>(
                remoteActorId == ActorId && sourceToken == source.SourceToken ? source : null);
        }

        public Task<RemoteActorMediaCacheClaim?> ClaimFetchAsync(
            RemoteActorMediaSource requestedSource,
            DateTimeOffset now,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                ClaimCount++;
                RemoteActorMediaCacheClaimState state;
                string? owner;
                if (fresh)
                {
                    state = RemoteActorMediaCacheClaimState.Fresh;
                    owner = null;
                }
                else if (claimed)
                {
                    state = RemoteActorMediaCacheClaimState.Busy;
                    owner = leaseOwner;
                }
                else
                {
                    claimed = true;
                    state = RemoteActorMediaCacheClaimState.Acquired;
                    owner = leaseOwner;
                }

                return Task.FromResult<RemoteActorMediaCacheClaim?>(new(
                    entryId,
                    state,
                    source,
                    owner,
                    MediaId,
                    RemoteETag,
                    RemoteLastModified,
                    null,
                    null));
            }
        }

        public Task<RemoteActorMediaCacheClaim?> ReadAsync(
            Guid remoteActorId,
            string sourceToken,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<RemoteActorMediaCacheClaim?>(fresh && MediaId is not null
                ? new(
                    entryId,
                    RemoteActorMediaCacheClaimState.Fresh,
                    source,
                    null,
                    MediaId,
                    RemoteETag,
                    RemoteLastModified,
                    null,
                    null)
                : null);

        public Task<bool> RenewLeaseAsync(
            Guid requestedEntryId,
            string requestedLeaseOwner,
            DateTimeOffset now,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> CompleteAsync(
            Guid requestedEntryId,
            string requestedLeaseOwner,
            Guid mediaId,
            string? remoteETag,
            DateTimeOffset? remoteLastModified,
            DateTimeOffset now,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                CompleteCount++;
                MediaId = mediaId;
                RemoteETag = remoteETag;
                RemoteLastModified = remoteLastModified;
                fresh = true;
                claimed = false;
            }

            return Task.FromResult(true);
        }

        public Task<bool> FailAsync(
            Guid requestedEntryId,
            string requestedLeaseOwner,
            RemoteMediaCacheFailureKind failureKind,
            DateTimeOffset now,
            DateTimeOffset retryAfter,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                FailureCount++;
                FailureKind = failureKind;
                claimed = false;
            }

            return Task.FromResult(true);
        }

        public Task<int> ExpireAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class RejectMediaPolicy : IDomainPolicyService
    {
        public Task<FederationPolicyKind> GetEffectivePolicyAsync(
            string domain,
            string? actorIri,
            CancellationToken cancellationToken) => Task.FromResult(FederationPolicyKind.RejectMedia);

        public Task<IReadOnlySet<string>> FindRejectedActorsAsync(
            IReadOnlyCollection<string> actorIris,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class AllowPolicy : IDomainPolicyService
    {
        public Task<FederationPolicyKind> GetEffectivePolicyAsync(
            string domain,
            string? actorIri,
            CancellationToken cancellationToken) => Task.FromResult(FederationPolicyKind.Allow);

        public Task<IReadOnlySet<string>> FindRejectedActorsAsync(
            IReadOnlyCollection<string> actorIris,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class RejectCdnPolicy : IDomainPolicyService
    {
        public Task<FederationPolicyKind> GetEffectivePolicyAsync(
            string domain,
            string? actorIri,
            CancellationToken cancellationToken) => Task.FromResult(
            domain == "cdn.blocked.example" ? FederationPolicyKind.RejectMedia : FederationPolicyKind.Allow);

        public Task<IReadOnlySet<string>> FindRejectedActorsAsync(
            IReadOnlyCollection<string> actorIris,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class RecordingHttpClient(SafeFederationResponse? response = null) : ISafeFederationHttpClient
    {
        public bool WasCalled { get; private set; }
        public SafeFederationRequest? Request { get; private set; }

        public Task<SafeFederationResponse> SendAsync(
            SafeFederationRequest request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            Request = request;
            return response is null
                ? throw new InvalidOperationException("HTTP must not be called for RejectMedia.")
                : Task.FromResult(response);
        }
    }

    private sealed class UnsafeTargetHttpClient : ISafeFederationHttpClient
    {
        public Task<SafeFederationResponse> SendAsync(
            SafeFederationRequest request,
            CancellationToken cancellationToken) =>
            throw new UnsafeFederationTargetException("The resolved address is private.");
    }

    private sealed class BlockingHttpClient(SafeFederationResponse response) : ISafeFederationHttpClient
    {
        private int callCount;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount => Volatile.Read(ref callCount);

        public async Task<SafeFederationResponse> SendAsync(
            SafeFederationRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return response;
        }
    }

    private sealed class RecordingMediaService : IMediaService
    {
        public Guid UploadedId { get; } = Guid.NewGuid();
        public int UploadCount { get; private set; }
        public MediaUploadCommand? UploadCommand { get; private set; }

        public Task<MediaUploadResult> UploadAsync(MediaUploadCommand command, CancellationToken cancellationToken)
        {
            UploadCount++;
            UploadCommand = command;
            return Task.FromResult(new MediaUploadResult(UploadedId, "image/png", 4, 1, 1, null));
        }

        public Task<MediaDownload?> OpenReadAsync(Guid id, string? requesterActorIri, CancellationToken cancellationToken) =>
            Task.FromResult<MediaDownload?>(new(
                new MemoryStream([0x89, 0x50, 0x4e, 0x47], writable: false),
                "image/png",
                4,
                "avatar.png",
                true,
                "\"local-etag\"",
                Now));
    }

    private sealed class NoopMediaService : IMediaService
    {
        public Task<MediaUploadResult> UploadAsync(MediaUploadCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Upload must not be called for RejectMedia.");

        public Task<MediaDownload?> OpenReadAsync(Guid id, string? requesterActorIri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Read must not be called for RejectMedia.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    }
}
