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
using DomainVisibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class VisibilityPickerTests : BunitContext
{
    [Fact]
    public async Task PreservesPinnedOptionsLocalizationLocalOnlyAndCloseContract()
    {
        var browser = new RecordingVisibilityPickerInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IVisibilityPickerInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new VisibilityLocalizer());
        var changes = new List<(DomainVisibility Visibility, bool LocalOnly)>();
        int closedCalls = 0;
        Guid id = overlays.ShowVisibilityPicker(
            default,
            DomainVisibility.MentionedOnly,
            currentLocalOnly: false,
            (visibility, localOnly) =>
            {
                changes.Add((visibility, localOnly));
                return Task.CompletedTask;
            });

        IRenderedComponent<MkVisibilityPicker> component = Render<MkVisibilityPicker>(parameters => parameters
            .Add(picker => picker.Id, id)
            .Add(picker => picker.Source, default(ElementReference))
            .Add(picker => picker.CurrentVisibility, DomainVisibility.MentionedOnly)
            .Add(picker => picker.CurrentLocalOnly, false)
            .Add(picker => picker.Changed, (visibility, localOnly) =>
            {
                changes.Add((visibility, localOnly));
                return Task.CompletedTask;
            })
            .Add(picker => picker.Closed, () => closedCalls++));

        component.WaitForAssertion(() => Assert.Equal(1, browser.AttachCalls));
        Assert.Equal("qzhlnise popup modal-popup-enter-active", component.Find(".qzhlnise").ClassName);
        Assert.Equal("bg _modalBg", component.Find(".qzhlnise > .bg").ClassName);
        IElement menu = component.Find(".qzhlnise > .content > .gqyayizv._popup[role=menu]");
        Assert.Equal("Visibility", menu.GetAttribute("aria-label"));
        Assert.Equal(5, menu.QuerySelectorAll(":scope > button").Length);
        Assert.Single(menu.QuerySelectorAll(":scope > .divider"));
        Assert.Equal(
            ["Public", "Home", "Followers", "Direct", "Local only"],
            menu.QuerySelectorAll(":scope > button > div:nth-child(2) > span:first-child")
                .Select(element => element.TextContent));
        Assert.Equal(
            ["Visible to everyone", "Home timeline only", "Followers only", "Specified users only", "Local instance only"],
            menu.QuerySelectorAll(":scope > button > div:nth-child(2) > span:last-child")
                .Select(element => element.TextContent));
        Assert.NotNull(menu.QuerySelector("button[data-index='1'] .fa-globe"));
        Assert.NotNull(menu.QuerySelector("button[data-index='2'] .fa-home"));
        Assert.NotNull(menu.QuerySelector("button[data-index='3'] .fa-unlock"));
        Assert.NotNull(menu.QuerySelector("button[data-index='4'] .fa-envelope"));
        Assert.NotNull(menu.QuerySelector("button[data-index='5'] .fa-biohazard"));

        IElement specified = component.Find("button[data-index='4']");
        Assert.Contains("active", specified.ClassList);
        Assert.False(specified.HasAttribute("disabled"));
        Assert.Equal("true", specified.GetAttribute("aria-checked"));

        component.Find("button[data-index='5']").Click();
        Assert.Equal([(DomainVisibility.MentionedOnly, true)], changes);
        specified = component.Find("button[data-index='4']");
        Assert.Contains("active", specified.ClassList);
        Assert.True(specified.HasAttribute("disabled"));
        IElement localOnly = component.Find("button[data-index='5']");
        Assert.Contains("active", localOnly.ClassList);
        Assert.Equal("true", localOnly.GetAttribute("aria-checked"));
        Assert.NotNull(localOnly.QuerySelector(":scope > div:nth-child(3) > .fa-toggle-on"));
        Assert.Equal(0, browser.Handle.CloseCalls);

        component.Find("button[data-index='3']").Click();
        Assert.Equal(
            [(DomainVisibility.MentionedOnly, true), (DomainVisibility.FollowersOnly, true)],
            changes);
        Assert.Contains("active", component.Find("button[data-index='3']").ClassList);
        Assert.Equal(1, browser.Handle.CloseCalls);

        await component.InvokeAsync(component.Instance.NotifyClosed);
        await component.InvokeAsync(component.Instance.NotifyClosed);
        Assert.Equal(1, closedCalls);
        Assert.Empty(overlays.VisibilityPickers);

        await component.Instance.DisposeAsync();
        Assert.Equal(1, browser.Handle.DisposeCalls);
    }

    [Fact]
    public void ContentAndEscapeCloseOnlyOnceThroughTheRegisteredOverlayLifecycle()
    {
        var browser = new RecordingVisibilityPickerInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IVisibilityPickerInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new VisibilityLocalizer());
        Guid id = overlays.ShowVisibilityPicker(
            default,
            DomainVisibility.Public,
            currentLocalOnly: false,
            (_, _) => Task.CompletedTask);
        IRenderedComponent<MkVisibilityPicker> component = Render<MkVisibilityPicker>(parameters => parameters
            .Add(picker => picker.Id, id)
            .Add(picker => picker.Source, default(ElementReference))
            .Add(picker => picker.CurrentVisibility, DomainVisibility.Public)
            .Add(picker => picker.Changed, (_, _) => Task.CompletedTask));

        component.WaitForAssertion(() => Assert.Equal(1, browser.AttachCalls));
        component.Find(".content").Click();
        component.Find(".qzhlnise").KeyDown("Escape");

        Assert.Equal(1, browser.Handle.CloseCalls);
    }

    private sealed class RecordingVisibilityPickerInterop : IVisibilityPickerInterop
    {
        public int AttachCalls { get; private set; }

        public RecordingHandle Handle { get; } = new();

        public ValueTask<ModalAttachment> AttachAsync(
            ElementReference source,
            ElementReference modal,
            ElementReference content,
            DotNetObjectReference<MkVisibilityPicker> receiver,
            CancellationToken cancellationToken)
        {
            _ = source;
            _ = modal;
            _ = content;
            _ = receiver;
            cancellationToken.ThrowIfCancellationRequested();
            AttachCalls++;
            return ValueTask.FromResult(new ModalAttachment(
                Handle,
                IsDrawer: false,
                MaximumHeight: 480,
                TransformOrigin: "center top",
                SourceWidth: 34));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int CloseCalls { get; private set; }

        public int DisposeCalls { get; private set; }

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
            else if (string.Equals(identifier, "dispose", StringComparison.Ordinal))
            {
                DisposeCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class VisibilityLocalizer : IMisskeyLocalizer
    {
        private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        {
            ["visibility"] = "Visibility",
            ["_visibility.public"] = "Public",
            ["_visibility.publicDescription"] = "Visible to everyone",
            ["_visibility.home"] = "Home",
            ["_visibility.homeDescription"] = "Home timeline only",
            ["_visibility.followers"] = "Followers",
            ["_visibility.followersDescription"] = "Followers only",
            ["_visibility.specified"] = "Direct",
            ["_visibility.specifiedDescription"] = "Specified users only",
            ["_visibility.localOnly"] = "Local only",
            ["_visibility.localOnlyDescription"] = "Local instance only"
        };

        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "en-US";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _ = arguments;
            return Values.TryGetValue(key, out string? value) ? value : key;
        }

        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
