using System.Globalization;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class EmojiPickerTests : BunitContext
{
    private static readonly string[] ExpectedCategories =
        ["face", "people", "animals_and_nature", "food_and_drink", "activity", "travel_and_places", "objects", "symbols", "flags"];
    private static readonly string[] RecentEmojis = ["😀", "🎉"];
    private static readonly string[] ReactionEmojis = ["🦊", "🎯"];

    [Fact]
    public void CatalogMatchesThePinnedMisskeyTwelveEmojiData()
    {
        var catalog = new EmojiCatalog();

        Assert.Equal(1_782, catalog.Emojis.Count);
        Assert.Equal(ExpectedCategories, catalog.Categories);
        UnicodeEmojiDefinition grinning = Assert.Single(catalog.Emojis, emoji => emoji.Name == "grinning");
        Assert.Equal("😀", grinning.Value);
        Assert.Contains("smile", grinning.Keywords);
        Assert.Equal("1f600", MkEmoji.TwemojiFileName(grinning.Value));
        Assert.Equal("1f469-200d-1f4bb", MkEmoji.TwemojiFileName("👩‍💻"));
    }

    [Fact]
    public void UsesTheUpstreamPickerHierarchyAndSearchesTheCompleteCatalog()
    {
        Services.AddSingleton<IEmojiCatalog, EmojiCatalog>();
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IEmojiPickerInterop>(new RecordingEmojiInterop());
        Services.AddSingleton<IRippleEffectInterop>(new NoOpRippleInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        string? chosen = null;

        IRenderedComponent<MkEmojiPicker> component = Render<MkEmojiPicker>(parameters => parameters
            .Add(value => value.CssClass, "ryghynhb _popup _shadow")
            .Add(value => value.Chosen, value => chosen = value));

        Assert.NotNull(component.Find(".omfetrab.s1.w3.h2.ryghynhb._popup._shadow > input.search"));
        Assert.Equal(10, component.FindAll(".omfetrab > .emojis > .group.index > section:first-child > .body > button.item").Count);
        Assert.Equal(9, component.FindAll(".omfetrab > .emojis > .group:last-child > section").Count);

        component.Find("input.search").Input("grinning");
        IReadOnlyList<AngleSharp.Dom.IElement> results = component.FindAll(".omfetrab > .emojis > section.result > .body > button.item");
        Assert.NotEmpty(results);
        Assert.Equal("grinning", results[0].GetAttribute("title"));
        results[0].Click();
        Assert.Equal("😀", chosen);
    }

    [Fact]
    public async Task ReactionPickerUsesPizzaxDimensionsPinnedStaticImagesPasteRippleLocalizationAndRecentLimit()
    {
        string[] recents = Enumerable.Range(0, 32).Select(index => $"recent-{index}").ToArray();
        var state = new FixedDeviceState(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["reactions"] = ReactionEmojis,
            ["reactionPickerSize"] = 3,
            ["reactionPickerWidth"] = 5,
            ["reactionPickerHeight"] = 4,
            ["disableShowingAnimatedImages"] = true,
            ["recentlyUsedEmojis"] = recents
        });
        var interop = new RecordingEmojiInterop();
        Services.AddSingleton<IEmojiCatalog, EmojiCatalog>();
        Services.AddSingleton<IPizzaxDeviceState>(state);
        Services.AddSingleton<IEmojiPickerInterop>(interop);
        Services.AddSingleton<IRippleEffectInterop>(new NoOpRippleInterop());
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        var chosen = new List<string>();
        IRenderedComponent<MkEmojiPicker> component = Render<MkEmojiPicker>(parameters => parameters
            .Add(picker => picker.AsReactionPicker, true)
            .Add(picker => picker.CustomEmojis, [new("party", "/media/party.webp", "fun", ["tada"])])
            .Add(picker => picker.Chosen, value => chosen.Add(value)));

        component.WaitForAssertion(() => Assert.NotNull(component.Find(".omfetrab.s3.w5.h4")));
        Assert.Equal("絵文字を検索", component.Find("input.search").GetAttribute("placeholder"));
        Assert.Equal(2, component.FindAll(".group.index > section:first-child > .body > button.item").Count);
        Assert.Contains("最近使った絵文字", component.Find(".group.index > section:last-child > header").TextContent, StringComparison.Ordinal);
        Assert.Contains("カスタム絵文字", component.Find(".emojis > .group:nth-of-type(2) > header").TextContent, StringComparison.Ordinal);
        Assert.Contains("絵文字", component.Find(".emojis > .group:last-child > header").TextContent, StringComparison.Ordinal);
        Assert.Equal(32, component.FindAll(".group.index > section:last-child > .body > button.item").Count);

        component.Find("input.search").Input("part");
        Assert.True(interop.ResetCalls >= 1);
        AngleSharp.Dom.IElement custom = component.Find("section.result button.item[title='party']");
        Assert.Equal("/media/party.webp?static=1", custom.QuerySelector("img")?.GetAttribute("src"));
        custom.Click(new MouseEventArgs { ClientX = 120, ClientY = 80 });
        Assert.Equal(":party:", chosen[^1]);
        Assert.NotNull(component.Find(".vswabwbm"));
        string[] written = Assert.IsType<string[]>(state.LastWrite);
        Assert.Equal(32, written.Length);
        Assert.Equal(":party:", written[0]);

        Assert.True(await component.Instance.NotifyPasted(":party:"));
        component.WaitForAssertion(() => Assert.Equal(":party:", chosen[^1]));
        component.Find("input.search").Input("grinn");
        Assert.True(await component.Instance.NotifyPasted("not-an-exact-match"));
        component.WaitForAssertion(() => Assert.Equal("😀", chosen[^1]));
    }

    [Fact]
    public void SectionPreservesThePinnedSlotToggleAndChosenMouseEventContract()
    {
        string? chosen = null;
        EmojiPickerChosenEvent? detailed = null;
        IRenderedComponent<MkEmojiPickerSection> component = Render<MkEmojiPickerSection>(parameters => parameters
            .Add(section => section.Emojis, ["😀", "🎉"])
            .Add(section => section.Chosen, value => chosen = value)
            .Add(section => section.ChosenWithEvent, value => detailed = value)
            .AddChildContent("Faces"));

        Assert.Contains("Faces (2)", component.Find("section > header").TextContent, StringComparison.Ordinal);
        Assert.Contains("fa-chevron-up", component.Find("header > i.toggle").ClassName, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("section > .body"));

        component.Find("section > header").Click();
        Assert.Contains("fa-chevron-down", component.Find("header > i.toggle").ClassName, StringComparison.Ordinal);
        IReadOnlyList<AngleSharp.Dom.IElement> buttons = component.FindAll("section > .body > button._button.item");
        Assert.Equal(2, buttons.Count);
        buttons[1].Click(new MouseEventArgs { ClientX = 42, ClientY = 24 });

        Assert.Equal("🎉", chosen);
        Assert.NotNull(detailed);
        Assert.Equal("🎉", detailed.Value);
        Assert.Equal(42, detailed.Event.ClientX);
        Assert.Equal(24, detailed.Event.ClientY);
    }

    [Fact]
    public async Task PizzaxDeviceStatePreservesUnrelatedVueSettings()
    {
        var storage = new RecordingStorage();
        var state = new PizzaxDeviceState(storage);

        await state.WriteAsync("recentlyUsedEmojis", RecentEmojis);

        Dictionary<string, JsonElement> document = Assert.IsType<Dictionary<string, JsonElement>>(storage.Value);
        Assert.Equal("deck", document["ui"].GetString());
        Assert.Equal(RecentEmojis, document["recentlyUsedEmojis"].Deserialize<string[]>());
    }

    private sealed class FixedDeviceState(IReadOnlyDictionary<string, object>? values = null) : IPizzaxDeviceState
    {
        public object? LastWrite { get; private set; }

        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default)
        {
            object value = values is not null && values.TryGetValue(propertyName, out object? configured)
                ? configured
                : fallback!;
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default)
        {
            LastWrite = value;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingEmojiInterop : IEmojiPickerInterop, IDisposable
    {
        public int FocusCalls { get; private set; }

        public int ResetCalls { get; private set; }

        public ValueTask FocusAsync(ElementReference search, CancellationToken cancellationToken)
        {
            FocusCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(ElementReference emojis, CancellationToken cancellationToken)
        {
            ResetCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class NoOpRippleInterop : IRippleEffectInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "search" => "絵文字を検索",
            "recentUsed" => "最近使った絵文字",
            "customEmojis" => "カスタム絵文字",
            "emoji" => "絵文字",
            "other" => "その他",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class RecordingStorage : IClientStorage
    {
        public object? Value { get; private set; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["ui"] = JsonSerializer.SerializeToElement("deck")
        };

        public ValueTask<T?> ReadAsync<T>(ClientStorageArea area, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult((T?)Value);

        public ValueTask WriteAsync<T>(ClientStorageArea area, string key, T value, CancellationToken cancellationToken = default)
        {
            Value = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(ClientStorageArea area, string key, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
