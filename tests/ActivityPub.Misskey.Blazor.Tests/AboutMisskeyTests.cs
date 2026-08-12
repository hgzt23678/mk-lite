using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class AboutMisskeyTests : BunitContext
{
    [Fact]
    public void RendersThePinnedAboutPageHierarchyAndPreparesEveryPhysicsEmoji()
    {
        var physics = new RecordingPhysicsInterop();
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            new Uri("https://source.example.test/activitypub-server", UriKind.Absolute)));
        Services.AddSingleton<IAboutMisskeyPhysicsInterop>(physics);
        Services.AddScoped<IStickyContainerInterop, DisconnectedStickyInterop>();
        Services.AddScoped<IPageHeaderInterop, DisconnectedPageHeaderInterop>();
        Services.AddScoped<ISpacerInterop, DisconnectedSpacerInterop>();
        Services.AddSingleton<IPizzaxDeviceState, DefaultDeviceState>();
        Services.AddScoped<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
        Services.AddScoped<IMfmParserInterop, DisconnectedMfmParserInterop>();
        Services.AddSingleton<IMisskeyLocalizer>(new AboutLocalizer());
        Services.AddScoped<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<ICurrentAccountPresentationService>(new UnusedCurrentAccount());
        JSInterop.Mode = JSRuntimeMode.Loose;

        IRenderedComponent<AboutMisskey> component = Render<AboutMisskey>();

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".znqjceqz > .about._formBlock"));
            Assert.Equal("/client-assets/about-icon.png", component.Find(".about > img.icon").GetAttribute("src"));
            Assert.Equal(32, component.FindAll(".about > span.emoji._physics_circle_").Count);
            Assert.Equal("v12.119.2-port.1", component.Find(".about > .version").TextContent);
            Assert.Equal(4, component.FindAll(".znqjceqz > .vrtktovh:nth-of-type(5) .ffcbddfc").Count);
            Assert.Equal(9, component.FindAll(".vrtktovh:nth-of-type(6) .ffcbddfc").Count);
            Assert.Equal(1, physics.PrepareCalls);
            Assert.DoesNotContain("iframe", component.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed class RecordingPhysicsInterop : IAboutMisskeyPhysicsInterop, IDisposable
    {
        public int PrepareCalls { get; private set; }

        public ValueTask<bool> PrepareAsync(ElementReference container, CancellationToken cancellationToken)
        {
            PrepareCalls++;
            return ValueTask.FromResult(true);
        }

        public ValueTask<IJSObjectReference> StartAsync(
            ElementReference container,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
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
            where T : class => throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedPageHeaderInterop : IPageHeaderInterop, IDisposable
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

    private sealed class DisconnectedSpacerInterop : ISpacerInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DefaultDeviceState : IPizzaxDeviceState
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

    private sealed class DisconnectedButtonRippleInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) => throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedMfmParserInterop : IMfmParserInterop, IDisposable
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) => throw new JSDisconnectedException("No browser in the component test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class UnusedCurrentAccount : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The public about page must not load a private account projection.");
    }

    private sealed class AboutLocalizer : IMisskeyLocalizer
    {
        private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        {
            ["aboutMisskey"] = "Misskeyについて",
            ["_aboutMisskey.about"] = "Misskeyはsyuiloによって2014年から開発されている、オープンソースのソフトウェアです。",
            ["learnMore"] = "詳しく",
            ["_aboutMisskey.source"] = "ソースコード",
            ["_aboutMisskey.translation"] = "Misskeyを翻訳",
            ["_aboutMisskey.donate"] = "Misskeyに寄付",
            ["_aboutMisskey.contributors"] = "主なコントリビューター",
            ["_aboutMisskey.patrons"] = "支援者"
        };

        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
            Values.TryGetValue(key, out string? value) ? value : key;

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
