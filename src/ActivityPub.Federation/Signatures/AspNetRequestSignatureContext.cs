using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using NSign;
using NSign.Http;
using NSign.Signatures;

namespace ActivityPub.Federation.Signatures;

internal sealed class AspNetRequestSignatureContext(
    HttpRequest request,
    SignatureVerificationOptions verificationOptions)
    : MessageContext(NullLogger.Instance, new HttpFieldOptions())
{
    public override SignatureVerificationOptions VerificationOptions => verificationOptions;
    public override bool HasResponse => false;
    public override CancellationToken Aborted => request.HttpContext.RequestAborted;

    public override void AddHeader(string headerName, string value) => throw new NotSupportedException();

    public override IEnumerable<string> GetHeaderValues(string headerName) => GetValues(headerName);

    public override IEnumerable<string> GetRequestHeaderValues(string headerName) => GetValues(headerName);

    public override IEnumerable<string> GetTrailerValues(string fieldName) => [];

    public override IEnumerable<string> GetRequestTrailerValues(string fieldName) => [];

    public override IEnumerable<string> GetQueryParamValues(string paramName) =>
        request.Query.TryGetValue(paramName, out StringValues values) ? values : [];

    public override bool HasHeader(bool bindRequest, string headerName) => GetValues(headerName).Count > 0;

    public override bool HasTrailer(bool bindRequest, string fieldName) => false;

    public override bool HasExactlyOneQueryParamValue(string paramName) =>
        request.Query.TryGetValue(paramName, out StringValues values) && values.Count == 1;

    public override string GetDerivedComponentValue(DerivedComponent component) =>
        component.ComponentName switch
        {
            Constants.DerivedComponents.Method => request.Method,
            Constants.DerivedComponents.TargetUri => $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}",
            Constants.DerivedComponents.Authority => NormalizeAuthority(request),
            Constants.DerivedComponents.Scheme => request.Scheme.ToLowerInvariant(),
            Constants.DerivedComponents.RequestTarget => $"{request.PathBase}{request.Path}{request.QueryString}",
            Constants.DerivedComponents.Path => $"{request.PathBase}{request.Path}",
            Constants.DerivedComponents.Query => request.QueryString.HasValue ? request.QueryString.Value! : "?",
            Constants.DerivedComponents.SignatureParams => throw new NotSupportedException("Signature parameters are built by NSign."),
            Constants.DerivedComponents.QueryParam => throw new NotSupportedException("Query parameter values have a dedicated code path."),
            Constants.DerivedComponents.Status => throw new NotSupportedException("Request messages have no status component."),
            _ => throw new NotSupportedException($"Unsupported derived signature component '{component.ComponentName}'.")
        };

    private StringValues GetValues(string headerName)
    {
        if (request.Headers.TryGetValue(headerName, out StringValues values))
        {
            return values;
        }

        if (string.Equals(headerName, "content-length", StringComparison.OrdinalIgnoreCase) && request.ContentLength is { } length)
        {
            return new StringValues(length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return StringValues.Empty;
    }

    private static string NormalizeAuthority(HttpRequest source)
    {
        if (source.Host.Port is { } port &&
            ((string.Equals(source.Scheme, "http", StringComparison.OrdinalIgnoreCase) && port == 80) ||
             (string.Equals(source.Scheme, "https", StringComparison.OrdinalIgnoreCase) && port == 443)))
        {
            return source.Host.Host.ToLowerInvariant();
        }

        return source.Host.ToUriComponent().ToLowerInvariant();
    }
}
