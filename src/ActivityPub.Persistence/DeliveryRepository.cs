using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class DeliveryRepository(
    IDbContextFactory<FederationDbContext> contextFactory,
    IStreamEventNotifier streamEventNotifier) : IDeliveryRepository
{
    public async Task<OutboundCommitResult> CommitOutboundAsync(OutboundCommit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        if (commit.ClientIdempotency is not null)
        {
            string lockKey = commit.ClientIdempotency.Subject + ":" + commit.ClientIdempotency.Key;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken).ConfigureAwait(false);
            ClientIdempotencyRecord? existing = await db.ClientIdempotency.SingleOrDefaultAsync(
                x => x.Subject == commit.ClientIdempotency.Subject && x.Key == commit.ClientIdempotency.Key,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null && existing.ExpiresAt > commit.ClientIdempotency.CreatedAt)
            {
                if (!string.Equals(existing.RequestHash, commit.ClientIdempotency.RequestHash, StringComparison.Ordinal))
                {
                    throw new DomainException("Idempotency key was already used with a different request body.");
                }

                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new(true, new ClientOutboxResult(existing.ActivityIri, existing.ObjectIri, existing.ResponseBody));
            }

            if (existing is not null)
            {
                db.ClientIdempotency.Remove(existing);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (commit.FederatedObject is not null)
        {
            bool objectExists = await db.Objects.AnyAsync(x => x.Id == commit.FederatedObject.Id, cancellationToken).ConfigureAwait(false);
            if (objectExists)
            {
                db.Attach(commit.FederatedObject);
                db.Entry(commit.FederatedObject).State = EntityState.Modified;
                db.Entry(commit.FederatedObject).Property(x => x.Version).OriginalValue = checked(commit.FederatedObject.Version - 1);
            }
            else
            {
                db.Objects.Add(commit.FederatedObject);
            }
        }

        if (commit.ObjectRevision is not null)
        {
            db.ObjectRevisions.Add(commit.ObjectRevision);
        }

        if (commit.FollowRelation is not null)
        {
            bool followExists = await db.FollowRelations.AnyAsync(x => x.Id == commit.FollowRelation.Id, cancellationToken).ConfigureAwait(false);
            if (followExists)
            {
                db.Attach(commit.FollowRelation);
                db.Entry(commit.FollowRelation).State = EntityState.Modified;
            }
            else
            {
                db.FollowRelations.Add(commit.FollowRelation);
            }
        }

        var relationshipMutations = new List<FollowRelation>();
        if (commit.FollowRelation is not null)
        {
            relationshipMutations.Add(commit.FollowRelation);
        }

        await PersistAggregateAsync(db, commit.CollectionMembership, db.CollectionMemberships, cancellationToken).ConfigureAwait(false);
        await PersistReplacedLikeAsync(db, commit.ReplacedLikeRelation, cancellationToken).ConfigureAwait(false);
        await PersistAggregateAsync(db, commit.LikeRelation, db.LikeRelations, cancellationToken).ConfigureAwait(false);
        await PersistAggregateAsync(db, commit.EmojiReactionRelation, db.EmojiReactionRelations, cancellationToken).ConfigureAwait(false);
        await PersistAggregateAsync(db, commit.AnnounceRelation, db.AnnounceRelations, cancellationToken).ConfigureAwait(false);
        await PersistAggregateAsync(db, commit.ActorMove, db.ActorMoves, cancellationToken).ConfigureAwait(false);
        await PersistAggregateAsync(db, commit.UserBlock, db.UserBlocks, cancellationToken).ConfigureAwait(false);
        await PersistQuestionPollAsync(
            db,
            commit.QuestionPoll,
            commit.PollOptions,
            cancellationToken).ConfigureAwait(false);
        PollVote? persistedPollVote = await PersistPollVoteAsync(
            db,
            commit.PollVote,
            commit.Activity,
            cancellationToken).ConfigureAwait(false);
        if (commit.UserBlock is { State: FederatedRelationState.Active } block)
        {
            FollowRelation[] blockedRelations = await db.FollowRelations.AsTracking().Where(x =>
                    (x.FollowerIri == block.OwnerActorIri && x.FollowedIri == block.TargetActorIri ||
                     x.FollowerIri == block.TargetActorIri && x.FollowedIri == block.OwnerActorIri) &&
                    (x.State == FollowState.Pending || x.State == FollowState.Accepted))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            foreach (FollowRelation relation in blockedRelations)
            {
                relation.CancelBecauseBlocked(block.OwnerActorIri, block.UpdatedAt);
                relationshipMutations.Add(relation);
            }
        }

        if (commit.MediaAttachments is not null)
        {
            if (commit.FederatedObject is null)
            {
                throw new InvalidOperationException("Media attachments require an object mutation in the same transaction.");
            }

            Guid[] mediaIds = commit.MediaAttachments.Select(x => x.MediaId).Distinct().ToArray();
            MediaResource[] authorizedMedia = await db.Media.AsTracking()
                .Where(x => mediaIds.Contains(x.Id) &&
                            x.OwnerActorIri == commit.Activity.ActorIri &&
                            x.State == MediaState.Available)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (authorizedMedia.Length != mediaIds.Length)
            {
                throw new InvalidOperationException("Object references unavailable media or media owned by another actor.");
            }

            foreach (MediaResource media in authorizedMedia)
            {
                media.SetVisibility(commit.FederatedObject.Visibility, commit.Activity.ReceivedAt);
            }

            await db.MediaAttachments
                .Where(x => x.ObjectId == commit.FederatedObject.Id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            db.MediaAttachments.AddRange(commit.MediaAttachments);
        }

        db.Activities.Add(commit.Activity);
        StreamEvent? streamEvent = StreamEvent.FromObjectMutation(
            commit.Activity,
            commit.FederatedObject,
            isLocal: true);
        streamEvent ??= await BuildReactionStreamEventAsync(
            db,
            commit.Activity,
            commit.LikeRelation ?? commit.ReplacedLikeRelation,
            commit.EmojiReactionRelation,
            isLocal: true,
            cancellationToken).ConfigureAwait(false);
        if (streamEvent is null && persistedPollVote is not null)
        {
            FederatedObject? question = await db.Objects.SingleOrDefaultAsync(
                value => value.Id == persistedPollVote.PollId && !value.IsDeleted,
                cancellationToken).ConfigureAwait(false);
            if (question is not null)
            {
                streamEvent = StreamEvent.FromPollVote(
                    commit.Activity,
                    question,
                    persistedPollVote.ChoiceIndex,
                    isLocal: true);
            }
        }
        if (streamEvent is not null)
        {
            db.StreamEvents.Add(streamEvent);
        }

        IReadOnlyList<StreamEvent> relationshipEvents = await RelationshipStreamEventFactory.CreateAsync(
            db,
            commit.Activity,
            relationshipMutations,
            isLocal: true,
            cancellationToken).ConfigureAwait(false);
        if (relationshipEvents.Count > 0)
        {
            db.StreamEvents.AddRange(relationshipEvents);
        }

        UserNotification[] notifications = await BuildLocalNotificationsAsync(
            db,
            commit,
            cancellationToken).ConfigureAwait(false);
        if (notifications.Length > 0)
        {
            db.UserNotifications.AddRange(notifications);
            db.StreamEvents.AddRange(notifications.Select(notification =>
                StreamEvent.FromNotification(notification, Visibility.MentionedOnly, isLocal: true)));
        }

        if (commit.ClientIdempotency is not null)
        {
            db.ClientIdempotency.Add(commit.ClientIdempotency);
        }

        db.ActivityRecipients.AddRange(commit.Recipients);
        db.Deliveries.AddRange(commit.Deliveries);
        if (commit.DeliveryTargets is not null)
        {
            db.DeliveryTargets.AddRange(commit.DeliveryTargets);
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

        return new(false, null);
    }

    private static async Task PersistQuestionPollAsync(
        FederationDbContext db,
        QuestionPoll? candidate,
        IReadOnlyList<PollOption>? candidateOptions,
        CancellationToken cancellationToken)
    {
        if (candidate is null)
        {
            if (candidateOptions is not null)
            {
                throw new InvalidOperationException("Poll options require a Question poll aggregate.");
            }

            return;
        }

        QuestionPoll? existing = await db.QuestionPolls.SingleOrDefaultAsync(
            value => value.QuestionObjectId == candidate.QuestionObjectId,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            PollOption[] existingOptions = await db.PollOptions
                .Where(value => value.PollId == existing.Id)
                .OrderBy(value => value.ChoiceIndex)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            PollOption[] incoming = candidateOptions?.OrderBy(value => value.ChoiceIndex).ToArray() ?? [];
            if (existing.Multiple != candidate.Multiple || existingOptions.Length != incoming.Length ||
                existingOptions.Where((value, index) =>
                        value.ChoiceIndex != incoming[index].ChoiceIndex ||
                        !string.Equals(value.Title, incoming[index].Title, StringComparison.Ordinal))
                    .Any())
            {
                throw new InvalidOperationException("Stored poll metadata conflicts with the submitted Question snapshot.");
            }

            return;
        }

        PollOption[] options = candidateOptions?.OrderBy(value => value.ChoiceIndex).ToArray() ?? [];
        if (options.Length is < 2 or > 10 || options.Any(value => value.PollId != candidate.Id))
        {
            throw new InvalidOperationException("A Question poll requires a valid option snapshot.");
        }

        db.QuestionPolls.Add(candidate);
        db.PollOptions.AddRange(options);
    }

    private static async Task<PollVote?> PersistPollVoteAsync(
        FederationDbContext db,
        PollVote? submission,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        if (submission is null)
        {
            return null;
        }

        string lockKey = $"poll:{submission.PollId:N}:voter:{submission.VoterActorIri}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken).ConfigureAwait(false);
        QuestionPoll poll = db.ChangeTracker.Entries<QuestionPoll>()
            .Select(entry => entry.Entity)
            .SingleOrDefault(value => value.Id == submission.PollId)
            ?? await db.QuestionPolls.SingleOrDefaultAsync(
                value => value.Id == submission.PollId,
                cancellationToken).ConfigureAwait(false)
            ?? throw new ClientPollVoteException(ClientPollVoteError.NoPoll, "The note does not attach a poll.");
        int[] choices = db.ChangeTracker.Entries<PollOption>()
            .Select(entry => entry.Entity)
            .Where(value => value.PollId == poll.Id)
            .Select(value => value.ChoiceIndex)
            .ToArray();
        if (choices.Length == 0)
        {
            choices = await db.PollOptions
                .Where(value => value.PollId == poll.Id)
                .Select(value => value.ChoiceIndex)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        if (poll.IsExpired(activity.ReceivedAt))
        {
            throw new ClientPollVoteException(ClientPollVoteError.Expired, "The poll is already expired.");
        }

        if (!choices.Contains(submission.ChoiceIndex))
        {
            throw new ClientPollVoteException(ClientPollVoteError.InvalidChoice, "The poll choice is invalid.");
        }

        int ballotKey = poll.Multiple ? submission.ChoiceIndex : -1;
        if (await db.PollVotes.AnyAsync(value =>
                value.PollId == poll.Id && value.VoterActorIri == submission.VoterActorIri &&
                value.BallotKey == ballotKey,
            cancellationToken).ConfigureAwait(false))
        {
            throw new ClientPollVoteException(ClientPollVoteError.AlreadyVoted, "The authenticated actor has already voted.");
        }

        PollVote vote = poll.CastVote(
            submission.VoterActorIri,
            submission.ChoiceIndex,
            choices.ToHashSet(),
            activity.Iri,
            activity.ReceivedAt);
        db.PollVotes.Add(vote);
        return vote;
    }

    private static async Task<UserNotification[]> BuildLocalNotificationsAsync(
        FederationDbContext db,
        OutboundCommit commit,
        CancellationToken cancellationToken)
    {
        var candidates = new List<(string Recipient, UserNotificationKind Kind, string? ObjectIri, string? Reaction)>();
        if (commit.FollowRelation is not null && commit.Activity.Type == "Follow")
        {
            candidates.Add((commit.FollowRelation.FollowedIri, UserNotificationKind.Follow, null, null));
        }
        else if (commit.LikeRelation is not null && commit.Activity.Type == "Like")
        {
            string? owner = await db.Objects.Where(x => x.Iri == commit.LikeRelation.ObjectIri && !x.IsDeleted)
                .Select(x => x.OwnerIri)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (owner is not null)
            {
                using JsonDocument activityDocument = JsonDocument.Parse(commit.Activity.RawJson);
                bool explicitReaction = activityDocument.RootElement.TryGetProperty("_misskey_reaction", out _) ||
                    activityDocument.RootElement.TryGetProperty("content", out _);
                candidates.Add((
                    owner,
                    explicitReaction ? UserNotificationKind.Reaction : UserNotificationKind.Favourite,
                    commit.LikeRelation.ObjectIri,
                    commit.LikeRelation.EffectiveReaction));
            }
        }
        else if (commit.AnnounceRelation is not null && commit.Activity.Type == "Announce")
        {
            string? owner = await db.Objects.Where(x => x.Iri == commit.AnnounceRelation.ObjectIri && !x.IsDeleted)
                .Select(x => x.OwnerIri)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (owner is not null)
            {
                candidates.Add((owner, UserNotificationKind.Reblog, commit.AnnounceRelation.ObjectIri, null));
            }
        }
        else if (commit.Activity.Type == "Create" && commit.FederatedObject is not null)
        {
            string[] mentionedActors = commit.Recipients
                .Where(x => x.Field is AudienceField.To or AudienceField.Cc or AudienceField.Bto or AudienceField.Bcc)
                .Select(x => x.RecipientIri)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            candidates.AddRange(mentionedActors.Select(recipient =>
                (recipient, UserNotificationKind.Mention, (string?)commit.FederatedObject.Iri, (string?)null)));
        }

        string[] localRecipients = await db.LocalActors.Where(x => !x.IsSuspended)
            .Select(x => x.Iri)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> local = localRecipients.ToHashSet(StringComparer.Ordinal);
        var result = new List<UserNotification>();
        foreach ((string recipient, UserNotificationKind kind, string? objectIri, string? reaction) in candidates
                     .DistinctBy(x => (x.Recipient, x.Kind)))
        {
            if (!local.Contains(recipient) || string.Equals(recipient, commit.Activity.ActorIri, StringComparison.Ordinal))
            {
                continue;
            }

            bool suppressed = await db.UserMutes.AnyAsync(x =>
                x.OwnerActorIri == recipient && x.TargetActorIri == commit.Activity.ActorIri && x.HideNotifications &&
                x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > commit.Activity.ReceivedAt),
                cancellationToken).ConfigureAwait(false);
            suppressed = suppressed || await db.ActorPolicies.AnyAsync(x =>
                x.ActorIri == commit.Activity.ActorIri &&
                (x.Kind == ModerationActionKind.BlockActor || x.Kind == ModerationActionKind.MuteActor) &&
                x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > commit.Activity.ReceivedAt),
                cancellationToken).ConfigureAwait(false);
            suppressed = suppressed || await db.UserBlocks.AnyAsync(x =>
                (x.OwnerActorIri == recipient && x.TargetActorIri == commit.Activity.ActorIri ||
                 x.OwnerActorIri == commit.Activity.ActorIri && x.TargetActorIri == recipient) &&
                x.State == FederatedRelationState.Active,
                cancellationToken).ConfigureAwait(false);
            if (!suppressed)
            {
                result.Add(UserNotification.Create(
                    recipient,
                    commit.Activity.ActorIri,
                    kind,
                    commit.Activity.Iri,
                    objectIri,
                    reaction,
                    commit.Activity.OccurredAt));
            }
        }

        return result.ToArray();
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

    private static async Task PersistReplacedLikeAsync(
        FederationDbContext db,
        LikeRelation? relation,
        CancellationToken cancellationToken)
    {
        if (relation is null)
        {
            return;
        }

        db.Attach(relation);
        db.Entry(relation).State = EntityState.Modified;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitRelayDeliveriesAsync(
        IReadOnlyList<Delivery> deliveries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        if (deliveries.Count == 0)
        {
            return;
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        db.Deliveries.AddRange(deliveries);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientIdempotencyRecord?> FindClientIdempotencyAsync(
        string subject,
        string key,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ClientIdempotency.SingleOrDefaultAsync(
            x => x.Subject == subject && x.Key == key && x.ExpiresAt > now,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Delivery>> ClaimAsync(
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
        List<Delivery> deliveries = await db.Deliveries
            .FromSqlInterpolated($"""
                SELECT d.* FROM activitypub.deliveries d
                WHERE ((d.state = 'Pending' AND d.available_at <= {now})
                    OR (d.state = 'Leased' AND d.lease_expires_at <= {now}))
                  AND NOT EXISTS (
                      SELECT 1 FROM activitypub.operational_controls c
                      WHERE c.name = 'outbound-delivery-pause' AND c.enabled)
                  AND COALESCE((
                      SELECT p.kind FROM activitypub.domain_policies p
                      WHERE (p.domain = d.remote_domain OR d.remote_domain LIKE '%.' || p.domain)
                        AND p.revoked_at IS NULL
                        AND (p.expires_at IS NULL OR p.expires_at > {now})
                      ORDER BY length(p.domain) DESC, p.created_at DESC
                      LIMIT 1), 'Allow') NOT IN ('PauseOutbound', 'Reject')
                ORDER BY d.available_at, d.created_at, d.id
                LIMIT {count}
                FOR UPDATE OF d SKIP LOCKED
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Delivery delivery in deliveries)
        {
            delivery.AcquireLease(workerId, now, leaseDuration);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deliveries;
    }

    public async Task ExtendLeaseAsync(
        Guid deliveryId,
        string workerId,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset expiresAt = now.Add(leaseDuration);
        int changed = await db.Deliveries
            .Where(x => x.Id == deliveryId && x.State == WorkItemState.Leased && x.LeaseOwner == workerId && x.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.LeaseExpiresAt, expiresAt)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
        if (changed != 1)
        {
            throw new InvalidOperationException("Delivery lease was lost before heartbeat.");
        }
    }

    public async Task ReleaseWithoutAttemptAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Attach(delivery);
        db.Entry(delivery).State = EntityState.Modified;
        db.Entry(delivery).Property(x => x.Version).OriginalValue = checked(delivery.Version - 1);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAttemptAsync(
        Delivery delivery,
        DeliveryAttempt attempt,
        DeadLetter? deadLetter,
        CancellationToken cancellationToken,
        EndpointRediscoveryPlan? endpointRediscovery = null)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(attempt);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        List<DeliveryTarget>? replacementTargets = null;
        List<Delivery>? additionalDeliveries = null;
        List<DeliveryTarget>? additionalTargets = null;
        List<DeliveryEndpointChange>? endpointChanges = null;
        if (endpointRediscovery is not null)
        {
            Guid[] collidingDeliveryIds = await db.Deliveries
                .Where(x => x.Id != delivery.Id && x.ActivityId == delivery.ActivityId &&
                    x.EndpointIri == delivery.EndpointIri &&
                    (x.State == WorkItemState.Pending || x.State == WorkItemState.Leased))
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            string[] collidingActors = collidingDeliveryIds.Length == 0
                ? []
                : await db.DeliveryTargets
                    .Where(x => collidingDeliveryIds.Contains(x.DeliveryId))
                    .Select(x => x.ActorIri)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (collidingDeliveryIds.Length > 0)
            {
                await db.Deliveries
                    .Where(x => collidingDeliveryIds.Contains(x.Id) &&
                        (x.State == WorkItemState.Pending || x.State == WorkItemState.Leased))
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.State, WorkItemState.Cancelled)
                        .SetProperty(x => x.LeaseOwner, (string?)null)
                        .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                        .SetProperty(x => x.LastErrorCode, "endpoint-rediscovery-merged")
                        .SetProperty(x => x.LastError, "Delivery targets were merged into the failing delivery after endpoint rediscovery.")
                        .SetProperty(x => x.CompletedAt, attempt.CompletedAt)
                        .SetProperty(x => x.UpdatedAt, attempt.CompletedAt)
                        .SetProperty(x => x.Version, x => x.Version + 1),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            replacementTargets = endpointRediscovery.ReplacementTargets
                .Select(x => x.ActorIri)
                .Concat(collidingActors)
                .Distinct(StringComparer.Ordinal)
                .Select(actorIri => DeliveryTarget.Create(delivery.Id, actorIri))
                .ToList();
            additionalDeliveries = [];
            additionalTargets = [];
            endpointChanges = endpointRediscovery.Changes
                .Where(x => x.DeliveryId == delivery.Id)
                .ToList();
            foreach (Delivery fork in endpointRediscovery.AdditionalDeliveries)
            {
                Delivery? existing = await db.Deliveries.FirstOrDefaultAsync(x =>
                    x.Id != delivery.Id && x.ActivityId == fork.ActivityId && x.EndpointIri == fork.EndpointIri &&
                    (x.State == WorkItemState.Pending || x.State == WorkItemState.Leased),
                    cancellationToken).ConfigureAwait(false);
                DeliveryTarget[] forkTargets = endpointRediscovery.AdditionalTargets
                    .Where(x => x.DeliveryId == fork.Id)
                    .ToArray();
                DeliveryEndpointChange? forkChange = endpointRediscovery.Changes.FirstOrDefault(x => x.DeliveryId == fork.Id);
                if (existing is null)
                {
                    additionalDeliveries.Add(fork);
                    additionalTargets.AddRange(forkTargets);
                    if (forkChange is not null)
                    {
                        endpointChanges.Add(forkChange);
                    }

                    continue;
                }

                string[] existingActors = await db.DeliveryTargets
                    .Where(x => x.DeliveryId == existing.Id)
                    .Select(x => x.ActorIri)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                additionalTargets.AddRange(forkTargets
                    .Where(x => !existingActors.Contains(x.ActorIri, StringComparer.Ordinal))
                    .Select(x => DeliveryTarget.Create(existing.Id, x.ActorIri)));
                if (forkChange is not null)
                {
                    endpointChanges.Add(DeliveryEndpointChange.Create(
                        existing.Id,
                        forkChange.PreviousEndpointIri,
                        forkChange.ReplacementEndpointIri,
                        forkChange.RecipientCount,
                        forkChange.DiscoveredAt));
                }
            }
        }

        db.Attach(delivery);
        db.Entry(delivery).State = EntityState.Modified;
        db.Entry(delivery).Property(x => x.Version).OriginalValue = checked(delivery.Version - 1);
        db.DeliveryAttempts.Add(attempt);
        if (endpointRediscovery is not null)
        {
            await db.DeliveryTargets
                .Where(x => x.DeliveryId == delivery.Id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            db.DeliveryTargets.AddRange(replacementTargets!);
            db.Deliveries.AddRange(additionalDeliveries!);
            db.DeliveryTargets.AddRange(additionalTargets!);
            db.DeliveryEndpointChanges.AddRange(endpointChanges!);
        }
        if (deadLetter is not null)
        {
            db.DeadLetters.Add(deadLetter);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> FindRecipientActorsAsync(
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string[] persistedTargets = await db.DeliveryTargets
            .Where(x => x.DeliveryId == deliveryId)
            .OrderBy(x => x.ActorIri)
            .Select(x => x.ActorIri)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (persistedTargets.Length > 0)
        {
            return persistedTargets;
        }

        // Expand/migrate compatibility: deliveries created by the previous release have no
        // delivery_targets rows. Reconstruct only recipients whose cached endpoint exactly
        // matches this delivery; the first successful rediscovery persists the snapshot.
        var delivery = await db.Deliveries
            .Where(x => x.Id == deliveryId)
            .Select(x => new { x.ActivityId, x.EndpointIri })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null)
        {
            return [];
        }

        return await (
            from recipient in db.ActivityRecipients
            join endpoint in db.RemoteEndpoints on recipient.RecipientIri equals endpoint.ActorIri
            where recipient.ActivityId == delivery.ActivityId &&
                endpoint.EndpointIri == delivery.EndpointIri &&
                (endpoint.Kind == EndpointKind.Inbox || endpoint.Kind == EndpointKind.SharedInbox)
            select recipient.RecipientIri)
            .Distinct()
            .OrderBy(x => x)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RequeueDeadLetterAsync(
        Guid deadLetterId,
        string operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        DeadLetter? deadLetter = await db.DeadLetters.SingleOrDefaultAsync(x => x.Id == deadLetterId, cancellationToken).ConfigureAwait(false);
        if (deadLetter is null || deadLetter.ReplayedAt is not null || !string.Equals(deadLetter.SourceType, "delivery", StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        Delivery? delivery = await db.Deliveries.SingleOrDefaultAsync(x => x.Id == deadLetter.SourceId, cancellationToken).ConfigureAwait(false);
        if (delivery is null || delivery.State != WorkItemState.DeadLettered)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        delivery.RequeueFromDeadLetter(now);
        deadLetter.MarkReplayed(operatorId, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task CancelPendingForDomainAsync(
        string domain,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        string safeReason = reason.Length <= 4_096 ? reason : reason[..4_096];
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Deliveries
            .Where(x => x.RemoteDomain == domain && (x.State == WorkItemState.Pending || x.State == WorkItemState.Leased))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.State, WorkItemState.Cancelled)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.LastErrorCode, "cancelled")
                .SetProperty(x => x.LastError, safeReason)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<long> CountPendingAsync(CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Deliveries.LongCountAsync(
            x => x.State == WorkItemState.Pending || x.State == WorkItemState.Leased,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeSpan?> GetOldestPendingAgeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? oldest = await db.Deliveries
            .Where(x => x.State == WorkItemState.Pending || x.State == WorkItemState.Leased)
            .MinAsync(x => (DateTimeOffset?)x.CreatedAt, cancellationToken)
            .ConfigureAwait(false);
        return oldest is null ? null : now - oldest.Value;
    }

    private static async Task PersistAggregateAsync<TEntity>(
        FederationDbContext db,
        TEntity? aggregate,
        DbSet<TEntity> set,
        CancellationToken cancellationToken)
        where TEntity : Entity
    {
        if (aggregate is null)
        {
            return;
        }

        bool exists = await set.AnyAsync(x => x.Id == aggregate.Id, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            db.Attach(aggregate);
            db.Entry(aggregate).State = EntityState.Modified;
        }
        else
        {
            set.Add(aggregate);
        }
    }
}
