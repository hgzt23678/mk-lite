using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Http;

namespace ActivityPub.MisskeyApi;

internal static class MisskeyStreamingEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task StreamAsync(
        HttpContext context,
        MisskeyQueryService query,
        IDurableStreamEventPump pump,
        IStreamEventStore store,
        IStreamConnectionLeaseStore connectionLeases,
        StreamingRuntimeIdentity runtimeIdentity,
        StreamingOptions options,
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

        string? viewerActorIri = await FindViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        (bool valid, long cursor) = await ResolveCursorAsync(context, store, cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
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
            await RunAsync(socket, viewerActorIri, cursor, query, pump, options, connectionCancellation.Token).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        var subscriptions = new Dictionary<string, MisskeySubscription>(StringComparer.Ordinal);
        var capturedNotes = new HashSet<string>(StringComparer.Ordinal);
        await using IAsyncEnumerator<StreamEvent> enumerator = pump.SubscribeAsync(
            cursor,
            options.BufferCapacity,
            options.PollInterval,
            cancellationToken).GetAsyncEnumerator(cancellationToken);
        Task<bool> moveNext = enumerator.MoveNextAsync().AsTask();
        Task<string?> receive = ReceiveTextAsync(socket, options.MaximumInboundMessageBytes, cancellationToken);
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            Task heartbeat = Task.Delay(options.HeartbeatInterval, cancellationToken);
            Task completed = await Task.WhenAny(moveNext, receive, heartbeat).ConfigureAwait(false);
            if (completed == heartbeat)
            {
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
                    cancellationToken).ConfigureAwait(false);
                receive = ReceiveTextAsync(socket, options.MaximumInboundMessageBytes, cancellationToken);
                continue;
            }

            if (!await moveNext.ConfigureAwait(false))
            {
                break;
            }

            StreamEvent item = enumerator.Current;
            await PublishCapturedNoteAsync(socket, item, capturedNotes, viewerActorIri, query, cancellationToken).ConfigureAwait(false);
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
                        await SendAsync(socket, "channel", new
                        {
                            id = subscription.Id,
                            type = relationship.Type,
                            body = relationship.User
                        }, cancellationToken).ConfigureAwait(false);
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
                        await SendAsync(socket, "channel", new
                        {
                            id = subscription.Id,
                            type = "notification",
                            body = notification
                        }, cancellationToken).ConfigureAwait(false);
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
                        await SendAsync(socket, "channel", new
                        {
                            id = subscription.Id,
                            type = "note",
                            body = note
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            moveNext = enumerator.MoveNextAsync().AsTask();
        }

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stream ended", CancellationToken.None).ConfigureAwait(false);
        }
    }

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

    private static async Task PublishCapturedNoteAsync(
        WebSocket socket,
        StreamEvent item,
        HashSet<string> capturedNotes,
        string? viewerActorIri,
        MisskeyQueryService query,
        CancellationToken cancellationToken)
    {
        if (item.ResourceId is not { } resourceId || item.Kind is not (
                StreamEventKind.PostUpdated or StreamEventKind.PostDeleted or StreamEventKind.ReactionChanged or StreamEventKind.PollVoted))
        {
            return;
        }

        string noteId = await query.MapStreamNoteIdAsync(resourceId, item.OccurredAt, cancellationToken).ConfigureAwait(false);
        if (!capturedNotes.Contains(noteId) || !await query.CanReceiveStreamEventAsync(
                item,
                viewerActorIri,
                ClientStreamAudience.Home,
                false,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (item.Kind == StreamEventKind.ReactionChanged)
        {
            string? userId = await query.MapStreamActorIdAsync(item.ActorIri, cancellationToken).ConfigureAwait(false);
            if (userId is null || item.Reaction is null)
            {
                return;
            }

            await SendAsync(socket, "noteUpdated", new
            {
                id = noteId,
                type = item.ReactionRemoved == true ? "unreacted" : "reacted",
                body = new { reaction = item.Reaction, userId }
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (item.Kind == StreamEventKind.PollVoted)
        {
            string? userId = await query.MapStreamActorIdAsync(item.ActorIri, cancellationToken).ConfigureAwait(false);
            if (userId is null || item.PollChoiceIndex is not { } choice)
            {
                return;
            }

            await SendAsync(socket, "noteUpdated", new
            {
                id = noteId,
                type = "pollVoted",
                body = new { choice, userId }
            }, cancellationToken).ConfigureAwait(false);
            return;
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
                return;
            }

            body = note;
        }

        await SendAsync(socket, "noteUpdated", new { id = noteId, type, body }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyClientMessageAsync(
        WebSocket socket,
        string message,
        IDictionary<string, MisskeySubscription> subscriptions,
        HashSet<string> capturedNotes,
        bool authenticated,
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
                await SendAsync(socket, "error", new
                {
                    code = authenticated ? "UNSUPPORTED_CHANNEL" : "AUTHENTICATION_REQUIRED",
                    id = connectionId,
                    channel
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            subscriptions[subscription.Id] = subscription;
            if (pong)
            {
                await SendAsync(socket, "connected", new { id = subscription.Id }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendAsync(
        WebSocket socket,
        string type,
        object body,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { type, body }, JsonOptions);
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
}
