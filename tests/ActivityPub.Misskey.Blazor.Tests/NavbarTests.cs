using System.Globalization;
using System.Security.Claims;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NavbarTests : BunitContext
{
    [Fact]
    public async Task DesktopNavbarUsesAccountLocalizationAvailableRoutesAndSecurePopupActions()
    {
        (FixedDeviceState device, RecordingNavbarInterop navbar, IMisskeyOverlayService overlays) = Configure(
            ["notifications", "favorites", "drive", "-", "explore", "announcements", "search", "-", "ui"],
            "sideIcon");
        IRenderedComponent<CascadingValue<Task<AuthenticationState>>> host = RenderDesktopAuthorized(
            new Claim(ClaimTypes.Role, "activitypub-admin"));
        IRenderedComponent<MisskeyNavbar> component = host.FindComponent<MisskeyNavbar>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("iconOnly", component.Find(".mvcprjjd").ClassList);
            Assert.Equal("/static-assets/favicon.png", component.Find(".top > .instance > img.icon").GetAttribute("src"));
            Assert.Equal("Localized timeline", component.Find(".middle > a.index > .text").TextContent);
            Assert.Empty(component.FindAll(".middle > .favorites, .middle > .drive, .middle > .explore, .middle > .search, .middle > .ui"));
            Assert.Equal("my/notifications", component.Find(".middle > a.notifications").GetAttribute("href"));
            Assert.Equal("announcements", component.Find(".middle > a.announcements").GetAttribute("href"));
            Assert.Single(component.FindAll(".middle > a[href='admin']"));
            Assert.Single(component.FindAll(".middle > a[href='settings']"));
            Assert.Single(component.FindAll(".middle > button"));
            Assert.Equal("Localized more", component.Find(".middle > button > .text").TextContent);
            Assert.Equal("/static-assets/favicon.png", component.Find(".bottom > .account > .avatar > img.inner").GetAttribute("src"));
            Assert.Contains("@alice", component.Find(".bottom > .account > .mk-acct.text").TextContent, StringComparison.Ordinal);
            Assert.Contains("menu", device.ReadProperties);
            Assert.Contains("menuDisplay", device.ReadProperties);
        });

        component.Find(".top > .instance").Click();
        MisskeyOverlayEntry instancePopup = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PopupMenu, instancePopup.Kind);
        Assert.Equal("left", instancePopup.PopupAlign);
        Assert.Contains(instancePopup.MenuItems, item => item.Kind == MisskeyMenuItemKind.Link && item.Href == "/about-misskey");
        Assert.Contains(instancePopup.MenuItems, item => item.Kind == MisskeyMenuItemKind.Parent && item.Children?.Single().Href == "https://misskey-hub.net/help.html");
        Assert.DoesNotContain(instancePopup.MenuItems, item => item.Href is "/about" or "/my/drive" or "/settings");
        overlays.Close(instancePopup.Id);

        component.Find(".middle > button").Click();
        MisskeyOverlayEntry morePopup = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.LaunchPad, morePopup.Kind);
        MisskeyLaunchPadOptions launchPad = Assert.IsType<MisskeyLaunchPadOptions>(morePopup.LaunchPad);
        Assert.NotNull(launchPad.Source);
        MisskeyMenuItem reload = Assert.Single(launchPad.Items);
        Assert.Equal("Localized reload", reload.Text);
        Assert.Equal(MisskeyMenuItemKind.Action, reload.Kind);
        overlays.Close(morePopup.Id);

        component.Find(".bottom > .account").Click();
        MisskeyOverlayEntry accountPopup = Assert.Single(overlays.Entries);
        Assert.Contains(accountPopup.MenuItems, item => item.Kind == MisskeyMenuItemKind.User && item.User?.Acct == "alice");
        MisskeyMenuItem logout = Assert.Single(accountPopup.MenuItems, item => item.Text == "Localized logout");
        Assert.True(logout.Danger);
        await logout.Action!();
        Assert.Equal(1, navbar.SubmitCalls);
        overlays.Close(accountPopup.Id);

        component.Find(".bottom > .post").Click();
        Assert.Contains(overlays.Entries, item => item.Kind == MisskeyOverlayKind.PostForm);
    }

    [Fact]
    public async Task MobileAndResponsiveContractsFollowMenuDisplayWithoutRenderingUnsupportedEntries()
    {
        (FixedDeviceState device, RecordingNavbarInterop navbar, IMisskeyOverlayService overlays) = Configure(
            ["notifications", "-", "drive"],
            "sideFull");
        IRenderedComponent<CascadingValue<Task<AuthenticationState>>> mobileHost = RenderMobileAuthorized();
        IRenderedComponent<MisskeyNavbarMobile> mobile = mobileHost.FindComponent<MisskeyNavbarMobile>();

        mobile.WaitForAssertion(() =>
        {
            Assert.NotNull(mobile.Find(".kmwsukvl > .body > .top > .instance"));
            Assert.Single(mobile.FindAll(".kmwsukvl > .body > .middle > button"));
            Assert.Single(mobile.FindAll(".kmwsukvl > .body > .middle > .divider"));
            Assert.Empty(mobile.FindAll(".drive"));
            Assert.Equal("my/notifications", mobile.Find("a.notifications").GetAttribute("href"));
        });
        Assert.Contains("menu", device.ReadProperties);
        Assert.DoesNotContain("menuDisplay", device.ReadProperties);

        mobile.Find(".middle > button").Click();
        MisskeyOverlayEntry launchPadEntry = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.LaunchPad, launchPadEntry.Kind);
        MisskeyLaunchPadOptions mobileLaunchPad = Assert.IsType<MisskeyLaunchPadOptions>(launchPadEntry.LaunchPad);
        Assert.Null(mobileLaunchPad.Source);
        Assert.Contains(mobileLaunchPad.Items, item =>
            item.Kind == MisskeyMenuItemKind.Link && item.Href == "announcements");
        Assert.Equal("Localized reload", Assert.Single(mobileLaunchPad.Items, item =>
            item.Kind == MisskeyMenuItemKind.Action).Text);
        overlays.Close(launchPadEntry.Id);

        mobile.Find(".top > .instance").Click();
        MisskeyOverlayEntry instancePopup = Assert.Single(overlays.Entries);
        Assert.Contains(instancePopup.MenuItems, item => item.Href == "/about-misskey");
        overlays.Close(instancePopup.Id);

        mobile.Find(".bottom > .post").Click();
        MisskeyOverlayEntry postForm = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PostForm, postForm.Kind);
        overlays.Close(postForm.Id);

        mobile.Find(".bottom > .account").Click();
        MisskeyOverlayEntry accountPopup = Assert.Single(overlays.Entries);
        MisskeyMenuItem logout = Assert.Single(accountPopup.MenuItems, item => item.Text == "Localized logout");
        await logout.Action!();
        Assert.Equal(1, navbar.SubmitCalls);
        overlays.Close(accountPopup.Id);

        IRenderedComponent<CascadingValue<Task<AuthenticationState>>> desktopHost = RenderDesktopAuthorized();
        IRenderedComponent<MisskeyNavbar> desktop = desktopHost.FindComponent<MisskeyNavbar>();
        await desktop.InvokeAsync(() => desktop.Instance.UpdateViewport(1279));
        Assert.Contains("iconOnly", desktop.Find(".mvcprjjd").ClassList);
        await desktop.InvokeAsync(() => desktop.Instance.UpdateViewport(1280));
        Assert.DoesNotContain("iconOnly", desktop.Find(".mvcprjjd").ClassList);
    }

    private (FixedDeviceState Device, RecordingNavbarInterop Navbar, IMisskeyOverlayService Overlays) Configure(
        string[] menu,
        string menuDisplay)
    {
        var device = new FixedDeviceState(menu, menuDisplay);
        var navbar = new RecordingNavbarInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IInstancePresentationService>(new FixedInstance());
        Services.AddSingleton<ICurrentAccountPresentationService>(new FixedCurrentAccount());
        Services.AddSingleton<IPizzaxDeviceState>(device);
        Services.AddSingleton<IViewportInterop>(new RecordingViewportInterop());
        Services.AddSingleton<INavbarInterop>(navbar);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example", UriKind.Absolute),
            LocalAccountsEnabled: true));
        JSInterop.Mode = JSRuntimeMode.Loose;
        return (device, navbar, overlays);
    }

    private IRenderedComponent<CascadingValue<Task<AuthenticationState>>> RenderDesktopAuthorized(Claim? role = null)
    {
        Task<AuthenticationState> state = CreateAuthenticationState(role);
        return Render<CascadingValue<Task<AuthenticationState>>>(parameters => parameters
            .Add(value => value.Value, state)
            .AddChildContent<MisskeyNavbar>());
    }

    private IRenderedComponent<CascadingValue<Task<AuthenticationState>>> RenderMobileAuthorized(Claim? role = null)
    {
        Task<AuthenticationState> state = CreateAuthenticationState(role);
        return Render<CascadingValue<Task<AuthenticationState>>>(parameters => parameters
            .Add(value => value.Value, state)
            .AddChildContent<MisskeyNavbarMobile>());
    }

    private static Task<AuthenticationState> CreateAuthenticationState(Claim? role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "alice-id"),
            new(ClaimTypes.Name, "alice"),
            new("preferred_username", "alice")
        };
        if (role is not null)
        {
            claims.Add(role);
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "navbar-tests", ClaimTypes.Name, ClaimTypes.Role));
        return Task.FromResult(new AuthenticationState(principal));
    }

    private sealed class FixedInstance : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(
            new InstanceSummaryViewModel(
                "Production instance",
                "Federated server",
                "12.119.2-server",
                "/static-assets/favicon.png",
                "/static-assets/banner.webp",
                null,
                DisableRegistration: true,
                EmailRequiredForSignup: false,
                EnableEmail: false,
                TosUrl: null));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class FixedCurrentAccount : ICurrentAccountPresentationService
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

    private sealed class FixedDeviceState(string[] menu, string menuDisplay) : IPizzaxDeviceState
    {
        public List<string> ReadProperties { get; } = [];

        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            ReadProperties.Add(propertyName);
            object value = propertyName switch
            {
                "menu" => menu,
                "menuDisplay" => menuDisplay,
                _ => fallback!
            };
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingViewportInterop : IViewportInterop
    {
        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingNavbarInterop : INavbarInterop
    {
        public int SubmitCalls { get; private set; }

        public ValueTask SubmitAsync(ElementReference form, CancellationToken cancellationToken)
        {
            SubmitCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedLocalizer : IMisskeyLocalizer
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
            "instance" => "インスタンス",
            "timeline" => "Localized timeline",
            "more" => "Localized more",
            "note" => "Localized note",
            "account" => "Localized account",
            "help" => "Localized help",
            "document" => "Localized documentation",
            "aboutMisskey" => "Localized about Misskey",
            "reload" => "Localized reload",
            "logout" => "Localized logout",
            "controlPanel" => "Localized control panel",
            "settings" => "Localized settings",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
