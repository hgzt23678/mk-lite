using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class PollEditorTests : BunitContext
{
    public PollEditorTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new PollEditorLocalizer());
        Services.AddSingleton<IFormInputInterop, DisconnectedFormInputInterop>();
        Services.AddSingleton<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
    }

    [Fact]
    public void PreservesThePinnedHierarchyLocalizationAndImmediateChoiceUpdates()
    {
        var model = new ComposerPollViewModel { Choices = ["最初の選択肢"] };
        int changes = 0;

        IRenderedComponent<MkPollEditor> component = Render<MkPollEditor>(parameters => parameters
            .Add(editor => editor.Model, model)
            .Add(editor => editor.ModelChanged, _ => changes++)
            .AddUnmatched("class", "fixture-poll")
            .AddUnmatched("data-contract", "poll-editor"));

        Assert.NotNull(component.Find(".zmdxowus.fixture-poll[data-contract=poll-editor]"));
        Assert.Equal(
            "選択肢は最低2つ必要です",
            component.Find(".zmdxowus > .caution").TextContent);
        Assert.NotNull(component.Find(".caution > .fas.fa-exclamation-triangle"));
        Assert.Single(component.FindAll(".zmdxowus > ul > li"));

        AngleSharp.Dom.IElement input = component.Find(".zmdxowus > ul > li > .matxzzsk.input input");
        Assert.Equal("選択肢1", input.GetAttribute("placeholder"));
        Assert.Null(input.GetAttribute("maxlength"));
        Assert.Equal("--mk-form-input-height: 36px;", component.Find(".matxzzsk.input").GetAttribute("style"));
        Assert.Null(component.Find(".zmdxowus > ul > li > button._button").GetAttribute("aria-label"));

        input.Input("更新した選択肢");

        Assert.Equal("更新した選択肢", model.Choices[0]);
        Assert.Equal(1, changes);
        Assert.Equal("更新した選択肢", component.Find(".zmdxowus > ul > li input").GetAttribute("value"));
    }

    [Fact]
    public void AddsUpToTenChoicesAndRemovalRestoresThePinnedAddAction()
    {
        var model = new ComposerPollViewModel
        {
            Choices = Enumerable.Range(1, 9).Select(index => $"選択肢{index}").ToList()
        };
        int changes = 0;
        IRenderedComponent<MkPollEditor> component = Render<MkPollEditor>(parameters => parameters
            .Add(editor => editor.Model, model)
            .Add(editor => editor.ModelChanged, _ => changes++));

        component.Find(".zmdxowus > button.add").Click();

        Assert.Equal(10, model.Choices.Count);
        Assert.Equal(10, component.FindAll(".zmdxowus > ul > li").Count);
        AngleSharp.Dom.IElement maximum = component.Find(".zmdxowus > button.add");
        Assert.NotNull(maximum.GetAttribute("disabled"));
        Assert.Equal("これ以上追加できません", maximum.TextContent.Trim());

        component.Find(".zmdxowus > ul > li:first-child > button._button").Click();

        Assert.Equal(9, model.Choices.Count);
        Assert.Equal("選択肢2", model.Choices[0]);
        Assert.Null(component.Find(".zmdxowus > button.add").GetAttribute("disabled"));
        Assert.Equal("追加", component.Find(".zmdxowus > button.add").TextContent.Trim());
        Assert.Equal(2, changes);
    }

    [Fact]
    public void ProjectsMultipleAndBothExpirationModesThroughTheExistingDraftModel()
    {
        var model = new ComposerPollViewModel();
        int changes = 0;
        IRenderedComponent<MkPollEditor> component = Render<MkPollEditor>(parameters => parameters
            .Add(editor => editor.Model, model)
            .Add(editor => editor.ModelChanged, _ => changes++));

        Assert.Equal("複数回答可", component.Find(".ziffeomt > .label > span").TextContent);
        component.Find(".ziffeomt > .button").Click();
        Assert.True(model.Multiple);
        Assert.Contains("checked", component.Find(".ziffeomt").ClassList);

        AngleSharp.Dom.IElement expiration = component.Find(".zmdxowus > section > div > .vblkjoeq select");
        expiration.Input("at");
        Assert.Equal(ComposerPollExpiration.At, model.Expiration);
        Assert.Equal("期日", component.Find("input[type=date]").ParentElement?.ParentElement?.QuerySelector(":scope > .label")?.TextContent);
        Assert.Equal("時間", component.Find("input[type=time]").ParentElement?.ParentElement?.QuerySelector(":scope > .label")?.TextContent);

        component.Find("input[type=date]").Input("2026-08-10");
        component.Find("input[type=time]").Input("12:30");
        Assert.Equal(new DateOnly(2026, 8, 10), model.AtDate);
        Assert.Equal(new TimeOnly(12, 30), model.AtTime);

        component.Find(".zmdxowus > section > div > .vblkjoeq select").Input("after");
        Assert.Equal(ComposerPollExpiration.After, model.Expiration);
        AngleSharp.Dom.IElement after = component.Find("input[type=number]");
        Assert.Null(after.GetAttribute("min"));
        after.Input("2");
        component.Find(".zmdxowus > section > div > section .vblkjoeq select").Input("day");

        Assert.Equal(2, model.After);
        Assert.Equal(ComposerPollUnit.Day, model.Unit);
        Assert.Equal(7, changes);
    }

    private sealed class DisconnectedFormInputInterop : IFormInputInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit has no form geometry bridge.");

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
            throw new JSDisconnectedException("bUnit has no pointer ripple bridge.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class PollEditorLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged;

        public string CurrentLocale => "ja-JP";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo("ja-JP");

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "_poll.noOnlyOneChoice" => "選択肢は最低2つ必要です",
            "_poll.choiceN" => $"選択肢{arguments!["n"]}",
            "_poll.noMore" => "これ以上追加できません",
            "_poll.canMultipleVote" => "複数回答可",
            "_poll.expiration" => "期限",
            "_poll.infinite" => "無期限",
            "_poll.at" => "日時指定",
            "_poll.after" => "経過指定",
            "_poll.deadlineDate" => "期日",
            "_poll.deadlineTime" => "時間",
            "_poll.duration" => "期間",
            "_time.second" => "秒",
            "_time.minute" => "分",
            "_time.hour" => "時間",
            "_time.day" => "日",
            "add" => "追加",
            "itsOn" => "オンになっています",
            "itsOff" => "オフになっています",
            _ => key
        };

        public bool TrySelectLocale(string? locale)
        {
            LocaleChanged?.Invoke(this, EventArgs.Empty);
            return string.Equals(locale, CurrentLocale, StringComparison.OrdinalIgnoreCase);
        }
    }
}
