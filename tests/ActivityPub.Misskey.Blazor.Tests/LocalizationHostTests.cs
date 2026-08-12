using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class LocalizationHostTests : BunitContext
{
    [Fact]
    public async Task HydrationSelectionRerendersDescendantsAndDisposesBrowserBoundary()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "ja-JP";
        var catalog = new MisskeyLocaleCatalog();
        var localizer = new MisskeyLocalizer(
            catalog,
            new MisskeyLocaleRequestResolver(catalog),
            new HttpContextAccessor { HttpContext = context });
        var interop = new RecordingLocaleInterop();
        Services.AddSingleton<IMisskeyLocalizer>(localizer);
        Services.AddSingleton<IMisskeyLocaleInterop>(interop);

        IRenderedComponent<MisskeyLocalizationHost> component = Render<MisskeyLocalizationHost>(parameters => parameters
            .Add(host => host.ChildContent, builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "data-localized", "show-more");
                builder.AddContent(2, localizer.Translate("showMore"));
                builder.CloseElement();
            }));

        component.WaitForAssertion(() => Assert.Single(interop.Attachments));
        Assert.Equal("もっと見る", component.Find("[data-localized='show-more']").TextContent);
        LocaleAttachment attachment = Assert.Single(interop.Attachments);
        Assert.Equal(25, attachment.SupportedLocales.Count);
        Assert.Equal("ja-JP", attachment.CurrentLocale);

        await component.InvokeAsync(() => component.Instance.SelectStoredLocaleAsync("en-US"));
        component.WaitForAssertion(() =>
        {
            Assert.Equal("Show more", component.Find("[data-localized='show-more']").TextContent);
            Assert.Equal(new LocaleApplication("en-US", "ltr"), Assert.Single(attachment.Handle.Applications));
        });

        await component.Instance.DisposeAsync();
        Assert.Equal(1, attachment.Handle.DisposeCalls);
        Assert.True(attachment.Handle.ReferenceDisposed);
    }

    private sealed class RecordingLocaleInterop : IMisskeyLocaleInterop
    {
        public List<LocaleAttachment> Attachments { get; } = [];

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            IReadOnlyList<MisskeyLocaleDefinition> supportedLocales,
            string currentLocale,
            string direction,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken = default)
            where T : class
        {
            _ = receiver;
            cancellationToken.ThrowIfCancellationRequested();
            var handle = new RecordingLocaleHandle();
            Attachments.Add(new LocaleAttachment(supportedLocales, currentLocale, direction, handle));
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }
    }

    private sealed class RecordingLocaleHandle : IJSObjectReference
    {
        public List<LocaleApplication> Applications { get; } = [];

        public int DisposeCalls { get; private set; }

        public bool ReferenceDisposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "applyLocale")
            {
                object?[] values = args ?? [];
                Applications.Add(new LocaleApplication(
                    Assert.IsType<string>(values[0]),
                    Assert.IsType<string>(values[1])));
            }
            else if (identifier == "dispose")
            {
                DisposeCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            ReferenceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record LocaleAttachment(
        IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales,
        string CurrentLocale,
        string Direction,
        RecordingLocaleHandle Handle);

    private sealed record LocaleApplication(string Locale, string Direction);
}
