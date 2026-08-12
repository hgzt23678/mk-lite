using ActivityPub.Misskey.Blazor.BrowserInterop;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class BrowserModuleImporterTests
{
    [Fact]
    public async Task PageDisposalMarkerIsTranslatedToBrowserDisconnection()
    {
        var runtime = new ImportRuntime(
            new JSException(BrowserModuleImporter.PageDisposalMarker + ": discarded document"));

        JSDisconnectedException exception = await Assert.ThrowsAsync<JSDisconnectedException>(async () =>
            await BrowserModuleImporter.ImportAsync(runtime, "./fixture.js"));

        Assert.Equal(
            "The browser document was disposed while loading a Misskey interop module.",
            exception.Message);
        Assert.Equal("activityPubMisskeyInterop.importModule", runtime.Identifier);
        Assert.Equal("./fixture.js", Assert.Single(runtime.Arguments!));
    }

    [Fact]
    public async Task OrdinaryModuleFailureIsNotClassifiedAsDisconnection()
    {
        var original = new JSException("Importing a module script failed because the resource is missing.");
        var runtime = new ImportRuntime(original);

        JSException exception = await Assert.ThrowsAsync<JSException>(async () =>
            await BrowserModuleImporter.ImportAsync(runtime, "./missing.js"));

        Assert.Same(original, exception);
        Assert.False(BrowserModuleImporter.IsPageDisposalImportFailure(exception));
    }

    [Fact]
    public async Task CancellationIsNotReclassifiedAsPageDisposal()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runtime = new ImportRuntime(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await BrowserModuleImporter.ImportAsync(runtime, "./fixture.js", cancellation.Token));
    }

    private sealed class ImportRuntime : IJSRuntime
    {
        private readonly Exception? exception;
        private readonly CancellationToken cancellationToken;

        public ImportRuntime(Exception exception)
        {
            this.exception = exception;
        }

        public ImportRuntime(CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
        }

        public string? Identifier { get; private set; }

        public object?[]? Arguments { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken invocationCancellationToken,
            object?[]? args)
        {
            Identifier = identifier;
            Arguments = args;
            if (exception is not null)
            {
                return ValueTask.FromException<TValue>(exception);
            }

            CancellationToken expected = cancellationToken.IsCancellationRequested
                ? cancellationToken
                : invocationCancellationToken;
            return ValueTask.FromCanceled<TValue>(expected);
        }
    }
}
