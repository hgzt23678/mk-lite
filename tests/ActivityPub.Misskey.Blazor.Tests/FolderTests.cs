using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FolderTests : BunitContext
{
    private readonly RecordingFolderInterop browser = new();

    public FolderTests()
    {
        Services.AddSingleton<IFolderInterop>(browser);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
    }

    [Fact]
    public void PreservesPinnedDomAndRestoresTheRawBrowserState()
    {
        using IRenderedComponent<MkFolder> component = Render<MkFolder>(parameters => parameters
            .Add(folder => folder.Expanded, false)
            .Add(folder => folder.PersistKey, "timeline")
            .Add(folder => folder.Header, builder => builder.AddContent(0, "Header"))
            .Add(folder => folder.ChildContent, builder => builder.AddMarkupContent(0, "<p data-body>Body</p>"))
            .AddUnmatched("class", "fixture-folder")
            .AddUnmatched("data-contract", "folder"));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".ssazuxis.max-width_500px.fixture-folder[data-contract=folder]"));
            Assert.Contains(
                "background: rgba(1, 2, 3, 0.85)",
                component.Find(".ssazuxis > header").GetAttribute("style"),
                StringComparison.Ordinal);
            Assert.Equal("Header", component.Find("header > .title").TextContent);
            Assert.NotNull(component.Find("header > .divider"));
            Assert.NotNull(component.Find("header > button > i.fa-angle-up"));
            Assert.Equal("Body", component.Find("[data-body]").TextContent);
            Assert.False(component.Find(".ssazuxis > div:last-child").HasAttribute("style"));
        });

        Assert.Equal("timeline", browser.PersistKey);
        Assert.False(browser.DefaultExpanded);
    }

    [Fact]
    public async Task ToggleUsesAnimationGenerationAndDisposesTheAttachment()
    {
        IRenderedComponent<MkFolder> component = Render<MkFolder>(parameters => parameters
            .Add(folder => folder.Header, builder => builder.AddContent(0, "Header"))
            .Add(folder => folder.ChildContent, builder => builder.AddContent(0, "Body")));

        component.WaitForAssertion(() => Assert.NotNull(component.Find("i.fa-angle-up")));
        component.Find("header").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("i.fa-angle-down")));
        Assert.Single(browser.Handle.Motions);
        Assert.Equal((false, true, 1L), browser.Handle.Motions[0]);

        await component.Instance.DisposeAsync();
        Assert.Equal(1, browser.Handle.DisposeCalls);
        Assert.True(browser.Handle.ReferenceDisposed);
    }

    [Fact]
    public void RejectsUnsafePersistenceKeys()
    {
        Assert.Throws<ArgumentException>(() => Render<MkFolder>(parameters => parameters
            .Add(folder => folder.PersistKey, "bad\nkey")));
    }

    private sealed class RecordingFolderInterop : IFolderInterop
    {
        public RecordingHandle Handle { get; } = new();
        public string? PersistKey { get; private set; }
        public bool DefaultExpanded { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference content,
            string? persistKey,
            bool expanded,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = root;
            _ = content;
            ArgumentNullException.ThrowIfNull(receiver);
            PersistKey = persistKey;
            DefaultExpanded = expanded;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public List<(bool Expanded, bool Animation, long Generation)> Motions { get; } = [];
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
            if (identifier == "getState")
            {
                return ValueTask.FromResult((TValue)(object)new FolderBrowserState(
                    true,
                    "rgba(1, 2, 3, 0.85)",
                    true));
            }

            if (identifier == "setExpanded")
            {
                Motions.Add(((bool)args![0]!, (bool)args[1]!, (long)args[2]!));
                return ValueTask.FromResult((TValue)(object)false);
            }

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
}
