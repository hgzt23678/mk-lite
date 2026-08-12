using System.Globalization;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class WidgetsTests : BunitContext
{
    [Fact]
    public async Task UniversalWidgetsLoadsPizzaxAndPreservesUnsupportedEntriesAcrossMutations()
    {
        IReadOnlyList<MisskeyWidgetModel> initial =
        [
            Widget("rss", "unsupported"),
            Widget("calendar", "calendar"),
            Widget("digitalClock", "digital")
        ];
        var state = new RecordingDeviceState(initial);
        Services.AddSingleton<IPizzaxDeviceState>(state);
        Services.AddSingleton<IMisskeyLocalizer>(new WidgetLocalizer());
        ComponentFactories.AddStub<MkWidgets>();

        using IRenderedComponent<MisskeyWidgets> component = Render<MisskeyWidgets>();
        await component.InvokeAsync(() => Task.CompletedTask);
        Bunit.TestDoubles.Stub<MkWidgets> widgets = component.FindComponent<Bunit.TestDoubles.Stub<MkWidgets>>().Instance;
        IReadOnlyList<MisskeyWidgetModel> loaded = widgets.Parameters.Get(value => value.Widgets);
        Assert.Equal(["rss", "calendar", "digitalClock"], loaded.Select(widget => widget.Name));
        Assert.NotNull(component.Find(".efzpzdvf > .mk-widget-edit"));

        component.Find(".mk-widget-edit").Click();
        Assert.True(component.FindComponent<Bunit.TestDoubles.Stub<MkWidgets>>().Instance.Parameters.Get(value => value.Edit));

        await component.InvokeAsync(() => widgets.Parameters.Get(value => value.UpdateWidgets).InvokeAsync(
        [
            Widget("digitalClock", "digital"),
            Widget("calendar", "calendar")
        ]));
        Assert.Equal(["rss", "digitalClock", "calendar"], state.LastWritten.Select(widget => widget.Name));

        await component.InvokeAsync(() =>
            widgets.Parameters.Get(value => value.AddWidget).InvokeAsync(Widget("clock", "clock")));
        Assert.Equal(["clock", "rss", "digitalClock", "calendar"], state.LastWritten.Select(widget => widget.Name));

        await component.InvokeAsync(() =>
            widgets.Parameters.Get(value => value.RemoveWidget).InvokeAsync(Widget("digitalClock", "digital")));
        Assert.Equal(["clock", "rss", "calendar"], state.LastWritten.Select(widget => widget.Name));

        IReadOnlyDictionary<string, JsonElement> data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["transparent"] = JsonSerializer.SerializeToElement(true)
        };
        await component.InvokeAsync(() =>
            widgets.Parameters.Get(value => value.UpdateWidget).InvokeAsync(new MisskeyWidgetUpdate("calendar", data)));
        Assert.True(state.LastWritten.Single(widget => widget.Id == "calendar").Data["transparent"].GetBoolean());
    }

    [Fact]
    public async Task MkWidgetsUsesExactEditDomSupportedChoicesAndRealCallbacks()
    {
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IWidgetsInterop>(new NoOpWidgetsInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new WidgetLocalizer());
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        ComponentFactories.AddStub<MkFormSelect>();
        ComponentFactories.AddStub<MkButton>();
        ComponentFactories.AddStub<MkwWidgetHost>();
        MisskeyWidgetModel? added = null;
        MisskeyWidgetModel? removed = null;
        MisskeyWidgetUpdate? updated = null;
        IReadOnlyList<MisskeyWidgetModel>? reordered = null;
        int exits = 0;
        IReadOnlyList<MisskeyWidgetModel> source =
        [
            Widget("calendar", "calendar"),
            Widget("rss", "unsupported"),
            Widget("digitalClock", "digital")
        ];

        using IRenderedComponent<MkWidgets> component = Render<MkWidgets>(parameters => parameters
            .Add(value => value.Edit, true)
            .Add(value => value.Widgets, source)
            .Add(value => value.AddWidget, value => added = value)
            .Add(value => value.RemoveWidget, value => removed = value)
            .Add(value => value.UpdateWidget, value => updated = value)
            .Add(value => value.UpdateWidgets, value => reordered = value)
            .Add(value => value.Exit, () => exits++));

        Assert.NotNull(component.Find(".vjoppmmu > header"));
        Assert.Equal(2, component.FindAll(".vjoppmmu .customize-container").Count);
        Assert.Equal(["calendar", "digital"], component
            .FindAll(".customize-container")
            .Select(element => element.GetAttribute("data-widget-id")));
        Assert.Equal(2, component.FindAll(".customize-container > .config._button").Count);
        Assert.Equal(2, component.FindAll(".customize-container > .remove._button").Count);
        Assert.Equal(2, component.FindAll(".customize-container > .handle[draggable=true]").Count);

        Bunit.TestDoubles.Stub<MkFormSelect> select = component.FindComponent<Bunit.TestDoubles.Stub<MkFormSelect>>().Instance;
        IReadOnlyList<MkFormSelectItem> choices = select.Parameters.Get(value => value.Items);
        Assert.Equal(["timeline", "calendar", "clock", "digitalClock", "postForm", "memo", "unixClock", "trends"], choices.Select(choice => choice.Value));
        Assert.DoesNotContain(choices, choice => choice.Value is "rss" or "notifications");
        Assert.DoesNotContain(choices, choice => choice.Value is "aichan" or "button" or "aiscript" or "server-metric");
        await component.InvokeAsync(() => select.Parameters.Get(value => value.ValueChanged).InvokeAsync("clock"));

        IReadOnlyList<IRenderedComponent<Bunit.TestDoubles.Stub<MkButton>>> buttons = component.FindComponents<Bunit.TestDoubles.Stub<MkButton>>();
        await component.InvokeAsync(() =>
            buttons[0].Instance.Parameters.Get(value => value.OnClick).InvokeAsync(new MouseEventArgs()));
        Assert.Equal("clock", added?.Name);
        Assert.True(Guid.TryParse(added?.Id, out _));
        Assert.Null(added?.Place);
        await component.InvokeAsync(() =>
            buttons[1].Instance.Parameters.Get(value => value.OnClick).InvokeAsync(new MouseEventArgs()));
        Assert.Equal(1, exits);

        component.Find(".customize-container[data-widget-id=calendar] > .remove").Click();
        Assert.Equal("calendar", removed?.Id);

        component.Find(".customize-container[data-widget-id=calendar] > .config").Click();
        MisskeyOverlayEntry dialog = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.FormDialog, dialog.Kind);
        Assert.Equal("calendar", dialog.FormDialog?.Title);
        Assert.Equal("transparent", Assert.Single(dialog.FormDialog!.Form).Name);
        await component.InvokeAsync(() => dialog.FormDialog.Done!(new MisskeyFormDialogResult(
            Canceled: false,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["transparent"] = true })));
        Assert.Equal("calendar", updated?.Id);
        Assert.True(updated?.Data["transparent"].GetBoolean());

        await component.InvokeAsync(() => component.Instance.ReorderWidget("digital", "calendar", after: false));
        Assert.Equal(["digital", "calendar"], reordered?.Select(widget => widget.Id));
    }

    [Fact]
    public void MkWidgetsNonEditRendersOnlySupportedWidgetRoots()
    {
        Services.AddSingleton<IWidgetsInterop>(new NoOpWidgetsInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new WidgetLocalizer());
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        ComponentFactories.AddStub<MkwWidgetHost>();

        using IRenderedComponent<MkWidgets> component = Render<MkWidgets>(parameters => parameters
            .Add(value => value.Widgets,
            [
                Widget("calendar", "calendar"),
                Widget("notifications", "unsupported"),
                Widget("postForm", "post")
            ]));

        IReadOnlyList<IRenderedComponent<Bunit.TestDoubles.Stub<MkwWidgetHost>>> rendered =
            component.FindComponents<Bunit.TestDoubles.Stub<MkwWidgetHost>>();
        Assert.Equal(2, rendered.Count);
        Assert.Equal(["calendar", "postForm"], rendered.Select(item => item.Instance.Parameters.Get(value => value.Widget).Name));
        Assert.All(rendered, item => Assert.Equal("widget", item.Instance.Parameters.Get(value => value.CssClass)));
    }

    private static MisskeyWidgetModel Widget(string name, string id) => new()
    {
        Name = name,
        Id = id,
        Data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
    };

    private sealed class RecordingDeviceState : IPizzaxDeviceState
    {
        private readonly IReadOnlyList<MisskeyWidgetModel> initial;

        public RecordingDeviceState(IReadOnlyList<MisskeyWidgetModel> initial)
        {
            this.initial = initial;
            LastWritten = initial;
        }

        public IReadOnlyList<MisskeyWidgetModel> LastWritten { get; private set; }

        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default)
        {
            Assert.Equal("widgets", propertyName);
            return ValueTask.FromResult((T)(object)initial);
        }

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default)
        {
            Assert.Equal("widgets", propertyName);
            LastWritten = Assert.IsAssignableFrom<IReadOnlyList<MisskeyWidgetModel>>(value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpWidgetsInterop : IWidgetsInterop
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class WidgetLocalizer : IMisskeyLocalizer
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
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;
        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
