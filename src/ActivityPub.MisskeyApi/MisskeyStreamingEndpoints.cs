using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.MisskeyApi;

internal static class MisskeyStreamingEndpoints
{
    private const string ResumeProtocolVersion = "v1";
    private const WebSocketCloseStatus AuthenticationRequiredCloseStatus = (WebSocketCloseStatus)4401;
    private const WebSocketCloseStatus SlowConsumerCloseStatus = (WebSocketCloseStatus)4408;
    private const WebSocketCloseStatus ResyncRequiredCloseStatus = (WebSocketCloseStatus)4409;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task StreamAsync(
        HttpContext context,
        MisskeyQueryService query,
        IDurableStreamEventPump pump,
        IStreamEventStore store,
        IStreamConnectionLeaseStore connectionLeases,
        StreamingRuntimeIdentity runtimeIdentity,
        StreamingOptions options,
        IMisskeyAuthenticationService misskeyAuthentication,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new { error = new { message = "The streaming endpoint requires WebSocket.", code = "WEBSOCKET_REQUIRED" } },
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        bool resumeProtocol = string.Equals(
            context.Request.Query["resume"].FirstOrDefault(),
            ResumeProtocolVersion,
            StringComparison.Ordinal);
        string? viewerActorIri = await FindViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        StreamAuthenticationState? authenticationState = await CreateAuthenticationStateAsync(
            context,
            context.RequestServices).ConfigureAwait(false);
        (bool valid, long cursor) = await ResolveCursorAsync(context, store, cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            if (resumeProtocol)
            {
                using WebSocket rejectedSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                await SendProtocolErrorAndCloseAsync(
                    rejectedSocket,
                    "RESYNC_REQUIRED",
                    "CURSOR_EXPIRED",
                    ResyncRequiredCloseStatus,
                    "resync required").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status410Gone;
            await context.Response.WriteAsJsonAsync(
                new { error = new { message = "The stream cursor is no longer retained.", code = "CURSOR_EXPIRED" } },
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        string remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        StreamConnectionLeaseToken? lease = await connectionLeases.TryAcquireAsync(
            viewerActorIri,
            remoteAddress,
            runtimeIdentity.InstanceId,
            options.MaximumConnectionsPerUser,
            options.MaximumConnectionsPerIp,
            DateTimeOffset.UtcNow,
            options.ConnectionLeaseDuration,
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "60";
            await context.Response.WriteAsJsonAsync(
                new { error = new { message = "Streaming connection limit exceeded.", code = "RATE_LIMIT_EXCEEDED" } },
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task renewal = RenewLeaseAsync(connectionLeases, lease, options, connectionCancellation);
        try
        {
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            try
            {
                await RunAsync(
                    socket,
                    viewerActorIri,
                    cursor,
                    query,
                    pump,
                    options,
                    resumeProtocol,
                    authenticationState,
                    misskeyAuthentication,
                    context.RequestServices,
                    connectionCancellation.Token).ConfigureAwait(false);
            }
            catch (StreamSlowConsumerException) when (resumeProtocol)
            {
                await SendProtocolErrorAndCloseAsync(
                    socket,
                    "SLOW_CONSUMER",
                    "The bounded stream buffer was exhausted.",
                    SlowConsumerCloseStatus,
                    "slow consumer").ConfigureAwait(false);
            }
            catch (StreamCursorExpiredException) when (resumeProtocol)
            {
                await SendProtocolErrorAndCloseAsync(
                    socket,
                    "RESYNC_REQUIRED",
                    "CURSOR_EXPIRED",
                    ResyncRequiredCloseStatus,
                    "resync required").ConfigureAwait(false);
            }
        }
        finally
        {
            connectionCancellation.Cancel();
            await ObserveRenewalAsync(renewal).ConfigureAwait(false);
            await connectionLeases.ReleaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task RunAsync(
        WebSocket socket,
        string? viewerActorIri,
        long cursor,
        MisskeyQueryService query,
        IDurableStreamEventPump pump,
        StreamingOptions options,
        bool resumeProtocol,
        StreamAuthenticationState? authenticationState,
        IMisskeyAuthenticationService misskeyAuthentication,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var subscriptions = new Dictionary<string, MisskeySubscription>(StringComparer.Ordinal);
        var capturedNotes = new HashSet<string>(StringComparer.Ordinal);
        Task<string?> receive = ReceiveTextAsync(socket, options.MaximumInboundMessageBytes, cancellationToken);
        if (resumeProtocol)
        {
            while (socket.State == WebSocketState.Open &&
                   subscriptions.Count == 0 &&
                   !cancellationToken.IsCancellationRequested)
            {
                string? message = await receive.ConfigureAwait(false);
                if (message is null)
                {
                    return;
                }

                if (!await ValidateAuthenticationStateAsync(
                        authenticationState,
                        misskeyAuthentication,
                        services,
                        query,
                        cancellationToken).ConfigureAwait(false))
                {
                    await SendAuthenticationExpiredAndCloseAsync(socket).ConfigureAwait(false);
                    return;
                }

                await ApplyClientMessageAsync(
                    socket,
                    message,
                    subscriptions,
                    capturedNotes,
                    viewerActorIri is not null,
                    forceAcknowledgement: true,
                    resumeProtocol,
                    cancellationToken).ConfigureAwait(false);
                if (socket.State == WebSocketState.Open)
                {
                    receive = ReceiveTextAsync(socket, options.MaximumInboundMessageBytes, cancellationToken);
                }
            }

            if (socket.State != WebSocketState.Open || cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }

        await using IAsyncEnumerator<StreamEvent> enumerator = pump.SubscribeAsync(
            cursor,
            options.BufferCapacity,
            options.PollInterval,
            cancellationToken).GetAsyncEnumerator(cancellationToken);
        Task<bool> moveNext = enumerator.MoveNextAsync().AsTask();
        Task heartbeat = Task.Delay(options.HeartbeatInterval, cancellationToken);
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            Task completed = await Task.WhenAny(moveNext, receive, heartbeat).ConfigureAwait(false);
            if (completed == heartbeat)
            {
                if (!await ValidateAuthenticationStateAsync(
                        authenticationState,
                        misskeyAuthentication,
                        services,
                        query,
                        cancellationToken).ConfigureAwait(false))
                {
                    await SendAuthenticationExpiredAndCloseAsync(socket).ConfigureAwait(false);
                    return;
                }

                heartbeat = Task.Delay(options.HeartbeatInterval, cancellationToken);
                continue;
            }

            if (completed == receive)
            {
                string? message = await receive.ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                await ApplyClientMessageAsync(
                    socket,
                    message,
                    subscriptions,
                    capturedNotes,
                    viewerActorIri is not null,
                    forceAcknowledgement: resumeProtocol,
                    resumeProtocol,
                    cancellationToken).ConfigureAwait(false);
                if (socket.State == WebSocketState.Open)
                {
                    receive = ReceiveTextAsync(socket, options.MaximumInboundMessageBytes, cancellationToken);
                }

                continue;
            }

            if (!await moveNext.ConfigureAwait(false))
            {
                break;
            }

            StreamEvent item = enumerator.Current;
            if (!await ValidateAuthenticationStateAsync(
                    authenticationState,
                    misskeyAuthentication,
                    services,
                    query,
                    cancellationToken).ConfigureAwait(false))
            {
                await SendAuthenticationExpiredAndCloseAsync(socket).ConfigureAwait(false);
                return;
            }

            if (!await PublishCapturedNoteAsync(
                socket,
                item,
                capturedNotes,
                viewerActorIri,
                query,
                resumeProtocol,
                authenticationState,
                misskeyAuthentication,
                services,
                cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (item.Kind == StreamEventKind.RelationshipChanged &&
                item.ResourceId is { } targetActorId &&
                viewerActorIri is not null &&
                string.Equals(item.RecipientActorIri, viewerActorIri, StringComparison.Ordinal))
            {
                MisskeyRelationshipStreamProjection? relationship = await query.FindStreamRelationshipUserAsync(
                    targetActorId,
                    viewerActorIri,
                    cancellationToken).ConfigureAwait(false);
                if (relationship is not null)
                {
                    foreach (MisskeySubscription subscription in subscriptions.Values.Where(x => x.Kind == MisskeyStreamKind.Main).ToArray())
                    {
                        if (!await SendDurablePayloadAsync(socket, "channel", new
                        {
                            id = subscription.Id,
                            type = relationship.Type,
                            body = relationship.User
                        }, item.Cursor, resumeProtocol, authenticationState, misskeyAuthentication, services, query, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            return;
                        }
                    }
                }
            }

            if (item.Kind == StreamEventKind.NotificationCreated &&
                item.ResourceId is { } notificationId &&
                viewerActorIri is not null &&
                string.Equals(item.RecipientActorIri, viewerActorIri, StringComparison.Ordinal))
            {
                object? notification = await query.FindStreamNotificationAsync(
                    notificationId,
                    viewerActorIri,
                    cancellationToken).ConfigureAwait(false);
                if (notification is not null)
                {
                    foreach (MisskeySubscription subscription in subscriptions.Values.Where(x => x.Kind == MisskeyStreamKind.Main).ToArray())
                    {
                        if (!await SendDurablePayloadAsync(socket, "channel", new
                        {
                            id = subscription.Id,
                            type = "notification",
                            body = notification
                        }, item.Cursor, resumeProtocol, authenticationState, misskeyAuthentication, services, query, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            return;
                        }
                    }
                }
            }

            if (item.Kind == StreamEventKind.PostCreated && item.ResourceId is { } resourceId)
            {
                foreach (MisskeySubscription subscription in subscriptions.Values.ToArray())
                {
                    object? note = await ProjectForSubscriptionAsync(
                        resourceId,
                        viewerActorIri,
                        subscription,
                        query,
                        cancellationToken).ConfigureAwait(false);
                    if (note is not null)
                    {
                        if (!await SendDurablePayloadAsync(socket, "channel", new
                        {
                            id = subscription.Id,
                            type = "note",
                            body = note
                        }, item.Cursor, resumeProtocol, authenticationState, misskeyAuthentication, services, query, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            return;
                        }
                    }
                }
            }

            if (resumeProtocol)
            {
                if (!await SendDurablePayloadAsync(
                    socket,
                    "checkpoint",
                    new { cursor = item.Cursor },
                    item.Cursor,
                    includeCursor: true,
                    authenticationState,
                    misskeyAuthentication,
                    services,
                    query,
                    cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }

            moveNext = enumerator.MoveNextAsync().AsTask();
        }

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stream ended", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<StreamAuthenticationState?> CreateAuthenticationStateAsync(
        HttpContext context,
        IServiceProvider services)
    {
        ClaimsIdentity? identity = context.User.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated);
        if (identity?.AuthenticationType is null)
        {
            return null;
        }

        string actorIri = context.User.FindFirst("actor")?.Value ??
            context.User.FindFirst(LocalAccountServiceCollectionExtensions.LocalActorClaim)?.Value ??
            string.Empty;
        string username = context.User.FindFirst("preferred_username")?.Value ??
            context.User.Identity?.Name ??
            string.Empty;
        if (string.IsNullOrWhiteSpace(actorIri) || string.IsNullOrWhiteSpace(username))
        {
            return new(identity.AuthenticationType, actorIri, username, null, null, null, null, null);
        }

        if (string.Equals(identity.AuthenticationType, MisskeyTokenAuthenticationHandler.SchemeName, StringComparison.Ordinal))
        {
            string? rawToken = ReadMisskeyToken(context.Request);
            Guid? tokenId = Guid.TryParseExact(context.User.FindFirst("sub")?.Value, "N", out Guid parsedTokenId)
                ? parsedTokenId
                : null;
            return new(identity.AuthenticationType, actorIri, username, rawToken, tokenId, null, null, null);
        }

        if (context.User.HasClaim(FrontendBrowserSessionMetadata.SessionClaim, "true"))
        {
            UserManager<LocalIdentityUser>? users = services.GetService<UserManager<LocalIdentityUser>>();
            Guid? userId = Guid.TryParseExact(
                    context.User.FindFirst(LocalAccountServiceCollectionExtensions.LocalIdentityClaim)?.Value,
                    "N",
                    out Guid parsedUserId)
                ? parsedUserId
                : null;
            string? securityStamp = users is null
                ? null
                : context.User.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value;
            AuthenticateResult authentication = await context.AuthenticateAsync(
                OAuthAuthorizationServerExtensions.ExternalSessionScheme).ConfigureAwait(false);
            return new(
                OAuthAuthorizationServerExtensions.ExternalSessionScheme,
                actorIri,
                username,
                null,
                null,
                userId,
                securityStamp,
                authentication.Properties?.ExpiresUtc);
        }

        return new(identity.AuthenticationType, actorIri, username, null, null, null, null, null);
    }

    private static async Task<bool> ValidateAuthenticationStateAsync(
        StreamAuthenticationState? state,
        IMisskeyAuthenticationService misskeyAuthentication,
        IServiceProvider services,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        if (state is null)
        {
            return true;
        }

        if (state.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        if (string.Equals(state.AuthenticationType, MisskeyTokenAuthenticationHandler.SchemeName, StringComparison.Ordinal))
        {
            if (state.RawToken is null || state.TokenId is null)
            {
                return false;
            }

            MisskeyTokenPrincipal? principal = await misskeyAuthentication.ValidateAsync(
                state.RawToken,
                cancellationToken).ConfigureAwait(false);
            return principal is not null &&
                principal.TokenId == state.TokenId &&
                string.Equals(principal.ActorIri, state.ActorIri, StringComparison.Ordinal) &&
                string.Equals(principal.Username, state.Username, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(
                state.AuthenticationType,
                OAuthAuthorizationServerExtensions.ExternalSessionScheme,
                StringComparison.Ordinal))
        {
            if (state.UserId is null || string.IsNullOrWhiteSpace(state.SecurityStamp))
            {
                return false;
            }

            await using AsyncServiceScope validationScope = services.CreateAsyncScope();
            UserManager<LocalIdentityUser>? users = validationScope.ServiceProvider
                .GetService<UserManager<LocalIdentityUser>>();
            if (users is null)
            {
                return false;
            }

            LocalIdentityUser? user = await users.FindByIdAsync(state.UserId.Value.ToString()).ConfigureAwait(false);
            string? storedStamp = user is null ? null : await users.GetSecurityStampAsync(user).ConfigureAwait(false);
            if (user is null ||
                user.ProvisioningState != LocalAccountProvisioningState.Active ||
                !string.Equals(state.SecurityStamp, storedStamp, StringComparison.Ordinal) ||
                !string.Equals(state.ActorIri, user.LocalActorIri, StringComparison.Ordinal))
            {
                return false;
            }
        }

        string? activeActorIri = await query.FindViewerActorIriAsync(
            state.Username,
            cancellationToken).ConfigureAwait(false);
        return string.Equals(activeActorIri, state.ActorIri, StringComparison.Ordinal);
    }

    private static string? ReadMisskeyToken(HttpRequest request)
    {
        string authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer mk_", StringComparison.Ordinal))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        return request.Query.TryGetValue("i", out var values) && values.Count == 1
            ? values[0]
            : null;
    }

    private static Task SendAuthenticationExpiredAndCloseAsync(WebSocket socket) =>
        SendProtocolErrorAndCloseAsync(
            socket,
            "AUTHENTICATION_EXPIRED",
            "The streaming authentication is no longer valid.",
            AuthenticationRequiredCloseStatus,
            "authentication expired");

    private static async Task<object?> ProjectForSubscriptionAsync(
        Guid resourceId,
        string? viewerActorIri,
        MisskeySubscription subscription,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        if (subscription.Kind == MisskeyStreamKind.Hybrid)
        {
            object? home = await query.FindStreamNoteAsync(
                resourceId,
                viewerActorIri,
                ClientStreamAudience.Home,
                false,
                cancellationToken).ConfigureAwait(false);
            return home ?? await query.FindStreamNoteAsync(
                resourceId,
                viewerActorIri,
                ClientStreamAudience.Public,
                true,
                cancellationToken).ConfigureAwait(false);
        }

        return await query.FindStreamNoteAsync(
            resourceId,
            viewerActorIri,
            subscription.Kind == MisskeyStreamKind.Public
                ? ClientStreamAudience.Public
                : ClientStreamAudience.Home,
            subscription.LocalOnly,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> PublishCapturedNoteAsync(
        WebSocket socket,
        StreamEvent item,
        HashSet<string> capturedNotes,
        string? viewerActorIri,
        MisskeyQueryService query,
        bool resumeProtocol,
        StreamAuthenticationState? authenticationState,
        IMisskeyAuthenticationService misskeyAuthentication,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (item.ResourceId is not { } resourceId || item.Kind is not (
                StreamEventKind.PostUpdated or StreamEventKind.PostDeleted or StreamEventKind.ReactionChanged or StreamEventKind.PollVoted))
        {
            return true;
        }

        string noteId = await query.MapStreamNoteIdAsync(resourceId, item.OccurredAt, cancellationToken).ConfigureAwait(false);
        if (!capturedNotes.Contains(noteId) || !await query.CanReceiveStreamEventAsync(
                item,
                viewerActorIri,
                ClientStreamAudience.Home,
                false,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (item.Kind == StreamEventKind.ReactionChanged)
        {
            string? userId = await query.MapStreamActorIdAsync(item.ActorIri, cancellationToken).ConfigureAwait(false);
            if (userId is null || item.Reaction is null)
            {
                return true;
            }

            return await SendDurablePayloadAsync(socket, "noteUpdated", new
            {
                id = noteId,
                type = item.ReactionRemoved == true ? "unreacted" : "reacted",
                body = new { reaction = item.Reaction, userId }
            }, item.Cursor, resumeProtocol, authenticationState, misskeyAuthentication, services, query, cancellationToken)
                .ConfigureAwait(false);
        }

        if (item.Kind == StreamEventKind.PollVoted)
        {
            string? userId = await query.MapStreamActorIdAsync(item.ActorIri, cancellationToken).ConfigureAwait(false);
            if (userId is null || item.PollChoiceIndex is not { } choice)
            {
                return true;
            }

            return await SendDurablePayloadAsync(socket, "noteUpdated", new
            {
                id = noteId,
                type = "pollVoted",
                body = new { choice, userId }
            }, item.Cursor, resumeProtocol, authenticationState, misskeyAuthentication, services, query, cancellationToken)
                .ConfigureAwait(false);
        }

        object body;
        string type;
        if (item.Kind == StreamEventKind.PostDeleted)
        {
            type = "deleted";
            body = new { deletedAt = item.OccurredAt };
        }
        else
        {
            type = "updated";
            object? note = await query.FindStreamNoteAsync(
                resourceId,
                viewerActorIri,
                ClientStreamAudience.Home,
                false,
                cancellationToken).ConfigureAwait(false);
            if (note is null)
            {
                return true;
            }

            body = note;
        }

        return await SendDurablePayloadAsync(
            socket,
            "noteUpdated",
            new { id = noteId, type, body },
            item.Cursor,
            resumeProtocol,
            authenticationState,
            misskeyAuthentication,
            services,
            query,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> SendDurablePayloadAsync(
        WebSocket socket,
        string type,
        object body,
        long cursor,
        bool includeCursor,
        StreamAuthenticationState? authenticationState,
        IMisskeyAuthenticationService misskeyAuthentication,
        IServiceProvider services,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        if (!await ValidateAuthenticationStateAsync(
                authenticationState,
                misskeyAuthentication,
                services,
                query,
                cancellationToken).ConfigureAwait(false))
        {
            await SendAuthenticationExpiredAndCloseAsync(socket).ConfigureAwait(false);
            return false;
        }

        await SendAsync(socket, type, body, cursor, includeCursor, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task ApplyClientMessageAsync(
        WebSocket socket,
        string message,
        IDictionary<string, MisskeySubscription> subscriptions,
        HashSet<string> capturedNotes,
        bool authenticated,
        bool forceAcknowledgement,
        bool resumeProtocol,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            await SendAsync(socket, "error", new { code = "INVALID_MESSAGE" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            string? type = ReadString(root, "type");
            if (!root.TryGetProperty("body", out JsonElement body) || body.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (type is "subNote" or "s" or "sr")
            {
                string? noteId = ReadString(body, "id");
                if (!string.IsNullOrWhiteSpace(noteId))
                {
                    capturedNotes.Add(noteId);
                }

                return;
            }

            if (type is "unsubNote" or "un")
            {
                string? noteId = ReadString(body, "id");
                if (noteId is not null)
                {
                    capturedNotes.Remove(noteId);
                }

                return;
            }

            if (type == "disconnect")
            {
                string? id = ReadString(body, "id");
                if (id is not null)
                {
                    subscriptions.Remove(id);
                }

                return;
            }

            if (type != "connect")
            {
                return;
            }

            string? connectionId = ReadString(body, "id");
            string? channel = ReadString(body, "channel");
            bool pong = body.TryGetProperty("pong", out JsonElement pongElement) && pongElement.ValueKind == JsonValueKind.True;
            MisskeySubscription? subscription = (connectionId, channel) switch
            {
                ({ Length: > 0 }, "globalTimeline") => new(connectionId, MisskeyStreamKind.Public, false),
                ({ Length: > 0 }, "localTimeline") => new(connectionId, MisskeyStreamKind.Public, true),
                ({ Length: > 0 }, "homeTimeline") when authenticated => new(connectionId, MisskeyStreamKind.Home, false),
                ({ Length: > 0 }, "hybridTimeline") when authenticated => new(connectionId, MisskeyStreamKind.Hybrid, false),
                ({ Length: > 0 }, "main") when authenticated => new(connectionId, MisskeyStreamKind.Main, false),
                _ => null
            };
            if (subscription is null)
            {
                if (resumeProtocol && !authenticated && channel is "homeTimeline" or "hybridTimeline" or "main")
                {
                    await SendProtocolErrorAndCloseAsync(
                        socket,
                        "AUTHENTICATION_REQUIRED",
                        "The requested channel requires an authenticated browser session.",
                        AuthenticationRequiredCloseStatus,
                        "authentication required").ConfigureAwait(false);
                    return;
                }

                await SendAsync(socket, "error", new
                {
                    code = authenticated ? "UNSUPPORTED_CHANNEL" : "AUTHENTICATION_REQUIRED",
                    id = connectionId,
                    channel
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            subscriptions[subscription.Id] = subscription;
            if (pong || forceAcknowledgement)
            {
                await SendAsync(socket, "connected", new { id = subscription.Id }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendProtocolErrorAndCloseAsync(
        WebSocket socket,
        string code,
        string reason,
        WebSocketCloseStatus closeStatus,
        string closeDescription)
    {
        if (socket.State == WebSocketState.Open)
        {
            await SendAsync(socket, "error", new { code, reason }, CancellationToken.None).ConfigureAwait(false);
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(closeStatus, closeDescription, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task SendAsync(
        WebSocket socket,
        string type,
        object body,
        CancellationToken cancellationToken)
    {
        await SendAsync(socket, type, body, cursor: 0, includeCursor: false, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendAsync(
        WebSocket socket,
        string type,
        object body,
        long cursor,
        bool includeCursor,
        CancellationToken cancellationToken)
    {
        byte[] payload = includeCursor
            ? JsonSerializer.SerializeToUtf8Bytes(new { type, body, cursor }, JsonOptions)
            : JsonSerializer.SerializeToUtf8Bytes(new { type, body }, JsonOptions);
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(bool Valid, long Cursor)> ResolveCursorAsync(
        HttpContext context,
        IStreamEventStore store,
        CancellationToken cancellationToken)
    {
        string? raw = context.Request.Query["cursor"].FirstOrDefault();
        if (raw is not null && (!long.TryParse(raw, out long parsed) || parsed < 0))
        {
            return (false, 0);
        }

        long requested = raw is null ? 0 : long.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        StreamEventPage page = await store.ReadAfterAsync(requested, 1, cancellationToken).ConfigureAwait(false);
        return page.RequestedCursorExpired
            ? (false, 0)
            : (true, raw is null ? page.LatestCursor ?? 0 : requested);
    }

    private static async Task<string?> FindViewerActorIriAsync(
        ClaimsPrincipal principal,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        string? direct = principal.FindFirst("actor")?.Value;
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        string? username = principal.FindFirst("preferred_username")?.Value ?? principal.Identity?.Name;
        return username is null ? null : await query.FindViewerActorIriAsync(username, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReceiveTextAsync(
        WebSocket socket,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[Math.Min(maximumBytes, 16 * 1024)];
        using var output = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text || output.Length + result.Count > maximumBytes)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out JsonElement item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static async Task RenewLeaseAsync(
        IStreamConnectionLeaseStore store,
        StreamConnectionLeaseToken lease,
        StreamingOptions options,
        CancellationTokenSource connectionCancellation)
    {
        try
        {
            while (!connectionCancellation.IsCancellationRequested)
            {
                await Task.Delay(options.ConnectionLeaseRenewalInterval, connectionCancellation.Token).ConfigureAwait(false);
                bool renewed = await store.ExtendAsync(
                    lease,
                    DateTimeOffset.UtcNow,
                    options.ConnectionLeaseDuration,
                    connectionCancellation.Token).ConfigureAwait(false);
                if (!renewed)
                {
                    connectionCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            connectionCancellation.Cancel();
        }
    }

    private static async Task ObserveRenewalAsync(Task renewal)
    {
        try
        {
            await renewal.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private enum MisskeyStreamKind
    {
        Public,
        Home,
        Hybrid,
        Main
    }

    private sealed record MisskeySubscription(string Id, MisskeyStreamKind Kind, bool LocalOnly);

    private sealed record StreamAuthenticationState(
        string AuthenticationType,
        string ActorIri,
        string Username,
        string? RawToken,
        Guid? TokenId,
        Guid? UserId,
        string? SecurityStamp,
        DateTimeOffset? ExpiresAt);
}
