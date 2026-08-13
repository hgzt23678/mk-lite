using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Persistence;

internal sealed class InboxRepository(
    IDbContextFactory<FederationDbContext> contextFactory,
    IDomainPolicyService policyService,
    IStreamEventNotifier streamEventNotifier,
    IFederationQueueSignal queueSignal,
    IClientProjectionCache projectionCache,
    ILogger<InboxRepository> logger) : IInboxRepository
{
    private const string PublicAudience = "https://www.w3.org/ns/activitystreams#Public";

    private static readonly Action<ILogger, string, Exception?> ConcurrentInsert =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1_001, nameof(ConcurrentInsert)),
            "Concurrent inbox insert detected for activity {ActivityIdHash}");

    public async Task<InboxAcceptance> AcceptAsync(
        VerifiedInboundActivity activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!string.Equals(PayloadDigest.Sha256Hex(activity.RawBody), activity.PayloadHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Verified inbound payload hash does not match its raw bytes.");
        }

        FederationPolicyKind policy = await policyService.GetEffectivePolicyAsync(
            new Uri(activity.Origin).IdnHost,
            activity.ActorIri,
            cancellationToken).ConfigureAwait(false);
        if (policy == FederationPolicyKind.Reject)
        {
            return new(InboxAcceptanceStatus.RejectedByPolicy, null, "Remote actor or domain is rejected.");
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        InboxItem? existing = await db.InboxItems
            .SingleOrDefaultAsync(x => x.ActivityIri == activity.ActivityIri, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            InboxAcceptance result = await RecordDuplicateOrConflictAsync(db, existing, activity, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        string[] recipients = activity.Audience.Select(x => x.Iri).Distinct(StringComparer.Ordinal).ToArray();
        IQueryable<string> eligibleLocalActors = db.LocalActors
            .Where(x => !x.IsSuspended)
            .Select(x => x.Iri);
        if (activity.RequiredLocalActorIri is not null)
        {
            eligibleLocalActors = eligibleLocalActors.Where(iri => iri == activity.RequiredLocalActorIri);
        }

        IQueryable<string> directlyAddressed = eligibleLocalActors.Where(iri => recipients.Contains(iri));
        string followersIri = activity.ActorIri.TrimEnd('/') + "/followers";
        IQueryable<string> collectionAddressed = recipients.Contains(followersIri)
            ? from localActorIri in eligibleLocalActors
              join follow in db.FollowRelations on localActorIri equals follow.FollowerIri
              where follow.FollowedIri == activity.ActorIri && follow.State == FollowState.Accepted
              select localActorIri
            : db.LocalActors.Where(_ => false).Select(x => x.Iri);
        IQueryable<string> ownedPublicMutationRecipients =
            activity.ActivityType is "Update" or "Delete" &&
            activity.ObjectIri is not null &&
            recipients.Contains(PublicAudience)
                ? from localActorIri in eligibleLocalActors
                  join follow in db.FollowRelations on localActorIri equals follow.FollowerIri
                  join federatedObject in db.Objects on follow.FollowedIri equals federatedObject.OwnerIri
                  where follow.FollowedIri == activity.ActorIri &&
                        follow.State == FollowState.Accepted &&
                        federatedObject.Iri == activity.ObjectIri &&
                        federatedObject.OwnerIri == activity.ActorIri
                  select localActorIri
                : db.LocalActors.Where(_ => false).Select(x => x.Iri);
        IQueryable<string> ownedTargetRecipients =
            recipients.Length == 0 &&
            activity.ActivityType is "Like" or "EmojiReact" or "EmojiReaction" &&
            activity.ObjectIri is not null
                ? from localActorIri in eligibleLocalActors
                  join federatedObject in db.Objects on localActorIri equals federatedObject.OwnerIri
                  where federatedObject.Iri == activity.ObjectIri && !federatedObject.IsDeleted
                  select localActorIri
                : db.LocalActors.Where(_ => false).Select(x => x.Iri);
        IQueryable<string> verifiedUndoRecipients =
            recipients.Length == 0 &&
            activity.ActivityType == "Undo" &&
            activity.ObjectIri is not null
                ? from localActorIri in eligibleLocalActors
                  join recipient in db.ActivityRecipients on localActorIri equals recipient.RecipientIri
                  join targetActivity in db.Activities on recipient.ActivityId equals targetActivity.Id
                  where targetActivity.Iri == activity.ObjectIri &&
                        targetActivity.ActorIri == activity.ActorIri
                  select localActorIri
                : db.LocalActors.Where(_ => false).Select(x => x.Iri);
        string[] authorizedLocalRecipients = await directlyAddressed
            .Concat(collectionAddressed)
            .Concat(ownedPublicMutationRecipients)
            .Concat(ownedTargetRecipients)
            .Concat(verifiedUndoRecipients)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (authorizedLocalRecipients.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(InboxAcceptanceStatus.NoLocalRecipient, null, "Activity has no active local recipient.");
        }

        InboxItem item = InboxItem.Accept(
            activity.ActivityIri,
            activity.ActorIri,
            activity.ActivityType,
            activity.RawBody,
            activity.SignatureProfile,
            activity.KeyIri,
            activity.SignatureCreatedAt,
            activity.ReceivedAt);
        db.InboxItems.Add(item);
        db.InboxItemRecipients.AddRange(authorizedLocalRecipients.Select(actorIri =>
            InboxItemRecipient.Create(item.Id, actorIri)));
        db.SignatureReplays.Add(SignatureReplay.Create(
            activity.ReplayFingerprint,
            activity.NonceHash,
            activity.KeyIri,
            activity.ActivityIri,
            activity.ReceivedAt,
            activity.ReceivedAt.AddHours(24)));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await queueSignal.NotifyInboxAvailableAsync(cancellationToken).ConfigureAwait(false);
            return new(InboxAcceptanceStatus.Accepted, item.Id, null);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            ConcurrentInsert(logger, PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(activity.ActivityIri)), exception);
            return await ResolveConcurrentInsertAsync(activity, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<InboxItem>> ClaimAsync(
        string workerId,
        int count,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (count is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        List<InboxItem> items = await db.InboxItems
            .FromSqlInterpolated($"""
                SELECT * FROM activitypub.inbox_items
                WHERE (state = 'Pending' AND available_at <= {now})
                   OR (state = 'Leased' AND lease_expires_at <= {now})
                ORDER BY available_at, created_at, id
                LIMIT {count}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (InboxItem item in items)
        {
            item.AcquireLease(workerId, now, leaseDuration);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return items;
    }

    public async Task ExtendLeaseAsync(
        Guid itemId,
        string workerId,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset expiresAt = now.Add(leaseDuration);
        int changed = await db.InboxItems
            .Where(x => x.Id == itemId && x.State == WorkItemState.Leased && x.LeaseOwner == workerId && x.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.LeaseExpiresAt, expiresAt)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
        if (changed != 1)
        {
            throw new InvalidOperationException("Inbox lease was lost before heartbeat.");
        }
    }

    public async Task<FederatedObject?> FindObjectAsync(string objectIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Objects.SingleOrDefaultAsync(x => x.Iri == objectIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FollowRelation?> FindFollowByActivityAsync(string followActivityIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.FollowRelations.SingleOrDefaultAsync(x => x.FollowActivityIri == followActivityIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FollowRelation?> FindFollowByPairAsync(
        string followerIri,
        string followedIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.FollowRelations.SingleOrDefaultAsync(
            x => x.FollowerIri == followerIri && x.FollowedIri == followedIri,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionMembership?> FindCollectionMembershipByActivityAsync(
        string activityIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.CollectionMemberships.SingleOrDefaultAsync(
            x => x.AddActivityIri == activityIri || x.RemoveActivityIri == activityIri,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionMembership?> FindActiveCollectionMembershipAsync(
        string collectionIri,
        string objectIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.CollectionMemberships.SingleOrDefaultAsync(
            x => x.CollectionIri == collectionIri && x.ObjectIri == objectIri && x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LikeRelation?> FindLikeByActivityAsync(string activityIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.LikeRelations.SingleOrDefaultAsync(x => x.ActivityIri == activityIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LikeRelation?> FindActiveLikeAsync(
        string actorIri,
        string objectIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.LikeRelations.SingleOrDefaultAsync(
            x => x.ActorIri == actorIri && x.ObjectIri == objectIri && x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AnnounceRelation?> FindAnnounceByActivityAsync(string activityIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.AnnounceRelations.SingleOrDefaultAsync(x => x.ActivityIri == activityIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmojiReactionRelation?> FindEmojiReactionByActivityAsync(
        string activityIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.EmojiReactionRelations.SingleOrDefaultAsync(
            x => x.ActivityIri == activityIri,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmojiReactionRelation?> FindActiveEmojiReactionAsync(
        string actorIri,
        string objectIri,
        string reaction,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.EmojiReactionRelations.SingleOrDefaultAsync(
            x => x.ActorIri == actorIri && x.ObjectIri == objectIri && x.Reaction == reaction &&
                x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AnnounceRelation?> FindActiveAnnounceAsync(
        string actorIri,
        string objectIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.AnnounceRelations.SingleOrDefaultAsync(
            x => x.ActorIri == actorIri && x.ObjectIri == objectIri && x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActorMove?> FindMoveByActivityAsync(string activityIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ActorMoves.SingleOrDefaultAsync(x => x.ActivityIri == activityIri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserBlock?> FindBlockByActivityAsync(string activityIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.UserBlocks.SingleOrDefaultAsync(
            x => x.BlockActivityIri == activityIri,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserBlock?> FindActiveBlockAsync(
        string ownerActorIri,
        string targetActorIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.UserBlocks.SingleOrDefaultAsync(
            x => x.OwnerActorIri == ownerActorIri && x.TargetActorIri == targetActorIri &&
                x.State == FederatedRelationState.Active,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> FindAcceptedRecipientsAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.InboxItemRecipients
            .Where(x => x.InboxItemId == inboxItemId)
            .OrderBy(x => x.ActorIri)
            .Select(x => x.ActorIri)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveProcessedAsync(
        InboxItem item,
        InboxSideEffects effects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(effects);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        db.Attach(item);
        db.Entry(item).State = EntityState.Modified;
        db.Entry(item).Property(x => x.Version).OriginalValue = checked(item.Version - 1);
        db.Activities.Add(effects.Activity);
        if (effects.Recipients is not null)
        {
            db.ActivityRecipients.AddRange(effects.Recipients);
        }

        if (effects.FederatedObject is not null)
        {
            bool objectExists = await db.Objects.AnyAsync(x => x.Id == effects.FederatedObject.Id, cancellationToken).ConfigureAwait(false);
            if (objectExists)
            {
                db.Attach(effects.FederatedObject);
                db.Entry(effects.FederatedObject).State = EntityState.Modified;
                db.Entry(effects.FederatedObject).Property(x => x.Version).OriginalValue = checked(effects.FederatedObject.Version - 1);
            }
            else
            {
                db.Objects.Add(effects.FederatedObject);
            }
        }

        if (effects.ObjectRevision is not null)
        {
            db.ObjectRevisions.Add(effects.ObjectRevision);
        }

        if (effects.FollowRelation is not null)
        {
            bool followExists = await db.FollowRelations.AnyAsync(x => x.Id == effects.FollowRelation.Id, cancellationToken).ConfigureAwait(false);
            if (followExists)
            {
                db.Attach(effects.FollowRelation);
                db.Entry(effects.FollowRelation).State = EntityState.Modified;
            }
            else
            {
                db.FollowRelations.Add(effects.FollowRelation);
            }
        }

        var relationshipMutations = new List<FollowRelation>();
        if (effects.FollowRelation is not null)
        {
            relationshipMutations.Add(effects.FollowRelation);
        }

        if (effects.CollectionMembership is not null)
        {
            bool exists = await db.CollectionMemberships.AnyAsync(
                x => x.Id == effects.CollectionMembership.Id,
                cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                db.Attach(effects.CollectionMembership);
                db.Entry(effects.CollectionMembership).State = EntityState.Modified;
            }
            else
            {
                db.CollectionMemberships.Add(effects.CollectionMembership);
            }
        }

        if (effects.ReplacedLikeRelation is not null)
        {
            db.Attach(effects.ReplacedLikeRelation);
            db.Entry(effects.ReplacedLikeRelation).State = EntityState.Modified;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (effects.LikeRelation is not null)
        {
            bool exists = await db.LikeRelations.AnyAsync(x => x.Id == effects.LikeRelation.Id, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                db.Attach(effects.LikeRelation);
                db.Entry(effects.LikeRelation).State = EntityState.Modified;
            }
            else
            {
                db.LikeRelations.Add(effects.LikeRelation);
            }
        }

        if (effects.AnnounceRelation is not null)
        {
            bool exists = await db.AnnounceRelations.AnyAsync(x => x.Id == effects.AnnounceRelation.Id, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                db.Attach(effects.AnnounceRelation);
                db.Entry(effects.AnnounceRelation).State = EntityState.Modified;
            }
            else
            {
                db.AnnounceRelations.Add(effects.AnnounceRelation);
            }
        }

        if (effects.EmojiReactionRelation is not null)
        {
            bool exists = await db.EmojiReactionRelations.AnyAsync(
                x => x.Id == effects.EmojiReactionRelation.Id,
                cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                db.Attach(effects.EmojiReactionRelation);
                db.Entry(effects.EmojiReactionRelation).State = EntityState.Modified;
            }
            else
            {
                db.EmojiReactionRelations.Add(effects.EmojiReactionRelation);
            }
        }

        if (effects.ActorMove is not null)
        {
            bool exists = await db.ActorMoves.AnyAsync(x => x.Id == effects.ActorMove.Id, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                db.Attach(effects.ActorMove);
                db.Entry(effects.ActorMove).State = EntityState.Modified;
            }
            else
            {
                db.ActorMoves.Add(effects.ActorMove);
            }
        }

        if (effects.UserBlock is not null)
        {
            bool exists = await db.UserBlocks.AnyAsync(x => x.Id == effects.UserBlock.Id, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                db.Attach(effects.UserBlock);
                db.Entry(effects.UserBlock).State = EntityState.Modified;
            }
            else
            {
                db.UserBlocks.Add(effects.UserBlock);
            }

            relationshipMutations.AddRange(await CancelBlockedFollowsAsync(
                db,
                effects.UserBlock,
                cancellationToken).ConfigureAwait(false));
        }

        if (effects.Report is not null)
        {
            db.Reports.Add(effects.Report);
        }

        if (effects.ActorPolicy is not null)
        {
            db.ActorPolicies.Add(effects.ActorPolicy);
        }

        if (effects.DeadLetter is not null)
        {
            db.DeadLetters.Add(effects.DeadLetter);
        }

        StreamEvent? streamEvent = StreamEvent.FromObjectMutation(
            effects.Activity,
            effects.FederatedObject,
            isLocal: false);
        streamEvent ??= await BuildReactionStreamEventAsync(
            db,
            effects.Activity,
            effects.LikeRelation ?? effects.ReplacedLikeRelation,
            effects.EmojiReactionRelation,
            isLocal: false,
            cancellationToken).ConfigureAwait(false);
        if (streamEvent is not null)
        {
            db.StreamEvents.Add(streamEvent);
        }

        IReadOnlyList<StreamEvent> relationshipEvents = await RelationshipStreamEventFactory.CreateAsync(
            db,
            effects.Activity,
            relationshipMutations,
            isLocal: false,
            cancellationToken).ConfigureAwait(false);
        if (relationshipEvents.Count > 0)
        {
            db.StreamEvents.AddRange(relationshipEvents);
        }

        UserNotification[] notifications = await BuildNotificationsAsync(
            db,
            item.Id,
            effects,
            cancellationToken).ConfigureAwait(false);
        if (notifications.Length > 0)
        {
            db.UserNotifications.AddRange(notifications);
            db.StreamEvents.AddRange(notifications.Select(notification =>
                StreamEvent.FromNotification(notification, Visibility.MentionedOnly, isLocal: false)));
        }

        if (effects.OutboundResponse is not null)
        {
            db.Activities.Add(effects.OutboundResponse.Activity);
            db.ActivityRecipients.AddRange(effects.OutboundResponse.Recipients);
            db.Deliveries.AddRange(effects.OutboundResponse.Deliveries);
            if (effects.OutboundResponse.DeliveryTargets is not null)
            {
                db.DeliveryTargets.AddRange(effects.OutboundResponse.DeliveryTargets);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        long[] committedCursors = db.StreamEvents.Local
            .Select(item => item.Cursor)
            .Where(cursor => cursor > 0)
            .Distinct()
            .ToArray();
        if (committedCursors.Length > 0)
        {
            await streamEventNotifier.PublishAsync(committedCursors, cancellationToken).ConfigureAwait(false);
        }

        foreach (string recipientActorIri in notifications
                     .Select(notification => notification.RecipientActorIri)
                     .Distinct(StringComparer.Ordinal))
        {
            await projectionCache.InvalidateNotificationsAsync(recipientActorIri, cancellationToken)
                .ConfigureAwait(false);
        }

        if (effects.OutboundResponse?.Deliveries.Count > 0)
        {
            await queueSignal.NotifyDeliveryAvailableAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<FollowRelation>> CancelBlockedFollowsAsync(
        FederationDbContext db,
        UserBlock block,
        CancellationToken cancellationToken)
    {
        if (block.State != FederatedRelationState.Active)
        {
            return [];
        }

        FollowRelation[] relations = await db.FollowRelations.AsTracking().Where(x =>
                (x.FollowerIri == block.OwnerActorIri && x.FollowedIri == block.TargetActorIri ||
                 x.FollowerIri == block.TargetActorIri && x.FollowedIri == block.OwnerActorIri) &&
                (x.State == FollowState.Pending || x.State == FollowState.Accepted))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        foreach (FollowRelation relation in relations)
        {
            relation.CancelBecauseBlocked(block.OwnerActorIri, block.UpdatedAt);
        }

        return relations;
    }

    private static async Task<UserNotification[]> BuildNotificationsAsync(
        FederationDbContext db,
        Guid inboxItemId,
        InboxSideEffects effects,
        CancellationToken cancellationToken)
    {
        UserNotificationKind? kind = NotificationKind(effects);
        if (kind is null)
        {
            return [];
        }

        string[] recipients = await db.InboxItemRecipients.Where(x => x.InboxItemId == inboxItemId)
            .Select(x => x.ActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (kind == UserNotificationKind.Mention)
        {
            HashSet<string> directlyAddressed = (effects.Recipients ?? [])
                .Where(x => x.Field != AudienceField.Audience)
                .Select(x => x.RecipientIri)
                .ToHashSet(StringComparer.Ordinal);
            recipients = recipients.Where(directlyAddressed.Contains).ToArray();
        }

        recipients = recipients.Where(recipient =>
                !string.Equals(recipient, effects.Activity.ActorIri, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (recipients.Length == 0)
        {
            return [];
        }

        string[] notificationMuted = await db.UserMutes.Where(x =>
                recipients.Contains(x.OwnerActorIri) && x.TargetActorIri == effects.Activity.ActorIri &&
                x.HideNotifications && x.RevokedAt == null &&
                (x.ExpiresAt == null || x.ExpiresAt > effects.Activity.ReceivedAt))
            .Select(x => x.OwnerActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string[] blockedRecipients = await db.UserBlocks.Where(x =>
                recipients.Contains(x.OwnerActorIri) && x.TargetActorIri == effects.Activity.ActorIri ||
                x.OwnerActorIri == effects.Activity.ActorIri && recipients.Contains(x.TargetActorIri))
            .Where(x => x.State == FederatedRelationState.Active)
            .Select(x => x.OwnerActorIri == effects.Activity.ActorIri ? x.TargetActorIri : x.OwnerActorIri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        string? reaction = effects.LikeRelation?.EffectiveReaction ?? effects.EmojiReactionRelation?.Reaction;
        return recipients.Except(notificationMuted, StringComparer.Ordinal)
            .Except(blockedRecipients, StringComparer.Ordinal)
            .Select(recipient => UserNotification.Create(
                recipient,
                effects.Activity.ActorIri,
                kind.Value,
                effects.Activity.Iri,
                effects.Activity.ObjectIri,
                reaction,
                effects.Activity.ReceivedAt))
            .ToArray();
    }

    private static UserNotificationKind? NotificationKind(InboxSideEffects effects)
    {
        if (effects.Activity.Type == "Follow")
        {
            return UserNotificationKind.Follow;
        }

        if (effects.Activity.Type == "Announce")
        {
            return UserNotificationKind.Reblog;
        }

        if (effects.Activity.Type is "EmojiReact" or "EmojiReaction")
        {
            return UserNotificationKind.Reaction;
        }

        if (effects.Activity.Type == "Like")
        {
            using JsonDocument document = JsonDocument.Parse(effects.Activity.RawJson);
            return document.RootElement.TryGetProperty("_misskey_reaction", out _) ||
                document.RootElement.TryGetProperty("content", out _)
                ? UserNotificationKind.Reaction
                : UserNotificationKind.Favourite;
        }

        return effects.Activity.Type == "Create" ? UserNotificationKind.Mention : null;
    }

    private static async Task<StreamEvent?> BuildReactionStreamEventAsync(
        FederationDbContext db,
        ActivityRecord activity,
        LikeRelation? like,
        EmojiReactionRelation? emoji,
        bool isLocal,
        CancellationToken cancellationToken)
    {
        string? objectIri = like?.ObjectIri ?? emoji?.ObjectIri;
        string? reaction = like?.EffectiveReaction ?? emoji?.Reaction;
        if (objectIri is null || reaction is null)
        {
            return null;
        }

        FederatedObject? item = await db.Objects.SingleOrDefaultAsync(
            x => x.Iri == objectIri && !x.IsDeleted,
            cancellationToken).ConfigureAwait(false);
        return item is null
            ? null
            : StreamEvent.FromReactionMutation(
                activity,
                item,
                reaction,
                activity.Type == "Undo",
                isLocal);
    }

    public async Task SaveFailureAsync(
        InboxItem item,
        DeadLetter? deadLetter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        db.Attach(item);
        db.Entry(item).State = EntityState.Modified;
        db.Entry(item).Property(x => x.Version).OriginalValue = checked(item.Version - 1);
        if (deadLetter is not null)
        {
            db.DeadLetters.Add(deadLetter);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<InboxAcceptance> RecordDuplicateOrConflictAsync(
        FederationDbContext db,
        InboxItem existing,
        VerifiedInboundActivity incoming,
        CancellationToken cancellationToken)
    {
        if (string.Equals(existing.PayloadHash, incoming.PayloadHash, StringComparison.Ordinal))
        {
            return new(InboxAcceptanceStatus.Duplicate, existing.Id, null);
        }

        bool alreadyRecorded = await db.InboxConflicts.AnyAsync(
            x => x.ActivityIri == incoming.ActivityIri && x.IncomingPayloadHash == incoming.PayloadHash,
            cancellationToken).ConfigureAwait(false);
        if (!alreadyRecorded)
        {
            db.InboxConflicts.Add(InboxConflict.Create(
                incoming.ActivityIri,
                existing.PayloadHash,
                incoming.PayloadHash,
                incoming.RawBody,
                incoming.ReceivedAt));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new(InboxAcceptanceStatus.ConflictQuarantined, existing.Id, "Same Activity ID was received with different bytes.");
    }

    private async Task<InboxAcceptance> ResolveConcurrentInsertAsync(
        VerifiedInboundActivity activity,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        InboxItem? existing = await db.InboxItems.SingleOrDefaultAsync(x => x.ActivityIri == activity.ActivityIri, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(InboxAcceptanceStatus.RejectedByPolicy, null, "HTTP signature or nonce was replayed.");
        }

        InboxAcceptance result = await RecordDuplicateOrConflictAsync(db, existing, activity, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}
