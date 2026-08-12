using System.Text;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using ActivityPub.Workers.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class RelationshipStreamIntegrationTests(ActivityPubApiFixture fixture)
{
    private const string LocalActor = "https://local.example/users/alice";

    [Fact]
    public async Task RemoteInboxAcceptCommitsOneRecipientScopedRelationshipEventWithTheFollowState()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string remoteActorIri = $"https://relationship-{suffix}.example/users/bob";
        string followIri = $"https://local.example/activities/follow-{suffix}";
        string acceptIri = $"{remoteActorIri}/activities/accept-{suffix}";
        Guid remoteActorId;
        await using (AsyncServiceScope setupScope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            RemoteActor remote = RemoteActor.Create(
                remoteActorIri,
                "Person",
                "bob",
                $"{{\"id\":\"{remoteActorIri}\",\"type\":\"Person\",\"preferredUsername\":\"bob\"}}",
                DateTimeOffset.UtcNow);
            remoteActorId = remote.Id;
            db.RemoteActors.Add(remote);
            db.FollowRelations.Add(FollowRelation.Request(LocalActor, remoteActorIri, followIri, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        string json = $$"""
            {
              "@context": "https://www.w3.org/ns/activitystreams",
              "id": "{{acceptIri}}",
              "type": "Accept",
              "actor": "{{remoteActorIri}}",
              "object": "{{followIri}}",
              "to": "{{LocalActor}}"
            }
            """;
        byte[] body = Encoding.UTF8.GetBytes(json);
        var verified = new VerifiedInboundActivity(
            acceptIri,
            remoteActorIri,
            "Accept",
            followIri,
            null,
            new Uri(remoteActorIri).GetLeftPart(UriPartial.Authority),
            [new(LocalActor, AudienceField.To)],
            LocalActor,
            body,
            PayloadDigest.Sha256Hex(body),
            SignatureProfile.LegacyCavage,
            remoteActorIri + "#main-key",
            DateTimeOffset.UtcNow,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(acceptIri)),
            null,
            DateTimeOffset.UtcNow);

        await using (AsyncServiceScope workerScope = fixture.Services.CreateAsyncScope())
        {
            IInboxRepository repository = workerScope.ServiceProvider.GetRequiredService<IInboxRepository>();
            InboxAcceptance accepted = await repository.AcceptAsync(verified, CancellationToken.None);
            Assert.Equal(InboxAcceptanceStatus.Accepted, accepted.Status);
            InboxAcceptance duplicate = await repository.AcceptAsync(verified, CancellationToken.None);
            Assert.Equal(InboxAcceptanceStatus.Duplicate, duplicate.Status);
            string worker = "relationship-stream-" + Guid.NewGuid().ToString("N");
            IReadOnlyList<InboxItem> claimed = await repository.ClaimAsync(
                worker,
                100,
                TimeSpan.FromMinutes(2),
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            InboxItem item = Assert.Single(claimed, value => value.Id == accepted.InboxItemId);
            IInboxItemProcessor processor = workerScope.ServiceProvider.GetRequiredService<IInboxItemProcessor>();
            await processor.ProcessAsync(item, worker, CancellationToken.None);
        }

        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await verificationFactory.CreateDbContextAsync();
        FollowRelation relationship = await verification.FollowRelations.AsNoTracking()
            .SingleAsync(value => value.FollowActivityIri == followIri);
        Assert.Equal(FollowState.Accepted, relationship.State);
        StreamEvent streamEvent = await verification.StreamEvents.AsNoTracking().SingleAsync(value =>
            value.Kind == StreamEventKind.RelationshipChanged &&
            value.ResourceId == remoteActorId &&
            value.RecipientActorIri == LocalActor);
        Assert.Equal(remoteActorIri, streamEvent.ResourceIri);
        Assert.Equal(Visibility.MentionedOnly, streamEvent.Visibility);
        ActivityRecord persistedActivity = await verification.Activities.AsNoTracking()
            .SingleAsync(value => value.Iri == acceptIri);
        Assert.StartsWith($"activity:{persistedActivity.Id:N}:relationship:", streamEvent.DeduplicationKey, StringComparison.Ordinal);
    }
}
