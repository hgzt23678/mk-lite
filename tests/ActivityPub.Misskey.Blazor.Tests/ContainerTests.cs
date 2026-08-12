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

public sealed class ContainerTests : BunitContext
{
    public ContainerTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
    }

    [Fact]
    public async Task PreservesPinnedDomSlotsPropsMeasurementAndFallthrough()
    {
        var interop = new RecordingContainerInterop();
        Services.AddSingleton<IContainerInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: true));

        IRenderedComponent<MkContainer> component = Render<MkContainer>(parameters => parameters
            .Add(container => container.Thin, true)
            .Add(container => container.Naked, true)
            .Add(container => container.Foldable, true)
            .Add(container => container.Scrollable, true)
            .Add(container => container.MaxHeight, 120)
            .Add(container => container.Header, "header")
            .Add(container => container.Function, "function")
            .Add(container => container.ChildContent, "body")
            .AddUnmatched("class", "fixture-container")
            .AddUnmatched("style", "width: 360px;")
            .AddUnmatched("data-contract", "container"));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        IElement root = component.Find(".fixture-container");
        Assert.Equal("ukygtjoj _panel naked thin scrollable fixture-container", root.ClassName);
        Assert.Equal("container", root.GetAttribute("data-contract"));
        Assert.Equal("width: 360px;", root.GetAttribute("style"));
        Assert.Equal("header", root.QuerySelector(":scope > header > .title")?.TextContent);
        Assert.StartsWith("function", root.QuerySelector(":scope > header > .sub")?.TextContent, StringComparison.Ordinal);
        Assert.Equal("body", root.QuerySelector(":scope > .content")?.TextContent);
        Assert.Equal("true", root.QuerySelector(":scope > header > .sub > button")?.GetAttribute("aria-expanded"));

        ContainerAttachment attachment = Assert.Single(interop.Attachments);
        Assert.Equal(120, attachment.MaxHeight);
        Assert.True(attachment.Expanded);

        await component.InvokeAsync(() => component.Instance.UpdateContainerMeasurements(42, true, true));
        root = component.Find(".fixture-container");
        Assert.Contains("max-width_380px", root.ClassList);
        Assert.Contains("omitted", root.QuerySelector(":scope > .content")!.ClassList);
        IElement reveal = component.Find(".fade");
        Assert.Equal("もっと見る", reveal.TextContent.Trim());

        reveal.Click();
        component.WaitForAssertion(() => Assert.DoesNotContain("omitted", component.Find(".content").ClassList));
        Assert.Equal(1, attachment.Handle.RevealCalls);

        component.Find("header button").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Contains("closed", component.Find(".fixture-container").ClassList);
            Assert.Equal("display: none;", component.Find(".content").GetAttribute("style"));
            Assert.Equal("false", component.Find("header button").GetAttribute("aria-expanded"));
        });
        Assert.Equal(new ContainerMotionCall(false, true, 1), Assert.Single(attachment.Handle.MotionCalls));

        component.Find("header button").Click();
        component.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("closed", component.Find(".fixture-container").ClassList);
            Assert.Null(component.Find(".content").GetAttribute("style"));
            Assert.Equal("true", component.Find("header button").GetAttribute("aria-expanded"));
        });
        Assert.Equal(new ContainerMotionCall(true, true, 2), attachment.Handle.MotionCalls[1]);

        await component.Instance.DisposeAsync();
        Assert.Equal(1, attachment.Handle.DisposeCalls);
        Assert.True(attachment.Handle.ReferenceDisposed);
    }

    [Fact]
    public async Task PreservesCollapsedHeaderlessStateAndDeviceAnimationSetting()
    {
        var interop = new RecordingContainerInterop();
        Services.AddSingleton<IContainerInterop>(interop);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: false));

        IRenderedComponent<MkContainer> component = Render<MkContainer>(parameters => parameters
            .Add(container => container.ShowHeader, false)
            .Add(container => container.Foldable, true)
            .Add(container => container.Expanded, false)
            .Add(container => container.ChildContent, "collapsed"));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        Assert.Empty(component.FindAll("header"));
        Assert.Equal("ukygtjoj _panel hideHeader closed", component.Find(".ukygtjoj").ClassName);
        Assert.Equal("display: none;", component.Find(".content").GetAttribute("style"));

        await component.InvokeAsync(() => component.Instance.ToggleContentAsync(show: true));
        component.WaitForAssertion(() => Assert.Null(component.Find(".content").GetAttribute("style")));
        Assert.Equal(new ContainerMotionCall(true, false, 1), Assert.Single(interop.Attachments).Handle.MotionCalls.Single());
    }

    private sealed class FixedDeviceState(bool animation) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("animation", propertyName);
            return ValueTask.FromResult(animation is T value ? value : fallback);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingContainerInterop : IContainerInterop
    {
        public List<ContainerAttachment> Attachments { get; } = [];

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference content,
            double? maxHeight,
            bool expanded,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            var handle = new RecordingContainerHandle();
            Attachments.Add(new ContainerAttachment(maxHeight, expanded, handle));
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingContainerHandle : IJSObjectReference
    {
        public List<ContainerMotionCall> MotionCalls { get; } = [];

        public int RevealCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public bool ReferenceDisposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            switch (identifier)
            {
                case "setExpanded":
                    object?[] values = args ?? [];
                    MotionCalls.Add(new ContainerMotionCall(
                        Assert.IsType<bool>(values[0]),
                        Assert.IsType<bool>(values[1]),
                        Assert.IsType<long>(values[2])));
                    return ValueTask.FromResult((TValue)(object)false);
                case "reveal":
                    RevealCalls++;
                    break;
                case "dispose":
                    DisposeCalls++;
                    break;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            ReferenceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ContainerAttachment(
        double? MaxHeight,
        bool Expanded,
        RecordingContainerHandle Handle);

    private sealed record ContainerMotionCall(bool Expanded, bool Animation, long Generation);

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged;

        public string CurrentLocale => "ja-JP";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _ = arguments;
            return key == "showMore" ? "もっと見る" : key;
        }

        public bool TrySelectLocale(string? locale)
        {
            _ = locale;
            LocaleChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }
}
