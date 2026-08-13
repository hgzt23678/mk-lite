using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Layouts;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class UniversalShellTests : BunitContext
{
    private readonly RecordingUniversalShellInterop browser = new();

    public UniversalShellTests()
    {
        Services.AddSingleton<IUniversalShellInterop>(browser);
        Services.AddSingleton<IWidgetsInterop>(new NoOpWidgetsInterop());
        Services.AddSingleton<IViewportInterop>(new NoOpViewportInterop());
        Services.AddSingleton<INavbarInterop>(new NoOpNavbarInterop());
        Services.AddSingleton<IStickyContainerInterop>(new NoOpStickyContainerInterop());
        Services.AddSingleton<IInstancePresentationService>(new InstanceService());
        Services.AddSingleton<ICurrentAccountPresentationService>(new CurrentAccountService());
        Services.AddSingleton<IPizzaxDeviceState>(new DeviceState());
        Services.AddSingleton(MisskeyFrontendRuntimeConfiguration.Default);
        Services.AddSingleton<IMisskeyLocalizer>(new Localizer());
        Services.AddScoped<IMisskeyOverlayService, MisskeyOverlayService>();
    }

    [Fact]
    public async Task PreservesPinnedDesktopHierarchyAndMobileDrawerMotion()
    {
        IRenderedComponent<UniversalShell> component = Render<UniversalShell>(parameters => parameters
            .AddChildContent("page"));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".dkgtipfy > .sidebar.mvcprjjd"));
            IElement universalHeader = component.Find(".dkgtipfy > .contents > div:first-child");
            Assert.NotNull(universalHeader.QuerySelector(":scope > ._statusbars_1bps6_1"));
            Assert.Equal(string.Empty, universalHeader.TextContent.Trim());
            Assert.DoesNotContain("Production instance", universalHeader.TextContent, StringComparison.Ordinal);
            Assert.Equal("page", component.Find(".dkgtipfy > .contents main > div").TextContent);
            Assert.NotNull(component.Find(".dkgtipfy > .widgets > .efzpzdvf"));
        });

        await component.InvokeAsync(() => component.Instance.UpdateUniversalMetrics(390, "smartphone", true));
        component.WaitForAssertion(() =>
        {
            Assert.Contains("wallpaper", component.Find(".dkgtipfy").ClassList);
            Assert.Empty(component.FindAll(".dkgtipfy > .sidebar"));
            Assert.Equal(5, component.FindAll(".dkgtipfy > .buttons > button").Count);
        });

        component.Find(".dkgtipfy > .buttons > .nav").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Contains("menuDrawer-back-enter-from", component.Find(".menuDrawer-back").ClassList);
            Assert.Contains("menuDrawer-enter-from", component.Find(".menuDrawer").ClassList);
            Assert.Contains("beginEnter", browser.Attachment.Invocations);
        });

        await component.InvokeAsync(() => component.Instance.NotifyUniversalMotionCompleted("menuDrawer", 1, true));
        component.Find(".menuDrawer-back").Click();
        component.WaitForAssertion(() => Assert.Contains("beginLeave", browser.Attachment.Invocations));

        await component.InvokeAsync(() => component.Instance.NotifyUniversalMotionCompleted("menuDrawer", 2, false));
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll(".menuDrawer"));
            Assert.Empty(component.FindAll(".menuDrawer-back"));
        });
    }

    private sealed class RecordingUniversalShellInterop : IUniversalShellInterop
    {
        public RecordingHandle Attachment { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(Attachment);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpViewportInterop : IViewportInterop
    {
        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpStickyContainerInterop : IStickyContainerInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference body,
            double parentTop,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpWidgetsInterop : IWidgetsInterop
    {
        public ValueTask<IJSObjectReference?> AttachAsync(
            ElementReference root,
            DotNetObjectReference<MkWidgets> receiver,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IJSObjectReference?>(null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpNavbarInterop : INavbarInterop
    {
        public ValueTask SubmitAsync(ElementReference form, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CurrentAccountService : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(
            new NoteAuthorViewModel(
                "alice-id",
                "alice",
                "alice",
                "Alice",
                "/static-assets/favicon.png",
                IsBot: false));
    }

    private sealed class DeviceState : IPizzaxDeviceState
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

    private sealed class RecordingHandle : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InstanceService : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(
            new InstanceSummaryViewModel(
                "Production instance",
                "Federated server",
                "12.119.2-server",
                "/static-assets/favicon.png",
                BackgroundImageUrl: null,
                LogoImageUrl: null,
                DisableRegistration: true,
                EmailRequiredForSignup: false,
                EnableEmail: false,
                TosUrl: null));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class Localizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;
        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
