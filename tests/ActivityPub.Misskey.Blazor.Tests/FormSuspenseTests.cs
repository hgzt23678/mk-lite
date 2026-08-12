using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FormSuspenseTests : BunitContext
{
    private readonly RecordingFormSuspenseInterop browser = new();

    public FormSuspenseTests()
    {
        Services.AddSingleton<IFormSuspenseInterop>(browser);
        Services.AddSingleton<IMisskeyLocalizer>(new SuspenseLocalizer());
        Services.AddSingleton<IButtonRippleInterop>(new RecordingButtonRippleInterop());
    }

    [Fact]
    public void PendingThenResolvedRendersTypedResultSlotAndAttributeFallthrough()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: false));
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using IRenderedComponent<MkFormSuspense<string>> component = RenderSuspense(
            _ => completion.Task,
            additionalAttributes: new Dictionary<string, object>
            {
                ["class"] = "fixture",
                ["data-contract"] = "suspense"
            });

        IElement pending = component.Find("div.fixture");
        Assert.Equal("suspense", pending.GetAttribute("data-contract"));
        Assert.NotNull(pending.QuerySelector(":scope > ._root_13vug_9"));
        Assert.Empty(browser.Requests);

        completion.SetResult("database-ready");

        component.WaitForAssertion(() =>
        {
            IElement resolved = component.Find("[data-result]");
            Assert.Equal("database-ready", resolved.TextContent);
            Assert.Equal("fixture", Assert.IsAssignableFrom<IElement>(resolved.ParentElement).ClassName);
            Assert.Empty(browser.Requests);
        });
    }

    [Fact]
    public void RejectedRendersPinnedErrorAndRetryStartsARealNewAttempt()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: false));
        int attempts = 0;
        Task<string> Process(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            return attempts == 1
                ? Task.FromException<string>(new InvalidOperationException("fixture rejection"))
                : Task.FromResult("recovered");
        }

        using IRenderedComponent<MkFormSuspense<string>> component = RenderSuspense(Process);

        component.WaitForAssertion(() =>
        {
            IElement error = component.Find(".wszdbhzo");
            Assert.Equal("問題が発生しました", error.QuerySelector(":scope > div")?.TextContent.Trim());
            Assert.NotNull(error.QuerySelector(".fa-exclamation-triangle"));
            IElement retry = component.Find("button.retry.inline");
            Assert.Contains("再試行", retry.TextContent, StringComparison.Ordinal);
            Assert.NotNull(retry.QuerySelector(".fa-redo-alt"));
        });

        component.Find("button.retry").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("recovered", component.Find("[data-result]").TextContent);
            Assert.Equal(2, attempts);
        });
    }

    [Fact]
    public void ProcessReferenceChangeCancelsOldAttemptAndIgnoresItsLateResult()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: false));
        var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        CancellationToken secondToken = default;
        Func<CancellationToken, Task<string>> firstProcess = token =>
        {
            firstToken = token;
            return first.Task;
        };
        Func<CancellationToken, Task<string>> secondProcess = token =>
        {
            secondToken = token;
            return second.Task;
        };

        using IRenderedComponent<MkFormSuspense<string>> component = RenderSuspense(firstProcess);
        component.Render(parameters => parameters.Add(value => value.P, secondProcess));

        Assert.True(firstToken.IsCancellationRequested);
        Assert.False(secondToken.IsCancellationRequested);
        first.SetResult("stale");
        second.SetResult("current");

        component.WaitForAssertion(() =>
        {
            Assert.Equal("current", component.Find("[data-result]").TextContent);
            Assert.DoesNotContain("stale", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AnimationUsesStrictLeaveThenEnterAndDisposesBothGenerations()
    {
        var deviceState = new FixedDeviceState(animation: true);
        Services.AddSingleton<IPizzaxDeviceState>(deviceState);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IRenderedComponent<MkFormSuspense<string>> component = RenderSuspense(_ => completion.Task);

        component.WaitForAssertion(() => Assert.Equal(1, deviceState.ReadCalls));
        completion.SetResult("animated");
        await WaitUntilAsync(() => browser.Requests.Count == 1);
        Assert.Equal("leave", browser.Requests[0].Phase);
        Assert.NotNull(component.Find("._root_13vug_9"));
        Assert.Empty(component.FindAll("[data-result]"));

        await browser.CompleteAsync(0);
        component.WaitForAssertion(() =>
            Assert.Equal("animated", component.Find("[data-result]").TextContent));
        await WaitUntilAsync(() =>
            browser.Requests.Count == 2 && browser.Requests[0].Handle.ReferenceDisposed);
        Assert.Equal("enter", browser.Requests[1].Phase);
        Assert.True(browser.Requests[1].Generation > browser.Requests[0].Generation);

        await browser.CompleteAsync(1);
        await WaitUntilAsync(() => browser.Requests[1].Handle.ReferenceDisposed);
    }

    [Fact]
    public async Task NewProcessCancelsAnInFlightTransitionGeneration()
    {
        var deviceState = new FixedDeviceState(animation: true);
        Services.AddSingleton<IPizzaxDeviceState>(deviceState);
        var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<string>> firstProcess = _ => first.Task;
        Func<CancellationToken, Task<string>> secondProcess = _ => second.Task;
        using IRenderedComponent<MkFormSuspense<string>> component = RenderSuspense(firstProcess);

        component.WaitForAssertion(() => Assert.Equal(1, deviceState.ReadCalls));
        first.SetResult("stale");
        await WaitUntilAsync(() => browser.Requests.Count == 1);
        component.Render(parameters => parameters.Add(value => value.P, secondProcess));

        await WaitUntilAsync(() => browser.Requests[0].Handle.ReferenceDisposed);
        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find("._root_13vug_9"));
            Assert.Empty(component.FindAll("[data-result]"));
        });
    }

    [Fact]
    public async Task DisposalCancelsPendingProcessAndTransitionResources()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: false));
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken processToken = default;
        IRenderedComponent<MkFormSuspense<string>> component = RenderSuspense(token =>
        {
            processToken = token;
            return completion.Task;
        });

        await DisposeComponentsAsync();

        Assert.True(processToken.IsCancellationRequested);
        completion.SetResult("late");
    }

    private IRenderedComponent<MkFormSuspense<string>> RenderSuspense(
        Func<CancellationToken, Task<string>> process,
        IReadOnlyDictionary<string, object>? additionalAttributes = null)
    {
        RenderFragment<string> content = result => builder =>
        {
            builder.OpenElement(0, "output");
            builder.AddAttribute(1, "data-result", string.Empty);
            builder.AddContent(2, result);
            builder.CloseElement();
        };
        return Render<MkFormSuspense<string>>(parameters =>
        {
            parameters.Add(component => component.P, process);
            parameters.Add(component => component.ChildContent, content);
            if (additionalAttributes is not null)
            {
                parameters.Add(component => component.AdditionalAttributes, additionalAttributes);
            }
        });
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!predicate() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.True(predicate());
    }

    private sealed class RecordingFormSuspenseInterop : IFormSuspenseInterop
    {
        public List<TransitionRequest> Requests { get; } = [];

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            DotNetObjectReference<FormSuspenseTransitionReceiver> receiver,
            long generation,
            string phase,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = new RecordingJsReference();
            Requests.Add(new(receiver.Value, generation, phase, handle));
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public Task CompleteAsync(int index)
        {
            TransitionRequest request = Requests[index];
            return request.Receiver.NotifyTransitionCompleted(request.Generation, request.Phase);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record TransitionRequest(
        FormSuspenseTransitionReceiver Receiver,
        long Generation,
        string Phase,
        RecordingJsReference Handle);

    private sealed class RecordingButtonRippleInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingJsReference());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingJsReference : IJSObjectReference
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
            if (string.Equals(identifier, "dispose", StringComparison.Ordinal))
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

    private sealed class FixedDeviceState(bool animation) : IPizzaxDeviceState
    {
        public int ReadCalls { get; private set; }

        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("animation", propertyName);
            ReadCalls++;
            return ValueTask.FromResult((T)(object)animation);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SuspenseLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "somethingHappened" => "問題が発生しました",
            "retry" => "再試行",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
