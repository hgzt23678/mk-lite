using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class ContextMenuTests : BunitContext
{
    public ContextMenuTests()
    {
        Services.AddSingleton<IMenuInterop>(new RecordingMenuInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
    }

    [Fact]
    public async Task PreservesPinnedPositionMenuAndClosedContract()
    {
        var browser = new RecordingContextMenuInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IContextMenuInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        int actionCalls = 0;
        int closedCalls = 0;
        IReadOnlyList<MisskeyMenuItem> items =
        [
            new(MisskeyMenuItemKind.Action, "Edit", "fas fa-pen", Action: () =>
            {
                actionCalls++;
                return Task.CompletedTask;
            }),
            MisskeyMenuItem.Divider,
            MisskeyMenuItem.Link("About", "fas fa-info-circle", "/about")
        ];
        Guid id = overlays.ShowContextMenu(120.5, 240.25, items);

        IRenderedComponent<MkContextMenu> component = Render<MkContextMenu>(parameters => parameters
            .Add(menu => menu.Id, id)
            .Add(menu => menu.X, 120.5)
            .Add(menu => menu.Y, 240.25)
            .Add(menu => menu.Items, items)
            .Add(menu => menu.Closed, () =>
            {
                closedCalls++;
                return Task.CompletedTask;
            }));

        component.WaitForAssertion(() => Assert.Equal(1, browser.AttachCalls));
        Assert.Equal(120.5, browser.X);
        Assert.Equal(240.25, browser.Y);
        Assert.True(browser.Animation);
        Assert.NotNull(component.Find(".nvlagfpb"));
        Assert.NotNull(component.Find(".nvlagfpb .rrevdjwt._popup._shadow"));
        IElement menu = component.Find(".rrevdjwt");
        Assert.Equal(2, menu.QuerySelectorAll(":scope > .item").Length);
        Assert.Single(menu.QuerySelectorAll(":scope > .divider"));
        Assert.NotNull(menu.QuerySelector("button.item > i.fa-fw.fas.fa-pen"));

        component.Find("button.item").Click();
        Assert.Equal(1, actionCalls);
        Assert.Equal(1, browser.Handle.CloseCalls);

        await component.InvokeAsync(component.Instance.NotifyClosed);
        Assert.Equal(1, closedCalls);
        Assert.Empty(overlays.ContextMenus);
    }

    [Fact]
    public async Task RequestCloseTopClosesTheContextMenu()
    {
        var browser = new RecordingContextMenuInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IContextMenuInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Guid id = overlays.ShowContextMenu(10, 20, [new(MisskeyMenuItemKind.Action, "Action")]);

        IRenderedComponent<MkContextMenu> component = Render<MkContextMenu>(parameters => parameters
            .Add(menu => menu.Id, id)
            .Add(menu => menu.X, 10)
            .Add(menu => menu.Y, 20)
            .Add(menu => menu.Items, new[] { new MisskeyMenuItem(MisskeyMenuItemKind.Action, "Action") }));

        component.WaitForAssertion(() => Assert.Equal(1, browser.AttachCalls));
        await overlays.RequestCloseTopAsync();
        Assert.Equal(1, browser.Handle.CloseCalls);
    }

    [Fact]
    public void ShowContextMenuRejectsNonFiniteCoordinates()
    {
        var overlays = new MisskeyOverlayService();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            overlays.ShowContextMenu(double.NaN, 10, [new(MisskeyMenuItemKind.Action, "Action")]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            overlays.ShowContextMenu(10, double.PositiveInfinity, [new(MisskeyMenuItemKind.Action, "Action")]));
    }

    private sealed class RecordingContextMenuInterop : IContextMenuInterop
    {
        public int AttachCalls { get; private set; }

        public double X { get; private set; }

        public double Y { get; private set; }

        public bool Animation { get; private set; }

        public RecordingHandle Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            double x,
            double y,
            bool animate,
            DotNetObjectReference<MkContextMenu> receiver,
            CancellationToken cancellationToken)
        {
            _ = root;
            _ = receiver;
            cancellationToken.ThrowIfCancellationRequested();
            AttachCalls++;
            X = x;
            Y = y;
            Animation = animate;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
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

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            _ = propertyName;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(fallback);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default)
        {
            _ = propertyName;
            _ = value;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
