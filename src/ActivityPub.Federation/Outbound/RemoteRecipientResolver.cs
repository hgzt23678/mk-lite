using System.Net;
using System.Text;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Http;
using ActivityPub.Federation.Protocol;

namespace ActivityPub.Federation.Outbound;

public sealed class RemoteRecipientResolver(
    IRemoteActorDirectory directory,
    ISafeFederationHttpClient httpClient,
    IDomainPolicyService policyService,
    FederationOptions options,
    IClock clock) : IRemoteRecipientResolver
{
    private static readonly IReadOnlySet<string> AcceptedMediaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/activity+json",
        "application/ld+json",
        "application/json"
    };

    public async Task<IReadOnlyList<RemoteActorEndpoint>> ResolveAsync(
        string localActorIri,
        IReadOnlyList<AudienceAddress> audience,
        CancellationToken cancellationToken) =>
        await ResolveCoreAsync(localActorIri, audience, includeUserBlockedActors: false, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<RemoteActorEndpoint>> ResolveIncludingBlockedAsync(
        string localActorIri,
        IReadOnlyList<AudienceAddress> audience,
        CancellationToken cancellationToken) =>
        await ResolveCoreAsync(localActorIri, audience, includeUserBlockedActors: true, cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<RemoteActorEndpoint>> ResolveCoreAsync(
        string localActorIri,
        IReadOnlyList<AudienceAddress> audience,
        bool includeUserBlockedActors,
        CancellationToken cancellationToken)
    {
        string followersIri = localActorIri.TrimEnd('/') + "/followers";
        var result = new Dictionary<string, RemoteActorEndpoint>(StringComparer.Ordinal);
        IReadOnlyList<RemoteActorEndpoint> followers = [];
        if (audience.Any(x => string.Equals(x.Iri.TrimEnd('/'), followersIri, StringComparison.Ordinal)))
        {
            followers = await directory
                .FindAcceptedFollowerEndpointsAsync(localActorIri, cancellationToken)
                .ConfigureAwait(false);
        }

        Uri localOrigin = options.PublicBaseUri;
        string[] explicitRemoteActors = audience
            .Select(x => x.Iri)
            .Where(iri => iri != ActivityStreamsConstants.PublicAudience &&
                !string.Equals(iri.TrimEnd('/'), followersIri, StringComparison.Ordinal))
            .Where(iri => !IsLocal(new Uri(iri), localOrigin))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] candidates = followers.Select(x => x.ActorIri)
            .Concat(explicitRemoteActors)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IReadOnlySet<string> rejected = includeUserBlockedActors
            ? await policyService.FindRejectedActorsAsync(candidates, cancellationToken).ConfigureAwait(false)
            : await policyService.FindRejectedActorsForLocalAsync(localActorIri, candidates, cancellationToken).ConfigureAwait(false);
        foreach (RemoteActorEndpoint follower in followers)
        {
            if (!rejected.Contains(follower.ActorIri))
            {
                result.TryAdd(follower.ActorIri, follower);
            }
        }

        if (result.Count > options.MaximumRecipientsPerActivity)
        {
            throw new InvalidOperationException("Activity recipient count exceeds the configured limit.");
        }

        int fetches = 0;
        foreach (string recipientIri in explicitRemoteActors)
        {
            if (rejected.Contains(recipientIri))
            {
                continue;
            }

            RemoteActorEndpoint? endpoint = await directory.FindEndpointAsync(recipientIri, cancellationToken).ConfigureAwait(false);
            if (endpoint is null)
            {
                fetches++;
                if (fetches > options.MaximumFetchesPerOperation)
                {
                    throw new InvalidOperationException("Remote actor discovery fetch limit was exceeded.");
                }

                endpoint = await DiscoverAsync(recipientIri, cancellationToken).ConfigureAwait(false);
            }

            result.TryAdd(endpoint.ActorIri, endpoint);
            if (result.Count > options.MaximumRecipientsPerActivity)
            {
                throw new InvalidOperationException("Activity recipient count exceeds the configured limit.");
            }
        }

        return result.Values.ToArray();
    }

    public Task<RemoteActorEndpoint> RediscoverAsync(
        string actorIri,
        CancellationToken cancellationToken) =>
        DiscoverAsync(actorIri, cancellationToken);

    private static bool IsLocal(Uri recipient, Uri localOrigin) =>
        string.Equals(recipient.Scheme, localOrigin.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(recipient.IdnHost, localOrigin.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        recipient.Port == localOrigin.Port;

    private async Task<RemoteActorEndpoint> DiscoverAsync(string actorIri, CancellationToken cancellationToken)
    {
        string canonicalActorIri = CanonicalIri.RequireAbsoluteHttp(actorIri, nameof(actorIri));
        var request = new SafeFederationRequest(
            HttpMethod.Get,
            new Uri(canonicalActorIri),
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AcceptedMediaTypes,
            options.MaximumRemoteDocumentBytes);
        SafeFederationResponse response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"Remote actor discovery returned HTTP {(int)response.StatusCode}.");
        }

        JsonSafetyValidator.Validate(response.Body);
        using JsonDocument document = JsonDocument.Parse(response.Body, new JsonDocumentOptions { MaxDepth = 64 });
        JsonElement root = document.RootElement;
        string id = RequiredIri(root, "id");
        if (!string.Equals(id, canonicalActorIri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Remote actor document id does not match the requested IRI.");
        }

        string type = ActivityStreamsParser.ReadTypes(root)[0];
        if (type is not ("Person" or "Service" or "Application" or "Group" or "Organization"))
        {
            throw new InvalidOperationException("Remote document is not an ActivityStreams actor.");
        }

        string inbox = RequiredIri(root, "inbox");
        string? sharedInbox = null;
        if (root.TryGetProperty("endpoints", out JsonElement endpoints) && endpoints.ValueKind == JsonValueKind.Object &&
            endpoints.TryGetProperty("sharedInbox", out JsonElement shared))
        {
            sharedInbox = ReadIri(shared, "sharedInbox");
        }

        string? preferredUsername = root.TryGetProperty("preferredUsername", out JsonElement preferred) && preferred.ValueKind == JsonValueKind.String
            ? preferred.GetString()
            : null;
        DateTimeOffset now = clock.UtcNow;
        var snapshot = new RemoteActorSnapshot(
            id,
            type,
            preferredUsername,
            Encoding.UTF8.GetString(response.Body),
            inbox,
            sharedInbox,
            response.ETag,
            response.LastModified,
            now);
        await directory.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return new(id, inbox, sharedInbox);
    }

    private static string RequiredIri(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidOperationException($"Remote actor has no {name}.");
        }

        return ReadIri(value, name);
    }

    private static string ReadIri(JsonElement value, string name)
    {
        string? candidate = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String => id.GetString(),
            JsonValueKind.Object when value.TryGetProperty("href", out JsonElement href) && href.ValueKind == JsonValueKind.String => href.GetString(),
            _ => null
        };
        return candidate is null
            ? throw new InvalidOperationException($"Remote actor {name} is not an IRI.")
            : CanonicalIri.RequireAbsoluteHttp(candidate, name);
    }
}
