using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Protocol;
using ActivityPub.Federation.Signatures;
using Microsoft.AspNetCore.Http;

namespace ActivityPub.Federation.Inbound;

public interface IInboundActivityReceiver
{
    Task<InboxAcceptance> ReceiveAsync(HttpContext context, string? requiredLocalActorIri, CancellationToken cancellationToken);
}

public sealed class InboundActivityReceiver(
    IHttpSignatureVerifier signatureVerifier,
    IInboxRepository inboxRepository,
    IFederationInstrumentation instrumentation,
    FederationOptions options,
    IClock clock) : IInboundActivityReceiver
{
    public async Task<InboxAcceptance> ReceiveAsync(HttpContext context, string? requiredLocalActorIri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateContentType(context.Request.ContentType);
        if (context.Request.ContentLength is > 0 && context.Request.ContentLength > options.MaximumInboxBodyBytes)
        {
            throw new BadHttpRequestException("Inbox body exceeds the configured limit.", StatusCodes.Status413PayloadTooLarge);
        }

        byte[] rawBody = await ReadBodyAsync(
            context.Request.Body,
            options.MaximumInboxBodyBytes,
            cancellationToken).ConfigureAwait(false);
        HttpSignatureVerification signature = await signatureVerifier
            .VerifyAsync(context, rawBody, cancellationToken)
            .ConfigureAwait(false);
        ActivityStreamsDocument document = ActivityStreamsParser.ParseActivity(rawBody);
        if (!string.Equals(signature.KeyOwnerIri, document.ActorIri, StringComparison.Ordinal))
        {
            throw new HttpSignatureException("Signing key owner does not match Activity actor.");
        }

        IReadOnlyList<AudienceAddress> audience = InferRouteRecipientForTargetedActivity(
            document,
            requiredLocalActorIri);
        DateTimeOffset now = clock.UtcNow;
        var verified = new VerifiedInboundActivity(
            document.Id,
            document.ActorIri,
            document.PrimaryType,
            document.ObjectIri,
            document.ObjectOwnerIri,
            document.Origin,
            audience,
            requiredLocalActorIri,
            rawBody,
            PayloadDigest.Sha256Hex(rawBody),
            signature.Profile,
            signature.KeyIri,
            signature.CreatedAt,
            signature.ReplayFingerprint,
            signature.NonceHash,
            now);
        InboxAcceptance result = await inboxRepository.AcceptAsync(verified, cancellationToken).ConfigureAwait(false);
        instrumentation.InboxAccepted(result.Status);
        return result;
    }

    private static IReadOnlyList<AudienceAddress> InferRouteRecipientForTargetedActivity(
        ActivityStreamsDocument document,
        string? requiredLocalActorIri)
    {
        if (requiredLocalActorIri is null || document.Audience.Count != 0)
        {
            return document.Audience;
        }

        if (document.PrimaryType == "Follow" &&
            string.Equals(document.ObjectIri, requiredLocalActorIri, StringComparison.Ordinal))
        {
            // Mastodon and Misskey omit to/cc from Follow. The Follow object is
            // the addressed actor, so a user-inbox route can verify it directly.
            return [new AudienceAddress(requiredLocalActorIri, AudienceField.To)];
        }

        if (document.PrimaryType is not ("Accept" or "Reject") ||
            !document.Root.TryGetProperty("object", out JsonElement follow) ||
            follow.ValueKind != JsonValueKind.Object ||
            !ActivityStreamsParser.ReadTypes(follow).Contains("Follow", StringComparer.Ordinal) ||
            !TryReadIri(follow, "actor", out string? follower) ||
            !TryReadIri(follow, "object", out string? followed) ||
            !string.Equals(follower, requiredLocalActorIri, StringComparison.Ordinal) ||
            !string.Equals(followed, document.ActorIri, StringComparison.Ordinal))
        {
            return document.Audience;
        }

        // Mastodon deliberately omits to/cc from AcceptFollow and RejectFollow.
        // The embedded Follow is still an unambiguous, ownership-checked recipient
        // proof: its actor is the local inbox owner and its object is the signer.
        return [new AudienceAddress(requiredLocalActorIri, AudienceField.To)];
    }

    private static bool TryReadIri(JsonElement owner, string propertyName, out string? iri)
    {
        iri = null;
        if (!owner.TryGetProperty(propertyName, out JsonElement value))
        {
            return false;
        }

        iri = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String => id.GetString(),
            JsonValueKind.Object when value.TryGetProperty("href", out JsonElement href) && href.ValueKind == JsonValueKind.String => href.GetString(),
            _ => null
        };
        return Uri.TryCreate(iri, UriKind.Absolute, out Uri? parsed) && parsed.Scheme is "http" or "https";
    }

    internal static void ValidateContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) || !MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed))
        {
            throw new BadHttpRequestException("Inbox Content-Type is missing or malformed.", StatusCodes.Status415UnsupportedMediaType);
        }

        if (string.Equals(parsed.MediaType, ActivityStreamsConstants.ActivityJson, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(parsed.MediaType, "application/ld+json", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadHttpRequestException("Inbox Content-Type is not ActivityStreams JSON.", StatusCodes.Status415UnsupportedMediaType);
        }

        NameValueHeaderValue? profile = parsed.Parameters.FirstOrDefault(x => string.Equals(x.Name, "profile", StringComparison.OrdinalIgnoreCase));
        string? value = profile?.Value?.Trim('"');
        if (!string.Equals(value, ActivityStreamsConstants.ActivityStreamsContext, StringComparison.Ordinal))
        {
            throw new BadHttpRequestException("application/ld+json inbox requests require the ActivityStreams profile.", StatusCodes.Status415UnsupportedMediaType);
        }
    }

    private static async Task<byte[]> ReadBodyAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1_024));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1_024);
        try
        {
            int total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > maximumBytes)
                {
                    throw new BadHttpRequestException("Inbox body exceeds the configured limit.", StatusCodes.Status413PayloadTooLarge);
                }

                output.Write(buffer, 0, read);
            }

            if (total == 0)
            {
                throw new BadHttpRequestException("Inbox body is empty.", StatusCodes.Status400BadRequest);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
