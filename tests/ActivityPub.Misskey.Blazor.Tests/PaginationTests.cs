using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class PaginationTests : BunitContext
{
    private readonly RecordingPaginationInterop browser = new();

    public PaginationTests()
    {
        Services.AddSingleton<IPaginationInterop>(browser);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IMisskeyLocalizer>(new PaginationLocalizer());
        Services.AddSingleton<IButtonRippleInterop>(new RecordingButtonRippleInterop());
        Services.AddSingleton<IErrorAppearInterop>(new RecordingErrorAppearInterop());
    }

    [Fact]
    public void InitialAndMorePreserveLimitCursorAdvertisementAndAppendContracts()
    {
        var source = new RecordingSource(new(Limit: 4));
        source.Responses.Enqueue(
        [
            new("10"), new("9"), new("8"), new("7"), new("6")
        ]);
        source.Responses.Enqueue(Enumerable.Range(0, 31)
            .Select(index => new PageItem($"older-{index:D2}"))
            .ToArray());

        using IRenderedComponent<MkPagination<PageItem>> component = RenderPagination(source);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["10", "9", "8", "7"], component.Instance.Items.Select(item => item.Id));
            Assert.True(component.Instance.Items[^1].ShouldInsertAdvertisement);
            Assert.True(component.Instance.More);
            Assert.Equal(5, source.Requests[0].Limit);
            Assert.True(browser.Attached);
            Assert.True(browser.AutoLoadEnabled);
            Assert.Single(component.FindAll("[data-pagination-auto-load]"));
        });

        component.Find(".cxiknjgy > .button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(34, component.Instance.Items.Count);
            Assert.Equal(31, source.Requests[1].Limit);
            Assert.Equal("7", source.Requests[1].UntilId);
            Assert.Null(source.Requests[1].SinceId);
            Assert.True(component.Instance.Items[14].ShouldInsertAdvertisement);
            Assert.True(component.Instance.Backed);
            Assert.True(component.Instance.More);
            Assert.Equal(34, component.FindAll("[data-page-item]").Count);
        });
    }

    [Fact]
    public void ReversedMoreAheadPreservesOrderSinceUntilAndScrollAnchor()
    {
        browser.Snapshot = new(12, 300, UsesWindow: false, AtBottom: false);
        var source = new RecordingSource(new(Limit: 2, Reversed: true));
        source.Responses.Enqueue([new("30"), new("29"), new("28")]);
        source.Responses.Enqueue([new("27"), new("26")]);

        using IRenderedComponent<MkPagination<PageItem>> component = RenderPagination(source);
        component.WaitForAssertion(() =>
            Assert.Equal(["29", "30"], component.Instance.Items.Select(item => item.Id)));

        component.Find(".cxiknjgy > .button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["26", "27", "29", "30"], component.Instance.Items.Select(item => item.Id));
            Assert.Equal("29", source.Requests[1].UntilId);
            Assert.Null(source.Requests[1].SinceId);
            Assert.Equal(1, browser.CaptureCalls);
            Assert.Equal(1, browser.RestoreCalls);
            Assert.False(browser.LastStickToBottom);
        });
    }

    [Fact]
    public async Task QueuesAwayFromTopThenFlushesAndExposesMutationMethods()
    {
        browser.TopVisible = false;
        var source = new RecordingSource(new(Limit: 3, NoPaging: true));
        source.Responses.Enqueue([new("3"), new("2"), new("1")]);
        var queueCounts = new List<int>();

        using IRenderedComponent<MkPagination<PageItem>> component = RenderPagination(
            source,
            displayLimit: 3,
            queueChanged: count => queueCounts.Add(count));
        component.WaitForAssertion(() => Assert.Equal(3, component.Instance.Items.Count));

        await component.Instance.PrependAsync(new("4"));
        Assert.Equal(["4"], component.Instance.QueuedItems.Select(item => item.Id));
        Assert.Equal(["3", "2", "1"], component.Instance.Items.Select(item => item.Id));

        await component.Instance.UpsertAsync(new("4"));
        Assert.Single(component.Instance.QueuedItems);

        await component.Instance.ShowQueuedAsync();
        Assert.Empty(component.Instance.QueuedItems);
        Assert.Equal(["4", "3"], component.Instance.Items.Select(item => item.Id));
        Assert.Equal([1, 0], queueCounts);
        Assert.True(component.Instance.More);
        Assert.Equal(1, browser.ScrollToTopCalls);

        browser.TopVisible = false;
        await component.Instance.NotifyViewportStateAsync(false);
        await component.Instance.PrependAsync(new("queued-remove"));
        await component.Instance.RemoveItemAsync(item => item.Id == "queued-remove");
        Assert.Empty(component.Instance.QueuedItems);
        Assert.Equal([1, 0, 1, 0], queueCounts);

        await component.Instance.AppendAsync(new("5"));
        await component.Instance.UpdateItemAsync("3", old => old with { Id = "3-updated" });
        await component.Instance.RemoveItemAsync(item => item.Id == "4");
        Assert.Equal(["3-updated", "5"], component.Instance.Items.Select(item => item.Id));
        Assert.Equal(1, browser.TopVisibleCalls);
    }

    [Fact]
    public async Task ReversedPrependAtBottomTrimsAndRestoresBottom()
    {
        browser.BottomVisible = true;
        browser.Snapshot = new(180, 300, UsesWindow: false, AtBottom: true);
        var source = new RecordingSource(new(Limit: 3, NoPaging: true, Reversed: true));
        source.Responses.Enqueue([new("3"), new("2"), new("1")]);

        using IRenderedComponent<MkPagination<PageItem>> component = RenderPagination(
            source,
            displayLimit: 3);
        component.WaitForAssertion(() =>
            Assert.Equal(["1", "2", "3"], component.Instance.Items.Select(item => item.Id)));

        await component.Instance.PrependAsync(new("4"));

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["2", "3", "4"], component.Instance.Items.Select(item => item.Id));
            Assert.True(component.Instance.More);
            Assert.Equal(1, browser.RestoreCalls);
            Assert.True(browser.LastStickToBottom);
        });
    }

    [Fact]
    public void ErrorRetryAndEmptyPreservePinnedBranchesAndLocalizedFallback()
    {
        var source = new RecordingSource(new(Limit: 10));
        source.Failures.Enqueue(new InvalidOperationException("fixture failure"));
        source.Responses.Enqueue([]);

        using IRenderedComponent<MkPagination<PageItem>> component = RenderPagination(source);
        component.WaitForAssertion(() =>
            Assert.Equal("問題が発生しました", component.Find(".mjndxjcg > p").TextContent.Trim()));

        component.Find(".mjndxjcg > .button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".empty > ._fullinfo > img._ghost"));
            Assert.Equal("ありません", component.Find(".empty > ._fullinfo > div").TextContent);
            Assert.Equal(2, source.Requests.Count);
        });
    }

    [Fact]
    public async Task OffsetModeUsesLoadedCountAndDisposesObserver()
    {
        var source = new RecordingSource(new(Limit: 2, OffsetMode: true));
        source.Responses.Enqueue([new("3"), new("2"), new("1")]);
        source.Responses.Enqueue([new("0")]);

        IRenderedComponent<MkPagination<PageItem>> component = RenderPagination(source);
        component.WaitForAssertion(() => Assert.True(browser.Attached));
        component.Find(".cxiknjgy > .button").Click();
        component.WaitForAssertion(() => Assert.Equal(2, source.Requests[1].Offset));

        await DisposeComponentsAsync();
        Assert.True(browser.Reference.Disposed);
    }

    private IRenderedComponent<MkPagination<PageItem>> RenderPagination(
        RecordingSource source,
        int displayLimit = 30,
        Action<int>? queueChanged = null)
    {
        RenderFragment<IReadOnlyList<PageItem>> content = values => builder =>
        {
            foreach (PageItem item in values)
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "data-page-item", item.Id);
                builder.AddContent(2, item.Id);
                builder.CloseElement();
            }
        };
        return Render<MkPagination<PageItem>>(parameters =>
        {
            parameters.Add(component => component.Source, source);
            parameters.Add(component => component.DisplayLimit, displayLimit);
            parameters.Add(component => component.ChildContent, content);
            if (queueChanged is not null)
            {
                parameters.Add(component => component.QueueChanged, queueChanged);
            }
        });
    }

    private sealed record PageItem(string Id, bool ShouldInsertAdvertisement = false);

    private sealed class RecordingSource(MisskeyPaginationOptions options)
        : IMisskeyPaginationSource<PageItem>
    {
        public Queue<IReadOnlyList<PageItem>> Responses { get; } = [];
        public Queue<Exception> Failures { get; } = [];
        public List<MisskeyPaginationRequest> Requests { get; } = [];
        public MisskeyPaginationOptions Options { get; } = options;

        public ValueTask<IReadOnlyList<PageItem>> FetchAsync(
            MisskeyPaginationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Failures.TryDequeue(out Exception? failure))
            {
                return ValueTask.FromException<IReadOnlyList<PageItem>>(failure);
            }
            return ValueTask.FromResult(Responses.Dequeue());
        }

        public string GetId(PageItem item) => item.Id;

        public PageItem MarkAdvertisement(PageItem item) =>
            item with { ShouldInsertAdvertisement = true };
    }

    private sealed class RecordingPaginationInterop : IPaginationInterop
    {
        public RecordingReference Reference { get; } = new();
        public bool Attached { get; private set; }
        public bool AutoLoadEnabled { get; private set; }
        public bool TopVisible { get; set; } = true;
        public bool BottomVisible { get; set; }
        public int TopVisibleCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int ScrollToTopCalls { get; private set; }
        public bool LastStickToBottom { get; private set; }
        public PaginationScrollSnapshot Snapshot { get; set; } =
            new(0, 100, UsesWindow: false, AtBottom: false);

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            DotNetObjectReference<T> receiver,
            bool enableAutoLoad,
            CancellationToken cancellationToken)
            where T : class
        {
            Attached = true;
            AutoLoadEnabled = enableAutoLoad;
            return ValueTask.FromResult<IJSObjectReference>(Reference);
        }

        public ValueTask<bool> IsTopVisibleAsync(
            ElementReference root,
            CancellationToken cancellationToken)
        {
            TopVisibleCalls++;
            return ValueTask.FromResult(TopVisible);
        }

        public ValueTask<bool> IsBottomVisibleAsync(
            ElementReference root,
            double tolerance,
            CancellationToken cancellationToken) => ValueTask.FromResult(BottomVisible);

        public ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(
            ElementReference root,
            CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask RestoreScrollAsync(
            ElementReference root,
            PaginationScrollSnapshot snapshot,
            bool stickToBottom,
            CancellationToken cancellationToken)
        {
            RestoreCalls++;
            LastStickToBottom = stickToBottom;
            return ValueTask.CompletedTask;
        }

        public ValueTask ScrollToTopAsync(
            ElementReference root,
            CancellationToken cancellationToken)
        {
            ScrollToTopCalls++;
            TopVisible = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult((T)(object)true);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingButtonRippleInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingReference());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingErrorAppearInterop : IErrorAppearInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool animate,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingReference());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingReference : IJSObjectReference
    {
        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PaginationLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "nothing" => "ありません",
            "loadMore" => "もっと見る",
            "somethingHappened" => "問題が発生しました",
            "retry" => "再試行",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
