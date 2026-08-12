using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class MisskeyVisibilityContractTests(ActivityPubApiFixture fixture)
{
    [Fact]
    public async Task RemoteAccountImagesUseSameOriginOpaqueProxyPaths()
    {
        using HttpClient client = CreateClient(authenticated: false);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users/show",
            new { userId = fixture.MisskeyRecipientActorId },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        string expectedPrefix = $"/media/proxy/actor/{fixture.MisskeyRecipientActorId}/";
        string avatar = Assert.IsType<string>(json.RootElement.GetProperty("avatarUrl").GetString());
        string banner = Assert.IsType<string>(json.RootElement.GetProperty("bannerUrl").GetString());
        Assert.Equal(expectedPrefix + RemoteMediaSourceToken.Create(ActivityPubApiFixture.RecipientAvatarIri), avatar);
        Assert.Equal(expectedPrefix + RemoteMediaSourceToken.Create(ActivityPubApiFixture.RecipientBannerIri), banner);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("remote.example/media", body, StringComparison.Ordinal);
        Assert.DoesNotContain("cdn.remote.example", body, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.RecipientActorId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledMediaReturnsExplicitUnavailableForActorProxy()
    {
        using HttpClient client = CreateClient(authenticated: false);
        string path = $"/media/proxy/actor/{fixture.MisskeyRecipientActorId}/{RemoteMediaSourceToken.Create(ActivityPubApiFixture.RecipientAvatarIri)}";

        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ActorProxyResolvesExternalIdAndHonorsConditionalGet()
    {
        var proxy = new FixtureActorMediaProxy();
        using WebApplicationFactory<Program> enabledFactory = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Media:Enabled", "true");
            builder.UseSetting("Media:Bucket", "api-test-media");
            builder.UseSetting("Media:GarbageCollectionEnabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Media:Enabled"] = "true",
                    ["Media:Bucket"] = "api-test-media",
                    ["Media:GarbageCollectionEnabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRemoteMediaProxyService>();
                services.AddSingleton<IRemoteMediaProxyService>(proxy);
            });
        });
        using HttpClient client = enabledFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });
        string path = $"/media/proxy/actor/{fixture.MisskeyRecipientActorId}/{RemoteMediaSourceToken.Create(ActivityPubApiFixture.RecipientAvatarIri)}";

        using HttpResponseMessage first = await client.GetAsync(path, CancellationToken.None);

        Assert.True(
            first.StatusCode == HttpStatusCode.OK,
            $"Expected actor proxy 200 but received {(int)first.StatusCode}; proxy calls: {proxy.ActorReadCount}; body: {await first.Content.ReadAsStringAsync()}.");
        Assert.Equal("\"actor-content-hash\"", first.Headers.ETag?.ToString());
        Assert.Equal(FixtureActorMediaProxy.LastModified, first.Content.Headers.LastModified);
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=86400", first.Headers.CacheControl?.ToString());
        Assert.Equal([0x89, 0x50, 0x4e, 0x47], await first.Content.ReadAsByteArrayAsync());

        using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
        conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"actor-content-hash\""));
        using HttpResponseMessage second = await client.SendAsync(conditional, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, proxy.ActorReadCount);
        Assert.All(proxy.ActorIds, id => Assert.Equal(fixture.RecipientActorId, id));
        Assert.All(proxy.SourceTokens, token => Assert.Equal(
            RemoteMediaSourceToken.Create(ActivityPubApiFixture.RecipientAvatarIri),
            token));

        using HttpResponseMessage unknownActor = await client.GetAsync(
            "/media/proxy/actor/0000000000/0123456789abcdef0123456789abcdef",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, unknownActor.StatusCode);
        Assert.Equal(2, proxy.ActorReadCount);
    }

    [Fact]
    public async Task UsersShowArrayPreservesRequestedOrderAndDetailedShape()
    {
        using HttpClient client = CreateClient(authenticated: false);
        string[] requested = [fixture.MisskeyRemotePublisherId, fixture.MisskeyLocalActorId];

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users/show",
            new { userIds = requested },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement[] users = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(requested, users.Select(user => user.GetProperty("id").GetString()));
        Assert.All(users, user =>
        {
            Assert.Equal(JsonValueKind.String, user.GetProperty("username").ValueKind);
            Assert.True(user.TryGetProperty("avatarUrl", out _));
            Assert.True(user.TryGetProperty("createdAt", out _));
        });
    }

    [Fact]
    public async Task UsersShowRejectsDuplicateIdsInsteadOfReturningAmbiguousRows()
    {
        using HttpClient client = CreateClient(authenticated: false);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users/show",
            new { userIds = new[] { fixture.MisskeyLocalActorId, fixture.MisskeyLocalActorId } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("INVALID_PARAM", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MentionedRecipientIdsAreProjectedOnlyAfterTheNoteViewerIsAuthorized()
    {
        using HttpClient anonymous = CreateClient(authenticated: false);
        using HttpResponseMessage denied = await anonymous.PostAsJsonAsync(
            "/api/notes/show",
            new { noteId = fixture.MisskeyPrivatePostId },
            CancellationToken.None);
        string deniedBody = await denied.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain(fixture.MisskeyRecipientActorId, deniedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("private-fixture-secret", deniedBody, StringComparison.Ordinal);

        using HttpClient owner = CreateClient(authenticated: true);
        using HttpResponseMessage allowed = await owner.PostAsJsonAsync(
            "/api/notes/show",
            new { noteId = fixture.MisskeyPrivatePostId },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await allowed.Content.ReadAsStreamAsync());
        Assert.Equal("specified", json.RootElement.GetProperty("visibility").GetString());
        Assert.False(json.RootElement.GetProperty("localOnly").GetBoolean());
        Assert.Equal(
            [fixture.MisskeyRecipientActorId],
            json.RootElement.GetProperty("visibleUserIds").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain(
            fixture.MisskeyLocalActorId,
            json.RootElement.GetProperty("visibleUserIds").EnumerateArray().Select(value => value.GetString()),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task LocalOnlyFlagIsProjectedFromThePersistedNoteContract()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string iri = $"https://local.example/objects/local-only-{Guid.NewGuid():N}";
        string raw = JsonSerializer.Serialize(new
        {
            id = iri,
            type = "Note",
            attributedTo = "https://local.example/users/alice",
            content = "local-only-contract",
            localOnly = true,
            to = "https://www.w3.org/ns/activitystreams#Public"
        });
        Guid objectId;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            FederatedObject note = FederatedObject.Create(
                iri,
                "https://local.example/users/alice",
                "Note",
                Visibility.Public,
                raw,
                PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(raw)),
                now,
                now);
            objectId = note.Id;
            db.Objects.Add(note);
            await db.SaveChangesAsync();
        }

        string noteId;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IExternalEntityIdService ids = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
            noteId = await ids.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Post,
                objectId,
                now,
                CancellationToken.None);
        }

        using HttpClient client = CreateClient(authenticated: false);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/notes/show",
            new { noteId },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.GetProperty("localOnly").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("visibleUserIds").EnumerateArray());
    }

    private HttpClient CreateClient(bool authenticated)
    {
        HttpClient client = fixture.CreateClient(new()
        {
            BaseAddress = new Uri("https://local.example"),
            AllowAutoRedirect = false
        });
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-alice");
        }
        return client;
    }

    private sealed class FixtureActorMediaProxy : IRemoteMediaProxyService
    {
        public static readonly DateTimeOffset LastModified = new(2026, 8, 2, 11, 0, 0, TimeSpan.Zero);

        private readonly List<Guid> actorIds = [];
        private readonly List<string> sourceTokens = [];

        public int ActorReadCount => actorIds.Count;
        public IReadOnlyList<Guid> ActorIds => actorIds;
        public IReadOnlyList<string> SourceTokens => sourceTokens;

        public Task<MediaDownload?> OpenReadAsync(
            Guid objectId,
            string sourceToken,
            string? requesterActorIri,
            CancellationToken cancellationToken) => Task.FromResult<MediaDownload?>(null);

        public Task<RemoteMediaOpenResult> OpenActorReadAsync(
            Guid remoteActorId,
            string sourceToken,
            CancellationToken cancellationToken)
        {
            actorIds.Add(remoteActorId);
            sourceTokens.Add(sourceToken);
            return Task.FromResult(RemoteMediaOpenResult.Success(new MediaDownload(
                new MemoryStream([0x89, 0x50, 0x4e, 0x47], writable: false),
                "image/png",
                4,
                "avatar.png",
                true,
                "\"actor-content-hash\"",
                LastModified)));
        }
    }
}
