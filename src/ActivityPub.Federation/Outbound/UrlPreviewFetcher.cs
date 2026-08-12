using System.Net;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Federation.Http;

namespace ActivityPub.Federation.Outbound;

public sealed class UrlPreviewFetcher(ISafeFederationHttpClient httpClient) : IUrlPreviewFetcher
{
    private const int MaximumPreviewBytes = 1_000_000;

    public async Task<UrlPreviewResult?> FetchAsync(
        string url,
        string? lang,
        CancellationToken cancellationToken)
    {
        _ = lang;
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri(url),
            Body: null,
            ContentType: null,
            Headers: new Dictionary<string, string>
            {
                ["Accept"] = "text/html, application/xhtml+xml, application/xml;q=0.9, */*;q=0.8",
                ["Accept-Language"] = "ja-JP,ja;q=0.9,en;q=0.8"
            },
            AcceptedMediaTypes: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "text/html",
                "application/xhtml+xml",
                "application/xml"
            },
            MaximumResponseBytes: MaximumPreviewBytes,
            TargetValidator: (uri, _) => Task.FromResult(uri.Scheme is "http" or "https"));

        SafeFederationResponse response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _ = exception;
            return null;
        }

        if (!response.StatusCode.IsSuccessStatusCode() ||
            response.MediaType is not null &&
            !response.MediaType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
            !response.MediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string html = Encoding.UTF8.GetString(response.Body);
        HtmlMetaSummary summary = HtmlMetaParser.Parse(html, response.FinalUri.AbsoluteUri);
        if (summary.Title.Length == 0)
        {
            return null;
        }

        return new UrlPreviewResult(
            summary.Title,
            summary.Description,
            summary.Thumbnail,
            summary.Icon,
            summary.SiteName,
            summary.PlayerUrl,
            summary.PlayerWidth,
            summary.PlayerHeight);
    }
}

internal static class HttpStatusCodeExtensions
{
    public static bool IsSuccessStatusCode(this HttpStatusCode status) =>
        status is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices;
}
