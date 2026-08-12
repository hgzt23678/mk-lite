using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using ActivityPub.Workers.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class FederatedEmojiReactionTests(ActivityPubApiFixture fixture)
{
    private const string RemoteActor = "https://media-blocked.example/users/publisher";
    private const string LocalActor = "https://local.example/users/alice";
    private const string ObjectIri = "https://media-blocked.example/objects/1";

    [Fact]
    public async Task MisskeyLikeAndLitePubEmojiReactRemainDistinctAndIdempotent()
    {
        const string unicodeActivityIri = "https://media-blocked.example/activities/reaction-unicode";
        string unicodeJson = $$"""
              {
                "@context": "https://www.w3.org/ns/activitystreams",
                "id": "https://media-blocked.example/activities/reaction-unicode",
                "type": "Like",
                "actor": "{{RemoteActor}}",
                "object": "{{ObjectIri}}",
                "to": "{{LocalActor}}",
                "_misskey_reaction": "🎉"
              }
              """;
        await ProcessAsync(unicodeActivityIri, unicodeJson);

        await using AsyncServiceScope duplicateScope = fixture.Services.CreateAsyncScope();
        IInboxRepository duplicateRepository = duplicateScope.ServiceProvider.GetRequiredService<IInboxRepository>();
        InboxAcceptance duplicate = await duplicateRepository.AcceptAsync(
            CreateVerified(unicodeActivityIri, unicodeJson), CancellationToken.None);
        Assert.Equal(InboxAcceptanceStatus.Duplicate, duplicate.Status);

        InboxAcceptance conflict = await duplicateRepository.AcceptAsync(CreateVerified(
            unicodeActivityIri,
            $$"""
              {"id":"{{unicodeActivityIri}}","type":"Like","actor":"{{RemoteActor}}","object":"{{ObjectIri}}","to":"{{LocalActor}}","_misskey_reaction":"🔥"}
              """), CancellationToken.None);
        Assert.Equal(InboxAcceptanceStatus.ConflictQuarantined, conflict.Status);

        await ProcessAsync(
            "https://media-blocked.example/activities/reaction-custom",
            $$"""
              {
                "@context": [
                  "https://www.w3.org/ns/activitystreams",
                  {"misskey":"https://misskey-hub.net/ns#","_misskey_reaction":"misskey:_misskey_reaction"}
                ],
                "id": "https://media-blocked.example/activities/reaction-custom",
                "type": "EmojiReaction",
                "actor": "{{RemoteActor}}",
                "object": "{{ObjectIri}}",
                "to": "{{LocalActor}}",
                "_misskey_reaction": ":party:",
                "tag": {
                  "id": "https://media-blocked.example/emojis/party",
                  "type": "Emoji",
                  "name": ":party:",
                  "icon": {"type":"Image","mediaType":"image/png","url":"https://media-blocked.example/media/party.png"}
                }
              }
              """);

        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        LikeRelation misskeyReaction = await db.LikeRelations.AsNoTracking()
            .Where(x => x.ActorIri == RemoteActor && x.ObjectIri == ObjectIri)
            .SingleAsync();
        EmojiReactionRelation litePubReaction = await db.EmojiReactionRelations.AsNoTracking()
            .Where(x => x.ActorIri == RemoteActor && x.ObjectIri == ObjectIri)
            .SingleAsync();

        Assert.Equal(FederatedRelationState.Active, misskeyReaction.State);
        Assert.Equal("🎉", misskeyReaction.EffectiveReaction);
        Assert.Equal(FederatedRelationState.Active, litePubReaction.State);
        Assert.Equal(":party@media-blocked.example:", litePubReaction.Reaction);
        Assert.Equal("https://media-blocked.example/media/party.png", litePubReaction.CustomEmojiUrl);

        await ProcessAsync(
            "https://media-blocked.example/activities/reaction-custom-undo",
            $$"""
              {
                "@context": "https://www.w3.org/ns/activitystreams",
                "id": "https://media-blocked.example/activities/reaction-custom-undo",
                "type": "Undo",
                "actor": "{{RemoteActor}}",
                "object": "https://media-blocked.example/activities/reaction-custom",
                "to": "{{LocalActor}}"
              }
              """);

        EmojiReactionRelation reversed = await db.EmojiReactionRelations.AsNoTracking()
            .Where(x => x.ActorIri == RemoteActor && x.ObjectIri == ObjectIri)
            .SingleAsync();
        LikeRelation stillActive = await db.LikeRelations.AsNoTracking()
            .Where(x => x.ActorIri == RemoteActor && x.ObjectIri == ObjectIri)
            .SingleAsync();
        Assert.Equal(FederatedRelationState.Reversed, reversed.State);
        Assert.Equal(FederatedRelationState.Active, stillActive.State);
    }

    private async Task ProcessAsync(string activityIri, string json)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IInboxRepository repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        InboxAcceptance acceptance = await repository.AcceptAsync(
            CreateVerified(activityIri, json),
            CancellationToken.None);
        Assert.Equal(InboxAcceptanceStatus.Accepted, acceptance.Status);
        Guid inboxItemId = Assert.IsType<Guid>(acceptance.InboxItemId);

        string worker = "reaction-test-" + Guid.NewGuid().ToString("N");
        IReadOnlyList<InboxItem> claimed = await repository.ClaimAsync(
            worker,
            100,
            TimeSpan.FromMinutes(2),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        InboxItem item = Assert.Single(claimed, x => x.Id == inboxItemId);
        IInboxItemProcessor processor = scope.ServiceProvider.GetRequiredService<IInboxItemProcessor>();
        await processor.ProcessAsync(item, worker, CancellationToken.None);
    }

    private static VerifiedInboundActivity CreateVerified(string activityIri, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        using JsonDocument document = JsonDocument.Parse(body);
        string activityType = document.RootElement.GetProperty("type").GetString()!;
        string? objectIri = document.RootElement.TryGetProperty("object", out JsonElement objectValue) &&
            objectValue.ValueKind == JsonValueKind.String
                ? objectValue.GetString()
                : null;
        return new(
            activityIri,
            RemoteActor,
            activityType,
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
    }
}
