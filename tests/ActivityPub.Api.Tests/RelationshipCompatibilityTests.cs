using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class RelationshipCompatibilityTests(ActivityPubApiFixture fixture)
{
    private readonly HttpClient client = CreateClient(fixture);

    [Fact]
    public async Task MisskeyUsersSearchUsesTheDolphinPrefixContractAndViewerSafeUserProjection()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users/search",
            new { query = "publ", limit = 10, detail = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement publisher = Assert.Single(
            json.RootElement.EnumerateArray(),
            value => string.Equals(value.GetProperty("username").GetString(), "publisher", StringComparison.Ordinal));
        Assert.True(publisher.GetProperty("id").GetString() is { Length: > 0 });
        Assert.True(publisher.TryGetProperty("avatarUrl", out _));
        Assert.True(publisher.TryGetProperty("followersCount", out _));

        using HttpResponseMessage invalid = await client.PostAsJsonAsync(
            "/api/users/search",
            new { query = "publ", username = "publisher" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using JsonDocument invalidJson = await JsonDocument.ParseAsync(await invalid.Content.ReadAsStreamAsync());
        Assert.Equal("INVALID_PARAM", invalidJson.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MisskeyUserFollowersAndFollowingExposePersistedDolphinRelationsWithStableIds()
    {
        using HttpResponseMessage following = await client.PostAsJsonAsync(
            "/api/users/following",
            new { userId = fixture.MisskeyLocalActorId, limit = 10 });
        using HttpResponseMessage followers = await client.PostAsJsonAsync(
            "/api/users/followers",
            new { userId = fixture.MisskeyRecipientActorId, limit = 10 });

        Assert.Equal(HttpStatusCode.OK, following.StatusCode);
        Assert.Equal(HttpStatusCode.OK, followers.StatusCode);
        using JsonDocument followingJson = await JsonDocument.ParseAsync(await following.Content.ReadAsStreamAsync());
        using JsonDocument followersJson = await JsonDocument.ParseAsync(await followers.Content.ReadAsStreamAsync());
        JsonElement outbound = Assert.Single(
            followingJson.RootElement.EnumerateArray(),
            value => string.Equals(
                value.GetProperty("followeeId").GetString(),
                fixture.MisskeyRecipientActorId,
                StringComparison.Ordinal));
        JsonElement inbound = Assert.Single(
            followersJson.RootElement.EnumerateArray(),
            value => string.Equals(
                value.GetProperty("followerId").GetString(),
                fixture.MisskeyLocalActorId,
                StringComparison.Ordinal));
        Assert.Equal(outbound.GetProperty("id").GetString(), inbound.GetProperty("id").GetString());
        Assert.Equal(fixture.MisskeyLocalActorId, outbound.GetProperty("followerId").GetString());
        Assert.Equal(fixture.MisskeyRecipientActorId, outbound.GetProperty("followeeId").GetString());
        Assert.Equal("bob", outbound.GetProperty("followee").GetProperty("username").GetString());
        Assert.Equal("alice", inbound.GetProperty("follower").GetProperty("username").GetString());
        Assert.False(outbound.TryGetProperty("follower", out _));
        Assert.False(inbound.TryGetProperty("followee", out _));

        using HttpResponseMessage repeated = await client.PostAsJsonAsync(
            "/api/users/following",
            new { userId = fixture.MisskeyLocalActorId, limit = 10 });
        using JsonDocument repeatedJson = await JsonDocument.ParseAsync(await repeated.Content.ReadAsStreamAsync());
        Assert.Equal(
            outbound.GetProperty("id").GetString(),
            Assert.Single(
                repeatedJson.RootElement.EnumerateArray(),
                value => string.Equals(
                    value.GetProperty("followeeId").GetString(),
                    fixture.MisskeyRecipientActorId,
                    StringComparison.Ordinal)).GetProperty("id").GetString());

        using HttpResponseMessage exhausted = await client.PostAsJsonAsync(
            "/api/users/following",
            new { userId = fixture.MisskeyLocalActorId, untilId = outbound.GetProperty("id").GetString(), limit = 10 });
        Assert.Equal(HttpStatusCode.OK, exhausted.StatusCode);
        using JsonDocument exhaustedJson = await JsonDocument.ParseAsync(await exhausted.Content.ReadAsStreamAsync());
        Assert.Empty(exhaustedJson.RootElement.EnumerateArray());

        using HttpResponseMessage missing = await client.PostAsJsonAsync(
            "/api/users/followers",
            new { userId = "missing-user-id" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using JsonDocument missingJson = await JsonDocument.ParseAsync(await missing.Content.ReadAsStreamAsync());
        Assert.Equal("NO_SUCH_USER", missingJson.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UserPreviewQueriesProjectPersistedMisskeyDetailAndRelationshipState()
    {
        string misskeyAccountId = fixture.MisskeyRemotePublisherId;
        using HttpResponseMessage userResponse = await client.PostAsJsonAsync(
            "/api/users/show",
            new { userId = misskeyAccountId });
        using HttpResponseMessage relationshipResponse = await client.PostAsJsonAsync(
            "/api/users/relation",
            new { userId = misskeyAccountId });

        Assert.Equal(HttpStatusCode.OK, userResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, relationshipResponse.StatusCode);
        using JsonDocument user = await JsonDocument.ParseAsync(await userResponse.Content.ReadAsStreamAsync());
        using JsonDocument relationship = await JsonDocument.ParseAsync(await relationshipResponse.Content.ReadAsStreamAsync());
        Assert.Equal(misskeyAccountId, user.RootElement.GetProperty("id").GetString());
        Assert.Equal("publisher", user.RootElement.GetProperty("username").GetString());
        Assert.Equal(JsonValueKind.String, user.RootElement.GetProperty("description").ValueKind);
        Assert.True(user.RootElement.TryGetProperty("notesCount", out JsonElement notesCount));
        Assert.True(user.RootElement.TryGetProperty("followingCount", out _));
        Assert.True(user.RootElement.TryGetProperty("followersCount", out _));
        Assert.True(user.RootElement.TryGetProperty("isLocked", out _));

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        RemoteActor actor = await db.RemoteActors.AsNoTracking().SingleAsync(x =>
            x.Iri == "https://media-blocked.example/users/publisher");
        long persistedNotes = await db.Objects.AsNoTracking().LongCountAsync(x => x.OwnerIri == actor.Iri);
        FollowRelation? persistedRelationship = await db.FollowRelations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.FollowerIri == "https://local.example/users/alice" && x.FollowedIri == actor.Iri);

        Assert.Equal(persistedNotes, notesCount.GetInt64());
        Assert.Equal(persistedRelationship?.State == FollowState.Accepted,
            relationship.RootElement.GetProperty("isFollowing").GetBoolean());
        Assert.Equal(persistedRelationship?.State == FollowState.Pending,
            relationship.RootElement.GetProperty("hasPendingFollowRequestFromYou").GetBoolean());
    }

    [Fact]
    public async Task MisskeyFollowAndMastodonUnfollowShareOneRelationAndExactFederationActivities()
    {
        string mastodonAccountId = await ExternalIdAsync(ApiDialect.Mastodon);
        string misskeyAccountId = fixture.MisskeyRemotePublisherId;
        string followKey = "follow-create-" + Guid.NewGuid().ToString("N");
        using HttpRequestMessage follow = Post(
            "/api/following/create",
            followKey,
            new { userId = misskeyAccountId });
        using HttpResponseMessage followed = await client.SendAsync(follow);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);

        using HttpResponseMessage relationResponse = await client.GetAsync(
            "/api/v1/accounts/relationships?id%5B%5D=" + mastodonAccountId);
        Assert.Equal(HttpStatusCode.OK, relationResponse.StatusCode);
        using JsonDocument mastodonRelation = await JsonDocument.ParseAsync(await relationResponse.Content.ReadAsStreamAsync());
        JsonElement relationship = Assert.Single(mastodonRelation.RootElement.EnumerateArray());
        Assert.False(relationship.GetProperty("following").GetBoolean());
        Assert.True(relationship.GetProperty("requested").GetBoolean());

        string unfollowKey = "follow-delete-" + Guid.NewGuid().ToString("N");
        using HttpRequestMessage unfollow = new(HttpMethod.Post, $"/api/v1/accounts/{mastodonAccountId}/unfollow");
        unfollow.Headers.TryAddWithoutValidation("Idempotency-Key", unfollowKey);
        using HttpResponseMessage unfollowed = await client.SendAsync(unfollow);
        Assert.Equal(HttpStatusCode.OK, unfollowed.StatusCode);

        using HttpResponseMessage misskeyRelationResponse = await client.PostAsJsonAsync(
            "/api/users/relation",
            new { userId = misskeyAccountId });
        Assert.Equal(HttpStatusCode.OK, misskeyRelationResponse.StatusCode);
        using JsonDocument misskeyRelation = await JsonDocument.ParseAsync(await misskeyRelationResponse.Content.ReadAsStreamAsync());
        Assert.False(misskeyRelation.RootElement.GetProperty("isFollowing").GetBoolean());
        Assert.False(misskeyRelation.RootElement.GetProperty("hasPendingFollowRequestFromYou").GetBoolean());

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        FollowRelation stored = await db.FollowRelations.AsNoTracking().SingleAsync(x =>
            x.FollowerIri == "https://local.example/users/alice" &&
            x.FollowedIri == "https://media-blocked.example/users/publisher");
        Assert.Equal(FollowState.Cancelled, stored.State);
        ActivityRecord followActivity = await db.Activities.AsNoTracking().SingleAsync(x => x.Iri == stored.FollowActivityIri);
        ActivityRecord[] undoCandidates = await db.Activities.AsNoTracking()
            .Where(x => x.Type == "Undo")
            .ToArrayAsync();
        ActivityRecord undoActivity = Assert.Single(undoCandidates, x =>
            x.RawJson.Contains(stored.FollowActivityIri, StringComparison.Ordinal));
        Assert.Equal("Follow", followActivity.Type);
        Assert.Equal(1, await db.Deliveries.CountAsync(x => x.ActivityId == followActivity.Id));
        Assert.Equal(1, await db.Deliveries.CountAsync(x => x.ActivityId == undoActivity.Id));
        Assert.Equal(1, await db.ClientIdempotency.CountAsync(x => x.Subject == "alice" && x.Key == followKey));
        Assert.Equal(1, await db.ClientIdempotency.CountAsync(x => x.Subject == "alice" && x.Key == unfollowKey));
    }

    [Fact]
    public async Task MastodonMuteAndMisskeyUnmuteSharePersistentRelationshipState()
    {
        string mastodonAccountId = await ExternalIdAsync(ApiDialect.Mastodon);
        string misskeyAccountId = fixture.MisskeyRemotePublisherId;
        using HttpResponseMessage muted = await client.PostAsJsonAsync(
            $"/api/v1/accounts/{mastodonAccountId}/mute",
            new { notifications = true, duration = 3600 });
        Assert.Equal(HttpStatusCode.OK, muted.StatusCode);
        using JsonDocument mastodon = await JsonDocument.ParseAsync(await muted.Content.ReadAsStreamAsync());
        Assert.True(mastodon.RootElement.GetProperty("muting").GetBoolean());
        Assert.True(mastodon.RootElement.GetProperty("muting_notifications").GetBoolean());

        using HttpResponseMessage misskeyRelationResponse = await client.PostAsJsonAsync(
            "/api/users/relation",
            new { userId = misskeyAccountId });
        using JsonDocument misskey = await JsonDocument.ParseAsync(await misskeyRelationResponse.Content.ReadAsStreamAsync());
        Assert.True(misskey.RootElement.GetProperty("isMuted").GetBoolean());

        using HttpResponseMessage unmuted = await client.PostAsJsonAsync(
            "/api/mute/delete",
            new { userId = misskeyAccountId });
        Assert.Equal(HttpStatusCode.NoContent, unmuted.StatusCode);
        using HttpResponseMessage relationship = await client.GetAsync(
            "/api/v1/accounts/relationships?id%5B%5D=" + mastodonAccountId);
        using JsonDocument after = await JsonDocument.ParseAsync(await relationship.Content.ReadAsStreamAsync());
        Assert.False(Assert.Single(after.RootElement.EnumerateArray()).GetProperty("muting").GetBoolean());

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        UserMute stored = await db.UserMutes.AsNoTracking().OrderByDescending(x => x.CreatedAt).FirstAsync(x =>
            x.OwnerActorIri == "https://local.example/users/alice" &&
            x.TargetActorIri == "https://media-blocked.example/users/publisher");
        Assert.NotNull(stored.RevokedAt);
    }

    [Fact]
    public async Task MisskeyBlockAndMastodonUnblockShareAggregateAndExactFederationUndo()
    {
        string mastodonAccountId = await ExternalIdAsync(ApiDialect.Mastodon);
        string misskeyAccountId = fixture.MisskeyRemotePublisherId;
        using HttpRequestMessage follow = Post(
            "/api/following/create",
            "block-follow-" + Guid.NewGuid().ToString("N"),
            new { userId = misskeyAccountId });
        using HttpResponseMessage followed = await client.SendAsync(follow);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);

        string blockKey = "block-create-" + Guid.NewGuid().ToString("N");
        using HttpRequestMessage block = Post(
            "/api/blocking/create",
            blockKey,
            new { userId = misskeyAccountId });
        using HttpResponseMessage blocked = await client.SendAsync(block);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        using HttpResponseMessage mastodonRelation = await client.GetAsync(
            "/api/v1/accounts/relationships?id%5B%5D=" + mastodonAccountId);
        using JsonDocument relation = await JsonDocument.ParseAsync(await mastodonRelation.Content.ReadAsStreamAsync());
        JsonElement item = Assert.Single(relation.RootElement.EnumerateArray());
        Assert.True(item.GetProperty("blocking").GetBoolean());
        Assert.False(item.GetProperty("following").GetBoolean());
        Assert.False(item.GetProperty("requested").GetBoolean());

        using HttpResponseMessage hidden = await client.GetAsync($"/api/v1/statuses/{fixture.MastodonPublicPostId}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        await using (AsyncServiceScope blockedScope = fixture.Services.CreateAsyncScope())
        {
            IRemoteRecipientResolver resolver = blockedScope.ServiceProvider.GetRequiredService<IRemoteRecipientResolver>();
            AudienceAddress[] audience = [new("https://media-blocked.example/users/publisher", AudienceField.To)];
            Assert.Empty(await resolver.ResolveAsync("https://local.example/users/alice", audience, CancellationToken.None));
            Assert.Single(await resolver.ResolveIncludingBlockedAsync("https://local.example/users/alice", audience, CancellationToken.None));
        }

        string unblockKey = "block-delete-" + Guid.NewGuid().ToString("N");
        using HttpRequestMessage unblock = new(HttpMethod.Post, $"/api/v1/accounts/{mastodonAccountId}/unblock");
        unblock.Headers.TryAddWithoutValidation("Idempotency-Key", unblockKey);
        using HttpResponseMessage unblocked = await client.SendAsync(unblock);
        Assert.Equal(HttpStatusCode.OK, unblocked.StatusCode);

        using HttpResponseMessage misskeyRelation = await client.PostAsJsonAsync(
            "/api/users/relation",
            new { userId = misskeyAccountId });
        using JsonDocument after = await JsonDocument.ParseAsync(await misskeyRelation.Content.ReadAsStreamAsync());
        Assert.False(after.RootElement.GetProperty("isBlocking").GetBoolean());
        using HttpResponseMessage visible = await client.GetAsync($"/api/v1/statuses/{fixture.MastodonPublicPostId}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        UserBlock stored = await db.UserBlocks.AsNoTracking().OrderByDescending(x => x.CreatedAt).FirstAsync(x =>
            x.OwnerActorIri == "https://local.example/users/alice" &&
            x.TargetActorIri == "https://media-blocked.example/users/publisher");
        Assert.Equal(FederatedRelationState.Reversed, stored.State);
        Assert.NotNull(stored.UndoActivityIri);
        ActivityRecord blockActivity = await db.Activities.AsNoTracking().SingleAsync(x => x.Iri == stored.BlockActivityIri);
        ActivityRecord undoActivity = await db.Activities.AsNoTracking().SingleAsync(x => x.Iri == stored.UndoActivityIri);
        Assert.Equal("Block", blockActivity.Type);
        Assert.Equal("Undo", undoActivity.Type);
        Assert.Contains(stored.BlockActivityIri, undoActivity.RawJson, StringComparison.Ordinal);
        Assert.Equal(1, await db.Deliveries.CountAsync(x => x.ActivityId == blockActivity.Id));
        Assert.Equal(1, await db.Deliveries.CountAsync(x => x.ActivityId == undoActivity.Id));
        Assert.Equal(1, await db.ClientIdempotency.CountAsync(x => x.Subject == "alice" && x.Key == blockKey));
        Assert.Equal(1, await db.ClientIdempotency.CountAsync(x => x.Subject == "alice" && x.Key == unblockKey));
    }

    private async Task<string> ExternalIdAsync(ApiDialect dialect)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        RemoteActor actor = await db.RemoteActors.AsNoTracking().SingleAsync(x =>
            x.Iri == "https://media-blocked.example/users/publisher");
        return await externalIds.GetOrCreateAsync(
            dialect,
            ExternalEntityType.Actor,
            actor.Id,
            actor.FetchedAt,
            CancellationToken.None);
    }

    private static HttpRequestMessage Post(string path, string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
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
