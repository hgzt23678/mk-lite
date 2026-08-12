using System.Net;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using ActivityPub.Federation.Protocol;

namespace ActivityPub.Federation.Inbound;

public sealed class ActorMoveValidator(
    ISafeFederationHttpClient httpClient,
    FederationOptions options) : IActorMoveValidator
{
    private static readonly IReadOnlySet<string> AcceptedMediaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/activity+json",
        "application/ld+json",
        "application/json"
    };

    public async Task ValidateAsync(
        string sourceActorIri,
        string targetActorIri,
        CancellationToken cancellationToken)
    {
        string source = CanonicalIri.RequireAbsoluteHttp(sourceActorIri, nameof(sourceActorIri));
        string target = CanonicalIri.RequireAbsoluteHttp(targetActorIri, nameof(targetActorIri));
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri(target),
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AcceptedMediaTypes,
            options.MaximumRemoteDocumentBytes);
        SafeFederationResponse response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ActivityStreamsProtocolException($"Move target returned HTTP {(int)response.StatusCode}.");
        }

        JsonSafetyValidator.Validate(response.Body);
        using JsonDocument document = JsonDocument.Parse(response.Body, new JsonDocumentOptions { MaxDepth = 64 });
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.String ||
            !string.Equals(CanonicalIri.RequireAbsoluteHttp(id.GetString()!, "id"), target, StringComparison.Ordinal))
        {
            throw new ActivityStreamsProtocolException("Move target actor id does not match target IRI.");
        }

        List<string> aliases = ReadAliases(root);
        if (!aliases.Contains(source, StringComparer.Ordinal))
        {
            throw new ActivityStreamsProtocolException("Move target does not declare the source actor in alsoKnownAs.");
        }
    }

    private static List<string> ReadAliases(JsonElement root)
    {
        if (!root.TryGetProperty("alsoKnownAs", out JsonElement aliases))
        {
            return [];
        }

        IEnumerable<JsonElement> values = aliases.ValueKind == JsonValueKind.Array
            ? aliases.EnumerateArray()
            : [aliases];
        var result = new List<string>();
        foreach (JsonElement value in values)
        {
            string? candidate = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Object when value.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String => id.GetString(),
                JsonValueKind.Object when value.TryGetProperty("href", out JsonElement href) && href.ValueKind == JsonValueKind.String => href.GetString(),
                _ => null
            };
            if (candidate is not null)
            {
                result.Add(CanonicalIri.RequireAbsoluteHttp(candidate, "alsoKnownAs"));
            }
        }

        return result;
    }
}
