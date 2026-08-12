using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class AnnouncementsPageTests : BunitContext
{
    [Fact]
    public async Task PreservesPinnedCardsSafeImageMfmAndPersistentReadAction()
    {
        var announcements = new RecordingAnnouncements();
        Services.AddSingleton<IAnnouncementPagePresentationService>(announcements);
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddSingleton<IMisskeyLocalizer>(new AnnouncementLocalizer());
        Services.AddSingleton<IStickyContainerInterop>(new NoOpBrowserInterop());
        Services.AddSingleton<IPageHeaderInterop>(new NoOpBrowserInterop());
        Services.AddSingleton<ISpacerInterop>(new NoOpBrowserInterop());
        Services.AddSingleton<IPaginationInterop>(new NoOpBrowserInterop());
        Services.AddSingleton<IButtonRippleInterop>(new NoOpBrowserInterop());
        Services.AddSingleton<IErrorAppearInterop>(new NoOpErrorAppearInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<ICurrentAccountPresentationService>(new EmptyCurrentAccount());
        ComponentFactories.AddStub<MfmView>();

        using IRenderedComponent<AnnouncementsPage> component = Render<AnnouncementsPage>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".ruryvtyk > .announcement").Count));

        Assert.Equal("Announcements", component.Find(".titleContainer .title .title").TextContent);
        Assert.NotNull(component.Find(".titleContainer > i.fa-broadcast-tower"));
        Assert.Equal("🆕 Notice", component.Find(".announcement:first-child > ._title").TextContent.Trim());
        Assert.Equal("Read notice", component.Find(".announcement:last-child > ._title").TextContent.Trim());
        Assert.Equal("/static-assets/favicon.png", component.Find(".announcement img").GetAttribute("src"));
        Assert.Single(component.FindAll(".announcement img"));
        Assert.Equal(
            ["I $[jelly ❤] Misskey", "Already read"],
            component.FindComponents<Bunit.TestDoubles.Stub<MfmView>>()
                .Select(value => value.Instance.Parameters.Get(mfm => mfm.Text)));
        Assert.Single(component.FindAll(".announcement > ._footer"));

        component.Find(".announcement > ._footer button").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".announcement > ._footer")));
        Assert.Equal(["announcement-unread"], announcements.MarkedIds);

        var source = new AnnouncementPaginationSource(announcements);
        await Assert.ThrowsAsync<AnnouncementPresentationException>(() => source.FetchAsync(
            new(10, SinceId: "unsupported"),
            CancellationToken.None).AsTask());
    }

    private sealed class RecordingAnnouncements : IAnnouncementPagePresentationService
    {
        public List<string> MarkedIds { get; } = [];

        public Task<IReadOnlyList<AnnouncementPageViewModel>> ReadAsync(
            string? untilId,
            int limit,
            CancellationToken cancellationToken)
        {
            Assert.Null(untilId);
            Assert.Equal(11, limit);
            return Task.FromResult<IReadOnlyList<AnnouncementPageViewModel>>(
            [
                new(
                    "announcement-unread",
                    new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
                    "Notice",
                    "I $[jelly ❤] Misskey",
                    "/static-assets/favicon.png",
                    IsRead: false),
                new(
                    "announcement-read",
                    new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
                    "Read notice",
                    "Already read",
                    "https://remote.example/image.png",
                    IsRead: true)
            ]);
        }

        public Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken)
        {
            MarkedIds.Add(id);
            return Task.FromResult(true);
        }
    }

    private sealed class AnnouncementLocalizer : IMisskeyLocalizer
    {
        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public event EventHandler? LocaleChanged { add { } remove { } }

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "announcements" => "Announcements",
            "gotIt" => "Got it",
            _ => key
        };

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.OrdinalIgnoreCase);
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

    private sealed class EmptyCurrentAccount : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice",
            "Alice",
            "/static-assets/user-unknown.png",
            IsBot: false));
    }

    private sealed class NoOpBrowserInterop :
        IStickyContainerInterop,
        IPageHeaderInterop,
        ISpacerInterop,
        IPaginationInterop,
        IButtonRippleInterop,
        IJSObjectReference
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference body,
            double parentTop,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            DotNetObjectReference<T> receiver,
            bool enableAutoLoad,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool autofocus,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<bool> IsTopVisibleAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> IsBottomVisibleAsync(
            ElementReference root,
            double tolerance,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(
            ElementReference root,
            CancellationToken cancellationToken) => ValueTask.FromResult(new PaginationScrollSnapshot(0, 0, true, false));

        public ValueTask RestoreScrollAsync(
            ElementReference root,
            PaginationScrollSnapshot snapshot,
            bool stickToBottom,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ScrollToTopAsync(ElementReference root, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpErrorAppearInterop : IErrorAppearInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool animate,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new NoOpBrowserInterop());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
