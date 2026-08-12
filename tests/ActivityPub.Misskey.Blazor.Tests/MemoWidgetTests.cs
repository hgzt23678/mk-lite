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

public sealed class MemoWidgetTests : BunitContext
{
    private readonly RecordingContainerInterop container = new();

    public MemoWidgetTests()
    {
        Services.AddSingleton<IContainerInterop>(container);
        Services.AddSingleton<IMisskeyLocalizer>(new MemoLocalizer());
    }

    [Fact]
    public async Task LoadsMemoFromBrowserStorageAndSavesOnInput()
    {
        var device = new RecordingDeviceState();
        device.Seed("memo", "initial");
        Services.AddSingleton<IPizzaxDeviceState>(device);
        var widget = new MisskeyWidgetModel { Name = "memo", Id = "w1" };

        using IRenderedComponent<MkwMemo> component = Render<MkwMemo>(parameters => parameters
            .Add(memo => memo.Widget, widget));

        component.WaitForAssertion(() => Assert.Equal("initial", component.Find("textarea").GetAttribute("value")));
        Assert.Equal("mkw-memo", component.Find(".mkw-memo").ClassName);
        Assert.NotNull(component.Find(".mkw-memo .otgbylcu textarea"));
        IElement save = component.Find(".mkw-memo .otgbylcu button");
        Assert.True(save.HasAttribute("disabled"));

        component.Find("textarea").Input("hello");
        Assert.False(component.Find("button").HasAttribute("disabled"));
        component.Find("button").Click();
        Assert.Equal("hello", Assert.Single(device.Writes.Values));
    }

    [Fact]
    public void WidgetPropShowHeaderTogglesTheContainerHeader()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new RecordingDeviceState());
        var widget = new MisskeyWidgetModel
        {
            Name = "memo",
            Id = "w1",
            Data = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["showHeader"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("false")
            }
        };

        using IRenderedComponent<MkwMemo> component = Render<MkwMemo>(parameters => parameters
            .Add(memo => memo.Widget, widget));

        Assert.DoesNotContain("_header", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitSaveCancelsThePendingDebouncedWrite()
    {
        var device = new RecordingDeviceState();
        Services.AddSingleton<IPizzaxDeviceState>(device);
        var widget = new MisskeyWidgetModel { Name = "memo", Id = "w1" };

        using IRenderedComponent<MkwMemo> component = Render<MkwMemo>(parameters => parameters
            .Add(memo => memo.Widget, widget));
        component.WaitForAssertion(() => Assert.NotNull(component.Find("textarea")));

        component.Find("textarea").Input("one");
        component.Find("button").Click();
        Assert.Equal("one", Assert.Single(device.Writes.Values));

        await Task.Delay(1_150);
        Assert.Single(device.Writes.Values);
    }

    private sealed class RecordingContainerInterop : IContainerInterop
    {
        private readonly RecordingHandle handle = new();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference content,
            double? maxHeight,
            bool expanded,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingDeviceState : IPizzaxDeviceState
    {
        public Dictionary<string, object> Writes { get; } = new(StringComparer.Ordinal);

        public void Seed(string propertyName, object value) => Writes[propertyName] = value;

        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object? value = Writes.GetValueOrDefault(propertyName);
            return ValueTask.FromResult(value is T typed ? typed : fallback);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes[propertyName] = value!;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoLocalizer : IMisskeyLocalizer
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
            return key switch
            {
                "_widgets.memo" => "メモ",
                "placeholder" => "プレースホルダー",
                "save" => "保存",
                _ => key
            };
        }

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
