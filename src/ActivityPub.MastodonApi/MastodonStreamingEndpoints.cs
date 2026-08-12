using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.Http;

namespace ActivityPub.MastodonApi;

internal static class MastodonStreamingEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task StreamAsync(
        HttpContext context,
        string? any,
        MastodonQueryService query,
        IDurableStreamEventPump pump,
        IStreamEventStore store,
        IStreamConnectionLeaseStore connectionLeases,
        StreamingRuntimeIdentity runtimeIdentity,
        StreamingOptions options,
        CancellationToken cancellationToken)
    {
        MastodonStreamSpec? spec = ParseSpec(context, any);
        if (spec is null)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest, "Unsupported or missing stream.", cancellationToken).ConfigureAwait(false);
            return;
        }

        string? viewerActorIri = await FindViewerActorIriAsync(context.User, query, cancellationToken).ConfigureAwait(false);
        if (spec.Audience == ClientStreamAudience.Home && viewerActorIri is null)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized, "This stream requires authentication.", cancellationToken).ConfigureAwait(false);
            return;
        }

        (bool valid, long cursor, string? error) = await ResolveCursorAsync(context, store, cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status410Gone, error!, cancellationToken).ConfigureAwait(false);
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
            context.Response.Headers.RetryAfter = "60";
            await WriteJsonErrorAsync(context, StatusCodes.Status429TooManyRequests, "Streaming connection limit exceeded.", cancellationToken).ConfigureAwait(false);
            return;
        }

        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task renewal = RenewLeaseAsync(connectionLeases, lease, options, connectionCancellation);
        try
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                await RunWebSocketAsync(context, spec, viewerActorIri, cursor, query, pump, options, connectionCancellation.Token).ConfigureAwait(false);
                return;
            }

            if (!context.Request.GetTypedHeaders().Accept?.Any(value =>
                    string.Equals(value.MediaType.Value, "text/event-stream", StringComparison.OrdinalIgnoreCase)) ?? true)
            {
                await WriteJsonErrorAsync(context, StatusCodes.Status406NotAcceptable, "Streaming requires WebSocket or text/event-stream.", connectionCancellation.Token).ConfigureAwait(false);
                return;
            }

            await RunSseAsync(context, spec, viewerActorIri, cursor, query, pump, options, connectionCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            connectionCancellation.Cancel();
            await ObserveRenewalAsync(renewal).ConfigureAwait(false);
            await connectionLeases.ReleaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task RunSseAsync(
        HttpContext context,
        MastodonStreamSpec spec,
        string? viewerActorIri,
        long cursor,
        MastodonQueryService query,
        IDurableStreamEventPump pump,
        StreamingOptions options,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        await context.Response.StartAsync(cancellationToken).ConfigureAwait(false);
        await using IAsyncEnumerator<StreamEvent> enumerator = pump.SubscribeAsync(
            cursor,
            options.BufferCapacity,
            options.PollInterval,
            cancellationToken).GetAsyncEnumerator(cancellationToken);
        Task<bool> moveNext = enumerator.MoveNextAsync().AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            Task heartbeat = Task.Delay(options.HeartbeatInterval, cancellationToken);
            Task completed = await Task.WhenAny(moveNext, heartbeat).ConfigureAwait(false);
            if (completed == heartbeat)
            {
                await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!await moveNext.ConfigureAwait(false))
            {
                break;
            }

            StreamEvent item = enumerator.Current;
            MastodonWireEvent? wire = await ProjectAsync(item, spec, viewerActorIri, query, cancellationToken).ConfigureAwait(false);
            if (wire is not null)
            {
                await context.Response.WriteAsync(
                    $"id: {item.Cursor}\nevent: {wire.Event}\ndata: {wire.Payload}\n\n",
                    cancellationToken).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            moveNext = enumerator.MoveNextAsync().AsTask();
        }
    }

    private static async Task RunWebSocketAsync(
        HttpContext context,
        MastodonStreamSpec initialSpec,
        string? viewerActorIri,
        long cursor,
        MastodonQueryService query,
        IDurableStreamEventPump pump,
        StreamingOptions options,
        CancellationToken cancellationToken)
    {
        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var subscriptions = new Dictionary<string, MastodonStreamSpec>(StringComparer.Ordinal)
        {
            [initialSpec.Name] = initialSpec
        };
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

                ApplySubscriptionMessage(message, subscriptions, viewerActorIri is not null);
                receive = ReceiveTextAsync(socket, options.MaximumInboundMessageBytes, cancellationToken);
                continue;
            }

            if (!await moveNext.ConfigureAwait(false))
            {
                break;
            }

            StreamEvent item = enumerator.Current;
            foreach (MastodonStreamSpec spec in subscriptions.Values.ToArray())
            {
                MastodonWireEvent? wire = await ProjectAsync(item, spec, viewerActorIri, query, cancellationToken).ConfigureAwait(false);
                if (wire is null)
                {
                    continue;
                }

                byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    stream = new[] { spec.Name },
                    @event = wire.Event,
                    payload = wire.Payload
                }, JsonOptions);
                await socket.SendAsync(envelope, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }

            moveNext = enumerator.MoveNextAsync().AsTask();
        }

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stream ended", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<MastodonWireEvent?> ProjectAsync(
        StreamEvent item,
        MastodonStreamSpec spec,
        string? viewerActorIri,
        MastodonQueryService query,
        CancellationToken cancellationToken)
    {
        if (item.Kind == StreamEventKind.NotificationCreated &&
            spec.Audience == ClientStreamAudience.Home &&
            viewerActorIri is not null &&
            string.Equals(item.RecipientActorIri, viewerActorIri, StringComparison.Ordinal) &&
            item.ResourceId is { } notificationId)
        {
            MastodonNotification? notification = await query.FindNotificationAsync(
                viewerActorIri,
                notificationId,
                cancellationToken).ConfigureAwait(false);
            return notification is null
                ? null
                : new("notification", JsonSerializer.Serialize(notification, JsonOptions));
        }

        if (!await query.CanReceiveStreamEventAsync(
                item,
                viewerActorIri,
                spec.Audience,
                spec.LocalOnly,
                cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (item.Kind == StreamEventKind.PostDeleted && item.ResourceId is { } deletedId)
        {
            string externalId = await query.MapStreamPostIdAsync(deletedId, item.OccurredAt, cancellationToken).ConfigureAwait(false);
            return new("delete", externalId);
        }

        if (item.ResourceId is not { } resourceId || item.Kind is not (StreamEventKind.PostCreated or StreamEventKind.PostUpdated))
        {
            return null;
        }

        MastodonStatus? status = await query.FindStreamStatusAsync(
            resourceId,
            viewerActorIri,
            spec.Audience,
            spec.LocalOnly,
            cancellationToken).ConfigureAwait(false);
        return status is null
            ? null
            : new(item.Kind == StreamEventKind.PostUpdated ? "status.update" : "update", JsonSerializer.Serialize(status, JsonOptions));
    }

    private static MastodonStreamSpec? ParseSpec(HttpContext context, string? wildcard)
    {
        string? value = context.Request.Query["stream"].FirstOrDefault();
        value ??= wildcard?.Trim('/').Replace("/", ":", StringComparison.Ordinal);
        return value switch
        {
            "public" => new("public", ClientStreamAudience.Public, false),
            "public:local" => new("public:local", ClientStreamAudience.Public, true),
            "user" => new("user", ClientStreamAudience.Home, false),
            _ => null
        };
    }

    private static void ApplySubscriptionMessage(
        string message,
        IDictionary<string, MastodonStreamSpec> subscriptions,
        bool authenticated)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("stream", out JsonElement streamElement) || streamElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            string? name = streamElement.GetString();
            MastodonStreamSpec? spec = name switch
            {
                "public" => new("public", ClientStreamAudience.Public, false),
                "public:local" => new("public:local", ClientStreamAudience.Public, true),
                "user" when authenticated => new("user", ClientStreamAudience.Home, false),
                _ => null
            };
            if (spec is null)
            {
                return;
            }

            if (string.Equals(typeElement.GetString(), "subscribe", StringComparison.Ordinal))
            {
                subscriptions[spec.Name] = spec;
            }
            else if (string.Equals(typeElement.GetString(), "unsubscribe", StringComparison.Ordinal))
            {
                subscriptions.Remove(spec.Name);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static async Task<(bool Valid, long Cursor, string? Error)> ResolveCursorAsync(
        HttpContext context,
        IStreamEventStore store,
        CancellationToken cancellationToken)
    {
        string? raw = context.Request.Query["cursor"].FirstOrDefault();
        raw ??= context.Request.Headers["Last-Event-ID"].FirstOrDefault();
        if (raw is not null && (!long.TryParse(raw, out long parsed) || parsed < 0))
        {
            return (false, 0, "The stream cursor is invalid.");
        }

        StreamEventPage page = await store.ReadAfterAsync(raw is null ? 0 : long.Parse(raw, System.Globalization.CultureInfo.InvariantCulture), 1, cancellationToken).ConfigureAwait(false);
        if (page.RequestedCursorExpired)
        {
            return (false, 0, "The stream cursor is no longer retained.");
        }

        return (true, raw is null ? page.LatestCursor ?? 0 : long.Parse(raw, System.Globalization.CultureInfo.InvariantCulture), null);
    }

    private static async Task<string?> FindViewerActorIriAsync(
        ClaimsPrincipal principal,
        MastodonQueryService query,
        CancellationToken cancellationToken)
    {
        string? direct = principal.FindFirst("actor")?.Value;
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        string? username = principal.FindFirst("preferred_username")?.Value ?? principal.Identity?.Name;
        return username is null ? null : await query.FindLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
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

    private static async Task WriteJsonErrorAsync(HttpContext context, int statusCode, string message, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = message }, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

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

    private sealed record MastodonStreamSpec(string Name, ClientStreamAudience Audience, bool LocalOnly);
    private sealed record MastodonWireEvent(string Event, string Payload);
}
