using System.Net.Http.Json;
using System.Text.Json;
using ActivityPub.Application;

namespace ActivityPub.Identity;

public sealed class VaultTransitKeyProvisioner(
    HttpClient httpClient,
    VaultTransitOptions options) : IExternalKeyProvisioner
{
    public async Task<ExternalKeyProvision> CreateRsaKeyAsync(string handle, CancellationToken cancellationToken)
    {
        ValidateHandle(handle);
        string token = await ReadTokenAsync(cancellationToken).ConfigureAwait(false);
        using var create = CreateRequest(HttpMethod.Post, $"v1/{Uri.EscapeDataString(options.Mount)}/keys/{Uri.EscapeDataString(handle)}", token);
        create.Content = JsonContent.Create(new
        {
            type = "rsa-2048",
            exportable = false,
            allow_plaintext_backup = false,
            deletion_allowed = false
        });
        using HttpResponseMessage createResponse = await httpClient.SendAsync(create, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!createResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Vault Transit key creation failed with HTTP {(int)createResponse.StatusCode}.");
        }

        using var read = CreateRequest(HttpMethod.Get, $"v1/{Uri.EscapeDataString(options.Mount)}/keys/{Uri.EscapeDataString(handle)}", token);
        using HttpResponseMessage response = await httpClient.SendAsync(read, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Vault Transit key read failed with HTTP {(int)response.StatusCode}.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("latest_version", out JsonElement latest) ||
            !data.TryGetProperty("keys", out JsonElement keys) ||
            !keys.TryGetProperty(latest.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), out JsonElement key) ||
            !key.TryGetProperty("public_key", out JsonElement publicKey) ||
            publicKey.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Vault Transit returned no RSA public key.");
        }

        return new(handle, publicKey.GetString()!);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("X-Vault-Token", token);
        if (!string.IsNullOrWhiteSpace(options.Namespace))
        {
            request.Headers.TryAddWithoutValidation("X-Vault-Namespace", options.Namespace);
        }

        return request;
    }

    private async Task<string> ReadTokenAsync(CancellationToken cancellationToken)
    {
        string token = (await File.ReadAllTextAsync(options.TokenFile, cancellationToken).ConfigureAwait(false)).Trim();
        if (token.Length is < 8 or > 4_096 || token.Any(char.IsControl))
        {
            throw new InvalidOperationException("Vault token file does not contain a valid bounded token.");
        }

        return token;
    }

    private static void ValidateHandle(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || handle.Length > 128 ||
            handle.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Vault key handle must contain only ASCII letters, digits, and hyphens.", nameof(handle));
        }
    }
}
