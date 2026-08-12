using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MkATests : BunitContext
{
    public MkATests()
    {
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<IMisskeyLocalizer>(new Localizer());
        Services.AddSingleton<IClipboardInterop, ClipboardStub>();
    }

    [Fact]
    public void MkAPreservesHrefAndActiveClass()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/app/explore");
        IRenderedComponent<MkA> rendered = Render<MkA>(parameters => parameters
            .Add(component => component.To, "/app/explore")
            .Add(component => component.ActiveClass, "active")
            .AddChildContent("Explore"));

        AngleSharp.Dom.IElement anchor = rendered.Find("a");
        Assert.Equal("/app/explore", anchor.GetAttribute("href"));
        Assert.Equal("active", anchor.GetAttribute("class"));
        Assert.Equal("Explore", anchor.TextContent);
    }

    private sealed class ClipboardStub : IClipboardInterop, IDisposable
    {
        public ValueTask<ClipboardWriteResult> WriteTextAsync(string value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ClipboardWriteResult(true, "test", null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
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
}
