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

public sealed class AboutPageTests : BunitContext
{
    [Fact]
    public async Task PreservesOverviewHashFederationFiltersAndOffsetPagination()
    {
        var about = new RecordingAboutService();
        var browser = new NoOpBrowserInterop();
        Services.AddSingleton<IInstancePresentationService>(new FixedInstanceService());
        Services.AddSingleton<IAboutPresentationService>(about);
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            new Uri("https://source.example.test/activitypub-server", UriKind.Absolute),
            new Uri("https://social.example.test", UriKind.Absolute)));
        Services.AddSingleton<IMisskeyLocalizer>(new AboutLocalizer());
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddSingleton<IMisskeyTransientFeedbackService>(new MisskeyTransientFeedbackService());
        Services.AddSingleton<ICurrentAccountPresentationService>(new UnusedCurrentAccount());
        Services.AddSingleton<IPizzaxDeviceState>(new MotionDisabledDeviceState());
        Services.AddSingleton<IStickyContainerInterop>(browser);
        Services.AddSingleton<IPageHeaderInterop>(browser);
        Services.AddSingleton<ISpacerInterop>(browser);
        Services.AddSingleton<IFormSuspenseInterop>(browser);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IPaginationInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IClipboardInterop>(browser);

        using IRenderedComponent<AboutPage> component = Render<AboutPage>();
        component.WaitForAssertion(() => Assert.NotNull(component.Find(".fwhjspax > .content")));

        Assert.Equal("Instance information", component.Find(".titleContainer .title .title").TextContent);
        Assert.Equal(["Overview", "Federation"], component.FindAll(".fdidabkb .tabs > button.tab")
            .Select(element => element.TextContent.Trim()));
        Assert.DoesNotContain("Custom emojis", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Charts", component.Markup, StringComparison.Ordinal);
        Assert.Equal("Browser instance", component.Find(".fwhjspax .name").TextContent.Trim());
        Assert.Contains("background-image: url('/static-assets/favicon.png')", component.Find(".fwhjspax").GetAttribute("style"));
        Assert.Contains("12,345", component.Markup, StringComparison.Ordinal);
        Assert.Contains("67,890", component.Markup, StringComparison.Ordinal);
        Assert.Equal(5, component.FindAll(".vrtktovh:last-child .ffcbddfc").Count);
        Assert.Contains("https://social.example.test/.well-known/nodeinfo", component.Markup, StringComparison.Ordinal);

        component.Find("button[title='Federation']").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find(".taeiyria > .query")));
        Assert.EndsWith("/about#federation", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
        component.WaitForAssertion(() => Assert.Equal(10, component.FindAll(".dqokceoi > a.instance").Count));
        Assert.Equal(new AboutFederationQuery(null, "federating", "+pubSub", 11, 0), about.LastQuery);

        component.Find(".cxiknjgy button").Click();
        component.WaitForAssertion(() => Assert.Equal(40, component.FindAll(".dqokceoi > a.instance").Count));
        Assert.Equal(new AboutFederationQuery(null, "federating", "+pubSub", 31, 10), about.LastQuery);

        var source = new AboutFederationPaginationSource(about, "mastodon", "blocked", "-notes");
        await source.FetchAsync(new MisskeyPaginationRequest(11, Offset: 20), CancellationToken.None);
        Assert.Equal(new AboutFederationQuery("mastodon", "blocked", "-notes", 11, 20), about.LastQuery);
        await Assert.ThrowsAsync<AboutPresentationException>(() => source.FetchAsync(
            new MisskeyPaginationRequest(11, SinceId: "cursor"),
            CancellationToken.None).AsTask());

        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/about");
        component.WaitForAssertion(() => Assert.NotNull(component.Find(".fwhjspax")));
    }

    private sealed class FixedInstanceService : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new InstanceSummaryViewModel(
            "Browser instance",
            "About description",
            "12.119.2-test",
            "/static-assets/favicon.png",
            "/static-assets/favicon.png",
            null,
            DisableRegistration: false,
            EmailRequiredForSignup: true,
            EnableEmail: true,
            TosUrl: "https://terms.example.test/",
            MaintainerName: "Maintainer",
            MaintainerEmail: "maintainer@example.test"));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class RecordingAboutService : IAboutPresentationService
    {
        public AboutFederationQuery? LastQuery { get; private set; }

        public Task<AboutStatisticsViewModel> GetStatisticsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AboutStatisticsViewModel(12_345, 67_890));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
            AboutFederationQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>(Enumerable.Range(1, 42)
                .Select(index => new FederationInstanceViewModel(
                    $"instance-{index}",
                    $"node-{index}.example.test",
                    "/static-assets/favicon.png",
                    SoftwareName: "Misskey",
                    SoftwareVersion: "12.119.2",
                    Name: $"Node {index}",
                    FollowersCount: index,
                    LastCommunicatedAt: new DateTimeOffset(2026, 8, 4, 0, index, 0, TimeSpan.Zero)))
                .Skip(query.Offset)
                .Take(query.Limit)
                .ToArray());
        }
    }

    private sealed class AboutLocalizer : IMisskeyLocalizer
    {
        private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        {
            ["instanceInfo"] = "Instance information",
            ["overview"] = "Overview",
            ["federation"] = "Federation",
            ["customEmojis"] = "Custom emojis",
            ["charts"] = "Charts",
            ["description"] = "Description",
            ["aboutMisskey"] = "About Misskey",
            ["administrator"] = "Administrator",
            ["contact"] = "Contact",
            ["tos"] = "Terms of service",
            ["statistics"] = "Statistics",
            ["users"] = "Users",
            ["notes"] = "Notes",
            ["host"] = "Host",
            ["state"] = "State",
            ["all"] = "All",
            ["federating"] = "Federating",
            ["subscribing"] = "Subscribing",
            ["publishing"] = "Publishing",
            ["suspended"] = "Suspended",
            ["blocked"] = "Blocked",
            ["notResponding"] = "Not responding",
            ["sort"] = "Sort",
            ["pubSub"] = "Pub/Sub",
            ["descendingOrder"] = "Descending",
            ["ascendingOrder"] = "Ascending",
            ["following"] = "Following",
            ["followers"] = "Followers",
            ["registeredAt"] = "Registered at",
            ["lastCommunication"] = "Last communication",
            ["loadMore"] = "Load more",
            ["nothing"] = "Nothing"
        };

        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
            Values.TryGetValue(key, out string? value) ? value : key;
        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MotionDisabledDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default) =>
            string.Equals(propertyName, "animation", StringComparison.Ordinal) && typeof(T) == typeof(bool)
                ? ValueTask.FromResult((T)(object)false)
                : ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class UnusedCurrentAccount : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The public about page must not load a private account projection.");
    }

    private sealed class NoOpBrowserInterop :
        IStickyContainerInterop,
        IPageHeaderInterop,
        ISpacerInterop,
        IFormSuspenseInterop,
        IFormInputInterop,
        IPaginationInterop,
        IButtonRippleInterop,
        IClipboardInterop,
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

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            DotNetObjectReference<FormSuspenseTransitionReceiver> receiver,
            long generation,
            string phase,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(this);

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(this);

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

        public ValueTask<bool> IsTopVisibleAsync(ElementReference root, CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<bool> IsBottomVisibleAsync(ElementReference root, double tolerance, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PaginationScrollSnapshot(0, 0, true, false));
        public ValueTask RestoreScrollAsync(ElementReference root, PaginationScrollSnapshot snapshot, bool stickToBottom, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ScrollToTopAsync(ElementReference root, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<ClipboardWriteResult> WriteTextAsync(string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ClipboardWriteResult(true, "test", null));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
