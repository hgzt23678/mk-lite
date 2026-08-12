using ActivityPub.Misskey.Blazor.BrowserInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class VisitorShellInteropTests
{
    [Fact]
    public async Task DisposeIgnoresModuleImportCancellationDuringCircuitShutdown()
    {
        var interop = new VisitorShellInterop(new CancelledImportRuntime());
        using var receiver = DotNetObjectReference.Create(new object());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await interop.AttachAsync(default(ElementReference), receiver, CancellationToken.None));

        await interop.DisposeAsync();
    }

    private sealed class CancelledImportRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromCanceled<TValue>(new CancellationToken(canceled: true));

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            ValueTask.FromCanceled<TValue>(new CancellationToken(canceled: true));
    }
}
