using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NotificationPresentationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StoredNotificationProjectsRealAccountNoteReactionAndReadMutation()
    {
        ClientPostView post = ClientViewFactory.Post("federated note") with
        {
            Emojis = [new("party", "/media/party.webp", "/media/party-static.webp", true, null)]
        };
        var stored = new ClientNotificationView(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            UserNotificationKind.Reaction,
            Now,
            IsRead: false,
            ":party:",
            post.Account,
            post);
        var persistence = new RecordingNotifications(stored);
        var externalIds = new InMemoryExternalIds();
        var query = new StubClientQuery
        {
            Reactions = new(
                new Dictionary<string, long>(StringComparer.Ordinal) { [":party:"] = 2 },
                ":party:",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["party"] = "/media/proxy/party"
                })
        };
        var service = new NotificationPresentationService(
            persistence,
            query,
            externalIds,
            new FixedActorContext(),
            new UnusedUserPreviews());

        NotificationViewModel projected = Assert.Single(await service.ReadAsync(
            new(null, 10),
            CancellationToken.None));

        Assert.Equal(stored.Id, projected.InternalId);
        Assert.Equal(MisskeyNotificationType.Reaction, projected.Type);
        Assert.Equal(":party:", projected.Reaction);
        Assert.Equal("federated note", projected.Note?.Text);
        Assert.Equal("party", Assert.Single(projected.Note!.Emojis).Key);
        Assert.NotNull(projected.FullNote);
        Assert.Equal(2, projected.FullNote!.ReactionsCount);
        Assert.Equal(":party:", projected.FullNote.ViewerReaction);
        Assert.Equal("/media/party.webp", projected.FullNote.Emojis["party"]);
        Assert.False(projected.IsRead);
        Assert.True(await service.MarkReadAsync(projected.InternalId, CancellationToken.None));
        Assert.Equal(stored.Id, Assert.Single(persistence.MarkedIds));
        Assert.Equal("https://local.example/users/alice", persistence.LastRecipient);
    }

    [Fact]
    public async Task ReplyIsDistinguishedFromMentionAndApplicationPayloadGapIsExplicit()
    {
        ClientPostView reply = ClientViewFactory.Post("reply") with
        {
            InReplyToId = Guid.Parse("44444444-4444-4444-4444-444444444444")
        };
        var mention = new ClientNotificationView(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            UserNotificationKind.Mention,
            Now,
            IsRead: false,
            null,
            reply.Account,
            reply);
        var persistence = new RecordingNotifications(mention);
        var query = new StubClientQuery
        {
            StreamPost = ClientViewFactory.Post("authorized reply parent") with
            {
                Id = reply.InReplyToId!.Value
            }
        };
        var service = new NotificationPresentationService(
            persistence,
            query,
            new InMemoryExternalIds(),
            new FixedActorContext(),
            new UnusedUserPreviews());

        NotificationViewModel replyProjection = Assert.Single(await service.ReadAsync(
            new(null, 10, IncludeTypes: new HashSet<MisskeyNotificationType> { MisskeyNotificationType.Reply }),
            CancellationToken.None));
        Assert.Equal(MisskeyNotificationType.Reply, replyProjection.Type);
        Assert.Equal("authorized reply parent", replyProjection.FullNote?.Reply?.Text);

        persistence.Value = mention with { Kind = UserNotificationKind.Application, Post = null };
        NotificationViewModel appProjection = Assert.Single(await service.ReadAsync(
            new(null, 10),
            CancellationToken.None));
        Assert.Equal(MisskeyNotificationType.App, appProjection.Type);
        Assert.Equal("NOTIFICATION_APPLICATION_PAYLOAD_UNAVAILABLE", appProjection.BlockedReason);
        Assert.Null(appProjection.Body);
    }

    [Fact]
    public async Task PaginationSourceRejectsDirectionsThePersistentNotificationContractCannotHonor()
    {
        var source = new NotificationPaginationSource(new EmptyNotificationPresentation());

        NotificationPresentationException exception = await Assert.ThrowsAsync<NotificationPresentationException>(async () =>
            await source.FetchAsync(new(10, SinceId: "future"), CancellationToken.None));

        Assert.Equal("NOTIFICATION_PAGINATION_DIRECTION_UNSUPPORTED", exception.ErrorCode);
    }

    private sealed class RecordingNotifications(ClientNotificationView value) : IClientNotificationService
    {
        public ClientNotificationView Value { get; set; } = value;

        public List<Guid> MarkedIds { get; } = [];

        public string? LastRecipient { get; private set; }

        public Task<ClientPage<ClientNotificationView>> ReadAsync(
            string recipientActorIri,
            ClientNotificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRecipient = recipientActorIri;
            bool included = query.IncludeKinds is null || query.IncludeKinds.Count == 0 || query.IncludeKinds.Contains(Value.Kind);
            bool excluded = query.ExcludeKinds?.Contains(Value.Kind) == true;
            return Task.FromResult(new ClientPage<ClientNotificationView>(
                included && !excluded ? [Value] : [],
                null,
                null));
        }

        public Task<ClientNotificationView?> FindAsync(
            string recipientActorIri,
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientNotificationView?>(id == Value.Id ? Value : null);

        public Task<bool> MarkReadAsync(
            string recipientActorIri,
            IReadOnlyCollection<Guid> ids,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            LastRecipient = recipientActorIri;
            MarkedIds.AddRange(ids);
            return Task.FromResult(true);
        }

        public Task<int> MarkAllReadAsync(string recipientActorIri, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(1);

        public Task<bool> DismissAsync(string recipientActorIri, Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ClearAsync(string recipientActorIri, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<long> CountUnreadAsync(string recipientActorIri, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedActorContext : IAuthenticatedActorContext
    {
        private static readonly AuthenticatedActor Actor = new("alice", "https://local.example/users/alice");

        public Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedActor?>(Actor);

        public Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) => Task.FromResult(Actor);
        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class UnusedUserPreviews : IUserPreviewPresentationService
    {
        public Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UserPreviewViewModel> FollowAsync(
            UserPreviewViewModel user,
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserPreviewViewModel> UnfollowAsync(
            UserPreviewViewModel user,
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyNotificationPresentation : INotificationPresentationService
    {
        public Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
            NotificationPresentationQuery request,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationViewModel>>([]);

        public Task<NotificationViewModel?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult<NotificationViewModel?>(null);

        public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> MarkAllReadAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
