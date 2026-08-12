using System.Security.Claims;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class UpdatedTests : BunitContext
{
    [Fact]
    public void PreservesPinnedDomLocalizationRuntimeVersionAndReleaseTarget()
    {
        RecordingClientUpdateInterop updateInterop = RegisterDependencies();

        IRenderedComponent<MkUpdated> component = Render<MkUpdated>();

        IElement root = component.Find(".qzhlnise.dialog.modal-enter-active.modal-enter-from");
        Assert.Equal("dialog", root.GetAttribute("role"));
        Assert.Equal("true", root.GetAttribute("aria-modal"));
        Assert.Equal("Misskeyが更新されました！", root.GetAttribute("aria-label"));
        Assert.NotNull(root.QuerySelector(":scope > .bg._modalBg"));
        IElement panel = Assert.IsAssignableFrom<IElement>(
            root.QuerySelector(":scope > .content > .ewlycnyt"));
        Assert.Equal(4, panel.Children.Length);
        Assert.Equal("Misskeyが更新されました！", panel.QuerySelector(":scope > .title > .mk-sparkle")?.TextContent);
        Assert.Equal("✨12.119.2-port.1🚀", panel.QuerySelector(":scope > .version")?.TextContent);
        IElement[] buttons = panel.QuerySelectorAll(":scope > button.bghgjjyj._button.full")
            .Cast<IElement>()
            .ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.Equal("変更点", buttons[0].TextContent.Trim());
        Assert.DoesNotContain("primary", buttons[0].ClassList);
        Assert.Equal("わかった", buttons[1].TextContent.Trim());
        Assert.Contains("gotIt", buttons[1].ClassList);
        Assert.Contains("primary", buttons[1].ClassList);

        component.WaitForAssertion(() =>
        {
            Assert.Equal("https://misskey-hub.net/docs/releases.html#_12-119-2-port-1", updateInterop.ReleaseNotesUrl?.AbsoluteUri);
            Assert.NotNull(updateInterop.Receiver);
        });
    }

    [Fact]
    public async Task ClosedIsEmittedOnceAfterTheBrowserMotionCallbackAndDisposalReleasesTheHandle()
    {
        RecordingClientUpdateInterop updateInterop = RegisterDependencies();
        int closed = 0;
        IRenderedComponent<MkUpdated> component = Render<MkUpdated>(parameters => parameters
            .Add(updated => updated.Closed, () => closed++));
        component.WaitForAssertion(() => Assert.NotNull(updateInterop.Receiver));

        Assert.Equal(0, closed);
        await component.InvokeAsync(updateInterop.Receiver!.NotifyClosed);
        await component.InvokeAsync(updateInterop.Receiver.NotifyClosed);
        Assert.Equal(1, closed);

        await component.Instance.DisposeAsync();
        Assert.Contains("dispose", updateInterop.Handle.Invocations);
        Assert.True(updateInterop.Handle.Disposed);
    }

    [Fact]
    public void AuthenticatedUpgradeUsesTheRawVersionSnapshotToDisplayTheRealDialog()
    {
        RecordingClientUpdateInterop updateInterop = RegisterDependencies(
            new ClientVersionStorageSnapshot("12.119.1", Changed: true));
        AuthenticationState state = new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "alice")],
            authenticationType: "tests")));

        IRenderedComponent<CascadingValue<Task<AuthenticationState>>> host =
            Render<CascadingValue<Task<AuthenticationState>>>(parameters => parameters
                .Add(cascade => cascade.Value, Task.FromResult(state))
                .AddChildContent<MisskeyClientUpdateHost>());

        host.WaitForAssertion(() =>
        {
            Assert.Equal(1, updateInterop.SynchronizationCalls);
            Assert.Single(host.FindComponents<MkUpdated>());
        });
    }

    [Fact]
    public void DeniedBrowserStorageProducesOnlyTheSafeDiagnosticAndNoPopup()
    {
        RecordingClientUpdateInterop updateInterop = RegisterDependencies(
            new ClientVersionStorageSnapshot(
                PreviousVersion: null,
                Changed: false,
                Available: false,
                ErrorCode: "untrusted-browser-detail"));
        AuthenticationState state = new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "alice")],
            authenticationType: "tests")));

        IRenderedComponent<CascadingValue<Task<AuthenticationState>>> host =
            Render<CascadingValue<Task<AuthenticationState>>>(parameters => parameters
                .Add(cascade => cascade.Value, Task.FromResult(state))
                .AddChildContent<MisskeyClientUpdateHost>());

        host.WaitForAssertion(() => Assert.Equal(1, updateInterop.SynchronizationCalls));
        Assert.Empty(host.FindComponents<MkUpdated>());
        string warning = Assert.Single(logger.Messages);
        Assert.Contains("CLIENT_VERSION_STORAGE_UNAVAILABLE", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("untrusted-browser-detail", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleOrCspJavascriptFailureRemainsObservable()
    {
        RecordingClientUpdateInterop updateInterop = RegisterDependencies();
        updateInterop.SynchronizationFailure = new JSException("CLIENT_UPDATE_MODULE_EVALUATION_FAILED");
        AuthenticationState state = new(new ClaimsPrincipal(new ClaimsIdentity()));

        JSException exception = Assert.Throws<JSException>(() =>
            Render<CascadingValue<Task<AuthenticationState>>>(parameters => parameters
                .Add(cascade => cascade.Value, Task.FromResult(state))
                .AddChildContent<MisskeyClientUpdateHost>()));

        Assert.Equal("CLIENT_UPDATE_MODULE_EVALUATION_FAILED", exception.Message);
        Assert.Empty(logger.Messages);
    }

    [Theory]
    [InlineData("12.119.2", "12.119.1", 1)]
    [InlineData("v12.119.2", "12.119.2", 0)]
    [InlineData("12.119.2-beta.2", "12.119.2-beta.1", 1)]
    [InlineData("12.119.2", "12.119.2-rc.1", 1)]
    [InlineData("12.119.2-port.1", "12.119.2", -1)]
    [InlineData("12.119", "12.119.0", 0)]
    public void VersionComparisonMatchesCompareVersionsFive(string current, string previous, int expected)
    {
        Assert.True(MisskeyClientVersionComparer.TryCompare(current, previous, out int actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InvalidLegacyVersionIsSafelyRejectedLikeThePinnedTryCatch()
    {
        Assert.False(MisskeyClientVersionComparer.TryCompare(
            "12.119.2",
            "not-a-version",
            out int comparison));
        Assert.Equal(0, comparison);
    }

    private RecordingClientUpdateInterop RegisterDependencies(ClientVersionStorageSnapshot? snapshot = null)
    {
        var update = new RecordingClientUpdateInterop(
            snapshot ?? new ClientVersionStorageSnapshot(null, Changed: true));
        Services.AddSingleton<IClientUpdateInterop>(update);
        Services.AddSingleton<ISparkleInterop>(new NoOpSparkleInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IButtonRippleInterop>(new NoOpButtonInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new UpdatedLocalizer());
        logger = new RecordingLogger<MisskeyClientUpdateHost>();
        Services.AddSingleton<ILogger<MisskeyClientUpdateHost>>(logger);
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            SourceUrl: null,
            PublicBaseUri: new Uri("https://social.example", UriKind.Absolute)));
        return update;
    }

    private RecordingLogger<MisskeyClientUpdateHost> logger = new();

    private sealed class RecordingClientUpdateInterop(ClientVersionStorageSnapshot snapshot) : IClientUpdateInterop
    {
        public RecordingJsReference Handle { get; } = new();

        public MkUpdated? Receiver { get; private set; }

        public Uri? ReleaseNotesUrl { get; private set; }

        public int SynchronizationCalls { get; private set; }

        public JSException? SynchronizationFailure { get; set; }

        public ValueTask<ClientVersionStorageSnapshot> SynchronizeVersionAsync(
            string currentVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(MisskeyFrontendRuntimeConfiguration.PortVersion, currentVersion);
            SynchronizationCalls++;
            if (SynchronizationFailure is not null)
            {
                return ValueTask.FromException<ClientVersionStorageSnapshot>(SynchronizationFailure);
            }

            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<IJSObjectReference> AttachDialogAsync(
            ElementReference modal,
            ElementReference content,
            ElementReference panel,
            DotNetObjectReference<MkUpdated> receiver,
            Uri releaseNotesUrl,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Receiver = receiver.Value;
            ReleaseNotesUrl = releaseNotesUrl;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpSparkleInterop : ISparkleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference content,
            DotNetObjectReference<MkSparkle> receiver,
            long generation,
            bool animationEnabled,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingJsReference());

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

    private sealed class NoOpButtonInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingJsReference());

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool autofocus,
            CancellationToken cancellationToken) => AttachAsync(element, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UpdatedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";

        public string Direction => "ltr";

        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo("ja-JP");

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "misskeyUpdated" => "Misskeyが更新されました！",
            "whatIsNew" => "変更点",
            "gotIt" => "わかった",
            _ => throw new KeyNotFoundException(key)
        };

        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class RecordingJsReference : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];

        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Warning, logLevel);
            Assert.Equal(4701, eventId.Id);
            Assert.Null(exception);
            Messages.Add(formatter(state, exception));
        }
    }
}
