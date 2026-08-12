using System.Text.Json;
using System.Text.Json.Nodes;
using ActivityPub.Application;

namespace ActivityPub.Federation.Protocol;

public static class ActivityStreamsSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static byte[] SerializeActor(ActorDocument actor, PublicIriFactory iriFactory)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(iriFactory);
        var document = new JsonObject
        {
            ["@context"] = BuildContext(),
            ["id"] = actor.Iri,
            ["type"] = actor.Kind.ToString(),
            ["preferredUsername"] = actor.Username,
            ["name"] = actor.DisplayName,
            ["summary"] = actor.SummaryHtml,
            ["inbox"] = iriFactory.ActorInbox(actor.Username),
            ["outbox"] = iriFactory.ActorOutbox(actor.Username),
            ["followers"] = iriFactory.ActorFollowers(actor.Username),
            ["following"] = iriFactory.ActorFollowing(actor.Username),
            ["liked"] = iriFactory.ActorLiked(actor.Username),
            ["featured"] = iriFactory.ActorFeatured(actor.Username),
            ["manuallyApprovesFollowers"] = actor.ManuallyApprovesFollowers,
            ["discoverable"] = actor.Discoverable,
            ["indexable"] = actor.Indexable,
            ["endpoints"] = new JsonObject { ["sharedInbox"] = new Uri(new Uri(actor.Iri), "/inbox").AbsoluteUri },
            ["publicKey"] = BuildPublicKeys(actor)
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    public static byte[] StripBlindRecipientsAndSerialize(JsonElement value)
    {
        JsonNode node = JsonNode.Parse(value.GetRawText(), documentOptions: new JsonDocumentOptions { MaxDepth = 64 })
            ?? throw new ActivityStreamsProtocolException("Activity JSON cannot be empty.");
        RemoveBlindRecipients(node);
        return JsonSerializer.SerializeToUtf8Bytes(node, JsonOptions);
    }

    private static JsonArray BuildContext() =>
    [
        ActivityStreamsConstants.ActivityStreamsContext,
        new JsonObject
        {
            ["sensitive"] = "as:sensitive",
            ["Hashtag"] = "as:Hashtag",
            ["Emoji"] = "toot:Emoji",
            ["toot"] = "http://joinmastodon.org/ns#",
            ["featured"] = new JsonObject { ["@id"] = "toot:featured", ["@type"] = "@id" },
            ["discoverable"] = "toot:discoverable",
            ["indexable"] = "toot:indexable",
            ["manuallyApprovesFollowers"] = "as:manuallyApprovesFollowers"
        }
    ];

    private static JsonNode BuildPublicKeys(ActorDocument actor)
    {
        JsonObject active = PublicKey(actor.PublicKeyIri, actor.Iri, actor.PublicKeyPem);
        if (actor.RetiredPublicKeys.Count == 0)
        {
            return active;
        }

        var keys = new JsonArray(active);
        foreach (ActorPublicKeyDocument key in actor.RetiredPublicKeys)
        {
            JsonObject value = PublicKey(key.KeyIri, actor.Iri, key.PublicKeyPem);
            value["expires"] = key.ExpiresAt;
            keys.Add(value);
        }

        return keys;
    }

    private static JsonObject PublicKey(string keyIri, string ownerIri, string publicKeyPem) => new()
    {
        ["id"] = keyIri,
        ["owner"] = ownerIri,
        ["publicKeyPem"] = publicKeyPem
    };

    private static void RemoveBlindRecipients(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("bto");
            obj.Remove("bcc");
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToArray())
            {
                if (property.Value is not null)
                {
                    RemoveBlindRecipients(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item is not null)
                {
                    RemoveBlindRecipients(item);
                }
            }
        }
    }
}
