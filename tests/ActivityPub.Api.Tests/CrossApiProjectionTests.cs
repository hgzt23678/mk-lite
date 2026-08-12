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
public sealed class CrossApiProjectionTests(ActivityPubApiFixture fixture)
{
    private readonly HttpClient client = CreateClient(fixture);

    [Fact]
    public async Task MastodonCreateIsReadThroughMisskeyWithoutDuplicateFederationSideEffects()
    {
        string marker = "mastodon-to-misskey-" + Guid.NewGuid().ToString("N");
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/statuses")
        {
            Content = JsonContent.Create(new
            {
                status = $"@publisher@media-blocked.example {marker}",
                visibility = "direct"
            })
        };
        create.Headers.TryAddWithoutValidation("Idempotency-Key", marker);
        using HttpResponseMessage created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using JsonDocument mastodon = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        string mastodonId = mastodon.RootElement.GetProperty("id").GetString()!;

        (Guid internalId, string misskeyId) = await ResolveAndMapAsync(
            ApiDialect.Mastodon,
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            mastodonId);
        Assert.NotEqual(mastodonId, misskeyId);
        Assert.True(long.TryParse(mastodonId, out _));
        Assert.Matches("^[0-9a-z]{10}$", misskeyId);

        using HttpResponseMessage shown = await client.PostAsJsonAsync(
            "/api/notes/show",
            new { noteId = misskeyId },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        using JsonDocument misskey = await JsonDocument.ParseAsync(await shown.Content.ReadAsStreamAsync());
        Assert.Equal(misskeyId, misskey.RootElement.GetProperty("id").GetString());
        Assert.Contains(marker, misskey.RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);

        await AssertSingleFederationMutationAsync(internalId);
    }

    [Fact]
    public async Task MisskeyCreateIsReadThroughMastodonWithoutDuplicateFederationSideEffects()
    {
        string marker = "misskey-to-mastodon-" + Guid.NewGuid().ToString("N");
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
        using HttpResponseMessage created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using JsonDocument misskey = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        string misskeyId = misskey.RootElement.GetProperty("createdNote").GetProperty("id").GetString()!;

        (Guid internalId, string mastodonId) = await ResolveAndMapAsync(
            ApiDialect.Misskey,
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            misskeyId);
        Assert.NotEqual(misskeyId, mastodonId);

        using var showRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/statuses/" + mastodonId);
        showRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-alice");
        using HttpResponseMessage shown = await client.SendAsync(showRequest);
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        using JsonDocument mastodon = await JsonDocument.ParseAsync(await shown.Content.ReadAsStreamAsync());
        Assert.Equal(mastodonId, mastodon.RootElement.GetProperty("id").GetString());
        Assert.Contains(marker, mastodon.RootElement.GetProperty("content").GetString(), StringComparison.Ordinal);

        await AssertSingleFederationMutationAsync(internalId);
    }

    private async Task<(Guid InternalId, string OtherDialectId)> ResolveAndMapAsync(
        ApiDialect sourceDialect,
        ApiDialect targetDialect,
        ExternalEntityType entityType,
        string sourceId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        Guid internalId = await externalIds.ResolveAsync(
            sourceDialect,
            entityType,
            sourceId,
            CancellationToken.None)
            ?? throw new InvalidOperationException("The source API returned an unresolvable identifier.");
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        DateTimeOffset timestamp = await db.Objects.Where(item => item.Id == internalId)
            .Select(item => item.PublishedAt)
            .SingleAsync();
        string targetId = await externalIds.GetOrCreateAsync(
            targetDialect,
            entityType,
            internalId,
            timestamp,
            CancellationToken.None);
        return (internalId, targetId);
    }

    private async Task AssertSingleFederationMutationAsync(Guid objectId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        string objectIri = await db.Objects.Where(item => item.Id == objectId).Select(item => item.Iri).SingleAsync();
        ActivityRecord activity = Assert.Single(await db.Activities.Where(item =>
            item.ObjectIri == objectIri && item.Type == "Create").ToArrayAsync());
        Delivery delivery = Assert.Single(await db.Deliveries.Where(item => item.ActivityId == activity.Id).ToArrayAsync());
        Assert.Equal("https://media-blocked.example/inbox", delivery.EndpointIri);
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
