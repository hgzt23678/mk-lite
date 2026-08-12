using System.Security.Claims;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ActivityPub.MisskeyApi;

public static class MisskeyEndpoints
{
    private const int MaximumJsonRequestBodyBytes = 2_000_000;
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapMisskeyApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/streaming", MisskeyStreamingEndpoints.StreamAsync)
            .RequireRateLimiting("local-api");
        RouteGroupBuilder publicApi = endpoints.MapGroup("/api").RequireRateLimiting("local-api");
        publicApi.MapPost("/meta", Meta);
        publicApi.MapPost("/username/available", UsernameAvailableAsync);
        publicApi.MapPost("/email-address/available", EmailAddressAvailableAsync);
        publicApi.MapPost("/stats", StatsAsync);
        publicApi.MapPost("/federation/instances", FederationInstancesAsync);
        publicApi.MapPost("/users/show", ShowUserAsync);
        publicApi.MapPost("/users/search", SearchUsersAsync);
        publicApi.MapPost("/users/search-by-username-and-host", SearchUsersByUsernameAndHostAsync);
        publicApi.MapPost("/users/followers", FollowersAsync);
        publicApi.MapPost("/users/following", FollowingAsync);
        publicApi.MapPost("/hashtags/search", SearchHashtagsAsync);
        publicApi.MapPost("/hashtags/trend", TrendHashtagsAsync);
        publicApi.MapPost("/users/notes", UserNotesAsync);
        publicApi.MapPost("/notes/show", ShowNoteAsync);
        publicApi.MapPost("/notes/global-timeline", (HttpContext context, MisskeyQueryService service, CancellationToken token) =>
            TimelineAsync(context, service, "public", localOnly: false, token));
        publicApi.MapPost("/notes/local-timeline", (HttpContext context, MisskeyQueryService service, CancellationToken token) =>
            TimelineAsync(context, service, "public", localOnly: true, token));
        publicApi.MapPost("/notes/reactions", ReactionsAsync);
        publicApi.MapPost("/announcements", AnnouncementsAsync);
        publicApi.MapPost("/miauth/{session}/check", CheckMiAuthSessionAsync);

        RouteGroupBuilder authenticated = endpoints.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting("local-api");
        authenticated.MapPost("/i", MeAsync);
        authenticated.MapPost("/i/update", UpdateProfileAsync).RequireAuthorization("misskey.write:account");
        authenticated.MapPost("/notes/timeline", (HttpContext context, MisskeyQueryService service, CancellationToken token) =>
            TimelineAsync(context, service, "home", localOnly: false, token));
        authenticated.MapPost("/notes/hybrid-timeline", (HttpContext context, MisskeyQueryService service, CancellationToken token) =>
            TimelineAsync(context, service, "home", localOnly: false, token));
        authenticated.MapPost("/miauth/gen-token", GenerateMiAuthTokenAsync).RequireAuthorization("misskey.secure");
        authenticated.MapPost("/i/apps", ListApplicationsAsync).RequireAuthorization("misskey.secure");
        authenticated.MapPost("/i/revoke-token", RevokeApplicationAsync).RequireAuthorization("misskey.secure");
        authenticated.MapPost("/i/notifications", NotificationsAsync).RequireAuthorization("misskey.read:notifications");
        authenticated.MapPost("/notifications/read", ReadNotificationsAsync).RequireAuthorization("misskey.write:notifications");
        authenticated.MapPost("/notifications/mark-all-as-read", MarkAllNotificationsReadAsync).RequireAuthorization("misskey.write:notifications");
        authenticated.MapPost("/i/read-announcement", ReadAnnouncementAsync).RequireAuthorization("misskey.write:account");
        authenticated.MapPost("/following/create", FollowAsync).RequireAuthorization("misskey.write:following");
        authenticated.MapPost("/following/delete", UnfollowAsync).RequireAuthorization("misskey.write:following");
        authenticated.MapPost("/mute/create", MuteAsync).RequireAuthorization("misskey.write:mutes");
        authenticated.MapPost("/mute/delete", UnmuteAsync).RequireAuthorization("misskey.write:mutes");
        authenticated.MapPost("/blocking/create", BlockAsync).RequireAuthorization("misskey.write:blocks");
        authenticated.MapPost("/blocking/delete", UnblockAsync).RequireAuthorization("misskey.write:blocks");
        authenticated.MapPost("/users/relation", RelationshipAsync).RequireAuthorization("misskey.read:following");

        RouteGroupBuilder api = endpoints.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting("local-api");
        api.MapPost("/notes/create", CreateNoteAsync).RequireAuthorization("misskey.write:notes");
        api.MapPost("/notes/delete", DeleteNoteAsync).RequireAuthorization("misskey.write:notes");
        api.MapPost("/notes/reactions/create", CreateReactionAsync).RequireAuthorization("misskey.write:reactions");
        api.MapPost("/notes/reactions/delete", DeleteReactionAsync).RequireAuthorization("misskey.write:reactions");
        api.MapPost("/notes/polls/vote", VotePollAsync).RequireAuthorization("misskey.write:votes");

