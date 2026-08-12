using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;

namespace ActivityPub.MastodonApi;

public static class MastodonEndpoints
{
    public static IEndpointRouteBuilder MapMastodonApi(this IEndpointRouteBuilder endpoints, bool oauthEnabled = false)
    {
        RouteGroupBuilder api = endpoints.MapGroup("/api").RequireRateLimiting("local-api");
        api.MapGet("/v1/instance", InstanceV1);
        api.MapGet("/v2/instance", InstanceV2);
        if (oauthEnabled)
        {
            api.MapPost("/v1/apps", RegisterApplicationAsync);
            api.MapGet("/v1/apps/verify_credentials", VerifyApplicationCredentialsAsync)
                .RequireAuthorization("mastodon.read");
        }
        api.MapGet("/v1/accounts/lookup", LookupAccountAsync);
        api.MapGet("/v1/accounts/{id}", AccountAsync);
        api.MapGet("/v1/accounts/{id}/statuses", AccountStatusesAsync);
        api.MapGet("/v1/statuses/{id}", StatusAsync);
        api.MapGet("/v1/timelines/public", PublicTimelineAsync);
        api.MapGet("/v1/accounts/verify_credentials", VerifyCredentialsAsync).RequireAuthorization("mastodon.read");
        api.MapGet("/v1/accounts/relationships", RelationshipsAsync).RequireAuthorization("mastodon.read:follows");
        api.MapGet("/v1/timelines/home", HomeTimelineAsync).RequireAuthorization("mastodon.read");
        api.MapGet("/v1/notifications", NotificationsAsync).RequireAuthorization("mastodon.read:notifications");
        api.MapGet("/v1/notifications/unread_count", NotificationUnreadCountAsync).RequireAuthorization("mastodon.read:notifications");
        api.MapGet("/v1/notifications/{id}", NotificationAsync).RequireAuthorization("mastodon.read:notifications");
        api.MapPost("/v1/notifications/clear", ClearNotificationsAsync).RequireAuthorization("mastodon.write:notifications");
        api.MapPost("/v1/notifications/{id}/dismiss", DismissNotificationAsync).RequireAuthorization("mastodon.write:notifications");
        api.MapGet("/v1/streaming", MastodonStreamingEndpoints.StreamAsync);
        api.MapGet("/v1/streaming/{**any}", MastodonStreamingEndpoints.StreamAsync);
        api.MapPost("/v1/statuses", CreateStatusAsync).RequireAuthorization("mastodon.write");
        api.MapDelete("/v1/statuses/{id}", DeleteStatusAsync).RequireAuthorization("mastodon.write");
        api.MapPost("/v1/statuses/{id}/favourite", FavouriteAsync).RequireAuthorization("mastodon.write:favourites");
        api.MapPost("/v1/statuses/{id}/unfavourite", UnfavouriteAsync).RequireAuthorization("mastodon.write:favourites");
        api.MapPost("/v1/statuses/{id}/reblog", ReblogAsync).RequireAuthorization("mastodon.write");
        api.MapPost("/v1/statuses/{id}/unreblog", UnreblogAsync).RequireAuthorization("mastodon.write");
        api.MapPost("/v1/accounts/{id}/follow", FollowAsync).RequireAuthorization("mastodon.write:follows");
        api.MapPost("/v1/accounts/{id}/unfollow", UnfollowAsync).RequireAuthorization("mastodon.write:follows");
        api.MapPost("/v1/accounts/{id}/mute", MuteAsync).RequireAuthorization("mastodon.write:mutes");
        api.MapPost("/v1/accounts/{id}/unmute", UnmuteAsync).RequireAuthorization("mastodon.write:mutes");
        api.MapPost("/v1/accounts/{id}/block", BlockAsync).RequireAuthorization("mastodon.write:blocks");
        api.MapPost("/v1/accounts/{id}/unblock", UnblockAsync).RequireAuthorization("mastodon.write:blocks");
        return endpoints;
    }

