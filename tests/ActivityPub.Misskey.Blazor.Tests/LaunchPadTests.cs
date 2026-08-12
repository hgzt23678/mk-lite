using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class LaunchPadTests : BunitContext
{
    private readonly MisskeyOverlayService overlays = new();
    private readonly RecordingModalInterop modalInterop = new();

    public LaunchPadTests()
    {
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMkModalInterop>(modalInterop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
    }

    [Fact]
    public async Task PreservesPinnedGridPopupDrawerActionAndClosedContracts()
    {
        int invoked = 0;
        int closed = 0;
        IReadOnlyList<MisskeyMenuItem> items =
        [
            new(
                MisskeyMenuItemKind.Action,
                "Reload",
                "fas fa-redo-alt",
                Action: () =>
                {
                    invoked++;
                    return Task.CompletedTask;
                },
                Indicate: true),
            MisskeyMenuItem.Link("Timeline", "fas fa-home", "/")
        ];
        Guid id = overlays.ShowLaunchPad(default, items);
        using IRenderedComponent<MkLaunchPad> component = Render<MkLaunchPad>(parameters => parameters
            .Add(launchPad => launchPad.Id, id)
            .Add(launchPad => launchPad.Source, new ElementReference("source"))
            .Add(launchPad => launchPad.Items, items)
            .Add(launchPad => launchPad.Closed, () => { closed++; }));

        component.WaitForAssertion(() => Assert.Single(modalInterop.Attachments));
        Assert.NotNull(component.Find(".szkkfdyq._popup._shadow > .main"));
        Assert.NotNull(component.Find("button._button > i.icon.fas.fa-redo-alt"));
        Assert.Equal("Reload", component.Find("button > .text").TextContent);
        Assert.NotNull(component.Find("button > .indicator > i.fas.fa-circle"));
        Assert.Equal("/", component.Find("a").GetAttribute("href"));

        MkModalInteropOptions options = Assert.Single(modalInterop.Attachments);
        Assert.Equal("auto", options.PreferType);
        Assert.Equal("right", options.AnchorX);
        Assert.Equal("center", options.AnchorY);
        Assert.True(options.TransparentBackground);

        IRenderedComponent<MkModal> modal = component.FindComponent<MkModal>();
        await component.InvokeAsync(modal.Instance.NotifyOpened);
        component.Find("button").Click();
        component.WaitForAssertion(() => Assert.Contains("hide", modalInterop.Handle.Invocations));
        Assert.Equal(1, invoked);

        await modal.Instance.NotifyPlacement(new("drawer", true, 420, "center", 0, 1_000_100));
        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".szkkfdyq.asDrawer"));
            Assert.Contains("max-height: 420px", component.Find(".szkkfdyq").GetAttribute("style"), StringComparison.Ordinal);
        });
        await component.InvokeAsync(modal.Instance.NotifyClosed);
        Assert.Equal(1, closed);
        Assert.Empty(overlays.Entries);
    }

    [Fact]
    public async Task CloseRequestedBeforeModalOpenedWaitsForTheRealAttachmentThenRemovesTheEntry()
    {
        IReadOnlyList<MisskeyMenuItem> items =
        [
            new(MisskeyMenuItemKind.Action, "Reload", "fas fa-redo-alt", Action: () => Task.CompletedTask)
        ];
        Guid id = overlays.ShowLaunchPad(default, items);
        using IRenderedComponent<MkLaunchPad> component = Render<MkLaunchPad>(parameters => parameters
            .Add(launchPad => launchPad.Id, id)
            .Add(launchPad => launchPad.Items, items));
        component.WaitForAssertion(() => Assert.Single(modalInterop.Attachments));

        await component.InvokeAsync(component.Instance.CloseAsync);
        Assert.DoesNotContain("hide", modalInterop.Handle.Invocations);

        IRenderedComponent<MkModal> modal = component.FindComponent<MkModal>();
        await component.InvokeAsync(modal.Instance.NotifyOpened);
        component.WaitForAssertion(() => Assert.Contains("hide", modalInterop.Handle.Invocations));
        await component.InvokeAsync(modal.Instance.NotifyClosed);
        Assert.Empty(overlays.Entries);
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

    private sealed class RecordingModalInterop : IMkModalInterop
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
            return ValueTask.FromResult(new MkModalAttachment(
                Handle,
                new("popup", true, 320, "right center", 80, 1_000_100)));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
