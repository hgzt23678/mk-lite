using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class PopupMenuTests : BunitContext
{
    public PopupMenuTests()
    {
        Services.AddSingleton<IMenuInterop>(new RecordingMenuInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
    }

    [Fact]
    public async Task PreservesPinnedPopupItemsAlignmentWidthKeyboardAndClosedContract()
    {
        var browser = new RecordingModalInterop(isDrawer: false, maximumHeight: 321.25, sourceWidth: 144);
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IModalInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        int actionCalls = 0;
        int closedCalls = 0;
        IReadOnlyList<MisskeyMenuItem> items =
        [
            new(MisskeyMenuItemKind.Label, "Section"),
            new(MisskeyMenuItemKind.Action, "Danger", "fas fa-trash", Action: () =>
            {
                actionCalls++;
                return Task.CompletedTask;
            }, Danger: true),
            MisskeyMenuItem.Link("About", "fas fa-info-circle", "/about"),
            MisskeyMenuItem.ExternalLink("Help", "fas fa-question-circle", "https://misskey-hub.net/help.md"),
            MisskeyMenuItem.Divider
        ];
        Guid id = overlays.ShowPopupMenu(default, items);

        IRenderedComponent<MkPopupMenu> component = Render<MkPopupMenu>(parameters => parameters
            .Add(menu => menu.Id, id)
            .Add(menu => menu.Src, default(ElementReference))
            .Add(menu => menu.Items, items)
            .Add(menu => menu.ViaKeyboard, true)
            .Add(menu => menu.Align, "center")
            .Add(menu => menu.Width, 288)
            .Add(menu => menu.MatchSourceWidth, true)
            .Add(menu => menu.Closed, () =>
            {
                closedCalls++;
                return Task.CompletedTask;
            }));

        component.WaitForAssertion(() => Assert.Equal(1, browser.AttachCalls));
        Assert.True(browser.ViaKeyboard);
        Assert.Equal("qzhlnise popup modal-popup-enter-active", component.Find(".qzhlnise").ClassName);
        Assert.NotNull(component.Find(".qzhlnise > .bg._modalBg.transparent"));
        Assert.NotNull(component.Find(".qzhlnise > .content > .sfhdhdhq:not(.drawer)"));
        IElement menu = component.Find(".sfhdhdhq > .rrevdjwt._popup._shadow.center:not(.asDrawer)");
        Assert.Contains("max-height: 321.25px", menu.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("width: 288px", menu.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Equal(4, menu.QuerySelectorAll(":scope > .item").Length);
        Assert.Single(menu.QuerySelectorAll(":scope > .divider"));
        Assert.Equal("Section", menu.QuerySelector(":scope > .label.item > span")?.TextContent);
        Assert.NotNull(menu.QuerySelector(":scope > button.item.danger > i.fa-fw.fas.fa-trash"));
        Assert.Equal("noopener noreferrer", menu.QuerySelector("a[target='_blank']")?.GetAttribute("rel"));

        component.Find("button.item.danger").Click();
        Assert.Equal(1, actionCalls);
        Assert.Equal(1, browser.Handle.CloseCalls);

        await component.InvokeAsync(component.Instance.NotifyClosed);
        Assert.Equal(1, closedCalls);
        Assert.Empty(overlays.Entries);
    }

    [Fact]
    public void DrawerUsesPinnedClassesMaximumHeightAndIgnoresPopupWidth()
    {
        var browser = new RecordingModalInterop(isDrawer: true, maximumHeight: 562.667, sourceWidth: 144);
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IModalInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        IReadOnlyList<MisskeyMenuItem> items = [new(MisskeyMenuItemKind.Action, "Action")];
        Guid id = overlays.ShowPopupMenu(default, items);

        IRenderedComponent<MkPopupMenu> component = Render<MkPopupMenu>(parameters => parameters
            .Add(menu => menu.Id, id)
            .Add(menu => menu.Src, default(ElementReference))
            .Add(menu => menu.Items, items)
            .Add(menu => menu.Align, "center")
            .Add(menu => menu.Width, 288));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("qzhlnise drawer modal-drawer-enter-active", component.Find(".qzhlnise").ClassName);
            IElement wrapper = component.Find(".content > .sfhdhdhq.drawer");
            IElement menu = wrapper.QuerySelector(":scope > .rrevdjwt._popup._shadow.center.asDrawer")!;
            Assert.Contains("max-height: 562.667px", menu.GetAttribute("style"), StringComparison.Ordinal);
            Assert.DoesNotContain("width:", menu.GetAttribute("style"), StringComparison.Ordinal);
        });
    }

    private sealed class RecordingModalInterop(bool isDrawer, double maximumHeight, double sourceWidth) : IModalInterop
    {
        public int AttachCalls { get; private set; }

        public bool ViaKeyboard { get; private set; }

        public RecordingHandle Handle { get; } = new();

        public ValueTask<ModalAttachment> AttachAsync(
            ElementReference source,
            ElementReference modal,
            ElementReference content,
            bool openedViaKeyboard,
            DotNetObjectReference<MkPopupMenu> receiver,
            CancellationToken cancellationToken)
        {
            _ = source;
            _ = modal;
            _ = content;
            _ = receiver;
            cancellationToken.ThrowIfCancellationRequested();
            AttachCalls++;
            ViaKeyboard = openedViaKeyboard;
            return ValueTask.FromResult(new ModalAttachment(
                Handle,
                isDrawer,
                maximumHeight,
                "center top",
                sourceWidth));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int CloseCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            _ = args;
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(identifier, "close", StringComparison.Ordinal))
            {
                CloseCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingMenuInterop : IMenuInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            ElementReference items,
            bool viaKeyboard,
            DotNetObjectReference<MkMenu> receiver,
            CancellationToken cancellationToken)
        {
            _ = root;
            _ = items;
            _ = viaKeyboard;
            _ = receiver;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());
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
            return ValueTask.CompletedTask;
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
