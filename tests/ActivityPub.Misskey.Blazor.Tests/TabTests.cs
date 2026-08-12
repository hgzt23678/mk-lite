using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class TabTests : BunitContext
{
    private static readonly MisskeyTabOption[] Options =
    [
        new(null, "Notes"),
        new("replies", "Notes and replies"),
        new("files", "With files")
    ];
    private static readonly string[] OptionLabels = ["Notes", "Notes and replies", "With files"];

    [Fact]
    public async Task PreservesRenderedOptionOrderSelectionAndAttributeFallthrough()
    {
        var interop = new RecordingSizeInterop();
        Services.AddSingleton<IElementSizeInterop>(interop);
        string? selected = null;
        IRenderedComponent<MkTab> component = Render<MkTab>(parameters => parameters
            .Add(tab => tab.Value, null)
            .Add(tab => tab.ValueChanged, value => selected = value)
            .Add(tab => tab.Options, Options)
            .AddUnmatched("class", "fixture")
            .AddUnmatched("style", "margin-bottom: 16px")
            .AddUnmatched("data-contract", "tab"));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));

        IElement root = component.Find("div.pxhvhrfw.fixture");
        Assert.Equal("margin-bottom: 16px", root.GetAttribute("style"));
        Assert.Equal("tab", root.GetAttribute("data-contract"));
        IReadOnlyList<IElement> buttons = component.FindAll("button._button");
        Assert.Equal(OptionLabels, buttons.Select(button => button.TextContent));
        Assert.All(buttons, button => Assert.Null(button.GetAttribute("type")));
        Assert.True(buttons[0].HasAttribute("disabled"));
        Assert.True(buttons[0].ClassList.Contains("active"));

        buttons[1].Click();
        Assert.Equal("replies", selected);
        component.Render(parameters => parameters
            .Add(tab => tab.Value, selected)
            .Add(tab => tab.ValueChanged, value => selected = value)
            .Add(tab => tab.Options, Options));
        Assert.True(component.FindAll("button")[1].HasAttribute("disabled"));
        Assert.True(component.FindAll("button")[1].ClassList.Contains("active"));
    }

    [Fact]
    public async Task WidthDirectiveMatchesThePinnedInclusiveFiveHundredPixelBoundary()
    {
        var interop = new RecordingSizeInterop();
        Services.AddSingleton<IElementSizeInterop>(interop);
        IRenderedComponent<MkTab> component = Render<MkTab>(parameters => parameters
            .Add(tab => tab.Value, "replies")
            .Add(tab => tab.Options, Options));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));

        await component.InvokeAsync(() => interop.Receiver!.UpdateElementSize(501, 1_440));
        Assert.False(component.Find(".pxhvhrfw").ClassList.Contains("max-width_500px"));
        await component.InvokeAsync(() => interop.Receiver!.UpdateElementSize(500, 1_440));
        Assert.True(component.Find(".pxhvhrfw").ClassList.Contains("max-width_500px"));
        await component.InvokeAsync(() => interop.Receiver!.UpdateElementSize(600, 1_440));
        Assert.False(component.Find(".pxhvhrfw").ClassList.Contains("max-width_500px"));
    }

    [Fact]
    public async Task DisposalDisconnectsResizeAndIntersectionObservers()
    {
        var interop = new RecordingSizeInterop();
        Services.AddSingleton<IElementSizeInterop>(interop);
        IRenderedComponent<MkTab> component = Render<MkTab>(parameters => parameters
            .Add(tab => tab.Value, null)
            .Add(tab => tab.Options, Options));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));

        await component.Instance.DisposeAsync();
        Assert.Equal(1, interop.Handle.DisposeCalls);
        Assert.True(interop.Handle.ReferenceDisposed);
    }

    private sealed class RecordingSizeInterop : IElementSizeInterop
    {
        public RecordingHandle Handle { get; } = new();

        public MkTab? Receiver { get; private set; }

        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = element;
            Receiver = Assert.IsType<MkTab>(receiver.Value);
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int DisposeCalls { get; private set; }

        public bool ReferenceDisposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "dispose")
            {
                DisposeCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            ReferenceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
