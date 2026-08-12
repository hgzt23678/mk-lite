using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class UserListTests : BunitContext
{
    public UserListTests()
    {
        Services.AddSingleton<IPaginationInterop>(new NoOpPaginationInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IMisskeyLocalizer>(CreateLocalizer());
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IErrorAppearInterop>(new NoOpErrorAppearInterop());
        Services.AddSingleton<IButtonRippleInterop>(new NoOpButtonRippleInterop());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example")));
    }

    [Fact]
    public async Task PreservesLoadingKeyedGridLoadMoreReloadAndAttributeFallthrough()
    {
        var source = new UserSource(new(Limit: 2));
        var initial = new TaskCompletionSource<IReadOnlyList<UserPreviewViewModel>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.Responses.Enqueue(() => new(initial.Task));
        source.Responses.Enqueue(() => ValueTask.FromResult<IReadOnlyList<UserPreviewViewModel>>([User("carol", 3)]));
        source.Responses.Enqueue(() => ValueTask.FromResult<IReadOnlyList<UserPreviewViewModel>>([User("bob", 2)]));

        using IRenderedComponent<MkUserList> component = Render<MkUserList>(parameters => parameters
            .Add(list => list.Pagination, source)
            .Add(list => list.NoGap, true)
            .AddUnmatched("class", "contract-list")
            .AddUnmatched("data-contract", "user-list"));

        Assert.NotNull(component.Find("._root_13vug_9.contract-list[data-contract='user-list']"));
        Assert.Empty(component.FindAll(".efvhhmdq"));

        initial.SetResult([User("alice", 1), User("bob", 2), User("sentinel", 99)]);
        component.WaitForAssertion(() =>
        {
            IElement root = component.Find("div.contract-list[data-contract='user-list']");
            IElement grid = root.QuerySelector(":scope > .efvhhmdq")!;
            Assert.Equal(2, grid.QuerySelectorAll(":scope > ._panel.vjnjpkug.user").Length);
            Assert.Equal(["Alice", "Bob"], grid.QuerySelectorAll(":scope > .user > .title > .name")
                .Select(element => element.TextContent.Trim()));
            Assert.Single(root.QuerySelectorAll(":scope > .cxiknjgy > button"));
            Assert.DoesNotContain("noGap", component.Markup, StringComparison.Ordinal);
        });

        await component.InvokeAsync(component.Instance.LoadMoreAsync);
        component.WaitForAssertion(() =>
        {
            Assert.Equal(3, component.FindAll(".efvhhmdq > .user").Count);
            Assert.Equal(["Alice", "Bob", "Carol"], component.FindAll(".efvhhmdq > .user > .title > .name")
                .Select(element => element.TextContent.Trim()));
            Assert.Equal(31, source.Requests[1].Limit);
            Assert.Equal("bob", source.Requests[1].UntilId);
        });

        await component.InvokeAsync(component.Instance.ReloadAsync);
        component.WaitForAssertion(() =>
        {
            Assert.Single(component.FindAll(".efvhhmdq > .user"));
            Assert.Equal("Bob", component.Find(".efvhhmdq > .user > .title > .name").TextContent.Trim());
            Assert.Equal(3, source.Requests[2].Limit);
            Assert.Null(source.Requests[2].UntilId);
        });
    }

    [Fact]
    public void EmptyUsesTheLocalModifiedAssetAndNoUsersMessage()
    {
        var source = new UserSource(new(Limit: 10));
        source.Responses.Enqueue(() => ValueTask.FromResult<IReadOnlyList<UserPreviewViewModel>>([]));

        using IRenderedComponent<MkUserList> component = Render<MkUserList>(parameters => parameters
            .Add(list => list.Pagination, source)
            .Add(list => list.NoGap, true)
            .AddUnmatched("class", "contract-empty"));

        component.WaitForAssertion(() =>
        {
            IElement empty = component.Find(".empty.contract-empty > ._fullinfo");
            Assert.Equal("/client-assets/about-icon.png", empty.QuerySelector(":scope > img._ghost")?.GetAttribute("src"));
            Assert.Equal("There are no users", empty.QuerySelector(":scope > div")?.TextContent);
            Assert.DoesNotContain("xn--931a.moe", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("noGap", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BackendFailureUsesTheExplicitPaginationErrorInsteadOfAnEmptyFallback()
    {
        var source = new UserSource(new(Limit: 10));
        source.Responses.Enqueue(() => ValueTask.FromException<IReadOnlyList<UserPreviewViewModel>>(
            new InvalidOperationException("backend user listing is unavailable")));

        using IRenderedComponent<MkUserList> component = Render<MkUserList>(parameters => parameters
            .Add(list => list.Pagination, source));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("An error has occurred", component.Find(".mjndxjcg > p").TextContent.Trim());
            Assert.Empty(component.FindAll(".efvhhmdq"));
            Assert.Empty(component.FindAll("._fullinfo"));
        });
    }

    private static UserPreviewViewModel User(string username, int ordinal) => new(
        Guid.Parse($"00000000-0000-0000-0000-{ordinal:D12}"),
        username,
        new NoteAuthorViewModel(
            username,
            username,
            $"{username}@remote.example",
            char.ToUpperInvariant(username[0]) + username[1..],
            "/static-assets/user-unknown.png",
            IsBot: false,
            OnlineStatus: "unknown"),
        string.Empty,
        null,
        NotesCount: ordinal,
        FollowingCount: ordinal + 1,
        FollowersCount: ordinal + 2,
        IsLocked: false,
        CanFollow: false,
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

    private sealed class UserSource(MisskeyPaginationOptions options)
        : IMisskeyPaginationSource<UserPreviewViewModel>
    {
        public Queue<Func<ValueTask<IReadOnlyList<UserPreviewViewModel>>>> Responses { get; } = [];
        public List<MisskeyPaginationRequest> Requests { get; } = [];
        public MisskeyPaginationOptions Options { get; } = options;

        public ValueTask<IReadOnlyList<UserPreviewViewModel>> FetchAsync(
            MisskeyPaginationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Responses.Dequeue()();
        }

        public string GetId(UserPreviewViewModel item) => item.Id;
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

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MfmNode>>(
            [new MfmNode("text", JsonSerializer.SerializeToElement(new { text }), null)]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpPaginationInterop : IPaginationInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            DotNetObjectReference<T> receiver,
            bool enableAutoLoad,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask<bool> IsTopVisibleAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> IsBottomVisibleAsync(
            ElementReference root,
            double tolerance,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(
            ElementReference root,
            CancellationToken cancellationToken) => ValueTask.FromResult(new PaginationScrollSnapshot(0, 0, false, false));

        public ValueTask RestoreScrollAsync(
            ElementReference root,
            PaginationScrollSnapshot snapshot,
            bool stickToBottom,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ScrollToTopAsync(
            ElementReference root,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpErrorAppearInterop : IErrorAppearInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool animate,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpButtonRippleInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpJsObject : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
