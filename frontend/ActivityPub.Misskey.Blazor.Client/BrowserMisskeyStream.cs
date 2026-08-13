using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.Streaming;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed class BrowserTimelineSubscriptionService(
    MisskeyBrowserApiClient api,
    BrowserMisskeyStreamConnection stream) : ITimelineSubscriptionService
{
    public async Task<long> GetLatestCursorAsync(CancellationToken cancellationToken)
    {
        JsonElement response = await api.PostAsync(
            "/api/streaming/cursor",
            new { },
            cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("cursor", out JsonElement cursor) ||
            !cursor.TryGetInt64(out long value) ||
            value < 0)
        {
            throw new TimelineCursorException("STREAM_CURSOR_INVALID");
        }

        return value;
    }

    public IAsyncEnumerable<TimelineMutation> SubscribeAsync(
        TimelineKind kind,
        long afterCursor,
        CancellationToken cancellationToken) =>
        stream.SubscribeTimelineAsync(kind, afterCursor, cancellationToken);
}

public sealed class BrowserNotificationSubscriptionService(
    MisskeyBrowserApiClient api,
    BrowserMisskeyStreamConnection stream) : INotificationSubscriptionService
{
    public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken) =>
        BrowserStreamCursor.ReadAsync(api, cancellationToken);

    public IAsyncEnumerable<NotificationMutation> SubscribeAsync(
        long afterCursor,
        IReadOnlySet<MisskeyNotificationType>? includeTypes,
        IReadOnlySet<MisskeyNotificationType>? excludeTypes,
        CancellationToken cancellationToken) =>
        stream.SubscribeNotificationsAsync(afterCursor, includeTypes, excludeTypes, cancellationToken);
}

public sealed class BrowserRelationshipSubscriptionService(
    MisskeyBrowserApiClient api,
    BrowserMisskeyStreamConnection stream) : IRelationshipSubscriptionService
{
    public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken) =>
        BrowserStreamCursor.ReadAsync(api, cancellationToken);

    public IAsyncEnumerable<RelationshipMutation> SubscribeAsync(
        Guid targetActorId,
        long afterCursor,
        CancellationToken cancellationToken) =>
        stream.SubscribeRelationshipAsync(targetActorId, afterCursor, cancellationToken);
}

internal static class BrowserStreamCursor
{
    public static async Task<long> ReadAsync(MisskeyBrowserApiClient api, CancellationToken cancellationToken)
    {
        JsonElement response = await api.PostAsync(
            "/api/streaming/cursor",
            new { },
            cancellationToken).ConfigureAwait(false);
        return response.TryGetProperty("cursor", out JsonElement cursor) &&
               cursor.TryGetInt64(out long value) && value >= 0
            ? value
            : throw new TimelineCursorException("STREAM_CURSOR_INVALID");
    }
}

public sealed class BrowserMisskeyStreamConnection : IAsyncDisposable
{
    private const int MaximumFrameCharacters = 2_000_000;
    private readonly IJSRuntime javascript;
    private readonly FrontendRuntimeConfigurationState runtime;
    private readonly BrowserTimelinePresentationService timeline;
    private readonly BrowserNotificationPresentationService notifications;
    private readonly IMisskeyStreamConnectionStatus status;
    private readonly ConcurrentDictionary<string, TimelineSink> timelineSinks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, NotificationSink> notificationSinks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RelationshipSink> relationshipSinks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim initialization = new(1, 1);
    private readonly DotNetObjectReference<BrowserMisskeyStreamConnection> receiver;
    private IJSObjectReference? module;
    private IJSObjectReference? connection;
    private bool disposed;

    public BrowserMisskeyStreamConnection(
        IJSRuntime javascript,
        FrontendRuntimeConfigurationState runtime,
        BrowserTimelinePresentationService timeline,
        BrowserNotificationPresentationService notifications,
        IMisskeyStreamConnectionStatus status)
    {
        this.javascript = javascript;
        this.runtime = runtime;
        this.timeline = timeline;
        this.notifications = notifications;
        this.status = status;
        receiver = DotNetObjectReference.Create(this);
    }

