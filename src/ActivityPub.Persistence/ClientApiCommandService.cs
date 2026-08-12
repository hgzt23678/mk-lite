using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed partial class ClientApiCommandService(
    IDbContextFactory<FederationDbContext> contextFactory,
    IClientOutboxService outbox,
    IClientApiQueryService queryService,
    IHashtagRepository hashtags,
    IAnnounceChainGuard announceChainGuard,
    PublicIriFactory iriFactory) : IClientApiCommandService
{
    public async Task<ClientPostView> CreatePostAsync(
        string username,
        string idempotencyKey,
        ClientPostMutation mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (string.IsNullOrWhiteSpace(mutation.SourceText) && mutation.MediaIds.Count == 0 && mutation.Poll is null)
        {
            throw new ArgumentException("A post requires text, media, or a poll.", nameof(mutation));
        }
        string actorIri = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        var note = new Dictionary<string, object?>
        {
            ["type"] = mutation.Poll is null ? "Note" : "Question",
            ["content"] = EncodeStatus(mutation.SourceText),
            ["source"] = new Dictionary<string, object?>
            {
                ["content"] = mutation.SourceText,
                ["mediaType"] = mutation.SourceFormat
            },
            ["sensitive"] = mutation.Sensitive,
            ["summary"] = mutation.ContentWarning ?? string.Empty
        };
        if (mutation.Poll is not null)
        {
            string[] choices = ValidatePollMutation(mutation.Poll);
            string property = mutation.Poll.Multiple ? "anyOf" : "oneOf";
            note[property] = choices.Select(choice => new Dictionary<string, object?>
            {
                ["type"] = "Note",
                ["name"] = choice,
                ["replies"] = new Dictionary<string, object?>
                {
                    ["type"] = "Collection",
                    ["totalItems"] = 0
                }
            }).ToArray();
            if (mutation.Poll.ExpiresAt is { } expiresAt)
            {
                if (expiresAt <= DateTimeOffset.UtcNow)
                {
                    throw new ArgumentException("A poll expiration must be in the future.", nameof(mutation));
                }

                note["endTime"] = expiresAt;
            }
        }
        IReadOnlyList<ResolvedMention> mentions = await ResolveMentionsAsync(
            mutation.SourceText,
            cancellationToken).ConfigureAwait(false);
        (string[] to, string[] cc) = ResolveAudience(
            actorIri,
            mutation.Visibility,
            mentions);
        note["to"] = to;
        note["cc"] = cc;
        if (mentions.Count > 0)
        {
            note["tag"] = mentions.Select(mention => new Dictionary<string, object?>
            {
                ["type"] = "Mention",
                ["href"] = mention.ActorIri,
                ["name"] = mention.Name
            }).ToArray();
        }
        if (mutation.InReplyToId is not null)
        {
            note["inReplyTo"] = await RequireObjectIriAsync(mutation.InReplyToId.Value, cancellationToken).ConfigureAwait(false);
        }

        if (mutation.QuoteTargetId is not null)
        {
            note["quoteUri"] = await RequireObjectIriAsync(mutation.QuoteTargetId.Value, cancellationToken).ConfigureAwait(false);
        }

        if (mutation.MediaIds.Count > 0)
        {
            note["attachment"] = await ResolveMediaAttachmentsAsync(
                actorIri,
                mutation.MediaIds,
                cancellationToken).ConfigureAwait(false);
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(note);
        ClientOutboxResult result = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        Guid objectId = await RequireObjectIdByIriAsync(result.ObjectIri, cancellationToken).ConfigureAwait(false);
        ClientPostView view = await queryService.FindPostAsync(objectId, actorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Created status is not readable after commit.");
        IReadOnlyList<string> extractedTags = ExtractHashtags(mutation.SourceText);
        if (extractedTags.Count > 0)
        {
            await hashtags.RecordUsageAsync(extractedTags, actorIri, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        return view;
    }

    public async Task<ClientPostView> DeletePostAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        string actorIri = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        FederatedObject item = await RequireOwnedObjectAsync(actorIri, postId, cancellationToken).ConfigureAwait(false);
        ClientPostView status = await queryService.FindPostAsync(postId, actorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found.");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "Delete",
            @object = item.Iri,
            to = new[] { "https://www.w3.org/ns/activitystreams#Public", actorIri.TrimEnd('/') + "/followers" }
        });
        _ = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        return status;
    }

    public Task<ClientPostView> LikeAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ApplyObjectActivityAsync(username, postId, idempotencyKey, "Like", cancellationToken);

    public Task<ClientPostView> AnnounceAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ApplyObjectActivityAsync(username, postId, idempotencyKey, "Announce", cancellationToken);

    public async Task<ClientPostView> UndoLikeAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await UndoObjectActivityAsync(username, postId, idempotencyKey, "Like", cancellationToken).ConfigureAwait(false);

    public async Task<ClientPostView> ReactAsync(
        string username,
        Guid postId,
        string reaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        (string actorIri, FederatedObject item) = await RequireReadableReactionTargetAsync(
            username,
            postId,
            cancellationToken).ConfigureAwait(false);
        FederatedReaction normalized = ResolveReaction(item, actorIri, reaction);
        ReactionReference? existing = await FindReactionAsync(
            actorIri,
            item.Iri,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null && string.Equals(existing.Reaction, normalized.Value, StringComparison.Ordinal))
        {
            throw new ClientReactionStateException(
                ClientReactionStateError.AlreadyReacted,
                "The authenticated actor already uses this reaction.");
        }

        ReactionAudience audience = BuildReactionAudience(item.Visibility, actorIri, item.OwnerIri);
        if (existing is not null)
        {
            byte[] undoBody = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "Undo",
                @object = existing.ActivityIri,
                to = audience.To,
                cc = audience.Cc
            });
            _ = await outbox.SubmitAsync(
                username,
                DeriveReactionIdempotencyKey(idempotencyKey, "undo"),
                undoBody,
                cancellationToken).ConfigureAwait(false);
        }

        bool useEmojiReact = await UsesLitePubEmojiReactAsync(item.OwnerIri, cancellationToken).ConfigureAwait(false);
        var activity = new Dictionary<string, object?>
        {
            ["type"] = useEmojiReact ? "EmojiReact" : "Like",
            ["object"] = item.Iri,
            ["content"] = normalized.Value,
            ["to"] = audience.To,
            ["cc"] = audience.Cc
        };
        if (!useEmojiReact)
        {
            activity["_misskey_reaction"] = normalized.Value;
        }

        if (normalized.IsCustomEmoji)
        {
            activity["tag"] = new[]
            {
                new
                {
                    id = normalized.CustomEmojiIri,
                    type = "Emoji",
                    name = normalized.CustomEmojiName,
                    icon = new
                    {
                        type = "Image",
                        mediaType = normalized.CustomEmojiMediaType ?? "image/png",
                        url = normalized.CustomEmojiUrl
                    }
                }
            };
        }

        _ = await outbox.SubmitAsync(
            username,
            DeriveReactionIdempotencyKey(idempotencyKey, "create"),
            JsonSerializer.SerializeToUtf8Bytes(activity),
            cancellationToken).ConfigureAwait(false);
        return await queryService.FindPostAsync(postId, actorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found after applying the reaction.");
    }

    public async Task<ClientPostView> UndoReactionAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        (string actorIri, FederatedObject item) = await RequireReadableReactionTargetAsync(
            username,
            postId,
            cancellationToken).ConfigureAwait(false);
        ReactionReference? existing = await FindReactionAsync(
            actorIri,
            item.Iri,
            cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            throw new ClientReactionStateException(
                ClientReactionStateError.NotReacted,
                "The authenticated actor has not reacted to this status.");
        }

        ReactionAudience audience = BuildReactionAudience(item.Visibility, actorIri, item.OwnerIri);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "Undo",
            @object = existing.ActivityIri,
            to = audience.To,
            cc = audience.Cc
        });
        _ = await outbox.SubmitAsync(
            username,
            DeriveReactionIdempotencyKey(idempotencyKey, "delete"),
            body,
            cancellationToken).ConfigureAwait(false);
        return await queryService.FindPostAsync(postId, actorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found after removing the reaction.");
    }

    public async Task<ClientPostView> VotePollAsync(
        string username,
        Guid postId,
        int choiceIndex,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        string actorIri = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        await using (FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            FederatedObject? question = await db.Objects.SingleOrDefaultAsync(
                item => item.Id == postId && !item.IsDeleted,
                cancellationToken).ConfigureAwait(false);
            if (question is null)
            {
                throw new KeyNotFoundException("Poll note was not found.");
            }

            if (question.Type != "Question")
            {
                throw new ClientPollVoteException(ClientPollVoteError.NoPoll, "The note does not attach a poll.");
            }

            if (!await CanReadReactionTargetAsync(db, actorIri, question, cancellationToken).ConfigureAwait(false))
            {
                throw new ClientPollVoteException(ClientPollVoteError.NotVisible, "The poll is not visible to the authenticated actor.");
            }

            bool blocked = await db.UserBlocks.AnyAsync(value =>
                    value.OwnerActorIri == question.OwnerIri && value.TargetActorIri == actorIri &&
                    value.State == FederatedRelationState.Active,
                cancellationToken).ConfigureAwait(false);
            if (blocked)
            {
                throw new ClientPollVoteException(ClientPollVoteError.Blocked, "The poll owner has blocked the authenticated actor.");
            }

            ClientPostView visible = await queryService.FindPostAsync(postId, actorIri, cancellationToken).ConfigureAwait(false)
                ?? throw new ClientPollVoteException(ClientPollVoteError.NotVisible, "The poll is not visible to the authenticated actor.");
            if (visible.Poll is null)
            {
                throw new ClientPollVoteException(ClientPollVoteError.NoPoll, "The note does not attach a poll.");
            }

            if (visible.Poll.Expired)
            {
                throw new ClientPollVoteException(ClientPollVoteError.Expired, "The poll is already expired.");
            }

            if (choiceIndex < 0 || choiceIndex >= visible.Poll.Options.Count)
            {
                throw new ClientPollVoteException(ClientPollVoteError.InvalidChoice, "The poll choice is invalid.");
            }

            if (visible.Poll.OwnVotes.Contains(choiceIndex) || !visible.Poll.Multiple && visible.Poll.OwnVotes.Count > 0)
            {
                throw new ClientPollVoteException(ClientPollVoteError.AlreadyVoted, "The authenticated actor has already voted.");
            }

            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "Note",
                name = visible.Poll.Options[choiceIndex].Title,
                inReplyTo = question.Iri,
                to = question.OwnerIri,
                _activitypubServerChoiceIndex = choiceIndex
            });
            _ = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        }

        return await queryService.FindPostAsync(postId, actorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Poll note was not found after voting.");
    }

    public async Task<ClientPostView> UndoAnnounceAsync(
        string username,
        Guid postId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await UndoObjectActivityAsync(username, postId, idempotencyKey, "Announce", cancellationToken).ConfigureAwait(false);

    public async Task<ClientRelationshipView> FollowAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        (string owner, ClientAccountView target) = await RequireRelationshipActorsAsync(
            username,
            accountId,
            cancellationToken).ConfigureAwait(false);
        await using (FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            FollowState? state = await db.FollowRelations
                .Where(x => x.FollowerIri == owner && x.FollowedIri == target.Iri)
                .Select(x => (FollowState?)x.State)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (state is FollowState.Pending or FollowState.Accepted)
            {
                return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "Follow",
            @object = target.Iri,
            to = target.Iri
        });
        _ = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientRelationshipView> UnfollowAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        (string owner, ClientAccountView target) = await RequireRelationshipActorsAsync(
            username,
            accountId,
            cancellationToken).ConfigureAwait(false);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        FollowRelation? relation = await db.FollowRelations.SingleOrDefaultAsync(
            x => x.FollowerIri == owner && x.FollowedIri == target.Iri,
            cancellationToken).ConfigureAwait(false);
        if (relation is null || relation.State is not (FollowState.Pending or FollowState.Accepted))
        {
            return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "Undo",
            @object = relation.FollowActivityIri,
            to = target.Iri
        });
        _ = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientRelationshipView> MuteAsync(
        string username,
        Guid accountId,
        bool hideNotifications,
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        string owner = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        ClientAccountView target = await queryService.FindAccountByIdAsync(accountId, new Uri(owner).IdnHost, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Account was not found.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = duration is null ? null : now.Add(duration.Value);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        UserMute? existing = await db.UserMutes.SingleOrDefaultAsync(
            x => x.OwnerActorIri == owner && x.TargetActorIri == target.Iri && x.RevokedAt == null,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.ExpiresAt is not null && existing.ExpiresAt <= now)
        {
            existing.Revoke(now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            existing = null;
        }

        if (existing is null)
        {
            db.UserMutes.Add(UserMute.Create(owner, target.Iri, hideNotifications, now, expiresAt));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientRelationshipView> UnmuteAsync(
        string username,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        string owner = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        ClientAccountView target = await queryService.FindAccountByIdAsync(accountId, new Uri(owner).IdnHost, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Account was not found.");
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        UserMute? existing = await db.UserMutes.SingleOrDefaultAsync(
            x => x.OwnerActorIri == owner && x.TargetActorIri == target.Iri && x.RevokedAt == null,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Revoke(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientRelationshipView> BlockAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        (string owner, ClientAccountView target) = await RequireRelationshipActorsAsync(
            username,
            accountId,
            cancellationToken).ConfigureAwait(false);
        await using (FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            bool exists = await db.UserBlocks.AnyAsync(x =>
                x.OwnerActorIri == owner && x.TargetActorIri == target.Iri &&
                x.State == FederatedRelationState.Active,
                cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "Block",
            @object = target.Iri,
            to = target.Iri
        });
        _ = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientRelationshipView> UnblockAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        (string owner, ClientAccountView target) = await RequireRelationshipActorsAsync(
            username,
            accountId,
            cancellationToken).ConfigureAwait(false);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        UserBlock? block = await db.UserBlocks.SingleOrDefaultAsync(x =>
            x.OwnerActorIri == owner && x.TargetActorIri == target.Iri &&
            x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
        if (block is null)
        {
            return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "Undo",
            @object = block.BlockActivityIri,
            to = target.Iri
        });
        _ = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        return await RequireRelationshipAsync(owner, target.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClientPostView> ApplyObjectActivityAsync(
        string username,
        Guid statusId,
        string idempotencyKey,
        string activityType,
        CancellationToken cancellationToken)
    {
        string actorIri = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        FederatedObject item = await db.Objects.SingleOrDefaultAsync(x => x.Id == statusId && !x.IsDeleted, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found.");
        byte[] body;
        if (activityType == "Announce")
        {
            if (item.Visibility is not (Visibility.Public or Visibility.Unlisted))
            {
                throw new InvalidOperationException("Followers-only and mentioned-only objects cannot be announced.");
            }

            if (!await announceChainGuard.IsWithinChainLimitAsync(item.Iri, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The announce chain is too deep; this renote would feed a fork bomb.");
            }

            const string publicAudience = "https://www.w3.org/ns/activitystreams#Public";
            string followers = actorIri.TrimEnd('/') + "/followers";
            string[] to = item.Visibility == Visibility.Public ? [publicAudience] : [followers];
            string[] cc = item.Visibility == Visibility.Public
                ? [followers, item.OwnerIri]
                : [publicAudience, item.OwnerIri];
            body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = activityType,
                @object = item.Iri,
                to,
                cc
            });
        }
        else
        {
            body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = activityType,
                @object = item.Iri,
                to = item.OwnerIri
            });
        }

        _ = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        return await queryService.FindPostAsync(statusId, actorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found.");
    }

    private async Task<ClientPostView> UndoObjectActivityAsync(
        string username,
        Guid statusId,
        string idempotencyKey,
        string activityType,
        CancellationToken cancellationToken)
    {
        string actorIri = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        FederatedObject item = await db.Objects.SingleOrDefaultAsync(x => x.Id == statusId && !x.IsDeleted, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found.");
        string? activityIri = activityType == "Like"
            ? await db.LikeRelations.Where(x =>
                    x.ActorIri == actorIri && x.ObjectIri == item.Iri && x.State == FederatedRelationState.Active)
                .Select(x => x.ActivityIri)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : await db.AnnounceRelations.Where(x =>
                    x.ActorIri == actorIri && x.ObjectIri == item.Iri && x.State == FederatedRelationState.Active)
                .Select(x => x.ActivityIri)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (activityIri is null)
        {
            return await queryService.FindPostAsync(statusId, actorIri, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Status was not found.");
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "Undo",
            @object = activityIri,
            to = item.OwnerIri
        });
        _ = await outbox.SubmitAsync(username, idempotencyKey, body, cancellationToken).ConfigureAwait(false);
        return await queryService.FindPostAsync(statusId, actorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found.");
    }

    private async Task<(string ActorIri, FederatedObject Item)> RequireReadableReactionTargetAsync(
        string username,
        Guid postId,
        CancellationToken cancellationToken)
    {
        string actorIri = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        FederatedObject item = await db.Objects.SingleOrDefaultAsync(
            value => value.Id == postId && !value.IsDeleted,
            cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found.");
        if (item.Type is not ("Note" or "Article" or "Page" or "Question") ||
            !await CanReadReactionTargetAsync(db, actorIri, item, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("Status is not accessible to the authenticated actor.");
        }

        return (actorIri, item);
    }

    private static async Task<bool> CanReadReactionTargetAsync(
        FederationDbContext db,
        string actorIri,
        FederatedObject item,
        CancellationToken cancellationToken)
    {
        if (item.Visibility is Visibility.Public or Visibility.Unlisted ||
            string.Equals(item.OwnerIri, actorIri, StringComparison.Ordinal))
        {
            return true;
        }

        if (item.Visibility == Visibility.FollowersOnly && await db.FollowRelations.AnyAsync(
                value => value.FollowerIri == actorIri && value.FollowedIri == item.OwnerIri &&
                         value.State == FollowState.Accepted,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await (from activity in db.Activities
                      join recipient in db.ActivityRecipients on activity.Id equals recipient.ActivityId
                      where activity.ObjectIri == item.Iri && recipient.RecipientIri == actorIri
                      select recipient.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReactionReference?> FindReactionAsync(
        string actorIri,
        string objectIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        LikeRelation? like = await db.LikeRelations.SingleOrDefaultAsync(
            value => value.ActorIri == actorIri && value.ObjectIri == objectIri &&
                     value.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
        EmojiReactionRelation? emoji = await db.EmojiReactionRelations
            .Where(value => value.ActorIri == actorIri && value.ObjectIri == objectIri &&
                            value.State == FederatedRelationState.Active)
            .OrderByDescending(value => value.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return like is not null
            ? new ReactionReference(like.ActivityIri, like.EffectiveReaction)
            : emoji is null ? null : new ReactionReference(emoji.ActivityIri, emoji.Reaction);
    }

    private static FederatedReaction ResolveReaction(FederatedObject item, string actorIri, string value)
    {
        FederatedReaction normalized = FederatedReaction.Create(value, actorIri);
        if (!normalized.IsCustomEmoji)
        {
            return normalized;
        }

        string token = normalized.Value[1..^1];
        string shortcode = token.Split('@', 2)[0];
        using JsonDocument document = JsonDocument.Parse(item.RawJson);
        if (!document.RootElement.TryGetProperty("tag", out JsonElement tags))
        {
            throw new DomainException("Custom emoji metadata is not available on this status.");
        }

        IEnumerable<JsonElement> values = tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray()
            : [tags];
        foreach (JsonElement tag in values)
        {
            if (tag.ValueKind != JsonValueKind.Object ||
                !tag.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), $":{shortcode}:", StringComparison.Ordinal) ||
                !tag.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.String ||
                !tag.TryGetProperty("icon", out JsonElement icon) || icon.ValueKind != JsonValueKind.Object ||
                !icon.TryGetProperty("url", out JsonElement url) || url.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? mediaType = icon.TryGetProperty("mediaType", out JsonElement type) && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
            return FederatedReaction.Create(
                normalized.Value,
                actorIri,
                id.GetString(),
                name.GetString(),
                url.GetString(),
                mediaType);
        }

        throw new DomainException("Custom emoji metadata is not available on this status.");
    }

    private static string[] ValidatePollMutation(ClientPollMutation poll)
    {
        ArgumentNullException.ThrowIfNull(poll);
        string[] choices = poll.Choices
            .Select(value => value?.Trim() ?? string.Empty)
            .ToArray();
        if (choices.Length is < 2 or > 10 || choices.Any(value => value.Length is 0 or > 100))
        {
            throw new ArgumentException("A poll requires between two and ten non-empty choices of at most 100 characters.", nameof(poll));
        }

        return choices;
    }

    private static ReactionAudience BuildReactionAudience(Visibility visibility, string actorIri, string ownerIri)
    {
        string followers = actorIri.TrimEnd('/') + "/followers";
        return visibility switch
        {
            Visibility.Public => new ReactionAudience(
                ["https://www.w3.org/ns/activitystreams#Public"],
                [ownerIri, followers]),
            Visibility.Unlisted => new ReactionAudience(
                [ownerIri, followers],
                ["https://www.w3.org/ns/activitystreams#Public"]),
            Visibility.FollowersOnly => new ReactionAudience([ownerIri, followers], []),
            _ => new ReactionAudience([ownerIri], [])
        };
    }

    private async Task<bool> UsesLitePubEmojiReactAsync(string ownerIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string? raw = await db.RemoteActors.Where(value => value.Iri == ownerIri && value.GoneAt == null)
            .Select(value => value.RawJson)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return raw is not null &&
            (raw.Contains("litepub.social", StringComparison.OrdinalIgnoreCase) ||
             raw.Contains("EmojiReact", StringComparison.Ordinal));
    }

    private static string DeriveReactionIdempotencyKey(string value, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"misskey-reaction:{operation}:{value}"));
        return "mk-react-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static (string[] To, string[] Cc) ResolveAudience(
        string actorIri,
        Visibility visibility,
        IReadOnlyList<ResolvedMention> mentions)
    {
        const string publicAudience = "https://www.w3.org/ns/activitystreams#Public";
        string followers = actorIri.TrimEnd('/') + "/followers";
        return visibility switch
        {
            Visibility.Public => ([publicAudience], [followers]),
            Visibility.Unlisted => ([followers], [publicAudience]),
            Visibility.FollowersOnly => ([followers], []),
            Visibility.MentionedOnly when mentions.Count > 0 => (mentions.Select(mention => mention.ActorIri).ToArray(), []),
            Visibility.MentionedOnly => throw new ArgumentException("Mentioned-only post requires at least one resolvable mention.", nameof(mentions)),
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), "Unsupported post visibility.")
        };
    }

    private async Task<IReadOnlyList<Dictionary<string, object?>>> ResolveMediaAttachmentsAsync(
        string actorIri,
        IReadOnlyList<Guid> requestedIds,
        CancellationToken cancellationToken)
    {
        Guid[] mediaIds = requestedIds.Distinct().ToArray();
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        MediaResource[] media = await db.Media
            .Where(item => mediaIds.Contains(item.Id) &&
                           item.OwnerActorIri == actorIri &&
                           item.State == MediaState.Available)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (media.Length != mediaIds.Length)
        {
            throw new KeyNotFoundException("One or more media attachments are unavailable or owned by another actor.");
        }

        Dictionary<Guid, MediaResource> byId = media.ToDictionary(item => item.Id);
        return mediaIds.Select(mediaId =>
        {
            MediaResource item = byId[mediaId];
            string mediaIri = iriFactory.MediaIri(mediaId);
            return new Dictionary<string, object?>
            {
                ["type"] = ActivityStreamsMediaType(item.DetectedMediaType),
                ["id"] = mediaIri,
                ["url"] = mediaIri,
                ["mediaType"] = item.DetectedMediaType,
                ["name"] = item.OriginalFileName,
                ["width"] = item.Width,
                ["height"] = item.Height
            };
        }).ToArray();
    }

    private static string ActivityStreamsMediaType(string mediaType)
    {
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return "Image";
        }

        if (mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return "Audio";
        }

        return mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? "Video" : "Document";
    }

    private async Task<IReadOnlyList<ResolvedMention>> ResolveMentionsAsync(
        string status,
        CancellationToken cancellationToken)
    {
        string[] handles = status.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('(', ')', '[', ']', '{', '}', ',', '.', ':', ';', '!', '?'))
            .Where(token => token.StartsWith('@') && token.Length > 1)
            .Select(token => token[1..])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ResolvedMention>();
        foreach (string handle in handles)
        {
            string[] parts = handle.Split('@', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                string normalized = parts[0].ToUpperInvariant();
                string? local = await db.LocalActors.Where(x => x.NormalizedUsername == normalized && !x.IsSuspended)
                    .Select(x => x.Iri)
                    .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (local is not null)
                {
                    result.Add(new(local, "@" + parts[0]));
                }
            }
            else if (parts.Length == 2)
            {
                RemoteActor[] candidates = await db.RemoteActors
                    .Where(x => x.PreferredUsername == parts[0] && x.GoneAt == null)
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false);
                RemoteActor? remote = candidates.FirstOrDefault(x =>
                    string.Equals(new Uri(x.Iri).IdnHost, parts[1], StringComparison.OrdinalIgnoreCase));
                if (remote is not null)
                {
                    result.Add(new(remote.Iri, $"@{parts[0]}@{parts[1]}"));
                }
            }
        }

        return result.DistinctBy(mention => mention.ActorIri, StringComparer.Ordinal).ToArray();
    }

    private sealed record ResolvedMention(string ActorIri, string Name);

    private sealed record ReactionAudience(string[] To, string[] Cc);

    private sealed record ReactionReference(string ActivityIri, string Reaction);

    private async Task<string> RequireLocalActorIriAsync(string username, CancellationToken cancellationToken)
    {
        string normalized = username.ToUpperInvariant();
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.LocalActors.Where(x => x.NormalizedUsername == normalized && !x.IsSuspended)
            .Select(x => x.Iri)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Authenticated account has no local actor.");
    }

    private async Task<FederatedObject> RequireOwnedObjectAsync(
        string actorIri,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Objects.SingleOrDefaultAsync(x => x.Id == objectId && x.OwnerIri == actorIri && !x.IsDeleted, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Status was not found or is not owned by the authenticated actor.");
    }

    private async Task<string> RequireObjectIriAsync(Guid objectId, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Objects.Where(x => x.Id == objectId && !x.IsDeleted).Select(x => x.Iri)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Referenced status was not found.");
    }

    private async Task<Guid> RequireObjectIdByIriAsync(string? objectIri, CancellationToken cancellationToken)
    {
        if (objectIri is null)
        {
            throw new InvalidOperationException("Outbox did not create an object.");
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Objects.Where(x => x.Iri == objectIri).Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Created object was not persisted.");
    }

    private async Task<(string Owner, ClientAccountView Target)> RequireRelationshipActorsAsync(
        string username,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        string owner = await RequireLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        ClientAccountView target = await queryService.FindAccountByIdAsync(
            accountId,
            new Uri(owner).IdnHost,
            cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Account was not found.");
        if (string.Equals(owner, target.Iri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An account cannot follow itself.");
        }

        return (owner, target);
    }

    private async Task<ClientRelationshipView> RequireRelationshipAsync(
        string ownerActorIri,
        Guid accountId,
        CancellationToken cancellationToken) =>
        await queryService.FindRelationshipAsync(
            ownerActorIri,
            accountId,
            new Uri(ownerActorIri).IdnHost,
            cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException("Account was not found.");

    private static string EncodeStatus(string status)
    {
        string normalized = status.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string encoded = HtmlEncoder.Default.Encode(normalized);
        return "<p>" + encoded.Replace("\n", "<br>", StringComparison.Ordinal) + "</p>";
    }

    internal static IReadOnlyList<string> ExtractHashtags(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in HashtagPattern().Matches(source))
        {
            if (match.Groups[1].Value is { Length: > 0 } tag)
            {
                tags.Add(tag.ToLowerInvariant());
            }
        }

        return tags.Count > 0 ? tags.ToArray() : [];
    }

    [GeneratedRegex("#([a-zA-Z0-9_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex HashtagPattern();
}
