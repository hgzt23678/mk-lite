using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class PageHeaderTests : BunitContext
{
    private readonly RecordingPageHeaderInterop interop = new();
    private readonly MisskeyOverlayService overlays = new();

    public PageHeaderTests()
    {
        Services.AddSingleton<IPageHeaderInterop>(interop);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<ICurrentAccountPresentationService>(new FixedCurrentAccountService());
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParserInterop());
    }

    [Fact]
    public async Task PreservesMetadataTabsActionsFallthroughAndExactNarrowPopupContract()
    {
        var selected = new List<string>();
        int actionCalls = 0;
        NoteAuthorViewModel avatar = User("metadata-user", "Metadata user");
        MkPageHeaderTab[] tabs =
        [
            new("overview", "概要", "fas fa-home"),
            new("activity", "アクティビティ", "fas fa-stream", IconOnly: true),
            new(null, "履歴", "fas fa-history", IconOnly: true, Action: "history")
        ];
        MkPageHeaderAction[] actions =
        [
            new("更新", "fas fa-sync", () =>
            {
                actionCalls++;
                return Task.CompletedTask;
            }, Highlighted: true)
        ];

        IRenderedComponent<MkPageHeader> component = Render<MkPageHeader>(parameters => parameters
            .Add(header => header.Metadata, new MkPageHeaderMetadata(
                "ページタイトル",
                "ページ副題",
                Avatar: avatar,
                Background: "#224466",
                AvatarOnlineStatus: "online"))
            .Add(header => header.Tabs, tabs)
            .Add(header => header.Tab, "overview")
            .Add(header => header.TabChanged, value => selected.Add(value))
            .Add(header => header.Actions, actions)
            .Add(header => header.DisplayMyAvatar, true)
            .Add(header => header.AuxiliaryRequested, value => selected.Add($"aux:{value}"))
            .AddUnmatched("class", "contract-header")
            .AddUnmatched("style", "--contract: 1;")
            .AddUnmatched("data-contract", "header"));

        component.WaitForAssertion(() => Assert.Equal(1, interop.AttachCalls));
        IElement root = component.Find(".fdidabkb.contract-header");
        Assert.Equal("header", root.GetAttribute("data-contract"));
        Assert.Contains("--contract: 1;", root.GetAttribute("style") ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("#224466", root.GetAttribute("data-header-background"));
        Assert.NotNull(root.QuerySelector(":scope > .titleContainer > .avatar.eiwwqkts"));
        Assert.NotNull(root.QuerySelector(":scope > .titleContainer > .avatar > .indicator.fzgwjkgc.online"));
        Assert.Null(root.QuerySelector(":scope > .titleContainer > .avatar")?.GetAttribute("onlinestatus"));
        Assert.Equal("ページタイトル", root.QuerySelector(":scope > .titleContainer > .title > .title")?.TextContent);
        Assert.Equal("ページ副題", root.QuerySelector(":scope > .titleContainer > .title > .subtitle")?.TextContent);
        Assert.Equal(3, root.QuerySelectorAll(":scope > .tabs > button.tab").Length);
        Assert.Equal("true", root.QuerySelector("button.tab.active")?.GetAttribute("aria-selected"));
        Assert.NotNull(root.QuerySelector(":scope > .buttons.right > button.button.highlighted[aria-label='更新']"));

        component.Find(".buttons.right > button").Click();
        Assert.Equal(1, actionCalls);

        IElement activity = component.Find(".tabs > button:nth-of-type(2)");
        activity.MouseDown();
        activity.Click();
        Assert.Equal(2, selected.Count);
        Assert.Equal("activity", selected[0]);
        Assert.Equal("activity", selected[1]);

        await component.InvokeAsync(() => component.Instance.UpdatePageHeaderNarrow(true));
        root = component.Find(".fdidabkb.slim");
        Assert.NotNull(root.QuerySelector(":scope > .buttons.left > .avatar"));
        Assert.Null(root.QuerySelector(":scope > .tabs"));
        Assert.Equal("概要", root.QuerySelector(".subtitle.activeTab")?.TextContent.Trim());
        IElement trigger = component.Find(".titleContainer[data-tabs-popup-trigger=true]");
        Assert.Equal("button", trigger.GetAttribute("role"));
        Assert.Equal("0", trigger.GetAttribute("tabindex"));

        trigger.Click(new MouseEventArgs { Detail = 1 });
        MisskeyOverlayEntry menu = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PopupMenu, menu.Kind);
        Assert.Equal(3, menu.MenuItems.Count);
        Assert.True(menu.MenuItems[0].Active);
        Assert.False(menu.OpenedViaKeyboard);
        await menu.MenuItems[1].Action!();
        Assert.Equal("activity", selected[^1]);

        await component.Instance.DisposeAsync();
        Assert.Equal(1, interop.Handle.DisposeCalls);
        Assert.Empty(overlays.Entries);
        component.Dispose();
    }

    [Fact]
    public async Task NamedCascadesPreserveThinAndOmittedTitleBranches()
    {
        IRenderedComponent<CascadingValue<bool>> outer = Render<CascadingValue<bool>>(parameters => parameters
            .Add(value => value.Name, "ShouldHeaderThin")
            .Add(value => value.Value, true)
            .Add(value => value.ChildContent, builder =>
            {
                builder.OpenComponent<CascadingValue<bool>>(0);
                builder.AddAttribute(1, nameof(CascadingValue<bool>.Name), "ShouldOmitHeaderTitle");
                builder.AddAttribute(2, nameof(CascadingValue<bool>.Value), true);
                builder.AddAttribute(3, nameof(CascadingValue<bool>.ChildContent), (RenderFragment)(nested =>
                {
                    nested.OpenComponent<MkPageHeader>(0);
                    nested.AddAttribute(1, nameof(MkPageHeader.Metadata), new MkPageHeaderMetadata(
                        "ユーザー",
                        UserName: User("header-user", "Header $[jelly User]")));
                    nested.AddAttribute(2, nameof(MkPageHeader.Tabs), new[]
                    {
                        new MkPageHeaderTab("one", "One")
                    });
                    nested.AddAttribute(3, nameof(MkPageHeader.Tab), "one");
                    nested.CloseComponent();
                }));
                builder.CloseComponent();
            }));

        IRenderedComponent<MkPageHeader> header = outer.FindComponent<MkPageHeader>();
        await header.InvokeAsync(() => header.Instance.UpdatePageHeaderNarrow(true));
        IElement root = outer.Find(".fdidabkb.thin.slim");
        Assert.Null(root.QuerySelector(":scope > .titleContainer"));
        Assert.NotNull(root.QuerySelector(":scope > .tabs"));
        Assert.Null(root.QuerySelector("[data-tabs-popup-trigger]"));
    }

    [Fact]
    public void UserNameMetadataUsesThePinnedPlainNowrapMfmBranch()
    {
        IRenderedComponent<MkPageHeader> component = Render<MkPageHeader>(parameters => parameters
            .Add(header => header.Metadata, new MkPageHeaderMetadata(
                "fallback",
                UserName: User("header-user", "Header $[jelly User]"))));

        component.WaitForAssertion(() =>
        {
            IElement name = component.Find(".titleContainer > .title > .title.havbbuyv.nowrap");
            Assert.Equal("Header $[jelly User]", name.TextContent);
            Assert.Empty(component.FindAll(".titleContainer > .title > div.title"));
        });
    }

    [Fact]
    public void OmittedTitleWithoutTabsOrActionsMatchesThePinnedVIfAndRendersNothing()
    {
        IRenderedComponent<CascadingValue<bool>> host = Render<CascadingValue<bool>>(parameters => parameters
            .Add(value => value.Name, "ShouldOmitHeaderTitle")
            .Add(value => value.Value, true)
            .Add(value => value.ChildContent, builder =>
            {
                builder.OpenComponent<MkPageHeader>(0);
                builder.AddAttribute(1, nameof(MkPageHeader.Metadata), new MkPageHeaderMetadata("Hidden"));
                builder.CloseComponent();
            }));

        Assert.Empty(host.FindAll(".fdidabkb"));
        Assert.Equal(0, interop.AttachCalls);
    }

    private static NoteAuthorViewModel User(string username, string displayName) => new(
        username,
        username,
        username,
        displayName,
        "/static-assets/favicon.png",
        IsBot: false);

    private sealed class FixedCurrentAccountService : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(User("alice", "Alice"));
    }

    private sealed class PlainMfmParserInterop : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MfmNode>>(
            [new MfmNode("text", JsonSerializer.SerializeToElement(new { text }), null)]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingPageHeaderInterop : IPageHeaderInterop
    {
        public int AttachCalls { get; private set; }

        public RecordingHandle Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            AttachCalls++;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int DisposeCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (string.Equals(identifier, "dispose", StringComparison.Ordinal))
            {
                DisposeCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
