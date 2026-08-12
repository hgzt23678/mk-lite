using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class ToastTests : BunitContext
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreservesPinnedDomAttributeFallthroughAndAnimationSetting(bool animation)
    {
        var interop = new RecordingToastInterop();
        Services.AddSingleton<IToastInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation));

        IRenderedComponent<MkToast> component = Render<MkToast>(parameters => parameters
            .Add(toast => toast.Message, "Welcome back, Alice")
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "toast"));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));

        IElement root = component.Find(".mk-toast.fixture");
        Assert.Equal("toast", root.GetAttribute("data-contract"));
        IElement body = Assert.IsAssignableFrom<IElement>(root.QuerySelector(":scope > .body._acrylic"));
        Assert.Equal("status", body.GetAttribute("role"));
        Assert.Equal("polite", body.GetAttribute("aria-live"));
        Assert.Equal("Welcome back, Alice", body.QuerySelector(":scope > .message")?.TextContent);
        Assert.Equal(animation, interop.Animate);
    }

    [Fact]
    public async Task ClosedIsEmittedOnceForCurrentGenerationAndDisposalCancelsTheHandle()
    {
        var interop = new RecordingToastInterop();
        Services.AddSingleton<IToastInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(true));
        int closed = 0;
        IRenderedComponent<MkToast> component = Render<MkToast>(parameters => parameters
            .Add(toast => toast.Message, "Done")
            .Add(toast => toast.Closed, () => closed++));
        component.WaitForAssertion(() => Assert.NotNull(interop.Receiver));

        await component.InvokeAsync(() => interop.Receiver!.NotifyClosed(interop.Generation + 1));
        await component.InvokeAsync(() => interop.Receiver!.NotifyClosed(interop.Generation));
        await component.InvokeAsync(() => interop.Receiver!.NotifyClosed(interop.Generation));
        await component.Instance.DisposeAsync();

        Assert.Equal(1, closed);
        Assert.Equal(1, interop.Handle.DisposeCalls);
        Assert.True(interop.Handle.ReferenceDisposed);
    }

    [Fact]
    public void TransientServiceKeepsToastAndSuccessSurfacesIndependent()
    {
        var service = new MisskeyTransientFeedbackService();
        Guid success = service.ShowSuccess("Copied");
        Guid toast = service.ShowToast("Welcome back");

        Assert.Equal(success, Assert.Single(service.Entries).Id);
        Assert.Equal(toast, Assert.Single(service.Toasts).Id);
        service.Close(toast);
        Assert.Single(service.Entries);
        Assert.Empty(service.Toasts);
        service.Close(success);
        Assert.Empty(service.Entries);
    }

    private sealed class FixedDeviceState(bool animation) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("animation", propertyName);
            return ValueTask.FromResult((T)(object)animation);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingToastInterop : IToastInterop
    {
        public RecordingToastHandle Handle { get; } = new();

        public MkToast? Receiver { get; private set; }

        public long Generation { get; private set; }

        public bool Animate { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference body,
            DotNetObjectReference<MkToast> receiver,
            long generation,
            bool animate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = body;
            Receiver = receiver.Value;
            Generation = generation;
            Animate = animate;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingToastHandle : IJSObjectReference
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
