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

public sealed class MkSampleTests : BunitContext
{
    public MkSampleTests()
    {
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddSingleton<MisskeyFrontendRuntimeConfiguration>(new MisskeyFrontendRuntimeConfiguration("12.119.2-port.1", null, new Uri("https://local.example")));
        Services.AddSingleton<IMisskeyLocalizer>(new Localizer());
        Services.AddSingleton<IPizzaxDeviceState>((IPizzaxDeviceState)new DeviceState());
        Services.AddScoped<IMfmParserInterop, EmptyMfmParserInterop>();
        Services.AddScoped<IFormInputInterop, DisconnectedFormInputInterop>();
        Services.AddScoped<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
    }

    [Fact]
    public void PreservesSampleCardsAndUsesTypedOverlayActionsForUnsupportedDrive()
    {
        IRenderedComponent<MkSample> component = Render<MkSample>();
        Assert.Equal(3, component.FindAll("._content").Count);
        Assert.Equal(6, component.FindAll(".bghgjjyj.inline").Count);
        Assert.Contains("Open menu", component.Markup);

        component.FindAll("button").Single(button => button.TextContent.Contains("Open menu", StringComparison.Ordinal)).Click();
        MisskeyOverlayService overlays = (MisskeyOverlayService)Services.GetRequiredService<IMisskeyOverlayService>();
        Assert.Single(overlays.ContextMenus);
        Assert.Equal("Fruits", overlays.ContextMenus[0].Items[0].Text);

        component.FindAll("button").Single(button => button.TextContent.Contains("Open drive", StringComparison.Ordinal)).Click();
        Assert.Equal(MisskeyOverlayKind.Alert, Assert.Single(overlays.Entries).Kind);
        Assert.Contains("Drive unavailable", overlays.Entries[0].Alert!.Title);
    }

    private sealed class EmptyMfmParserInterop : IMfmParserInterop, IDisposable
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(string text, bool plain, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MfmNode>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);
        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class DisconnectedFormInputInterop : IFormInputInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference input, ElementReference prefix, ElementReference suffix, bool autofocus, CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit has no input runtime.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DisconnectedButtonRippleInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit has no ripple runtime.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class Localizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;
        public bool TrySelectLocale(string? locale) => false;
    }
}
