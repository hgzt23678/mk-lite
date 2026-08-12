using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FormControlsPortTests : BunitContext
{
    public FormControlsPortTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new FormLocalizer());
        Services.AddSingleton<IRippleEffectInterop, DisconnectedRippleInterop>();
        Services.AddSingleton<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
    }

    [Fact]
    public void SlotAndSplitPreserveUpstreamHierarchySlotsAndGeometryContract()
    {
        int focusRequests = 0;
        IRenderedComponent<MkFormSlot> slot = Render<MkFormSlot>(parameters => parameters
            .Add(item => item.Label, builder => builder.AddContent(0, "ラベル"))
            .Add(item => item.Caption, builder => builder.AddContent(0, "説明"))
            .Add(item => item.FocusRequested, () => focusRequests++)
            .AddUnmatched("class", "fixture-slot")
            .AddChildContent("内容"));

        Assert.Equal("ラベル", slot.Find(".adhpbeou.fixture-slot > .label").TextContent);
        Assert.Equal("内容", slot.Find(".adhpbeou > .content").TextContent);
        Assert.Equal("説明", slot.Find(".adhpbeou > .caption").TextContent);
        slot.Find(".label").Click();
        Assert.Equal(1, focusRequests);

        IRenderedComponent<MkFormSplit> split = Render<MkFormSplit>(parameters => parameters
            .Add(item => item.MinWidth, 240)
            .AddUnmatched("class", "fixture-split")
            .AddChildContent(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "id", "first-field");
                builder.CloseElement();
                builder.OpenElement(2, "div");
                builder.AddAttribute(3, "id", "second-field");
                builder.CloseElement();
            }));

        Assert.NotNull(split.Find(".terlnhxf._formBlock.fixture-split > #first-field"));
        Assert.NotNull(split.Find(".terlnhxf > #second-field"));
        Assert.Equal("--mk-form-split-min-width: 240px;", split.Find(".terlnhxf").GetAttribute("style"));
    }

    [Fact]
    public void CheckboxPreservesHiddenInputSlotsDisabledStateAndCheckRipple()
    {
        bool value = false;
        IRenderedComponent<MkFormCheckbox> component = Render<MkFormCheckbox>(parameters => parameters
            .Add(item => item.Value, value)
            .Add(item => item.ValueChanged, next => value = next)
            .Add(item => item.Label, builder => builder.AddContent(0, "ラベル"))
            .Add(item => item.Caption, builder => builder.AddContent(0, "説明"))
            .AddChildContent("互換slot"));

        Assert.NotNull(component.Find(".ziffeoms > input[type=checkbox]"));
        Assert.NotNull(component.Find(".ziffeoms > .button > .check.fas.fa-check"));
        Assert.Equal("ラベル互換slot", component.Find(".ziffeoms > .label > span").TextContent);
        Assert.Equal("説明", component.Find(".ziffeoms > .label > .caption").TextContent);

        component.Find(".button").Click();

        Assert.True(value);
        Assert.NotNull(component.Find(".button > .checkbox-ripple"));

        component.Render(parameters => parameters
            .Add(item => item.Value, value)
            .Add(item => item.ValueChanged, next => value = next)
            .Add(item => item.Disabled, true)
            .AddChildContent("無効"));
        Assert.Contains("checked", component.Find(".ziffeoms").ClassList);
        Assert.Contains("disabled", component.Find(".ziffeoms").ClassList);
        component.Find(".label > span").Click();
        Assert.True(value);
    }

    [Fact]
    public void RadiosPreserveGroupSlotsAndImmediatelyProjectTheSelectedOption()
    {
        string value = "public";
        IRenderedComponent<MkFormRadios<string>> component = Render<MkFormRadios<string>>(parameters => parameters
            .Add(item => item.Value, value)
            .Add(item => item.ValueChanged, next => value = next)
            .Add(item => item.Label, builder => builder.AddContent(0, "公開範囲"))
            .Add(item => item.Caption, builder => builder.AddContent(0, "投稿の受信者"))
            .Add(item => item.ChildContent, builder =>
            {
                builder.OpenComponent<MkFormRadio<string>>(0);
                builder.AddAttribute(1, nameof(MkFormRadio<string>.Value), "public");
                builder.AddAttribute(2, nameof(MkFormRadio<string>.ChildContent),
                    (RenderFragment)(content => content.AddContent(0, "パブリック")));
                builder.CloseComponent();
                builder.OpenComponent<MkFormRadio<string>>(3);
                builder.AddAttribute(4, nameof(MkFormRadio<string>.Value), "followers");
                builder.AddAttribute(5, nameof(MkFormRadio<string>.ChildContent),
                    (RenderFragment)(content => content.AddContent(0, "フォロワー")));
                builder.CloseComponent();
            }));

        Assert.Equal("公開範囲", component.Find(".novjtcto > .label").TextContent);
        Assert.Equal(2, component.FindAll(".novjtcto > .body > .novjtctn").Count);
        Assert.Contains("checked", component.FindAll(".novjtctn")[0].ClassList);
        Assert.Equal("true", component.FindAll(".novjtctn")[0].GetAttribute("aria-checked"));

        component.FindAll(".novjtctn")[1].Click();

        Assert.Equal("followers", value);
        Assert.DoesNotContain("checked", component.FindAll(".novjtctn")[0].ClassList);
        Assert.Contains("checked", component.FindAll(".novjtctn")[1].ClassList);
        Assert.Equal("true", component.FindAll(".novjtctn")[1].GetAttribute("aria-checked"));
    }

    [Fact]
    public void TextareaPreservesAttributesFocusClassesEventsAndManualSave()
    {
        string value = "before";
        int changes = 0;
        int enters = 0;
        IRenderedComponent<MkFormTextarea> component = Render<MkFormTextarea>(parameters => parameters
            .Add(item => item.Value, value)
            .Add(item => item.ValueChanged, next => value = next)
            .Add(item => item.Changed, _ => changes++)
            .Add(item => item.Entered, () => enters++)
            .Add(item => item.Required, true)
            .Add(item => item.ReadOnly, true)
            .Add(item => item.Pattern, ".{3,}")
            .Add(item => item.Placeholder, "本文")
            .Add(item => item.Autocomplete, "off")
            .Add(item => item.Spellcheck, false)
            .Add(item => item.Code, true)
            .Add(item => item.Tall, true)
            .Add(item => item.Pre, true)
            .Add(item => item.ManualSave, true)
            .Add(item => item.Label, builder => builder.AddContent(0, "内容"))
            .Add(item => item.Caption, builder => builder.AddContent(0, "説明")));

        AngleSharp.Dom.IElement textarea = component.Find(".adhpbeos > .input.tall.pre > textarea.code._monospace");
        Assert.NotNull(textarea.GetAttribute("required"));
        Assert.NotNull(textarea.GetAttribute("readonly"));
        Assert.Equal(".{3,}", textarea.GetAttribute("pattern"));
        Assert.Equal("off", textarea.GetAttribute("autocomplete"));
        Assert.Equal("false", textarea.GetAttribute("spellcheck"));

        textarea.TriggerEvent("onfocus", new FocusEventArgs());
        Assert.Contains("focused", component.Find(".adhpbeos > .input").ClassList);
        textarea.Input("after");
        textarea.KeyDown(new KeyboardEventArgs { Code = "Enter" });

        Assert.Equal("before", value);
        Assert.Equal(1, changes);
        Assert.Equal(1, enters);
        Assert.Equal("保存", component.Find(".adhpbeos > .save").TextContent.Trim());

        component.Find(".adhpbeos > .save").Click();
        Assert.Equal("after", value);
        Assert.Empty(component.FindAll(".adhpbeos > .save"));
    }

    private sealed class DisconnectedRippleInterop : IRippleEffectInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class =>
            throw new JSDisconnectedException("bUnit has no SMIL event bridge.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedButtonRippleInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit has no pointer event bridge.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FormLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged;

        public string CurrentLocale => "ja-JP";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo("ja-JP");

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
            key switch
            {
                "save" => "保存",
                "itsOn" => "オン",
                "itsOff" => "オフ",
                _ => key
            };

        public bool TrySelectLocale(string? locale)
        {
            LocaleChanged?.Invoke(this, EventArgs.Empty);
            return string.Equals(locale, CurrentLocale, StringComparison.OrdinalIgnoreCase);
        }
    }
}
