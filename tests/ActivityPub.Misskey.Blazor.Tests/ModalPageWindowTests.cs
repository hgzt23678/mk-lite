using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
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

public sealed class ModalPageWindowTests : BunitContext
{
    private readonly RecordingModalInterop modalInterop;
    private readonly MisskeyOverlayService overlays = new();

    public ModalPageWindowTests()
    {
        modalInterop = new RecordingModalInterop(new("dialog", false, null, "center", 0, 1_000_100));
        Services.AddSingleton<IMkModalInterop>(modalInterop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IStickyContainerInterop>(new NoOpStickyContainerInterop());
        Services.AddSingleton<IPageHeaderInterop>(new NoOpPageHeaderInterop());
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<ICurrentAccountPresentationService>(new FixedCurrentAccountService());
        Services.AddSingleton<IClipboardInterop>(new RecordingClipboardInterop());
        Services.AddSingleton<IModalPageWindowInterop>(new RecordingWindowInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example", UriKind.Absolute)));
    }

    [Fact]
    public void PreservesPinnedModalHeaderBodyPageHeaderFooterAndContextMenuContract()
    {
        using IRenderedComponent<MkModalPageWindow> component = RenderWindow();

        component.WaitForAssertion(() => Assert.Single(modalInterop.Attachments));
        IElement modal = component.Find(".qzhlnise.dialog.contract-window[data-contract=modal-page-window]");
        Assert.Contains("modal-enter-active", modal.ClassList);
        IElement root = component.Find(".qzhlnise > .content > .hrmcaedk._narrow_");
        Assert.Equal("width: 860px; height: min(660px, 100%);", root.GetAttribute("style"));

        IElement header = root.QuerySelector(":scope > .header")!;
        Assert.NotNull(header.QuerySelector(":scope > span[style='display: inline-block; width: 20px']"));
        Assert.Equal("First page", header.QuerySelector(":scope > .title > span")?.TextContent);
        Assert.NotNull(header.QuerySelector(":scope > .title > .icon.fas.fa-home"));
        Assert.Equal("Close", header.QuerySelector(":scope > button:last-child")?.GetAttribute("aria-label"));

        Assert.NotNull(root.QuerySelector(":scope > .body .fdidabkb.thin > .tabs"));
        Assert.Equal("/first", root.QuerySelector("[data-page]")?.GetAttribute("data-path"));
        Assert.Equal("First footer", root.QuerySelector("footer[data-page-footer]")?.TextContent);

        header.ContextMenu();
        MisskeyOverlayEntry menu = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PopupMenu, menu.Kind);
        Assert.Equal("/first", menu.MenuItems[0].Text);
        Assert.Equal(
            ["Show in page", "Pop-out", "Open in new tab", "Copy link"],
            menu.MenuItems.Where(item => item.Kind == MisskeyMenuItemKind.Action).Select(item => item.Text));
    }

    [Fact]
    public async Task NavigationRecordsHistoryAndBackRestoresThePreviousPathAndMetadata()
    {
        var paths = new List<string>();
        using IRenderedComponent<MkModalPageWindow> component = RenderWindow(paths);

        component.Find("[data-navigate]").Click();
        Assert.Equal("/second", component.Instance.CurrentPath);
        Assert.Equal("Second page", component.Find(".hrmcaedk > .header > .title > span").TextContent);
        Assert.Equal("/second", component.Find("[data-page]").GetAttribute("data-path"));
        Assert.Equal("Back", component.Find(".hrmcaedk > .header > button:first-child").GetAttribute("aria-label"));
        Assert.Equal(["/second"], paths);

        component.Find(".hrmcaedk > .header > button:first-child").Click();
        Assert.Equal("/first", component.Instance.CurrentPath);
        Assert.Equal("First page", component.Find(".hrmcaedk > .header > .title > span").TextContent);
        Assert.NotNull(component.Find(".hrmcaedk > .header > span:first-child"));
        Assert.Equal(["/second", "/first"], paths);

        await component.InvokeAsync(() => component.Instance.NavigateAsync("/first"));
        Assert.Equal(["/second", "/first"], paths);
    }

    [Fact]
    public async Task DrawerPlacementBackgroundClickAndCloseUseThePinnedModalMotionContract()
    {
        modalInterop.Placement = new("drawer", true, 480, "center", 0, 1_000_100);
        int clicked = 0;
        int closed = 0;
        using IRenderedComponent<MkModalPageWindow> component = RenderWindow(
            clicked: () => clicked++,
            closed: () => closed++,
            maxWidth: 720,
            maxHeight: 480);

        component.WaitForAssertion(() => Assert.Single(modalInterop.Attachments));
        Assert.Equal("auto", modalInterop.Attachments[0].PreferType);
        Assert.NotNull(component.Find(".qzhlnise.drawer.modal-drawer-enter-active"));
        Assert.Equal(
            "width: 720px; height: min(480px, 100%);",
            component.Find(".hrmcaedk").GetAttribute("style"));

        IRenderedComponent<MkModal> modalComponent = component.FindComponent<MkModal>();
        await modalComponent.InvokeAsync(modalComponent.Instance.NotifyClicked);
        Assert.Equal(1, clicked);
        Assert.DoesNotContain("hide", modalInterop.Handle.Invocations);

        component.Find(".hrmcaedk > .header > button:last-child").Click();
        component.WaitForAssertion(() => Assert.Contains("hide", modalInterop.Handle.Invocations));
        Assert.Contains("modal-drawer-leave-active", component.Find(".qzhlnise.drawer").ClassList);
        await modalComponent.InvokeAsync(modalComponent.Instance.NotifyClosed);
        Assert.Equal(1, closed);
    }

    private IRenderedComponent<MkModalPageWindow> RenderWindow(
        List<string>? paths = null,
        Action? clicked = null,
        Action? closed = null,
        int maxWidth = 860,
        int maxHeight = 660) => Render<MkModalPageWindow>(parameters => parameters
        .Add(window => window.InitialPath, "/first")
        .Add(window => window.MaxWidth, maxWidth)
        .Add(window => window.MaxHeight, maxHeight)
        .Add(window => window.MetadataResolver, Metadata)
        .Add(window => window.PathChanged, path => paths?.Add(path))
        .Add(window => window.Clicked, () => clicked?.Invoke())
        .Add(window => window.Closed, () => closed?.Invoke())
        .Add(window => window.ChildContent, context => builder =>
        {
            builder.OpenElement(0, "section");
            builder.AddAttribute(1, "data-page", true);
            builder.AddAttribute(2, "data-path", context.Path);
            builder.AddContent(3, context.Path == "/first" ? "First body" : "Second body");
            if (context.Path == "/first")
            {
                builder.OpenElement(4, "button");
                builder.AddAttribute(5, "type", "button");
                builder.AddAttribute(6, "data-navigate", true);
                builder.AddAttribute(7, "onclick", EventCallback.Factory.Create(this, () => context.NavigateAsync("/second")));
                builder.AddContent(8, "Next");
                builder.CloseElement();
            }
            builder.CloseElement();
        })
        .Add(window => window.Footer, context => builder =>
        {
            builder.OpenElement(0, "footer");
            builder.AddAttribute(1, "data-page-footer", true);
            builder.AddContent(2, context.Path == "/first" ? "First footer" : "Second footer");
            builder.CloseElement();
        })
        .AddUnmatched("class", "contract-window")
        .AddUnmatched("data-contract", "modal-page-window"));

    private static MkModalPageWindowMetadata Metadata(string path) => new(
        new MkPageHeaderMetadata(
            path == "/first" ? "First page" : "Second page",
            Icon: path == "/first" ? "fas fa-home" : "fas fa-stream"),
        Tabs:
        [
            new MkPageHeaderTab("overview", "Overview", "fas fa-home"),
            new MkPageHeaderTab("activity", "Activity", "fas fa-stream")
        ],
        ActiveTab: path == "/first" ? "overview" : "activity");

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = propertyName switch
            {
                "animation" => true,
                "disableDrawer" => false,
                _ => fallback!
            };
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FixedCurrentAccountService : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new NoteAuthorViewModel(
            "alice",
            "alice",
            "alice",
            "Alice",
            "/static-assets/favicon.png",
            IsBot: false));
    }

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "goBack" => "Back",
            "close" => "Close",
            "showInPage" => "Show in page",
            "popout" => "Pop-out",
            "openInNewTab" => "Open in new tab",
            "copyLink" => "Copy link",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class RecordingModalInterop(MkModalBrowserPlacement placement) : IMkModalInterop
    {
        public MkModalBrowserPlacement Placement { get; set; } = placement;
        public List<MkModalInteropOptions> Attachments { get; } = [];
        public RecordingJsObject Handle { get; } = new();

        public ValueTask<MkModalAttachment> AttachAsync(
            ElementReference? source,
            ElementReference modal,
            ElementReference background,
            ElementReference content,
            DotNetObjectReference<MkModal> receiver,
            MkModalInteropOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attachments.Add(options);
            return ValueTask.FromResult(new MkModalAttachment(Handle, Placement));
        }

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
            where T : class => ValueTask.FromResult<IJSObjectReference>(new RecordingJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpPageHeaderInterop : IPageHeaderInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new RecordingJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingClipboardInterop : IClipboardInterop
    {
        public ValueTask<ClipboardWriteResult> WriteTextAsync(
            string value,
            CancellationToken cancellationToken) => ValueTask.FromResult(new ClipboardWriteResult(true, "test", null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingWindowInterop : IModalPageWindowInterop
    {
        public ValueTask<bool> OpenNewTabAsync(Uri url, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask<bool> PopoutAsync(
            Uri url,
            ElementReference window,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingJsObject : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];

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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
