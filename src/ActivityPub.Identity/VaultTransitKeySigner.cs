using System.Net.Http.Json;
using System.Text.Json;
using ActivityPub.Application;

namespace ActivityPub.Identity;

public sealed class VaultTransitKeySigner(
    HttpClient httpClient,
    VaultTransitOptions options) : IKeySigner
{
    public async Task<byte[]> SignAsync(
        string privateKeyHandle,
        string algorithm,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyHandle);
        if (!string.Equals(algorithm, "rsa-v1_5-sha256", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Vault Transit signer only accepts rsa-v1_5-sha256 keys.");
        }

        if (privateKeyHandle.Length > 256 || privateKeyHandle.Contains('/', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Vault Transit key handle is invalid.");
        }

        string token = (await File.ReadAllTextAsync(options.TokenFile, cancellationToken).ConfigureAwait(false)).Trim();
        if (token.Length is < 8 or > 4_096 || token.Any(char.IsControl))
        {
            throw new InvalidOperationException("Vault token file does not contain a valid bounded token.");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1/{Uri.EscapeDataString(options.Mount)}/sign/{Uri.EscapeDataString(privateKeyHandle)}/sha2-256");
        message.Headers.TryAddWithoutValidation("X-Vault-Token", token);
        if (!string.IsNullOrWhiteSpace(options.Namespace))
        {
            message.Headers.TryAddWithoutValidation("X-Vault-Namespace", options.Namespace);
        }

        message.Content = JsonContent.Create(new
        {
            input = Convert.ToBase64String(data.Span),
            signature_algorithm = "pkcs1v15",
            prehashed = false
        });
        using HttpResponseMessage response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Vault Transit signing failed with HTTP {(int)response.StatusCode}.");
        }

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(
            responseStream,
            new JsonDocumentOptions { MaxDepth = 16 },
            cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out JsonElement responseData) ||
            !responseData.TryGetProperty("signature", out JsonElement signatureElement) ||
            signatureElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Vault Transit response contains no signature.");
        }

        string signature = signatureElement.GetString()!;
        int delimiter = signature.LastIndexOf(':');
        if (delimiter <= 0 || delimiter == signature.Length - 1)
        {
            throw new InvalidOperationException("Vault Transit signature envelope is malformed.");
        }

        try
        {
            return Convert.FromBase64String(signature[(delimiter + 1)..]);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Vault Transit returned invalid signature bytes.", exception);
        }
    }
}
