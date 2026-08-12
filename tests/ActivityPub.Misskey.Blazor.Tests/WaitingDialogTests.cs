using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class WaitingDialogTests : BunitContext
{
    [Fact]
    public void WaitingWithTextPreservesPinnedDomAndDoesNotCloseOnBackgroundClick()
    {
        var interop = new RecordingDialogInterop();
        Services.AddSingleton<IDialogWindowInterop>(interop);
        int done = 0;
        IRenderedComponent<MkWaitingDialog> component = Render<MkWaitingDialog>(parameters => parameters
            .Add(dialog => dialog.Text, "同期しています")
            .Add(dialog => dialog.AccessibleLabel, "同期状態")
            .Add(dialog => dialog.Done, () => done++));

        Assert.NotNull(component.Find(".qzhlnise.dialog > .bg._modalBg"));
        Assert.NotNull(component.Find(".qzhlnise.dialog > .content > .iuyakobc:not(.iconOnly)"));
        Assert.NotNull(component.Find(".iuyakobc > i.fas.fa-spinner.fa-pulse.icon.waiting"));
        Assert.Equal("同期しています...", component.Find(".iuyakobc > .text").TextContent);
        Assert.Equal("status", component.Find(".qzhlnise").GetAttribute("role"));
        Assert.Equal("polite", component.Find(".qzhlnise").GetAttribute("aria-live"));
        Assert.Equal("同期状態", component.Find(".qzhlnise").GetAttribute("aria-label"));
        component.Find(".bg").Click();

        Assert.Equal(0, done);
        Assert.DoesNotContain("close", interop.Reference.Invocations);
        component.WaitForAssertion(() => Assert.True(interop.HighPriority));
    }

    [Fact]
    public void SuccessUsesIconOnlyBranchAndBackgroundClickEmitsDoneBeforeClosing()
    {
        var interop = new RecordingDialogInterop();
        Services.AddSingleton<IDialogWindowInterop>(interop);
        int done = 0;
        IRenderedComponent<MkWaitingDialog> component = Render<MkWaitingDialog>(parameters => parameters
            .Add(dialog => dialog.Success, true)
            .Add(dialog => dialog.Text, "hidden while successful")
            .Add(dialog => dialog.Done, () => done++));
        component.WaitForAssertion(() => Assert.True(interop.HighPriority));

        Assert.NotNull(component.Find(".iuyakobc.iconOnly > i.fas.fa-check.icon.success"));
        Assert.Empty(component.FindAll(".iuyakobc > .text"));
        component.Find(".bg").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, done);
            Assert.Contains("close", interop.Reference.Invocations);
        });
    }

    [Fact]
    public void ShowingTransitionToFalseEmitsDoneOnceAndCloses()
    {
        var interop = new RecordingDialogInterop();
        Services.AddSingleton<IDialogWindowInterop>(interop);
        int done = 0;
        IRenderedComponent<MkWaitingDialog> component = Render<MkWaitingDialog>(parameters => parameters
            .Add(dialog => dialog.Showing, true)
            .Add(dialog => dialog.Done, () => done++));
        component.WaitForAssertion(() => Assert.True(interop.HighPriority));

        component.Render(parameters => parameters
            .Add(dialog => dialog.Showing, false)
            .Add(dialog => dialog.Done, () => done++));

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, done);
            Assert.Single(interop.Reference.Invocations, invocation => invocation == "close");
        });
    }

    private sealed class RecordingDialogInterop : IDialogWindowInterop
    {
        public RecordingReference Reference { get; } = new();

        public bool HighPriority { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference focusRoot,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(Reference);

        public ValueTask<IJSObjectReference> AttachHighPriorityAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference focusRoot,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            HighPriority = true;
            return ValueTask.FromResult<IJSObjectReference>(Reference);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingReference : IJSObjectReference
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
