using System.Text.Json;
using ActivityPub.Domain;
using ActivityPub.Federation.Protocol;
using ActivityPub.Federation.Signatures;

namespace ActivityPub.Server;

internal static class ExceptionResponse
{
    public static async Task WriteAsync(HttpContext context)
    {
        Exception? exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        int status = exception switch
        {
            BadHttpRequestException request => request.StatusCode,
            Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException => StatusCodes.Status400BadRequest,
            HttpSignatureException => StatusCodes.Status401Unauthorized,
            ActivityStreamsProtocolException or DomainException or ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        string title = status switch
        {
            StatusCodes.Status400BadRequest when exception is Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException => "Invalid antiforgery token",
            StatusCodes.Status400BadRequest => "Invalid federation request",
            StatusCodes.Status401Unauthorized => "HTTP signature verification failed",
            StatusCodes.Status413PayloadTooLarge => "Request body is too large",
            StatusCodes.Status415UnsupportedMediaType => "Unsupported media type",
            _ => "Request processing failed"
        };
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            type = "about:blank",
            title,
            status,
            traceId = context.TraceIdentifier
        }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }
}
