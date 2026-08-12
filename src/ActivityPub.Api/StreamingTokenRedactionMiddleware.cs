using Microsoft.Extensions.Primitives;

namespace ActivityPub.Server;

internal sealed class StreamingTokenRedactionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        string? secretName = context.Request.Path.StartsWithSegments("/streaming", StringComparison.Ordinal)
            ? "i"
            : context.Request.Path.StartsWithSegments("/api/v1/streaming", StringComparison.Ordinal)
                ? "access_token"
                : null;
        if (secretName is null || !context.Request.Query.TryGetValue(secretName, out StringValues values))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 4096)
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }

        string authorization = "Bearer " + values[0];
        if (context.Request.Headers.TryGetValue("Authorization", out StringValues existing) &&
            !string.Equals(existing.ToString(), authorization, StringComparison.Ordinal))
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }

        context.Request.Headers.Authorization = authorization;
        IEnumerable<KeyValuePair<string, string?>> remaining = context.Request.Query
            .Where(pair => !string.Equals(pair.Key, secretName, StringComparison.Ordinal))
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)));
        context.Request.QueryString = QueryString.Create(remaining);
        await next(context).ConfigureAwait(false);
    }

    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Conflicting or invalid streaming credential.\"}").ConfigureAwait(false);
    }
}
