using System.Globalization;
using System.Security;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Inbound;
using ActivityPub.Federation.Protocol;
using ActivityPub.Federation.Signatures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ActivityPub.Server;

internal static class FederationEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ActivityPubProtocols = ["activitypub"];
    private static readonly string[] NoServices = [];

    public static IEndpointRouteBuilder MapFederationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/webfinger", WebFingerAsync).RequireRateLimiting("discovery");
        endpoints.MapGet("/.well-known/host-meta", HostMeta);
        endpoints.MapGet("/.well-known/nodeinfo", NodeInfoDiscovery);
        endpoints.MapGet("/nodeinfo/2.0", NodeInfoAsync);
        endpoints.MapGet("/users/{username}", ActorAsync).RequireRateLimiting("federation-get");

        endpoints.MapGet("/users/{username}/outbox", (HttpContext context, string username, IFederationQueryStore store, PublicIriFactory iriFactory, CancellationToken token) =>
            CollectionAsync(context, username, "outbox", store, iriFactory, null, token)).RequireRateLimiting("federation-get");
        endpoints.MapGet("/users/{username}/liked", (HttpContext context, string username, IFederationQueryStore store, PublicIriFactory iriFactory, CancellationToken token) =>
            CollectionAsync(context, username, "liked", store, iriFactory, null, token)).RequireRateLimiting("federation-get");
        endpoints.MapGet("/users/{username}/featured", (HttpContext context, string username, IFederationQueryStore store, PublicIriFactory iriFactory, CancellationToken token) =>
            CollectionAsync(context, username, "featured", store, iriFactory, null, token)).RequireRateLimiting("federation-get");
        endpoints.MapGet("/users/{username}/followers", SignedCollectionAsync("followers")).RequireRateLimiting("signed-get");
        endpoints.MapGet("/users/{username}/following", SignedCollectionAsync("following")).RequireRateLimiting("signed-get");
        endpoints.MapGet("/users/{username}/inbox", AuthorizedInboxCollectionAsync)
            .RequireAuthorization("activitypub.read")
            .RequireRateLimiting("local-api");

        endpoints.MapPost("/users/{username}/inbox", UserInboxAsync).RequireRateLimiting("inbox");
        endpoints.MapPost("/inbox", SharedInboxAsync).RequireRateLimiting("inbox");
        endpoints.MapPost("/users/{username}/outbox", ClientOutboxAsync)
            .RequireAuthorization("activitypub.write")
            .RequireRateLimiting("local-api");

        endpoints.MapGet("/objects/{id:guid}", ObjectAsync).RequireRateLimiting("federation-get");
        endpoints.MapGet("/activities/{id:guid}", ActivityAsync).RequireRateLimiting("federation-get");
        endpoints.MapGet("/collections/{id:guid}", CollectionByIdAsync).RequireRateLimiting("federation-get");
        return endpoints;
    }

    private static async Task WebFingerAsync(
        HttpContext context,
        string resource,
        IFederationQueryStore store,
        FederationOptions options,
        CancellationToken cancellationToken)
    {
        string? username = ParseWebFingerResource(resource, options.PublicBaseUri);
        if (username is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ActorDocument? actor = await store.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        string subject = $"acct:{actor.Username}@{options.PublicBaseUri.IdnHost}";
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            subject,
            aliases = new[] { actor.Iri },
            links = new object[]
            {
                new { rel = "self", type = ActivityStreamsConstants.ActivityJson, href = actor.Iri },
                new { rel = "http://webfinger.net/rel/profile-page", type = "text/html", href = actor.Iri }
            }
        }, JsonOptions);
        await WriteCacheableAsync(context, body, "application/jrd+json", actor.UpdatedAt, isPublic: true, cancellationToken).ConfigureAwait(false);
    }

    private static IResult HostMeta(FederationOptions options)
    {
        string template = options.PublicBaseUri.AbsoluteUri.TrimEnd('/') + "/.well-known/webfinger?resource={uri}";
        string xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><XRD xmlns=\"http://docs.oasis-open.org/ns/xri/xrd-1.0\"><Link rel=\"lrdd\" type=\"application/jrd+json\" template=\"{SecurityElement.Escape(template)}\"/></XRD>";
        return Results.Text(xml, "application/xrd+xml", Encoding.UTF8);
    }

    private static IResult NodeInfoDiscovery(FederationOptions options)
    {
        string href = options.PublicBaseUri.AbsoluteUri.TrimEnd('/') + "/nodeinfo/2.0";
        return Results.Json(new
        {
            links = new[] { new { rel = "http://nodeinfo.diaspora.software/ns/schema/2.0", href } }
        });
    }

    private static async Task<IResult> NodeInfoAsync(IFederationQueryStore store, CancellationToken cancellationToken)
    {
        NodeInfoCounts counts = await store.GetNodeInfoCountsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(new
        {
            version = "2.0",
            software = new { name = "activitypub-dotnet", version = typeof(FederationEndpoints).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" },
            protocols = ActivityPubProtocols,
            services = new { inbound = NoServices, outbound = NoServices },
            openRegistrations = false,
            usage = new { users = new { total = counts.LocalUsers }, localPosts = counts.LocalPosts },
            metadata = new { }
        }, contentType: "application/json; charset=utf-8");
    }

    private static async Task ActorAsync(
        HttpContext context,
        string username,
        IFederationQueryStore store,
        PublicIriFactory iriFactory,
        CancellationToken cancellationToken)
    {
        ActorDocument? actor = await store.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        byte[] body = ActivityStreamsSerializer.SerializeActor(actor, iriFactory);
        await WriteCacheableAsync(context, body, ActivityStreamsConstants.ActivityJson, actor.UpdatedAt, isPublic: true, cancellationToken).ConfigureAwait(false);
    }

    private static RequestDelegate SignedCollectionAsync(string collection) => async context =>
    {
        string username = context.Request.RouteValues["username"]?.ToString() ?? string.Empty;
        var store = context.RequestServices.GetRequiredService<IFederationQueryStore>();
        var iriFactory = context.RequestServices.GetRequiredService<PublicIriFactory>();
        var verifier = context.RequestServices.GetRequiredService<IHttpSignatureVerifier>();
        HttpSignatureVerification signature = await verifier.VerifyAsync(context, [], context.RequestAborted).ConfigureAwait(false);
        await CollectionAsync(context, username, collection, store, iriFactory, signature.KeyOwnerIri, context.RequestAborted).ConfigureAwait(false);
    };

    private static async Task AuthorizedInboxCollectionAsync(
        HttpContext context,
        string username,
        IFederationQueryStore store,
        PublicIriFactory iriFactory,
        CancellationToken cancellationToken)
    {
        if (!IsRouteUser(context.User, username))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await CollectionAsync(context, username, "inbox", store, iriFactory, context.User.Identity?.Name, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CollectionAsync(
        HttpContext context,
        string username,
        string collection,
        IFederationQueryStore store,
        PublicIriFactory iriFactory,
        string? requester,
        CancellationToken cancellationToken)
    {
        ActorDocument? actor = await store.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (collection is "followers" or "following" &&
            string.IsNullOrWhiteSpace(requester))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "private, no-store";
            return;
        }

        string collectionIri = collection switch
        {
            "inbox" => iriFactory.ActorInbox(actor.Username),
            "outbox" => iriFactory.ActorOutbox(actor.Username),
            "followers" => iriFactory.ActorFollowers(actor.Username),
            "following" => iriFactory.ActorFollowing(actor.Username),
            "liked" => iriFactory.ActorLiked(actor.Username),
            "featured" => iriFactory.ActorFeatured(actor.Username),
            _ => throw new ArgumentOutOfRangeException(nameof(collection))
        };
        bool page = string.Equals(context.Request.Query["page"], "true", StringComparison.OrdinalIgnoreCase) || context.Request.Query.ContainsKey("cursor");
        if (!page)
        {
            byte[] root = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["@context"] = ActivityStreamsConstants.ActivityStreamsContext,
                ["id"] = collectionIri,
                ["type"] = "OrderedCollection",
                ["first"] = collectionIri + "?page=true"
            }, JsonOptions);
            bool isPublicCollection = collection is not ("inbox" or "followers" or "following");
            await WriteCacheableAsync(context, root, ActivityStreamsConstants.ActivityJson, actor.UpdatedAt, isPublicCollection, cancellationToken).ConfigureAwait(false);
            return;
        }

        int limit = 40;
        if (context.Request.Query.TryGetValue("limit", out var rawLimit) &&
            (!int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 80))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string? cursor = context.Request.Query["cursor"].FirstOrDefault();
        CursorPage<CollectionEntry> result = await store.ReadCollectionAsync(
            actor.Iri,
            collection,
            new PageRequest(cursor, limit),
            cancellationToken).ConfigureAwait(false);
        var items = new JsonArray();
        foreach (CollectionEntry entry in result.Items)
        {
            items.Add(JsonNode.Parse(entry.Json));
        }

        string current = collectionIri + "?page=true" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
        var response = new JsonObject
        {
            ["@context"] = ActivityStreamsConstants.ActivityStreamsContext,
            ["id"] = current,
            ["type"] = "OrderedCollectionPage",
            ["partOf"] = collectionIri,
            ["orderedItems"] = items
        };
        if (result.NextCursor is not null)
        {
            response["next"] = collectionIri + "?page=true&cursor=" + Uri.EscapeDataString(result.NextCursor);
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        bool isPublicPage = collection is not ("inbox" or "followers" or "following");
        await WriteCacheableAsync(context, body, ActivityStreamsConstants.ActivityJson, result.LastModified, isPublicPage, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UserInboxAsync(
        HttpContext context,
        string username,
        IInboundActivityReceiver receiver,
        IFederationQueryStore store,
        CancellationToken cancellationToken)
    {
        ActorDocument? actor = await store.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        InboxAcceptance acceptance = await receiver.ReceiveAsync(context, actor.Iri, cancellationToken).ConfigureAwait(false);
        SetInboxStatus(context, acceptance);
    }

    private static async Task SharedInboxAsync(
        HttpContext context,
        IInboundActivityReceiver receiver,
        CancellationToken cancellationToken)
    {
        InboxAcceptance acceptance = await receiver.ReceiveAsync(context, null, cancellationToken).ConfigureAwait(false);
        SetInboxStatus(context, acceptance);
    }

    private static void SetInboxStatus(HttpContext context, InboxAcceptance acceptance)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.StatusCode = acceptance.Status switch
        {
            InboxAcceptanceStatus.Accepted or InboxAcceptanceStatus.Duplicate or InboxAcceptanceStatus.ConflictQuarantined => StatusCodes.Status202Accepted,
            InboxAcceptanceStatus.NoLocalRecipient => StatusCodes.Status404NotFound,
            InboxAcceptanceStatus.RejectedByPolicy => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private static async Task ClientOutboxAsync(
        HttpContext context,
        string username,
        IClientOutboxService outbox,
        FederationOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.ClientToServerEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!IsRouteUser(context.User, username))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!IsActivityStreamsContentType(context.Request.ContentType))
        {
            throw new BadHttpRequestException("ActivityStreams content type is required.", StatusCodes.Status415UnsupportedMediaType);
        }

        if (context.Request.ContentLength is > 0 && context.Request.ContentLength > options.MaximumInboxBodyBytes)
        {
            throw new BadHttpRequestException("Request body is too large.", StatusCodes.Status413PayloadTooLarge);
        }

        byte[] body = await ReadBoundedBodyAsync(context.Request, options.MaximumInboxBodyBytes, cancellationToken).ConfigureAwait(false);
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyValues) ||
            idempotencyValues.Count != 1 || string.IsNullOrWhiteSpace(idempotencyValues[0]))
        {
            throw new BadHttpRequestException("A single Idempotency-Key header is required.", StatusCodes.Status400BadRequest);
        }

        ClientOutboxResult result = await outbox.SubmitAsync(username, idempotencyValues[0]!, body, cancellationToken).ConfigureAwait(false);
        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.Headers.Location = result.ActivityIri;
        if (result.ObjectIri is not null)
        {
            context.Response.Headers.ContentLocation = result.ObjectIri;
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = ActivityStreamsConstants.ActivityJson;
        context.Response.ContentLength = result.ResponseBody.Length;
        await context.Response.Body.WriteAsync(result.ResponseBody, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsActivityStreamsContentType(string? rawContentType)
    {
        if (string.IsNullOrWhiteSpace(rawContentType) ||
            !Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(rawContentType, out var mediaType))
        {
            return false;
        }

        return string.Equals(mediaType.MediaType.Value, "application/activity+json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType.MediaType.Value, "application/ld+json", StringComparison.OrdinalIgnoreCase) &&
            mediaType.Parameters.Any(parameter =>
                string.Equals(parameter.Name.Value, "profile", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parameter.Value.Value?.Trim('"'), ActivityStreamsConstants.ActivityStreamsContext, StringComparison.Ordinal));
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(HttpRequest request, int maximumBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        byte[] chunk = new byte[16 * 1024];
        int total = 0;
        while (true)
        {
            int read = await request.Body.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new BadHttpRequestException("Request body is too large.", StatusCodes.Status413PayloadTooLarge);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (total == 0)
        {
            throw new BadHttpRequestException("Request body is empty.", StatusCodes.Status400BadRequest);
        }

        return buffer.ToArray();
    }

    private static Task ObjectAsync(HttpContext context, Guid id, IFederationQueryStore store, PublicIriFactory iriFactory, IHttpSignatureVerifier verifier, CancellationToken token) =>
        StoredDocumentAsync(context, iriFactory.ObjectIri(id), store.FindObjectAsync, store, verifier, token);

    private static Task ActivityAsync(HttpContext context, Guid id, IFederationQueryStore store, PublicIriFactory iriFactory, IHttpSignatureVerifier verifier, CancellationToken token) =>
        StoredDocumentAsync(context, iriFactory.ActivityIri(id), store.FindActivityAsync, store, verifier, token);

    private static Task CollectionByIdAsync(HttpContext context, Guid id, IFederationQueryStore store, PublicIriFactory iriFactory, IHttpSignatureVerifier verifier, CancellationToken token) =>
        StoredDocumentAsync(context, iriFactory.CollectionIri(id), store.FindObjectAsync, store, verifier, token);

    private static async Task StoredDocumentAsync(
        HttpContext context,
        string iri,
        Func<string, CancellationToken, Task<StoredDocument?>> loader,
        IFederationQueryStore store,
        IHttpSignatureVerifier verifier,
        CancellationToken cancellationToken)
    {
        StoredDocument? document = await loader(iri, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        bool isPublic = document.Visibility is Visibility.Public or Visibility.Unlisted;
        if (!isPublic)
        {
            HttpSignatureVerification signature = await verifier.VerifyAsync(context, [], cancellationToken).ConfigureAwait(false);
            if (!await store.IsAuthorizedRecipientAsync(document.Iri, signature.KeyOwnerIri, cancellationToken).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await WriteCacheableAsync(context, document.Body, document.MediaType, document.LastModified, isPublic, cancellationToken, document.ETag).ConfigureAwait(false);
    }

    private static async Task WriteCacheableAsync(
        HttpContext context,
        byte[] body,
        string contentType,
        DateTimeOffset lastModified,
        bool isPublic,
        CancellationToken cancellationToken,
        string? suppliedEtag = null)
    {
        DateTimeOffset normalizedLastModified = new(lastModified.Year, lastModified.Month, lastModified.Day, lastModified.Hour, lastModified.Minute, lastModified.Second, TimeSpan.Zero);
        string etag = suppliedEtag ?? $"\"sha256-{PayloadDigest.Sha256Hex(body)}\"";
        context.Response.Headers.ETag = etag;
        context.Response.Headers.LastModified = normalizedLastModified.ToString("r", CultureInfo.InvariantCulture);
        context.Response.Headers.CacheControl = isPublic ? "public, max-age=60, stale-while-revalidate=300" : "private, no-store";
        context.Response.Headers.Vary = isPublic ? "Accept" : "Accept, Signature, Authorization";
        bool hasIfNoneMatch = context.Request.Headers.IfNoneMatch.Count > 0;
        bool etagMatches = context.Request.Headers.IfNoneMatch
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Any(value => value == "*" ||
                string.Equals(value, etag, StringComparison.Ordinal) ||
                string.Equals(value, "W/" + etag, StringComparison.Ordinal));
        bool lastModifiedMatches = !hasIfNoneMatch &&
            context.Request.GetTypedHeaders().IfModifiedSince is { } modified && normalizedLastModified <= modified;
        if (etagMatches || lastModifiedMatches)
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        context.Response.ContentType = contentType;
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static string? ParseWebFingerResource(string resource, Uri publicBaseUri)
    {
        if (resource.StartsWith("acct:", StringComparison.OrdinalIgnoreCase))
        {
            string account = resource[5..];
            int separator = account.LastIndexOf('@');
            if (separator <= 0 || !string.Equals(account[(separator + 1)..], publicBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return account[..separator];
        }

        if (Uri.TryCreate(resource, UriKind.Absolute, out Uri? iri) &&
            string.Equals(iri.Scheme, publicBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(iri.IdnHost, publicBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase))
        {
            string[] segments = iri.AbsolutePath.Trim('/').Split('/');
            return segments.Length == 2 && segments[0] == "users" ? Uri.UnescapeDataString(segments[1]) : null;
        }

        return null;
    }

    private static bool IsRouteUser(ClaimsPrincipal user, string username) =>
        user.IsInRole("activitypub-admin") ||
        string.Equals(user.FindFirstValue("preferred_username"), username, StringComparison.OrdinalIgnoreCase);
}
