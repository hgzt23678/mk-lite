using System.Globalization;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Layouts;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class VisitorShellTests : BunitContext
{
    private readonly RecordingVisitorShellInterop visitorInterop = new();

    public VisitorShellTests()
    {
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            SourceUrl: null,
            PublicBaseUri: new Uri("https://configured.example", UriKind.Absolute),
            LocalAccountsEnabled: true));
        Services.AddSingleton<IInstancePresentationService>(new VisitorInstanceService());
        Services.AddSingleton<IAnnouncementPresentationService>(new VisitorAnnouncementService());
        Services.AddSingleton<IMfmParserInterop>(new VisitorMfmParserInterop());
        Services.AddSingleton<IVisitorShellInterop>(visitorInterop);
        Services.AddSingleton<IMisskeyLocalizer>(new VisitorLocalizer());
        Services.AddScoped<IMisskeyOverlayService, MisskeyOverlayService>();
    }

    [Fact]
    public async Task NonRootShellSwitchesBetweenPinnedSideBannerAndHeaderBreakpoints()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("about-misskey");
        IRenderedComponent<VisitorShell> component = Render<VisitorShell>(parameters => parameters
            .AddChildContent("page"));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".mk-app > .side > .kanban.rwqkcmrc"));
            Assert.NotNull(component.Find(".kanban .wrapper > h1.full"));
            Assert.Equal(2, component.FindAll(".kanban .wrapper > .action > button").Count);
            Assert.Equal("Federated visitor surface", component.Find(".kanban .about > .desc").TextContent);
            Assert.Equal("Scheduled maintenance", component.Find(".kanban .announcements .item > .title").TextContent);
        });

        await component.InvokeAsync(() => component.Instance.UpdateVisitorMetrics(1024, 1024, "Misskeyについて"));
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll(".mk-app > .side"));
            Assert.NotNull(component.Find(".mk-app > .main > .banner.rwqkcmrc"));
            Assert.Empty(component.FindAll(".banner .wrapper > h1.full"));
            Assert.NotNull(component.Find(".header.sqxihjet > .narrow"));
            Assert.Equal("Misskeyについて", component.Find(".header .narrow > .title").TextContent);
        });

        await component.InvokeAsync(() => component.Instance.UpdateVisitorMetrics(1440, 940, "Misskeyについて"));
        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".mk-app > .side"));
            Assert.Empty(component.FindAll(".mk-app > .main > .banner"));
            Assert.NotNull(component.Find(".header.sqxihjet > .narrow"));
        });

        await component.InvokeAsync(() => component.Instance.UpdateVisitorMetrics(1920, 1420, "Misskeyについて"));
        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".header.sqxihjet > .wide > .content"));
            Assert.Equal(4, component.FindAll(".header .wide > .content > a.link").Count);
            Assert.Equal("ホーム", component.Find(".header a.link[href='']").TextContent);
            Assert.Equal("新規登録", component.Find(".header button.signup").TextContent);
            Assert.Equal("ログイン", component.Find(".header button.login").TextContent);
            Assert.Equal("configured.example", component.Find(".contents > .powered-by > b").TextContent);
        });
    }

    [Fact]
    public void HeaderPreservesPageIconActionAndAttributeFallthrough()
    {
        bool actioned = false;
        IRenderedComponent<VisitorHeader> component = Render<VisitorHeader>(parameters => parameters
            .Add(value => value.Narrow, false)
            .Add(value => value.PageTitle, "固定ページ")
            .Add(value => value.PageIcon, "fas fa-star")
            .Add(value => value.PageAction, EventCallback.Factory.Create(this, () => actioned = true))
            .AddUnmatched("class", "fixture-header")
            .AddUnmatched("data-contract", "visitor-header"));

        Assert.Contains("fixture-header", component.Find(".sqxihjet").ClassList);
        Assert.Equal("visitor-header", component.Find(".sqxihjet").GetAttribute("data-contract"));
        Assert.Contains("fa-star", component.Find(".page > .title > i.icon").ClassList);
        Assert.Equal("固定ページ", component.Find(".page > .title > .text").TextContent);

        component.Find(".page > button.action").Click();

        Assert.True(actioned);
    }

    [Fact]
    public async Task NarrowTrayUsesVueMotionClassesAndRemainsUntilLeaveCompletes()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("about-misskey");
        IRenderedComponent<VisitorShell> component = Render<VisitorShell>();
        await component.InvokeAsync(() => component.Instance.UpdateVisitorMetrics(390, 390, "Misskeyについて"));

        component.Find(".header .narrow > button.menu").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Contains("tray-back-enter-active", component.Find(".mk-app > .menu-back").ClassList);
            Assert.Contains("tray-back-enter-from", component.Find(".mk-app > .menu-back").ClassList);
            Assert.Contains("tray-enter-active", component.Find(".mk-app > .menu").ClassList);
            Assert.Contains("tray-enter-from", component.Find(".mk-app > .menu").ClassList);
            Assert.Equal(4, component.FindAll(".mk-app > .menu > a.link").Count);
            Assert.Equal("true", component.Find(".header button.menu").GetAttribute("aria-expanded"));
        });

        await component.InvokeAsync(() => component.Instance.NotifyTrayEntered());
        component.Find(".mk-app").KeyDown("Escape");
        component.WaitForAssertion(() => Assert.NotNull(component.Find(".mk-app > .menu")));

        await component.InvokeAsync(() => component.Instance.NotifyTrayLeft());
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll(".mk-app > .menu"));
            Assert.Empty(component.FindAll(".mk-app > .menu-back"));
            Assert.Equal("false", component.Find(".header button.menu").GetAttribute("aria-expanded"));
        });
        Assert.Contains("beginEnter", visitorInterop.Attachment.Invocations);
        Assert.Contains("beginLeave", visitorInterop.Attachment.Invocations);
    }

    [Fact]
    public void RootKeepsPinnedVisitorHierarchyWithoutNonRootChrome()
    {
        IRenderedComponent<VisitorShell> component = Render<VisitorShell>(parameters => parameters
            .AddChildContent(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "rsqzvsbo");
                builder.CloseElement();
            }));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".mk-app > .github-corner"));
            Assert.NotNull(component.Find(".mk-app > .main > .contents > main > .rsqzvsbo"));
            Assert.Empty(component.FindAll(".mk-app > .side"));
            Assert.Empty(component.FindAll(".mk-app .header.sqxihjet"));
            Assert.Empty(component.FindAll(".mk-app .powered-by"));
        });
    }

    [Theory]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://user:password@example.test/background.png", false)]
    [InlineData("https://cdn.example.test/background.png", false)]
    [InlineData("/media/proxy/9fce/image", true)]
    public void KanbanOnlyEmitsSafeHttpBackgroundImages(string background, bool expected)
    {
        var instance = VisitorInstanceService.Create() with { BackgroundImageUrl = background };
        IRenderedComponent<VisitorKanban> component = Render<VisitorKanban>(parameters => parameters
            .Add(value => value.Instance, instance)
            .Add(value => value.Full, true));

        string? style = component.Find(".rwqkcmrc").GetAttribute("style");
        Assert.Equal(expected, style?.Contains("background-image", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void TransparentKanbanSuppressesConfiguredBackgroundAndPreservesFallthrough()
    {
        var instance = VisitorInstanceService.Create() with
        {
            BackgroundImageUrl = "/media/proxy/background"
        };
        IRenderedComponent<VisitorKanban> component = Render<VisitorKanban>(parameters => parameters
            .Add(value => value.Instance, instance)
            .Add(value => value.Transparent, true)
            .AddUnmatched("class", "fixture-kanban")
            .AddUnmatched("data-contract", "visitor-kanban"));

        AngleSharp.Dom.IElement root = component.Find(".rwqkcmrc");
        Assert.Contains("fixture-kanban", root.ClassList);
        Assert.Equal("visitor-kanban", root.GetAttribute("data-contract"));
        Assert.Equal("background-image: none", root.GetAttribute("style"));
        Assert.Contains("transparent", component.Find(".rwqkcmrc > .back").ClassList);
        Assert.DoesNotContain("/media/proxy/background", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void KanbanNeverEmitsRemoteAnnouncementOrLogoUrls()
    {
        var instance = VisitorInstanceService.Create() with
        {
            LogoImageUrl = "https://tracker.example.test/logo.png",
            BackgroundImageUrl = "/static-assets/background.png"
        };
        IRenderedComponent<VisitorKanban> component = Render<VisitorKanban>(parameters => parameters
            .Add(value => value.Instance, instance)
            .Add(value => value.Full, true)
            .Add(value => value.Announcements,
            [
                new("remote", "Remote image", "Must not contact the remote origin.", "https://tracker.example.test/announcement.png"),
                new("local", "Cached image", "Uses the same-origin cache.", "/media/cached.png")
            ]));

        Assert.Empty(component.FindAll("h1 img.logo"));
        Assert.Single(component.FindAll(".announcements img"));
        Assert.Equal("/media/cached.png", component.Find(".announcements img").GetAttribute("src"));
        Assert.DoesNotContain("tracker.example.test", component.Markup, StringComparison.Ordinal);
    }

    private sealed class VisitorInstanceService : IInstancePresentationService
    {
        public static InstanceSummaryViewModel Create() => new(
            "Production instance",
            "Federated visitor surface",
            "12.119.2-server",
            "/static-assets/favicon.png",
            BackgroundImageUrl: "https://cdn.example.test/background.png",
            LogoImageUrl: "https://cdn.example.test/logo.png",
            DisableRegistration: false,
            EmailRequiredForSignup: false,
            EnableEmail: false,
            TosUrl: null);

        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Create());

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class VisitorAnnouncementService : IAnnouncementPresentationService
    {
        public Task<IReadOnlyList<VisitorAnnouncementViewModel>> ReadPublicAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VisitorAnnouncementViewModel>>(
            [
                new("9announcement", "Scheduled maintenance", "Service work is scheduled.", "/static-assets/favicon.png")
            ]);
    }

    private sealed class VisitorMfmParserInterop : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<MfmNode>>(
            [
                new("text", JsonSerializer.SerializeToElement(new { text }), Children: null)
            ]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class VisitorLocalizer : IMisskeyLocalizer
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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "home" => "ホーム",
            "explore" => "みつける",
            "featured" => "ハイライト",
            "channel" => "チャンネル",
            "search" => "検索",
            "signup" => "新規登録",
            "login" => "ログイン",
            "menu" => "メニュー",
            "announcements" => "お知らせ",
            "introMisskey" => "Misskeyでつながろう",
            _ => key
        };

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }

    private sealed class RecordingVisitorShellInterop : IVisitorShellInterop
    {
        public RecordingJsObjectReference Attachment { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(Attachment);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingJsObjectReference : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

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
}
