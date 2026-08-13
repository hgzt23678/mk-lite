using System.Runtime.CompilerServices;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using ActivityPub.Misskey.Blazor.Streaming;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class UserInfoTests : BunitContext
{
    [Fact]
    public void PreservesPinnedUserCardDomContentAndAttributeFallthrough()
    {
        UserPreviewViewModel user = User(canFollow: false);
        RegisterServices(user);

        using IRenderedComponent<MkUserInfo> component = Render<MkUserInfo>(parameters => parameters
            .Add(value => value.User, user)
            .AddUnmatched("class", "user")
            .AddUnmatched("data-contract", "user-info"));

        component.WaitForAssertion(() =>
        {
            IElement root = component.Find("._panel.vjnjpkug.user[data-contract='user-info']");
            Assert.Equal("background-image: url('/media/banner')", root.QuerySelector(":scope > .banner")?.GetAttribute("style"));
            IElement avatar = root.QuerySelector(":scope > .avatar")!;
            Assert.Equal("@alice@remote.example", avatar.GetAttribute("href"));
            Assert.Equal("/media/avatar", avatar.QuerySelector(":scope > img.inner")?.GetAttribute("src"));
            Assert.Null(avatar.GetAttribute("data-user-preview"));
            Assert.NotNull(avatar.QuerySelector(":scope > .indicator"));
            Assert.Equal("@alice@remote.example", root.QuerySelector(":scope > .title > .name")?.GetAttribute("href"));
            Assert.Equal("Alice :wave:", root.QuerySelector(":scope > .title > .name")?.TextContent.Trim());
            Assert.Equal("@alice@remote.example", root.QuerySelector(":scope > .title > .username .mk-acct")?.TextContent.Trim());
            Assert.Equal("Hello #fediverse :wave:", root.QuerySelector(":scope > .description > .mfm")?.TextContent.Trim());
            string[] labels = root.QuerySelectorAll(":scope > .status > div > p").Select(value => value.TextContent).ToArray();
            string[] counts = root.QuerySelectorAll(":scope > .status > div > span").Select(value => value.TextContent).ToArray();
            Assert.Equal(["Notes", "Following", "Followers"], labels);
            Assert.Equal(["73", "19", "31"], counts);
            Assert.Null(root.QuerySelector(":scope > .koudoku-button"));
        });
    }

    [Fact]
    public void ShowsNoDescriptionAndFollowOnlyForAnEligibleViewer()
    {
        UserPreviewViewModel eligible = User(canFollow: true);
        RegisterServices(eligible);

        using IRenderedComponent<MkUserInfo> followable = Render<MkUserInfo>(parameters => parameters
            .Add(value => value.User, eligible));
        Assert.NotNull(followable.Find(".vjnjpkug > button.kpoogebi.koudoku-button[mini]"));

        UserPreviewViewModel selfOrAnonymous = eligible with
        {
            Description = string.Empty,
            BannerUrl = "https://tracker.invalid/banner.png",
            CanFollow = false
        };
        using IRenderedComponent<MkUserInfo> hidden = Render<MkUserInfo>(parameters => parameters
            .Add(value => value.User, selfOrAnonymous));

        IElement root = hidden.Find("._panel.vjnjpkug");
        Assert.Equal("This user has not written their bio yet.", root.QuerySelector(":scope > .description > span")?.TextContent);
        Assert.Null(root.QuerySelector(":scope > .banner")?.GetAttribute("style"));
        Assert.Null(root.QuerySelector(":scope > .koudoku-button"));
        Assert.DoesNotContain("tracker.invalid", hidden.Markup, StringComparison.Ordinal);
    }

    private void RegisterServices(UserPreviewViewModel user)
    {
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IUserPreviewPresentationService>(new FixedPreviewService(user));
        Services.AddSingleton<IRelationshipSubscriptionService>(new NoOpRelationshipSubscriptionService());
        Services.AddSingleton<IMisskeyLocalizer>(CreateLocalizer());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example")));
    }

    private static UserPreviewViewModel User(bool canFollow) => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "alice-id",
        new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice@remote.example",
            "Alice :wave:",
            "/media/avatar",
            IsBot: false,
            OnlineStatus: "active",
            Emojis: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["wave"] = "/media/emoji/wave"
            }),
        "Hello #fediverse :wave:",
        "/media/banner",
        NotesCount: 73,
        FollowingCount: 19,
        FollowersCount: 31,
        IsLocked: false,
        CanFollow: canFollow,
        IsFollowing: false,
        HasPendingFollowRequestFromYou: false,
        IsFollowed: false);

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

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MfmNode>>(
            [new MfmNode("text", JsonSerializer.SerializeToElement(new { text }), null)]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FixedPreviewService(UserPreviewViewModel user) : IUserPreviewPresentationService
    {
        public Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(user);

        public Task<UserPreviewViewModel> FollowAsync(
            UserPreviewViewModel value,
            string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult(value with { IsFollowing = true });

        public Task<UserPreviewViewModel> UnfollowAsync(
            UserPreviewViewModel value,
            string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult(value with { IsFollowing = false });
    }

    private sealed class NoOpRelationshipSubscriptionService : IRelationshipSubscriptionService
    {
        public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken) => Task.FromResult(0L);

        public async IAsyncEnumerable<RelationshipMutation> SubscribeAsync(
            Guid targetActorId,
            long afterCursor,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }
}
