using ActivityPub.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenIddict.EntityFrameworkCore.Models;

namespace ActivityPub.Persistence;

public sealed class FederationDbContext(DbContextOptions<FederationDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<LocalActor> LocalActors => Set<LocalActor>();
    public DbSet<RemoteActor> RemoteActors => Set<RemoteActor>();
    public DbSet<ActorKey> ActorKeys => Set<ActorKey>();
    public DbSet<ActivityRecord> Activities => Set<ActivityRecord>();
    public DbSet<ActivityRecipient> ActivityRecipients => Set<ActivityRecipient>();
    public DbSet<InboxItemRecipient> InboxItemRecipients => Set<InboxItemRecipient>();
    public DbSet<FederatedObject> Objects => Set<FederatedObject>();
    public DbSet<ObjectRevision> ObjectRevisions => Set<ObjectRevision>();
    public DbSet<FollowRelation> FollowRelations => Set<FollowRelation>();
    public DbSet<CollectionMembership> CollectionMemberships => Set<CollectionMembership>();
    public DbSet<LikeRelation> LikeRelations => Set<LikeRelation>();
    public DbSet<EmojiReactionRelation> EmojiReactionRelations => Set<EmojiReactionRelation>();
    public DbSet<AnnounceRelation> AnnounceRelations => Set<AnnounceRelation>();
    public DbSet<Relay> Relays => Set<Relay>();
    public DbSet<Hashtag> Hashtags => Set<Hashtag>();
    public DbSet<HashtagUsage> HashtagUsages => Set<HashtagUsage>();
    public DbSet<UrlPreview> UrlPreviews => Set<UrlPreview>();
    public DbSet<DriveFolder> DriveFolders => Set<DriveFolder>();
    public DbSet<QuestionPoll> QuestionPolls => Set<QuestionPoll>();
    public DbSet<PollOption> PollOptions => Set<PollOption>();
    public DbSet<PollVote> PollVotes => Set<PollVote>();
    public DbSet<ActorMove> ActorMoves => Set<ActorMove>();
    public DbSet<InboxItem> InboxItems => Set<InboxItem>();
    public DbSet<InboxConflict> InboxConflicts => Set<InboxConflict>();
    public DbSet<SignatureReplay> SignatureReplays => Set<SignatureReplay>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<DeliveryTarget> DeliveryTargets => Set<DeliveryTarget>();
    public DbSet<DeliveryEndpointChange> DeliveryEndpointChanges => Set<DeliveryEndpointChange>();
    public DbSet<RemoteEndpoint> RemoteEndpoints => Set<RemoteEndpoint>();
    public DbSet<RemoteKeyCache> RemoteKeyCache => Set<RemoteKeyCache>();
    public DbSet<MediaResource> Media => Set<MediaResource>();
    public DbSet<MediaAttachment> MediaAttachments => Set<MediaAttachment>();
    public DbSet<RemoteMediaCacheEntry> RemoteMediaCache => Set<RemoteMediaCacheEntry>();
    public DbSet<RemoteActorMediaCacheEntry> RemoteActorMediaCache => Set<RemoteActorMediaCacheEntry>();
    public DbSet<LegalHold> LegalHolds => Set<LegalHold>();
    public DbSet<UserMute> UserMutes => Set<UserMute>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<ClientIdempotencyRecord> ClientIdempotency => Set<ClientIdempotencyRecord>();
    public DbSet<ExternalEntityId> ExternalEntityIds => Set<ExternalEntityId>();
    public DbSet<MisskeyAuthSession> MisskeyAuthSessions => Set<MisskeyAuthSession>();
    public DbSet<MisskeyAccessToken> MisskeyAccessTokens => Set<MisskeyAccessToken>();
    public DbSet<StreamEvent> StreamEvents => Set<StreamEvent>();
    public DbSet<StreamConnectionLease> StreamConnectionLeases => Set<StreamConnectionLease>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementRead> AnnouncementReads => Set<AnnouncementRead>();
    public DbSet<DomainPolicy> DomainPolicies => Set<DomainPolicy>();
    public DbSet<ActorPolicy> ActorPolicies => Set<ActorPolicy>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<DeadLetter> DeadLetters => Set<DeadLetter>();
    internal DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();
    internal DbSet<SchemaCompatibility> SchemaCompatibility => Set<SchemaCompatibility>();
    internal DbSet<DomainDeliveryLease> DomainDeliveryLeases => Set<DomainDeliveryLease>();
    internal DbSet<RemoteDomainCircuit> RemoteDomainCircuits => Set<RemoteDomainCircuit>();
    internal DbSet<OperationalControl> OperationalControls => Set<OperationalControl>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("activitypub");
        modelBuilder.UseOpenIddict();
        modelBuilder.HasSequence<long>("external_mastodon_id_seq").StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>("external_misskey_id_seq").StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>("stream_event_cursor_seq").StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>("announcement_sort_seq").StartsAt(1).IncrementsBy(1);
        modelBuilder.Entity<DataProtectionKey>(entity =>
        {
            entity.ToTable("data_protection_keys", "activitypub");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.FriendlyName).HasColumnName("friendly_name");
            entity.Property(x => x.Xml).HasColumnName("xml");
        });
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication>().ToTable("oauth_applications");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization>().ToTable("oauth_authorizations");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope>().ToTable("oauth_scopes");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken>().ToTable("oauth_tokens");
        // Inbox and delivery share domain behavior but are separate persistence aggregates.
        // Ignoring the abstract CLR base prevents an accidental shared TPH work table.
        modelBuilder.Ignore<DurableWorkItem>();

        ConfigureLocalActor(modelBuilder.Entity<LocalActor>());
        ConfigureRemoteActor(modelBuilder.Entity<RemoteActor>());
        ConfigureActorKey(modelBuilder.Entity<ActorKey>());
        ConfigureActivity(modelBuilder.Entity<ActivityRecord>());
        ConfigureActivityRecipient(modelBuilder.Entity<ActivityRecipient>());
        ConfigureInboxItemRecipient(modelBuilder.Entity<InboxItemRecipient>());
        ConfigureObject(modelBuilder.Entity<FederatedObject>());
        ConfigureObjectRevision(modelBuilder.Entity<ObjectRevision>());
        ConfigureFollow(modelBuilder.Entity<FollowRelation>());
        ConfigureCollectionMembership(modelBuilder.Entity<CollectionMembership>());
        ConfigureLike(modelBuilder.Entity<LikeRelation>());
        ConfigureEmojiReaction(modelBuilder.Entity<EmojiReactionRelation>());
        ConfigureAnnounce(modelBuilder.Entity<AnnounceRelation>());
        ConfigureRelay(modelBuilder.Entity<Relay>());
        ConfigureHashtag(modelBuilder.Entity<Hashtag>());
        ConfigureHashtagUsage(modelBuilder.Entity<HashtagUsage>());
        ConfigureUrlPreview(modelBuilder.Entity<UrlPreview>());
        ConfigureDriveFolder(modelBuilder.Entity<DriveFolder>());
        ConfigureMediaDriveFields(modelBuilder.Entity<MediaResource>());
        ConfigureQuestionPoll(modelBuilder.Entity<QuestionPoll>());
        ConfigurePollOption(modelBuilder.Entity<PollOption>());
        ConfigurePollVote(modelBuilder.Entity<PollVote>());
        ConfigureActorMove(modelBuilder.Entity<ActorMove>());
        ConfigureInbox(modelBuilder.Entity<InboxItem>());
        ConfigureInboxConflict(modelBuilder.Entity<InboxConflict>());
        ConfigureSignatureReplay(modelBuilder.Entity<SignatureReplay>());
        ConfigureDelivery(modelBuilder.Entity<Delivery>());
        ConfigureDeliveryAttempt(modelBuilder.Entity<DeliveryAttempt>());
        ConfigureDeliveryTarget(modelBuilder.Entity<DeliveryTarget>());
        ConfigureDeliveryEndpointChange(modelBuilder.Entity<DeliveryEndpointChange>());
        ConfigureRemoteEndpoint(modelBuilder.Entity<RemoteEndpoint>());
        ConfigureRemoteKey(modelBuilder.Entity<RemoteKeyCache>());
        ConfigureMedia(modelBuilder.Entity<MediaResource>());
        ConfigureMediaAttachment(modelBuilder.Entity<MediaAttachment>());
        ConfigureRemoteMediaCache(modelBuilder.Entity<RemoteMediaCacheEntry>());
        ConfigureRemoteActorMediaCache(modelBuilder.Entity<RemoteActorMediaCacheEntry>());
        ConfigureLegalHold(modelBuilder.Entity<LegalHold>());
        ConfigureUserMute(modelBuilder.Entity<UserMute>());
        ConfigureUserBlock(modelBuilder.Entity<UserBlock>());
        ConfigureClientIdempotency(modelBuilder.Entity<ClientIdempotencyRecord>());
        ConfigureExternalEntityId(modelBuilder.Entity<ExternalEntityId>());
        ConfigureMisskeyAuthSession(modelBuilder.Entity<MisskeyAuthSession>());
        ConfigureMisskeyAccessToken(modelBuilder.Entity<MisskeyAccessToken>());
        ConfigureStreamEvent(modelBuilder.Entity<StreamEvent>());
        ConfigureStreamConnectionLease(modelBuilder.Entity<StreamConnectionLease>());
        ConfigureUserNotification(modelBuilder.Entity<UserNotification>());
        ConfigureAnnouncement(modelBuilder.Entity<Announcement>());
        ConfigureAnnouncementRead(modelBuilder.Entity<AnnouncementRead>());
        ConfigureDomainPolicy(modelBuilder.Entity<DomainPolicy>());
        ConfigureActorPolicy(modelBuilder.Entity<ActorPolicy>());
        ConfigureModerationAction(modelBuilder.Entity<ModerationAction>());
        ConfigureReport(modelBuilder.Entity<Report>());
        ConfigureAudit(modelBuilder.Entity<AuditEvent>());
        ConfigureDeadLetter(modelBuilder.Entity<DeadLetter>());
        ConfigureWorkerHeartbeat(modelBuilder.Entity<WorkerHeartbeat>());
        ConfigureSchemaCompatibility(modelBuilder.Entity<SchemaCompatibility>());
        ConfigureDomainDeliveryLease(modelBuilder.Entity<DomainDeliveryLease>());
        ConfigureRemoteDomainCircuit(modelBuilder.Entity<RemoteDomainCircuit>());
        ConfigureOperationalControl(modelBuilder.Entity<OperationalControl>());
    }

    private static void ConfigureQuestionPoll(EntityTypeBuilder<QuestionPoll> entity)
    {
        entity.ToTable("question_polls");
        entity.HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.QuestionObjectId).HasColumnName("question_object_id").IsRequired();
        entity.Property(x => x.Multiple).HasColumnName("multiple").IsRequired();
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.BaselineVotersCount).HasColumnName("baseline_voters_count").IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        entity.HasOne<FederatedObject>()
            .WithOne()
            .HasForeignKey<QuestionPoll>(x => x.QuestionObjectId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => x.QuestionObjectId)
            .IsUnique()
            .HasDatabaseName("ux_question_polls_object");
        entity.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_question_polls_expiry");
    }

    private static void ConfigurePollOption(EntityTypeBuilder<PollOption> entity)
    {
        entity.ToTable("poll_options");
        entity.HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.PollId).HasColumnName("poll_id").IsRequired();
        entity.Property(x => x.ChoiceIndex).HasColumnName("choice_index").IsRequired();
        entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(100).IsRequired();
        entity.Property(x => x.BaselineVotesCount).HasColumnName("baseline_votes_count").IsRequired();
        entity.HasOne<QuestionPoll>()
            .WithMany()
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasAlternateKey(x => new { x.PollId, x.ChoiceIndex });
    }

    private static void ConfigurePollVote(EntityTypeBuilder<PollVote> entity)
    {
        entity.ToTable("poll_votes");
        entity.HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.PollId).HasColumnName("poll_id").IsRequired();
        entity.Property(x => x.VoterActorIri).HasColumnName("voter_actor_iri").HasMaxLength(2_048).IsRequired();
        entity.Property(x => x.ChoiceIndex).HasColumnName("choice_index").IsRequired();
        entity.Property(x => x.BallotKey).HasColumnName("ballot_key").IsRequired();
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048).IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        entity.HasOne<QuestionPoll>()
            .WithMany()
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PollOption>()
            .WithMany()
            .HasForeignKey(x => new { x.PollId, x.ChoiceIndex })
            .HasPrincipalKey(x => new { x.PollId, x.ChoiceIndex })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.PollId, x.VoterActorIri, x.BallotKey })
            .IsUnique()
            .HasDatabaseName("ux_poll_votes_ballot");
        entity.HasIndex(x => x.ActivityIri)
            .IsUnique()
            .HasDatabaseName("ux_poll_votes_activity");
        entity.HasIndex(x => new { x.PollId, x.ChoiceIndex, x.CreatedAt })
            .HasDatabaseName("ix_poll_votes_poll_choice_created");
    }

    private static void ConfigureStreamConnectionLease(EntityTypeBuilder<StreamConnectionLease> entity)
    {
        entity.ToTable("stream_connection_leases");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(2_048);
        entity.Property(x => x.RemoteAddress).HasColumnName("remote_address").HasMaxLength(128).IsRequired();
        entity.Property(x => x.InstanceId).HasColumnName("instance_id").HasMaxLength(512).IsRequired();
        entity.Property(x => x.AcquiredAt).HasColumnName("acquired_at").IsRequired();
        entity.Property(x => x.LastHeartbeatAt).HasColumnName("last_heartbeat_at").IsRequired();
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        entity.HasIndex(x => new { x.Subject, x.ExpiresAt }).HasDatabaseName("ix_stream_connection_leases_subject_expiry");
        entity.HasIndex(x => new { x.RemoteAddress, x.ExpiresAt }).HasDatabaseName("ix_stream_connection_leases_address_expiry");
        entity.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_stream_connection_leases_expiry");
    }

    private static void ConfigureUserNotification(EntityTypeBuilder<UserNotification> entity)
    {
        entity.ToTable("user_notifications");
        entity.HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.RecipientActorIri).HasColumnName("recipient_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.SourceActorIri).HasColumnName("source_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(64);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.ObjectIri).HasColumnName("object_iri").HasMaxLength(2_048);
        entity.Property(x => x.Reaction).HasColumnName("reaction").HasMaxLength(512);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ReadAt).HasColumnName("read_at");
        entity.Property(x => x.DismissedAt).HasColumnName("dismissed_at");
        entity.HasIndex(x => new { x.RecipientActorIri, x.CreatedAt, x.Id })
            .HasDatabaseName("ix_user_notifications_recipient_created");
        entity.HasIndex(x => new { x.RecipientActorIri, x.ReadAt, x.DismissedAt })
            .HasDatabaseName("ix_user_notifications_recipient_state");
        entity.HasIndex(x => new { x.RecipientActorIri, x.ActivityIri, x.Kind })
            .IsUnique()
            .HasFilter("activity_iri IS NOT NULL")
            .HasDatabaseName("ux_user_notifications_activity_kind");
    }

    private static void ConfigureAnnouncement(EntityTypeBuilder<Announcement> entity)
    {
        entity.ToTable("announcements");
        entity.HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.SortOrdinal)
            .HasColumnName("sort_ordinal")
            .HasDefaultValueSql("nextval('activitypub.announcement_sort_seq')")
            .ValueGeneratedOnAdd();
        entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
        entity.Property(x => x.Text).HasColumnName("text").HasMaxLength(64_000).IsRequired();
        entity.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(2_048);
        entity.Property(x => x.Audience).HasColumnName("audience").HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.PublishedAt).HasColumnName("published_at").IsRequired();
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(256).IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        entity.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(256);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.DeletedBy).HasColumnName("deleted_by").HasMaxLength(256);
        entity.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        entity.HasIndex(x => x.SortOrdinal).IsUnique().HasDatabaseName("ux_announcements_sort_ordinal");
        entity.HasIndex(x => new { x.Audience, x.SortOrdinal })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_announcements_active_audience_order");
        entity.HasIndex(x => new { x.PublishedAt, x.ExpiresAt })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_announcements_active_publication_window");
    }

    private static void ConfigureAnnouncementRead(EntityTypeBuilder<AnnouncementRead> entity)
    {
        entity.ToTable("announcement_reads");
        entity.HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.AnnouncementId).HasColumnName("announcement_id").IsRequired();
        entity.Property(x => x.ReaderActorIri).HasColumnName("reader_actor_iri").HasMaxLength(2_048).IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        entity.HasOne<Announcement>()
            .WithMany()
            .HasForeignKey(x => x.AnnouncementId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.AnnouncementId, x.ReaderActorIri })
            .IsUnique()
            .HasDatabaseName("ux_announcement_reads_announcement_actor");
        entity.HasIndex(x => new { x.ReaderActorIri, x.CreatedAt })
            .HasDatabaseName("ix_announcement_reads_actor_created");
    }

    private static void ConfigureExternalEntityId(EntityTypeBuilder<ExternalEntityId> entity)
    {
        entity.ToTable("external_entity_ids").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Dialect).HasColumnName("dialect").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.EntityType).HasColumnName("entity_type").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.InternalId).HasColumnName("internal_id");
        entity.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(128);
        entity.Property(x => x.SortOrdinal).HasColumnName("sort_ordinal");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.RetiredAt).HasColumnName("retired_at");
        entity.HasIndex(x => new { x.Dialect, x.EntityType, x.ExternalId }).IsUnique()
            .HasDatabaseName("ux_external_entity_ids_dialect_type_external");
        entity.HasIndex(x => new { x.Dialect, x.EntityType, x.InternalId }).IsUnique()
            .HasDatabaseName("ux_external_entity_ids_dialect_type_internal");
        entity.HasIndex(x => new { x.Dialect, x.EntityType, x.SortOrdinal })
            .HasDatabaseName("ix_external_entity_ids_dialect_type_sort");
    }

    private static void ConfigureMisskeyAuthSession(EntityTypeBuilder<MisskeyAuthSession> entity)
    {
        entity.ToTable("misskey_auth_sessions").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.SessionKey).HasColumnName("session_key").HasMaxLength(64);
        entity.Property(x => x.ClientName).HasColumnName("client_name").HasMaxLength(200);
        entity.Property(x => x.ClientIconUri).HasColumnName("client_icon_uri").HasMaxLength(2_048);
        entity.Property(x => x.ClientUri).HasColumnName("client_uri").HasMaxLength(2_048);
        entity.Property(x => x.CallbackUri).HasColumnName("callback_uri").HasMaxLength(2_048);
        entity.Property(x => x.Permissions).HasColumnName("permissions").HasMaxLength(2_000);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.DecidedAt).HasColumnName("decided_at");
        entity.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        entity.Property(x => x.IssuedTokenId).HasColumnName("issued_token_id");
        entity.Property(x => x.EncryptedToken).HasColumnName("encrypted_token").HasColumnType("text");
        entity.HasIndex(x => x.SessionKey).IsUnique().HasDatabaseName("ux_misskey_auth_sessions_key");
        entity.HasIndex(x => new { x.State, x.ExpiresAt }).HasDatabaseName("ix_misskey_auth_sessions_state_expiry");
        entity.HasIndex(x => x.IssuedTokenId).IsUnique().HasFilter("issued_token_id IS NOT NULL")
            .HasDatabaseName("ux_misskey_auth_sessions_token");
    }

    private static void ConfigureMisskeyAccessToken(EntityTypeBuilder<MisskeyAccessToken> entity)
    {
        entity.ToTable("misskey_access_tokens").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(2_000);
        entity.Property(x => x.IconUri).HasColumnName("icon_uri").HasMaxLength(2_048);
        entity.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        entity.Property(x => x.Permissions).HasColumnName("permissions").HasMaxLength(2_000);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        entity.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
        entity.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_misskey_access_tokens_hash");
        entity.HasIndex(x => x.SourceSessionId).IsUnique().HasDatabaseName("ux_misskey_access_tokens_session");
        entity.HasIndex(x => new { x.ActorIri, x.RevokedAt, x.ExpiresAt })
            .HasDatabaseName("ix_misskey_access_tokens_actor_state");
        entity.HasOne<MisskeyAuthSession>().WithOne().HasForeignKey<MisskeyAccessToken>(x => x.SourceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureStreamEvent(EntityTypeBuilder<StreamEvent> entity)
    {
        entity.ToTable("stream_events").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Cursor).HasColumnName("cursor")
            .HasDefaultValueSql("nextval('activitypub.stream_event_cursor_seq')")
            .ValueGeneratedOnAdd();
        entity.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(2_048);
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(64);
        entity.Property(x => x.ResourceId).HasColumnName("resource_id");
        entity.Property(x => x.ResourceIri).HasColumnName("resource_iri").HasMaxLength(2_048);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.IsLocal).HasColumnName("is_local");
        entity.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        entity.Property(x => x.Reaction).HasColumnName("reaction").HasMaxLength(512);
        entity.Property(x => x.ReactionRemoved).HasColumnName("reaction_removed");
        entity.Property(x => x.PollChoiceIndex).HasColumnName("poll_choice_index");
        entity.Property(x => x.RecipientActorIri).HasColumnName("recipient_actor_iri").HasMaxLength(2_048);
        entity.HasIndex(x => x.Cursor).IsUnique().HasDatabaseName("ux_stream_events_cursor");
        entity.HasIndex(x => x.DeduplicationKey).IsUnique().HasDatabaseName("ux_stream_events_deduplication");
        entity.HasIndex(x => new { x.Kind, x.Cursor }).HasDatabaseName("ix_stream_events_kind_cursor");
        entity.HasIndex(x => new { x.ActorIri, x.Cursor }).HasDatabaseName("ix_stream_events_actor_cursor");
    }

    private static void ConfigureInboxItemRecipient(EntityTypeBuilder<InboxItemRecipient> entity)
    {
        entity.ToTable("inbox_item_recipients").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.InboxItemId).HasColumnName("inbox_item_id");
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.HasIndex(x => new { x.InboxItemId, x.ActorIri }).IsUnique()
            .HasDatabaseName("ux_inbox_item_recipients_item_actor");
        entity.HasIndex(x => x.ActorIri).HasDatabaseName("ix_inbox_item_recipients_actor");
        entity.HasOne<InboxItem>().WithMany().HasForeignKey(x => x.InboxItemId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLocalActor(EntityTypeBuilder<LocalActor> entity)
    {
        entity.ToTable("local_actors").HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.Iri).HasColumnName("iri").HasMaxLength(2_048);
        entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(64);
        entity.Property(x => x.NormalizedUsername).HasColumnName("normalized_username").HasMaxLength(64);
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        entity.Property(x => x.SummaryHtml).HasColumnName("summary_html").HasColumnType("text");
        entity.Property(x => x.ManuallyApprovesFollowers).HasColumnName("manually_approves_followers");
        entity.Property(x => x.Discoverable).HasColumnName("discoverable");
        entity.Property(x => x.Indexable).HasColumnName("indexable");
        entity.Property(x => x.IsSuspended).HasColumnName("is_suspended");
        entity.Property(x => x.ActiveKeyId).HasColumnName("active_key_id");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        entity.HasIndex(x => x.Iri).IsUnique().HasDatabaseName("ux_local_actors_iri");
        entity.HasIndex(x => x.NormalizedUsername).IsUnique().HasDatabaseName("ux_local_actors_username");
        entity.HasOne<ActorKey>().WithMany().HasForeignKey(x => x.ActiveKeyId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRemoteActor(EntityTypeBuilder<RemoteActor> entity)
    {
        entity.ToTable("remote_actors").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Iri).HasColumnName("iri").HasMaxLength(2_048);
        entity.Property(x => x.Origin).HasColumnName("origin").HasMaxLength(512);
        entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(128);
        entity.Property(x => x.PreferredUsername).HasColumnName("preferred_username").HasMaxLength(256);
        entity.Property(x => x.RawJson).HasColumnName("raw_json").HasColumnType("jsonb");
        entity.Property(x => x.ETag).HasColumnName("etag").HasMaxLength(512);
        entity.Property(x => x.LastModified).HasColumnName("last_modified");
        entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.GoneAt).HasColumnName("gone_at");
        entity.HasIndex(x => x.Iri).IsUnique().HasDatabaseName("ux_remote_actors_iri");
        entity.HasIndex(x => x.Origin).HasDatabaseName("ix_remote_actors_origin");
    }

    private static void ConfigureActorKey(EntityTypeBuilder<ActorKey> entity)
    {
        entity.ToTable("actor_keys").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.KeyIri).HasColumnName("key_iri").HasMaxLength(2_048);
        entity.Property(x => x.OwnerIri).HasColumnName("owner_iri").HasMaxLength(2_048);
        entity.Property(x => x.PublicKeyPem).HasColumnName("public_key_pem").HasColumnType("text");
        entity.Property(x => x.Algorithm).HasColumnName("algorithm").HasMaxLength(128);
        entity.Property(x => x.IsLocal).HasColumnName("is_local");
        entity.Property(x => x.PrivateKeyHandle).HasColumnName("private_key_handle").HasMaxLength(2_048);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ActivatedAt).HasColumnName("activated_at");
        entity.Property(x => x.RetiredAt).HasColumnName("retired_at");
        entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.HasIndex(x => x.KeyIri).IsUnique().HasDatabaseName("ux_actor_keys_key_iri");
        entity.HasIndex(x => new { x.OwnerIri, x.State }).HasDatabaseName("ix_actor_keys_owner_state");
    }

    private static void ConfigureActivity(EntityTypeBuilder<ActivityRecord> entity)
    {
        entity.ToTable("activities").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Iri).HasColumnName("iri").HasMaxLength(2_048);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(256);
        entity.Property(x => x.ObjectIri).HasColumnName("object_iri").HasMaxLength(2_048);
        entity.Property(x => x.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.RawJson).HasColumnName("raw_json").HasColumnType("jsonb");
        entity.Property(x => x.AuditRawJson).HasColumnName("audit_raw_json").HasColumnType("jsonb");
        entity.Property(x => x.RawJsonPurgedAt).HasColumnName("raw_json_purged_at");
        entity.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128);
        entity.Property(x => x.IsTransient).HasColumnName("is_transient");
        entity.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        entity.Property(x => x.ReceivedAt).HasColumnName("received_at");
        entity.HasIndex(x => x.Iri).IsUnique().HasDatabaseName("ux_activities_iri");
        entity.HasIndex(x => new { x.ActorIri, x.OccurredAt }).HasDatabaseName("ix_activities_actor_occurred");
        entity.HasIndex(x => x.ObjectIri).HasDatabaseName("ix_activities_object_iri");
    }

    private static void ConfigureActivityRecipient(EntityTypeBuilder<ActivityRecipient> entity)
    {
        entity.ToTable("activity_recipients").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ActivityId).HasColumnName("activity_id");
        entity.Property(x => x.RecipientIri).HasColumnName("recipient_iri").HasMaxLength(2_048);
        entity.Property(x => x.Field).HasColumnName("field").HasConversion<string>().HasMaxLength(16);
        entity.HasIndex(x => new { x.ActivityId, x.RecipientIri, x.Field }).IsUnique().HasDatabaseName("ux_activity_recipients");
        entity.HasIndex(x => x.RecipientIri).HasDatabaseName("ix_activity_recipients_recipient");
        entity.HasOne<ActivityRecord>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureObject(EntityTypeBuilder<FederatedObject> entity)
    {
        entity.ToTable("objects").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Iri).HasColumnName("iri").HasMaxLength(2_048);
        entity.Property(x => x.OwnerIri).HasColumnName("owner_iri").HasMaxLength(2_048);
        entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(256);
        entity.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.RawJson).HasColumnName("raw_json").HasColumnType("jsonb");
        entity.Property(x => x.AuditRawJson).HasColumnName("audit_raw_json").HasColumnType("jsonb");
        entity.Property(x => x.RawJsonPurgedAt).HasColumnName("raw_json_purged_at");
        entity.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128);
        entity.Property(x => x.IsDeleted).HasColumnName("is_deleted");
        entity.Property(x => x.PublishedAt).HasColumnName("published_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        entity.HasIndex(x => x.Iri).IsUnique().HasDatabaseName("ux_objects_iri");
        entity.HasIndex(x => new { x.OwnerIri, x.PublishedAt }).HasDatabaseName("ix_objects_owner_published");
    }

    private static void ConfigureObjectRevision(EntityTypeBuilder<ObjectRevision> entity)
    {
        entity.ToTable("object_revisions").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ObjectId).HasColumnName("object_id");
        entity.Property(x => x.Version).HasColumnName("version");
        entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(256);
        entity.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.RawJson).HasColumnName("raw_json").HasColumnType("jsonb");
        entity.Property(x => x.AuditRawJson).HasColumnName("audit_raw_json").HasColumnType("jsonb");
        entity.Property(x => x.RawJsonPurgedAt).HasColumnName("raw_json_purged_at");
        entity.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128);
        entity.Property(x => x.CapturedAt).HasColumnName("captured_at");
        entity.HasIndex(x => new { x.ObjectId, x.Version }).IsUnique().HasDatabaseName("ux_object_revisions_version");
        entity.HasOne<FederatedObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureFollow(EntityTypeBuilder<FollowRelation> entity)
    {
        entity.ToTable("follow_relations").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.FollowerIri).HasColumnName("follower_iri").HasMaxLength(2_048);
        entity.Property(x => x.FollowedIri).HasColumnName("followed_iri").HasMaxLength(2_048);
        entity.Property(x => x.FollowActivityIri).HasColumnName("follow_activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.DecisionActivityIri).HasColumnName("decision_activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => new { x.FollowerIri, x.FollowedIri }).IsUnique().HasDatabaseName("ux_follow_relations_pair");
        entity.HasIndex(x => x.FollowActivityIri).IsUnique().HasDatabaseName("ux_follow_relations_activity");
    }

    private static void ConfigureCollectionMembership(EntityTypeBuilder<CollectionMembership> entity)
    {
        entity.ToTable("collection_memberships").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.CollectionIri).HasColumnName("collection_iri").HasMaxLength(2_048);
        entity.Property(x => x.ObjectIri).HasColumnName("object_iri").HasMaxLength(2_048);
        entity.Property(x => x.AddActivityIri).HasColumnName("add_activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.RemoveActivityIri).HasColumnName("remove_activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => x.AddActivityIri).IsUnique().HasDatabaseName("ux_collection_memberships_add_activity");
        entity.HasIndex(x => x.RemoveActivityIri).IsUnique().HasFilter("remove_activity_iri IS NOT NULL")
            .HasDatabaseName("ux_collection_memberships_remove_activity");
        entity.HasIndex(x => new { x.CollectionIri, x.ObjectIri }).IsUnique().HasFilter("state = 'Active'")
            .HasDatabaseName("ux_collection_memberships_active_item");
    }

    private static void ConfigureLike(EntityTypeBuilder<LikeRelation> entity)
    {
        entity.ToTable("like_relations").HasKey(x => x.Id);
        ConfigureId(entity);
        ConfigureLikeProperties(entity);
        entity.HasIndex(x => x.ActivityIri).IsUnique().HasDatabaseName("ux_like_relations_activity");
        entity.HasIndex(x => new { x.ActorIri, x.ObjectIri }).IsUnique().HasFilter("state = 'Active'")
            .HasDatabaseName("ux_like_relations_active_pair");
    }

    private static void ConfigureAnnounce(EntityTypeBuilder<AnnounceRelation> entity)
    {
        entity.ToTable("announce_relations").HasKey(x => x.Id);
        ConfigureId(entity);
        ConfigureAnnounceProperties(entity);
        entity.HasIndex(x => x.ActivityIri).IsUnique().HasDatabaseName("ux_announce_relations_activity");
        entity.HasIndex(x => new { x.ActorIri, x.ObjectIri }).IsUnique().HasFilter("state = 'Active'")
            .HasDatabaseName("ux_announce_relations_active_pair");
    }

    private static void ConfigureRelay(EntityTypeBuilder<Relay> entity)
    {
        entity.ToTable("relays").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Inbox).HasColumnName("inbox").HasMaxLength(2_048);
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(x => x.Inbox).IsUnique().HasDatabaseName("ux_relays_inbox");
    }

    private static void ConfigureHashtag(EntityTypeBuilder<Hashtag> entity)
    {
        entity.ToTable("hashtags").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
        entity.Property(x => x.Count).HasColumnName("count");
        entity.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        entity.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_hashtags_name");
        entity.HasIndex(x => new { x.Name, x.Count }).HasDatabaseName("ix_hashtags_name_count");
    }

    private static void ConfigureHashtagUsage(EntityTypeBuilder<HashtagUsage> entity)
    {
        entity.ToTable("hashtag_usages").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
        entity.Property(x => x.OwnerIri).HasColumnName("owner_iri").HasMaxLength(2_048);
        entity.Property(x => x.UsedAt).HasColumnName("used_at");
        entity.HasIndex(x => new { x.Name, x.UsedAt }).HasDatabaseName("ix_hashtag_usages_name_used_at");
    }

    private static void ConfigureUrlPreview(EntityTypeBuilder<UrlPreview> entity)
    {
        entity.ToTable("url_previews").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(2_048);
        entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(1_024);
        entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(2_048);
        entity.Property(x => x.Thumbnail).HasColumnName("thumbnail").HasMaxLength(2_048);
        entity.Property(x => x.Icon).HasColumnName("icon").HasMaxLength(2_048);
        entity.Property(x => x.SiteName).HasColumnName("site_name").HasMaxLength(256);
        entity.Property(x => x.PlayerUrl).HasColumnName("player_url").HasMaxLength(2_048);
        entity.Property(x => x.PlayerWidth).HasColumnName("player_width");
        entity.Property(x => x.PlayerHeight).HasColumnName("player_height");
        entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.HasIndex(x => x.Url).IsUnique().HasDatabaseName("ux_url_previews_url");
    }

    private static void ConfigureDriveFolder(EntityTypeBuilder<DriveFolder> entity)
    {
        entity.ToTable("drive_folders").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.OwnerActorIri).HasColumnName("owner_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(256);
        entity.Property(x => x.ParentId).HasColumnName("parent_id");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => new { x.OwnerActorIri, x.ParentId }).HasDatabaseName("ix_drive_folders_owner_parent");
    }

    private static void ConfigureMediaDriveFields(EntityTypeBuilder<MediaResource> entity)
    {
        entity.Property(x => x.FolderId).HasColumnName("folder_id");
        entity.Property(x => x.IsSensitive).HasColumnName("is_sensitive");
        entity.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(512);
        entity.HasIndex(x => new { x.OwnerActorIri, x.FolderId }).HasDatabaseName("ix_media_owner_folder");
    }

    private static void ConfigureEmojiReaction(EntityTypeBuilder<EmojiReactionRelation> entity)
    {
        entity.ToTable("emoji_reaction_relations").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.ObjectIri).HasColumnName("object_iri").HasMaxLength(2_048);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.Reaction).HasColumnName("reaction").HasMaxLength(256);
        entity.Property(x => x.CustomEmojiIri).HasColumnName("custom_emoji_iri").HasMaxLength(2_048);
        entity.Property(x => x.CustomEmojiName).HasColumnName("custom_emoji_name").HasMaxLength(68);
        entity.Property(x => x.CustomEmojiUrl).HasColumnName("custom_emoji_url").HasMaxLength(2_048);
        entity.Property(x => x.CustomEmojiMediaType).HasColumnName("custom_emoji_media_type").HasMaxLength(128);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => x.ActivityIri).IsUnique().HasDatabaseName("ux_emoji_reaction_relations_activity");
        entity.HasIndex(x => new { x.ActorIri, x.ObjectIri, x.Reaction }).IsUnique().HasFilter("state = 'Active'")
            .HasDatabaseName("ux_emoji_reaction_relations_active_reaction");
        entity.HasIndex(x => new { x.ObjectIri, x.State }).HasDatabaseName("ix_emoji_reaction_relations_object_state");
    }

    private static void ConfigureLikeProperties(EntityTypeBuilder<LikeRelation> entity)
    {
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.ObjectIri).HasColumnName("object_iri").HasMaxLength(2_048);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.Reaction).HasColumnName("reaction").HasMaxLength(256).IsRequired(false);
        entity.Ignore(x => x.EffectiveReaction);
        entity.Property(x => x.CustomEmojiIri).HasColumnName("custom_emoji_iri").HasMaxLength(2_048);
        entity.Property(x => x.CustomEmojiName).HasColumnName("custom_emoji_name").HasMaxLength(68);
        entity.Property(x => x.CustomEmojiUrl).HasColumnName("custom_emoji_url").HasMaxLength(2_048);
        entity.Property(x => x.CustomEmojiMediaType).HasColumnName("custom_emoji_media_type").HasMaxLength(128);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }

    private static void ConfigureAnnounceProperties(EntityTypeBuilder<AnnounceRelation> entity)
    {
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.ObjectIri).HasColumnName("object_iri").HasMaxLength(2_048);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }

    private static void ConfigureActorMove(EntityTypeBuilder<ActorMove> entity)
    {
        entity.ToTable("actor_moves").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.TargetActorIri).HasColumnName("target_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => x.ActivityIri).IsUnique().HasDatabaseName("ux_actor_moves_activity");
        entity.HasIndex(x => x.ActorIri).IsUnique().HasFilter("state = 'Active'")
            .HasDatabaseName("ux_actor_moves_active_actor");
    }

    private static void ConfigureInbox(EntityTypeBuilder<InboxItem> entity)
    {
        entity.ToTable("inbox_items").HasKey(x => x.Id);
        ConfigureWorkItem(entity);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.ActivityType).HasColumnName("activity_type").HasMaxLength(256);
        entity.Property(x => x.RawBody).HasColumnName("raw_body").HasColumnType("bytea");
        entity.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128);
        entity.Property(x => x.SignatureProfile).HasColumnName("signature_profile").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.KeyIri).HasColumnName("key_iri").HasMaxLength(2_048);
        entity.Property(x => x.SignatureCreatedAt).HasColumnName("signature_created_at");
        entity.Property(x => x.IsQuarantined).HasColumnName("is_quarantined");
        entity.Property(x => x.QuarantineReason).HasColumnName("quarantine_reason").HasMaxLength(4_096);
        entity.HasIndex(x => x.ActivityIri).IsUnique().HasDatabaseName("ux_inbox_items_activity_iri");
        entity.HasIndex(x => new { x.State, x.AvailableAt }).HasDatabaseName("ix_inbox_items_claim");
        entity.HasIndex(x => x.LeaseExpiresAt).HasDatabaseName("ix_inbox_items_lease_expiry");
    }

    private static void ConfigureInboxConflict(EntityTypeBuilder<InboxConflict> entity)
    {
        entity.ToTable("inbox_conflicts").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.ExistingPayloadHash).HasColumnName("existing_payload_hash").HasMaxLength(128);
        entity.Property(x => x.IncomingPayloadHash).HasColumnName("incoming_payload_hash").HasMaxLength(128);
        entity.Property(x => x.IncomingBody).HasColumnName("incoming_body").HasColumnType("bytea");
        entity.Property(x => x.DetectedAt).HasColumnName("detected_at");
        entity.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        entity.Property(x => x.ReviewedBy).HasColumnName("reviewed_by").HasMaxLength(256);
        entity.HasIndex(x => new { x.ActivityIri, x.IncomingPayloadHash }).IsUnique().HasDatabaseName("ux_inbox_conflicts_payload");
    }

    private static void ConfigureDelivery(EntityTypeBuilder<Delivery> entity)
    {
        entity.ToTable("deliveries").HasKey(x => x.Id);
        ConfigureWorkItem(entity);
        entity.Property(x => x.ActivityId).HasColumnName("activity_id");
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.EndpointIri).HasColumnName("endpoint_iri").HasMaxLength(2_048);
        entity.Property(x => x.RemoteDomain).HasColumnName("remote_domain").HasMaxLength(255);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("bytea");
        entity.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128);
        entity.Property(x => x.SignatureProfile).HasColumnName("signature_profile").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.LastStatusCode).HasColumnName("last_status_code");
        entity.Property(x => x.EndpointRediscoveryAt).HasColumnName("endpoint_rediscovery_at");
        entity.HasIndex(x => new { x.ActivityId, x.EndpointIri })
            .IsUnique()
            .HasFilter("state IN ('Pending', 'Leased')")
            .IsCreatedConcurrently()
            .HasDatabaseName("ux_deliveries_activity_endpoint");
        entity.HasIndex(x => new { x.State, x.AvailableAt }).HasDatabaseName("ix_deliveries_claim");
        entity.HasIndex(x => new { x.RemoteDomain, x.State, x.AvailableAt }).HasDatabaseName("ix_deliveries_domain_claim");
        entity.HasIndex(x => x.LeaseExpiresAt).HasDatabaseName("ix_deliveries_lease_expiry");
        entity.HasOne<ActivityRecord>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDeliveryTarget(EntityTypeBuilder<DeliveryTarget> entity)
    {
        entity.ToTable("delivery_targets").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.DeliveryId).HasColumnName("delivery_id");
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.HasIndex(x => new { x.DeliveryId, x.ActorIri }).IsUnique()
            .HasDatabaseName("ux_delivery_targets_delivery_actor");
        entity.HasIndex(x => x.ActorIri).HasDatabaseName("ix_delivery_targets_actor");
        entity.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureDeliveryEndpointChange(EntityTypeBuilder<DeliveryEndpointChange> entity)
    {
        entity.ToTable("delivery_endpoint_changes").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.DeliveryId).HasColumnName("delivery_id");
        entity.Property(x => x.PreviousEndpointIri).HasColumnName("previous_endpoint_iri").HasMaxLength(2_048);
        entity.Property(x => x.ReplacementEndpointIri).HasColumnName("replacement_endpoint_iri").HasMaxLength(2_048);
        entity.Property(x => x.RecipientCount).HasColumnName("recipient_count");
        entity.Property(x => x.DiscoveredAt).HasColumnName("discovered_at");
        entity.HasIndex(x => new { x.DeliveryId, x.DiscoveredAt }).HasDatabaseName("ix_delivery_endpoint_changes_delivery");
        entity.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureSignatureReplay(EntityTypeBuilder<SignatureReplay> entity)
    {
        entity.ToTable("signature_replays").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(128);
        entity.Property(x => x.NonceHash).HasColumnName("nonce_hash").HasMaxLength(128);
        entity.Property(x => x.KeyIri).HasColumnName("key_iri").HasMaxLength(2_048);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.ReceivedAt).HasColumnName("received_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.HasIndex(x => x.Fingerprint).IsUnique().HasDatabaseName("ux_signature_replays_fingerprint");
        entity.HasIndex(x => new { x.KeyIri, x.NonceHash }).IsUnique().HasFilter("nonce_hash IS NOT NULL").HasDatabaseName("ux_signature_replays_nonce");
        entity.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_signature_replays_expiry");
    }

    private static void ConfigureDeliveryAttempt(EntityTypeBuilder<DeliveryAttempt> entity)
    {
        entity.ToTable("delivery_attempts").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.DeliveryId).HasColumnName("delivery_id");
        entity.Property(x => x.AttemptNumber).HasColumnName("attempt_number");
        entity.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.StatusCode).HasColumnName("status_code");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        entity.Property(x => x.Error).HasColumnName("error").HasMaxLength(4_096);
        entity.Property(x => x.DurationMilliseconds).HasColumnName("duration_milliseconds");
        entity.Property(x => x.StartedAt).HasColumnName("started_at");
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
        entity.HasIndex(x => new { x.DeliveryId, x.AttemptNumber }).IsUnique().HasDatabaseName("ux_delivery_attempts_number");
        entity.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRemoteEndpoint(EntityTypeBuilder<RemoteEndpoint> entity)
    {
        entity.ToTable("remote_endpoints").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.EndpointIri).HasColumnName("endpoint_iri").HasMaxLength(2_048);
        entity.Property(x => x.RemoteDomain).HasColumnName("remote_domain").HasMaxLength(255);
        entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.GoneAt).HasColumnName("gone_at");
        entity.HasIndex(x => new { x.ActorIri, x.Kind }).IsUnique().HasDatabaseName("ux_remote_endpoints_actor_kind");
        entity.HasIndex(x => x.EndpointIri).HasDatabaseName("ix_remote_endpoints_iri");
    }

    private static void ConfigureRemoteKey(EntityTypeBuilder<RemoteKeyCache> entity)
    {
        entity.ToTable("remote_key_cache").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.KeyIri).HasColumnName("key_iri").HasMaxLength(2_048);
        entity.Property(x => x.OwnerIri).HasColumnName("owner_iri").HasMaxLength(2_048);
        entity.Property(x => x.PublicKeyPem).HasColumnName("public_key_pem").HasColumnType("text");
        entity.Property(x => x.Algorithm).HasColumnName("algorithm").HasMaxLength(128);
        entity.Property(x => x.SourceDocumentHash).HasColumnName("source_document_hash").HasMaxLength(128);
        entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.RefreshBlockedUntil).HasColumnName("refresh_blocked_until");
        entity.HasIndex(x => x.KeyIri).IsUnique().HasDatabaseName("ux_remote_key_cache_key_iri");
        entity.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_remote_key_cache_expiry");
    }

    private static void ConfigureMedia(EntityTypeBuilder<MediaResource> entity)
    {
        entity.ToTable("media").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.OwnerActorIri).HasColumnName("owner_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(1_024);
        entity.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(128);
        entity.Property(x => x.DetectedMediaType).HasColumnName("detected_media_type").HasMaxLength(256);
        entity.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(512);
        entity.Property(x => x.Length).HasColumnName("length");
        entity.Property(x => x.Width).HasColumnName("width");
        entity.Property(x => x.Height).HasColumnName("height");
        entity.Property(x => x.DurationMilliseconds).HasColumnName("duration_milliseconds");
        entity.Property(x => x.ThumbnailStorageKey).HasColumnName("thumbnail_storage_key").HasMaxLength(1_024);
        entity.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.QuarantineReason).HasColumnName("quarantine_reason").HasMaxLength(2_000);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        entity.Property(x => x.PurgedAt).HasColumnName("purged_at");
        entity.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("ux_media_storage_key");
        entity.HasIndex(x => x.ContentHash).HasDatabaseName("ix_media_content_hash");
        entity.HasIndex(x => new { x.State, x.PurgedAt, x.UpdatedAt })
            .HasDatabaseName("ix_media_gc")
            .IsCreatedConcurrently();
    }

    private static void ConfigureDomainPolicy(EntityTypeBuilder<DomainPolicy> entity)
    {
        entity.ToTable("domain_policies").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(255);
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2_000);
        entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(256);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        entity.Property(x => x.RevokedBy).HasColumnName("revoked_by").HasMaxLength(256);
        entity.HasIndex(x => new { x.Domain, x.Kind }).HasDatabaseName("ix_domain_policies_domain_kind");
    }

    private static void ConfigureMediaAttachment(EntityTypeBuilder<MediaAttachment> entity)
    {
        entity.ToTable("media_attachments").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.MediaId).HasColumnName("media_id");
        entity.Property(x => x.ObjectId).HasColumnName("object_id");
        entity.HasIndex(x => new { x.MediaId, x.ObjectId }).IsUnique().HasDatabaseName("ux_media_attachments_media_object");
        entity.HasIndex(x => x.ObjectId).HasDatabaseName("ix_media_attachments_object");
        entity.HasOne<MediaResource>().WithMany().HasForeignKey(x => x.MediaId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<FederatedObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureRemoteMediaCache(EntityTypeBuilder<RemoteMediaCacheEntry> entity)
    {
        entity.ToTable("remote_media_cache").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ObjectId).HasColumnName("object_id");
        entity.Property(x => x.SourceIri).HasColumnName("source_iri").HasMaxLength(2_048);
        entity.Property(x => x.SourceToken).HasColumnName("source_token").HasMaxLength(128);
        entity.Property(x => x.MediaId).HasColumnName("media_id");
        entity.Property(x => x.ETag).HasColumnName("etag").HasMaxLength(512);
        entity.Property(x => x.LastModified).HasColumnName("last_modified");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.RefreshedAt).HasColumnName("refreshed_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.HasIndex(x => new { x.ObjectId, x.SourceToken }).IsUnique()
            .HasDatabaseName("ux_remote_media_cache_object_token");
        entity.HasIndex(x => new { x.ExpiresAt, x.Id }).HasDatabaseName("ix_remote_media_cache_expiry");
        entity.HasOne<FederatedObject>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<MediaResource>().WithMany().HasForeignKey(x => x.MediaId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRemoteActorMediaCache(EntityTypeBuilder<RemoteActorMediaCacheEntry> entity)
    {
        entity.ToTable("remote_actor_media_cache").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.RemoteActorId).HasColumnName("remote_actor_id");
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.SourceIri).HasColumnName("source_iri").HasMaxLength(2_048);
        entity.Property(x => x.SourceToken).HasColumnName("source_token").HasMaxLength(RemoteMediaSourceToken.Length);
        entity.Property(x => x.MediaId).HasColumnName("media_id");
        entity.Property(x => x.RemoteETag).HasColumnName("remote_etag").HasMaxLength(512);
        entity.Property(x => x.RemoteLastModified).HasColumnName("remote_last_modified");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.RefreshedAt).HasColumnName("refreshed_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        entity.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
        entity.Property(x => x.FailureKind).HasColumnName("failure_kind").HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.RetryAfter).HasColumnName("retry_after");
        entity.HasIndex(x => new { x.RemoteActorId, x.SourceToken }).IsUnique()
            .HasDatabaseName("ux_remote_actor_media_cache_actor_token");
        entity.HasIndex(x => new { x.ExpiresAt, x.Id })
            .HasDatabaseName("ix_remote_actor_media_cache_expiry");
        entity.HasIndex(x => new { x.LeaseExpiresAt, x.Id })
            .HasDatabaseName("ix_remote_actor_media_cache_lease");
        entity.HasOne<RemoteActor>().WithMany().HasForeignKey(x => x.RemoteActorId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<MediaResource>().WithMany().HasForeignKey(x => x.MediaId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureLegalHold(EntityTypeBuilder<LegalHold> entity)
    {
        entity.ToTable("legal_holds").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ResourceKind).HasColumnName("resource_kind").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.ResourceId).HasColumnName("resource_id");
        entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2_000);
        entity.Property(x => x.PlacedBy).HasColumnName("placed_by").HasMaxLength(256);
        entity.Property(x => x.PlacedAt).HasColumnName("placed_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.ReleasedAt).HasColumnName("released_at");
        entity.Property(x => x.ReleasedBy).HasColumnName("released_by").HasMaxLength(256);
        entity.HasIndex(x => new { x.ResourceKind, x.ResourceId }).IsUnique().HasFilter("released_at IS NULL")
            .HasDatabaseName("ux_legal_holds_active_resource");
        entity.HasIndex(x => new { x.ExpiresAt, x.Id }).HasDatabaseName("ix_legal_holds_expiry");
    }

    private static void ConfigureUserMute(EntityTypeBuilder<UserMute> entity)
    {
        entity.ToTable("user_mutes").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.OwnerActorIri).HasColumnName("owner_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.TargetActorIri).HasColumnName("target_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.HideNotifications).HasColumnName("hide_notifications");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        entity.HasIndex(x => new { x.OwnerActorIri, x.TargetActorIri }).IsUnique().HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ux_user_mutes_active_pair");
        entity.HasIndex(x => new { x.OwnerActorIri, x.ExpiresAt }).HasDatabaseName("ix_user_mutes_owner_expiry");
    }

    private static void ConfigureUserBlock(EntityTypeBuilder<UserBlock> entity)
    {
        entity.ToTable("user_blocks").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.OwnerActorIri).HasColumnName("owner_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.TargetActorIri).HasColumnName("target_actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.BlockActivityIri).HasColumnName("block_activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.UndoActivityIri).HasColumnName("undo_activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => x.BlockActivityIri).IsUnique().HasDatabaseName("ux_user_blocks_activity_iri");
        entity.HasIndex(x => new { x.OwnerActorIri, x.TargetActorIri }).IsUnique().HasFilter("state = 'Active'")
            .HasDatabaseName("ux_user_blocks_active_pair");
        entity.HasIndex(x => new { x.TargetActorIri, x.State }).HasDatabaseName("ix_user_blocks_target_state");
    }

    private static void ConfigureClientIdempotency(EntityTypeBuilder<ClientIdempotencyRecord> entity)
    {
        entity.ToTable("client_idempotency").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(256);
        entity.Property(x => x.Key).HasColumnName("key").HasMaxLength(256);
        entity.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(128);
        entity.Property(x => x.ActivityIri).HasColumnName("activity_iri").HasMaxLength(2_048);
        entity.Property(x => x.ObjectIri).HasColumnName("object_iri").HasMaxLength(2_048);
        entity.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("bytea");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.HasIndex(x => new { x.Subject, x.Key }).IsUnique().HasDatabaseName("ux_client_idempotency_subject_key");
        entity.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_client_idempotency_expiry");
    }

    private static void ConfigureActorPolicy(EntityTypeBuilder<ActorPolicy> entity)
    {
        entity.ToTable("actor_policies").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.ActorIri).HasColumnName("actor_iri").HasMaxLength(2_048);
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2_000);
        entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(256);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        entity.Property(x => x.RevokedBy).HasColumnName("revoked_by").HasMaxLength(256);
        entity.HasIndex(x => new { x.ActorIri, x.Kind }).HasDatabaseName("ix_actor_policies_actor_kind");
    }

    private static void ConfigureModerationAction(EntityTypeBuilder<ModerationAction> entity)
    {
        entity.ToTable("moderation_actions").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Target).HasColumnName("target").HasMaxLength(2_048);
        entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2_000);
        entity.Property(x => x.OperatorId).HasColumnName("operator_id").HasMaxLength(256);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        entity.Property(x => x.RevokedBy).HasColumnName("revoked_by").HasMaxLength(256);
        entity.HasIndex(x => new { x.Target, x.Kind, x.CreatedAt }).HasDatabaseName("ix_moderation_actions_target");
    }

    private static void ConfigureReport(EntityTypeBuilder<Report> entity)
    {
        entity.ToTable("reports").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Iri).HasColumnName("iri").HasMaxLength(2_048);
        entity.Property(x => x.ReporterIri).HasColumnName("reporter_iri").HasMaxLength(2_048);
        entity.Property(x => x.TargetIri).HasColumnName("target_iri").HasMaxLength(2_048);
        entity.Property(x => x.RawJson).HasColumnName("raw_json").HasColumnType("jsonb");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        entity.Property(x => x.ResolvedBy).HasColumnName("resolved_by").HasMaxLength(256);
        entity.HasIndex(x => x.Iri).IsUnique().HasFilter("iri IS NOT NULL").HasDatabaseName("ux_reports_iri");
    }

    private static void ConfigureAudit(EntityTypeBuilder<AuditEvent> entity)
    {
        entity.ToTable("audit_events").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.Category).HasColumnName("category").HasMaxLength(128);
        entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(128);
        entity.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(256);
        entity.Property(x => x.Target).HasColumnName("target").HasMaxLength(2_048);
        entity.Property(x => x.DetailsJson).HasColumnName("details_json").HasColumnType("jsonb");
        entity.Property(x => x.PreviousHash).HasColumnName("previous_hash").HasMaxLength(128);
        entity.Property(x => x.EventHash).HasColumnName("event_hash").HasMaxLength(128);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(x => x.EventHash).IsUnique().HasDatabaseName("ux_audit_events_hash");
        entity.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_audit_events_created");
    }

    private static void ConfigureDeadLetter(EntityTypeBuilder<DeadLetter> entity)
    {
        entity.ToTable("dead_letters").HasKey(x => x.Id);
        ConfigureId(entity);
        entity.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(128);
        entity.Property(x => x.SourceId).HasColumnName("source_id");
        entity.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(128);
        entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(4_096);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ReplayedAt).HasColumnName("replayed_at");
        entity.Property(x => x.ReplayedBy).HasColumnName("replayed_by").HasMaxLength(256);
        entity.HasIndex(x => new { x.SourceType, x.SourceId }).IsUnique().HasDatabaseName("ux_dead_letters_source");
    }

    private static void ConfigureWorkerHeartbeat(EntityTypeBuilder<WorkerHeartbeat> entity)
    {
        entity.ToTable("worker_heartbeats").HasKey(x => new { x.WorkerId, x.WorkerType });
        entity.Property(x => x.WorkerId).HasColumnName("worker_id").HasMaxLength(256);
        entity.Property(x => x.WorkerType).HasColumnName("worker_type").HasMaxLength(64);
        entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        entity.HasIndex(x => new { x.WorkerType, x.LastSeenAt }).HasDatabaseName("ix_worker_heartbeats_type_seen");
    }

    private static void ConfigureSchemaCompatibility(EntityTypeBuilder<SchemaCompatibility> entity)
    {
        entity.ToTable("schema_compatibility").HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.MinimumApplicationVersion).HasColumnName("minimum_application_version").HasMaxLength(64);
        entity.Property(x => x.MaximumApplicationVersion).HasColumnName("maximum_application_version").HasMaxLength(64);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasData(new SchemaCompatibility
        {
            Id = 1,
            MinimumApplicationVersion = "1.0.0",
            MaximumApplicationVersion = "1.999.999",
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
    }

    private static void ConfigureId<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : Entity =>
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

    private static void ConfigureDomainDeliveryLease(EntityTypeBuilder<DomainDeliveryLease> entity)
    {
        entity.ToTable("domain_delivery_leases").HasKey(x => new { x.Domain, x.Slot });
        entity.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(255);
        entity.Property(x => x.Slot).HasColumnName("slot");
        entity.Property(x => x.Owner).HasColumnName("owner").HasMaxLength(256);
        entity.Property(x => x.DeliveryId).HasColumnName("delivery_id");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.HasIndex(x => x.DeliveryId).IsUnique().HasDatabaseName("ux_domain_delivery_leases_delivery");
        entity.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_domain_delivery_leases_expiry");
    }

    private static void ConfigureRemoteDomainCircuit(EntityTypeBuilder<RemoteDomainCircuit> entity)
    {
        entity.ToTable("remote_domain_circuits").HasKey(x => x.Domain);
        entity.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(255);
        entity.Property(x => x.ConsecutiveFailures).HasColumnName("consecutive_failures");
        entity.Property(x => x.OpenUntil).HasColumnName("open_until");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => x.OpenUntil).HasDatabaseName("ix_remote_domain_circuits_open_until");
    }

    private static void ConfigureOperationalControl(EntityTypeBuilder<OperationalControl> entity)
    {
        entity.ToTable("operational_controls").HasKey(x => x.Name);
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
        entity.Property(x => x.Enabled).HasColumnName("enabled");
        entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2_000);
        entity.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(256);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
    }

    private static void ConfigureWorkItem<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : DurableWorkItem
    {
        ConfigureId(entity);
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.AvailableAt).HasColumnName("available_at");
        entity.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(256);
        entity.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
        entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        entity.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(128);
        entity.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(4_096);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
    }
}

internal sealed class WorkerHeartbeat
{
    public required string WorkerId { get; init; }
    public required string WorkerType { get; init; }
    public DateTimeOffset LastSeenAt { get; set; }
}

internal sealed class SchemaCompatibility
{
    public short Id { get; init; }
    public required string MinimumApplicationVersion { get; init; }
    public required string MaximumApplicationVersion { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

internal sealed class DomainDeliveryLease
{
    public required string Domain { get; init; }
    public int Slot { get; init; }
    public required string Owner { get; init; }
    public Guid DeliveryId { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

internal sealed class RemoteDomainCircuit
{
    public required string Domain { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTimeOffset? OpenUntil { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

internal sealed class OperationalControl
{
    public required string Name { get; init; }
    public bool Enabled { get; set; }
    public string? Reason { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
