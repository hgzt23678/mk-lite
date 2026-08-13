using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence.Tests;

[Collection(PostgreSqlFixtureDefinition.Name)]
public sealed class DurableQueueIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentWorkersDoNotClaimTheSameDelivery()
    {
        Guid activityId = await InsertOutboundAsync(2);
        await using AsyncServiceScope scopeA = fixture.Services.CreateAsyncScope();
        await using AsyncServiceScope scopeB = fixture.Services.CreateAsyncScope();
        IDeliveryRepository repositoryA = scopeA.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        IDeliveryRepository repositoryB = scopeB.ServiceProvider.GetRequiredService<IDeliveryRepository>();

        Task<IReadOnlyList<Delivery>> claimA = repositoryA.ClaimAsync("worker-a", 1, TimeSpan.FromMinutes(1), Now, CancellationToken.None);
        Task<IReadOnlyList<Delivery>> claimB = repositoryB.ClaimAsync("worker-b", 1, TimeSpan.FromMinutes(1), Now, CancellationToken.None);
        IReadOnlyList<Delivery>[] results = await Task.WhenAll(claimA, claimB);

        Delivery[] claimed = results.SelectMany(x => x).Where(x => x.ActivityId == activityId).ToArray();
        Assert.Equal(2, claimed.Length);
        Assert.Equal(2, claimed.Select(x => x.Id).Distinct().Count());
        Assert.Equal(2, claimed.Select(x => x.LeaseOwner).Distinct().Count());
    }

    [Fact]
    public async Task FederationQueueAdministrationListsSafeJobMetadataAndCurrentCounts()
    {
        string domain = $"queue-admin-{Guid.NewGuid():N}.example";
        Guid activityId = await InsertOutboundToDomainAsync(domain);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> contextFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FederationDbContext>>();
        DateTimeOffset inboxCreatedAt = DateTimeOffset.UtcNow;
        string inboxDomain = $"inbox-delay-{Guid.NewGuid():N}.example";
        InboxItem delayedInbox = InboxItem.Accept(
            $"https://{inboxDomain}/activities/1",
            $"https://{inboxDomain}/users/alice",
            "Create",
            "{}"u8.ToArray(),
            SignatureProfile.LegacyCavage,
            $"https://{inboxDomain}/users/alice#main-key",
            inboxCreatedAt,
            inboxCreatedAt);
        delayedInbox.AcquireLease("queue-admin-test", inboxCreatedAt, TimeSpan.FromMinutes(1));
        delayedInbox.ScheduleRetry(
            "queue-admin-test",
            inboxCreatedAt,
            inboxCreatedAt.AddHours(1),
            "fixture-delay",
            "fixture delay");
        await using (FederationDbContext db = await contextFactory.CreateDbContextAsync())
        {
            db.InboxItems.Add(delayedInbox);
            await db.SaveChangesAsync();
        }

        IFederationQueueAdministration administration = scope.ServiceProvider
            .GetRequiredService<IFederationQueueAdministration>();

        IReadOnlyList<FederationQueueJobSummary> jobs = await administration.ListAsync(
            WorkItemState.Pending,
            null,
            domain,
            null,
            10,
            CancellationToken.None);
        FederationQueueJobSummary job = Assert.Single(jobs, item => item.ActivityId == activityId);
        Assert.Equal(domain, job.RemoteDomain);
        Assert.Equal(0, job.AttemptCount);
        Assert.Null(job.LeaseOwner);

        FederationQueueStats stats = await administration.GetStatsAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        Assert.True(stats.Waiting + stats.Delayed + stats.Active + stats.Stalled > 0);
        Assert.False(stats.RedisWakeupEnabled);
        Assert.Contains(stats.InboxDelayedByDomain, item =>
            item.Domain == inboxDomain && item.Count >= 1);
    }

    [Fact]
    public async Task ExpiredDeliveryLeaseIsRecoveredAfterWorkerCrash()
    {
        Guid activityId = await InsertOutboundAsync(1);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        Delivery first = Assert.Single(await repository.ClaimAsync(
            "crashed-worker",
            1,
            TimeSpan.FromSeconds(10),
            Now,
            CancellationToken.None), x => x.ActivityId == activityId);

        IReadOnlyList<Delivery> recovered = await repository.ClaimAsync(
            "replacement-worker",
            100,
            TimeSpan.FromMinutes(1),
            Now.AddSeconds(10),
            CancellationToken.None);

        Delivery item = Assert.Single(recovered, x => x.Id == first.Id);
        Assert.Equal("replacement-worker", item.LeaseOwner);
        Assert.Equal(2, item.AttemptCount);
    }

    [Fact]
    public async Task AllMigrationsApplyAndModelHasNoPendingChanges()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync(CancellationToken.None);

        string[] pending = (await db.Database.GetPendingMigrationsAsync(CancellationToken.None)).ToArray();
        bool modelChanged = db.Database.HasPendingModelChanges();

        IDbContextFactory<LocalIdentityDbContext> identityFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync(CancellationToken.None);
        string[] pendingIdentity = (await identity.Database.GetPendingMigrationsAsync(CancellationToken.None)).ToArray();
        bool identityModelChanged = identity.Database.HasPendingModelChanges();

        Assert.Empty(pending);
        Assert.False(modelChanged);
        Assert.Empty(pendingIdentity);
        Assert.False(identityModelChanged);
    }

    [Fact]
    public async Task DuplicateActivityIsIdempotentAndChangedBytesAreQuarantined()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string localIri = $"https://local.example/users/{suffix}";
        await using (AsyncServiceScope setupScope = fixture.Services.CreateAsyncScope())
        {
            IDbContextFactory<FederationDbContext> factory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
            await using FederationDbContext db = await factory.CreateDbContextAsync(CancellationToken.None);
            db.LocalActors.Add(LocalActor.Create(localIri, "u" + suffix[..20], ActorKind.Person, Now));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        string activityIri = $"https://remote.example/activities/{Guid.NewGuid()}";
        byte[] original = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Create\",\"actor\":\"https://remote.example/users/alice\",\"to\":\"{localIri}\"}}");
        byte[] changed = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Delete\",\"actor\":\"https://remote.example/users/alice\",\"to\":\"{localIri}\"}}");
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IInboxRepository repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();

        InboxAcceptance accepted = await repository.AcceptAsync(Inbound(activityIri, localIri, original, "replay-a"), CancellationToken.None);
        InboxAcceptance duplicate = await repository.AcceptAsync(Inbound(activityIri, localIri, original, "replay-a"), CancellationToken.None);
        InboxAcceptance conflict = await repository.AcceptAsync(Inbound(activityIri, localIri, changed, "replay-b"), CancellationToken.None);

        Assert.Equal(InboxAcceptanceStatus.Accepted, accepted.Status);
        Assert.Equal(InboxAcceptanceStatus.Duplicate, duplicate.Status);
        Assert.Equal(InboxAcceptanceStatus.ConflictQuarantined, conflict.Status);
        IDbContextFactory<FederationDbContext> verificationFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await verificationFactory.CreateDbContextAsync(CancellationToken.None);
        Assert.Equal(1, await verification.InboxItems.CountAsync(x => x.ActivityIri == activityIri));
        Assert.Equal(1, await verification.InboxConflicts.CountAsync(x => x.ActivityIri == activityIri));
    }

    [Fact]
    public async Task ProcessedInboundActivityPersistsNormalizedRecipientsAtomically()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string localIri = $"https://local.example/users/{suffix}";
        string activityIri = $"https://remote.example/activities/{Guid.NewGuid()}";
        byte[] body = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Like\",\"actor\":\"https://remote.example/users/alice\",\"to\":\"{localIri}\"}}");
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            setup.LocalActors.Add(LocalActor.Create(localIri, "u" + suffix[..20], ActorKind.Person, Now));
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        IInboxRepository repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        InboxAcceptance acceptance = await repository.AcceptAsync(
            Inbound(activityIri, localIri, body, "recipient-" + suffix),
            CancellationToken.None);
        InboxItem item = Assert.Single(await repository.ClaimAsync(
            "recipient-worker",
            1_000,
            TimeSpan.FromMinutes(1),
            Now,
            CancellationToken.None), candidate => candidate.Id == acceptance.InboxItemId);
        var activity = ActivityRecord.Create(
            activityIri,
            "https://remote.example/users/alice",
            "Like",
            null,
            ActivityDirection.Inbound,
            Visibility.MentionedOnly,
            Encoding.UTF8.GetString(body),
            PayloadDigest.Sha256Hex(body),
            false,
            Now,
            Now);
        item.Succeed("recipient-worker", Now);
        await repository.SaveProcessedAsync(
            item,
            new InboxSideEffects(
                activity,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [ActivityRecipient.Create(activity.Id, localIri, AudienceField.To)]),
            CancellationToken.None);

        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.True(await verification.ActivityRecipients.AnyAsync(
            recipient => recipient.ActivityId == activity.Id && recipient.RecipientIri == localIri));
        UserNotification notification = await verification.UserNotifications.SingleAsync(x =>
            x.RecipientActorIri == localIri && x.ActivityIri == activityIri);
        Assert.Equal(UserNotificationKind.Favourite, notification.Kind);
        StreamEvent notificationEvent = await verification.StreamEvents.SingleAsync(x =>
            x.Kind == StreamEventKind.NotificationCreated && x.ResourceId == notification.Id);
        Assert.Equal(localIri, notificationEvent.RecipientActorIri);
    }

    [Fact]
    public async Task LocalLikePersistsNotificationAndStreamEventInOutboundTransaction()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string sourceActorIri = $"https://local.example/users/source-{suffix}";
        string recipientActorIri = $"https://local.example/users/recipient-{suffix}";
        string objectIri = $"https://local.example/objects/{suffix}";
        string activityIri = $"https://local.example/activities/{Guid.NewGuid():N}";
        string objectJson = JsonSerializer.Serialize(new
        {
            id = objectIri,
            type = "Note",
            attributedTo = recipientActorIri,
            content = "local notification target",
            to = "https://www.w3.org/ns/activitystreams#Public"
        });
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync())
        {
            setup.LocalActors.Add(LocalActor.Create(sourceActorIri, "s" + suffix[..20], ActorKind.Person, Now));
            setup.LocalActors.Add(LocalActor.Create(recipientActorIri, "r" + suffix[..20], ActorKind.Person, Now));
            setup.Objects.Add(FederatedObject.Create(
                objectIri,
                recipientActorIri,
                "Note",
                Visibility.Public,
                objectJson,
                PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(objectJson)),
                Now,
                Now));
            await setup.SaveChangesAsync();
        }

        LikeRelation like = LikeRelation.Create(sourceActorIri, objectIri, activityIri, Now);
        string activityJson = JsonSerializer.Serialize(new
        {
            id = activityIri,
            type = "Like",
            actor = sourceActorIri,
            @object = objectIri,
            to = recipientActorIri
        });
        ActivityRecord activity = ActivityRecord.Create(
            activityIri,
            sourceActorIri,
            "Like",
            objectIri,
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            activityJson,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(activityJson)),
            false,
            Now,
            Now);
        await scope.ServiceProvider.GetRequiredService<IDeliveryRepository>().CommitOutboundAsync(
            new OutboundCommit(
                activity,
                null,
                null,
                null,
                null,
                null,
                [ActivityRecipient.Create(activity.Id, recipientActorIri, AudienceField.To)],
                [],
                LikeRelation: like),
            CancellationToken.None);

        await using FederationDbContext verification = await factory.CreateDbContextAsync();
        UserNotification notification = await verification.UserNotifications.SingleAsync(x =>
            x.ActivityIri == activityIri && x.RecipientActorIri == recipientActorIri);
        Assert.Equal(UserNotificationKind.Favourite, notification.Kind);
        Assert.True(await verification.StreamEvents.AnyAsync(x =>
            x.Kind == StreamEventKind.NotificationCreated && x.ResourceId == notification.Id &&
            x.RecipientActorIri == recipientActorIri));
        Assert.True(await verification.Activities.AnyAsync(x => x.Id == activity.Id));
        Assert.True(await verification.LikeRelations.AnyAsync(x => x.Id == like.Id));
    }

    [Fact]
    public async Task InboxDeadLetterCanBeAuditedAndRequeuedWithoutRetainingQuarantineFlag()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string localIri = $"https://local.example/users/{suffix}";
        string activityIri = $"https://remote.example/activities/{Guid.NewGuid()}";
        byte[] body = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Like\",\"actor\":\"https://remote.example/users/alice\",\"to\":\"{localIri}\"}}");
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            setup.LocalActors.Add(LocalActor.Create(localIri, "u" + suffix[..20], ActorKind.Person, Now));
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        IInboxRepository repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        InboxAcceptance acceptance = await repository.AcceptAsync(
            Inbound(activityIri, localIri, body, "dead-letter-" + suffix),
            CancellationToken.None);
        InboxItem item = Assert.Single(await repository.ClaimAsync(
            "quarantine-worker",
            1_000,
            TimeSpan.FromMinutes(1),
            Now,
            CancellationToken.None), candidate => candidate.Id == acceptance.InboxItemId);
        item.Quarantine("quarantine-worker", Now, "unsafe fixture");
        DeadLetter deadLetter = DeadLetter.Create("inbox", item.Id, "unsafe_activity", "unsafe fixture", Now);
        await repository.SaveFailureAsync(item, deadLetter, CancellationToken.None);

        IModerationAdministration administration = scope.ServiceProvider.GetRequiredService<IModerationAdministration>();
        Assert.True(await administration.RequeueDeadLetterAsync(
            deadLetter.Id,
            "test-operator",
            CancellationToken.None));

        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        InboxItem persisted = await verification.InboxItems.SingleAsync(x => x.Id == item.Id);
        DeadLetter persistedDeadLetter = await verification.DeadLetters.SingleAsync(x => x.Id == deadLetter.Id);
        Assert.Equal(WorkItemState.Pending, persisted.State);
        Assert.False(persisted.IsQuarantined);
        Assert.Null(persisted.QuarantineReason);
        Assert.NotNull(persistedDeadLetter.ReplayedAt);
        Assert.Equal("test-operator", persistedDeadLetter.ReplayedBy);
        Assert.True(await verification.AuditEvents.AnyAsync(x => x.Action == "dead-letter-requeued"));
    }

    [Fact]
    public async Task ClientIdempotencyKeyReturnsOriginalResultAndRejectsChangedBody()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        string idempotencyKey = "request-" + Guid.NewGuid().ToString("N");
        OutboundCommit firstCommit = IdempotentCommit(idempotencyKey, new string('a', 64));
        OutboundCommit sameCommit = IdempotentCommit(idempotencyKey, new string('a', 64));
        OutboundCommit changedCommit = IdempotentCommit(idempotencyKey, new string('b', 64));

        OutboundCommitResult first = await repository.CommitOutboundAsync(firstCommit, CancellationToken.None);
        OutboundCommitResult same = await repository.CommitOutboundAsync(sameCommit, CancellationToken.None);

        Assert.False(first.WasExisting);
        Assert.True(same.WasExisting);
        Assert.Equal(firstCommit.Activity.Iri, same.ExistingResult?.ActivityIri);
        await Assert.ThrowsAsync<DomainException>(() =>
            repository.CommitOutboundAsync(changedCommit, CancellationToken.None));
    }

    [Fact]
    public async Task EmojiReactionReplacementReversesOldRelationBeforeInsertingNewOne()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository deliveries = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        IInboxRepository inbox = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://local.example/users/reactor-{suffix}";
        string objectIri = $"https://remote.example/objects/{suffix}";
        string firstActivityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        string secondActivityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        LikeRelation firstRelation = LikeRelation.Create(
            actorIri,
            objectIri,
            firstActivityIri,
            FederatedReaction.Create("👍", actorIri),
            Now);
        ActivityRecord firstActivity = ActivityRecord.Create(
            firstActivityIri,
            actorIri,
            "Like",
            objectIri,
            ActivityDirection.Outbound,
            Visibility.Public,
            "{}",
            PayloadDigest.Sha256Hex("{}"u8),
            false,
            Now,
            Now);
        await deliveries.CommitOutboundAsync(new OutboundCommit(
            firstActivity,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            LikeRelation: firstRelation), CancellationToken.None);

        LikeRelation persistedFirst = await inbox.FindActiveLikeAsync(actorIri, objectIri, CancellationToken.None)
            ?? throw new InvalidOperationException("Initial reaction was not persisted.");
        persistedFirst.Undo(actorIri, Now.AddMinutes(1));
        FederatedReaction replacement = FederatedReaction.Create(
            ":party:",
            actorIri,
            "https://local.example/emojis/party",
            ":party:",
            "https://local.example/media/party.png",
            "image/png");
        LikeRelation replacementRelation = LikeRelation.Create(
            actorIri,
            objectIri,
            secondActivityIri,
            replacement,
            Now.AddMinutes(1));
        ActivityRecord secondActivity = ActivityRecord.Create(
            secondActivityIri,
            actorIri,
            "Like",
            objectIri,
            ActivityDirection.Outbound,
            Visibility.Public,
            "{}",
            PayloadDigest.Sha256Hex("{}"u8),
            false,
            Now.AddMinutes(1),
            Now.AddMinutes(1));

        await deliveries.CommitOutboundAsync(new OutboundCommit(
            secondActivity,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            LikeRelation: replacementRelation,
            ReplacedLikeRelation: persistedFirst), CancellationToken.None);

        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        LikeRelation[] stored = await verification.LikeRelations
            .Where(x => x.ActorIri == actorIri && x.ObjectIri == objectIri)
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(CancellationToken.None);
        Assert.Equal(2, stored.Length);
        Assert.Equal(FederatedRelationState.Reversed, stored[0].State);
        Assert.Equal(FederatedRelationState.Active, stored[1].State);
        Assert.Equal(":party@local.example:", stored[1].EffectiveReaction);
        Assert.Equal("https://local.example/media/party.png", stored[1].CustomEmojiUrl);
        Assert.Single(stored, x => x.State == FederatedRelationState.Active);
    }

    [Fact]
    public async Task LitePubAllowsDistinctEmojiReactionsButDatabaseRejectsDuplicateReaction()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://akkoma.example/users/{suffix}";
        string objectIri = $"https://remote.example/objects/{suffix}";

        await CommitEmojiAsync(repository, actorIri, objectIri, "🎉");
        await CommitEmojiAsync(repository, actorIri, objectIri, "🔥");
        await Assert.ThrowsAsync<DbUpdateException>(() => CommitEmojiAsync(repository, actorIri, objectIri, "🎉"));

        await using FederationDbContext db = await factory.CreateDbContextAsync(CancellationToken.None);
        EmojiReactionRelation[] active = await db.EmojiReactionRelations.AsNoTracking()
            .Where(x => x.ActorIri == actorIri && x.ObjectIri == objectIri && x.State == FederatedRelationState.Active)
            .OrderBy(x => x.Reaction)
            .ToArrayAsync();
        Assert.Equal(2, active.Length);
        Assert.Contains(active, x => x.Reaction == "🎉");
        Assert.Contains(active, x => x.Reaction == "🔥");
    }

    [Fact]
    public async Task MediaGarbageClaimIsDurableAndCanBeMarkedPurged()
    {
        string suffix = Guid.NewGuid().ToString("N");
        MediaResource media = MediaResource.Create(
            $"https://local.example/users/{suffix}",
            $"media/{suffix}/original.png",
            new string('c', 64),
            "image/png",
            "photo.png",
            128,
            Visibility.Public,
            Now.AddDays(-31));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IMediaRepository repository = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        await repository.AddAsync(media, CancellationToken.None);

        IReadOnlyList<MediaGarbageCandidate> claimed = await repository.ClaimGarbageAsync(
            Now.AddDays(-30),
            Now.AddMinutes(-5),
            Now,
            100,
            CancellationToken.None);

        MediaGarbageCandidate candidate = Assert.Single(claimed, x => x.Id == media.Id);
        Assert.Equal(media.StorageKey, candidate.StorageKey);
        await repository.MarkPurgedAsync(media.Id, Now.AddSeconds(1), CancellationToken.None);
        MediaResource? persisted = await repository.FindAsync(media.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(MediaState.Deleted, persisted.State);
        Assert.Equal(Now.AddSeconds(1), persisted.PurgedAt);
    }

    [Fact]
    public async Task MediaGarbageCollectionProtectsOnlyLiveAnnouncementImages()
    {
        string suffix = Guid.NewGuid().ToString("N");
        MediaResource liveMedia = CreateOldMedia(suffix + "-live");
        MediaResource expiredMedia = CreateOldMedia(suffix + "-expired");
        Announcement live = Announcement.Create(
            "Live " + suffix,
            "Live announcement media fixture.",
            $"/media/{liveMedia.Id}",
            AnnouncementAudience.Public,
            Now.AddDays(-31),
            null,
            "fixture-admin",
            Now.AddDays(-31));
        Announcement expired = Announcement.Create(
            "Expired " + suffix,
            "Expired announcement media fixture.",
            $"/media/{expiredMedia.Id}",
            AnnouncementAudience.Public,
            Now.AddDays(-31),
            Now.AddMinutes(-1),
            "fixture-admin",
            Now.AddDays(-31));

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            setup.Media.AddRange(liveMedia, expiredMedia);
            setup.Announcements.AddRange(live, expired);
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        IMediaRepository repository = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        IReadOnlyList<MediaGarbageCandidate> firstClaim = await repository.ClaimGarbageAsync(
            Now.AddDays(-30),
            Now.AddMinutes(-5),
            Now,
            1_000,
            CancellationToken.None);

        Assert.DoesNotContain(firstClaim, candidate => candidate.Id == liveMedia.Id);
        Assert.Contains(firstClaim, candidate => candidate.Id == expiredMedia.Id);

        await using (FederationDbContext update = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            update.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Announcement persisted = await update.Announcements.SingleAsync(
                value => value.Id == live.Id,
                CancellationToken.None);
            persisted.Delete("fixture-admin", Now.AddSeconds(1));
            await update.SaveChangesAsync(CancellationToken.None);
        }

        await using (FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            Announcement persistedAnnouncement = await verification.Announcements.SingleAsync(
                value => value.Id == live.Id,
                CancellationToken.None);
            MediaResource persistedMedia = await verification.Media.SingleAsync(
                value => value.Id == liveMedia.Id,
                CancellationToken.None);
            Assert.NotNull(persistedAnnouncement.DeletedAt);
            Assert.Equal(MediaState.PendingScan, persistedMedia.State);
            Assert.True(persistedMedia.UpdatedAt <= Now.AddDays(-30));
            Assert.False(await verification.MediaAttachments.AnyAsync(
                value => value.MediaId == liveMedia.Id,
                CancellationToken.None));
        }

        IReadOnlyList<MediaGarbageCandidate> secondClaim = await repository.ClaimGarbageAsync(
            Now.AddDays(-30),
            Now.AddMinutes(-5),
            Now.AddSeconds(2),
            1_000,
            CancellationToken.None);
        Assert.Contains(secondClaim, candidate => candidate.Id == liveMedia.Id);
    }

    [Fact]
    public async Task PostgreSqlBackupRestoresSchemaAndDataIntoCleanDatabase()
    {
        string marker = Guid.NewGuid().ToString("N");

        string restoredMarker = await fixture.VerifyBackupRestoreAsync(marker, CancellationToken.None);

        Assert.Equal(marker, restoredMarker);
    }

    [Fact]
    public async Task PendingDeliverySurvivesDatabaseConnectionInterruption()
    {
        Guid activityId = await InsertOutboundAsync(1);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext interrupted = await factory.CreateDbContextAsync(CancellationToken.None);
        await interrupted.Database.OpenConnectionAsync(CancellationToken.None);
        int backendProcessId = await interrupted.Database
            .SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(CancellationToken.None);

        await fixture.TerminateBackendAsync(backendProcessId, CancellationToken.None);
        _ = await Assert.ThrowsAnyAsync<Exception>(() =>
            interrupted.Database.ExecuteSqlRawAsync("SELECT 1", CancellationToken.None));

        IReadOnlyList<Delivery> recovered = await repository.ClaimAsync(
            "recovered-after-db-interruption",
            100,
            TimeSpan.FromMinutes(1),
            Now,
            CancellationToken.None);
        Assert.Contains(recovered, x => x.ActivityId == activityId);
    }

    [Fact]
    public async Task EmergencyOutboundPausePreventsNewClaimsAndCanResume()
    {
        Guid activityId = await InsertOutboundAsync(1);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IModerationAdministration administration = scope.ServiceProvider.GetRequiredService<IModerationAdministration>();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        await administration.SetOutboundDeliveryPausedAsync(
            true,
            "integration-test",
            "test-operator",
            CancellationToken.None);
        try
        {
            IReadOnlyList<Delivery> whilePaused = await repository.ClaimAsync(
                "paused-worker",
                1_000,
                TimeSpan.FromMinutes(1),
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            Assert.DoesNotContain(whilePaused, x => x.ActivityId == activityId);
        }
        finally
        {
            await administration.SetOutboundDeliveryPausedAsync(
                false,
                "integration-test-complete",
                "test-operator",
                CancellationToken.None);
        }

        IReadOnlyList<Delivery> afterResume = await repository.ClaimAsync(
            "resumed-worker",
            1_000,
            TimeSpan.FromMinutes(1),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        Assert.Contains(afterResume, x => x.ActivityId == activityId);
    }

    [Fact]
    public async Task RejectPoliciesPreventNewOutboundClaimsAndActorResolution()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string domain = $"blocked-{suffix}.example";
        string actorIri = $"https://{domain}/users/alice";
        Guid activityId = await InsertOutboundToDomainAsync(domain);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IModerationAdministration administration = scope.ServiceProvider.GetRequiredService<IModerationAdministration>();
        _ = await administration.CreateDomainPolicyAsync(
            domain,
            FederationPolicyKind.Reject,
            "integration-test",
            "test-operator",
            null,
            CancellationToken.None);

        IReadOnlySet<string> rejected = await scope.ServiceProvider.GetRequiredService<IDomainPolicyService>()
            .FindRejectedActorsAsync([actorIri], CancellationToken.None);
        IReadOnlyList<Delivery> claimed = await scope.ServiceProvider.GetRequiredService<IDeliveryRepository>()
            .ClaimAsync("policy-worker", 1_000, TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Contains(actorIri, rejected);
        Assert.DoesNotContain(claimed, delivery => delivery.ActivityId == activityId);
    }

    [Fact]
    public async Task LimitedDomainReceivesOnlyOneConcurrentExecutionSlot()
    {
        string domain = $"limited-{Guid.NewGuid():N}.example";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IModerationAdministration administration = scope.ServiceProvider.GetRequiredService<IModerationAdministration>();
        _ = await administration.CreateDomainPolicyAsync(
            domain,
            FederationPolicyKind.Limit,
            "integration-test",
            "test-operator",
            null,
            CancellationToken.None);
        IRemoteDomainExecutionStore store = scope.ServiceProvider.GetRequiredService<IRemoteDomainExecutionStore>();

        DomainLeaseToken? first = await store.TryAcquireAsync(
            domain,
            "worker-a",
            Guid.NewGuid(),
            3,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        DomainLeaseToken? second = await store.TryAcquireAsync(
            domain,
            "worker-b",
            Guid.NewGuid(),
            3,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        await store.ReleaseAsync(first, CancellationToken.None);
    }

    [Fact]
    public async Task GoneRemoteEndpointIsNotReusedByFutureDeliveries()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://remote-{suffix}.example/users/alice";
        string inboxIri = $"https://remote-{suffix}.example/inbox";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IRemoteActorDirectory directory = scope.ServiceProvider.GetRequiredService<IRemoteActorDirectory>();
        await directory.SaveAsync(
            new RemoteActorSnapshot(
                actorIri,
                "Person",
                "alice",
                $"{{\"id\":\"{actorIri}\",\"type\":\"Person\"}}",
                inboxIri,
                null,
                null,
                null,
                Now),
            CancellationToken.None);
        Assert.NotNull(await directory.FindEndpointAsync(actorIri, CancellationToken.None));

        await directory.MarkEndpointGoneAsync(inboxIri, Now.AddMinutes(1), CancellationToken.None);

        Assert.Null(await directory.FindEndpointAsync(actorIri, CancellationToken.None));
    }

    [Fact]
    public async Task FollowersCollectionDeliverySnapshotsTheEligibleLocalFollower()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string localIri = $"https://local.example/users/{suffix}";
        string remoteActorIri = $"https://remote-{suffix}.example/users/alice";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            setup.LocalActors.Add(LocalActor.Create(localIri, "u" + suffix[..20], ActorKind.Person, Now));
            FollowRelation follow = FollowRelation.Request(
                localIri,
                remoteActorIri,
                $"https://local.example/activities/{Guid.NewGuid()}",
                Now);
            follow.Accept(remoteActorIri, $"https://remote-{suffix}.example/activities/{Guid.NewGuid()}", Now);
            setup.FollowRelations.Add(follow);
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        string activityIri = $"https://remote-{suffix}.example/activities/{Guid.NewGuid()}";
        byte[] body = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Create\",\"actor\":\"{remoteActorIri}\",\"to\":\"{remoteActorIri}/followers\"}}");
        var inbound = new VerifiedInboundActivity(
            activityIri,
            remoteActorIri,
            "Create",
            null,
            null,
            new Uri(remoteActorIri).GetLeftPart(UriPartial.Authority),
            [new AudienceAddress(remoteActorIri + "/followers", AudienceField.To)],
            localIri,
            body,
            PayloadDigest.Sha256Hex(body),
            SignatureProfile.LegacyCavage,
            remoteActorIri + "#main-key",
            Now,
            "followers-" + suffix,
            null,
            Now);

        InboxAcceptance acceptance = await scope.ServiceProvider.GetRequiredService<IInboxRepository>()
            .AcceptAsync(inbound, CancellationToken.None);

        Assert.Equal(InboxAcceptanceStatus.Accepted, acceptance.Status);
        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.True(await verification.InboxItemRecipients.AnyAsync(
            recipient => recipient.InboxItemId == acceptance.InboxItemId && recipient.ActorIri == localIri));
    }

    [Fact]
    public async Task PublicDeleteWithoutFollowerCollectionTargetsOnlyFollowersOfVerifiedObjectOwner()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string localIri = $"https://local.example/users/{suffix}";
        string remoteActorIri = $"https://remote-{suffix}.example/users/alice";
        string objectIri = $"https://remote-{suffix}.example/objects/1";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            setup.LocalActors.Add(LocalActor.Create(localIri, "u" + suffix[..20], ActorKind.Person, Now));
            FollowRelation follow = FollowRelation.Request(
                localIri,
                remoteActorIri,
                $"https://local.example/activities/{Guid.NewGuid()}",
                Now);
            follow.Accept(remoteActorIri, $"https://remote-{suffix}.example/activities/{Guid.NewGuid()}", Now);
            setup.FollowRelations.Add(follow);
            setup.Objects.Add(FederatedObject.Create(
                objectIri,
                remoteActorIri,
                "Note",
                Visibility.Public,
                $"{{\"id\":\"{objectIri}\",\"type\":\"Note\",\"attributedTo\":\"{remoteActorIri}\"}}",
                new string('a', 64),
                Now,
                Now));
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        string activityIri = $"https://remote-{suffix}.example/activities/delete";
        byte[] body = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Delete\",\"actor\":\"{remoteActorIri}\",\"object\":\"{objectIri}\",\"to\":\"https://www.w3.org/ns/activitystreams#Public\"}}");
        var inbound = new VerifiedInboundActivity(
            activityIri,
            remoteActorIri,
            "Delete",
            objectIri,
            null,
            new Uri(remoteActorIri).GetLeftPart(UriPartial.Authority),
            [new AudienceAddress("https://www.w3.org/ns/activitystreams#Public", AudienceField.To)],
            null,
            body,
            PayloadDigest.Sha256Hex(body),
            SignatureProfile.LegacyCavage,
            remoteActorIri + "#main-key",
            Now,
            "delete-" + suffix,
            null,
            Now);

        InboxAcceptance acceptance = await scope.ServiceProvider.GetRequiredService<IInboxRepository>()
            .AcceptAsync(inbound, CancellationToken.None);

        Assert.Equal(InboxAcceptanceStatus.Accepted, acceptance.Status);
        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.True(await verification.InboxItemRecipients.AnyAsync(
            recipient => recipient.InboxItemId == acceptance.InboxItemId && recipient.ActorIri == localIri));
    }

    [Fact]
    public async Task OperatorCanCancelPendingDomainDeliveriesWithAnAuditEvent()
    {
        string domain = $"cancel-{Guid.NewGuid():N}.example";
        Guid activityId = await InsertOutboundToDomainAsync(domain);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IModerationAdministration administration = scope.ServiceProvider.GetRequiredService<IModerationAdministration>();

        int cancelled = await administration.CancelPendingDeliveriesForDomainAsync(
            domain,
            "incident-fixture",
            "test-operator",
            CancellationToken.None);

        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.Equal(1, cancelled);
        Assert.Equal(
            WorkItemState.Cancelled,
            await verification.Deliveries.Where(x => x.ActivityId == activityId).Select(x => x.State).SingleAsync());
        Assert.True(await verification.AuditEvents.AnyAsync(
            x => x.Action == "domain-deliveries-cancelled" && x.Target == domain));
    }

    [Fact]
    public async Task EndpointRediscoveryReplacesTheFailingDeliveryAndItsRecipientSnapshotAtomically()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string localActor = $"https://local.example/users/{suffix}";
        string remoteActor = $"https://remote.example/users/{suffix}";
        string activityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        string oldEndpoint = $"https://remote.example/inbox/old-{suffix}";
        string newEndpoint = $"https://remote.example/inbox/new-{suffix}";
        byte[] payload = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Like\"}}");
        ActivityRecord activity = ActivityRecord.Create(
            activityIri,
            localActor,
            "Like",
            "https://remote.example/objects/1",
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            false,
            Now,
            Now);
        Delivery delivery = Delivery.Create(
            activity.Id,
            activity.Iri,
            oldEndpoint,
            localActor,
            payload,
            SignatureProfile.LegacyCavage,
            Now);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        await repository.CommitOutboundAsync(
            new OutboundCommit(
                activity,
                null,
                null,
                null,
                null,
                null,
                [ActivityRecipient.Create(activity.Id, remoteActor, AudienceField.To)],
                [delivery],
                DeliveryTargets: [DeliveryTarget.Create(delivery.Id, remoteActor)]),
            CancellationToken.None);

        Delivery leased = Assert.Single(
            await repository.ClaimAsync("rediscovery-worker", 1_000, TimeSpan.FromMinutes(2), Now, CancellationToken.None),
            candidate => candidate.Id == delivery.Id);
        leased.ReplaceEndpoint("rediscovery-worker", newEndpoint, Now.AddSeconds(1));
        leased.ScheduleRetry(
            "rediscovery-worker",
            Now.AddSeconds(1),
            Now.AddMinutes(1),
            "endpoint_gone",
            "old inbox is gone");
        DeliveryAttempt attempt = DeliveryAttempt.Create(
            leased.Id,
            leased.AttemptCount,
            DeliveryAttemptOutcome.RetryScheduled,
            410,
            "endpoint_gone",
            "old inbox is gone",
            TimeSpan.FromMilliseconds(5),
            Now,
            Now.AddSeconds(1));
        var rediscovery = new EndpointRediscoveryPlan(
            [DeliveryTarget.Create(leased.Id, remoteActor)],
            [],
            [],
            [DeliveryEndpointChange.Create(leased.Id, oldEndpoint, newEndpoint, 1, Now.AddSeconds(1))]);

        await repository.SaveAttemptAsync(leased, attempt, null, CancellationToken.None, rediscovery);

        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        Delivery persisted = await verification.Deliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.Equal(newEndpoint, persisted.EndpointIri);
        Assert.Equal(WorkItemState.Pending, persisted.State);
        Assert.Equal([remoteActor], await verification.DeliveryTargets
            .Where(x => x.DeliveryId == delivery.Id)
            .Select(x => x.ActorIri)
            .ToArrayAsync());
        Assert.True(await verification.DeliveryEndpointChanges.AnyAsync(x =>
            x.DeliveryId == delivery.Id &&
            x.PreviousEndpointIri == oldEndpoint &&
            x.ReplacementEndpointIri == newEndpoint));
    }

    [Fact]
    public async Task EndpointRediscoveryMergesAnExistingActiveDeliveryIntoTheFailingDelivery()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string localActor = $"https://local.example/users/{suffix}";
        string firstRecipient = $"https://remote.example/users/first-{suffix}";
        string secondRecipient = $"https://remote.example/users/second-{suffix}";
        string activityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        string oldEndpoint = $"https://remote.example/inbox/old-{suffix}";
        string newEndpoint = $"https://remote.example/inbox/shared-{suffix}";
        byte[] payload = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Like\"}}");
        ActivityRecord activity = ActivityRecord.Create(
            activityIri,
            localActor,
            "Like",
            "https://remote.example/objects/1",
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            false,
            Now,
            Now);
        Delivery failing = Delivery.Create(
            activity.Id,
            activity.Iri,
            oldEndpoint,
            localActor,
            payload,
            SignatureProfile.LegacyCavage,
            Now);
        Delivery existing = Delivery.Create(
            activity.Id,
            activity.Iri,
            newEndpoint,
            localActor,
            payload,
            SignatureProfile.LegacyCavage,
            Now.AddSeconds(1));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        await repository.CommitOutboundAsync(
            new OutboundCommit(
                activity,
                null,
                null,
                null,
                null,
                null,
                [
                    ActivityRecipient.Create(activity.Id, firstRecipient, AudienceField.To),
                    ActivityRecipient.Create(activity.Id, secondRecipient, AudienceField.To)
                ],
                [failing, existing],
                DeliveryTargets:
                [
                    DeliveryTarget.Create(failing.Id, firstRecipient),
                    DeliveryTarget.Create(existing.Id, secondRecipient)
                ]),
            CancellationToken.None);

        Delivery leased = Assert.Single(
            await repository.ClaimAsync("rediscovery-merge-worker", 1, TimeSpan.FromMinutes(2), Now, CancellationToken.None),
            candidate => candidate.Id == failing.Id);
        leased.ReplaceEndpoint("rediscovery-merge-worker", newEndpoint, Now.AddSeconds(1));
        leased.ScheduleRetry(
            "rediscovery-merge-worker",
            Now.AddSeconds(1),
            Now.AddMinutes(1),
            "endpoint_gone",
            "old inbox is gone");
        DeliveryAttempt attempt = DeliveryAttempt.Create(
            leased.Id,
            leased.AttemptCount,
            DeliveryAttemptOutcome.RetryScheduled,
            410,
            "endpoint_gone",
            "old inbox is gone",
            TimeSpan.FromMilliseconds(5),
            Now,
            Now.AddSeconds(1));
        var rediscovery = new EndpointRediscoveryPlan(
            [DeliveryTarget.Create(leased.Id, firstRecipient)],
            [],
            [],
            [DeliveryEndpointChange.Create(leased.Id, oldEndpoint, newEndpoint, 1, Now.AddSeconds(1))]);

        await repository.SaveAttemptAsync(leased, attempt, null, CancellationToken.None, rediscovery);

        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.Equal(newEndpoint, await verification.Deliveries.Where(x => x.Id == failing.Id).Select(x => x.EndpointIri).SingleAsync());
        Assert.Equal(WorkItemState.Pending, await verification.Deliveries.Where(x => x.Id == failing.Id).Select(x => x.State).SingleAsync());
        Assert.Equal(WorkItemState.Cancelled, await verification.Deliveries.Where(x => x.Id == existing.Id).Select(x => x.State).SingleAsync());
        Assert.Equal(
            [firstRecipient, secondRecipient],
            await verification.DeliveryTargets.Where(x => x.DeliveryId == failing.Id)
                .OrderBy(x => x.ActorIri)
                .Select(x => x.ActorIri)
                .ToArrayAsync());
    }

    [Fact]
    public async Task RawJsonPurgeHonorsLegalHoldAndPreservesCanonicalDocuments()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://retention-{suffix}.example/users/alice";
        string objectIri = $"https://retention-{suffix}.example/objects/1";
        string activityIri = $"https://retention-{suffix}.example/activities/1";
        const string canonicalObject = "{\"type\":\"Note\",\"content\":\"preserved canonical content\"}";
        const string auditObject = "{\"type\":\"Note\",\"content\":\"original audit content\",\"bto\":\"https://secret.example/users/bob\"}";
        const string canonicalActivity = "{\"type\":\"Create\"}";
        const string auditActivity = "{\"type\":\"Create\",\"bto\":\"https://secret.example/users/bob\"}";
        FederatedObject item = FederatedObject.Create(
            objectIri,
            actorIri,
            "Note",
            Visibility.Public,
            canonicalObject,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(canonicalObject)),
            Now,
            Now,
            auditObject);
        ActivityRecord activity = ActivityRecord.Create(
            activityIri,
            actorIri,
            "Create",
            objectIri,
            ActivityDirection.Inbound,
            Visibility.Public,
            canonicalActivity,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(canonicalActivity)),
            false,
            Now,
            Now,
            auditActivity);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            setup.Objects.Add(item);
            setup.Activities.Add(activity);
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        IRawJsonRetentionStore retention = scope.ServiceProvider.GetRequiredService<IRawJsonRetentionStore>();
        Guid holdId = await retention.PlaceLegalHoldAsync(
            RawJsonResourceKind.FederatedObject,
            item.Id,
            "litigation fixture",
            "test-operator",
            null,
            CancellationToken.None);
        RawJsonPurgeResult first = await retention.PurgeBatchAsync(
            Now.AddMinutes(1),
            Now.AddMinutes(1),
            Now.AddMinutes(2),
            100,
            CancellationToken.None);

        await using (FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            ActivityRecord persistedActivity = await verification.Activities.SingleAsync(x => x.Id == activity.Id);
            FederatedObject persistedObject = await verification.Objects.SingleAsync(x => x.Id == item.Id);
            Assert.True(first.Activities >= 1);
            Assert.Null(persistedActivity.AuditRawJson);
            Assert.NotNull(persistedActivity.RawJsonPurgedAt);
            using JsonDocument canonicalActivityDocument = JsonDocument.Parse(persistedActivity.RawJson);
            using JsonDocument auditObjectDocument = JsonDocument.Parse(persistedObject.AuditRawJson!);
            using JsonDocument canonicalObjectDocument = JsonDocument.Parse(persistedObject.RawJson);
            Assert.Equal("Create", canonicalActivityDocument.RootElement.GetProperty("type").GetString());
            Assert.True(auditObjectDocument.RootElement.TryGetProperty("bto", out _));
            Assert.False(canonicalObjectDocument.RootElement.TryGetProperty("bto", out _));
            Assert.Equal("preserved canonical content", canonicalObjectDocument.RootElement.GetProperty("content").GetString());
        }

        Assert.True(await retention.ReleaseLegalHoldAsync(holdId, "test-operator", CancellationToken.None));
        RawJsonPurgeResult second = await retention.PurgeBatchAsync(
            Now.AddMinutes(1),
            Now.AddMinutes(1),
            Now.AddMinutes(3),
            100,
            CancellationToken.None);
        await using FederationDbContext finalVerification = await factory.CreateDbContextAsync(CancellationToken.None);
        FederatedObject finalObject = await finalVerification.Objects.SingleAsync(x => x.Id == item.Id);
        Assert.True(second.Objects >= 1);
        Assert.Null(finalObject.AuditRawJson);
        Assert.NotNull(finalObject.RawJsonPurgedAt);
        using JsonDocument finalCanonical = JsonDocument.Parse(finalObject.RawJson);
        Assert.Equal("preserved canonical content", finalCanonical.RootElement.GetProperty("content").GetString());
        Assert.True(await finalVerification.AuditEvents.AnyAsync(x => x.Action == "legal-hold-placed"));
        Assert.True(await finalVerification.AuditEvents.AnyAsync(x => x.Action == "legal-hold-released"));
    }

    [Fact]
    public async Task ExpiredRemoteMediaCacheReleasesAttachmentForDurableGarbageCollection()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://remote-{suffix}.example/users/alice";
        string objectIri = $"https://remote-{suffix}.example/objects/1";
        const string sourceToken = "0123456789abcdef0123456789abcdef";
        var item = FederatedObject.Create(
            objectIri,
            actorIri,
            "Note",
            Visibility.Public,
            "{\"type\":\"Note\"}",
            PayloadDigest.Sha256Hex("{}"u8),
            Now.AddDays(-2),
            Now.AddDays(-2));
        MediaResource media = MediaResource.Create(
            actorIri,
            $"remote/{suffix}",
            PayloadDigest.Sha256Hex("media"u8),
            "image/png",
            "remote.png",
            4,
            Visibility.Public,
            Now.AddDays(-2));
        media.MarkAvailable(
            $"remote/{suffix}",
            PayloadDigest.Sha256Hex("media"u8),
            "image/png",
            4,
            1,
            1,
            null,
            null,
            Now.AddDays(-2));
        RemoteMediaCacheEntry cacheEntry = RemoteMediaCacheEntry.Create(
            item.Id,
            $"https://remote-{suffix}.example/media/1.png",
            sourceToken,
            media.Id,
            null,
            null,
            Now.AddDays(-2),
            Now.AddDays(-1));

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            setup.Objects.Add(item);
            setup.Media.Add(media);
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        IRemoteMediaCacheRepository repository = scope.ServiceProvider.GetRequiredService<IRemoteMediaCacheRepository>();
        await repository.SaveAsync(cacheEntry, CancellationToken.None);
        Assert.Equal(1, await repository.ExpireAsync(Now, 100, CancellationToken.None));

        await using FederationDbContext verification = await factory.CreateDbContextAsync(CancellationToken.None);
        Assert.False(await verification.RemoteMediaCache.AnyAsync(x => x.Id == cacheEntry.Id));
        Assert.False(await verification.MediaAttachments.AnyAsync(x => x.MediaId == media.Id));
        Assert.True(await verification.Media.AnyAsync(x => x.Id == media.Id && x.PurgedAt == null));
    }

    [Fact]
    public async Task RemoteActorMediaCacheValidatesSourceAndSerializesFirstFetchAcrossInstances()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://actor-{suffix}.example/users/alice";
        string sourceIri = $"https://cdn-{suffix}.example/media/avatar.png";
        string sourceToken = RemoteMediaSourceToken.Create(sourceIri);
        string actorJson = JsonSerializer.Serialize(new
        {
            id = actorIri,
            type = "Person",
            icon = new { type = "Image", url = sourceIri }
        });
        var actor = RemoteActor.Create(
            actorIri,
            "Person",
            "alice",
            actorJson,
            Now.AddDays(-31));
        MediaResource media = MediaResource.Create(
            actorIri,
            $"remote-actor/{suffix}",
            PayloadDigest.Sha256Hex("actor-media"u8),
            "image/png",
            "avatar.png",
            11,
            Visibility.Public,
            Now.AddDays(-31));
        media.MarkAvailable(
            $"remote-actor/{suffix}",
            PayloadDigest.Sha256Hex("actor-media"u8),
            "image/png",
            11,
            32,
            32,
            null,
            null,
            Now.AddDays(-31));

        await using AsyncServiceScope setupScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using (FederationDbContext setup = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            setup.RemoteActors.Add(actor);
            setup.Media.Add(media);
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        await using AsyncServiceScope scopeA = fixture.Services.CreateAsyncScope();
        await using AsyncServiceScope scopeB = fixture.Services.CreateAsyncScope();
        IRemoteActorMediaCacheRepository repositoryA = scopeA.ServiceProvider.GetRequiredService<IRemoteActorMediaCacheRepository>();
        IRemoteActorMediaCacheRepository repositoryB = scopeB.ServiceProvider.GetRequiredService<IRemoteActorMediaCacheRepository>();
        RemoteActorMediaSource source = Assert.IsType<RemoteActorMediaSource>(await repositoryA.ResolveSourceAsync(
            actor.Id,
            sourceToken,
            CancellationToken.None));
        string tamperedToken = sourceToken[..^1] + (sourceToken[^1] == '0' ? '1' : '0');
        Assert.Null(await repositoryA.ResolveSourceAsync(actor.Id, tamperedToken, CancellationToken.None));

        Task<RemoteActorMediaCacheClaim?> claimA = repositoryA.ClaimFetchAsync(
            source,
            Now,
            Now.AddMinutes(2),
            CancellationToken.None);
        Task<RemoteActorMediaCacheClaim?> claimB = repositoryB.ClaimFetchAsync(
            source,
            Now,
            Now.AddMinutes(2),
            CancellationToken.None);
        RemoteActorMediaCacheClaim[] claims = (await Task.WhenAll(claimA, claimB))
            .Select(Assert.IsType<RemoteActorMediaCacheClaim>)
            .ToArray();
        RemoteActorMediaCacheClaim acquired = Assert.Single(
            claims,
            claim => claim.State == RemoteActorMediaCacheClaimState.Acquired);
        Assert.Single(claims, claim => claim.State == RemoteActorMediaCacheClaimState.Busy);
        Assert.NotNull(acquired.LeaseOwner);
        Task<bool> renewal = repositoryB.RenewLeaseAsync(
            acquired.EntryId,
            acquired.LeaseOwner,
            Now,
            Now.AddMinutes(3),
            CancellationToken.None);
        Task<bool> completion = repositoryA.CompleteAsync(
            acquired.EntryId,
            acquired.LeaseOwner,
            media.Id,
            "\"remote-etag\"",
            Now.AddHours(-1),
            Now,
            Now.AddDays(1),
            CancellationToken.None);
        _ = await renewal;
        Assert.True(await completion);

        RemoteActorMediaCacheClaim current = Assert.IsType<RemoteActorMediaCacheClaim>(await repositoryB.ReadAsync(
            actor.Id,
            sourceToken,
            Now,
            CancellationToken.None));
        Assert.Equal(RemoteActorMediaCacheClaimState.Fresh, current.State);
        Assert.Equal(media.Id, current.MediaId);
        Assert.Null(current.LeaseOwner);
        await using (FederationDbContext leaseVerification = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            RemoteActorMediaCacheEntry row = await leaseVerification.RemoteActorMediaCache.SingleAsync(
                value => value.Id == acquired.EntryId,
                CancellationToken.None);
            Assert.Null(row.LeaseOwner);
            Assert.Null(row.LeaseExpiresAt);
        }

        IMediaRepository mediaRepository = setupScope.ServiceProvider.GetRequiredService<IMediaRepository>();
        IReadOnlyList<MediaGarbageCandidate> protectedCandidates = await mediaRepository.ClaimGarbageAsync(
            Now.AddDays(-1),
            Now.AddDays(-1),
            Now,
            100,
            CancellationToken.None);
        Assert.DoesNotContain(protectedCandidates, candidate => candidate.Id == media.Id);

        Assert.Equal(1, await repositoryA.ExpireAsync(Now.AddDays(2), 100, CancellationToken.None));
        IReadOnlyList<MediaGarbageCandidate> releasedCandidates = await mediaRepository.ClaimGarbageAsync(
            Now.AddDays(1),
            Now.AddDays(1),
            Now.AddDays(2),
            100,
            CancellationToken.None);
        Assert.Contains(releasedCandidates, candidate => candidate.Id == media.Id);
    }

    private static MediaResource CreateOldMedia(string suffix) => MediaResource.Create(
        $"https://local.example/users/{Guid.NewGuid():N}",
        $"media/{suffix}/original.png",
        new string('d', 64),
        "image/png",
        "announcement.png",
        256,
        Visibility.Public,
        Now.AddDays(-31));

    private async Task<Guid> InsertOutboundAsync(int deliveryCount)
    {
        string suffix = Guid.NewGuid().ToString("N");
        string actorIri = $"https://local.example/users/{suffix}";
        string activityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        byte[] payload = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Create\"}}");
        var activity = ActivityRecord.Create(
            activityIri,
            actorIri,
            "Create",
            null,
            ActivityDirection.Outbound,
            Visibility.Public,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            false,
            Now,
            Now);
        Delivery[] deliveries = Enumerable.Range(0, deliveryCount)
            .Select(index => Delivery.Create(
                activity.Id,
                activity.Iri,
                $"https://remote-{suffix}-{index}.example/inbox",
                actorIri,
                payload,
                SignatureProfile.LegacyCavage,
                Now))
            .ToArray();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDeliveryRepository repository = scope.ServiceProvider.GetRequiredService<IDeliveryRepository>();
        _ = await repository.CommitOutboundAsync(
            new OutboundCommit(activity, null, null, null, null, null, [], deliveries),
            CancellationToken.None);
        return activity.Id;
    }

    private async Task<Guid> InsertOutboundToDomainAsync(string domain)
    {
        string actorIri = $"https://local.example/users/{Guid.NewGuid():N}";
        string activityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        byte[] payload = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Like\"}}");
        var activity = ActivityRecord.Create(
            activityIri,
            actorIri,
            "Like",
            null,
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            false,
            Now,
            Now);
        Delivery delivery = Delivery.Create(
            activity.Id,
            activity.Iri,
            $"https://{domain}/inbox",
            actorIri,
            payload,
            SignatureProfile.LegacyCavage,
            Now);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        _ = await scope.ServiceProvider.GetRequiredService<IDeliveryRepository>().CommitOutboundAsync(
            new OutboundCommit(activity, null, null, null, null, null, [], [delivery]),
            CancellationToken.None);
        return activity.Id;
    }

    private static async Task CommitEmojiAsync(
        IDeliveryRepository repository,
        string actorIri,
        string objectIri,
        string reaction)
    {
        string activityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        byte[] payload = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"EmojiReact\",\"content\":\"{reaction}\"}}");
        ActivityRecord activity = ActivityRecord.Create(
            activityIri,
            actorIri,
            "EmojiReact",
            objectIri,
            ActivityDirection.Outbound,
            Visibility.Public,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            false,
            Now,
            Now);
        EmojiReactionRelation relation = EmojiReactionRelation.Create(
            actorIri,
            objectIri,
            activityIri,
            FederatedReaction.Create(reaction, actorIri),
            Now);
        _ = await repository.CommitOutboundAsync(new OutboundCommit(
            activity,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            EmojiReactionRelation: relation), CancellationToken.None);
    }

    private static VerifiedInboundActivity Inbound(string activityIri, string localIri, byte[] body, string replay) => new(
        activityIri,
        "https://remote.example/users/alice",
        "Create",
        null,
        null,
        "https://remote.example",
        [new AudienceAddress(localIri, AudienceField.To)],
        null,
        body,
        PayloadDigest.Sha256Hex(body),
        SignatureProfile.LegacyCavage,
        "https://remote.example/users/alice#main-key",
        Now,
        replay,
        null,
        Now);

    private static OutboundCommit IdempotentCommit(string key, string requestHash)
    {
        string activityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        byte[] body = Encoding.UTF8.GetBytes($"{{\"id\":\"{activityIri}\",\"type\":\"Like\"}}");
        var activity = ActivityRecord.Create(
            activityIri,
            "https://local.example/users/alice",
            "Like",
            null,
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            Encoding.UTF8.GetString(body),
            PayloadDigest.Sha256Hex(body),
            false,
            Now,
            Now);
        ClientIdempotencyRecord idempotency = ClientIdempotencyRecord.Create(
            "alice",
            key,
            requestHash,
            activity.Iri,
            null,
            body,
            Now,
            Now.AddDays(1));
        return new(activity, null, null, null, null, idempotency, [], []);
    }
}
