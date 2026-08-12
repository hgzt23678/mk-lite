using System.Text.Json;
using ActivityPub.Domain;

namespace ActivityPub.Federation.Protocol;

public static class ActivityReactionParser
{
    private static readonly string[] ReactionProperties = ["_misskey_reaction", "content", "name"];

    public static FederatedReaction Parse(JsonElement activity, string actorIri)
    {
        string? value = null;
        foreach (string property in ReactionProperties)
        {
            if (activity.TryGetProperty(property, out JsonElement candidate) && candidate.ValueKind == JsonValueKind.String)
            {
                value = candidate.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    break;
                }
            }
        }

        FederatedReaction reaction = FederatedReaction.Create(value, actorIri);
        if (!reaction.IsCustomEmoji || !activity.TryGetProperty("tag", out JsonElement tags))
        {
            return reaction;
        }

        string shortcode = reaction.Value[1..^1].Split('@', 2)[0];
        foreach (JsonElement tag in EnumerateValues(tags))
        {
            if (tag.ValueKind != JsonValueKind.Object || !HasType(tag, "Emoji"))
            {
                continue;
            }

            string? name = ReadString(tag, "name");
            if (!string.Equals(name, $":{shortcode}:", StringComparison.Ordinal))
            {
                continue;
            }

            string? iri = ReadString(tag, "id");
            string? url = null;
            string? mediaType = null;
            if (tag.TryGetProperty("icon", out JsonElement icon))
            {
                JsonElement? iconObject = icon.ValueKind == JsonValueKind.Array
                    ? EnumerateValues(icon).FirstOrDefault(x => x.ValueKind == JsonValueKind.Object)
                    : icon.ValueKind == JsonValueKind.Object ? icon : null;
                if (iconObject is { } parsedIcon)
                {
                    url = ReadIriLike(parsedIcon, "url");
                    mediaType = ReadString(parsedIcon, "mediaType");
                }
            }

            string canonicalValue = QualifyCustomEmoji(value, reaction.Value, iri, url);
            return FederatedReaction.Create(canonicalValue, actorIri, iri, name, url, mediaType);
        }

        return reaction;
    }

    private static IEnumerable<JsonElement> EnumerateValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                yield return item;
            }
        }
        else
        {
            yield return value;
        }
    }

    private static bool HasType(JsonElement value, string expected)
    {
        if (!value.TryGetProperty("type", out JsonElement type))
        {
            return false;
        }

        return EnumerateValues(type).Any(candidate =>
            candidate.ValueKind == JsonValueKind.String && string.Equals(candidate.GetString(), expected, StringComparison.Ordinal));
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out JsonElement candidate) && candidate.ValueKind == JsonValueKind.String
            ? candidate.GetString()
            : null;

    private static string? ReadIriLike(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out JsonElement candidate))
        {
            return null;
        }

        if (candidate.ValueKind == JsonValueKind.String)
        {
            return candidate.GetString();
        }

        foreach (JsonElement item in EnumerateValues(candidate))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? iri = ReadString(item, "href") ?? ReadString(item, "url") ?? ReadString(item, "id");
            if (iri is not null)
            {
                return iri;
            }
        }

        return null;
    }

    private static string QualifyCustomEmoji(
        string? wireValue,
        string normalizedValue,
        string? emojiIri,
        string? emojiUrl)
    {
        string candidate = wireValue?.Trim() ?? string.Empty;
        if (candidate.Length < 3 || candidate[0] != ':' || candidate[^1] != ':' ||
            candidate[1..^1].Contains('@', StringComparison.Ordinal))
        {
            return normalizedValue;
        }

        string? metadataIri = emojiIri ?? emojiUrl;
        if (!Uri.TryCreate(metadataIri, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || uri.IdnHost.Length == 0)
        {
            return normalizedValue;
        }

        return $":{candidate[1..^1]}@{uri.IdnHost.ToLowerInvariant()}:";
    }
}
