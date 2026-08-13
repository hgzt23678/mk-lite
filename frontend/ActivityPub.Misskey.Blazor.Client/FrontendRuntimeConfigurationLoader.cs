using System.Net.Http.Json;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed class FrontendRuntimeConfigurationLoader(
    HttpClient httpClient,
    FrontendRuntimeConfigurationState state,
    Authentication.FrontendOrigin transportOrigin)
{
    private const int MaximumResponseBytes = 65_536;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/frontend/config");
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidOperationException("The frontend runtime configuration exceeded the allowed size.");
        }

        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        FrontendRuntimeConfigurationDocument? document = await response.Content
            .ReadFromJsonAsync<FrontendRuntimeConfigurationDocument>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        state.Initialize(Validate(document, transportOrigin.Value));
    }

    private static FrontendRuntimeSettings Validate(
        FrontendRuntimeConfigurationDocument? document,
        Uri transportOrigin)
    {
        if (document is null || !document.Enabled ||
            !TryAbsoluteHttpUri(document.PublicBaseUri, out Uri? publicBaseUri) ||
            !TryAbsoluteHttpUri(document.ApiBaseUri, out Uri? apiBaseUri) ||
            !TryAbsoluteHttpUri(document.Authority, out Uri? authority) ||
            publicBaseUri is null || apiBaseUri is null || authority is null ||
            !SameOrigin(publicBaseUri, transportOrigin) ||
            !SameOrigin(publicBaseUri, apiBaseUri) ||
            !apiBaseUri.AbsolutePath.EndsWith("/api/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The frontend runtime configuration is invalid.");
        }

        Uri validatedPublicBaseUri = publicBaseUri;
        Uri validatedApiBaseUri = apiBaseUri;
        Uri validatedAuthority = authority;

        Uri? sourceUrl = null;
        if (!string.IsNullOrWhiteSpace(document.SourceUrl) &&
            (!Uri.TryCreate(document.SourceUrl, UriKind.Absolute, out sourceUrl) ||
             !IsHttpScheme(sourceUrl.Scheme) ||
             sourceUrl.UserInfo.Length != 0))
        {
            throw new InvalidOperationException("The frontend source URL is invalid.");
        }

        return new FrontendRuntimeSettings(
            validatedPublicBaseUri,
            validatedApiBaseUri,
            validatedAuthority,
            sourceUrl,
            document.LocalAccountsEnabled);
    }

    private static bool TryAbsoluteHttpUri(string? value, out Uri? uri)
    {
        bool valid = Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            IsHttpScheme(uri.Scheme) &&
            uri.UserInfo.Length == 0 &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment);
        if (!valid)
        {
            uri = null;
        }

        return valid;
    }

    private static bool IsHttpScheme(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private sealed record FrontendRuntimeConfigurationDocument(
        bool Enabled,
        string? PublicBaseUri,
        string? ApiBaseUri,
        string? Authority,
        string? SourceUrl,
        bool LocalAccountsEnabled);
}

public sealed class FrontendRuntimeConfigurationState
{
    private FrontendRuntimeSettings? settings;

    public void Initialize(FrontendRuntimeSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Interlocked.CompareExchange(ref settings, value, null) is not null)
        {
            throw new InvalidOperationException("The frontend runtime configuration was initialized more than once.");
        }
    }

    public FrontendRuntimeSettings GetRequiredSettings() =>
        Volatile.Read(ref settings)
        ?? throw new InvalidOperationException("The frontend runtime configuration has not been initialized.");

    public MisskeyFrontendRuntimeConfiguration GetRequiredRuntime()
    {
        FrontendRuntimeSettings value = GetRequiredSettings();
        return new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            value.SourceUrl,
            value.PublicBaseUri,
            value.LocalAccountsEnabled);
    }
}

public sealed record FrontendRuntimeSettings(
    Uri PublicBaseUri,
    Uri ApiBaseUri,
    Uri Authority,
    Uri? SourceUrl,
    bool LocalAccountsEnabled);
