using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActivityPub.Misskey.Blazor.Localization;

public sealed class MisskeyFrontendLocalizationMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    public async Task InvokeAsync(HttpContext context, MisskeyLocaleRequestResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolver);
        if (IsDocumentRequest(context.Request))
        {
            string locale = resolver.Resolve(context);
            CultureInfo culture = CultureInfo.GetCultureInfo(locale);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            context.Response.Cookies.Append(
                MisskeyLocaleRequestResolver.CookieName,
                locale,
                new CookieOptions
                {
                    HttpOnly = false,
                    IsEssential = true,
                    MaxAge = CookieLifetime,
                    Path = "/",
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps
                });
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsDocumentRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        if (string.Equals(request.Headers["Sec-Fetch-Dest"], "document", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return request.Headers.Accept.Any(value =>
            value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(mediaType => mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) == true);
    }
}

public static class MisskeyFrontendLocalizationApplicationBuilderExtensions
{
    public static IApplicationBuilder UseMisskeyFrontendLocalization(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.UseMiddleware<MisskeyFrontendLocalizationMiddleware>();
    }
}
