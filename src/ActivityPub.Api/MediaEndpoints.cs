using System.Net.Http.Headers;
using System.Security.Claims;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Signatures;
using ActivityPub.Media;

namespace ActivityPub.Server;

internal static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints, MediaOptions options)
    {
        if (!options.Enabled)
        {
            endpoints.MapPost("/api/media", MediaUnavailableAsync)
                .RequireAuthorization("activitypub.write")
                .RequireRateLimiting("local-api")
                .DisableAntiforgery();
            endpoints.MapGet("/media/{id:guid}", MediaUnavailableAsync)
                .RequireRateLimiting("federation-get");
            endpoints.MapGet("/media/proxy/{objectId:guid}/{sourceToken}", MediaUnavailableAsync)
                .RequireRateLimiting("federation-get");
            endpoints.MapGet("/media/proxy/actor/{actorId}/{sourceToken}", MediaUnavailableAsync)
                .RequireRateLimiting("federation-get");
            return endpoints;
        }

        endpoints.MapPost("/api/media", UploadAsync)
            .RequireAuthorization("activitypub.write")
            .RequireRateLimiting("local-api")
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(options.MaximumUploadBytes));
        endpoints.MapGet("/media/{id:guid}", DownloadAsync).RequireRateLimiting("federation-get");
        endpoints.MapGet("/media/proxy/{objectId:guid}/{sourceToken}", ProxyDownloadAsync)
            .RequireRateLimiting("federation-get");
        endpoints.MapGet("/media/proxy/actor/{actorId}/{sourceToken}", ActorProxyDownloadAsync)
            .RequireRateLimiting("federation-get");
        return endpoints;
    }

    private static Task MediaUnavailableAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "300";
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        IMediaService mediaService,
        IFederationQueryStore queryStore,
        MediaOptions options,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        string? username = context.User.FindFirstValue("preferred_username");
        if (username is null)
        {
            return Results.Forbid();
        }

        ActorDocument? actor = await queryStore.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            return Results.Forbid();
        }

        IFormCollection form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        IFormFile? file = form.Files.GetFile("file");
        if (file is null || file.Length is <= 0 || file.Length > options.MaximumUploadBytes)
        {
            return Results.BadRequest(new { error = "A non-empty file within the configured size limit is required." });
        }

        if (!Enum.TryParse(form["visibility"].FirstOrDefault() ?? nameof(Visibility.Public), ignoreCase: true, out Visibility visibility))
        {
            return Results.BadRequest(new { error = "Unknown media visibility." });
        }

        await using Stream content = file.OpenReadStream();
        MediaUploadResult result = await mediaService.UploadAsync(
            new MediaUploadCommand(actor.Iri, file.FileName, file.ContentType, visibility, content),
            cancellationToken).ConfigureAwait(false);
        string location = $"/media/{result.Id}";
        return Results.Created(location, new
        {
            id = result.Id,
            url = location,
            mediaType = result.MediaType,
            result.Length,
            result.Width,
            result.Height,
            result.DurationMilliseconds
        });
    }

    private static async Task DownloadAsync(
        HttpContext context,
        Guid id,
        IMediaService mediaService,
        IFederationQueryStore queryStore,
        IHttpSignatureVerifier signatureVerifier,
        CancellationToken cancellationToken)
    {
        MediaDownload? media = await mediaService.OpenReadAsync(id, null, cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            string? requester = null;
            string? username = context.User.FindFirstValue("preferred_username");
            if (username is not null)
            {
                requester = (await queryStore.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false))?.Iri;
            }
            else if (context.Request.Headers.ContainsKey("Signature") || context.Request.Headers.ContainsKey("Signature-Input"))
            {
                requester = (await signatureVerifier.VerifyAsync(context, [], cancellationToken).ConfigureAwait(false)).KeyOwnerIri;
            }

            if (requester is not null)
            {
                media = await mediaService.OpenReadAsync(id, requester, cancellationToken).ConfigureAwait(false);
            }
        }

        if (media is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await WriteMediaAsync(context, media, "public, max-age=31536000, immutable", cancellationToken).ConfigureAwait(false);
    }

    private static async Task ProxyDownloadAsync(
        HttpContext context,
        Guid objectId,
        string sourceToken,
        IRemoteMediaProxyService proxyService,
        IFederationQueryStore queryStore,
        IHttpSignatureVerifier signatureVerifier,
        MediaOptions options,
        CancellationToken cancellationToken)
    {
        string? requester = null;
        string? username = context.User.FindFirstValue("preferred_username");
        if (username is not null)
        {
            requester = (await queryStore.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false))?.Iri;
        }
        else if (context.Request.Headers.ContainsKey("Signature") || context.Request.Headers.ContainsKey("Signature-Input"))
        {
            requester = (await signatureVerifier.VerifyAsync(context, [], cancellationToken).ConfigureAwait(false)).KeyOwnerIri;
        }

        MediaDownload? media = await proxyService.OpenReadAsync(
            objectId,
            sourceToken,
            requester,
            cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await WriteMediaAsync(context, media, ProxyCacheControl(options), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ActorProxyDownloadAsync(
        HttpContext context,
        string actorId,
        string sourceToken,
        IExternalEntityIdService externalIds,
        IRemoteMediaProxyService proxyService,
        MediaOptions options,
        CancellationToken cancellationToken)
    {
        if (!RemoteMediaSourceToken.TryNormalize(sourceToken, out string normalizedSourceToken))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Guid? remoteActorId = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            actorId,
            cancellationToken).ConfigureAwait(false);
        if (remoteActorId is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        RemoteMediaOpenResult result = await proxyService.OpenActorReadAsync(
            remoteActorId.Value,
            normalizedSourceToken,
            cancellationToken).ConfigureAwait(false);
        if (result.Status == RemoteMediaOpenStatus.NotFound)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (result.Status == RemoteMediaOpenStatus.Unavailable || result.Download is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.CacheControl = "no-store";
            DateTimeOffset retryAfter = result.RetryAfter ?? DateTimeOffset.UtcNow.AddMinutes(1);
            int seconds = Math.Max(1, (int)Math.Ceiling((retryAfter - DateTimeOffset.UtcNow).TotalSeconds));
            context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return;
        }

        await WriteMediaAsync(context, result.Download, ProxyCacheControl(options), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteMediaAsync(
        HttpContext context,
        MediaDownload media,
        string publicCacheControl,
        CancellationToken cancellationToken)
    {
        await using (media.Content)
        {
            if (media.EntityTag is not null)
            {
                context.Response.Headers.ETag = media.EntityTag;
            }

            if (media.LastModified is not null)
            {
                context.Response.Headers.LastModified = media.LastModified.Value.ToUniversalTime()
                    .ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }

            context.Response.Headers.CacheControl = media.IsPublic ? publicCacheControl : "private, no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (IsNotModified(context.Request, media))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            context.Response.ContentType = media.MediaType;
            context.Response.ContentLength = media.Length;
            context.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
            {
                FileNameStar = media.FileName
            }.ToString();
            await media.Content.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsNotModified(HttpRequest request, MediaDownload media)
    {
        if (media.EntityTag is not null && request.Headers.TryGetValue("If-None-Match", out Microsoft.Extensions.Primitives.StringValues values))
        {
            foreach (string? value in values)
            {
                foreach (string candidate in value?.Split(',') ?? [])
                {
                    string normalized = candidate.Trim();
                    if (normalized == "*" || string.Equals(normalized, media.EntityTag, StringComparison.Ordinal) ||
                        normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(normalized[2..], media.EntityTag, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return media.LastModified is not null &&
            request.Headers.TryGetValue("If-Modified-Since", out Microsoft.Extensions.Primitives.StringValues modifiedValues) &&
            DateTimeOffset.TryParse(
                modifiedValues.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTimeOffset modifiedSince) &&
            media.LastModified.Value.ToUniversalTime().ToUnixTimeSeconds() <= modifiedSince.ToUniversalTime().ToUnixTimeSeconds();
    }

    private static string ProxyCacheControl(MediaOptions options)
    {
        long seconds = Math.Max(0, Math.Min(86_400, (long)options.RemoteMediaCacheRetention.TotalSeconds));
        return $"public, max-age={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
