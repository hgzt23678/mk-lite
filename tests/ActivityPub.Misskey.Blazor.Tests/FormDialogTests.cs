using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FormDialogTests : BunitContext
{
    private readonly NoOpBrowser browser = new();

    public FormDialogTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new FormDialogLocalizer());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<IDialogWindowInterop>(browser);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IFormRangeInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<ISpacerInterop>(browser);
        Services.AddSingleton<IPizzaxDeviceState>(new EmptyDeviceState());
    }

    [Fact]
    public void PreservesThePinnedWindowFormHierarchyAndEveryVisibleFieldKind()
    {
        IReadOnlyList<MisskeyFormDialogItem> form = CreateCompleteForm();

        IRenderedComponent<MkFormDialog> component = Render<MkFormDialog>(parameters => parameters
            .Add(dialog => dialog.Title, "Example form")
            .Add(dialog => dialog.Form, form));

        Assert.Equal(
            "width: 450px; height: auto;",
            component.Find(".qzhlnise > .content > .ebkgoccj").GetAttribute("style"));
        Assert.Equal("Example form", component.Find(".ebkgoccj > .header > .title").TextContent);
        Assert.NotNull(component.Find(".ebkgoccj > .body .xkpnjxcv._formRoot"));
        Assert.Equal(8, component.FindAll(".xkpnjxcv > ._formBlock").Count);
        Assert.DoesNotContain("internal", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Note (任意)", component.Find(".matxzzsk > .label").TextContent, StringComparison.Ordinal);
        Assert.Equal("0.5", component.Find("input[type=number]").GetAttribute("step"));
        Assert.Equal(2, component.FindAll(".vblkjoeq select > option").Count);
        Assert.Equal(2, component.FindAll(".novjtcto > .body > .novjtctn").Count);
        Assert.Single(component.FindAll(".timctyfi"));
    }

    [Fact]
    public async Task EmitsTypedCurrentValuesOnceAndRunsButtonActionsAgainstTheSameValueBag()
    {
        int actionCalls = 0;
        IDictionary<string, object?>? actionValues = null;
        IReadOnlyList<MisskeyFormDialogItem> form =
        [
            new("title", "string") { DefaultValue = "before" },
            new("enabled", "boolean") { DefaultValue = false },
            new("sound", "enum")
            {
                DefaultValue = null,
                Options = [new("None", null), new("Bell", "bell")]
            },
            new("layout", "radio")
            {
                DefaultValue = 1,
                Options = [new("One", 1), new("Two", 2)]
            },
            new("listen", "button")
            {
                Action = context =>
                {
                    actionCalls++;
                    actionValues = context.Values;
                    return Task.CompletedTask;
                }
            }
        ];
        var results = new List<MisskeyFormDialogResult>();
        IRenderedComponent<MkFormDialog> component = Render<MkFormDialog>(parameters => parameters
            .Add(dialog => dialog.Title, "Configure")
            .Add(dialog => dialog.Form, form)
            .Add(dialog => dialog.Done, result => results.Add(result)));

        component.Find("input[type=text]").Input("after");
        component.Find(".ziffeomt > .button").Click();
        component.Find(".vblkjoeq select").Input("mk-form-option-1");
        component.FindAll(".novjtctn")[1].Click();
        component.Find(".xkpnjxcv > .bghgjjyj").Click();
        component.Find(".ebkgoccj > .header > button:last-child").Click();

        Assert.Equal(1, actionCalls);
        Assert.NotNull(actionValues);
        Assert.Equal("after", actionValues!["title"]);
        Assert.True((bool)actionValues["enabled"]!);
        Assert.Single(results);
        Assert.False(results[0].Canceled);
        Assert.Equal("after", results[0].Result!["title"]);
        Assert.Equal("bell", results[0].Result!["sound"]);
        Assert.Equal(2, results[0].Result!["layout"]);

        component.Find(".ebkgoccj > .header > button:first-child").Click();
        await Task.Yield();
        Assert.Single(results);
    }

    [Fact]
    public void OverlayServiceCreatesARealFormDialogEntryAndBackgroundCancelReturnsCanceled()
    {
        IMisskeyOverlayService overlays = Services.GetRequiredService<IMisskeyOverlayService>();
        MisskeyFormDialogResult? result = null;
        IReadOnlyList<MisskeyFormDialogItem> form =
        [
            new("name", "string") { DefaultValue = "Alice" }
        ];

        Guid id = overlays.ShowFormDialog(new("Profile", form, value =>
        {
            result = value;
            return Task.CompletedTask;
        }));
        MisskeyOverlayEntry entry = Assert.Single(overlays.Entries);
        Assert.Equal(id, entry.Id);
        Assert.Equal(MisskeyOverlayKind.FormDialog, entry.Kind);
        Assert.Same(form, entry.FormDialog!.Form);

        IRenderedComponent<MkFormDialog> component = Render<MkFormDialog>(parameters => parameters
            .Add(dialog => dialog.Title, entry.FormDialog.Title)
            .Add(dialog => dialog.Form, entry.FormDialog.Form)
            .Add(dialog => dialog.OverlayId, id)
            .Add(dialog => dialog.Done, entry.FormDialog.Done!));
        component.Find(".qzhlnise > .bg._modalBg").Click();

        Assert.NotNull(result);
        Assert.True(result.Canceled);
        Assert.Null(result.Result);
    }

    private static IReadOnlyList<MisskeyFormDialogItem> CreateCompleteForm() =>
    [
        new("note", "string")
        {
            Label = "Note",
            Description = "Single line",
            Required = false,
            DefaultValue = "hello"
        },
        new("count", "number") { DefaultValue = 3.5, Step = 0.5 },
        new("details", "string") { DefaultValue = "long", Multiline = true },
        new("enabled", "boolean") { DefaultValue = true },
        new("sound", "enum")
        {
            DefaultValue = "bell",
            Options = [new("None", null), new("Bell", "bell")]
        },
        new("layout", "radio")
        {
            DefaultValue = "one",
            Options = [new("One", "one"), new("Two", "two")]
        },
        new("volume", "range") { DefaultValue = 0.5, Min = 0, Max = 1, Step = 0.1 },
        new("listen", "button") { Content = "Listen", Action = _ => Task.CompletedTask },
        new("internal", "object") { Hidden = true, DefaultValue = new Dictionary<string, object?>() }
    ];

    private sealed class EmptyDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class NoOpBrowser :
        IDialogWindowInterop,
        IFormInputInterop,
        IFormRangeInterop,
        IButtonRippleInterop,
        ISpacerInterop
    {
        private readonly NoOpJsObject handle = new();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference container,
            ElementReference thumb,
            ElementReference highlight,
            double normalizedValue,
            DotNetObjectReference<MkFormRange> receiver,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpJsObject : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FormDialogLocalizer : IMisskeyLocalizer
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
            "optional" => "任意",
            "save" => "保存",
            "itsOn" => "オンになっています",
            "itsOff" => "オフになっています",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
