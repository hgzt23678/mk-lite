using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using Amazon.S3;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Media;

internal sealed class RemoteMediaProxyService(
    IRemoteMediaCacheRepository cache,
    IRemoteActorMediaCacheRepository actorCache,
    IMediaService mediaService,
    ISafeFederationHttpClient httpClient,
    IDomainPolicyService policyService,
    MediaOptions options,
    IClock clock,
    ILogger<RemoteMediaProxyService> logger) : IRemoteMediaProxyService
{
    private static readonly Action<ILogger, string, string, string, string, Exception?> LogRemoteMediaFetchFailure =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Warning,
            new EventId(7101, nameof(LogRemoteMediaFetchFailure)),
            "Remote media fetch failed for owner domain {OwnerDomain} and source domain {SourceDomain}; category={Category}, exception={ExceptionType}.");

    private static readonly HashSet<string> AcceptedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        "video/mp4",
        "video/webm",
        "audio/mpeg",
        "audio/ogg",
        "audio/mp4"
    };

    private static readonly HashSet<string> AcceptedActorImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp"
    };

    public async Task<MediaDownload?> OpenReadAsync(
        Guid objectId,
        string sourceToken,
        string? requesterActorIri,
        CancellationToken cancellationToken)
    {
        if (sourceToken.Length != 32 || sourceToken.Any(character => !char.IsAsciiHexDigit(character)))
        {
            return null;
        }

        DateTimeOffset now = clock.UtcNow;
        RemoteMediaSource? source = await cache.ResolveAuthorizedSourceAsync(
            objectId,
            sourceToken.ToLowerInvariant(),
            requesterActorIri,
            cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return null;
        }

        FederationPolicyKind policy = await policyService.GetEffectivePolicyAsync(
            new Uri(source.OwnerActorIri).IdnHost,
            source.OwnerActorIri,
            cancellationToken).ConfigureAwait(false);
        if (policy is FederationPolicyKind.Reject or FederationPolicyKind.RejectMedia)
        {
            return null;
        }

        RemoteMediaCacheEntry? cached = await cache.FindFreshAsync(
            objectId,
            source.SourceToken,
            now,
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return await mediaService.OpenReadAsync(cached.MediaId, requesterActorIri, cancellationToken).ConfigureAwait(false);
        }

        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri(source.SourceIri),
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AcceptedMediaTypes,
            options.MaximumRemoteMediaBytes,
            IsSourceDomainAllowedAsync);
        SafeFederationResponse response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is UnsafeFederationTargetException or FederationTargetPolicyException or HttpRequestException)
        {
            LogFailure(source.OwnerActorIri, source.SourceIri, RemoteMediaCacheFailureKind.Unavailable, exception);
            return null;
        }
        if (response.StatusCode != HttpStatusCode.OK || response.MediaType is null)
        {
            return null;
        }

        string fileName = Path.GetFileName(response.FinalUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "remote-media";
        }

        await using var content = new MemoryStream(response.Body, writable: false);
        MediaUploadResult uploaded = await mediaService.UploadAsync(
            new MediaUploadCommand(
                source.OwnerActorIri,
                fileName,
                response.MediaType,
                source.Visibility,
                content),
            cancellationToken).ConfigureAwait(false);
        var entry = RemoteMediaCacheEntry.Create(
            source.ObjectId,
            source.SourceIri,
            source.SourceToken,
            uploaded.Id,
            response.ETag,
            response.LastModified,
            now,
            now.Add(options.RemoteMediaCacheRetention));
        await cache.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
        RemoteMediaCacheEntry current = await cache.FindFreshAsync(
            objectId,
            source.SourceToken,
            now,
            cancellationToken).ConfigureAwait(false) ?? entry;
        return await mediaService.OpenReadAsync(current.MediaId, requesterActorIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteMediaOpenResult> OpenActorReadAsync(
        Guid remoteActorId,
        string sourceToken,
        CancellationToken cancellationToken)
    {
        if (remoteActorId == Guid.Empty || !RemoteMediaSourceToken.TryNormalize(sourceToken, out string token))
        {
            return RemoteMediaOpenResult.NotFound();
        }

        RemoteActorMediaSource? source = await actorCache.ResolveSourceAsync(
            remoteActorId,
            token,
            cancellationToken).ConfigureAwait(false);
        if (source is null || !await IsMediaAllowedAsync(source, cancellationToken).ConfigureAwait(false))
        {
            return RemoteMediaOpenResult.NotFound();
        }

        long waitStarted = Stopwatch.GetTimestamp();
        TimeSpan pollDelay = TimeSpan.FromMilliseconds(100);
        while (true)
        {
            DateTimeOffset now = clock.UtcNow;
            RemoteActorMediaCacheClaim? claim = await actorCache.ClaimFetchAsync(
                source,
                now,
                now.Add(options.RemoteMediaFetchLeaseDuration),
                cancellationToken).ConfigureAwait(false);
            if (claim is null)
            {
                return RemoteMediaOpenResult.NotFound();
            }

            switch (claim.State)
            {
                case RemoteActorMediaCacheClaimState.Fresh:
                    return await OpenCachedActorMediaAsync(claim, cancellationToken).ConfigureAwait(false);
                case RemoteActorMediaCacheClaimState.Failed:
                    return FailureResult(claim);
                case RemoteActorMediaCacheClaimState.Acquired:
                    return await FetchActorMediaAsync(claim, cancellationToken).ConfigureAwait(false);
                case RemoteActorMediaCacheClaimState.Busy:
                    if (Stopwatch.GetElapsedTime(waitStarted) >= options.RemoteMediaFetchWaitTimeout)
                    {
                        return RemoteMediaOpenResult.Unavailable(now.Add(options.RemoteMediaFailureRetryDelay));
                    }

                    await Task.Delay(pollDelay, cancellationToken).ConfigureAwait(false);
                    pollDelay = TimeSpan.FromMilliseconds(Math.Min(pollDelay.TotalMilliseconds * 2, 1_000));
                    break;
                default:
                    throw new InvalidOperationException("Unknown remote actor media cache state.");
            }
        }
    }

    private async Task<RemoteMediaOpenResult> FetchActorMediaAsync(
        RemoteActorMediaCacheClaim claim,
        CancellationToken cancellationToken)
    {
        if (claim.LeaseOwner is null)
        {
            throw new InvalidOperationException("An acquired remote actor media cache claim has no lease owner.");
        }

        using var heartbeatStop = new CancellationTokenSource();
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeat = RenewActorMediaLeaseAsync(claim, work, heartbeatStop.Token);
        try
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (claim.MediaId is not null && !string.IsNullOrWhiteSpace(claim.RemoteETag))
            {
                headers["If-None-Match"] = claim.RemoteETag;
            }

            if (claim.MediaId is not null && claim.RemoteLastModified is not null)
            {
                headers["If-Modified-Since"] = claim.RemoteLastModified.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }

            var request = new SafeFederationRequest(
                HttpMethod.Get,
                new Uri(claim.Source.SourceIri),
                null,
                null,
                headers,
                AcceptedActorImageTypes,
                options.MaximumRemoteMediaBytes,
                IsSourceDomainAllowedAsync);
            SafeFederationResponse response = await httpClient.SendAsync(request, work.Token).ConfigureAwait(false);
            DateTimeOffset now = clock.UtcNow;
            if (response.StatusCode == HttpStatusCode.NotModified && claim.MediaId is not null)
            {
                bool completed = await actorCache.CompleteAsync(
                    claim.EntryId,
                    claim.LeaseOwner,
                    claim.MediaId.Value,
                    response.ETag ?? claim.RemoteETag,
                    response.LastModified ?? claim.RemoteLastModified,
                    now,
                    now.Add(options.RemoteMediaCacheRetention),
                    work.Token).ConfigureAwait(false);
                return completed
                    ? await OpenMediaAsync(claim.MediaId.Value, work.Token).ConfigureAwait(false)
                    : await ReadAfterLostLeaseAsync(claim, work.Token).ConfigureAwait(false);
            }

            if (response.StatusCode != HttpStatusCode.OK || response.MediaType is null ||
                !AcceptedActorImageTypes.Contains(response.MediaType))
            {
                RemoteMediaCacheFailureKind failure = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                    ? RemoteMediaCacheFailureKind.NotFound
                    : RemoteMediaCacheFailureKind.Unavailable;
                DateTimeOffset retryAfter = FutureRetryAfter(response.RetryAfter, now);
                _ = await actorCache.FailAsync(
                    claim.EntryId,
                    claim.LeaseOwner,
                    failure,
                    now,
                    retryAfter,
                    work.Token).ConfigureAwait(false);
                return failure == RemoteMediaCacheFailureKind.NotFound
                    ? RemoteMediaOpenResult.NotFound()
                    : RemoteMediaOpenResult.Unavailable(retryAfter);
            }

            string fileName = Path.GetFileName(response.FinalUri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = claim.Source.Kind == RemoteActorMediaKind.Avatar ? "remote-avatar" : "remote-banner";
            }

            await using var content = new MemoryStream(response.Body, writable: false);
            MediaUploadResult uploaded = await mediaService.UploadAsync(
                new MediaUploadCommand(
                    claim.Source.ActorIri,
                    fileName,
                    response.MediaType,
                    Visibility.Public,
                    content),
                work.Token).ConfigureAwait(false);
            now = clock.UtcNow;
            bool saved = await actorCache.CompleteAsync(
                claim.EntryId,
                claim.LeaseOwner,
                uploaded.Id,
                response.ETag,
                response.LastModified,
                now,
                now.Add(options.RemoteMediaCacheRetention),
                work.Token).ConfigureAwait(false);
            return saved
                ? await OpenMediaAsync(uploaded.Id, work.Token).ConfigureAwait(false)
                : await ReadAfterLostLeaseAsync(claim, work.Token).ConfigureAwait(false);
        }
        catch (UnsafeFederationTargetException exception)
        {
            LogFailure(claim, RemoteMediaCacheFailureKind.Unsafe, exception);
            return await FailActorMediaAsync(claim, RemoteMediaCacheFailureKind.Unsafe, cancellationToken).ConfigureAwait(false);
        }
        catch (FederationTargetPolicyException exception)
        {
            LogFailure(claim, RemoteMediaCacheFailureKind.Unsafe, exception);
            return await FailActorMediaAsync(claim, RemoteMediaCacheFailureKind.Unsafe, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            LogFailure(claim, RemoteMediaCacheFailureKind.Unsafe, exception);
            return await FailActorMediaAsync(claim, RemoteMediaCacheFailureKind.Unsafe, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(claim, RemoteMediaCacheFailureKind.Unavailable, exception);
            return await FailActorMediaAsync(claim, RemoteMediaCacheFailureKind.Unavailable, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or Win32Exception or AmazonS3Exception)
        {
            LogFailure(claim, RemoteMediaCacheFailureKind.Unavailable, exception);
            return await FailActorMediaAsync(claim, RemoteMediaCacheFailureKind.Unavailable, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            heartbeatStop.Cancel();
            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the request finishes before the next renewal.
            }
        }
    }

    private async Task RenewActorMediaLeaseAsync(
        RemoteActorMediaCacheClaim claim,
        CancellationTokenSource work,
        CancellationToken stoppingToken)
    {
        while (true)
        {
            await Task.Delay(options.RemoteMediaFetchLeaseRenewalInterval, stoppingToken).ConfigureAwait(false);
            DateTimeOffset now = clock.UtcNow;
            bool renewed;
            try
            {
                renewed = await actorCache.RenewLeaseAsync(
                    claim.EntryId,
                    claim.LeaseOwner!,
                    now,
                    now.Add(options.RemoteMediaFetchLeaseDuration),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                renewed = false;
            }

            if (!renewed)
            {
                work.Cancel();
                return;
            }
        }
    }

    private async Task<bool> IsMediaAllowedAsync(
        RemoteActorMediaSource source,
        CancellationToken cancellationToken)
    {
        FederationPolicyKind actorPolicy = await policyService.GetEffectivePolicyAsync(
            new Uri(source.ActorIri).IdnHost,
            source.ActorIri,
            cancellationToken).ConfigureAwait(false);
        if (actorPolicy is FederationPolicyKind.Reject or FederationPolicyKind.RejectMedia)
        {
            return false;
        }

        return await IsSourceDomainAllowedAsync(new Uri(source.SourceIri), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsSourceDomainAllowedAsync(Uri target, CancellationToken cancellationToken)
    {
        FederationPolicyKind sourcePolicy = await policyService.GetEffectivePolicyAsync(
            target.IdnHost,
            null,
            cancellationToken).ConfigureAwait(false);
        return sourcePolicy is not (FederationPolicyKind.Reject or FederationPolicyKind.RejectMedia);
    }

    private async Task<RemoteMediaOpenResult> ReadAfterLostLeaseAsync(
        RemoteActorMediaCacheClaim claim,
        CancellationToken cancellationToken)
    {
        RemoteActorMediaCacheClaim? current = await actorCache.ReadAsync(
            claim.Source.RemoteActorId,
            claim.Source.SourceToken,
            clock.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return current?.State == RemoteActorMediaCacheClaimState.Fresh
            ? await OpenCachedActorMediaAsync(current, cancellationToken).ConfigureAwait(false)
            : RemoteMediaOpenResult.Unavailable();
    }

    private async Task<RemoteMediaOpenResult> OpenCachedActorMediaAsync(
        RemoteActorMediaCacheClaim claim,
        CancellationToken cancellationToken)
    {
        if (claim.MediaId is null)
        {
            return RemoteMediaOpenResult.Unavailable();
        }

        try
        {
            return await OpenMediaAsync(claim.MediaId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or Win32Exception or AmazonS3Exception)
        {
            LogFailure(claim, RemoteMediaCacheFailureKind.Unavailable, exception);
            return RemoteMediaOpenResult.Unavailable(clock.UtcNow.Add(options.RemoteMediaFailureRetryDelay));
        }
    }

    private async Task<RemoteMediaOpenResult> OpenMediaAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        MediaDownload? download = await mediaService.OpenReadAsync(mediaId, null, cancellationToken).ConfigureAwait(false);
        return download is null ? RemoteMediaOpenResult.Unavailable() : RemoteMediaOpenResult.Success(download);
    }

    private async Task<RemoteMediaOpenResult> FailActorMediaAsync(
        RemoteActorMediaCacheClaim claim,
        RemoteMediaCacheFailureKind failureKind,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset retryAfter = now.Add(options.RemoteMediaFailureRetryDelay);
        _ = await actorCache.FailAsync(
            claim.EntryId,
            claim.LeaseOwner!,
            failureKind,
            now,
            retryAfter,
            cancellationToken).ConfigureAwait(false);
        return failureKind is RemoteMediaCacheFailureKind.NotFound or RemoteMediaCacheFailureKind.Unsafe
            ? RemoteMediaOpenResult.NotFound()
            : RemoteMediaOpenResult.Unavailable(retryAfter);
    }

    private static RemoteMediaOpenResult FailureResult(RemoteActorMediaCacheClaim claim) =>
        claim.FailureKind is RemoteMediaCacheFailureKind.NotFound or RemoteMediaCacheFailureKind.Unsafe
            ? RemoteMediaOpenResult.NotFound()
            : RemoteMediaOpenResult.Unavailable(claim.RetryAfter);

    private DateTimeOffset FutureRetryAfter(DateTimeOffset? value, DateTimeOffset now) =>
        value is not null && value.Value > now
            ? value.Value
            : now.Add(options.RemoteMediaFailureRetryDelay);

    private void LogFailure(
        RemoteActorMediaCacheClaim claim,
        RemoteMediaCacheFailureKind failureKind,
        Exception exception) =>
        LogFailure(claim.Source.ActorIri, claim.Source.SourceIri, failureKind, exception);

    private void LogFailure(
        string ownerIri,
        string sourceIri,
        RemoteMediaCacheFailureKind failureKind,
        Exception exception) =>
        LogRemoteMediaFetchFailure(
            logger,
            new Uri(ownerIri).IdnHost,
            new Uri(sourceIri).IdnHost,
            failureKind.ToString(),
            exception.GetType().Name,
            null);
}
