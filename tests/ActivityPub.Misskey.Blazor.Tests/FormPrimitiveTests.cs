using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FormPrimitiveTests : BunitContext
{
    public FormPrimitiveTests() => Services.AddSingleton<IMisskeyLocalizer>(new FormPrimitiveLocalizer());

    [Fact]
    public void InputPreservesTheUpstreamDomSlotsGeometryAndImmediateModelUpdate()
    {
        var inputInterop = new DisconnectedFormInputInterop();
        Services.AddSingleton<IFormInputInterop>(inputInterop);
        Services.AddScoped<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
        string value = string.Empty;

        IRenderedComponent<MkFormInput> component = Render<MkFormInput>(parameters => parameters
            .Add(input => input.Value, value)
            .Add(input => input.ValueChanged, next => value = next)
            .Add(input => input.Type, "text")
            .Add(input => input.Required, true)
            .Add(input => input.Autofocus, true)
            .Add(input => input.Spellcheck, false)
            .Add(input => input.Placeholder, "ユーザー名")
            .Add(input => input.CssClassAdditional, "_formBlock")
            .Add(input => input.Label, builder => builder.AddContent(0, "ユーザー名"))
            .Add(input => input.Prefix, builder => builder.AddContent(0, "@"))
            .Add(input => input.Suffix, builder => builder.AddContent(0, "@example.test"))
            .Add(input => input.Caption, builder => builder.AddContent(0, "利用できます")));

        Assert.NotNull(component.Find(".matxzzsk._formBlock > .label"));
        Assert.NotNull(component.Find(".matxzzsk > .input > .prefix"));
        Assert.NotNull(component.Find(".matxzzsk > .input > input[required]"));
        Assert.NotNull(component.Find(".matxzzsk > .input > .suffix"));
        Assert.Equal("--mk-form-input-height: 38px;", component.Find(".matxzzsk").GetAttribute("style"));
        component.WaitForAssertion(() => Assert.True(inputInterop.Autofocus));

        component.Find("input").Input("alice");

        Assert.Equal("alice", value);
        Assert.Equal("alice", component.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void SwitchPreservesTheHiddenInputButtonKnobAndLabelState()
    {
        bool value = false;
        IRenderedComponent<MkFormSwitch> component = Render<MkFormSwitch>(parameters => parameters
            .Add(item => item.Value, value)
            .Add(item => item.ValueChanged, next => value = next)
            .Add(item => item.CssClassAdditional, "_formBlock")
            .Add(item => item.Label, builder => builder.AddContent(0, "利用規約"))
            .AddChildContent("に同意する"));

        Assert.NotNull(component.Find(".ziffeomt._formBlock > input[type=checkbox]"));
        Assert.Null(component.Find(".ziffeomt > input").GetAttribute("checked"));
        Assert.Null(component.Find(".ziffeomt > input").GetAttribute("aria-checked"));
        Assert.NotNull(component.Find(".ziffeomt > span.button > div.knob"));
        Assert.Equal("利用規約に同意する", component.Find(".ziffeomt > span.label > span").TextContent);
        Assert.Equal("オフになっています", component.Find(".ziffeomt > span.button").GetAttribute("title"));

        component.Find("span.button").Click();

        Assert.True(value);
        component.Render(parameters => parameters
            .Add(item => item.Value, value)
            .Add(item => item.ValueChanged, next => value = next)
            .Add(item => item.CssClassAdditional, "_formBlock")
            .AddChildContent("利用規約に同意する"));
        Assert.Contains("checked", component.Find(".ziffeomt").ClassList);
    }

    [Fact]
    public void NumberInputPublishesANumberAndPreservesListAndFallthroughContracts()
    {
        var inputInterop = new DisconnectedFormInputInterop();
        Services.AddSingleton<IFormInputInterop>(inputInterop);
        Services.AddScoped<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
        double value = 1.5;

        IRenderedComponent<MkFormInput> component = Render<MkFormInput>(parameters => parameters
            .Add(input => input.Type, "number")
            .Add(input => input.NumberValue, value)
            .Add(input => input.NumberValueChanged, next => value = next)
            .Add(input => input.Step, "0.25")
            .Add(input => input.DataList, ["1.5", "2.75"])
            .Add(input => input.ManualSave, true)
            .AddUnmatched("class", "fixture-number")
            .AddUnmatched("style", "margin-top: 3px;"));

        AngleSharp.Dom.IElement root = component.Find(".matxzzsk.fixture-number");
        Assert.Contains("--mk-form-input-height: 38px;", root.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("margin-top: 3px;", root.GetAttribute("style"), StringComparison.Ordinal);
        AngleSharp.Dom.IElement input = component.Find("input[type=number][step='0.25']");
        string listId = Assert.IsType<string>(input.GetAttribute("list"));
        Assert.Equal(2, component.FindAll($"datalist#{listId} > option").Count);

        input.Input("2.75");
        Assert.Equal(1.5, value);
        Assert.Equal("保存", component.Find(".matxzzsk > .save").TextContent.Trim());
        component.Find(".matxzzsk > .save").Click();

        Assert.Equal(2.75, value);
    }

    [Fact]
    public async Task SelectPreservesGroupedOptionsPopupWidthAndImmediateModelUpdate()
    {
        var inputInterop = new DisconnectedFormInputInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IFormInputInterop>(inputInterop);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddScoped<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
        string value = "home";
        IReadOnlyList<MkFormSelectItem> items =
        [
            MkFormSelectItem.Group("公開", [
                MkFormSelectItem.Option("public", "パブリック"),
                MkFormSelectItem.Option("home", "ホーム")
            ]),
            MkFormSelectItem.Option("followers", "フォロワー", disabled: true)
        ];

        IRenderedComponent<MkFormSelect> component = Render<MkFormSelect>(parameters => parameters
            .Add(select => select.Value, value)
            .Add(select => select.ValueChanged, next => value = next)
            .Add(select => select.Items, items)
            .Add(select => select.Required, true)
            .Add(select => select.Autofocus, true)
            .Add(select => select.Large, true)
            .Add(select => select.Label, builder => builder.AddContent(0, "公開範囲"))
            .Add(select => select.Prefix, builder => builder.AddMarkupContent(0, "<i class='fas fa-eye'></i>"))
            .Add(select => select.Caption, builder => builder.AddContent(0, "投稿の受信者"))
            .AddUnmatched("class", "fixture-select")
            .AddUnmatched("data-select-id", "visibility"));

        AngleSharp.Dom.IElement root = component.Find(".vblkjoeq.fixture-select[data-select-id=visibility]");
        Assert.Equal("--mk-form-select-height: 40px;", root.GetAttribute("style"));
        Assert.Equal("公開範囲", root.QuerySelector(":scope > .label")?.TextContent);
        Assert.Equal("投稿の受信者", root.QuerySelector(":scope > .caption")?.TextContent);
        Assert.NotNull(root.QuerySelector(":scope > .input > .prefix > .fa-eye"));
        Assert.NotNull(root.QuerySelector(":scope > .input > .suffix > .fa-chevron-down"));
        Assert.NotNull(root.QuerySelector(":scope > .input > select.select[required]"));
        Assert.Equal(3, component.FindAll("select > optgroup > option, select > option").Count);
        Assert.NotNull(component.Find("option[value=home]").GetAttribute("selected"));
        component.WaitForAssertion(() => Assert.True(inputInterop.Autofocus));

        component.Find(".vblkjoeq > .input").Click();

        MisskeyOverlayEntry popup = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PopupMenu, popup.Kind);
        Assert.True(popup.MatchSourceWidth);
        Assert.Equal(MisskeyMenuItemKind.Label, popup.MenuItems[0].Kind);
        Assert.True(popup.MenuItems[2].Active);
        Assert.True(popup.MenuItems[3].Disabled);

        await popup.MenuItems[1].Action!();

        Assert.Equal("public", value);
        Assert.NotNull(component.Find("option[value=public]").GetAttribute("selected"));
    }

    [Fact]
    public void SelectManualSaveDefersTheModelUpdateUntilThePinnedSaveAction()
    {
        Services.AddSingleton<IFormInputInterop>(new DisconnectedFormInputInterop());
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddScoped<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
        string value = "public";

        IRenderedComponent<MkFormSelect> component = Render<MkFormSelect>(parameters => parameters
            .Add(select => select.Value, value)
            .Add(select => select.ValueChanged, next => value = next)
            .Add(select => select.ManualSave, true)
            .Add(select => select.Items,
            [
                MkFormSelectItem.Option("public", "パブリック"),
                MkFormSelectItem.Option("followers", "フォロワー")
            ]));

        component.Find("select").Input("followers");

        Assert.Equal("public", value);
        Assert.Equal("保存", component.Find(".vblkjoeq > .save").TextContent.Trim());
        component.Find(".vblkjoeq > .save").Click();
        Assert.Equal("followers", value);
        Assert.Empty(component.FindAll(".vblkjoeq > .save"));
    }

    [Fact]
    public void FormLinkPreservesSlotsFallthroughAndPinnedNavigationBranches()
    {
        IRenderedComponent<FormLink> component = Render<FormLink>(parameters => parameters
            .Add(link => link.To, "/settings/profile")
            .Add(link => link.Active, true)
            .Add(link => link.Inline, true)
            .Add(link => link.Behavior, "browser")
            .Add(link => link.Icon, builder => builder.AddMarkupContent(0, "<i class='fas fa-user'></i>"))
            .Add(link => link.Suffix, builder => builder.AddContent(0, "Account"))
            .AddUnmatched("class", "fixture-link")
            .AddUnmatched("data-link", "profile")
            .AddChildContent("プロフィール"));

        AngleSharp.Dom.IElement root = component.Find(".ffcbddfc.inline.fixture-link[data-link=profile]");
        AngleSharp.Dom.IElement anchor = Assert.IsAssignableFrom<AngleSharp.Dom.IElement>(
            root.QuerySelector(":scope > a.main._button.active"));
        Assert.Equal("settings/profile", anchor.GetAttribute("href"));
        Assert.Equal("false", anchor.GetAttribute("data-enhance-nav"));
        Assert.Equal("browser", anchor.GetAttribute("data-misskey-behavior"));
        Assert.Equal("プロフィール", anchor.QuerySelector(":scope > .text")?.TextContent);
        Assert.Equal("Account", anchor.QuerySelector(":scope > .right > .text")?.TextContent);
        Assert.NotNull(anchor.QuerySelector(":scope > .right > .fa-chevron-right"));
    }

    [Theory]
    [InlineData(false, "fas fa-info-circle")]
    [InlineData(true, "fas fa-exclamation-triangle")]
    public void InfoPreservesTheUpstreamSemanticBranch(bool warn, string expectedIcon)
    {
        IRenderedComponent<MkInfo> component = Render<MkInfo>(parameters => parameters
            .Add(info => info.Warn, warn)
            .AddUnmatched("class", "fixture-info")
            .AddUnmatched("data-info-id", "diagnostic")
            .AddChildContent("診断メッセージ"));

        Assert.Equal("診断メッセージ", component.Find(".fpezltsf").TextContent.Trim());
        Assert.Equal(expectedIcon, component.Find(".fpezltsf > i").GetAttribute("class"));
        Assert.Equal("true", component.Find(".fpezltsf > i").GetAttribute("aria-hidden"));
        Assert.Contains("fixture-info", component.Find(".fpezltsf").ClassList);
        Assert.Equal("diagnostic", component.Find(".fpezltsf").GetAttribute("data-info-id"));
        Assert.Equal(warn, component.Find(".fpezltsf").ClassList.Contains("warn"));
    }

    [Fact]
    public void LoadingPreservesThePinnedSvgHierarchyDefaultsAndAccessibleStatus()
    {
        IRenderedComponent<MkLoading> component = Render<MkLoading>(parameters => parameters
            .AddUnmatched("class", "fixture-loading")
            .AddUnmatched("data-loading-id", "primary"));

        AngleSharp.Dom.IElement root = component.Find("._root_13vug_9");
        Assert.Contains("_colored_13vug_15", root.ClassList);
        Assert.DoesNotContain("_inline_13vug_18", root.ClassList);
        Assert.DoesNotContain("_mini_13vug_23", root.ClassList);
        Assert.Contains("fixture-loading", root.ClassList);
        Assert.Equal("status", root.GetAttribute("role"));
        Assert.Equal("読み込み中", root.GetAttribute("aria-label"));
        Assert.Equal("true", root.GetAttribute("aria-busy"));
        Assert.Equal("primary", root.GetAttribute("data-loading-id"));

        Assert.NotNull(root.QuerySelector(":scope > ._container_13vug_28"));
        Assert.NotNull(root.QuerySelector(
            ":scope > ._container_13vug_28 > svg._spinner_13vug_35._bg_13vug_48 > g > circle"));
        AngleSharp.Dom.IElement foreground = Assert.IsAssignableFrom<AngleSharp.Dom.IElement>(root.QuerySelector(
            ":scope > ._container_13vug_28 > svg._spinner_13vug_35._fg_13vug_52 > g > path"));
        Assert.Equal(
            "M128,64C128,28.654 99.346,0 64,0C99.346,0 128,28.654 128,64Z",
            foreground.GetAttribute("d"));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void LoadingProjectsEveryPinnedVariant(bool inline, bool colored, bool mini)
    {
        IRenderedComponent<MkLoading> component = Render<MkLoading>(parameters => parameters
            .Add(loading => loading.Inline, inline)
            .Add(loading => loading.Colored, colored)
            .Add(loading => loading.Mini, mini)
            .Add(loading => loading.AccessibleLabel, "Loading"));

        AngleSharp.Dom.IElement root = component.Find("._root_13vug_9");
        Assert.Equal(inline, root.ClassList.Contains("_inline_13vug_18"));
        Assert.Equal(colored, root.ClassList.Contains("_colored_13vug_15"));
        Assert.Equal(mini, root.ClassList.Contains("_mini_13vug_23"));
        Assert.Equal("Loading", root.GetAttribute("aria-label"));
    }

    [Fact]
    public void EllipsisPreservesThePinnedThreeSpanHierarchyWithoutDuplicateAnnouncement()
    {
        IRenderedComponent<MkEllipsis> component = Render<MkEllipsis>(parameters => parameters
            .AddUnmatched("class", "fixture-ellipsis")
            .AddUnmatched("data-ellipsis-id", "waiting"));

        AngleSharp.Dom.IElement root = component.Find("span.mk-ellipsis.fixture-ellipsis");
        Assert.Equal("true", root.GetAttribute("aria-hidden"));
        Assert.Equal("waiting", root.GetAttribute("data-ellipsis-id"));
        AngleSharp.Dom.IHtmlCollection<AngleSharp.Dom.IElement> dots = root.QuerySelectorAll(":scope > span");
        Assert.Equal(3, dots.Length);
        Assert.All(dots, dot => Assert.Equal(".", dot.TextContent));
    }

    [Fact]
    public void FormSectionPreservesThePinnedLabelAndFormRootSlots()
    {
        IRenderedComponent<FormSection> component = Render<FormSection>(parameters => parameters
            .Add(section => section.Label, builder => builder.AddContent(0, "外部リンク"))
            .AddUnmatched("class", "fixture-section")
            .AddUnmatched("data-section-id", "links")
            .AddChildContent(builder => builder.AddMarkupContent(0, "<a href=\"/about\">Misskey</a>")));

        AngleSharp.Dom.IElement root = component.Find(".vrtktovh._formBlock.fixture-section");
        Assert.Equal("links", root.GetAttribute("data-section-id"));
        Assert.Equal("外部リンク", root.QuerySelector(":scope > .label")?.TextContent);
        Assert.Equal("Misskey", root.QuerySelector(":scope > .main._formRoot > a")?.TextContent);
    }

    [Fact]
    public void ModalWindowPreservesThePinnedDialogHierarchyDimensionsAndCloseContract()
    {
        Services.AddScoped<IDialogWindowInterop, DisconnectedDialogWindowInterop>();
        int closeRequests = 0;
        IRenderedComponent<MkModalWindow> component = Render<MkModalWindow>(parameters => parameters
            .Add(dialog => dialog.Width, 370)
            .Add(dialog => dialog.Height, 400)
            .Add(dialog => dialog.AccessibleLabel, "ログイン")
            .Add(dialog => dialog.CloseRequested, () => closeRequests++)
            .Add(dialog => dialog.Header, builder => builder.AddContent(0, "ログイン"))
            .AddChildContent("フォーム"));

        Assert.NotNull(component.Find(".qzhlnise.dialog.modal-enter-active.modal-enter-from > .bg._modalBg"));
        Assert.NotNull(component.Find(".qzhlnise > .content > .ebkgoccj._narrow_ > .header > .title"));
        Assert.Equal(
            "width: 370px; height: 400px;",
            component.Find(".ebkgoccj").GetAttribute("style"));
        Assert.Equal("フォーム", component.Find(".ebkgoccj > .body").TextContent);

        component.Find("button[aria-label=\"閉じる\"]").Click();

        Assert.Equal(1, closeRequests);
    }

    private sealed class DisconnectedFormInputInterop : IFormInputInterop
    {
        public bool Autofocus { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken)
        {
            Autofocus = autofocus;
            throw new JSDisconnectedException("bUnit has no browser observer.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisconnectedButtonRippleInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit has no browser observer.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedDialogWindowInterop : IDialogWindowInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class =>
            throw new JSDisconnectedException("bUnit has no browser transition runtime.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FormPrimitiveLocalizer : IMisskeyLocalizer
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
            "save" => "保存",
            "itsOn" => "オンになっています",
            "itsOff" => "オフになっています",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
