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

public sealed class PageWindowTests : BunitContext
{
    private readonly RecordingWindowInterop windowInterop = new();
    private readonly MisskeyOverlayService overlays = new();

    public PageWindowTests()
    {
        Services.AddSingleton<IMkWindowInterop>(windowInterop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IClipboardInterop>(new RecordingClipboardInterop());
        Services.AddSingleton<IModalPageWindowInterop>(new RecordingPageWindowInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example", UriKind.Absolute)));
    }

    [Fact]
    public void RendersPinnedWindowHeaderButtonsAndContextMenuContract()
    {
        using IRenderedComponent<MkPageWindow> component = RenderWindow();

        component.WaitForAssertion(() => Assert.Single(windowInterop.Attachments));
        Assert.Equal(500, windowInterop.Attachments[0].InitialWidth);
        Assert.Equal(500, windowInterop.Attachments[0].InitialHeight);
        Assert.True(windowInterop.Attachments[0].CanResize);

        IElement header = component.Find(".ebkgocck .header");
        Assert.Equal("First page", header.QuerySelector(":scope > .title > span")?.TextContent);
        Assert.NotNull(header.QuerySelector(":scope > .title > .icon.fas.fa-home"));
        Assert.NotNull(header.QuerySelector(":scope > .right > button > i.fas.fa-expand-alt"));
        Assert.NotNull(header.QuerySelector(":scope > .right > button > i.fas.fa-window-maximize"));
        Assert.Null(header.QuerySelector(":scope > .left > button"));
        Assert.Equal("/first", component.Find(".yrolvcoq [data-page]").GetAttribute("data-path"));

        header.ContextMenu();
        MisskeyContextMenuEntry menu = Assert.Single(overlays.ContextMenus);
        Assert.Equal("/first", menu.Items[0].Text);
        Assert.Equal(
            ["Show in page", "Pop-out", "Open in new tab", "Copy link"],
            menu.Items.Where(item => item.Kind == MisskeyMenuItemKind.Action).Select(item => item.Text));
    }

    [Fact]
    public async Task NavigationRecordsHistoryAndBackRestoresThePreviousPath()
    {
        using IRenderedComponent<MkPageWindow> component = RenderWindow();

        component.Find("[data-navigate]").Click();
        Assert.Equal("/second", component.Instance.CurrentPath);
        Assert.Equal("Second page", component.Find(".ebkgocck .header .title > span").TextContent);
        Assert.NotNull(component.Find(".ebkgocck .header .left > button > i.fas.fa-arrow-left"));

        component.Find(".ebkgocck .header .left > button").Click();
        Assert.Equal("/first", component.Instance.CurrentPath);
        Assert.Equal("First page", component.Find(".ebkgocck .header .title > span").TextContent);
        Assert.Empty(component.FindAll(".ebkgocck .header .left > button"));

        await component.InvokeAsync(() => component.Instance.NavigateAsync("/first"));
        Assert.Equal("/first", component.Instance.CurrentPath);
    }

    private IRenderedComponent<MkPageWindow> RenderWindow() => Render<MkPageWindow>(parameters => parameters
        .Add(window => window.InitialPath, "/first")
        .Add(window => window.MetadataResolver, Metadata)
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
        }));

    private static MkModalPageWindowMetadata Metadata(string path) => new(
        new MkPageHeaderMetadata(
            path == "/first" ? "First page" : "Second page",
            Icon: path == "/first" ? "fas fa-home" : "fas fa-stream"));

    private sealed class RecordingWindowInterop : IMkWindowInterop
    {
        public List<MkWindowInteropOptions> Attachments { get; } = [];

        public RecordingHandle Handle { get; } = new();

        public ValueTask<MkWindowAttachment> AttachAsync(
            ElementReference root,
            ElementReference body,
            ElementReference title,
            DotNetObjectReference<MkWindow> receiver,
            MkWindowInteropOptions options,
            CancellationToken cancellationToken)
        {
            _ = root;
            _ = body;
            _ = title;
            _ = receiver;
            cancellationToken.ThrowIfCancellationRequested();
            Attachments.Add(options);
            return ValueTask.FromResult(new MkWindowAttachment(Handle, new MkWindowBrowserState(false, 0, 0, 500, 500, 1_000_000)));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingClipboardInterop : IClipboardInterop
    {
        public ValueTask<ClipboardWriteResult> WriteTextAsync(
            string value,
            CancellationToken cancellationToken)
        {
            _ = value;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ClipboardWriteResult(false, "test", null));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingPageWindowInterop : IModalPageWindowInterop
    {
        public ValueTask<bool> OpenNewTabAsync(Uri url, CancellationToken cancellationToken)
        {
            _ = url;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> PopoutAsync(
            Uri url,
            ElementReference window,
            CancellationToken cancellationToken)
        {
            _ = url;
            _ = window;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            _ = propertyName;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(fallback);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default)
        {
            _ = propertyName;
            _ = value;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _ = arguments;
            return key switch
            {
                "goBack" => "Back",
                "showInPage" => "Show in page",
                "popout" => "Pop-out",
                "openInNewTab" => "Open in new tab",
                "copyLink" => "Copy link",
                _ => key
            };
        }

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
