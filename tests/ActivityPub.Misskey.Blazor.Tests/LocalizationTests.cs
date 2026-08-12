using System.Globalization;
using ActivityPub.Misskey.Blazor.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class LocalizationTests
{
    private static readonly MisskeyLocaleCatalog Catalog = new();

    [Fact]
    public void EmbeddedCatalogContainsEverySupportedLocaleAndCompleteEffectiveKeys()
    {
        string[] expected =
        [
            "ar-SA", "cs-CZ", "da-DK", "de-DE", "en-US", "es-ES", "fr-FR", "id-ID", "it-IT",
            "ja-JP", "ja-KS", "kab-KAB", "kn-IN", "ko-KR", "nl-NL", "no-NO", "pl-PL", "pt-PT",
            "ru-RU", "sk-SK", "ug-CN", "uk-UA", "vi-VN", "zh-CN", "zh-TW"
        ];

        Assert.Equal(expected, Catalog.Locales.Select(locale => locale.Locale));
        Assert.All(Catalog.Locales, locale => Assert.Equal(1632, Catalog.GetTranslationCount(locale.Locale)));
        Assert.Equal("rtl", Catalog.GetRequiredDefinition("ar-SA").Direction);
        Assert.Equal("rtl", Catalog.GetRequiredDefinition("ug-CN").Direction);
        Assert.All(
            Catalog.Locales.Where(locale => locale.Locale is not ("ar-SA" or "ug-CN")),
            locale => Assert.Equal("ltr", locale.Direction));
    }

    [Fact]
    public void ReproducesPinnedFallbackChainsAndDotPathLookup()
    {
        Assert.Equal(["ja-JP"], Catalog.GetRequiredDefinition("ja-JP").FallbackChain);
        Assert.Equal(["ja-JP", "en-US"], Catalog.GetRequiredDefinition("en-US").FallbackChain);
        Assert.Equal(["ja-JP", "ja-KS"], Catalog.GetRequiredDefinition("ja-KS").FallbackChain);
        Assert.Equal(
            ["ja-JP", "en-US", "zh-CN", "zh-TW"],
            Catalog.GetRequiredDefinition("zh-TW").FallbackChain);
        Assert.Equal(["ja-JP", "en-US", "da-DK"], Catalog.GetRequiredDefinition("da-DK").FallbackChain);

        Assert.Equal("もっと見る", Catalog.Translate("ja-JP", "showMore"));
        Assert.Equal("Show more", Catalog.Translate("en-US", "showMore"));
        Assert.Equal("Show more", Catalog.Translate("da-DK", "showMore"));
        Assert.Equal("まだまだあるで！", Catalog.Translate("ja-KS", "showMore"));
        Assert.Equal("載入更多", Catalog.Translate("zh-TW", "showMore"));
        Assert.Equal("Monday", Catalog.Translate("en-US", "_weekday.monday"));
        Assert.Equal("missing.path", Catalog.Translate("en-US", "missing.path"));
    }

    [Fact]
    public void ReproducesUpstreamSingleReplacementInterpolation()
    {
        var arguments = new Dictionary<string, object?>
        {
            ["month"] = 8,
            ["day"] = 4
        };

        Assert.Equal("8/4", Catalog.Translate("en-US", "monthAndDay", arguments));
        Assert.Equal("8月 4日", Catalog.Translate("ja-JP", "monthAndDay", arguments));
        Assert.Equal("before 3 after {n}", MisskeyInterpolation.Apply("before {n} after {n}", new Dictionary<string, object?> { ["n"] = 3 }));
    }

    [Fact]
    public void ResolvesSafeCookieBeforeWeightedAcceptLanguageWithoutUsingHost()
    {
        var resolver = new MisskeyLocaleRequestResolver(Catalog);
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("ar-SA.tailnet.invalid");
        context.Request.Headers.AcceptLanguage = "ar-SA;q=1,en-US;q=0.5";
        context.Request.Headers.Cookie = $"{MisskeyLocaleRequestResolver.CookieName}=en-US";

        Assert.Equal("en-US", resolver.Resolve(context));

        context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "fr-CA;q=1,en-US;q=0.5";
        Assert.Equal("fr-FR", resolver.Resolve(context));

        context.Request.Headers.Cookie = $"{MisskeyLocaleRequestResolver.CookieName}=../../etc/passwd";
        Assert.Equal("fr-FR", resolver.Resolve(context));
        Assert.Equal("ja-JP", resolver.ResolveAcceptLanguage(new StringValues("*;q=1,xx-ZZ;q=0.9")));
    }

    [Fact]
    public void LocalizerAcceptsOnlyCatalogLocalesAndRaisesOneStateChange()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "ja-JP";
        var localizer = new MisskeyLocalizer(
            Catalog,
            new MisskeyLocaleRequestResolver(Catalog),
            new HttpContextAccessor { HttpContext = context });
        int changes = 0;
        localizer.LocaleChanged += (_, _) => changes++;

        Assert.False(localizer.TrySelectLocale("not-a-locale"));
        Assert.True(localizer.TrySelectLocale("AR-sa"));
        Assert.True(localizer.TrySelectLocale("ar-SA"));
        Assert.Equal("ar-SA", localizer.CurrentLocale);
        Assert.Equal("rtl", localizer.Direction);
        Assert.Equal("عرض المزيد", localizer.Translate("showMore"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task DocumentMiddlewarePinsTheSsrLocaleForTheInteractiveCircuitOnlyOnDocumentResponses()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var resolver = new MisskeyLocaleRequestResolver(Catalog);
            bool nextCalled = false;
            string? observedCulture = null;
            string? observedUiCulture = null;
            var middleware = new MisskeyFrontendLocalizationMiddleware(_ =>
            {
                nextCalled = true;
                observedCulture = CultureInfo.CurrentCulture.Name;
                observedUiCulture = CultureInfo.CurrentUICulture.Name;
                return Task.CompletedTask;
            });
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Get;
            context.Request.Headers.Accept = "text/html,application/xhtml+xml";
            context.Request.Headers.AcceptLanguage = "ar-SA,en-US;q=0.5";

            await middleware.InvokeAsync(context, resolver);

            Assert.True(nextCalled);
            Assert.Equal("ar-SA", observedCulture);
            Assert.Equal("ar-SA", observedUiCulture);
            string? setCookieValue = Assert.Single(context.Response.Headers.SetCookie);
            string setCookie = Assert.IsType<string>(setCookieValue);
            Assert.Contains("misskey.lang=ar-SA", setCookie, StringComparison.Ordinal);
            Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("httponly", setCookie, StringComparison.OrdinalIgnoreCase);

            context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/_framework/blazor.web.js";
            context.Request.Headers.Accept = "*/*";
            await middleware.InvokeAsync(context, resolver);
            Assert.Equal(0, context.Response.Headers.SetCookie.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
