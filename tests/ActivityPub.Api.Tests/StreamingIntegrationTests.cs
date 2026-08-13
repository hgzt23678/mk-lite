using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class StreamingIntegrationTests(ActivityPubApiFixture fixture)
{
    private readonly HttpClient client = CreateClient(fixture);

    [Fact]
    public async Task MastodonHomeWebSocketProjectsCommittedStatusAndAcceptsRedactedQueryToken()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri("ws://local.example/api/v1/streaming?stream=user&access_token=fixture-alice"),
            timeout.Token);
        string marker = "mastodon-stream-" + Guid.NewGuid().ToString("N");

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/statuses")
        {
            Content = JsonContent.Create(new { status = marker, visibility = "public" })
        };
        create.Headers.TryAddWithoutValidation("Idempotency-Key", marker);
        using HttpResponseMessage created = await client.SendAsync(create, timeout.Token);
        created.EnsureSuccessStatusCode();
        using JsonDocument response = await JsonDocument.ParseAsync(
            await created.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);
        string createdId = response.RootElement.GetProperty("id").GetString()!;

        using JsonDocument envelope = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("update", envelope.RootElement.GetProperty("event").GetString());
        Assert.Equal("user", envelope.RootElement.GetProperty("stream")[0].GetString());
        using JsonDocument payload = JsonDocument.Parse(envelope.RootElement.GetProperty("payload").GetString()!);
        Assert.Equal(createdId, payload.RootElement.GetProperty("id").GetString());
        Assert.Contains(marker, payload.RootElement.GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MisskeyHomeChannelProjectsCommittedNoteAndAcknowledgesConnection()
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
            body = new { channel = "homeTimeline", id = channelId, pong = true }
        }), timeout.Token);
        using JsonDocument connected = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("connected", connected.RootElement.GetProperty("type").GetString());
        Assert.Equal(channelId, connected.RootElement.GetProperty("body").GetProperty("id").GetString());

        string marker = "misskey-stream-" + Guid.NewGuid().ToString("N");
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/notes/create")
        {
            Content = JsonContent.Create(new
            {
                text = marker,
                visibility = "public",
                localOnly = false
            })
        };
        create.Headers.TryAddWithoutValidation("Idempotency-Key", marker);
        using HttpResponseMessage created = await client.SendAsync(create, timeout.Token);
        created.EnsureSuccessStatusCode();
        using JsonDocument response = await JsonDocument.ParseAsync(
            await created.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);
        string createdId = response.RootElement.GetProperty("createdNote").GetProperty("id").GetString()!;

        using JsonDocument envelope = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("channel", envelope.RootElement.GetProperty("type").GetString());
        Assert.False(envelope.RootElement.TryGetProperty("cursor", out _));
        JsonElement body = envelope.RootElement.GetProperty("body");
        Assert.Equal(channelId, body.GetProperty("id").GetString());
        Assert.Equal("note", body.GetProperty("type").GetString());
        Assert.Equal(createdId, body.GetProperty("body").GetProperty("id").GetString());
        Assert.Contains(marker, body.GetProperty("body").GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MisskeyResumeAcknowledgesSubscriptionBeforeReplayingPayloadAndCheckpoint()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        long initialCursor = await GetLatestStreamCursorAsync(timeout.Token);
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri($"ws://local.example/streaming?resume=v1&cursor={initialCursor}"),
            timeout.Token);

        string marker = "misskey-resume-before-ack-" + Guid.NewGuid().ToString("N");
        await CreatePublicStatusAsync(marker, timeout.Token);
        string channelId = Guid.NewGuid().ToString("N");
        await SendTextAsync(socket, JsonSerializer.Serialize(new
        {
            type = "connect",
            body = new { channel = "globalTimeline", id = channelId }
        }), timeout.Token);

        using JsonDocument connected = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("connected", connected.RootElement.GetProperty("type").GetString());
        Assert.Equal(channelId, connected.RootElement.GetProperty("body").GetProperty("id").GetString());

        using JsonDocument payload = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("channel", payload.RootElement.GetProperty("type").GetString());
        long payloadCursor = payload.RootElement.GetProperty("cursor").GetInt64();
        Assert.True(payloadCursor > initialCursor);
        Assert.Contains(
            marker,
            payload.RootElement.GetProperty("body").GetProperty("body").GetProperty("text").GetString(),
            StringComparison.Ordinal);

        using JsonDocument checkpoint = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("checkpoint", checkpoint.RootElement.GetProperty("type").GetString());
        Assert.Equal(payloadCursor, checkpoint.RootElement.GetProperty("cursor").GetInt64());
        Assert.Equal(payloadCursor, checkpoint.RootElement.GetProperty("body").GetProperty("cursor").GetInt64());
    }

    [Fact]
    public async Task MisskeyResumeAdvancesCheckpointWhenAnEventIsFiltered()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        long initialCursor = await GetLatestStreamCursorAsync(timeout.Token);
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri($"ws://local.example/streaming?resume=v1&cursor={initialCursor}"),
            timeout.Token);
        await SendTextAsync(socket, JsonSerializer.Serialize(new
        {
            type = "connect",
            body = new { channel = "globalTimeline", id = Guid.NewGuid().ToString("N") }
        }), timeout.Token);
        using JsonDocument connected = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("connected", connected.RootElement.GetProperty("type").GetString());

        string marker = "misskey-resume-filtered-" + Guid.NewGuid().ToString("N");
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/notes/create")
        {
            Content = JsonContent.Create(new
            {
                text = marker,
                visibility = "specified",
                visibleUserIds = new[] { fixture.MisskeyRemotePublisherId },
                localOnly = false
            })
        };
        create.Headers.TryAddWithoutValidation("Idempotency-Key", marker);
        using HttpResponseMessage response = await client.SendAsync(create, timeout.Token);
        response.EnsureSuccessStatusCode();

        using JsonDocument checkpoint = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("checkpoint", checkpoint.RootElement.GetProperty("type").GetString());
        Assert.True(checkpoint.RootElement.GetProperty("cursor").GetInt64() > initialCursor);
    }

    [Fact]
    public async Task MisskeyResumeUsesStableResyncAndAuthenticationCloseContracts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using (WebSocket expired = await socketClient.ConnectAsync(
                   new Uri("ws://local.example/streaming?resume=v1&cursor=-1"),
                   timeout.Token))
        {
            using JsonDocument error = JsonDocument.Parse(await ReceiveTextAsync(expired, timeout.Token));
            Assert.Equal("RESYNC_REQUIRED", error.RootElement.GetProperty("body").GetProperty("code").GetString());
            WebSocketReceiveResult closed = await ReceiveCloseAsync(expired, timeout.Token);
            Assert.Equal((WebSocketCloseStatus)4409, closed.CloseStatus);
        }

        using WebSocket unauthenticated = await socketClient.ConnectAsync(
            new Uri("ws://local.example/streaming?resume=v1"),
            timeout.Token);
        await SendTextAsync(unauthenticated, JsonSerializer.Serialize(new
        {
            type = "connect",
            body = new { channel = "homeTimeline", id = Guid.NewGuid().ToString("N") }
        }), timeout.Token);
        using JsonDocument authenticationError = JsonDocument.Parse(await ReceiveTextAsync(unauthenticated, timeout.Token));
        Assert.Equal(
            "AUTHENTICATION_REQUIRED",
            authenticationError.RootElement.GetProperty("body").GetProperty("code").GetString());
        WebSocketReceiveResult authenticationClosed = await ReceiveCloseAsync(unauthenticated, timeout.Token);
        Assert.Equal((WebSocketCloseStatus)4401, authenticationClosed.CloseStatus);
    }

    [Fact]
    public async Task MisskeyResumeUsesStableSlowConsumerCloseContract()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using WebApplicationFactory<Program> slowConsumerFactory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDurableStreamEventPump>();
                services.AddScoped<IDurableStreamEventPump, SlowConsumerPump>();
            }));
        WebSocketClient socketClient = slowConsumerFactory.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri("ws://local.example/streaming?resume=v1"),
            timeout.Token);
        await SendTextAsync(socket, JsonSerializer.Serialize(new
        {
            type = "connect",
            body = new { channel = "globalTimeline", id = Guid.NewGuid().ToString("N") }
        }), timeout.Token);
        using JsonDocument connected = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("connected", connected.RootElement.GetProperty("type").GetString());
        using JsonDocument error = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("SLOW_CONSUMER", error.RootElement.GetProperty("body").GetProperty("code").GetString());
        WebSocketReceiveResult closed = await ReceiveCloseAsync(socket, timeout.Token);
        Assert.Equal((WebSocketCloseStatus)4408, closed.CloseStatus);
    }

    [Fact]
    public async Task MisskeyResumeClosesBeforePublishingAfterNativeTokenRevocation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        MisskeyIssuedToken issued;
        await using (AsyncServiceScope issueScope = fixture.Services.CreateAsyncScope())
        {
            IMisskeyAuthenticationService authentication = issueScope.ServiceProvider
                .GetRequiredService<IMisskeyAuthenticationService>();
            issued = await authentication.IssueDirectAsync(
                "alice",
                "stream-revocation-test",
                null,
                null,
                ["read:account"],
                timeout.Token);
        }

        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri($"ws://local.example/streaming?resume=v1&i={issued.Token}"),
            timeout.Token);
        await SendTextAsync(socket, JsonSerializer.Serialize(new
        {
            type = "connect",
            body = new { channel = "globalTimeline", id = Guid.NewGuid().ToString("N") }
        }), timeout.Token);
        using JsonDocument connected = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("connected", connected.RootElement.GetProperty("type").GetString());

        await using (AsyncServiceScope revokeScope = fixture.Services.CreateAsyncScope())
        {
            IMisskeyAuthenticationService authentication = revokeScope.ServiceProvider
                .GetRequiredService<IMisskeyAuthenticationService>();
            Assert.True(await authentication.RevokeAsync(issued.ActorIri, issued.TokenId, timeout.Token));
        }

        string marker = "stream-after-revocation-" + Guid.NewGuid().ToString("N");
        await CreatePublicStatusAsync(marker, timeout.Token);
        await ReceiveAuthenticationExpiredAsync(socket, marker, timeout.Token);
        WebSocketReceiveResult closed = await ReceiveCloseAsync(socket, timeout.Token);
        Assert.Equal((WebSocketCloseStatus)4401, closed.CloseStatus);
    }

    [Fact]
    public async Task MisskeyResumeClosesBeforePublishingAfterBrowserSessionRevocation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var signInRequest = new HttpRequestMessage(HttpMethod.Post, "/api/signin")
        {
            Content = JsonContent.Create(new
            {
                username = "alice",
                password = ActivityPubApiFixture.FixtureAlicePassword
            })
        };
        signInRequest.Headers.TryAddWithoutValidation(
            FrontendBrowserSessionMetadata.RequestHeaderName,
            FrontendBrowserSessionMetadata.RequestHeaderValue);
        using HttpResponseMessage signIn = await client.SendAsync(signInRequest, timeout.Token);
        signIn.EnsureSuccessStatusCode();
        Assert.True(signIn.Headers.NonValidated.TryGetValues("Set-Cookie", out var setCookies));
        string sessionCookie = setCookies
            .Select(value => value.Split(';', 2)[0])
            .Single(value => value.StartsWith("__Host-activitypub-oauth-session=", StringComparison.Ordinal));

        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request =>
        {
            request.Headers.Host = "local.example";
            request.Headers.Remove("Origin");
            request.Headers.Origin = "https://client.local.example";
            request.Headers.Cookie = sessionCookie;
            request.Headers[FrontendBrowserSessionMetadata.RequestHeaderName] =
                FrontendBrowserSessionMetadata.RequestHeaderValue;
        };
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri("ws://local.example/streaming?resume=v1"),
            timeout.Token);
        await SendTextAsync(socket, JsonSerializer.Serialize(new
        {
            type = "connect",
            body = new { channel = "homeTimeline", id = Guid.NewGuid().ToString("N") }
        }), timeout.Token);
        using JsonDocument connected = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("connected", connected.RootElement.GetProperty("type").GetString());

        await using (AsyncServiceScope revokeScope = fixture.Services.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> users = revokeScope.ServiceProvider
                .GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = await users.FindByNameAsync("alice") ??
                throw new InvalidOperationException("The fixture identity is missing.");
            IdentityResult revoked = await users.UpdateSecurityStampAsync(user);
            Assert.True(revoked.Succeeded);
        }

        string marker = "stream-after-session-revocation-" + Guid.NewGuid().ToString("N");
        await CreatePublicStatusAsync(marker, timeout.Token);
        await ReceiveAuthenticationExpiredAsync(socket, marker, timeout.Token);
        WebSocketReceiveResult closed = await ReceiveCloseAsync(socket, timeout.Token);
        Assert.Equal((WebSocketCloseStatus)4401, closed.CloseStatus);
    }

    [Fact]
    public async Task MisskeyResumeRevalidatesAfterProjectionBeforeSendingDurablePayload()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var authentication = new ExpireBetweenProjectionAndSendAuthentication();
        using WebApplicationFactory<Program> factory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMisskeyAuthenticationService>();
                services.AddSingleton<IMisskeyAuthenticationService>(authentication);
            }));
        WebSocketClient socketClient = factory.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri($"ws://local.example/streaming?resume=v1&i={ExpireBetweenProjectionAndSendAuthentication.Token}"),
            timeout.Token);
        await SendTextAsync(socket, JsonSerializer.Serialize(new
        {
            type = "connect",
            body = new { channel = "globalTimeline", id = Guid.NewGuid().ToString("N") }
        }), timeout.Token);
        using JsonDocument connected = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("connected", connected.RootElement.GetProperty("type").GetString());

        string marker = "stream-projection-auth-race-" + Guid.NewGuid().ToString("N");
        await CreatePublicStatusAsync(marker, timeout.Token);

        await ReceiveAuthenticationExpiredAsync(socket, marker, timeout.Token);
        WebSocketReceiveResult closed = await ReceiveCloseAsync(socket, timeout.Token);
        Assert.Equal((WebSocketCloseStatus)4401, closed.CloseStatus);
        Assert.Equal(4, authentication.ValidationCount);
    }

    [Fact]
    public async Task MisskeyNoteCaptureReportsReactionAndUndoFromDurableDomainMutations()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri("ws://local.example/streaming?i=fixture-alice"),
            timeout.Token);
        await SendTextAsync(socket, JsonSerializer.Serialize(new
        {
            type = "s",
            body = new { id = fixture.MisskeyPublicPostId }
        }), timeout.Token);

        string reactionKey = "reaction-" + Guid.NewGuid().ToString("N");
        using var react = new HttpRequestMessage(HttpMethod.Post, "/api/notes/reactions/create")
        {
            Content = JsonContent.Create(new { noteId = fixture.MisskeyPublicPostId, reaction = "👍" })
        };
        react.Headers.TryAddWithoutValidation("Idempotency-Key", reactionKey);
        using HttpResponseMessage reactedResponse = await client.SendAsync(react, timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, reactedResponse.StatusCode);

        using JsonDocument reacted = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("noteUpdated", reacted.RootElement.GetProperty("type").GetString());
        JsonElement reactedBody = reacted.RootElement.GetProperty("body");
        Assert.Equal(fixture.MisskeyPublicPostId, reactedBody.GetProperty("id").GetString());
        Assert.Equal("reacted", reactedBody.GetProperty("type").GetString());
        Assert.Equal("👍", reactedBody.GetProperty("body").GetProperty("reaction").GetString());

        using var undo = new HttpRequestMessage(HttpMethod.Post, "/api/notes/reactions/delete")
        {
            Content = JsonContent.Create(new { noteId = fixture.MisskeyPublicPostId })
        };
        undo.Headers.TryAddWithoutValidation("Idempotency-Key", "undo-" + reactionKey);
        using HttpResponseMessage undoResponse = await client.SendAsync(undo, timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, undoResponse.StatusCode);

        using JsonDocument unreacted = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("unreacted", unreacted.RootElement.GetProperty("body").GetProperty("type").GetString());
        Assert.Equal("👍", unreacted.RootElement.GetProperty("body").GetProperty("body").GetProperty("reaction").GetString());
    }

    [Fact]
    public async Task MisskeyMainChannelReplaysPersistedFollowAndUnfollowWithoutDuplicateMutation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await EnsureNotFollowingAsync(fixture.MisskeyRemotePublisherId, timeout.Token);
        Guid targetActorId;
        long before;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync(timeout.Token);
            targetActorId = await db.RemoteActors.Where(x => x.Iri == "https://media-blocked.example/users/publisher")
                .Select(x => x.Id)
                .SingleAsync(timeout.Token);
            before = await db.StreamEvents.LongCountAsync(x =>
                x.Kind == StreamEventKind.RelationshipChanged &&
                x.ResourceId == targetActorId &&
                x.RecipientActorIri == "https://local.example/users/alice",
                timeout.Token);
        }

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
        _ = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));

        string followKey = "stream-follow-" + Guid.NewGuid().ToString("N");
        using HttpRequestMessage follow = PostMisskey(
            "/api/following/create",
            followKey,
            new { userId = fixture.MisskeyRemotePublisherId });
        using HttpResponseMessage followResponse = await client.SendAsync(follow, timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.OK, followResponse.StatusCode);
        using JsonDocument followed = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        JsonElement followedBody = followed.RootElement.GetProperty("body");
        Assert.Equal(channelId, followedBody.GetProperty("id").GetString());
        Assert.Equal("follow", followedBody.GetProperty("type").GetString());
        Assert.Equal(fixture.MisskeyRemotePublisherId, followedBody.GetProperty("body").GetProperty("id").GetString());
        Assert.True(followedBody.GetProperty("body").GetProperty("hasPendingFollowRequestFromYou").GetBoolean());

        using HttpRequestMessage duplicate = PostMisskey(
            "/api/following/create",
            "different-" + followKey,
            new { userId = fixture.MisskeyRemotePublisherId });
        using HttpResponseMessage duplicateResponse = await client.SendAsync(duplicate, timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

        using HttpRequestMessage unfollow = PostMisskey(
            "/api/following/delete",
            "stream-unfollow-" + Guid.NewGuid().ToString("N"),
            new { userId = fixture.MisskeyRemotePublisherId });
        using HttpResponseMessage unfollowResponse = await client.SendAsync(unfollow, timeout.Token);
        Assert.Equal(System.Net.HttpStatusCode.OK, unfollowResponse.StatusCode);
        using JsonDocument unfollowed = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        Assert.Equal("unfollow", unfollowed.RootElement.GetProperty("body").GetProperty("type").GetString());
        Assert.False(unfollowed.RootElement.GetProperty("body").GetProperty("body").GetProperty("isFollowing").GetBoolean());
        Assert.False(unfollowed.RootElement.GetProperty("body").GetProperty("body").GetProperty("hasPendingFollowRequestFromYou").GetBoolean());

        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await verificationFactory.CreateDbContextAsync(timeout.Token);
        long after = await verification.StreamEvents.LongCountAsync(x =>
            x.Kind == StreamEventKind.RelationshipChanged &&
            x.ResourceId == targetActorId &&
            x.RecipientActorIri == "https://local.example/users/alice",
            timeout.Token);
        Assert.Equal(before + 2, after);
    }

    [Fact]
    public async Task PublicMastodonStreamSkipsMentionedOnlyEventAndContinuesWithPublicEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Host = "local.example";
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri("ws://local.example/api/v1/streaming?stream=public"),
            timeout.Token);
        string privateMarker = "private-stream-" + Guid.NewGuid().ToString("N");
        using var privateCreate = new HttpRequestMessage(HttpMethod.Post, "/api/notes/create")
        {
            Content = JsonContent.Create(new
            {
                text = privateMarker,
                visibility = "specified",
                visibleUserIds = new[] { fixture.MisskeyRemotePublisherId },
                localOnly = false
            })
        };
        privateCreate.Headers.TryAddWithoutValidation("Idempotency-Key", privateMarker);
        using HttpResponseMessage privateResponse = await client.SendAsync(privateCreate, timeout.Token);
        privateResponse.EnsureSuccessStatusCode();

        string publicMarker = "public-stream-" + Guid.NewGuid().ToString("N");
        using var publicCreate = new HttpRequestMessage(HttpMethod.Post, "/api/v1/statuses")
        {
            Content = JsonContent.Create(new { status = publicMarker, visibility = "public" })
        };
        publicCreate.Headers.TryAddWithoutValidation("Idempotency-Key", publicMarker);
        using HttpResponseMessage publicResponse = await client.SendAsync(publicCreate, timeout.Token);
        publicResponse.EnsureSuccessStatusCode();

        using JsonDocument envelope = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
        using JsonDocument payload = JsonDocument.Parse(envelope.RootElement.GetProperty("payload").GetString()!);
        string content = payload.RootElement.GetProperty("content").GetString()!;
        Assert.Contains(publicMarker, content, StringComparison.Ordinal);
        Assert.DoesNotContain(privateMarker, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MastodonSseResumesFromLastEventIdUsingPostgresCursor()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/streaming?stream=public");
        firstRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using HttpResponseMessage firstStream = await client.SendAsync(
            firstRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        firstStream.EnsureSuccessStatusCode();
        string firstMarker = "sse-first-" + Guid.NewGuid().ToString("N");
        await CreatePublicStatusAsync(firstMarker, timeout.Token);
        await using Stream firstBody = await firstStream.Content.ReadAsStreamAsync(timeout.Token);
        (long cursor, string payload) = await ReadSseEventAsync(firstBody, timeout.Token);
        Assert.Contains(firstMarker, payload, StringComparison.Ordinal);
        firstStream.Dispose();

        string secondMarker = "sse-second-" + Guid.NewGuid().ToString("N");
        await CreatePublicStatusAsync(secondMarker, timeout.Token);
        using var resumedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/streaming?stream=public");
        resumedRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        resumedRequest.Headers.TryAddWithoutValidation("Last-Event-ID", cursor.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using HttpResponseMessage resumedStream = await client.SendAsync(
            resumedRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        resumedStream.EnsureSuccessStatusCode();
        await using Stream resumedBody = await resumedStream.Content.ReadAsStreamAsync(timeout.Token);
        (_, string resumedPayload) = await ReadSseEventAsync(resumedBody, timeout.Token);
        Assert.Contains(secondMarker, resumedPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConflictingStreamingCredentialsAreRejectedBeforeUpgrade()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request =>
        {
            request.Headers.Host = "local.example";
            request.Headers.Authorization = "Bearer different-token";
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => socketClient.ConnectAsync(
            new Uri("ws://local.example/streaming?i=fixture-alice"),
            timeout.Token));
        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserWebSocketRejectsAnUnconfiguredCrossSiteOrigin()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request =>
        {
            request.Headers.Host = "local.example";
            request.Headers.Remove("Origin");
            request.Headers.Origin = "https://attacker.example";
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using WebSocket socket = await socketClient.ConnectAsync(
                new Uri("ws://local.example/streaming"),
                timeout.Token);
        });
        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserWebSocketAcceptsTheConfiguredFrontendOrigin()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        WebSocketClient socketClient = fixture.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request =>
        {
            request.Headers.Host = "local.example";
            request.Headers.Remove("Origin");
            request.Headers.Origin = "https://client.local.example";
        };

        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri("ws://local.example/streaming"),
            timeout.Token);
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    private static async Task SendTextAsync(WebSocket socket, string value, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[128 * 1024];
        using var output = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }

    private static async Task<WebSocketReceiveResult> ReceiveCloseAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        WebSocketReceiveResult result = await socket.ReceiveAsync(new byte[1], cancellationToken);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        if (socket.State == WebSocketState.CloseReceived)
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
        }

        return result;
    }

    private static async Task ReceiveAuthenticationExpiredAsync(
        WebSocket socket,
        string forbiddenMarker,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string frame = await ReceiveTextAsync(socket, cancellationToken);
            Assert.DoesNotContain(forbiddenMarker, frame, StringComparison.Ordinal);
            using JsonDocument document = JsonDocument.Parse(frame);
            JsonElement root = document.RootElement;
            if (root.GetProperty("type").GetString() != "error")
            {
                continue;
            }

            Assert.Equal(
                "AUTHENTICATION_EXPIRED",
                root.GetProperty("body").GetProperty("code").GetString());
            return;
        }

        Assert.Fail("The stream did not report authentication expiry within the bounded frame window.");
    }

    private async Task<long> GetLatestStreamCursorAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/streaming/cursor",
            new { },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return body.RootElement.GetProperty("cursor").GetInt64();
    }

    private async Task CreatePublicStatusAsync(string marker, CancellationToken cancellationToken)
    {
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/statuses")
        {
            Content = JsonContent.Create(new { status = marker, visibility = "public" })
        };
        create.Headers.TryAddWithoutValidation("Idempotency-Key", marker);
        using HttpResponseMessage response = await client.SendAsync(create, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task EnsureNotFollowingAsync(string userId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage relation = await client.PostAsJsonAsync(
            "/api/users/relation",
            new { userId },
            cancellationToken);
        relation.EnsureSuccessStatusCode();
        using JsonDocument body = await JsonDocument.ParseAsync(
            await relation.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (!body.RootElement.GetProperty("isFollowing").GetBoolean() &&
            !body.RootElement.GetProperty("hasPendingFollowRequestFromYou").GetBoolean())
        {
            return;
        }

        using HttpRequestMessage request = PostMisskey(
            "/api/following/delete",
            "stream-cleanup-" + Guid.NewGuid().ToString("N"),
            new { userId });
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage PostMisskey(string path, string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<(long Cursor, string Payload)> ReadSseEventAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        long? cursor = null;
        string? payload = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                cursor = long.Parse(line[4..], System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                payload = line[6..];
            }
            else if (line.Length == 0 && cursor is not null && payload is not null)
            {
                return (cursor.Value, payload);
            }
        }

        throw new InvalidOperationException("The SSE stream ended before a complete event was received.");
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

    private sealed class SlowConsumerPump : IDurableStreamEventPump
    {
        public async IAsyncEnumerable<StreamEvent> SubscribeAsync(
            long afterCursor,
            int bufferCapacity,
            TimeSpan pollInterval,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return await Task.FromException<StreamEvent>(new StreamSlowConsumerException());
        }
    }

    private sealed class ExpireBetweenProjectionAndSendAuthentication : IMisskeyAuthenticationService
    {
        public const string Token = "mk_stream_projection_auth_race";

        private static readonly Guid TokenId = new("84d3e56b-2573-46af-80d9-b1a692be65f3");
        private int validationCount;

        public int ValidationCount => Volatile.Read(ref validationCount);

        public Task<MisskeyTokenPrincipal?> ValidateAsync(
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int call = Interlocked.Increment(ref validationCount);
            MisskeyTokenPrincipal? principal = call <= 3 && string.Equals(token, Token, StringComparison.Ordinal)
                ? new(
                    TokenId,
                    "https://local.example/users/alice",
                    "alice",
                    ["read:account"],
                    DateTimeOffset.UtcNow.AddMinutes(5))
                : null;
            return Task.FromResult(principal);
        }

        public Task<MisskeyIssuedToken> IssueDirectAsync(
            string username,
            string clientName,
            string? description,
            string? iconUri,
            IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MisskeyIssuedToken> IssueAsync(
            string username,
            string sessionKey,
            string clientName,
            string? description,
            string? iconUri,
            string? callbackUri,
            IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MisskeyIssuedToken?> ConsumeSessionAsync(
            string sessionKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MisskeyTokenSummary>> ListAsync(
            string actorIri,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RevokeAsync(
            string actorIri,
            Guid tokenId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
