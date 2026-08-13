using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using ActivityPub.Misskey.Blazor.Streaming;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class UserPreviewTests : BunitContext
{
    [Fact]
    public async Task PresentationReadsPersistentAccountShapeAndUsesSharedFollowCommands()
    {
        Guid accountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        DateTimeOffset createdAt = new(2026, 8, 4, 1, 2, 3, TimeSpan.Zero);
        var account = new ClientAccountView(
            accountId,
            "alice",
            "alice@remote.example",
            "Alice :wave:",
            Locked: false,
            Bot: false,
            Discoverable: true,
            Group: false,
            createdAt,
            "<p>Hello<br>Fediverse</p>",
            "https://remote.example/@alice",
            "https://remote.example/users/alice",
            "/media/proxy/actor/alice/avatar",
            "/media/proxy/actor/alice/banner",
            FollowersCount: 23,
            FollowingCount: 11,
            PostsCount: 42,
            LastPostAt: createdAt,
            Emojis: [new ClientCustomEmojiView("wave", "/media/emoji/wave", "/media/emoji/wave", true, null)],
            Fields: []);
        var relationship = Relationship(accountId, following: false, requested: true, followedBy: true);
        var query = new StubClientQuery
        {
            LocalActorIri = "https://local.example/users/viewer",
            LookupAccount = account,
            Relationship = relationship
        };
        query.AccountsById[accountId] = account;
        var commands = new RecordingClientCommands
        {
            Result = ClientViewFactory.Post(),
            RelationshipResult = Relationship(accountId, following: true, requested: false, followedBy: true)
        };
        var ids = new InMemoryExternalIds();
        string externalId = await ids.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            accountId,
            createdAt,
            CancellationToken.None);
        var authentication = new AuthenticatedActorContext(
            FixedAuthenticationStateProvider.Authenticated("viewer"),
            query);
        var service = new UserPreviewPresentationService(
            query,
            commands,
            ids,
            authentication,
            new MisskeyFrontendRuntimeConfiguration(
                MisskeyFrontendRuntimeConfiguration.PortVersion,
                null,
                new Uri("https://local.example")));

        UserPreviewViewModel preview = await service.ReadAsync(externalId, CancellationToken.None);

        Assert.Equal(accountId, preview.InternalId);
        Assert.Equal(externalId, preview.Id);
        Assert.Equal("Hello\nFediverse", preview.Description);
        Assert.Equal(42, preview.NotesCount);
        Assert.Equal(11, preview.FollowingCount);
        Assert.Equal(23, preview.FollowersCount);
        Assert.True(preview.IsFollowed);
        Assert.True(preview.HasPendingFollowRequestFromYou);
        Assert.Equal("/media/emoji/wave", preview.User.Emojis!["wave"]);

        preview = await service.FollowAsync(preview, "preview-follow-contract", CancellationToken.None);
        Assert.True(preview.IsFollowing);
        Assert.False(preview.HasPendingFollowRequestFromYou);
        Assert.Equal(1, commands.FollowCalls);
        Assert.Equal("viewer", commands.Username);
        Assert.Equal(accountId, commands.AccountId);
        Assert.Equal("preview-follow-contract", commands.IdempotencyKey);

        commands.RelationshipResult = Relationship(accountId, following: false, requested: false, followedBy: true);
        preview = await service.UnfollowAsync(preview, "preview-unfollow-contract", CancellationToken.None);
        Assert.False(preview.IsFollowing);
        Assert.Equal(1, commands.UnfollowCalls);
    }

    [Fact]
    public void UserPreviewPreservesPinnedDomAndRejectsRemoteBannerAndAvatarUrls()
    {
        var presentation = new RecordingPreviewService(CreatePreview(
            avatarUrl: "https://tracker.invalid/avatar.png",
            bannerUrl: "https://tracker.invalid/banner.png"));
        var overlays = new MisskeyOverlayService();
        RegisterComponentServices(presentation, overlays);
        Guid id = overlays.ShowUserPreview("host-1", "source-1", "alice-id", 1);

        IRenderedComponent<MkUserPreview> component = Render<MkUserPreview>(parameters => parameters
            .Add(preview => preview.Id, id)
            .Add(preview => preview.HostId, "host-1")
            .Add(preview => preview.SourceId, "source-1")
            .Add(preview => preview.Query, "alice-id")
            .Add(preview => preview.Generation, 1L)
            .Add(preview => preview.Showing, true));

        component.WaitForAssertion(() =>
        {
            IElement root = component.Find(".fxxzrfni._popup._shadow");
            Assert.Equal("loaded", root.GetAttribute("data-preview-load-state"));
            Assert.NotNull(root.QuerySelector(":scope > .info > .banner > .followed"));
            Assert.Equal("Alice :wave:", root.QuerySelector(".title > .name")?.TextContent.Trim());
            Assert.Equal("@alice@bücher.example", root.QuerySelector(".title .mk-acct")?.TextContent.Trim());
            Assert.Equal("73", root.QuerySelector(".status > div:nth-child(1) > span")?.TextContent);
            Assert.Equal("19", root.QuerySelector(".status > div:nth-child(2) > span")?.TextContent);
            Assert.Equal("31", root.QuerySelector(".status > div:nth-child(3) > span")?.TextContent);
            Assert.Equal("/static-assets/user-unknown.png", root.QuerySelector(".avatar > img.inner")?.GetAttribute("src"));
            Assert.Null(root.QuerySelector(".banner")?.GetAttribute("style"));
            Assert.DoesNotContain("tracker.invalid", component.Markup, StringComparison.Ordinal);
            Assert.NotNull(root.QuerySelector("button.kpoogebi.koudoku-button"));
        });
    }

    [Fact]
    public void AccountAndUserNamePreserveMfmPlainNoWrapAndUnicodeHostContracts()
    {
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(showFullAcct: false));
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example")));
        NoteAuthorViewModel user = CreatePreview(null, null).User;

        IRenderedComponent<MkUserName> name = Render<MkUserName>(parameters => parameters
            .Add(component => component.User, user));
        IRenderedComponent<MkAcct> acct = Render<MkAcct>(parameters => parameters
            .Add(component => component.User, user));

        Assert.Equal("havbbuyv nowrap", name.Find("span").ClassName);
        Assert.Equal("Alice :wave:", name.Find("span").TextContent.Trim());
        Assert.Equal("@alice@bücher.example", acct.Find(".mk-acct").TextContent.Trim());
    }

    [Fact]
    public void MiniFollowButtonInvokesTheSharedPresentationCommandAndUpdatesItsState()
    {
        var presentation = new RecordingPreviewService(CreatePreview(null, null) with
        {
            IsFollowing = false,
            HasPendingFollowRequestFromYou = false
        });
        Services.AddSingleton<IUserPreviewPresentationService>(presentation);
        Services.AddSingleton<IRelationshipSubscriptionService>(new NoOpRelationshipSubscriptionService());
        Services.AddSingleton<IMisskeyLocalizer>(CreateLocalizer());

        IRenderedComponent<MkFollowButton> component = Render<MkFollowButton>(parameters => parameters
            .Add(button => button.User, presentation.Model)
            .Add(button => button.Mini, true)
            .Add(button => button.CssClass, "koudoku-button"));

        component.Find("button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, presentation.FollowCalls);
            Assert.Contains("active", component.Find("button").ClassList);
            Assert.NotNull(component.Find("button > i.fa-minus"));
        });
    }

    [Fact]
    public async Task FollowButtonRefreshesFromDurableRelationshipSignalAndDisposesOnTargetChange()
    {
        UserPreviewViewModel initial = CreatePreview(null, null) with
        {
            IsFollowing = false,
            HasPendingFollowRequestFromYou = false
        };
        var presentation = new RecordingPreviewService(initial);
        var relationships = new ControllableRelationshipSubscriptionService();
        Services.AddSingleton<IUserPreviewPresentationService>(presentation);
        Services.AddSingleton<IRelationshipSubscriptionService>(relationships);
        Services.AddSingleton<IMisskeyLocalizer>(CreateLocalizer());
        int changes = 0;
        IRenderedComponent<MkFollowButton> component = Render<MkFollowButton>(parameters => parameters
            .Add(button => button.User, initial)
            .Add(button => button.UserChanged, EventCallback.Factory.Create<UserPreviewViewModel>(this, _ => changes++)));
        await relationships.WaitForSubscriptionAsync(initial.InternalId).WaitAsync(TimeSpan.FromSeconds(2));

        presentation.SetModel(initial with { IsFollowing = true });
        relationships.Publish(initial.InternalId);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("active", component.Find("button").ClassList);
            Assert.NotNull(component.Find("button > i.fa-minus"));
            Assert.Equal(1, changes);
        });

        UserPreviewViewModel replacement = initial with
        {
            InternalId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Id = "carol-id",
            User = initial.User with { Id = "carol-id", Username = "carol" },
            IsFollowing = false
        };
        component.Render(parameters => parameters.Add(button => button.User, replacement));
        await relationships.WaitForSubscriptionAsync(replacement.InternalId).WaitAsync(TimeSpan.FromSeconds(2));
        await relationships.WaitForDisposalAsync(initial.InternalId).WaitAsync(TimeSpan.FromSeconds(2));

        await component.Instance.DisposeAsync();
        await relationships.WaitForDisposalAsync(replacement.InternalId).WaitAsync(TimeSpan.FromSeconds(2));
    }

    private void RegisterComponentServices(
        IUserPreviewPresentationService presentation,
        IMisskeyOverlayService overlays)
    {
        Services.AddSingleton(presentation);
        Services.AddSingleton<IRelationshipSubscriptionService>(new NoOpRelationshipSubscriptionService());
        Services.AddSingleton(overlays);
        Services.AddSingleton<IUserPreviewInterop>(new RecordingPreviewInterop());
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(showFullAcct: false));
        Services.AddSingleton<IMisskeyLocalizer>(CreateLocalizer());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example")));
    }

    private static MisskeyLocalizer CreateLocalizer()
    {
        var catalog = new MisskeyLocaleCatalog();
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "en-US";
        return new MisskeyLocalizer(
            catalog,
            new MisskeyLocaleRequestResolver(catalog),
            new HttpContextAccessor { HttpContext = context });
    }

    private static UserPreviewViewModel CreatePreview(string? avatarUrl, string? bannerUrl)
    {
        var author = new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice@xn--bcher-kva.example",
            "Alice :wave:",
            avatarUrl ?? "/static-assets/favicon.png",
            IsBot: false,
            Emojis: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["wave"] = "/static-assets/favicon.png"
            });
        return new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "alice-id",
            author,
            "Hello @world #fediverse :wave:",
            bannerUrl ?? "/static-assets/favicon.png",
            NotesCount: 73,
            FollowingCount: 19,
            FollowersCount: 31,
            IsLocked: false,
            CanFollow: true,
            IsFollowing: false,
            HasPendingFollowRequestFromYou: false,
            IsFollowed: true);
    }

    private static ClientRelationshipView Relationship(Guid id, bool following, bool requested, bool followedBy) =>
        new(
            id,
            following,
            ShowingAnnounces: true,
            Notifying: false,
            followedBy,
            Blocking: false,
            BlockedBy: false,
            Muting: false,
            MutingNotifications: false,
            requested,
            RequestedBy: false,
            DomainBlocking: false,
            Endorsed: false,
            Note: string.Empty);

    private sealed class RecordingPreviewService(UserPreviewViewModel model) : IUserPreviewPresentationService
    {
        public UserPreviewViewModel Model { get; private set; } = model;
        public int FollowCalls { get; private set; }

        public void SetModel(UserPreviewViewModel value) => Model = value;

        public Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(Model);

        public Task<UserPreviewViewModel> FollowAsync(
            UserPreviewViewModel user,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            FollowCalls++;
            Model = user with { IsFollowing = true, HasPendingFollowRequestFromYou = false };
            return Task.FromResult(Model);
        }

        public Task<UserPreviewViewModel> UnfollowAsync(
            UserPreviewViewModel user,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            Model = user with { IsFollowing = false, HasPendingFollowRequestFromYou = false };
            return Task.FromResult(Model);
        }
    }

    private sealed class ControllableRelationshipSubscriptionService : IRelationshipSubscriptionService
    {
        private readonly ConcurrentDictionary<Guid, Channel<RelationshipMutation>> channels = new();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> subscriptions = new();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> disposals = new();
        private long cursor;

        public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Interlocked.Read(ref cursor));
        }

        public async IAsyncEnumerable<RelationshipMutation> SubscribeAsync(
            Guid targetActorId,
            long afterCursor,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = afterCursor;
            Channel<RelationshipMutation> channel = channels.GetOrAdd(
                targetActorId,
                _ => Channel.CreateUnbounded<RelationshipMutation>());
            subscriptions.GetOrAdd(
                targetActorId,
                _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            try
            {
                await foreach (RelationshipMutation mutation in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return mutation;
                }
            }
            finally
            {
                disposals.GetOrAdd(
                    targetActorId,
                    _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            }
        }

        public void Publish(Guid targetActorId)
        {
            long next = Interlocked.Increment(ref cursor);
            Assert.True(channels[targetActorId].Writer.TryWrite(new(next, Changed: true)));
        }

        public Task WaitForSubscriptionAsync(Guid targetActorId) => subscriptions.GetOrAdd(
            targetActorId,
            _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

        public Task WaitForDisposalAsync(Guid targetActorId) => disposals.GetOrAdd(
            targetActorId,
            _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    private sealed class RecordingPreviewInterop : IUserPreviewInterop
    {
        public ValueTask<IJSObjectReference> AttachDirectiveHostAsync(
            DotNetObjectReference<UserPreviewDirectiveHost> receiver,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new NoOpHandle());

        public ValueTask<IJSObjectReference> AttachPreviewAsync(
            string hostId,
            string sourceId,
            long generation,
            ElementReference preview,
            DotNetObjectReference<MkUserPreview> receiver,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new NoOpHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpRelationshipSubscriptionService : IRelationshipSubscriptionService
    {
        public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken) => Task.FromResult(0L);

        public async IAsyncEnumerable<RelationshipMutation> SubscribeAsync(
            Guid targetActorId,
            long afterCursor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MfmNode>>(
            [new MfmNode("text", JsonSerializer.SerializeToElement(new { text }), null)]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState(bool showFullAcct) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            object value = string.Equals(propertyName, "showFullAcct", StringComparison.Ordinal)
                ? showFullAcct
                : fallback!;
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
