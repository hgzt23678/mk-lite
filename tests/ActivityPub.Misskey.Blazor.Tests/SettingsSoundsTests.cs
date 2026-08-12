using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class SettingsSoundsTests : BunitContext
{
    private readonly DeviceState state = new();

    public SettingsSoundsTests()
    {
        Services.AddSingleton<IPizzaxDeviceState>(state);
        Services.AddSingleton<IMisskeyLocalizer>(new Localizer());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<IFormRangeInterop, DisconnectedRangeInterop>();
        Services.AddSingleton<IButtonRippleInterop, DisconnectedButtonInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LoadsSevenSoundRowsAndPersistsResetContract()
    {
        using IRenderedComponent<SettingsSounds> component = Render<SettingsSounds>();

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find("[data-setting='sound_masterVolume']"));
            Assert.Equal(7, component.FindAll("[data-sound]").Count);
            Assert.NotNull(component.Find(".settings-sounds"));
        });

        component.Find("button").Click();
        Assert.Contains("sound_masterVolume", state.Writes);
        Assert.Contains("sound_note", state.Writes);
    }

    private sealed class DeviceState : IPizzaxDeviceState
    {
        public List<string> Writes { get; } = [];

        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default)
        {
            Writes.Add(propertyName);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Localizer : IMisskeyLocalizer
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
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;
        public bool TrySelectLocale(string? locale) => true;
    }

    private sealed class DisconnectedRangeInterop : IFormRangeInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference container,
            ElementReference thumb,
            ElementReference highlight,
            double initialValue,
            DotNetObjectReference<MkFormRange> receiver,
            CancellationToken cancellationToken) => throw new JSDisconnectedException("bUnit");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DisconnectedButtonInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
