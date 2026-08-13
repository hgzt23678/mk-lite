using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence.Tests;

[Collection(PostgreSqlFixtureDefinition.Name)]
public sealed class ProjectionCachePrivacyIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task PublicTimelineRevalidatesVisibilityForRedisCandidateIds()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://local.example/users/cache-{suffix}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        FederatedObject publicObject = CreateObject(actorIri, suffix + "-public", Visibility.Public, now);
        FederatedObject privateObject = CreateObject(actorIri, suffix + "-private", Visibility.MentionedOnly, now.AddSeconds(1));

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext db = await factory.CreateDbContextAsync())
        {
            db.LocalActors.Add(LocalActor.Create(
                actorIri,
                "c" + suffix[..20],
                ActorKind.Person,
                now));
            db.Objects.AddRange(publicObject, privateObject);
            await db.SaveChangesAsync();
        }

        var query = new ClientApiQueryService(
            factory,
            new FixedCandidateCache([privateObject.Id, publicObject.Id]));
        ClientPage<ClientPostView> page = await query.ReadPublicTimelineAsync(
            null,
            40,
            false,
            CancellationToken.None);

        Assert.Contains(page.Items, item => item.Id == publicObject.Id);
        Assert.DoesNotContain(page.Items, item => item.Id == privateObject.Id);
    }

    private static FederatedObject CreateObject(
        string actorIri,
        string suffix,
        Visibility visibility,
        DateTimeOffset now)
    {
        string iri = "https://local.example/objects/" + suffix;
        string raw = JsonSerializer.Serialize(new
        {
            id = iri,
            type = "Note",
            attributedTo = actorIri,
            content = "cache privacy fixture"
        });
        return FederatedObject.Create(
            iri,
            actorIri,
            "Note",
            visibility,
            raw,
            PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(raw)),
            now,
            now);
    }

    private sealed class FixedCandidateCache(IReadOnlyList<Guid> ids) : IClientProjectionCache
    {
        public bool IsEnabled => true;

        public Task<IReadOnlyList<Guid>?> GetTimelineCandidatesAsync(
            string timeline,
            string? viewerActorIri,
            Guid? beforeId,
            int candidateLimit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>?>(ids);

        public Task SetTimelineCandidatesAsync(
            string timeline,
            string? viewerActorIri,
            Guid? beforeId,
            int candidateLimit,
            IReadOnlyList<Guid> objectIds,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<long?> GetUnreadNotificationCountAsync(
            string recipientActorIri,
            CancellationToken cancellationToken) => Task.FromResult<long?>(null);

        public Task SetUnreadNotificationCountAsync(
            string recipientActorIri,
            long count,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateNotificationsAsync(
            string recipientActorIri,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