    public async IAsyncEnumerable<TimelineMutation> SubscribeTimelineAsync(
        TimelineKind kind,
        long afterCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterCursor);
        ObjectDisposedException.ThrowIf(disposed, this);
        string id = "timeline-" + Guid.NewGuid().ToString("N");
        var channel = Channel.CreateBounded<TimelineMutation>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        var sink = new TimelineSink(kind, channel);
        if (!timelineSinks.TryAdd(id, sink))
        {
            throw new InvalidOperationException("A browser stream subscription identifier collided.");
        }

        try
        {
            IJSObjectReference browserConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            await browserConnection.InvokeVoidAsync(
                "subscribe",
                cancellationToken,
                id,
                ChannelName(kind),
                afterCursor).ConfigureAwait(false);
            await foreach (TimelineMutation mutation in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return mutation;
            }
        }
        finally
        {
            timelineSinks.TryRemove(id, out _);
            channel.Writer.TryComplete();
            if (connection is not null)
            {
                try
                {
                    await connection.InvokeVoidAsync("unsubscribe", id).ConfigureAwait(false);
                }
                catch (JSDisconnectedException)
                {
                }
            }
        }
    }

    public async IAsyncEnumerable<NotificationMutation> SubscribeNotificationsAsync(
        long afterCursor,
        IReadOnlySet<MisskeyNotificationType>? includeTypes,
        IReadOnlySet<MisskeyNotificationType>? excludeTypes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterCursor);
        ObjectDisposedException.ThrowIf(disposed, this);
        string id = "notifications-" + Guid.NewGuid().ToString("N");
        var channel = Channel.CreateBounded<NotificationMutation>(BoundedOptions());
        notificationSinks[id] = new(channel, includeTypes, excludeTypes);
        try
        {
            IJSObjectReference browserConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            await browserConnection.InvokeVoidAsync("subscribe", cancellationToken, id, "main", afterCursor)
                .ConfigureAwait(false);
            await foreach (NotificationMutation mutation in channel.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return mutation;
            }
        }
        finally
        {
            notificationSinks.TryRemove(id, out _);
            channel.Writer.TryComplete();
            await UnsubscribeAsync(id).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<RelationshipMutation> SubscribeRelationshipAsync(
        Guid targetActorId,
        long afterCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (targetActorId == Guid.Empty)
        {
            throw new ArgumentException("A target actor is required.", nameof(targetActorId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(afterCursor);
        ObjectDisposedException.ThrowIf(disposed, this);
        string id = "relationship-" + Guid.NewGuid().ToString("N");
        var channel = Channel.CreateBounded<RelationshipMutation>(BoundedOptions());
        relationshipSinks[id] = new(channel, targetActorId);
        try
        {
            IJSObjectReference browserConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            await browserConnection.InvokeVoidAsync("subscribe", cancellationToken, id, "main", afterCursor)
                .ConfigureAwait(false);
            await foreach (RelationshipMutation mutation in channel.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return mutation;
            }
        }
        finally
        {
            relationshipSinks.TryRemove(id, out _);
            channel.Writer.TryComplete();
            await UnsubscribeAsync(id).ConfigureAwait(false);
        }
    }

    [JSInvokable]
    public async Task ReceiveFrameAsync(string frame)
    {
        if (disposed || string.IsNullOrWhiteSpace(frame) || frame.Length > MaximumFrameCharacters)
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                frame,
                new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            string? type = root.OptionalString("type");
            if (type == "connected")
            {
                status.ReportConnected();
                return;
            }

            if (type == "error")
            {
                status.ReportDisconnected();
                string code = root.OptionalObject("body").OptionalString("code") ?? "STREAM_ERROR";
                if (code is "CURSOR_EXPIRED" or "RESYNC_REQUIRED" or "SLOW_CONSUMER")
                {
                    CompleteRecoverableStreamFailure(code);
                }
                else if (code is "AUTHENTICATION_EXPIRED" or "AUTHENTICATION_REQUIRED")
                {
                    CompleteAll(new ActivityPub.Misskey.Blazor.Identity.FrontendAuthenticationException(code));
                }
                return;
            }

            if (!root.TryGetProperty("cursor", out JsonElement cursorElement) ||
                !cursorElement.TryGetInt64(out long cursor) || cursor < 0)
            {
                return;
            }

            if (type == "checkpoint")
            {
                foreach (TimelineSink sink in timelineSinks.Values)
                {
                    await sink.Writer.WriteAsync(
                        new TimelineMutation(cursor, TimelineMutationKind.Checkpoint, string.Empty, null)).ConfigureAwait(false);
                }
                foreach (NotificationSink sink in notificationSinks.Values)
                {
                    await sink.Writer.WriteAsync(
                        new NotificationMutation(cursor, NotificationMutationKind.Checkpoint, null)).ConfigureAwait(false);
                }
                foreach (RelationshipSink sink in relationshipSinks.Values)
                {
                    await sink.Writer.WriteAsync(new RelationshipMutation(cursor, Changed: false)).ConfigureAwait(false);
                }
                return;
            }

            JsonElement envelope = root.OptionalObject("body");
            string? subscriptionId = envelope.OptionalString("id");
            if (type != "channel" || subscriptionId is null)
            {
                return;
            }

            string? eventType = envelope.OptionalString("type");
            if (eventType == "notification" &&
                envelope.TryGetProperty("body", out JsonElement notificationElement) &&
                notificationElement.ValueKind == JsonValueKind.Object)
            {
                if (notificationSinks.TryGetValue(subscriptionId, out NotificationSink? notificationTarget))
                {
                    NotificationViewModel notification = notifications.Map(notificationElement);
                    if (notificationTarget.Matches(notification.Type))
                    {
                        await notificationTarget.Writer.WriteAsync(
                            new NotificationMutation(cursor, NotificationMutationKind.Created, notification)).ConfigureAwait(false);
                    }
                }
                return;
            }

            if (eventType is "follow" or "unfollow" or "followed" or "unfollowed" &&
                envelope.TryGetProperty("body", out JsonElement relationshipUser) &&
                relationshipUser.ValueKind == JsonValueKind.Object)
            {
                if (relationshipSinks.TryGetValue(subscriptionId, out RelationshipSink? relationshipTarget))
                {
                    Guid changedActor = BrowserPresentationMapper.ParseInternalGuid(
                        relationshipUser.RequiredString("id"));
                    await relationshipTarget.Writer.WriteAsync(
                        new RelationshipMutation(cursor, changedActor == relationshipTarget.TargetActorId)).ConfigureAwait(false);
                }
                return;
            }

            if (timelineSinks.TryGetValue(subscriptionId, out TimelineSink? target) &&
                eventType == "note" && envelope.TryGetProperty("body", out JsonElement noteElement) &&
                noteElement.ValueKind == JsonValueKind.Object)
            {
                NoteViewModel note = timeline.MapStreamNote(noteElement);
                await target.Writer.WriteAsync(
                    new TimelineMutation(cursor, TimelineMutationKind.Upsert, note.Id, note)).ConfigureAwait(false);
            }
        }
        catch (JsonException)
        {
            status.ReportDisconnected();
            CompleteRecoverableStreamFailure("STREAM_FRAME_INVALID");
        }
    }

    [JSInvokable]
    public Task NotifyConnectionStateAsync(string value)
    {
        if (string.Equals(value, "connected", StringComparison.Ordinal))
        {
            status.ReportConnected();
        }
        else
        {
            status.ReportDisconnected();
        }

        return Task.CompletedTask;
    }

    private async Task<IJSObjectReference> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (connection is not null)
        {
            return connection;
        }

        await initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (connection is not null)
            {
                return connection;
            }

            FrontendRuntimeSettings settings = runtime.GetRequiredSettings();
            Uri websocketUri = new(
                (settings.PublicBaseUri.Scheme == Uri.UriSchemeHttps ? "wss://" : "ws://") +
                settings.PublicBaseUri.Authority + "/streaming");
            module = await javascript.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "./streaming.js").ConfigureAwait(false);
            connection = await module.InvokeAsync<IJSObjectReference>(
                "createMisskeyStream",
                cancellationToken,
                websocketUri.AbsoluteUri,
                receiver,
                512).ConfigureAwait(false);
            return connection;
        }
        finally
        {
            initialization.Release();
        }
    }

    private void CompleteAll(Exception exception)
    {
        foreach (TimelineSink sink in timelineSinks.Values)
        {
            sink.Channel.Writer.TryComplete(exception);
        }
        foreach (NotificationSink sink in notificationSinks.Values)
        {
            sink.Channel.Writer.TryComplete(exception);
        }
        foreach (RelationshipSink sink in relationshipSinks.Values)
        {
            sink.Channel.Writer.TryComplete(exception);
        }
    }

    private void CompleteRecoverableStreamFailure(string wireCode)
    {
        string errorCode = wireCode switch
        {
            "CURSOR_EXPIRED" or "RESYNC_REQUIRED" => "STREAM_CURSOR_EXPIRED",
            "SLOW_CONSUMER" => "STREAM_SLOW_CONSUMER",
            _ => "STREAM_FRAME_INVALID"
        };
        foreach (TimelineSink sink in timelineSinks.Values)
        {
            sink.Channel.Writer.TryComplete(new TimelineCursorException(errorCode));
        }
        foreach (NotificationSink sink in notificationSinks.Values)
        {
            sink.Channel.Writer.TryComplete(new NotificationCursorException(errorCode));
        }
        foreach (RelationshipSink sink in relationshipSinks.Values)
        {
            sink.Channel.Writer.TryComplete(new RelationshipCursorException(errorCode));
        }
    }

    private async ValueTask UnsubscribeAsync(string id)
    {
        if (connection is null) return;
        try
        {
            await connection.InvokeVoidAsync("unsubscribe", id).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private static BoundedChannelOptions BoundedOptions() => new(256)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false
    };

    private static string ChannelName(TimelineKind kind) => kind switch
    {
        TimelineKind.Home => "homeTimeline",
        TimelineKind.Local => "localTimeline",
        TimelineKind.Global => "globalTimeline",
        TimelineKind.Hybrid => "hybridTimeline",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CompleteAll(new ObjectDisposedException(nameof(BrowserMisskeyStreamConnection)));
        if (connection is not null)
        {
            try
            {
                await connection.InvokeVoidAsync("dispose").ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
            }
        }
        if (module is not null)
        {
            try
            {
                await module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
            }
        }
        receiver.Dispose();
        initialization.Dispose();
    }

    private sealed record TimelineSink(
        TimelineKind Kind,
        Channel<TimelineMutation> Channel)
    {
        public ChannelWriter<TimelineMutation> Writer => Channel.Writer;
    }

    private sealed record NotificationSink(
        Channel<NotificationMutation> Channel,
        IReadOnlySet<MisskeyNotificationType>? IncludeTypes,
        IReadOnlySet<MisskeyNotificationType>? ExcludeTypes)
    {
        public ChannelWriter<NotificationMutation> Writer => Channel.Writer;

        public bool Matches(MisskeyNotificationType type) =>
            (IncludeTypes is null || IncludeTypes.Count == 0 || IncludeTypes.Contains(type)) &&
            (ExcludeTypes is null || !ExcludeTypes.Contains(type));
    }

    private sealed record RelationshipSink(Channel<RelationshipMutation> Channel, Guid TargetActorId)
    {
        public ChannelWriter<RelationshipMutation> Writer => Channel.Writer;
    }
}
