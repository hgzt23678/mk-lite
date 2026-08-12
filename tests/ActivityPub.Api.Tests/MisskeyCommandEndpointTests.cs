using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class MisskeyCommandEndpointTests(ActivityPubApiFixture fixture)
{
    private static readonly string[] LitePubContexts =
        ["https://www.w3.org/ns/activitystreams", "http://litepub.social/ns#"];

    private readonly HttpClient client = CreateClient(fixture);

    [Fact]
    public async Task CreateNotePersistsObjectActivityAndIdempotencyAtomically()
    {
        string key = "note-create-" + Guid.NewGuid().ToString("N");
        using HttpRequestMessage firstRequest = Post("/api/notes/create", key, new
        {
            text = "Misskey v12 compose fixture",
            visibility = "public",
            localOnly = false
        });
        using HttpResponseMessage first = await client.SendAsync(firstRequest);
        using HttpRequestMessage replayRequest = Post("/api/notes/create", key, new
        {
            text = "Misskey v12 compose fixture",
            visibility = "public",
            localOnly = false
        });
        using HttpResponseMessage replay = await client.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using JsonDocument firstJson = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
        using JsonDocument replayJson = await JsonDocument.ParseAsync(await replay.Content.ReadAsStreamAsync());
        string noteId = firstJson.RootElement.GetProperty("createdNote").GetProperty("id").GetString()!;
        Assert.Equal(noteId, replayJson.RootElement.GetProperty("createdNote").GetProperty("id").GetString());

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        Guid id = await ResolveMisskeyIdAsync(ExternalEntityType.Post, noteId);
        FederatedObject stored = await db.Objects.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(Visibility.Public, stored.Visibility);
        Assert.Contains("Misskey v12 compose fixture", stored.RawJson, StringComparison.Ordinal);
        Assert.Equal(1, await db.ClientIdempotency.CountAsync(x => x.Subject == "alice" && x.Key == key));
    }

    [Fact]
    public async Task CustomReactionCreatesMisskeyCompatibleLikeAndDurableDelivery()
    {
        (Guid objectId, string objectIri) = await AddRemoteCustomEmojiNoteAsync();
        string noteId = await GetMisskeyIdAsync(ExternalEntityType.Post, objectId);
        string key = "reaction-create-" + Guid.NewGuid().ToString("N");
        using HttpRequestMessage request = Post("/api/notes/reactions/create", key, new
        {
            noteId,
            reaction = ":party@media-blocked.example:"
        });
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        LikeRelation relation = await db.LikeRelations.AsNoTracking().SingleAsync(x =>
            x.ActorIri == "https://local.example/users/alice" &&
            x.ObjectIri == objectIri &&
            x.State == FederatedRelationState.Active);
        Assert.Equal(":party@media-blocked.example:", relation.EffectiveReaction);
        Assert.Equal("https://media-blocked.example/media/party.png", relation.CustomEmojiUrl);

        ActivityRecord activity = await db.Activities.AsNoTracking().SingleAsync(x => x.Iri == relation.ActivityIri);
        Assert.Equal("Like", activity.Type);
        Assert.Contains("_misskey_reaction", activity.RawJson, StringComparison.Ordinal);
        Assert.Contains(":party@media-blocked.example:", activity.RawJson, StringComparison.Ordinal);
        Assert.Contains("https://media-blocked.example/media/party.png", activity.RawJson, StringComparison.Ordinal);
        Delivery delivery = await db.Deliveries.AsNoTracking().SingleAsync(x =>
            x.ActivityId == activity.Id &&
            x.EndpointIri == "https://media-blocked.example/inbox/shared");
        Assert.Equal("https://media-blocked.example/inbox/shared", delivery.EndpointIri);
        Assert.Equal(activity.PayloadHash, PayloadDigest.Sha256Hex(delivery.Payload));
        using JsonDocument delivered = JsonDocument.Parse(delivery.Payload);
        Assert.Equal("Like", delivered.RootElement.GetProperty("type").GetString());
        Assert.Equal(":party@media-blocked.example:", delivered.RootElement.GetProperty("_misskey_reaction").GetString());
    }

    [Fact]
    public async Task LitePubPeerReceivesEmojiReactWithoutMisskeyQualification()
    {
        (Guid objectId, string objectIri) = await AddLitePubCustomEmojiNoteAsync();
        string noteId = await GetMisskeyIdAsync(ExternalEntityType.Post, objectId);
        using HttpRequestMessage request = Post(
            "/api/notes/reactions/create",
            "litepub-reaction-" + Guid.NewGuid().ToString("N"),
            new { noteId, reaction = ":blob@akkoma.example:" });
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        EmojiReactionRelation relation = await db.EmojiReactionRelations.AsNoTracking().SingleAsync(x =>
            x.ActorIri == "https://local.example/users/alice" && x.ObjectIri == objectIri &&
            x.State == FederatedRelationState.Active);
        ActivityRecord activity = await db.Activities.AsNoTracking().SingleAsync(x => x.Iri == relation.ActivityIri);
        Delivery delivery = await db.Deliveries.AsNoTracking().SingleAsync(x =>
            x.ActivityId == activity.Id &&
            x.EndpointIri == "https://akkoma.example/inbox");
        using JsonDocument json = JsonDocument.Parse(delivery.Payload);

        Assert.Equal("EmojiReact", activity.Type);
        Assert.Equal("EmojiReact", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(":blob:", json.RootElement.GetProperty("content").GetString());
        Assert.False(json.RootElement.TryGetProperty("_misskey_reaction", out _));
        Assert.Equal(":blob@akkoma.example:", relation.Reaction);
        Assert.Equal("https://akkoma.example/inbox", delivery.EndpointIri);
    }

    [Fact]
    public async Task DeletingReactionFederatesUndoOfTheExactPriorActivity()
    {
        (Guid objectId, string objectIri) = await AddLitePubCustomEmojiNoteAsync();
        string noteId = await GetMisskeyIdAsync(ExternalEntityType.Post, objectId);
        using HttpRequestMessage createRequest = Post(
            "/api/notes/reactions/create",
            "litepub-create-" + Guid.NewGuid().ToString("N"),
            new { noteId, reaction = ":blob@akkoma.example:" });
        using HttpResponseMessage createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.NoContent, createResponse.StatusCode);

        string priorActivityIri;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            priorActivityIri = await db.EmojiReactionRelations.AsNoTracking()
                .Where(x => x.ActorIri == "https://local.example/users/alice" && x.ObjectIri == objectIri &&
                            x.State == FederatedRelationState.Active)
                .Select(x => x.ActivityIri)
                .SingleAsync();
        }

        using HttpRequestMessage deleteRequest = Post(
            "/api/notes/reactions/delete",
            "litepub-delete-" + Guid.NewGuid().ToString("N"),
            new { noteId, reaction = ":blob@akkoma.example:" });
        using HttpResponseMessage deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verificationDb = await verificationFactory.CreateDbContextAsync();
        EmojiReactionRelation relation = await verificationDb.EmojiReactionRelations.AsNoTracking()
            .SingleAsync(x => x.ActivityIri == priorActivityIri);
        Assert.Equal(FederatedRelationState.Reversed, relation.State);
        ActivityRecord[] undoCandidates = await verificationDb.Activities.AsNoTracking()
            .Where(x => x.ActorIri == "https://local.example/users/alice" && x.Type == "Undo")
            .ToArrayAsync();
        ActivityRecord undo = Assert.Single(undoCandidates, x => x.RawJson.Contains(priorActivityIri, StringComparison.Ordinal));
        Delivery delivery = await verificationDb.Deliveries.AsNoTracking().SingleAsync(x =>
            x.ActivityId == undo.Id &&
            x.EndpointIri == "https://akkoma.example/inbox");
        using JsonDocument payload = JsonDocument.Parse(delivery.Payload);
        Assert.Equal("Undo", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal(priorActivityIri, payload.RootElement.GetProperty("object").GetString());
        Assert.Equal("https://akkoma.example/inbox", delivery.EndpointIri);
    }

    private async Task<(Guid Id, string Iri)> AddRemoteCustomEmojiNoteAsync()
    {
        string iri = $"https://media-blocked.example/objects/{Guid.NewGuid():N}";
        string raw = JsonSerializer.Serialize(new
        {
            id = iri,
            type = "Note",
            attributedTo = "https://media-blocked.example/users/publisher",
            content = "custom emoji reaction target",
            to = "https://www.w3.org/ns/activitystreams#Public",
            tag = new
            {
                id = "https://media-blocked.example/emojis/party",
                type = "Emoji",
                name = ":party:",
                icon = new
                {
                    type = "Image",
                    mediaType = "image/png",
                    url = "https://media-blocked.example/media/party.png"
                }
            }
        });
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        FederatedObject item = FederatedObject.Create(
            iri,
            "https://media-blocked.example/users/publisher",
            "Note",
            Visibility.Public,
            raw,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(raw)),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        db.Objects.Add(item);
        await db.SaveChangesAsync();
        return (item.Id, item.Iri);
    }

    private async Task<(Guid Id, string Iri)> AddLitePubCustomEmojiNoteAsync()
    {
        string actorIri = $"https://akkoma.example/users/reactor-{Guid.NewGuid():N}";
        string iri = $"https://akkoma.example/objects/{Guid.NewGuid():N}";
        string rawActor = JsonSerializer.Serialize(new
        {
            @context = LitePubContexts,
            id = actorIri,
            type = "Person",
            preferredUsername = "reactor"
        });
        string rawObject = JsonSerializer.Serialize(new
        {
            id = iri,
            type = "Note",
            attributedTo = actorIri,
            content = "LitePub custom emoji target",
            to = "https://www.w3.org/ns/activitystreams#Public",
            tag = new
            {
                id = "https://akkoma.example/emoji/blob",
                type = "Emoji",
                name = ":blob:",
                icon = new { type = "Image", mediaType = "image/png", url = "https://akkoma.example/emoji/blob.png" }
            }
        });
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        db.RemoteActors.Add(RemoteActor.Create(actorIri, "Person", "reactor", rawActor, now));
        db.RemoteEndpoints.Add(RemoteEndpoint.Create(actorIri, EndpointKind.Inbox, "https://akkoma.example/inbox", now));
        FederatedObject item = FederatedObject.Create(
            iri,
            actorIri,
            "Note",
            Visibility.Public,
            rawObject,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(rawObject)),
            now,
            now);
        db.Objects.Add(item);
        await db.SaveChangesAsync();
        return (item.Id, item.Iri);
    }

    private async Task<string> GetMisskeyIdAsync(ExternalEntityType entityType, Guid internalId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        return await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            entityType,
            internalId,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
    }

    private async Task<Guid> ResolveMisskeyIdAsync(ExternalEntityType entityType, string externalId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        return await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            entityType,
            externalId,
            CancellationToken.None)
            ?? throw new InvalidOperationException("The response contained an unresolvable Misskey identifier.");
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

    private static HttpRequestMessage Post<T>(string path, string idempotencyKey, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
