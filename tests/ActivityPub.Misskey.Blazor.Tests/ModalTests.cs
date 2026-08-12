using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class ModalTests : BunitContext
{
    [Fact]
    public void DefaultDialogPreservesThePinnedDomMotionAndSlotContract()
    {
        var interop = new RecordingModalInterop(new("dialog", false, null, "center", 0, 1_000_100));
        Configure(interop, animation: true, disableDrawer: false);

        IRenderedComponent<MkModal> component = Render<MkModal>(parameters => parameters
            .Add(modal => modal.ChildContent, context => builder =>
            {
                builder.OpenElement(0, "article");
                builder.AddAttribute(1, "data-type", context.Type);
                builder.AddAttribute(2, "data-height", context.MaximumHeight);
                builder.AddContent(3, "content");
                builder.CloseElement();
            })
            .AddUnmatched("class", "fixture-modal")
            .AddUnmatched("style", "color: red;")
            .AddUnmatched("data-contract", "modal"));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        IElement root = component.Find(".qzhlnise.dialog.fixture-modal[data-contract=modal]");
        Assert.Contains("modal-enter-active", root.ClassList);
        Assert.Contains("modal-enter-from", root.ClassList);
        Assert.Contains("pointer-events: auto;", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("--transformOrigin: center;", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("color: red;", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.NotNull(component.Find(".qzhlnise > .bg._modalBg:not(.transparent)"));
        Assert.Equal("dialog", component.Find(".qzhlnise > .content > article").GetAttribute("data-type"));

        MkModalInteropOptions options = Assert.Single(interop.Attachments);
        Assert.Equal("auto", options.PreferType);
        Assert.Equal("center", options.AnchorX);
        Assert.Equal("bottom", options.AnchorY);
        Assert.Equal("low", options.Priority);
        Assert.True(options.NoOverlap);
        Assert.True(options.Animation);
        Assert.False(options.DisableDrawer);
        Assert.True(options.Showing);
    }

    [Fact]
    public async Task PopupProjectsPlacementEventsAndTheExposedCloseTransition()
    {
        var interop = new RecordingModalInterop(new("popup", true, 180, "right bottom", 48, 3_000_100));
        Configure(interop, animation: true, disableDrawer: true);
        int opening = 0;
        int opened = 0;
        int clicks = 0;
        int escapes = 0;
        int closeRequests = 0;
        int closed = 0;
        IRenderedComponent<MkModal> component = Render<MkModal>(parameters => parameters
            .Add(modal => modal.Source, new ElementReference("source"))
            .Add(modal => modal.PreferType, MkModalType.Popup)
            .Add(modal => modal.Anchor, new MkModalAnchor("right", "center"))
            .Add(modal => modal.ZPriority, MkModalZPriority.High)
            .Add(modal => modal.NoOverlap, false)
            .Add(modal => modal.TransparentBackground, true)
            .Add(modal => modal.Opening, () => opening++)
            .Add(modal => modal.Opened, () => opened++)
            .Add(modal => modal.Clicked, () => clicks++)
            .Add(modal => modal.Escape, () => escapes++)
            .Add(modal => modal.CloseRequested, () => closeRequests++)
            .Add(modal => modal.Closed, () => closed++)
            .Add(modal => modal.ChildContent, context => builder =>
            {
                builder.OpenElement(0, "output");
                builder.AddAttribute(1, "data-type", context.Type);
                builder.AddAttribute(2, "data-height", context.MaximumHeight);
                builder.CloseElement();
            }));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        Assert.NotNull(component.Find(".qzhlnise.popup > .bg._modalBg.transparent"));
        Assert.NotNull(component.Find(".qzhlnise.popup > .content.fixed"));
        Assert.Equal("popup", component.Find("output").GetAttribute("data-type"));
        Assert.Equal("180", component.Find("output").GetAttribute("data-height"));
        MkModalInteropOptions options = interop.Attachments[0];
        Assert.Equal("popup", options.PreferType);
        Assert.Equal("right", options.AnchorX);
        Assert.Equal("center", options.AnchorY);
        Assert.Equal("high", options.Priority);
        Assert.False(options.NoOverlap);
        Assert.True(options.TransparentBackground);
        Assert.True(options.DisableDrawer);

        await component.Instance.NotifyOpening();
        await component.Instance.NotifyOpened();
        await component.Instance.NotifyClicked();
        await component.Instance.NotifyEscape();
        Assert.Equal((1, 1, 1, 1), (opening, opened, clicks, escapes));

        await component.Instance.CloseAsync();
        component.WaitForAssertion(() => Assert.Contains("hide", interop.Handle.Invocations));
        Assert.Equal(1, closeRequests);
        Assert.Contains("modal-popup-leave-active", component.Find(".qzhlnise").ClassList);
        Assert.Contains("pointer-events: none;", component.Find(".qzhlnise").GetAttribute("style"), StringComparison.Ordinal);

        await component.Instance.NotifyClosed();
        Assert.Equal(1, closed);
        Assert.Contains("display: none;", component.Find(".qzhlnise").GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualDrawerVisibilityUsesShowAndHideAndSupportsRepeatedExposedCloseRequests()
    {
        var interop = new RecordingModalInterop(new("drawer", true, 400, "center", 0, 1_000_100));
        Configure(interop, animation: false, disableDrawer: false);
        int closeRequests = 0;
        IRenderedComponent<MkModal> component = Render<MkModal>(parameters => parameters
            .Add(modal => modal.ManualShowing, false)
            .Add(modal => modal.PreferType, MkModalType.Drawer)
            .Add(modal => modal.TransparentBackground, true)
            .Add(modal => modal.CloseRequested, () => closeRequests++)
            .Add(modal => modal.ChildContent, context => builder => builder.AddContent(0, context.Type)));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        Assert.False(interop.Attachments[0].Showing);
        Assert.NotNull(component.Find(".qzhlnise.drawer > .bg._modalBg:not(.transparent)"));
        Assert.DoesNotContain("modal-drawer-enter-active", component.Find(".qzhlnise").ClassList);
        Assert.Contains("display: none;", component.Find(".qzhlnise").GetAttribute("style"), StringComparison.Ordinal);

        component.Render(parameters => parameters
            .Add(modal => modal.ManualShowing, true)
            .Add(modal => modal.PreferType, MkModalType.Drawer)
            .Add(modal => modal.TransparentBackground, true)
            .Add(modal => modal.CloseRequested, () => closeRequests++)
            .Add(modal => modal.ChildContent, context => builder => builder.AddContent(0, context.Type)));
        component.WaitForAssertion(() => Assert.Contains("show", interop.Handle.Invocations));

        await component.Instance.CloseAsync();
        await component.Instance.CloseAsync();
        Assert.Equal(2, closeRequests);
        Assert.Equal(2, interop.Handle.Invocations.Count(call => call == "releaseSource"));

        component.Render(parameters => parameters
            .Add(modal => modal.ManualShowing, false)
            .Add(modal => modal.PreferType, MkModalType.Drawer)
            .Add(modal => modal.TransparentBackground, true)
            .Add(modal => modal.CloseRequested, () => closeRequests++)
            .Add(modal => modal.ChildContent, context => builder => builder.AddContent(0, context.Type)));
        component.WaitForAssertion(() => Assert.Contains("hide", interop.Handle.Invocations));
        Assert.Equal(2, closeRequests);
    }

    private void Configure(RecordingModalInterop interop, bool animation, bool disableDrawer)
    {
        Services.AddSingleton<IMkModalInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation, disableDrawer));
    }

    private sealed class FixedDeviceState(bool animation, bool disableDrawer) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = propertyName switch
            {
                "animation" => animation,
                "disableDrawer" => disableDrawer,
                _ => fallback!
            };
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingModalInterop(MkModalBrowserPlacement placement) : IMkModalInterop
    {
        public List<MkModalInteropOptions> Attachments { get; } = [];

        public RecordingHandle Handle { get; } = new();

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
            _ = source;
            _ = modal;
            _ = background;
            _ = content;
            _ = receiver;
            Attachments.Add(options);
            return ValueTask.FromResult(new MkModalAttachment(Handle, placement));
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
