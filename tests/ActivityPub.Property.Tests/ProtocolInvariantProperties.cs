using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActivityPub.Federation.Protocol;
using FsCheck;
using FsCheck.Xunit;

namespace ActivityPub.InvariantTests;

public sealed class ProtocolInvariantProperties
{
    private static readonly string[] CreateType = ["Create"];

    [Property(MaxTest = 500)]
    public bool ScalarAndSingletonArrayAudienceNormalizeIdentically(PositiveInt sequence)
    {
        string actor = $"https://remote.example/users/u{sequence.Get}";
        string recipient = $"https://local.example/users/u{sequence.Get}";
        byte[] scalar = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = $"https://remote.example/activities/scalar-{sequence.Get}",
            type = "Create",
            actor,
            @object = $"https://remote.example/objects/{sequence.Get}",
            to = recipient
        });
        byte[] array = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = $"https://remote.example/activities/array-{sequence.Get}",
            type = CreateType,
            actor,
            @object = new[] { $"https://remote.example/objects/{sequence.Get}" },
            to = new[] { recipient }
        });

        ActivityStreamsDocument scalarDocument = ActivityStreamsParser.ParseActivity(scalar);
        ActivityStreamsDocument arrayDocument = ActivityStreamsParser.ParseActivity(array);

        return scalarDocument.PrimaryType == arrayDocument.PrimaryType &&
               scalarDocument.ObjectIri == arrayDocument.ObjectIri &&
               scalarDocument.Visibility == arrayDocument.Visibility &&
               scalarDocument.Audience.SequenceEqual(arrayDocument.Audience);
    }

    [Property(MaxTest = 500)]
    public bool BlindRecipientsAreRemovedAtEveryGeneratedDepth(PositiveInt generatedDepth)
    {
        int depth = generatedDepth.Get % 32;
        var root = new JsonObject();
        JsonObject current = root;
        for (int index = 0; index <= depth; index++)
        {
            current["bto"] = $"https://remote.example/users/blind-{index}";
            current["bcc"] = new JsonArray($"https://remote.example/users/secret-{index}");
            if (index != depth)
            {
                var next = new JsonObject();
                current["object"] = new JsonArray(next);
                current = next;
            }
        }

        using JsonDocument input = JsonDocument.Parse(root.ToJsonString());
        byte[] serialized = ActivityStreamsSerializer.StripBlindRecipientsAndSerialize(input.RootElement);
        using JsonDocument output = JsonDocument.Parse(serialized);
        return ContainsNoBlindRecipients(output.RootElement);
    }

    [Property(MaxTest = 500)]
    public bool SanitizerNeverRetainsInjectedExecutableMarkup(NonNull<string> text)
    {
        var sanitizer = new IncomingHtmlSanitizer();
        string safeText = WebUtility.HtmlEncode(text.Get);
        string result = sanitizer.Sanitize(
            $"<p onload=alert(1)>{safeText}<script>alert(2)</script><a href=\"javascript:alert(3)\">x</a></p>");

        return !result.Contains("<script", StringComparison.OrdinalIgnoreCase) &&
               !result.Contains("onload=", StringComparison.OrdinalIgnoreCase) &&
               !result.Contains("javascript:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsNoBlindRecipients(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name is "bto" or "bcc" || !ContainsNoBlindRecipients(property.Value))
                {
                    return false;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (!ContainsNoBlindRecipients(item))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
