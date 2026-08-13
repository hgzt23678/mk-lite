using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActivityPub.Identity;

public sealed class FrontendBrowserAntiforgeryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (RequiresValidation(context))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/problem+json";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "about:blank",
                    title = "The browser request could not be verified.",
                    status = StatusCodes.Status400BadRequest
                }).ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool RequiresValidation(HttpContext context) =>
        FrontendBrowserSessionMetadata.IsExplicitBrowserRequest(context) &&
        string.IsNullOrEmpty(context.Request.Headers.Authorization) &&
        context.User.HasClaim(FrontendBrowserSessionMetadata.SessionClaim, "true") &&
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        !HttpMethods.IsOptions(context.Request.Method) &&
        !HttpMethods.IsTrace(context.Request.Method);
}

public static class FrontendBrowserAntiforgeryApplicationBuilderExtensions
{
    public static IApplicationBuilder UseFrontendBrowserAntiforgery(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<FrontendBrowserAntiforgeryMiddleware>();
    }
}
