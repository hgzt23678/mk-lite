using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ActivityPub.Misskey.Blazor.Client.Authentication;

public sealed class FrontendSessionClient(
    HttpClient httpClient,
    FrontendAntiforgeryTokenStore antiforgeryTokens,
    BrowserRequestAntiforgeryInterop directFetchAntiforgery)
{
    private const int MaximumResponseBytes = 65_536;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16
    };

    public async Task<FrontendSessionSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/frontend/session");
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            antiforgeryTokens.Clear();
            await directFetchAntiforgery.ClearAsync(cancellationToken).ConfigureAwait(false);
            return FrontendSessionSnapshot.Anonymous;
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidOperationException("The frontend session response exceeded the allowed size.");
        }

        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        FrontendSessionDocument? document = await response.Content
            .ReadFromJsonAsync<FrontendSessionDocument>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (document?.Csrf is null)
        {
            throw new InvalidOperationException("The frontend session response omitted its CSRF contract.");
        }

        antiforgeryTokens.Replace(document.Csrf.HeaderName, document.Csrf.RequestToken);
        if (antiforgeryTokens.RequestToken is null)
        {
            throw new InvalidOperationException("The frontend session response contained an invalid CSRF contract.");
        }
        await directFetchAntiforgery.ReplaceAsync(
            antiforgeryTokens.RequestToken,
            cancellationToken).ConfigureAwait(false);

        if (!document.Authenticated || !TryValidateViewer(document.Viewer, out FrontendViewer? viewer))
        {
            return FrontendSessionSnapshot.Anonymous;
        }

        return new FrontendSessionSnapshot(true, viewer);
    }

    private static bool TryValidateViewer(FrontendViewerDocument? value, out FrontendViewer? viewer)
    {
        viewer = null;
        if (value is null || !IsSafe(value.Username, 128) ||
            !Uri.TryCreate(value.ActorIri, UriKind.Absolute, out Uri? actorIri) ||
            !(string.Equals(actorIri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(actorIri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            actorIri.UserInfo.Length != 0 ||
            !string.IsNullOrEmpty(actorIri.Query) ||
            !string.IsNullOrEmpty(actorIri.Fragment) ||
            !actorIri.AbsolutePath.StartsWith("/users/", StringComparison.Ordinal))
        {
            return false;
        }

        string[] roles = (value.Roles ?? [])
            .Where(role => IsSafe(role, 128))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        viewer = new FrontendViewer(value.Username!, actorIri.AbsoluteUri, roles);
        return true;
    }

    private static bool IsSafe(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl);

    private sealed record FrontendSessionDocument(
        bool Authenticated,
        FrontendViewerDocument? Viewer,
        FrontendCsrfDocument? Csrf);

    private sealed record FrontendViewerDocument(
        string? Username,
        string? ActorIri,
        string[]? Roles);

    private sealed record FrontendCsrfDocument(
        string? HeaderName,
        string? RequestToken);
}

public sealed record FrontendViewer(
    string Username,
    string ActorIri,
    IReadOnlyList<string> Roles);

public sealed record FrontendSessionSnapshot(
    bool Authenticated,
    FrontendViewer? Viewer)
{
    public static FrontendSessionSnapshot Anonymous { get; } = new(false, null);
}
