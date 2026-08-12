using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class NotificationCompatibilityTests(ActivityPubApiFixture fixture)
{
    private static readonly string[] ReactionTypes = ["reaction"];
    private readonly HttpClient client = CreateClient(fixture);

    [Fact]
    public async Task MastodonAndMisskeyProjectSameNotificationAndShareReadDismissState()
    {
        (UserNotification notification, string mastodonId, string misskeyId) = await SeedAsync(addStreamEvent: false);

        using HttpResponseMessage shown = await client.GetAsync("/api/v1/notifications/" + mastodonId);
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        using JsonDocument mastodon = await JsonDocument.ParseAsync(await shown.Content.ReadAsStreamAsync());
        Assert.Equal(mastodonId, mastodon.RootElement.GetProperty("id").GetString());
        Assert.Equal("favourite", mastodon.RootElement.GetProperty("type").GetString());

        using HttpResponseMessage listed = await client.PostAsJsonAsync("/api/i/notifications", new
        {
            limit = 100,
            markAsRead = false,
            includeTypes = ReactionTypes
        });
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using JsonDocument misskey = await JsonDocument.ParseAsync(await listed.Content.ReadAsStreamAsync());
        JsonElement same = Assert.Single(misskey.RootElement.EnumerateArray(), item =>
            item.GetProperty("id").GetString() == misskeyId);
        Assert.Equal("reaction", same.GetProperty("type").GetString());
        Assert.Equal("👍", same.GetProperty("reaction").GetString());
        Assert.False(same.GetProperty("isRead").GetBoolean());

        using HttpResponseMessage read = await client.PostAsJsonAsync(
            "/api/notifications/read",
            new { notificationId = misskeyId });
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            Assert.NotNull(await db.UserNotifications.Where(x => x.Id == notification.Id).Select(x => x.ReadAt).SingleAsync());
        }

        using HttpResponseMessage dismissed = await client.PostAsync(
            "/api/v1/notifications/" + mastodonId + "/dismiss",
            null);
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
        using HttpResponseMessage missing = await client.GetAsync("/api/v1/notifications/" + mastodonId);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task CustomReactionRemainsMisskeyReactionAndIsNotFabricatedAsMastodonFavourite()
    {
        (_, string mastodonId, string misskeyId) = await SeedAsync(
            addStreamEvent: false,
            UserNotificationKind.Reaction,
            "🎉");

        using HttpResponseMessage mastodon = await client.GetAsync("/api/v1/notifications/" + mastodonId);
        Assert.Equal(HttpStatusCode.NotFound, mastodon.StatusCode);

        using HttpResponseMessage listed = await client.PostAsJsonAsync("/api/i/notifications", new
        {
            limit = 100,
            markAsRead = false,
            includeTypes = ReactionTypes
        });
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using JsonDocument payload = await JsonDocument.ParseAsync(await listed.Content.ReadAsStreamAsync());
        JsonElement same = Assert.Single(payload.RootElement.EnumerateArray(), item =>
            item.GetProperty("id").GetString() == misskeyId);
        Assert.Equal("reaction", same.GetProperty("type").GetString());
        Assert.Equal("🎉", same.GetProperty("reaction").GetString());
    }

    [Fact]
    public async Task UnreadMarkAllAndClearMutateTheSharedPersistentState()
    {
        (_, _, string firstMisskeyId) = await SeedAsync(addStreamEvent: false, UserNotificationKind.Update, null);
        (_, _, string secondMisskeyId) = await SeedAsync(addStreamEvent: false, UserNotificationKind.Update, null);

        using HttpResponseMessage unreadBefore = await client.GetAsync("/api/v1/notifications/unread_count");
        Assert.Equal(HttpStatusCode.OK, unreadBefore.StatusCode);
        using JsonDocument unreadPayload = await JsonDocument.ParseAsync(await unreadBefore.Content.ReadAsStreamAsync());
        Assert.True(unreadPayload.RootElement.GetProperty("count").GetInt64() >= 2);
        using HttpResponseMessage meBefore = await client.PostAsJsonAsync("/api/i", new { });
        using JsonDocument meBeforePayload = await JsonDocument.ParseAsync(await meBefore.Content.ReadAsStreamAsync());
        Assert.True(meBeforePayload.RootElement.GetProperty("hasUnreadNotification").GetBoolean());

        using HttpResponseMessage marked = await client.PostAsync("/api/notifications/mark-all-as-read", null);
        Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);
        using HttpResponseMessage meAfter = await client.PostAsJsonAsync("/api/i", new { });
        using JsonDocument meAfterPayload = await JsonDocument.ParseAsync(await meAfter.Content.ReadAsStreamAsync());
        Assert.False(meAfterPayload.RootElement.GetProperty("hasUnreadNotification").GetBoolean());

        using HttpResponseMessage misskeyList = await client.PostAsJsonAsync("/api/i/notifications", new
        {
            limit = 100,
            markAsRead = false
        });
        Assert.Equal(HttpStatusCode.OK, misskeyList.StatusCode);
        using JsonDocument notifications = await JsonDocument.ParseAsync(await misskeyList.Content.ReadAsStreamAsync());
        JsonElement[] seeded = notifications.RootElement.EnumerateArray()
            .Where(item => item.GetProperty("id").GetString() is string id &&
                (id == firstMisskeyId || id == secondMisskeyId))
            .ToArray();
        Assert.Equal(2, seeded.Length);
        Assert.All(seeded, item => Assert.True(item.GetProperty("isRead").GetBoolean()));

        using HttpResponseMessage cleared = await client.PostAsync("/api/v1/notifications/clear", null);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using HttpResponseMessage empty = await client.PostAsJsonAsync("/api/i/notifications", new
        {
            limit = 100,
            markAsRead = false
        });
        using JsonDocument afterClear = await JsonDocument.ParseAsync(await empty.Content.ReadAsStreamAsync());
        Assert.DoesNotContain(afterClear.RootElement.EnumerateArray(), item =>
            item.GetProperty("id").GetString() is string id && (id == firstMisskeyId || id == secondMisskeyId));
    }

    [Fact]
    public async Task NotificationMutationIsRestrictedToTheRecipient()
    {
        (_, string mastodonId, string misskeyId) = await SeedAsync(
            addStreamEvent: false,
            UserNotificationKind.Follow,
            null,
            recipientActorIri: "https://local.example/users/bob");

        using HttpResponseMessage show = await client.GetAsync("/api/v1/notifications/" + mastodonId);
        Assert.Equal(HttpStatusCode.NotFound, show.StatusCode);
        using HttpResponseMessage dismiss = await client.PostAsync("/api/v1/notifications/" + mastodonId + "/dismiss", null);
        Assert.Equal(HttpStatusCode.NotFound, dismiss.StatusCode);
        using HttpResponseMessage read = await client.PostAsJsonAsync(
            "/api/notifications/read",
            new { notificationId = misskeyId });
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task MisskeyMainChannelReceivesPersistedNotificationEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri("ws://local.example/streaming?i=fixture-alice"),
            timeout.Token);
        string channelId = Guid.NewGuid().ToString("N");
        await SendTextAsync(socket, JsonSerializer.Serialize(new
        {
            type = "connect",
            body = new { channel = "main", id = channelId, pong = true }
        }), timeout.Token);
        using JsonDocument connected = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("connected", connected.RootElement.GetProperty("type").GetString());

        (_, _, string misskeyId) = await SeedAsync(
            addStreamEvent: true,
            UserNotificationKind.Reaction,
            "🎉");
        using JsonDocument envelope = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("channel", envelope.RootElement.GetProperty("type").GetString());
        JsonElement body = envelope.RootElement.GetProperty("body");
        Assert.Equal(channelId, body.GetProperty("id").GetString());
        Assert.Equal("notification", body.GetProperty("type").GetString());
        Assert.Equal(misskeyId, body.GetProperty("body").GetProperty("id").GetString());
    }

    [Fact]
    public async Task MastodonUserStreamSkipsCustomReactionAndPublishesDurableFavouriteNotification()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri("ws://local.example/api/v1/streaming?stream=user&access_token=fixture-alice"),
            timeout.Token);

        _ = await SeedAsync(addStreamEvent: true, UserNotificationKind.Reaction, "🎉");
        (_, string mastodonId, _) = await SeedAsync(addStreamEvent: true);
        using JsonDocument envelope = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("notification", envelope.RootElement.GetProperty("event").GetString());
        using JsonDocument payload = JsonDocument.Parse(envelope.RootElement.GetProperty("payload").GetString()!);
        Assert.Equal(mastodonId, payload.RootElement.GetProperty("id").GetString());
        Assert.Equal("favourite", payload.RootElement.GetProperty("type").GetString());
    }

    private async Task<(UserNotification Notification, string MastodonId, string MisskeyId)> SeedAsync(
        bool addStreamEvent,
        UserNotificationKind kind = UserNotificationKind.Favourite,
        string? reaction = "👍",
        DateTimeOffset? createdAt = null,
        string recipientActorIri = "https://local.example/users/alice")
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        const string sourceActorIri = "https://media-blocked.example/users/publisher";
        string objectIri = await db.Objects.Where(x => x.OwnerIri == sourceActorIri && !x.IsDeleted)
            .Select(x => x.Iri)
            .FirstAsync();
        DateTimeOffset now = createdAt ?? DateTimeOffset.UtcNow;
        UserNotification notification = UserNotification.Create(
            recipientActorIri,
            sourceActorIri,
            kind,
            "https://media-blocked.example/activities/" + Guid.NewGuid().ToString("N"),
            objectIri,
            reaction,
            now);
        db.UserNotifications.Add(notification);
        if (addStreamEvent)
        {
            db.StreamEvents.Add(StreamEvent.FromNotification(notification, Visibility.MentionedOnly, isLocal: false));
        }

        await db.SaveChangesAsync();
        string mastodonId = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Notification,
            notification.Id,
            now,
            CancellationToken.None);
        string misskeyId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Notification,
            notification.Id,
            now,
            CancellationToken.None);
        return (notification, mastodonId, misskeyId);
    }

    private static async Task SendTextAsync(WebSocket socket, string value, CancellationToken cancellationToken)
    {
        await socket.SendAsync(Encoding.UTF8.GetBytes(value), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[128 * 1024];
        using var output = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }

    private static HttpClient CreateClient(ActivityPubApiFixture fixture)
    {
        HttpClient result = fixture.CreateClient(new()
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });
        result.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-alice");
        return result;
    }
}