    private static async Task<IResult> VerifyApplicationCredentialsAsync(
        ClaimsPrincipal principal,
        IMastodonOAuthApplicationService applications,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? clientId = principal.GetPresenters().SingleOrDefault();
        if (clientId is null)
        {
            return Results.Json(new { error = "The access token is not associated with an application." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        MastodonOAuthApplication? application = await applications.FindAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            return Results.Json(new { error = "The application no longer exists." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Application,
            application.InternalId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return Results.Json(new
        {
            id,
            name = application.Name,
            website = application.Website,
            redirect_uri = string.Join('\n', application.RedirectUris),
            client_id = application.ClientId
        });
    }

    private static async Task<IResult> RegisterApplicationAsync(
        HttpContext context,
        IMastodonOAuthApplicationService applications,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        (string name, string? website, string redirectUris, string scopes) =
            await ReadApplicationRegistrationAsync(context.Request, cancellationToken).ConfigureAwait(false);
        MastodonOAuthApplicationCredentials credentials = await applications.RegisterAsync(
            new MastodonOAuthApplicationRegistration(
                name,
                website,
                redirectUris.Split(['\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            cancellationToken).ConfigureAwait(false);
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Application,
            credentials.InternalId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return Results.Json(new
        {
            id,
            name = credentials.Name,
            website = credentials.Website,
            redirect_uri = string.Join('\n', credentials.RedirectUris),
            client_id = credentials.ClientId,
            client_secret = credentials.ClientSecret,
            vapid_key = (string?)null
        });
    }

    private static async Task<IResult> InstanceV1(
        FederationOptions options,
        IFederationQueryStore store,
        CancellationToken cancellationToken)
    {
        NodeInfoCounts counts = await store.GetNodeInfoCountsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(new
        {
            uri = options.PublicBaseUri.IdnHost,
            title = options.PublicBaseUri.IdnHost,
            short_description = "ActivityPub .NET server",
            description = "ActivityPub .NET server",
            email = string.Empty,
            version = typeof(MastodonEndpoints).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            urls = new { streaming_api = options.PublicBaseUri.AbsoluteUri.TrimEnd('/') },
            stats = new { user_count = counts.LocalUsers, status_count = counts.LocalPosts, domain_count = counts.RemoteDomains },
            thumbnail = (string?)null,
            languages = Array.Empty<string>(),
            registrations = false,
            approval_required = true,
            invites_enabled = false,
            configuration = Configuration(),
            contact_account = (object?)null,
            rules = Array.Empty<object>()
        });
    }

    private static async Task<IResult> InstanceV2(
        FederationOptions options,
        IFederationQueryStore store,
        CancellationToken cancellationToken)
    {
        NodeInfoCounts counts = await store.GetNodeInfoCountsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(new
        {
            domain = options.PublicBaseUri.IdnHost,
            title = options.PublicBaseUri.IdnHost,
            version = typeof(MastodonEndpoints).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            description = "ActivityPub .NET server",
            usage = new { users = new { active_month = counts.LocalUsers } },
            thumbnail = new { url = (string?)null },
            languages = Array.Empty<string>(),
            configuration = Configuration(),
            registrations = new { enabled = false, approval_required = true, message = (string?)null },
            contact = new { email = string.Empty, account = (object?)null },
            rules = Array.Empty<object>()
        });
    }

    private static object Configuration() => new
    {
        statuses = new { max_characters = 5_000, max_media_attachments = 4, characters_reserved_per_url = 23 },
        media_attachments = new
        {
            supported_mime_types = new[] { "image/jpeg", "image/png", "image/gif", "image/webp", "video/mp4", "audio/mpeg", "audio/ogg" },
            image_size_limit = 16_777_216,
            image_matrix_limit = 16_777_216,
            video_size_limit = 104_857_600,
            video_frame_rate_limit = 120,
            video_matrix_limit = 2_304_000
        },
        polls = new { max_options = 4, max_characters_per_option = 50, min_expiration = 300, max_expiration = 2_629_746 }
    };

    private static async Task<IResult> LookupAccountAsync(
        string acct,
        MastodonQueryService service,
        FederationOptions options,
        CancellationToken cancellationToken)
    {
        MastodonAccount? account = await service.FindAccountByLookupAsync(acct, options.PublicBaseUri.IdnHost, cancellationToken).ConfigureAwait(false);
        return account is null ? Results.NotFound(new { error = "Record not found" }) : Results.Json(account);
    }

    private static async Task<IResult> AccountAsync(
        string id,
        MastodonQueryService service,
        IExternalEntityIdService externalIds,
        FederationOptions options,
        CancellationToken cancellationToken)
    {
        Guid? accountId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            id,
            cancellationToken).ConfigureAwait(false);
        if (accountId is null)
        {
            return Results.NotFound(new { error = "Record not found" });
        }

        MastodonAccount? account = await service.FindAccountByIdAsync(accountId.Value, options.PublicBaseUri.IdnHost, cancellationToken).ConfigureAwait(false);
        return account is null ? Results.NotFound(new { error = "Record not found" }) : Results.Json(account);
    }

    private static async Task<IResult> VerifyCredentialsAsync(
        ClaimsPrincipal user,
        MastodonQueryService service,
        FederationOptions options,
        CancellationToken cancellationToken)
    {
        string? username = user.FindFirst("preferred_username")?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Forbid();
        }

        MastodonAccount? account = await service.FindAccountByLookupAsync(username, options.PublicBaseUri.IdnHost, cancellationToken).ConfigureAwait(false);
        return account is null ? Results.Forbid() : Results.Json(account);
    }

    private static async Task<IResult> StatusAsync(
        HttpContext context,
        string id,
        MastodonQueryService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        Guid? statusId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            id,
            cancellationToken).ConfigureAwait(false);
        if (statusId is null)
        {
            return Results.NotFound(new { error = "Record not found" });
        }

        string? viewer = context.User.FindFirst("actor")?.Value;
        MastodonStatus? status = await service.FindStatusAsync(statusId.Value, viewer, cancellationToken).ConfigureAwait(false);
        return status is null ? Results.NotFound(new { error = "Record not found" }) : Results.Json(status);
    }

    private static async Task<IResult> PublicTimelineAsync(
        HttpContext context,
        MastodonQueryService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        (bool valid, Guid? maxId, int limit) = await ReadPagingAsync(context, externalIds, cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            return Results.BadRequest(new { error = "Invalid pagination" });
        }

        bool local = string.Equals(context.Request.Query["local"], "true", StringComparison.OrdinalIgnoreCase);
        MastodonPage<MastodonStatus> page = await service.ReadPublicTimelineAsync(maxId, limit, local, cancellationToken).ConfigureAwait(false);
        WriteLinkHeader(context, page);
        return Results.Json(page.Items);
    }

    private static async Task<IResult> AccountStatusesAsync(
        HttpContext context,
        string id,
        MastodonQueryService service,
        IExternalEntityIdService externalIds,
        FederationOptions options,
        CancellationToken cancellationToken)
    {
        Guid? accountId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            id,
            cancellationToken).ConfigureAwait(false);
        (bool valid, Guid? maxId, int limit) = await ReadPagingAsync(context, externalIds, cancellationToken).ConfigureAwait(false);
        if (accountId is null || !valid)
        {
            return Results.BadRequest(new { error = "Invalid account or pagination" });
        }

        string? viewer = context.User.FindFirst("actor")?.Value;
        MastodonPage<MastodonStatus> page = await service.ReadAccountStatusesAsync(
            accountId.Value,
            options.PublicBaseUri.IdnHost,
            maxId,
            limit,
            viewer,
            cancellationToken).ConfigureAwait(false);
        WriteLinkHeader(context, page);
        return Results.Json(page.Items);
    }

    private static async Task<IResult> HomeTimelineAsync(
        HttpContext context,
        MastodonQueryService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        (bool valid, Guid? maxId, int limit) = await ReadPagingAsync(context, externalIds, cancellationToken).ConfigureAwait(false);
        if (username is null || !valid)
        {
            return Results.Forbid();
        }

        string? actorIri = await service.FindLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        MastodonPage<MastodonStatus> page = await service.ReadHomeTimelineAsync(
            actorIri,
            maxId,
            limit,
            cancellationToken).ConfigureAwait(false);
        WriteLinkHeader(context, page);
        return Results.Json(page.Items);
    }

    private static async Task<IResult> NotificationsAsync(
        HttpContext context,
        MastodonQueryService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(context.User, service, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        (bool valid, Guid? beforeId, int limit) = await ReadNotificationPagingAsync(
            context,
            externalIds,
            cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            return Results.BadRequest(new { error = "Invalid pagination" });
        }

        MastodonPage<MastodonNotification> page = await service.ReadNotificationsAsync(
            actorIri,
            beforeId,
            limit,
            ParseMastodonNotificationKinds(context.Request.Query["types[]"]),
            ParseMastodonNotificationKinds(context.Request.Query["exclude_types[]"]),
            cancellationToken).ConfigureAwait(false);
        WriteLinkHeader(context, page);
        return Results.Json(page.Items);
    }

    private static async Task<IResult> NotificationAsync(
        ClaimsPrincipal principal,
        string id,
        MastodonQueryService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(principal, service, cancellationToken).ConfigureAwait(false);
        Guid? notificationId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Notification,
            id,
            cancellationToken).ConfigureAwait(false);
        if (actorIri is null || notificationId is null)
        {
            return Results.NotFound(new { error = "Record not found" });
        }

        MastodonNotification? notification = await service.FindNotificationAsync(
            actorIri,
            notificationId.Value,
            cancellationToken).ConfigureAwait(false);
        return notification is null
            ? Results.NotFound(new { error = "Record not found" })
            : Results.Json(notification);
    }

    private static async Task<IResult> DismissNotificationAsync(
        ClaimsPrincipal principal,
        string id,
        MastodonQueryService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(principal, service, cancellationToken).ConfigureAwait(false);
        Guid? notificationId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Notification,
            id,
            cancellationToken).ConfigureAwait(false);
        return actorIri is not null && notificationId is not null && await service.DismissNotificationAsync(
            actorIri,
            notificationId.Value,
            cancellationToken).ConfigureAwait(false)
            ? Results.Ok()
            : Results.NotFound(new { error = "Record not found" });
    }

    private static async Task<IResult> ClearNotificationsAsync(
        ClaimsPrincipal principal,
        MastodonQueryService service,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(principal, service, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        _ = await service.ClearNotificationsAsync(actorIri, cancellationToken).ConfigureAwait(false);
        return Results.Ok();
    }

    private static async Task<IResult> NotificationUnreadCountAsync(
        ClaimsPrincipal principal,
        MastodonQueryService service,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(principal, service, cancellationToken).ConfigureAwait(false);
        return actorIri is null
            ? Results.Forbid()
            : Results.Json(new { count = await service.CountUnreadNotificationsAsync(actorIri, cancellationToken).ConfigureAwait(false) });
    }

    private static async Task<IResult> CreateStatusAsync(
        HttpContext context,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        if (username is null)
        {
            return Results.Forbid();
        }

        MastodonStatusMutation? mutation = await ReadStatusMutationAsync(
            context.Request,
            externalIds,
            cancellationToken).ConfigureAwait(false);
        if (mutation is null)
        {
            return Results.BadRequest(new { error = "Invalid status request" });
        }

        MastodonStatus status = await service.CreateStatusAsync(
            username,
            IdempotencyKey(context),
            mutation,
            cancellationToken).ConfigureAwait(false);
        return Results.Json(status);
    }

    private static Task<IResult> DeleteStatusAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        ExecuteStatusMutationAsync(context, id, service.DeleteStatusAsync, externalIds, cancellationToken);

    private static Task<IResult> FavouriteAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        ExecuteStatusMutationAsync(context, id, service.FavouriteAsync, externalIds, cancellationToken);

    private static Task<IResult> UnfavouriteAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        ExecuteStatusMutationAsync(context, id, service.UnfavouriteAsync, externalIds, cancellationToken);

    private static Task<IResult> ReblogAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        ExecuteStatusMutationAsync(context, id, service.ReblogAsync, externalIds, cancellationToken);

    private static Task<IResult> UnreblogAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        ExecuteStatusMutationAsync(context, id, service.UnreblogAsync, externalIds, cancellationToken);

    private static async Task<IResult> FollowAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        await ExecuteRelationshipMutationAsync(
            context,
            id,
            service.FollowAsync,
            externalIds,
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> UnfollowAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        await ExecuteRelationshipMutationAsync(
            context,
            id,
            service.UnfollowAsync,
            externalIds,
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> BlockAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        await ExecuteRelationshipMutationAsync(
            context,
            id,
            service.BlockAsync,
            externalIds,
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> UnblockAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken) =>
        await ExecuteRelationshipMutationAsync(
            context,
            id,
            service.UnblockAsync,
            externalIds,
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> ExecuteRelationshipMutationAsync(
        HttpContext context,
        string id,
        Func<string, Guid, string, CancellationToken, Task<MastodonRelationship>> mutation,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        Guid? accountId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            id,
            cancellationToken).ConfigureAwait(false);
        if (username is null || accountId is null)
        {
            return Results.NotFound(new { error = "Record not found" });
        }

        try
        {
            return Results.Json(await mutation(
                username,
                accountId.Value,
                IdempotencyKey(context),
                cancellationToken).ConfigureAwait(false));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "Record not found" });
        }
        catch (InvalidOperationException exception)
        {
            return Results.UnprocessableEntity(new { error = exception.Message });
        }
    }

    private static async Task<IResult> RelationshipsAsync(
        HttpContext context,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        string[] values = context.Request.Query["id[]"]
            .Concat(context.Request.Query["id"])
            .OfType<string>()
            .ToArray();
        if (username is null || values.Length == 0 || values.Length > 100)
        {
            return Results.UnprocessableEntity(new { error = "One to 100 account IDs are required." });
        }

        var result = new List<MastodonRelationship>(values.Length);
        foreach (string value in values.Distinct(StringComparer.Ordinal))
        {
            Guid? accountId = await externalIds.ResolveAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Actor,
                value,
                cancellationToken).ConfigureAwait(false);
            if (accountId is null)
            {
                continue;
            }

            MastodonRelationship? relationship = await service.FindRelationshipAsync(
                username,
                accountId.Value,
                cancellationToken).ConfigureAwait(false);
            if (relationship is not null)
            {
                result.Add(relationship);
            }
        }

        return Results.Json(result);
    }

    private static async Task<IResult> MuteAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        Guid? accountId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            id,
            cancellationToken).ConfigureAwait(false);
        if (username is null || accountId is null)
        {
            return Results.NotFound(new { error = "Record not found" });
        }

        (bool hideNotifications, TimeSpan? duration) = await ReadMuteOptionsAsync(context.Request, cancellationToken).ConfigureAwait(false);
        try
        {
            return Results.Json(await service.MuteAsync(
                username,
                accountId.Value,
                hideNotifications,
                duration,
                cancellationToken).ConfigureAwait(false));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "Record not found" });
        }
    }

