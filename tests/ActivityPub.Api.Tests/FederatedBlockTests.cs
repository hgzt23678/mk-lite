using System.Text;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using ActivityPub.Workers.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class FederatedBlockTests(ActivityPubApiFixture fixture)
{
    private const string RemoteActor = "https://media-blocked.example/users/publisher";
    private const string LocalActor = "https://local.example/users/alice";

    [Fact]
    public async Task InboundBlockAndExactUndoPersistDedicatedAggregate()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string blockIri = $"https://media-blocked.example/activities/block-{suffix}";
        await ProcessAsync(blockIri, "Block", LocalActor);

        await using (AsyncServiceScope activeScope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = activeScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync();
            UserBlock active = await db.UserBlocks.AsNoTracking().SingleAsync(x => x.BlockActivityIri == blockIri);
            Assert.Equal(FederatedRelationState.Active, active.State);
        }

        string undoIri = $"https://media-blocked.example/activities/undo-block-{suffix}";
        await ProcessAsync(undoIri, "Undo", blockIri);

        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await verificationFactory.CreateDbContextAsync();
        UserBlock stored = await verification.UserBlocks.AsNoTracking().SingleAsync(x => x.BlockActivityIri == blockIri);
        Assert.Equal(FederatedRelationState.Reversed, stored.State);
        Assert.Equal(undoIri, stored.UndoActivityIri);
    }

    private async Task ProcessAsync(string activityIri, string type, string objectIri)
    {
        string json = $$"""
            {
              "@context": "https://www.w3.org/ns/activitystreams",
              "id": "{{activityIri}}",
              "type": "{{type}}",
              "actor": "{{RemoteActor}}",
              "object": "{{objectIri}}",
              "to": "{{LocalActor}}"
            }
            """;
        byte[] body = Encoding.UTF8.GetBytes(json);
        var verified = new VerifiedInboundActivity(
            activityIri,
            RemoteActor,
            type,
            objectIri,
            null,
            "https://media-blocked.example",
            [new(LocalActor, AudienceField.To)],
            LocalActor,
            body,
            PayloadDigest.Sha256Hex(body),
            SignatureProfile.LegacyCavage,
            RemoteActor + "#main-key",
            DateTimeOffset.UtcNow,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(activityIri)),
            null,
            DateTimeOffset.UtcNow);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IInboxRepository repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        InboxAcceptance acceptance = await repository.AcceptAsync(verified, CancellationToken.None);
        Assert.Equal(InboxAcceptanceStatus.Accepted, acceptance.Status);
        Guid itemId = Assert.IsType<Guid>(acceptance.InboxItemId);
        string worker = "block-test-" + Guid.NewGuid().ToString("N");
        IReadOnlyList<InboxItem> claimed = await repository.ClaimAsync(
            worker,
            100,
            TimeSpan.FromMinutes(2),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        InboxItem item = Assert.Single(claimed, x => x.Id == itemId);
        IInboxItemProcessor processor = scope.ServiceProvider.GetRequiredService<IInboxItemProcessor>();
        await processor.ProcessAsync(item, worker, CancellationToken.None);
    }
}
