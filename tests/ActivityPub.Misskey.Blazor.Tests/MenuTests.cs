using System.Globalization;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MenuTests : BunitContext
{
    private readonly RecordingMenuInterop menuInterop = new();
    private readonly MisskeyOverlayService overlays = new();

    public MenuTests()
    {
        Services.AddSingleton<IMenuInterop>(menuInterop);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMfmParserInterop>(new TextMfmParser());
    }

    [Fact]
    public async Task RendersPinnedKindsAndPropagatesActionedSwitchAndAsyncResolution()
    {
        var pending = new TaskCompletionSource<MisskeyMenuItem?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        NoteAuthorViewModel user = User("alice", "Alice");
        int userActions = 0;
        bool? switchValue = null;
        var closeResults = new List<bool>();
        IReadOnlyList<MisskeyMenuItem> items =
        [
            new(MisskeyMenuItemKind.Label, "Section"),
            MisskeyMenuItem.Pending(pending.Task),
            new(
                MisskeyMenuItemKind.Link,
                "Profile",
                "fas fa-user",
                "/@alice",
                Avatar: user,
                Indicate: true),
            new(
                MisskeyMenuItemKind.ExternalLink,
                "Download",
                "fas fa-download",
                "https://example.test/file",
                Target: "_blank",
                Download: "file.txt",
                Indicate: true),
            new(
                MisskeyMenuItemKind.User,
                User: user,
                Action: () =>
                {
                    userActions++;
                    return Task.CompletedTask;
                },
                Indicate: true),
            new(
                MisskeyMenuItemKind.Switch,
                "Enabled",
                SwitchValue: true,
                SwitchChanged: value =>
                {
                    switchValue = value;
                    return Task.CompletedTask;
                }),
            new(
                MisskeyMenuItemKind.Parent,
                "Parent",
                "fas fa-folder",
                Children: [new(MisskeyMenuItemKind.Action, "Child")]),
            new(MisskeyMenuItemKind.Action, "Delete", "fas fa-trash", Danger: true),
            MisskeyMenuItem.Divider
        ];

        IRenderedComponent<MkMenu> component = Render<MkMenu>(parameters => parameters
            .Add(menu => menu.Items, items)
            .Add(menu => menu.ViaKeyboard, true)
            .Add(menu => menu.Align, "center")
            .Add(menu => menu.Width, 288)
            .Add(menu => menu.MaxHeight, 321.25)
            .Add(menu => menu.CssClass, "sfhdhdhq")
            .Add(menu => menu.Close, result => closeResults.Add(result)));

        IElement root = component.Find("div.sfhdhdhq");
        IElement menu = root.QuerySelector(":scope > .rrevdjwt._popup._shadow.center:not(.asDrawer)")!;
        Assert.Contains("width: 288px", menu.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("max-height: 321.25px", menu.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Equal(8, menu.QuerySelectorAll(":scope > .item").Length);
        Assert.Single(menu.QuerySelectorAll(":scope > .divider"));
        Assert.Equal("Section", menu.QuerySelector(":scope > .label.item > span")?.TextContent);
        Assert.NotNull(menu.QuerySelector(":scope > .pending.item .mk-ellipsis"));
        Assert.NotNull(menu.QuerySelector(":scope > a.item[href='/@alice'] > .avatar"));
        Assert.NotNull(menu.QuerySelector(":scope > a.item[href='/@alice'] > .indicator > i.fas.fa-circle"));
        Assert.Equal("file.txt", menu.QuerySelector("a[download]")?.GetAttribute("download"));
        Assert.Equal("noopener noreferrer", menu.QuerySelector("a[target='_blank']")?.GetAttribute("rel"));
        Assert.Equal("Alice", menu.QuerySelector(":scope > button.item .havbbuyv")?.TextContent);
        Assert.NotNull(menu.QuerySelector(":scope > .item > .form-switch.checked"));
        Assert.NotNull(menu.QuerySelector(":scope > button.item.parent > .caret > i.fa-caret-right"));
        Assert.NotNull(menu.QuerySelector(":scope > button.item.danger > i.fa-trash"));
        Assert.True(menuInterop.ViaKeyboard);

        component.Find(".form-switch .button").Click();
        Assert.False(switchValue);
        Assert.Empty(closeResults);
        Assert.NotNull(component.Find(".form-switch:not(.checked)"));

        component.Find("button.item:not(.parent):not(.danger)").Click();
        Assert.Equal(1, userActions);
        Assert.Equal([true], closeResults);

        pending.SetResult(new MisskeyMenuItem(MisskeyMenuItemKind.Action, "Resolved"));
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll(".pending.item"));
            Assert.Equal("Resolved", component.FindAll("button.item").Single(button => button.TextContent == "Resolved").TextContent);
        });
    }

    [Fact]
    public async Task ParentHoverPositionsChildAndBubblesActionedWhileEscapeIsNotActioned()
    {
        int childActions = 0;
        var closeResults = new List<bool>();
        MisskeyMenuItem parent = new(
            MisskeyMenuItemKind.Parent,
            "Parent",
            Children:
            [
                new(
                    MisskeyMenuItemKind.Action,
                    "Child action",
                    Action: () =>
                    {
                        childActions++;
                        return Task.CompletedTask;
                    })
            ]);
        IRenderedComponent<MkMenu> component = Render<MkMenu>(parameters => parameters
            .Add(menu => menu.Items, [parent, new MisskeyMenuItem(MisskeyMenuItemKind.Action, "Sibling")])
            .Add(menu => menu.Close, result => closeResults.Add(result)));

        component.Find("button.parent").MouseEnter();
        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find("button.parent.childShowing"));
            Assert.NotNull(component.Find(".child > .sfhdhdhr > div > .rrevdjwt"));
            Assert.True(menuInterop.PositionCalls >= 1);
            Assert.True(menuInterop.Handle.SetChildTargetCalls >= 1);
        });

        component.Find(".child button.item").Click();
        Assert.Equal(1, childActions);
        Assert.Equal([true], closeResults);

        await component.InvokeAsync(component.Instance.NotifyClose);
        Assert.Equal([true, false], closeResults);
    }

    [Fact]
    public void EmptyDrawerKeepsPinnedNoneAndDrawerGeometry()
    {
        IRenderedComponent<MkMenu> component = Render<MkMenu>(parameters => parameters
            .Add(menu => menu.Items, [null!])
            .Add(menu => menu.AsDrawer, true)
            .Add(menu => menu.Align, "center")
            .Add(menu => menu.Width, 288)
            .Add(menu => menu.MaxHeight, 562.667));

        IElement menu = component.Find(".rrevdjwt._popup._shadow.center.asDrawer");
        Assert.Equal("なし", menu.QuerySelector(":scope > .none.item > span")?.TextContent);
        Assert.Contains("max-height: 562.667px", menu.GetAttribute("style"), StringComparison.Ordinal);
        Assert.DoesNotContain("width:", menu.GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public void DrawerParentUsesPopupMenuInsteadOfAnInlineChild()
    {
        var closeResults = new List<bool>();
        IReadOnlyList<MisskeyMenuItem> children =
        [new(MisskeyMenuItemKind.Action, "Child action")];
        IRenderedComponent<MkMenu> component = Render<MkMenu>(parameters => parameters
            .Add(menu => menu.Items,
            [
                new MisskeyMenuItem(
                    MisskeyMenuItemKind.Parent,
                    "Parent",
                    Children: children)
            ])
            .Add(menu => menu.AsDrawer, true)
            .Add(menu => menu.Close, result => closeResults.Add(result)));

        component.Find("button.parent").MouseEnter();

        Assert.Empty(component.FindAll(".child"));
        MisskeyOverlayEntry childPopup = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PopupMenu, childPopup.Kind);
        Assert.Same(children, childPopup.MenuItems);
        Assert.Equal([false], closeResults);
    }

    private static NoteAuthorViewModel User(string username, string displayName) => new(
        username,
        username,
        username,
        displayName,
        "/static-assets/user-unknown.png",
        IsBot: false);

    private sealed class RecordingMenuInterop : IMenuInterop
    {
        public RecordingHandle Handle { get; } = new();

        public bool ViaKeyboard { get; private set; }

        public int PositionCalls { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            ElementReference items,
            bool viaKeyboard,
            DotNetObjectReference<MkMenu> receiver,
            CancellationToken cancellationToken)
        {
            _ = root;
            _ = items;
            _ = receiver;
            cancellationToken.ThrowIfCancellationRequested();
            ViaKeyboard = viaKeyboard;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask PositionChildAsync(
            ElementReference child,
            ElementReference target,
            ElementReference root,
            CancellationToken cancellationToken)
        {
            _ = child;
            _ = target;
            _ = root;
            cancellationToken.ThrowIfCancellationRequested();
            PositionCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int SetChildTargetCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            _ = args;
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "setChildTarget")
            {
                SetChildTargetCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TextMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken)
        {
            _ = plain;
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<MfmNode> nodes =
            [new("text", JsonSerializer.SerializeToElement(new { text }), null)];
            return ValueTask.FromResult(nodes);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedLocalizer : IMisskeyLocalizer
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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _ = arguments;
            return key == "none" ? "なし" : key;
        }

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
