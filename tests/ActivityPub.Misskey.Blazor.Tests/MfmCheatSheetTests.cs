using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MfmCheatSheetTests : BunitContext
{
    public MfmCheatSheetTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new Localizer());
        Services.AddSingleton<IPizzaxDeviceState, DeviceState>();
        Services.AddSingleton<IStickyContainerInterop, DisconnectedStickyInterop>();
        Services.AddSingleton<ISpacerInterop, DisconnectedSpacerInterop>();
        ComponentFactories.AddStub<MkPageHeader>();
        ComponentFactories.AddStub<MfmView>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void PreservesV12FeatureOrderAndEditableMfmPreviews()
    {
        using IRenderedComponent<MfmCheatSheetPage> component = Render<MfmCheatSheetPage>();

        string[] keys = component.FindAll("[data-mfm-feature]")
            .Select(element => element.GetAttribute("data-mfm-feature")!)
            .ToArray();
        Assert.Equal(29, keys.Length);
        Assert.Equal("mention", keys[0]);
        Assert.Equal("plain", keys[^1]);
        Assert.Equal(29, component.FindComponents<MkFormTextarea>().Count);
        Assert.Equal(29, component.FindComponents<Bunit.TestDoubles.Stub<MfmView>>().Count);
        Assert.Contains("_mfm.intro", component.Markup, StringComparison.Ordinal);
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

    private sealed class DisconnectedStickyInterop : IStickyContainerInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference body,
            double parentTop,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => throw new JSDisconnectedException("bUnit");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DisconnectedSpacerInterop : ISpacerInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => throw new JSDisconnectedException("bUnit");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