        RouteGroupBuilder admin = endpoints.MapGroup("/api/admin")
            .RequireAuthorization("activitypub.admin")
            .RequireRateLimiting("local-api");
        admin.MapPost("/announcements/list", AdminAnnouncementsAsync);
        admin.MapPost("/announcements/create", CreateAnnouncementAsync);
        admin.MapPost("/announcements/update", UpdateAnnouncementAsync);
        admin.MapPost("/announcements/delete", DeleteAnnouncementAsync);
        admin.MapPost("/invite", CreateRegistrationInvitationAsync);
        admin.MapPost("/relays/add", AddRelayAsync);
        admin.MapPost("/relays/list", ListRelaysAsync);
        admin.MapPost("/relays/remove", RemoveRelayAsync);
        authenticated.MapPost("/drive", DriveUsageAsync).RequireAuthorization("misskey.read:drive");
        authenticated.MapPost("/drive/files", ListDriveFilesAsync).RequireAuthorization("misskey.read:drive");
        authenticated.MapPost("/drive/files/create", CreateDriveFileAsync)
            .RequireAuthorization("misskey.write:drive")
            .WithMetadata(new IgnoreAntiforgeryTokenAttribute());
        authenticated.MapPost("/drive/files/delete", DeleteDriveFileAsync).RequireAuthorization("misskey.write:drive");
        authenticated.MapPost("/drive/files/update", UpdateDriveFileAsync).RequireAuthorization("misskey.write:drive");
        authenticated.MapPost("/drive/files/show", ShowDriveFileAsync).RequireAuthorization("misskey.read:drive");
        authenticated.MapPost("/drive/folders", ListDriveFoldersAsync).RequireAuthorization("misskey.read:drive");
        authenticated.MapPost("/drive/folders/create", CreateDriveFolderAsync).RequireAuthorization("misskey.write:drive");
        authenticated.MapPost("/drive/folders/delete", DeleteDriveFolderAsync).RequireAuthorization("misskey.write:drive");
        authenticated.MapPost("/drive/folders/update", UpdateDriveFolderAsync).RequireAuthorization("misskey.write:drive");
        return endpoints;
    }

    private static async Task<IResult> AnnouncementsAsync(
        HttpContext context,
        MisskeyAnnouncementService announcements,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        int limit = Integer(body, "limit", 10);
        if (limit is < 1 or > 100 ||
            !IsOptionalString(body, "sinceId") ||
            !IsOptionalString(body, "untilId") ||
            !IsOptionalBoolean(body, "withUnreads"))
        {
            return InvalidRequest();
        }

        string? viewerActorIri = await ViewerActorIriAsync(context.User, query, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            IReadOnlyList<MisskeyAnnouncement> values = await announcements.ReadAsync(
                new(String(body, "sinceId"), String(body, "untilId"), limit, Boolean(body, "withUnreads", false)),
                viewerActorIri,
                cancellationToken).ConfigureAwait(false);
            return Results.Json(values);
        }
        catch (MisskeyApiException exception)
        {
            return ApiError(exception);
        }
        catch (ArgumentOutOfRangeException)
        {
            return InvalidRequest();
        }
        catch (KeyNotFoundException)
        {
            return InvalidRequest();
        }
    }

    private static async Task<IResult> ReadAnnouncementAsync(
        HttpContext context,
        MisskeyAnnouncementService announcements,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? announcementId = String(body, "announcementId");
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (announcementId is null || actorIri is null)
        {
            return InvalidRequest();
        }

        bool marked = await announcements.MarkReadAsync(announcementId, actorIri, cancellationToken).ConfigureAwait(false);
        return marked
            ? Results.NoContent()
            : Error(404, "No such announcement.", "NO_SUCH_ANNOUNCEMENT", "184663db-df88-4bc2-8b52-fb85f0681939");
    }

    private static async Task<IResult> AdminAnnouncementsAsync(
        HttpContext context,
        MisskeyAnnouncementService announcements,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        int limit = Integer(body, "limit", 10);
        if (limit is < 1 or > 100 || !IsOptionalString(body, "sinceId") || !IsOptionalString(body, "untilId"))
        {
            return InvalidRequest();
        }

        try
        {
            return Results.Json(await announcements.ReadForAdministrationAsync(
                String(body, "sinceId"),
                String(body, "untilId"),
                limit,
                cancellationToken).ConfigureAwait(false));
        }
        catch (MisskeyApiException exception)
        {
            return ApiError(exception);
        }
        catch (ArgumentOutOfRangeException)
        {
            return InvalidRequest();
        }
        catch (KeyNotFoundException)
        {
            return InvalidRequest();
        }
    }

    private static async Task<IResult> CreateAnnouncementAsync(
        HttpContext context,
        MisskeyAnnouncementService announcements,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? ownerActorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (!TryAnnouncementMutation(body, out MisskeyAnnouncementMutation mutation) ||
            context.User.FindFirst("sub")?.Value is not { } operatorId ||
            ownerActorIri is null)
        {
            return InvalidRequest();
        }

        try
        {
            return Results.Json(await announcements.CreateAsync(
                mutation,
                operatorId,
                ownerActorIri,
                cancellationToken).ConfigureAwait(false));
        }
        catch (AnnouncementImageImportException exception)
        {
            return AnnouncementImageError(exception);
        }
        catch (DomainException)
        {
            return InvalidRequest();
        }
    }

    private static async Task<IResult> UpdateAnnouncementAsync(
        HttpContext context,
        MisskeyAnnouncementService announcements,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? id = String(body, "id");
        string? ownerActorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (id is null ||
            !TryAnnouncementMutation(body, out MisskeyAnnouncementMutation mutation) ||
            context.User.FindFirst("sub")?.Value is not { } operatorId ||
            ownerActorIri is null)
        {
            return InvalidRequest();
        }

        try
        {
            bool updated = await announcements.UpdateAsync(
                id,
                mutation,
                operatorId,
                ownerActorIri,
                cancellationToken).ConfigureAwait(false);
            return updated
                ? Results.NoContent()
                : Error(404, "No such announcement.", "NO_SUCH_ANNOUNCEMENT", "d3aae5a7-6372-4cb4-b61c-f511ffc2d7cc");
        }
        catch (AnnouncementImageImportException exception)
        {
            return AnnouncementImageError(exception);
        }
        catch (DomainException)
        {
            return InvalidRequest();
        }
    }

    private static async Task<IResult> DeleteAnnouncementAsync(
        HttpContext context,
        MisskeyAnnouncementService announcements,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? id = String(body, "id");
        if (id is null || context.User.FindFirst("sub")?.Value is not { } operatorId)
        {
            return InvalidRequest();
        }

        bool deleted = await announcements.DeleteAsync(id, operatorId, cancellationToken).ConfigureAwait(false);
        return deleted
            ? Results.NoContent()
            : Error(404, "No such announcement.", "NO_SUCH_ANNOUNCEMENT", "ecad8040-a276-4e85-bda9-015a708d291e");
    }

    private static async Task<IResult> GenerateMiAuthTokenAsync(
        HttpContext context,
        IMisskeyAuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        if (username is null)
        {
            return Results.Forbid();
        }

        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? session = String(body, "session");
        string[]? permissions = StringArray(body, "permission");
        if (permissions is null)
        {
            return InvalidRequest();
        }

        try
        {
            MisskeyIssuedToken issued = session is null
                ? await authentication.IssueDirectAsync(
                    username,
                    String(body, "name") ?? "Unnamed application",
                    String(body, "description"),
                    String(body, "iconUrl"),
                    permissions,
                    cancellationToken).ConfigureAwait(false)
                : await authentication.IssueAsync(
                    username,
                    session,
                    String(body, "name") ?? "Unnamed application",
                    String(body, "description"),
                    String(body, "iconUrl"),
                    callbackUri: null,
                    permissions,
                    cancellationToken).ConfigureAwait(false);
            return Results.Json(new { token = issued.Token });
        }
        catch (ArgumentException exception)
        {
            return Error(400, exception.Message, "INVALID_PARAM", "b5f82b3b-0f49-4dca-bd97-a3c3bd49a17e");
        }
        catch (InvalidOperationException exception)
        {
            return Error(409, exception.Message, "ALREADY_EXISTS", "2c2b1f8e-3b78-46d4-9895-4f16d8213dca");
        }
    }

    private static async Task<IResult> CheckMiAuthSessionAsync(
        string session,
        IMisskeyAuthenticationService authentication,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        try
        {
            MisskeyIssuedToken? issued = await authentication.ConsumeSessionAsync(session, cancellationToken).ConfigureAwait(false);
            if (issued is null)
            {
                return Results.Json(new { ok = false });
            }

            object? account = await query.FindMeAsync(issued.Username, cancellationToken).ConfigureAwait(false);
            return account is null
                ? Error(500, "The token owner is unavailable.", "INTERNAL_ERROR", "ec960909-4cb0-44f7-8c6f-7fe01e7aaeea")
                : Results.Json(new { ok = true, token = issued.Token, user = account });
        }
        catch (ArgumentException)
        {
            return Results.Json(new { ok = false });
        }
    }

    private static async Task<IResult> ListApplicationsAsync(
        HttpContext context,
        IMisskeyAuthenticationService authentication,
        IExternalEntityIdService externalIds,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken)
            .ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        IReadOnlyList<MisskeyTokenSummary> tokens = await authentication.ListAsync(actorIri, cancellationToken).ConfigureAwait(false);
        var response = new List<object>(tokens.Count);
        foreach (MisskeyTokenSummary token in tokens)
        {
            string id = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.AccessToken,
                token.Id,
                token.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            response.Add(new
            {
                id,
                name = token.Name,
                description = token.Description,
                iconUrl = token.IconUri,
                createdAt = token.CreatedAt,
                lastUsedAt = token.LastUsedAt,
                permission = token.Permissions
            });
        }

        return Results.Json(response);
    }

    private static async Task<IResult> RevokeApplicationAsync(
        HttpContext context,
        IMisskeyAuthenticationService authentication,
        IExternalEntityIdService externalIds,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? externalId = String(body, "tokenId");
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (externalId is null || actorIri is null)
        {
            return InvalidRequest();
        }

        Guid? internalId = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.AccessToken,
            externalId,
            cancellationToken).ConfigureAwait(false);
        if (internalId is not null)
        {
            _ = await authentication.RevokeAsync(actorIri, internalId.Value, cancellationToken).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    private static IResult Meta(MisskeyMetadataService metadata) => Results.Json(metadata.GetMetadata());

    private static async Task<IResult> CreateRegistrationInvitationAsync(
        HttpContext context,
        IRegistrationInvitationService invitations,
        CancellationToken cancellationToken)
    {
        _ = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? operatorId = context.User.FindFirst("sub")?.Value;
        if (operatorId is null)
        {
            return Results.Forbid();
        }

        try
        {
            RegistrationInvitationIssueResult invitation = await invitations
                .IssueAsync(operatorId, cancellationToken)
                .ConfigureAwait(false);
            return Results.Json(new { code = invitation.Code });
        }
        catch (InvalidOperationException)
        {
            return Error(
                StatusCodes.Status409Conflict,
                "Invitation registration is unavailable.",
                "INVITATION_UNAVAILABLE",
                "d4bb71df-7038-4de9-a2ad-f9d8f4ba1945");
        }
    }

    private static async Task<IResult> AddRelayAsync(
        HttpContext context,
        IRelayCommandService relays,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? inbox = String(body, "inbox");
        if (string.IsNullOrWhiteSpace(inbox))
        {
            return InvalidRequest();
        }

        try
        {
            Domain.Relay relay = await relays.AddAsync(inbox, cancellationToken).ConfigureAwait(false);
            return Results.Json(new
            {
                id = relay.Id.ToString("D"),
                inbox = relay.Inbox,
                status = RelayStatusName(relay.Status)
            });
        }
        catch (DomainException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                exception.Message,
                "INVALID_URL",
                "fb8c92d3-d4e5-44e7-b3d4-800d5cef8b2c");
        }
    }

    private static async Task<IResult> ListRelaysAsync(
        HttpContext context,
        IRelayCommandService relays,
        CancellationToken cancellationToken)
    {
        _ = context;
        IReadOnlyList<Domain.Relay> list = await relays.ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(list.Select(relay => new
        {
            id = relay.Id.ToString("D"),
            inbox = relay.Inbox,
            status = RelayStatusName(relay.Status)
        }));
    }

    private static async Task<IResult> RemoveRelayAsync(
        HttpContext context,
        IRelayCommandService relays,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? inbox = String(body, "inbox");
        if (string.IsNullOrWhiteSpace(inbox))
        {
            return InvalidRequest();
        }

        try
        {
            await relays.RemoveAsync(inbox, cancellationToken).ConfigureAwait(false);
            return Results.Json(new { });
        }
        catch (DomainException exception)
        {
            return Error(
                StatusCodes.Status404NotFound,
                exception.Message,
                "RELAY_NOT_FOUND",
                "fb8c92d3-d4e5-44e7-b3d4-800d5cef8b2d");
        }
    }

    private static string RelayStatusName(Domain.RelayStatus status) => status switch
    {
        Domain.RelayStatus.Accepted => "accepted",
        Domain.RelayStatus.Rejected => "rejected",
        _ => "requesting"
    };

    private static async Task<IResult> UsernameAvailableAsync(
        HttpContext context,
        IRegistrationAvailabilityService availability,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? username = String(body, "username");
        if (username is null)
        {
            return InvalidRequest();
        }

        return Results.Json(new
        {
            available = await availability.IsUsernameAvailableAsync(username, cancellationToken).ConfigureAwait(false)
        });
    }

    private static async Task<IResult> EmailAddressAvailableAsync(
        HttpContext context,
        IRegistrationAvailabilityService availability,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? emailAddress = String(body, "emailAddress");
        if (emailAddress is null)
        {
            return InvalidRequest();
        }

        RegistrationEmailAvailability result = await availability
            .CheckEmailAvailabilityAsync(emailAddress, cancellationToken)
            .ConfigureAwait(false);
        string? reason = result.Reason switch
        {
            RegistrationEmailAvailabilityReason.InvalidFormat => "format",
            _ => null
        };
        return Results.Json(new { available = result.Available, reason });
    }

    private static async Task<IResult> StatsAsync(
        IFederationQueryStore store,
        CancellationToken cancellationToken)
    {
        NodeInfoCounts counts = await store.GetNodeInfoCountsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(new
        {
            notesCount = counts.LocalPosts,
            originalNotesCount = counts.LocalPosts,
            usersCount = counts.LocalUsers,
            originalUsersCount = counts.LocalUsers,
            instances = counts.RemoteDomains,
            driveUsageLocal = counts.LocalMediaBytes,
            driveUsageRemote = counts.RemoteMediaBytes
        });
    }

    private static async Task<IResult> FederationInstancesAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        int limit = Integer(body, "limit", 30);
        int offset = Integer(body, "offset", 0);
        if (limit is < 1 or > 100 || offset < 0)
        {
            return InvalidRequest();
        }

        try
        {
            IReadOnlyList<MisskeyFederationInstance> values = await service.ReadFederationInstancesAsync(
                new(
                    String(body, "host"),
                    NullableBoolean(body, "blocked"),
                    NullableBoolean(body, "notResponding"),
                    NullableBoolean(body, "suspended"),
                    NullableBoolean(body, "federating"),
                    NullableBoolean(body, "subscribing"),
                    NullableBoolean(body, "publishing"),
                    limit,
                    offset,
                    String(body, "sort")),
                cancellationToken).ConfigureAwait(false);
            return Results.Json(values);
        }
        catch (ArgumentOutOfRangeException)
        {
            return InvalidRequest();
        }
    }

    private static async Task<IResult> MeAsync(
        ClaimsPrincipal user,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(user);
        if (username is null)
        {
            return Results.Forbid();
        }

        object? account = await service.FindMeAsync(username, cancellationToken).ConfigureAwait(false);
        return account is null ? Results.Forbid() : Results.Json(account);
    }

    private static async Task<IResult> UpdateProfileAsync(
        HttpContext context,
        MisskeyQueryService query,
        IProfileUpdateService profiles,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? username = AuthenticatedUsername(context.User);
        string? name = OptionalString(body, "name");
        string? description = OptionalString(body, "description");
        bool? isLocked = OptionalBoolean(body, "isLocked");
        bool? discoverable = OptionalBoolean(body, "discoverable");
        bool? indexable = OptionalBoolean(body, "indexable");
        if (username is null ||
            name is not null && name.Length > 200 ||
            description is not null && description.Length > 500)
        {
            return InvalidRequest();
        }

        bool updated = await profiles.UpdateAsync(
            username,
            new ProfileUpdateCommand(name, description, isLocked, discoverable, indexable),
            cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            return Results.Forbid();
        }

        object? me = await query.FindMeAsync(username, cancellationToken).ConfigureAwait(false);
        return Results.Json(me);
    }

    private static string? OptionalString(JsonElement body, string property) =>
        body.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? OptionalBoolean(JsonElement body, string property) =>
        body.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True
            ? true
            : body.TryGetProperty(property, out value) && value.ValueKind == JsonValueKind.False
                ? false
                : null;

    private static async Task<IResult> NotificationsAsync(
        HttpContext context,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<object> items = await query.ReadNotificationsAsync(
            actorIri,
            String(body, "untilId"),
            Integer(body, "limit", 10),
            Boolean(body, "unreadOnly", false),
            Boolean(body, "markAsRead", true),
            StringArray(body, "includeTypes"),
            StringArray(body, "excludeTypes"),
            cancellationToken).ConfigureAwait(false);
        return Results.Json(items);
    }

    private static async Task<IResult> ReadNotificationsAsync(
        HttpContext context,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string[] ids = StringArray(body, "notificationIds") ?? [];
        string? id = String(body, "notificationId");
        if (id is not null)
        {
            ids = [id];
        }

        if (ids.Length == 0 || !await query.MarkNotificationsReadAsync(actorIri, ids, cancellationToken).ConfigureAwait(false))
        {
            return Missing("NO_SUCH_NOTIFICATION", "No such notification.");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllNotificationsReadAsync(
        HttpContext context,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        _ = await query.MarkAllNotificationsReadAsync(actorIri, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static Task<IResult> FollowAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken) =>
        ExecuteFollowMutationAsync(context, service.FollowAsync, cancellationToken);

    private static Task<IResult> UnfollowAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken) =>
        ExecuteFollowMutationAsync(context, service.UnfollowAsync, cancellationToken);

    private static async Task<IResult> ExecuteFollowMutationAsync(
        HttpContext context,
        Func<string, string, string, CancellationToken, Task<object>> mutation,
        CancellationToken cancellationToken)
    {
        (string? username, string? idempotencyKey, IResult? error) = RequireCommandContext(context);
        if (error is not null)
        {
            return error;
        }

        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? userId = String(body, "userId");
        if (userId is null)
        {
            return InvalidRequest();
        }

        try
        {
            return Results.Json(await mutation(username!, idempotencyKey!, userId, cancellationToken).ConfigureAwait(false));
        }
        catch (MisskeyApiException exception)
        {
            return Results.Json(exception.Body, statusCode: exception.StatusCode);
        }
    }

    private static async Task<IResult> MuteAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? userId = String(body, "userId");
        if (username is null || userId is null)
        {
            return InvalidRequest();
        }

        try
        {
            await service.MuteAsync(username, userId, Integer64(body, "expiresAt"), cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (MisskeyApiException exception)
        {
            return Results.Json(exception.Body, statusCode: exception.StatusCode);
        }
    }

    private static async Task<IResult> UnmuteAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? userId = String(body, "userId");
        if (username is null || userId is null)
        {
            return InvalidRequest();
        }

        try
        {
            await service.UnmuteAsync(username, userId, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (MisskeyApiException exception)
        {
            return Results.Json(exception.Body, statusCode: exception.StatusCode);
        }
    }

    private static Task<IResult> BlockAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken) =>
        ExecuteFollowMutationAsync(context, service.BlockAsync, cancellationToken);

    private static Task<IResult> UnblockAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken) =>
        ExecuteFollowMutationAsync(context, service.UnblockAsync, cancellationToken);

    private static async Task<IResult> RelationshipAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        string? username = AuthenticatedUsername(context.User);
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        if (username is null || !body.TryGetProperty("userId", out JsonElement userId))
        {
            return InvalidRequest();
        }

        bool array = userId.ValueKind == JsonValueKind.Array;
        string[] ids = userId.ValueKind switch
        {
            JsonValueKind.String when userId.GetString() is { } value => [value],
            JsonValueKind.Array => userId.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToArray(),
            _ => []
        };
        if (ids.Length == 0 || ids.Length > 100 || ids.Length != ids.Distinct(StringComparer.Ordinal).Count())
        {
            return InvalidRequest();
        }

        var relationships = new List<object>(ids.Length);
        foreach (string id in ids)
        {
            object? relationship = await service.FindRelationshipAsync(username, id, cancellationToken).ConfigureAwait(false);
            if (relationship is null)
            {
                return Missing("NO_SUCH_USER", "No such user.");
            }

            relationships.Add(relationship);
        }

        return Results.Json(array ? relationships : relationships[0]);
    }

    private static async Task<IResult> SearchUsersByUsernameAndHostAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? username = String(body, "username");
        string? host = String(body, "host");
        int? limit = Integer(body, "limit", 10);
        bool? detail = Boolean(body, "detail", true);
        if (username is null && host is null ||
            limit is < 1 or > 100)
        {
            return InvalidRequest();
        }

        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(host))
        {
            return Results.Json(Array.Empty<object>());
        }

        IReadOnlyList<object> users = await service.SearchUsersAsync(
            username ?? string.Empty,
            host,
            limit ?? 10,
            detail ?? true,
            cancellationToken).ConfigureAwait(false);
        return Results.Json(users);
    }

    private static async Task<IResult> SearchUsersAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? query = String(body, "query");
        string? username = String(body, "username");
        string? host = String(body, "host");
        int limit = Integer(body, "limit", 10);
        int offset = Integer(body, "offset", 0);
        bool detail = Boolean(body, "detail", true);
        if (limit is < 1 or > 100 || offset < 0 ||
            query is not null && (username is not null || host is not null))
        {
            return InvalidRequest();
        }

        string search = query ?? username ?? string.Empty;
        if (string.IsNullOrWhiteSpace(search) && string.IsNullOrWhiteSpace(host))
        {
            return Results.Json(Array.Empty<object>());
        }

        // Dolphin's users/search contract orders local prefix matches before remote
        // prefix matches. The application query boundary currently exposes a bounded
        // prefix query, so apply the contract's offset after fetching the bounded page.
        int fetchLimit = Math.Min(100, checked(limit + offset));
        IReadOnlyList<object> users = await service.SearchUsersAsync(
            search,
            host,
            fetchLimit,
            detail,
            cancellationToken).ConfigureAwait(false);
        return Results.Json(users.Skip(offset).Take(limit).ToArray());
    }

    private static async Task<IResult> SearchHashtagsAsync(
        HttpContext context,
        IHashtagRepository hashtags,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? query = String(body, "query");
        int? limit = Integer(body, "limit", 10);
        int? offset = Integer(body, "offset", 0);
        if (query is null || limit is < 1 or > 100 || offset is < 0)
        {
            return InvalidRequest();
        }

        IReadOnlyList<string> results = await hashtags.SearchAsync(
            query,
            limit ?? 10,
            offset ?? 0,
            cancellationToken).ConfigureAwait(false);
        return Results.Json(results);
    }

    private static async Task<IResult> TrendHashtagsAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        _ = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<object> trends = await service.TrendHashtagsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(trends);
    }

    private static async Task<IResult> DriveUsageAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        _ = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        (long usage, long capacity) = await drive.GetUsageAsync(actorIri, cancellationToken).ConfigureAwait(false);
        return Results.Json(new { capacity, usage });
    }

    private static async Task<IResult> ListDriveFilesAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        int? limit = Integer(body, "limit", 10);
        string? folderId = String(body, "folderId");
        string? sinceId = String(body, "sinceId");
        string? untilId = String(body, "untilId");
        if (actorIri is null || limit is < 1 or > 100)
        {
            return InvalidRequest();
        }

        IReadOnlyList<ClientDriveFileView> files = await drive.ListFilesAsync(
            actorIri,
            ResolveMediaId(folderId),
            ResolveMediaId(sinceId),
            ResolveMediaId(untilId),
            limit.Value,
            cancellationToken).ConfigureAwait(false);
        var result = new List<object>(files.Count);
        foreach (ClientDriveFileView file in files)
        {
            result.Add(await query.MapDriveFileAsync(file, cancellationToken).ConfigureAwait(false));
        }

        return Results.Json(result);
    }

    private static async Task<IResult> CreateDriveFileAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (actorIri is null)
        {
            return Results.Forbid();
        }

        var form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        IFormFile? file = form.Files.Count > 0 ? form.Files[0] : null;
        if (file is null)
        {
            return InvalidRequest();
        }

        string? folderId = form["folderId"].ToString();
        string? name = form["name"].ToString();
        string? comment = form["comment"].ToString();
        bool isSensitive = string.Equals(form["isSensitive"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
        try
        {
            ClientDriveFileView created = await drive.UploadFileAsync(
                actorIri,
                ResolveMediaId(folderId),
                string.IsNullOrWhiteSpace(name) ? null : name,
                isSensitive,
                string.IsNullOrWhiteSpace(comment) ? null : comment,
                string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType,
                file.FileName,
                file.OpenReadStream(),
                cancellationToken).ConfigureAwait(false);
            return Results.Json(await query.MapDriveFileAsync(created, cancellationToken).ConfigureAwait(false));
        }
        catch (KeyNotFoundException exception)
        {
            return Missing("NO_SUCH_FOLDER", exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return Error(StatusCodes.Status400BadRequest, exception.Message, "INVALID_FILE", "b0a7f5f8-4976-4c80-b6d7-35d5a00af000");
        }
    }

    private static async Task<IResult> DeleteDriveFileAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        string? fileId = String(body, "fileId");
        if (actorIri is null || fileId is null)
        {
            return InvalidRequest();
        }

        Guid? internalId = await query.ResolveMediaIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (internalId is null)
        {
            return Missing("NO_SUCH_FILE", "File was not found.");
        }

        try
        {
            await drive.DeleteFileAsync(actorIri, internalId.Value, cancellationToken).ConfigureAwait(false);
            return Results.Json(new { });
        }
        catch (KeyNotFoundException exception)
        {
            return Missing("NO_SUCH_FILE", exception.Message);
        }
    }

    private static async Task<IResult> UpdateDriveFileAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        string? fileId = String(body, "fileId");
        if (actorIri is null || fileId is null)
        {
            return InvalidRequest();
        }

        Guid? internalId = await query.ResolveMediaIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (internalId is null)
        {
            return Missing("NO_SUCH_FILE", "File was not found.");
        }

        ClientDriveFileView? updated = await drive.UpdateFileAsync(
            actorIri,
            internalId.Value,
            String(body, "name"),
            ResolveMediaId(String(body, "folderId")),
            String(body, "comment"),
            Boolean(body, "isSensitive", false),
            cancellationToken).ConfigureAwait(false);
        if (updated is null)
        {
            return Missing("NO_SUCH_FILE", "File was not found.");
        }

        return Results.Json(await query.MapDriveFileAsync(updated, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ShowDriveFileAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        string? fileId = String(body, "fileId");
        if (actorIri is null || fileId is null)
        {
            return InvalidRequest();
        }

        Guid? internalId = await query.ResolveMediaIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (internalId is null)
        {
            return Missing("NO_SUCH_FILE", "File was not found.");
        }

        ClientDriveFileView? file = await drive.ShowFileAsync(actorIri, internalId.Value, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return Missing("NO_SUCH_FILE", "File was not found.");
        }

        return Results.Json(await query.MapDriveFileAsync(file, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ListDriveFoldersAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        int? limit = Integer(body, "limit", 10);
        string? parentId = String(body, "parentId");
        if (actorIri is null || limit is < 1 or > 100)
        {
            return InvalidRequest();
        }

        IReadOnlyList<ClientDriveFolderView> folders = await drive.ListFoldersAsync(
            actorIri,
            ResolveMediaId(parentId),
            limit.Value,
            cancellationToken).ConfigureAwait(false);
        var folderViews = new List<object>(folders.Count);
        foreach (ClientDriveFolderView folder in folders)
        {
            folderViews.Add(MapDriveFolder(folder));
        }

        return Results.Json(folderViews);
    }

    private static async Task<IResult> CreateDriveFolderAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        string? name = String(body, "name");
        if (actorIri is null || string.IsNullOrWhiteSpace(name))
        {
            return InvalidRequest();
        }

        try
        {
            ClientDriveFolderView folder = await drive.CreateFolderAsync(
                actorIri,
                name,
                ResolveMediaId(String(body, "parentId")),
                cancellationToken).ConfigureAwait(false);
            return Results.Json(MapDriveFolder(folder));
        }
        catch (KeyNotFoundException exception)
        {
            return Missing("NO_SUCH_FOLDER", exception.Message);
        }
    }

    private static async Task<IResult> DeleteDriveFolderAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        string? folderId = String(body, "folderId");
        if (actorIri is null || folderId is null)
        {
            return InvalidRequest();
        }

        try
        {
            await drive.DeleteFolderAsync(actorIri, RequireMediaId(folderId), cancellationToken).ConfigureAwait(false);
            return Results.Json(new { });
        }
        catch (KeyNotFoundException exception)
        {
            return Missing("NO_SUCH_FOLDER", exception.Message);
        }
    }

    private static async Task<IResult> UpdateDriveFolderAsync(
        HttpContext context,
        MisskeyQueryService query,
        IClientDriveService drive,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? actorIri = await ViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        string? folderId = String(body, "folderId");
        if (actorIri is null || folderId is null)
        {
            return InvalidRequest();
        }

        ClientDriveFolderView? folder = await drive.UpdateFolderAsync(
            actorIri,
            RequireMediaId(folderId),
            String(body, "name"),
            ResolveMediaId(String(body, "parentId")),
            cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            return Missing("NO_SUCH_FOLDER", "Folder was not found.");
        }

        return Results.Json(MapDriveFolder(folder));
    }

    private static Guid? ResolveMediaId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Guid.TryParse(value, out Guid id) ? id : throw new DomainException("Invalid identifier.");

    private static Guid RequireMediaId(string value) =>
        ResolveMediaId(value) ?? throw new DomainException("Invalid identifier.");

    private static object MapDriveFolder(ClientDriveFolderView folder) => new
    {
        id = folder.Id.ToString("D"),
        name = folder.Name,
        parentId = folder.ParentId?.ToString("D"),
        createdAt = folder.CreatedAt
    };

    private static async Task<IResult> ShowUserAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        if (body.TryGetProperty("userIds", out _))
        {
            string[]? userIds = StringArray(body, "userIds");
            if (userIds is null || userIds.Distinct(StringComparer.Ordinal).Count() != userIds.Length)
            {
                return InvalidRequest();
            }

            IReadOnlyList<object>? users = await service.FindUsersAsync(userIds, cancellationToken).ConfigureAwait(false);
            return users is null ? Missing("NO_SUCH_USER", "No such user.") : Results.Json(users);
        }

        if (String(body, "userId") is null && String(body, "username") is null)
        {
            return InvalidRequest();
        }

        object? user = await service.FindUserAsync(
            String(body, "userId"),
            String(body, "username"),
            String(body, "host"),
            cancellationToken).ConfigureAwait(false);
        return user is null ? Missing("NO_SUCH_USER", "No such user.") : Results.Json(user);
    }

    private static Task<IResult> FollowersAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken) =>
        UserFollowRelationsAsync(context, service, followers: true, cancellationToken);

    private static Task<IResult> FollowingAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken) =>
        UserFollowRelationsAsync(context, service, followers: false, cancellationToken);

    private static async Task<IResult> UserFollowRelationsAsync(
        HttpContext context,
        MisskeyQueryService service,
        bool followers,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? userId = String(body, "userId");
        string? username = String(body, "username");
        string? host = String(body, "host");
        string? sinceId = String(body, "sinceId");
        string? untilId = String(body, "untilId");
        int limit = Integer(body, "limit", 10);
        if (userId is null && username is null ||
            userId is not null && username is not null ||
            !IsOptionalString(body, "userId") ||
            !IsOptionalString(body, "username") ||
            !IsOptionalString(body, "host") ||
            !IsOptionalString(body, "sinceId") ||
            !IsOptionalString(body, "untilId") ||
            limit is < 1 or > 100)
        {
            return InvalidRequest();
        }

        IReadOnlyList<object>? values = await service.ReadUserFollowRelationsAsync(
            new(userId, username, host, sinceId, untilId, limit),
            followers,
            cancellationToken).ConfigureAwait(false);
        return values is null
            ? Error(404, "No such user.", "NO_SUCH_USER", followers
                ? "27fa5435-88ab-43de-9360-387de88727cd"
                : "63e4aba4-4156-4e53-be25-c9559e42d71b")
            : Results.Json(values);
    }

    private static async Task<IResult> ShowNoteAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? noteId = String(body, "noteId");
        if (noteId is null)
        {
            return Missing("NO_SUCH_NOTE", "No such note.");
        }

        object? note = await service.FindNoteAsync(
            noteId,
            await ViewerActorIriAsync(context.User, service, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        return note is null ? Missing("NO_SUCH_NOTE", "No such note.") : Results.Json(note);
    }

    private static async Task<IResult> TimelineAsync(
        HttpContext context,
        MisskeyQueryService service,
        string kind,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? viewer = await ViewerActorIriAsync(context.User, service, cancellationToken).ConfigureAwait(false);
        if (kind == "home" && viewer is null)
        {
            return Results.Forbid();
        }

        IReadOnlyList<object> notes = await service.ReadTimelineAsync(
            kind,
            viewer,
            String(body, "untilId"),
            Integer(body, "limit", 10),
            localOnly,
            cancellationToken).ConfigureAwait(false);
        return Results.Json(notes);
    }

    private static async Task<IResult> UserNotesAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? userId = String(body, "userId");
        if (userId is null)
        {
            return InvalidRequest();
        }

        IReadOnlyList<object>? notes = await service.ReadUserNotesAsync(
            userId,
            await ViewerActorIriAsync(context.User, service, cancellationToken).ConfigureAwait(false),
            String(body, "untilId"),
            Integer(body, "limit", 10),
            cancellationToken).ConfigureAwait(false);
        return notes is null
            ? Error(404, "No such user.", "NO_SUCH_USER", "27e494ba-2ac2-48e8-893b-10d4d8c2387b")
            : Results.Json(notes);
    }

    private static async Task<IResult> ReactionsAsync(
        HttpContext context,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        JsonElement body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);
        string? noteId = String(body, "noteId");
        if (noteId is null)
        {
            return InvalidRequest();
        }

        return Results.Json(await service.ReadReactionsAsync(
            noteId,
            Integer(body, "limit", 10),
            String(body, "type"),
            await ViewerActorIriAsync(context.User, service, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false));
    }

    private static Task<IResult> CreateReactionAsync(
        HttpContext context,
        MisskeyReactionService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, service.CreateAsync, cancellationToken);

    private static Task<IResult> DeleteReactionAsync(
        HttpContext context,
        MisskeyReactionService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, service.DeleteAsync, cancellationToken);

    private static async Task<IResult> CreateNoteAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken)
    {
        (string? username, string? idempotencyKey, IResult? error) = RequireCommandContext(context);
        if (error is not null)
        {
            return error;
        }

        MisskeyCreateNoteRequest? request = await DeserializeAsync<MisskeyCreateNoteRequest>(context, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return InvalidRequest();
        }

        try
        {
            object note = await service.CreateNoteAsync(username!, idempotencyKey!, request, cancellationToken).ConfigureAwait(false);
            return Results.Json(new { createdNote = note });
        }
        catch (MisskeyApiException exception)
        {
            return Results.Json(exception.Body, statusCode: exception.StatusCode);
        }
        catch (KeyNotFoundException exception)
        {
            return Missing("NO_SUCH_NOTE", exception.Message);
        }
    }

    private static async Task<IResult> DeleteNoteAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken)
    {
        (string? username, string? idempotencyKey, IResult? error) = RequireCommandContext(context);
        if (error is not null)
        {
            return error;
        }

        MisskeyDeleteNoteRequest? request = await DeserializeAsync<MisskeyDeleteNoteRequest>(context, cancellationToken).ConfigureAwait(false);
        if (request is null || string.IsNullOrWhiteSpace(request.NoteId))
        {
            return InvalidRequest();
        }

        try
        {
            await service.DeleteNoteAsync(username!, idempotencyKey!, request, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (MisskeyApiException exception)
        {
            return Results.Json(exception.Body, statusCode: exception.StatusCode);
        }
        catch (KeyNotFoundException exception)
        {
            return Missing("NO_SUCH_NOTE", exception.Message);
        }
    }

    private static async Task<IResult> VotePollAsync(
        HttpContext context,
        MisskeyCommandService service,
        CancellationToken cancellationToken)
    {
        (string? username, string? idempotencyKey, IResult? error) = RequireCommandContext(context);
        if (error is not null)
        {
            return error;
        }

        MisskeyPollVoteRequest? request = await DeserializeAsync<MisskeyPollVoteRequest>(context, cancellationToken).ConfigureAwait(false);
        if (request is null || string.IsNullOrWhiteSpace(request.NoteId) || request.Choice < 0)
        {
            return InvalidRequest();
        }

        try
        {
            await service.VotePollAsync(username!, idempotencyKey!, request, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (MisskeyApiException exception)
        {
            return Results.Json(exception.Body, statusCode: exception.StatusCode);
        }
        catch (KeyNotFoundException)
        {
            return Missing("NO_SUCH_NOTE", "No such note.");
        }
    }

    private static async Task<IResult> ExecuteAsync(
        HttpContext context,
        Func<string, string, MisskeyReactionRequest, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        string? username = context.User.FindFirst("preferred_username")?.Value ?? context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Forbid();
        }

        string? idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            idempotencyKey = "misskey-request-" + Guid.NewGuid().ToString("N");
        }
        else if (idempotencyKey.Length is < 8 or > 200 || idempotencyKey.Any(char.IsControl))
        {
            return Error(400, "Idempotency-Key must contain 8 to 200 non-control characters.", "INVALID_PARAM", "e8a7b1df-6dd4-4c67-a797-d6c2430b0d7a");
        }

        MisskeyReactionRequest? request;
        try
        {
            byte[] body = await ReadBoundedJsonBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            request = JsonSerializer.Deserialize<MisskeyReactionRequest>(
                body,
                WebJsonOptions);
        }
        catch (JsonException)
        {
            return InvalidRequest();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.NoteId))
        {
            return InvalidRequest();
        }

        try
        {
            await action(username, idempotencyKey, request, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (MisskeyApiException exception)
        {
            return Results.Json(exception.Body, statusCode: exception.StatusCode);
        }
    }

    private static (string? Username, string? IdempotencyKey, IResult? Error) RequireCommandContext(HttpContext context)
    {
        string? username = AuthenticatedUsername(context.User);
        if (string.IsNullOrWhiteSpace(username))
        {
            return (null, null, Results.Forbid());
        }

        string? key = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "misskey-request-" + Guid.NewGuid().ToString("N");
        }
        else if (key.Length is < 8 or > 200 || key.Any(char.IsControl))
        {
            return (null, null, Error(400, "Idempotency-Key must contain 8 to 200 non-control characters.", "INVALID_PARAM", "e8a7b1df-6dd4-4c67-a797-d6c2430b0d7a"));
        }

        return (username, key, null);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            byte[] body = await ReadBoundedJsonBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(body, WebJsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static IResult InvalidRequest() =>
        Error(400, "Invalid param.", "INVALID_PARAM", "3d81ceae-475f-4600-b2a8-2bc116157532");

    private static IResult Error(int status, string message, string code, string id) =>
        Results.Json(new MisskeyApiErrorBody(new(message, code, id, "client")), statusCode: status);

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            byte[] body = await ReadBoundedJsonBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(
                body,
                new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : EmptyObject();
        }
        catch (JsonException)
        {
            return EmptyObject();
        }
    }

    private static async Task<byte[]> ReadBoundedJsonBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > MaximumJsonRequestBodyBytes)
        {
            throw new BadHttpRequestException(
                "The Misskey API request is too large.",
                StatusCodes.Status413PayloadTooLarge);
        }

        await using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int total = 0;
        int read;
        while ((read = await request.Body.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaximumJsonRequestBodyBytes)
            {
                throw new BadHttpRequestException(
                    "The Misskey API request is too large.",
                    StatusCodes.Status413PayloadTooLarge);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private static JsonElement EmptyObject()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static string? String(JsonElement body, string property) =>
        body.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[]? StringArray(JsonElement body, string property)
    {
        if (!body.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text)
            {
                return null;
            }

            values.Add(text);
        }

        return values.ToArray();
    }

    private static int Integer(JsonElement body, string property, int fallback) =>
        body.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int parsed)
            ? parsed
            : fallback;

    private static long? Integer64(JsonElement body, string property) =>
        body.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long parsed)
            ? parsed
            : null;

    private static bool Boolean(JsonElement body, string property, bool fallback) =>
        body.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static bool? NullableBoolean(JsonElement body, string property) =>
        body.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static bool IsOptionalString(JsonElement body, string property) =>
        !body.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.String;

    private static bool IsOptionalBoolean(JsonElement body, string property) =>
        !body.TryGetProperty(property, out JsonElement value) || value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool TryAnnouncementMutation(
        JsonElement body,
        out MisskeyAnnouncementMutation mutation)
    {
        mutation = null!;
        string? title = String(body, "title");
        string? text = String(body, "text");
        if (title is null || text is null ||
            !body.TryGetProperty("imageUrl", out JsonElement imageElement) ||
            imageElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.String)
        {
            return false;
        }

        mutation = new(title, text, imageElement.ValueKind == JsonValueKind.Null ? null : imageElement.GetString());
        return true;
    }

    private static IResult ApiError(MisskeyApiException exception) =>
        Results.Json(exception.Body, statusCode: exception.StatusCode);

    private static IResult AnnouncementImageError(AnnouncementImageImportException exception) => exception.Failure switch
    {
        AnnouncementImageImportFailure.InvalidSource =>
            Error(400, exception.Message, "INVALID_PARAM", "8b7b61d1-d744-4309-a542-941dc84f2ddf"),
        AnnouncementImageImportFailure.RejectedByPolicy =>
            Error(403, exception.Message, "REMOTE_MEDIA_REJECTED", "2b08991d-08fd-4b31-b90d-e1327e35c9ae"),
        AnnouncementImageImportFailure.MediaUnavailable =>
            Error(503, exception.Message, "MEDIA_UNAVAILABLE", "865827f8-4014-4979-8da3-301f92c6dc92"),
        _ => Error(502, exception.Message, "REMOTE_MEDIA_IMPORT_FAILED", "815b2bbc-b9d5-4fb8-9d81-6c0417932326")
    };

    private static string? AuthenticatedUsername(ClaimsPrincipal principal) =>
        principal.FindFirst("preferred_username")?.Value ?? principal.Identity?.Name;

    private static async Task<string?> ViewerActorIriAsync(
        ClaimsPrincipal principal,
        MisskeyQueryService service,
        CancellationToken cancellationToken)
    {
        string? direct = principal.FindFirst("actor")?.Value;
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        string? username = AuthenticatedUsername(principal);
        return username is null
            ? null
            : await service.FindViewerActorIriAsync(username, cancellationToken).ConfigureAwait(false);
    }

    private static IResult Missing(string code, string message) =>
        Error(404, message, code, "27e0c8c2-9c4a-4f77-90e7-657c2d5b9814");
}
