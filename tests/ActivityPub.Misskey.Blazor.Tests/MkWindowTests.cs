using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MkWindowTests : BunitContext
{
    private readonly RecordingWindowInterop interop = new(
        new(false, 200, 300, 500, 420, 1_000_100));
    private readonly MisskeyOverlayService overlays = new();

    public MkWindowTests()
    {
        Services.AddSingleton<IMkWindowInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: true));
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
    }

    [Fact]
    public async Task EmojiPickerWindowPreservesThePinnedMiniWindowAndEmitsSelection()
    {
        ComponentFactories.AddStub<MkEmojiPicker>();
        string? chosen = null;
        int closed = 0;
        IReadOnlyList<EmojiPickerCustomEmoji> custom =
        [
            new("party", "/media/party.png", "custom", [])
        ];
        using IRenderedComponent<MkEmojiPickerWindow> component = Render<MkEmojiPickerWindow>(parameters => parameters
            .Add(window => window.ShowPinned, false)
            .Add(window => window.AsReactionPicker, true)
            .Add(window => window.CustomEmojis, custom)
            .Add(window => window.Chosen, (string value) => chosen = value)
            .Add(window => window.Closed, () => { closed++; }));

        Bunit.TestDoubles.Stub<MkEmojiPicker> picker = component.FindComponent<Bunit.TestDoubles.Stub<MkEmojiPicker>>().Instance;
        Assert.False(picker.Parameters.Get(component => component.ShowPinned));
        Assert.True(picker.Parameters.Get(component => component.AsReactionPicker));
        Assert.Same(custom, picker.Parameters.Get(component => component.CustomEmojis));
        await picker.Parameters.Get(component => component.Chosen).InvokeAsync(":party:");
        Assert.Equal(":party:", chosen);

        IRenderedComponent<MkWindow> window = component.FindComponent<MkWindow>();
        Assert.NotNull(window.Find(".ebkgocck > .body > .header.mini"));
        MkWindowInteropOptions options = Assert.Single(interop.Attachments);
        Assert.Null(options.InitialWidth);
        Assert.Null(options.InitialHeight);
        Assert.False(options.CanResize);
        Assert.True(options.Front);
        await window.Instance.NotifyClosed();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void PreservesPinnedDomButtonsMiniHeaderResizeHandlesAndContextMenu()
    {
        int leftClicks = 0;
        int rightClicks = 0;
        IReadOnlyList<MisskeyMenuItem> contextMenu =
        [
            new(MisskeyMenuItemKind.Action, "Inspect", "fas fa-search")
        ];
        using IRenderedComponent<MkWindow> component = Render<MkWindow>(parameters => parameters
            .Add(window => window.InitialWidth, 500)
            .Add(window => window.InitialHeight, 420)
            .Add(window => window.CanResize, true)
            .Add(window => window.Mini, true)
            .Add(window => window.Front, true)
            .Add(window => window.ContextMenu, contextMenu)
            .Add(window => window.ButtonsLeft,
            [
                new("fas fa-arrow-left", () =>
                {
                    leftClicks++;
                    return Task.CompletedTask;
                }, "Back", Highlighted: true)
            ])
            .Add(window => window.ButtonsRight,
            [
                new("fas fa-expand-alt", () =>
                {
                    rightClicks++;
                    return Task.CompletedTask;
                }, "Expand")
            ])
            .Add(window => window.HeaderContent, builder => builder.AddContent(0, "Window title"))
            .Add(window => window.ChildContent, builder =>
            {
                builder.OpenElement(0, "article");
                builder.AddAttribute(1, "data-contract", "window-body");
                builder.AddContent(2, "Body");
                builder.CloseElement();
            })
            .AddUnmatched("class", "fixture-window")
            .AddUnmatched("data-contract", "window"));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        IElement root = component.Find(".ebkgocck.fixture-window.window-enter-active.window-enter-from[data-contract=window]");
        Assert.NotNull(root.QuerySelector(":scope > .body._shadow._narrow_ > .header.mini"));
        Assert.Equal("Window title", root.QuerySelector(":scope > .body > .header > .title")?.TextContent.Trim());
        Assert.NotNull(root.QuerySelector(":scope > .body > .body > article[data-contract=window-body]"));
        Assert.NotNull(root.QuerySelector(".left > button.button._button.highlighted[title=Back] > i.fas.fa-arrow-left"));
        Assert.NotNull(root.QuerySelector(".right > button.button._button[title=Expand] > i.fas.fa-expand-alt"));
        Assert.NotNull(root.QuerySelector(".right > button > i.fas.fa-window-maximize"));
        Assert.NotNull(root.QuerySelector(".right > button > i.fas.fa-times"));
        Assert.Equal(8, root.QuerySelectorAll(":scope > .handle").Length);

        root.QuerySelector(".left > button")!.Click();
        root.QuerySelector(".right > button")!.Click();
        Assert.Equal((1, 1), (leftClicks, rightClicks));
        root.QuerySelector(":scope > .body > .header")!.ContextMenu();
        MisskeyContextMenuEntry contextMenuEntry = Assert.Single(overlays.ContextMenus);
        Assert.Equal(contextMenu, contextMenuEntry.Items);

        MkWindowInteropOptions options = Assert.Single(interop.Attachments);
        Assert.Equal(500, options.InitialWidth);
        Assert.Equal(420, options.InitialHeight);
        Assert.True(options.CanResize);
        Assert.True(options.Front);
        Assert.True(options.Animation);
    }

    [Fact]
    public async Task MaximizeRestoreEscapeAndClosePreserveStateMotionAndClosedContract()
    {
        int closed = 0;
        using IRenderedComponent<MkWindow> component = Render<MkWindow>(parameters => parameters
            .Add(window => window.CanResize, true)
            .Add(window => window.Closed, () => closed++));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        await component.Instance.NotifyOpened();
        Assert.DoesNotContain("window-enter-active", component.Find(".ebkgocck").ClassList);

        await component.Instance.NotifyWindowState(new(true, 0, 0, 1280, 720, 1_000_200));
        Assert.NotNull(component.Find(".ebkgocck.maximized > .body > .header .fa-window-restore"));
        component.Find(".fa-window-restore").ParentElement!.Click();
        Assert.Contains("restore", interop.Handle.Invocations);

        await component.Instance.NotifyEscape();
        Assert.Contains("window-leave-active", component.Find(".ebkgocck").ClassList);
        Assert.Contains("window-leave-to", component.Find(".ebkgocck").ClassList);
        Assert.Contains("close", interop.Handle.Invocations);

        await component.Instance.NotifyClosed();
        Assert.Empty(component.FindAll(".ebkgocck"));
        Assert.Equal(1, closed);
        Assert.True(interop.Handle.Disposed);
    }

    [Fact]
    public void NullDimensionsWithoutResizeOrClosePreserveTheCompactWindowContract()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: false));
        using IRenderedComponent<MkWindow> component = Render<MkWindow>(parameters => parameters
            .Add(window => window.InitialWidth, null)
            .Add(window => window.InitialHeight, null)
            .Add(window => window.CloseButton, false)
            .Add(window => window.ChildContent, builder => builder.AddContent(0, "picker")));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        Assert.DoesNotContain("window-enter-active", component.Find(".ebkgocck").ClassList);
        Assert.Empty(component.FindAll(".handle"));
        Assert.Empty(component.FindAll(".fa-times"));
        Assert.Empty(component.FindAll(".fa-window-maximize"));
        Assert.Null(interop.Attachments[0].InitialWidth);
        Assert.Null(interop.Attachments[0].InitialHeight);
    }

    private sealed class FixedDeviceState(bool animation) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = propertyName == "animation" ? animation : fallback!;
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingWindowInterop(MkWindowBrowserState state) : IMkWindowInterop
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
            cancellationToken.ThrowIfCancellationRequested();
            _ = root;
            _ = body;
            _ = title;
            _ = receiver;
            Attachments.Add(options);
            return ValueTask.FromResult(new MkWindowAttachment(Handle, state));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
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
}