    private static async Task<IResult> UnmuteAsync(
        HttpContext context,
        string id,
        MastodonCommandService service,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        Guid? accountId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            id,
            cancellationToken).ConfigureAwait(false);
        if (username is null || accountId is null)
        {
            return Results.NotFound(new { error = "Record not found" });
        }

        try
        {
            return Results.Json(await service.UnmuteAsync(username, accountId.Value, cancellationToken).ConfigureAwait(false));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "Record not found" });
        }
    }

    private static async Task<(bool HideNotifications, TimeSpan? Duration)> ReadMuteOptionsAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        string? notifications = null;
        string? duration = null;
        if (request.HasFormContentType)
        {
            IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            notifications = form["notifications"].FirstOrDefault();
            duration = form["duration"].FirstOrDefault();
        }
        else if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using JsonDocument json = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
            notifications = json.RootElement.TryGetProperty("notifications", out JsonElement notificationValue)
                ? notificationValue.ToString()
                : null;
            duration = json.RootElement.TryGetProperty("duration", out JsonElement durationValue)
                ? durationValue.ToString()
                : null;
        }

        bool hideNotifications = !string.Equals(notifications, "false", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(notifications, "0", StringComparison.Ordinal);
        TimeSpan? parsedDuration = long.TryParse(duration, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds) && seconds > 0
            ? TimeSpan.FromSeconds(Math.Min(seconds, 31_536_000))
            : null;
        return (hideNotifications, parsedDuration);
    }

    private static async Task<IResult> ExecuteStatusMutationAsync(
        HttpContext context,
        string id,
        Func<string, Guid, string, CancellationToken, Task<MastodonStatus>> operation,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        if (username is null)
        {
            return Results.Forbid();
        }

        Guid? statusId = await externalIds.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            id,
            cancellationToken).ConfigureAwait(false);
        if (statusId is null)
        {
            return Results.NotFound(new { error = "Record not found" });
        }

        try
        {
            MastodonStatus status = await operation(
                username,
                statusId.Value,
                IdempotencyKey(context),
                cancellationToken).ConfigureAwait(false);
            return Results.Json(status);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "Record not found" });
        }
    }

    private static async Task<MastodonStatusMutation?> ReadStatusMutationAsync(
        HttpRequest request,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        string? status;
        string visibility;
        string? spoilerText;
        bool sensitive;
        string? replyId;
        string[] mediaIds;
        if (request.HasFormContentType)
        {
            IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            status = form["status"].FirstOrDefault();
            visibility = form["visibility"].FirstOrDefault() ?? "public";
            spoilerText = form["spoiler_text"].FirstOrDefault();
            sensitive = string.Equals(form["sensitive"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(form["sensitive"].FirstOrDefault(), "1", StringComparison.Ordinal);
            replyId = form["in_reply_to_id"].FirstOrDefault();
            mediaIds = form["media_ids[]"].Concat(form["media_ids"])
                .Where(value => value is not null)
                .Select(value => value!)
                .ToArray();
        }
        else if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using JsonDocument body = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = body.RootElement;
            status = root.TryGetProperty("status", out JsonElement statusValue) && statusValue.ValueKind == JsonValueKind.String
                ? statusValue.GetString()
                : null;
            visibility = root.TryGetProperty("visibility", out JsonElement visibilityValue) && visibilityValue.ValueKind == JsonValueKind.String
                ? visibilityValue.GetString() ?? "public"
                : "public";
            spoilerText = root.TryGetProperty("spoiler_text", out JsonElement spoilerValue) && spoilerValue.ValueKind == JsonValueKind.String
                ? spoilerValue.GetString()
                : null;
            sensitive = root.TryGetProperty("sensitive", out JsonElement sensitiveValue) && sensitiveValue.ValueKind == JsonValueKind.True;
            replyId = root.TryGetProperty("in_reply_to_id", out JsonElement replyValue) && replyValue.ValueKind == JsonValueKind.String
                ? replyValue.GetString()
                : null;
            mediaIds = root.TryGetProperty("media_ids", out JsonElement mediaValue) && mediaValue.ValueKind == JsonValueKind.Array
                ? mediaValue.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
                : [];
        }
        else
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(status) || status.Length > 5_000 ||
            !new[] { "public", "unlisted", "private", "direct" }.Contains(visibility, StringComparer.Ordinal))
        {
            return null;
        }

        Guid? parsedReply = replyId is null
            ? null
            : await externalIds.ResolveAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Post,
                replyId,
                cancellationToken).ConfigureAwait(false);
        if (replyId is not null && parsedReply is null)
        {
            return null;
        }

        if (mediaIds.Length > 4)
        {
            return null;
        }

        var parsedMedia = new List<Guid>(mediaIds.Length);
        foreach (string mediaId in mediaIds.Distinct(StringComparer.Ordinal))
        {
            Guid? resolved = await externalIds.ResolveAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Media,
                mediaId,
                cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return null;
            }

            parsedMedia.Add(resolved.Value);
        }

        return new(
            status,
            visibility,
            spoilerText,
            sensitive,
            parsedReply,
            parsedMedia);
    }

    private static async Task<(string Name, string? Website, string RedirectUris, string Scopes)> ReadApplicationRegistrationAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HasFormContentType)
        {
            IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            return (
                form["client_name"].FirstOrDefault() ?? string.Empty,
                form["website"].FirstOrDefault(),
                form["redirect_uris"].FirstOrDefault() ?? string.Empty,
                form["scopes"].FirstOrDefault() ?? "read");
        }

        if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using JsonDocument body = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = body.RootElement;
            return (
                root.TryGetProperty("client_name", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("website", out JsonElement website) && website.ValueKind == JsonValueKind.String ? website.GetString() : null,
                root.TryGetProperty("redirect_uris", out JsonElement redirects) && redirects.ValueKind == JsonValueKind.String ? redirects.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("scopes", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.String ? scopes.GetString() ?? "read" : "read");
        }

        throw new BadHttpRequestException(
            "Mastodon application registration requires JSON or form data.",
            StatusCodes.Status415UnsupportedMediaType);
    }

    private static string? AuthenticatedUsername(ClaimsPrincipal principal) =>
        principal.FindFirst("preferred_username")?.Value ?? principal.Identity?.Name;

    private static async Task<string?> ViewerActorIriAsync(
        ClaimsPrincipal principal,
        MastodonQueryService service,
        CancellationToken cancellationToken)
    {
        string? actorIri = principal.FindFirst("actor")?.Value;
        if (!string.IsNullOrWhiteSpace(actorIri))
        {
            return actorIri;
        }

        string? username = AuthenticatedUsername(principal);
        return username is null ? null : await service.FindLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(bool Valid, Guid? BeforeId, int Limit)> ReadNotificationPagingAsync(
        HttpContext context,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        int limit = 15;
        string? rawLimit = context.Request.Query["limit"].FirstOrDefault();
        if (rawLimit is not null && (!int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 40))
        {
            return (false, null, limit);
        }

        string? rawMaxId = context.Request.Query["max_id"].FirstOrDefault();
        Guid? beforeId = rawMaxId is null
            ? null
            : await externalIds.ResolveAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Notification,
                rawMaxId,
                cancellationToken).ConfigureAwait(false);
        return (rawMaxId is null || beforeId is not null, beforeId, limit);
    }

    private static HashSet<UserNotificationKind>? ParseMastodonNotificationKinds(
        Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var result = new HashSet<UserNotificationKind>();
        foreach (string? value in values)
        {
            UserNotificationKind? kind = value switch
            {
                "follow" => UserNotificationKind.Follow,
                "favourite" => UserNotificationKind.Favourite,
                "reblog" => UserNotificationKind.Reblog,
                "poll" => UserNotificationKind.Poll,
                "update" => UserNotificationKind.Update,
                "mention" => UserNotificationKind.Mention,
                _ => null
            };
            if (kind is not null)
            {
                result.Add(kind.Value);
            }
        }

        return result;
    }

    private static string IdempotencyKey(HttpContext context)
    {
        string? provided = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        return provided is { Length: >= 8 and <= 200 } && !provided.Any(char.IsControl)
            ? provided
            : "mastodon-" + Guid.NewGuid().ToString("N");
    }

    private static async Task<(bool Valid, Guid? MaxId, int Limit)> ReadPagingAsync(
        HttpContext context,
        IExternalEntityIdService externalIds,
        CancellationToken cancellationToken)
    {
        Guid? maxId = null;
        int limit = 20;
        string? rawMaxId = context.Request.Query["max_id"].FirstOrDefault();
        if (rawMaxId is not null)
        {
            maxId = await externalIds.ResolveAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Post,
                rawMaxId,
                cancellationToken).ConfigureAwait(false);
            if (maxId is null)
            {
                return (false, null, limit);
            }
        }

        string? rawLimit = context.Request.Query["limit"].FirstOrDefault();
        bool valid = rawLimit is null ||
            int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) && limit is >= 1 and <= 40;
        return (valid, maxId, limit);
    }

    private static void WriteLinkHeader<T>(HttpContext context, MastodonPage<T> page)
    {
        var links = new List<string>();
        string path = context.Request.Path;
        if (page.NextId is not null)
        {
            links.Add($"<{path}?max_id={Uri.EscapeDataString(page.NextId)}>; rel=\"next\"");
        }

        if (page.PreviousId is not null)
        {
            links.Add($"<{path}?since_id={Uri.EscapeDataString(page.PreviousId)}>; rel=\"prev\"");
        }

        if (links.Count > 0)
        {
            context.Response.Headers.Link = string.Join(", ", links);
        }
    }
}
