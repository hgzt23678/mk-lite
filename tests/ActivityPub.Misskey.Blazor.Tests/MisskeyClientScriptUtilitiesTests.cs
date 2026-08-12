using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyClientScriptUtilitiesTests
{
    [Fact]
    public void ExtractsThePinnedBlurhashAverageColor()
    {
        Assert.Equal("#000000", MisskeyBlurhashUtilities.ExtractAverageColor("L00000"));
        Assert.Equal("#000001", MisskeyBlurhashUtilities.ExtractAverageColor("L00001"));
        Assert.Null(MisskeyBlurhashUtilities.ExtractAverageColor(null));
    }

    [Fact]
    public void ContainsTraversesParentsAndHonorsSameNodeFlag()
    {
        var root = new Node("root");
        var child = new Node("child") { Parent = root };
        var leaf = new Node("leaf") { Parent = child };

        Assert.True(MisskeyContainsUtilities.Contains(root, leaf, node => node.Parent));
        Assert.True(MisskeyContainsUtilities.Contains(root, root, node => node.Parent));
        Assert.False(MisskeyContainsUtilities.Contains(root, root, node => node.Parent, checkSame: false));
    }

    [Fact]
    public void IntervalHonorsImmediateStartAndIdempotentStop()
    {
        int calls = 0;
        using var interval = new MisskeyIntervalController(
            () => calls++,
            60_000,
            new MisskeyIntervalOptions(Immediate: true));

        interval.Start();
        interval.Start();
        Assert.Equal(1, calls);
        Assert.True(interval.IsStarted);
        interval.Stop();
        interval.Stop();
        Assert.False(interval.IsStarted);
    }

    [Fact]
    public void PopoutPreservesExistingQueryAndRejectsUserInfo()
    {
        Uri baseUri = new("https://activitypub.example/app/");
        Assert.Equal(
            "https://activitypub.example/app/notes/1?x=1&zen",
            MisskeyPopoutUtilities.BuildUrl("/app/notes/1?x=1", baseUri));
        Assert.Throws<ArgumentException>(() => MisskeyPopoutUtilities.BuildUrl("https://user:pass@example/", baseUri));
        Assert.Equal(
            "width=400, height=500, top=20, left=10",
            MisskeyPopoutUtilities.BuildFeatures(new(0, 0, 400, 500, 20, 10)));
    }

    [Fact]
    public void StickySidebarMaintainsDirectionAndSpacerState()
    {
        var down = MisskeyStickySidebarUtilities.Calculate(new(
            ScrollTop: 300,
            LastScrollTop: 100,
            ElementHeight: 800,
            MarginTop: 0,
            GlobalHeaderHeight: 59,
            WindowHeight: 600,
            ElementOffsetTop: 100,
            ContainerOffsetTop: 100,
            IsTop: true,
            IsBottom: false));
        Assert.Equal(-200, down.Top);
        Assert.Null(down.Bottom);
        Assert.False(down.IsTop);
        Assert.True(down.IsBottom);

        var up = MisskeyStickySidebarUtilities.Calculate(new(
            ScrollTop: 50,
            LastScrollTop: 100,
            ElementHeight: 800,
            MarginTop: 0,
            GlobalHeaderHeight: 59,
            WindowHeight: 600,
            ElementOffsetTop: 200,
            ContainerOffsetTop: 100,
            IsTop: false,
            IsBottom: true));
        Assert.Null(up.Top);
        Assert.Equal(-259, up.Bottom);
        Assert.False(up.IsBottom);
    }

    [Fact]
    public async Task AccountStoreReadsTheDedicatedIndexedDbRecordWithoutLoggingSecrets()
    {
        var storage = new FakeIndexedStorage
        {
            Accounts = [new("alice", "secret-token")]
        };
        var store = new MisskeyAccountStore(storage);

        MisskeyStoredAccount? account = await store.FindByIdAsync("alice");

        Assert.Equal("alice", account?.Id);
        Assert.Equal("secret-token", account?.Token);
        Assert.Empty(await new MisskeyAccountStore(new FakeIndexedStorage()).GetAccountsAsync());
    }

    [Fact]
    public void ResponsiveDirectiveProducesPinnedAddAndRemoveClassOrder()
    {
        MisskeyResponsiveClassOrder order = MisskeyResponsiveDirectiveUtilities.Calculate(
            800,
            max: [600, 900],
            min: [500, 900]);

        Assert.Equal(["max-width_900px", "min-width_500px"], order.Add);
        Assert.Equal(["max-width_600px", "min-width_900px"], order.Remove);
    }

    [Fact]
    public void PanelAndAnimationDirectivesKeepUpstreamStyleDecisions()
    {
        Assert.Equal("var(--bg)", MisskeyPanelDirectiveUtilities.ResolveBackground("rgb(1, 2, 3)", "rgb(4, 5, 6)", "rgb(1, 2, 3)"));
        Assert.Equal("rgb(4, 5, 6)", MisskeyPanelDirectiveUtilities.ResolveBorder("rgb(1, 2, 3)", "rgb(4, 5, 6)"));
        Assert.Equal(("0", "scale(0.9)", "_zoom"), MisskeyAnimationDirectiveUtilities.BeforeMount());
        Assert.Equal(("1", "none"), MisskeyAnimationDirectiveUtilities.AfterMount());
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;
        public Node? Parent { get; set; }
    }

    private sealed class FakeIndexedStorage : IMisskeyIndexedStorage
    {
        public MisskeyStoredAccount[]? Accounts { get; set; }

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            object? value = key == "accounts" ? Accounts : null;
            return ValueTask.FromResult((T?)value);
        }

        public ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
