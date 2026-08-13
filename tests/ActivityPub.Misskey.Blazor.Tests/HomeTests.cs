using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class HomeTests : BunitContext
{
    public HomeTests()
    {
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            SourceUrl: null,
            PublicBaseUri: new Uri("https://local.example", UriKind.Absolute),
            LocalAccountsEnabled: true));
        Services.AddSingleton<IMisskeyLocalizer>(new HomeLocalizer());
    }

    [Fact]
    public void RendersServerContractMetadataWithoutClientSidePlaceholder()
    {
        Services.AddSingleton<IInstancePresentationService>(new SuccessfulInstanceService());
        Services.AddSingleton<ITimelinePresentationService>(new EmptyTimelineService());
        Services.AddScoped<IButtonRippleInterop, NoOpButtonRippleInterop>();
        Services.AddScoped<IMarqueeInterop, NoOpMarqueeInterop>();
        Services.AddScoped<IWelcomeTimelineInterop, NoOpWelcomeTimelineInterop>();
        Services.AddScoped<IMisskeyOverlayService, MisskeyOverlayService>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        IRenderedComponent<Home> component = Render<Home>();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Production instance", component.Find("h1").TextContent);
            Assert.NotNull(component.Find(".rsqzvsbo > .top > .main > .fg > .action"));
            Assert.NotNull(component.Find(".xfbouadm.bg"));
            Assert.Equal(5, component.FindAll(".rsqzvsbo > .top > .emojis > img.mk-emoji").Count);
            Assert.NotNull(component.Find(".rsqzvsbo > .top > .federation > ._wrap_1hc4p_1"));
            Assert.Equal(4, component.FindAll("._federationInstance_jmpas_1").Count);
            Assert.False(component.Find("[data-cy-signup]").HasAttribute("disabled"));
            Assert.DoesNotContain("cdn.example", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("mk-instance-summary", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ConvertsServerConfigurationFailureToSafeDiagnosticCode()
    {
        Services.AddSingleton<IInstancePresentationService>(new FailingInstanceService());
        Services.AddSingleton<ITimelinePresentationService>(new EmptyTimelineService());
        Services.AddScoped<IButtonRippleInterop, NoOpButtonRippleInterop>();
        Services.AddScoped<IMarqueeInterop, NoOpMarqueeInterop>();
        Services.AddScoped<IWelcomeTimelineInterop, NoOpWelcomeTimelineInterop>();
        Services.AddScoped<IMisskeyOverlayService, MisskeyOverlayService>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        IRenderedComponent<Home> component = Render<Home>();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "INSTANCE_CONFIGURATION_INVALID",
                component.Find("[role=alert]").GetAttribute("data-error-code"));
            Assert.DoesNotContain("database-password", component.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void RequireSetupRendersPinnedWelcomeSetupInsteadOfEntrance()
    {
        var instance = new SetupRequiredInstanceService();
        Services.AddSingleton<IInstancePresentationService>(instance);
        Services.AddSingleton<ITimelinePresentationService>(new EmptyTimelineService());
        Services.AddScoped<IButtonRippleInterop, NoOpButtonRippleInterop>();
        Services.AddScoped<IFormInputInterop, NoOpFormInputInterop>();
        Services.AddScoped<IAuthenticationFormInterop, NoOpAuthenticationInterop>();
        Services.AddScoped<IMisskeyOverlayService, MisskeyOverlayService>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        IRenderedComponent<Home> component = Render<Home>();

        component.WaitForAssertion(() =>
        {
            IElement form = component.Find("form.mk-setup");
            Assert.Equal("Welcome to Misskey!", form.QuerySelector(":scope > h1")?.TextContent);
            Assert.NotNull(form.QuerySelector(":scope > div._formRoot > p"));
            Assert.Equal("^[a-zA-Z0-9_]{1,20}$", form.QuerySelector("input[name=username]")?.GetAttribute("pattern"));
            Assert.Equal("password", form.QuerySelector("input[name=password]")?.GetAttribute("type"));
            Assert.NotNull(form.QuerySelector(":scope > div._formRoot > .bottom._formBlock [data-cy-admin-ok]"));
            Assert.Empty(component.FindAll(".rsqzvsbo"));
            Assert.Equal(0, instance.FederationReads);
        });
    }

    private sealed class SuccessfulInstanceService : IInstancePresentationService
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
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>(
            [
                new("9federation1", "mastodon.example", null),
                new("9federation2", "misskey.example", "https://cdn.example/icon.png")
            ]);
    }

    private sealed class FailingInstanceService : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database-password must never reach the rendered response");

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
            CancellationToken cancellationToken) => throw new InvalidOperationException(
            "database-password must never reach the rendered response");
    }

    private sealed class SetupRequiredInstanceService : IInstancePresentationService
    {
        public int FederationReads { get; private set; }

        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(
            new InstanceSummaryViewModel(
                "Fresh instance",
                "",
                "12.119.2-server",
                "/static-assets/favicon.png",
                BackgroundImageUrl: null,
                LogoImageUrl: null,
                DisableRegistration: true,
                EmailRequiredForSignup: true,
                EnableEmail: true,
                TosUrl: null,
                RequireSetup: true));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
            CancellationToken cancellationToken)
        {
            FederationReads++;
            return Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
        }
    }

    private sealed class EmptyTimelineService : ITimelinePresentationService
    {
        public Task<TimelinePageViewModel> ReadAsync(TimelineKind kind, string? beforeId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new TimelinePageViewModel([], null));

        public Task<NoteViewModel> CreateAsync(NoteDraft draft, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> RenoteAsync(string noteId, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> ReactAsync(string noteId, string reaction, bool remove, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel> VotePollAsync(string noteId, int choiceIndex, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NoteViewModel?> FindForStreamAsync(Guid id, TimelineKind kind, CancellationToken cancellationToken) =>
            Task.FromResult<NoteViewModel?>(null);

        public Task<string> MapNoteIdAsync(Guid id, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
            Task.FromResult(id.ToString("N"));
    }

    private sealed class NoOpButtonRippleInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit does not attach browser listeners.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class NoOpFormInputInterop : IFormInputInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class NoOpAuthenticationInterop : IAuthenticationFormInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachSignInAsync(
            ElementReference form,
            DotNetObjectReference<MkSignin> receiver,
            string passkeyOptionsUrl,
            string passkeyAssertionUrl,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IJSObjectReference> AttachSignUpAsync(
            ElementReference form,
            DotNetObjectReference<MkSignup> receiver,
            string usernameAvailabilityUrl,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IJSObjectReference> AttachInitialSetupAsync(
            ElementReference form,
            DotNetObjectReference<WelcomeSetup> receiver,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
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

    private sealed class NoOpMarqueeInterop : IMarqueeInterop, IDisposable
    {
        public ValueTask<double> SetDurationAsync(
            ElementReference content,
            int repeat,
            double duration,
            CancellationToken cancellationToken) => ValueTask.FromResult(duration);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class NoOpWelcomeTimelineInterop : IWelcomeTimelineInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => throw new JSDisconnectedException("bUnit does not attach browser observers.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class HomeLocalizer : IMisskeyLocalizer
    {
        private static readonly Dictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["menu"] = "メニュー",
                ["headlineMisskey"] = "ノートでつながるネットワーク",
                ["signup"] = "登録",
                ["login"] = "ログイン",
                ["instanceInfo"] = "インスタンス情報",
                ["aboutMisskey"] = "Misskeyについて",
                ["help"] = "ヘルプ",
                ["intro"] = "Misskeyの初期設定を行います。",
                ["username"] = "ユーザー名",
                ["password"] = "パスワード",
                ["processing"] = "処理中",
                ["done"] = "完了",
                ["somethingHappened"] = "問題が発生しました",
                ["gotIt"] = "わかった"
            };

        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
            Values.TryGetValue(key, out string? value) ? value : key;

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
