using System.Globalization;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.Federation.Protocol;

public static class ActivityStreamsParser
{
    private static readonly (string Name, AudienceField Field)[] AudienceProperties =
    [
        ("to", AudienceField.To),
        ("cc", AudienceField.Cc),
        ("bto", AudienceField.Bto),
        ("bcc", AudienceField.Bcc),
        ("audience", AudienceField.Audience)
    ];

    public static ActivityStreamsDocument ParseActivity(byte[] rawBody)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        if (rawBody.Length == 0)
        {
            throw new ActivityStreamsProtocolException("Activity body is empty.");
        }

        JsonSafetyValidator.Validate(rawBody);
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawBody, new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ActivityStreamsProtocolException("Activity body must be a JSON object.");
            }

            string id = ReadRequiredIri(root, "id");
            string actor = ReadRequiredIri(root, "actor");
            IReadOnlyList<string> types = ReadTypes(root);
            string primaryType = types.FirstOrDefault(ActivityStreamsConstants.SupportedActivities.Contains) ?? types[0];
            (string? objectIri, string? ownerIri) = ReadObjectIdentity(root);
            IReadOnlyList<AudienceAddress> audience = ReadActivityAudience(root);
            Visibility visibility = NormalizeVisibility(actor, audience);
            DateTimeOffset? published = ReadTimestamp(root, "published") ?? ReadTimestamp(root, "updated");

            ValidateOriginOwnership(id, actor);
            ValidateMutationOwnership(primaryType, actor, objectIri, ownerIri);

            return new(
                id,
                actor,
                types,
                primaryType,
                objectIri,
                ownerIri,
                new Uri(actor).GetLeftPart(UriPartial.Authority),
                audience,
                visibility,
                published,
                ActivityStreamsConstants.SupportedActivities.Contains(primaryType),
                root.Clone(),
                rawBody.ToArray());
        }
        catch (JsonException exception)
        {
            throw new ActivityStreamsProtocolException("ActivityStreams JSON is malformed.", exception);
        }
        catch (DomainException exception)
        {
            throw new ActivityStreamsProtocolException(exception.Message, exception);
        }
    }

    public static IReadOnlyList<string> ReadTypes(JsonElement value)
    {
        if (!value.TryGetProperty("type", out JsonElement type))
        {
            throw new ActivityStreamsProtocolException("Activity type is required.");
        }

        List<string> result = ReadStringOrArray(type, "type", allowObjectLinks: false);
        if (result.Count == 0)
        {
            throw new ActivityStreamsProtocolException("Activity type must contain at least one value.");
        }

        return result;
    }

    public static IReadOnlyList<AudienceAddress> ReadAudience(JsonElement root)
    {
        var result = new List<AudienceAddress>();
        foreach ((string property, AudienceField field) in AudienceProperties)
        {
            if (!root.TryGetProperty(property, out JsonElement value))
            {
                continue;
            }

            foreach (string iri in ReadIriOrArray(value, property))
            {
                result.Add(new(iri, field));
            }
        }

        return result.Distinct().ToArray();
    }

    private static IReadOnlyList<AudienceAddress> ReadActivityAudience(JsonElement root)
    {
        var result = new List<AudienceAddress>(ReadAudience(root));
        if (!root.TryGetProperty("object", out JsonElement value))
        {
            return result;
        }

        AppendEmbeddedObjectAudience(value, result);
        return result.Distinct().ToArray();
    }

    private static void AppendEmbeddedObjectAudience(
        JsonElement value,
        List<AudienceAddress> result)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (AudienceAddress address in ReadAudience(value))
            {
                result.Add(address);
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                foreach (AudienceAddress address in ReadAudience(item))
                {
                    result.Add(address);
                }
            }
        }
    }

    public static Visibility NormalizeVisibility(string actorIri, IReadOnlyList<AudienceAddress> audience)
    {
        bool publicInTo = audience.Any(x => x.Field == AudienceField.To && x.Iri == ActivityStreamsConstants.PublicAudience);
        bool publicElsewhere = audience.Any(x => x.Iri == ActivityStreamsConstants.PublicAudience);
        if (publicInTo)
        {
            return Visibility.Public;
        }

        if (publicElsewhere)
        {
            return Visibility.Unlisted;
        }

        string followers = actorIri.TrimEnd('/') + "/followers";
        return audience.Any(x => string.Equals(x.Iri.TrimEnd('/'), followers, StringComparison.Ordinal))
            ? Visibility.FollowersOnly
            : Visibility.MentionedOnly;
    }

    private static string ReadRequiredIri(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
        {
            throw new ActivityStreamsProtocolException($"Activity {property} is required.");
        }

        List<string> values = ReadIriOrArray(value, property);
        if (values.Count != 1)
        {
            throw new ActivityStreamsProtocolException($"Activity {property} must resolve to exactly one IRI.");
        }

        return values[0];
    }

    private static (string? Iri, string? OwnerIri) ReadObjectIdentity(JsonElement root)
    {
        if (!root.TryGetProperty("object", out JsonElement value))
        {
            return (null, null);
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return (CanonicalIri.RequireAbsoluteHttp(value.GetString()!, "object"), null);
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            JsonElement first = value.EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.Undefined ? (null, null) : ReadObjectValue(first);
        }

        return ReadObjectValue(value);
    }

    private static (string? Iri, string? OwnerIri) ReadObjectValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return (CanonicalIri.RequireAbsoluteHttp(value.GetString()!, "object"), null);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ActivityStreamsProtocolException("Activity object must be an IRI, embedded object, Link, or array.");
        }

        string? iri = TryReadSingleIri(value, "id") ?? TryReadSingleIri(value, "href");
        string? owner = TryReadSingleIri(value, "attributedTo") ?? TryReadSingleIri(value, "actor");
        return (iri, owner);
    }

    private static string? TryReadSingleIri(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        List<string> values = ReadIriOrArray(value, property);
        return values.Count == 1 ? values[0] : null;
    }

    private static List<string> ReadIriOrArray(JsonElement value, string property)
    {
        List<string> raw = ReadStringOrArray(value, property, allowObjectLinks: true);
        var result = new List<string>(raw.Count);
        foreach (string candidate in raw)
        {
            result.Add(candidate == ActivityStreamsConstants.PublicAudience
                ? candidate
                : CanonicalIri.RequireAbsoluteHttp(candidate, property));
        }

        return result;
    }

    private static List<string> ReadStringOrArray(JsonElement value, string property, bool allowObjectLinks)
    {
        var result = new List<string>();
        Append(value, result, property, allowObjectLinks);
        return result;
    }

    private static void Append(JsonElement value, ICollection<string> output, string property, bool allowObjectLinks)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                output.Add(DomainText.Required(value.GetString()!, property, 2_048));
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Append(item, output, property, allowObjectLinks);
                }

                break;
            case JsonValueKind.Object when allowObjectLinks:
                string? iri = TryReadSingleIri(value, "id") ?? TryReadSingleIri(value, "href");
                if (iri is null)
                {
                    throw new ActivityStreamsProtocolException($"{property} object has neither id nor href.");
                }

                output.Add(iri);
                break;
            case JsonValueKind.Null:
                break;
            default:
                throw new ActivityStreamsProtocolException($"{property} must be a string, array, or link object.");
        }
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp))
        {
            throw new ActivityStreamsProtocolException($"{property} is not a valid timestamp.");
        }

        return timestamp.ToUniversalTime();
    }

    private static void ValidateOriginOwnership(string activityIri, string actorIri)
    {
        Uri activity = new(activityIri);
        Uri actor = new(actorIri);
        if (!string.Equals(activity.Scheme, actor.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(activity.IdnHost, actor.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            activity.Port != actor.Port)
        {
            throw new ActivityStreamsProtocolException("Activity id and actor must have the same origin.");
        }
    }

    private static void ValidateMutationOwnership(string activityType, string actorIri, string? objectIri, string? objectOwnerIri)
    {
        if (activityType is not ("Create" or "Update" or "Delete" or "Move") || objectIri is null)
        {
            return;
        }

        if (objectOwnerIri is not null && !string.Equals(actorIri, objectOwnerIri, StringComparison.Ordinal))
        {
            throw new ActivityStreamsProtocolException($"{activityType} actor does not own the embedded object.");
        }

        Uri actor = new(actorIri);
        Uri target = new(objectIri);
        if (!string.Equals(actor.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actor.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase) || actor.Port != target.Port)
        {
            throw new ActivityStreamsProtocolException($"{activityType} cannot mutate an object from another origin.");
        }
    }
}
