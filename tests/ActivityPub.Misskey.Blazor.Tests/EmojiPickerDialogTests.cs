using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class EmojiPickerDialogTests : BunitContext
{
    [Fact]
    public async Task ReactionPopupUsesPinnedModalPickerDomAndResetsAndFocusesOnOpening()
    {
        (RecordingModalInterop modalInterop, RecordingEmojiInterop emojiInterop, MisskeyOverlayService overlays) = Configure(
            reactionPickerUseDrawerForMobile: false,
            new("popup", true, 321.25, "center top", 48, 2_000_100));
        string? chosen = null;
        int close = 0;
        int closed = 0;
        ElementReference source = new("emoji-source");
        IReadOnlyList<EmojiPickerCustomEmoji> customEmojis = [new("party", "/media/party.webp", "fun", [])];
        Func<string, Task> choose = value =>
        {
            chosen = value;
            return Task.CompletedTask;
        };
        Guid id = overlays.ShowEmojiPicker(source, choose, asReactionPicker: true, customEmojis: customEmojis);
        IRenderedComponent<MkEmojiPickerDialog> component = Render<MkEmojiPickerDialog>(parameters => parameters
            .Add(dialog => dialog.Id, id)
            .Add(dialog => dialog.Source, source)
            .Add(dialog => dialog.AsReactionPicker, true)
            .Add(dialog => dialog.ShowPinned, false)
            .Add(dialog => dialog.CustomEmojis, customEmojis)
            .Add(dialog => dialog.Chosen, choose)
            .Add(dialog => dialog.Close, () => { close++; })
            .Add(dialog => dialog.Closed, () => { closed++; }));

        component.WaitForAssertion(() => Assert.Single(modalInterop.Attachments));
        MkModalInteropOptions options = modalInterop.Attachments[0];
        Assert.Equal("popup", options.PreferType);
        Assert.Equal("middle", options.Priority);
        Assert.True(options.TransparentBackground);
        Assert.NotNull(component.Find(".qzhlnise.popup > .bg._modalBg.transparent"));
        Assert.NotNull(component.Find(".qzhlnise.popup > .content > .omfetrab.s1.w1.h2.ryghynhb._popup._shadow:not(.drawer)"));
        Assert.Equal("max-height: 321.25px", component.Find(".omfetrab.ryghynhb").GetAttribute("style"));
        Assert.Single(component.FindAll(".omfetrab > .emojis > .group.index > section"));

        component.Find("input.search").Input("grinning");
        MkModal modal = component.FindComponent<MkModal>().Instance;
        await component.InvokeAsync(modal.NotifyOpening);
        component.WaitForAssertion(() => Assert.Equal(string.Empty, component.Find("input.search").GetAttribute("value")));
        Assert.Equal(2, emojiInterop.ResetCalls);
        Assert.True(emojiInterop.FocusCalls >= 2);

        component.Find("input.search").Input("party");
        component.Find(".omfetrab > .emojis > section.result > .body > button.item[title='party']").Click();
        component.WaitForAssertion(() => Assert.Contains("hide", modalInterop.Handle.Invocations));
        Assert.Equal(":party:", chosen);
        Assert.Equal(1, close);
        await component.InvokeAsync(modal.NotifyClosed);
        Assert.Equal(1, closed);
        Assert.DoesNotContain(overlays.EmojiPickers, entry => entry.Id == id);
    }

    [Fact]
    public async Task AutoDrawerProjectsDrawerClassHeightAndClosesFromEscapeAndBackground()
    {
        (RecordingModalInterop modalInterop, _, MisskeyOverlayService overlays) = Configure(
            reactionPickerUseDrawerForMobile: true,
            new("drawer", true, 400, "center", 48, 2_000_100));
        int close = 0;
        ElementReference source = new("emoji-source");
        Func<string, Task> choose = _ => Task.CompletedTask;
        Guid id = overlays.ShowEmojiPicker(source, choose, asReactionPicker: true);
        IRenderedComponent<MkEmojiPickerDialog> component = Render<MkEmojiPickerDialog>(parameters => parameters
            .Add(dialog => dialog.Id, id)
            .Add(dialog => dialog.Source, source)
            .Add(dialog => dialog.AsReactionPicker, true)
            .Add(dialog => dialog.Chosen, choose)
            .Add(dialog => dialog.Close, () => { close++; }));

        component.WaitForAssertion(() => Assert.Single(modalInterop.Attachments));
        Assert.Equal("auto", modalInterop.Attachments[0].PreferType);
        Assert.NotNull(component.Find(".qzhlnise.drawer > .bg._modalBg:not(.transparent)"));
        Assert.NotNull(component.Find(".qzhlnise.drawer > .content > .omfetrab.asDrawer.ryghynhb._popup._shadow.drawer"));
        Assert.Equal("max-height: 400px", component.Find(".omfetrab.ryghynhb").GetAttribute("style"));

        MkModal modal = component.FindComponent<MkModal>().Instance;
        await component.InvokeAsync(modal.NotifyEscape);
        component.WaitForAssertion(() => Assert.Contains("hide", modalInterop.Handle.Invocations));
        Assert.Equal(1, close);

        await component.InvokeAsync(modal.NotifyClicked);
        Assert.Equal(1, close);
    }

    private (RecordingModalInterop Modal, RecordingEmojiInterop Emoji, MisskeyOverlayService Overlays) Configure(
        bool reactionPickerUseDrawerForMobile,
        MkModalBrowserPlacement placement)
    {
        var modal = new RecordingModalInterop(placement);
        var emoji = new RecordingEmojiInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IMkModalInterop>(modal);
        Services.AddSingleton<IEmojiPickerInterop>(emoji);
        Services.AddSingleton<IRippleEffectInterop>(new NoOpRippleInterop());
        Services.AddSingleton<IEmojiCatalog>(new SmallEmojiCatalog());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(reactionPickerUseDrawerForMobile));
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        return (modal, emoji, overlays);
    }

    private sealed class SmallEmojiCatalog : IEmojiCatalog
    {
        public IReadOnlyList<UnicodeEmojiDefinition> Emojis { get; } =
        [
            new("face", "😀", "grinning", ["smile"]),
            new("face", "🎉", "tada", ["party"])
        ];

        public IReadOnlyList<string> Categories { get; } = ["face"];
    }

    private sealed class FixedDeviceState(bool reactionPickerUseDrawerForMobile) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = propertyName switch
            {
                "reactionPickerUseDrawerForMobile" => reactionPickerUseDrawerForMobile,
                "animation" => true,
                "disableDrawer" => false,
                "recentlyUsedEmojis" => Array.Empty<string>(),
                _ => fallback!
            };
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingEmojiInterop : IEmojiPickerInterop, IDisposable
    {
        public int FocusCalls { get; private set; }

        public int ResetCalls { get; private set; }

        public ValueTask FocusAsync(ElementReference search, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FocusCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(ElementReference emojis, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "search" => "Search",
            "recentUsed" => "Recently used",
            "customEmojis" => "Custom emojis",
            "emoji" => "Emoji",
            "other" => "Other",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class RecordingModalInterop(MkModalBrowserPlacement placement) : IMkModalInterop, IDisposable
    {
        public List<MkModalInteropOptions> Attachments { get; } = [];

        public RecordingHandle Handle { get; } = new();

        public ValueTask<MkModalAttachment> AttachAsync(
            ElementReference? source,
            ElementReference modal,
            ElementReference background,
            ElementReference content,
            DotNetObjectReference<MkModal> receiver,
            MkModalInteropOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attachments.Add(options);
            return ValueTask.FromResult(new MkModalAttachment(Handle, placement));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
