using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;

namespace ActivityPub.Misskey.Blazor.Client;

public static class MisskeyMfmUtilities
{
    public static IReadOnlyList<JsonElement> ExtractMentions(IEnumerable<MfmNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        List<JsonElement> result = [];
        Walk(nodes, node =>
        {
            if (string.Equals(node.Type, "mention", StringComparison.Ordinal))
            {
                result.Add(node.Props.Clone());
            }
        });
        return result;
    }

    public static IReadOnlyList<string> ExtractUrls(IEnumerable<MfmNode> nodes, bool respectSilentFlag = true)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        List<string> result = [];
        HashSet<string> withoutHash = new(StringComparer.Ordinal);
        Walk(nodes, node =>
        {
            bool candidate = string.Equals(node.Type, "url", StringComparison.Ordinal) ||
                (string.Equals(node.Type, "link", StringComparison.Ordinal) &&
                 (!respectSilentFlag || !ReadBoolean(node.Props, "silent")));
            if (!candidate || !TryReadString(node.Props, "url", out string? url) || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            string normalized = RemoveHash(url);
            if (withoutHash.Add(normalized))
            {
                result.Add(url);
            }
        });
        return result;
    }

    private static void Walk(IEnumerable<MfmNode> nodes, Action<MfmNode> visitor)
    {
        foreach (MfmNode node in nodes)
        {
            visitor(node);
            if (node.Children is { Count: > 0 })
            {
                Walk(node.Children, visitor);
            }
        }
    }

    private static string RemoveHash(string value)
    {
        int index = value.LastIndexOf('#');
        return index >= 0 ? value[..index] : value;
    }

    private static bool ReadBoolean(JsonElement props, string name) =>
        props.ValueKind == JsonValueKind.Object &&
        props.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.True;

    private static bool TryReadString(JsonElement props, string name, out string? value)
    {
        if (props.ValueKind == JsonValueKind.Object &&
            props.TryGetProperty(name, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }
}

public static class MisskeyMfmTags
{
    public static IReadOnlyList<string> Tags { get; } =
    [
        "tada", "jelly", "twitch", "shake", "spin", "jump", "bounce",
        "flip", "x2", "x3", "x4", "font", "blur", "rainbow", "sparkle", "rotate"
    ];
}

public sealed record MisskeyTimezone(string Name, string Abbreviation, int OffsetMinutes);

public static class MisskeyTimezones
{
    public static IReadOnlyList<MisskeyTimezone> Values { get; } =
    [
        new("UTC", "UTC", 0),
        new("Europe/Berlin", "CET", 60),
        new("Asia/Tokyo", "JST", 540),
        new("Asia/Seoul", "KST", 540),
        new("Asia/Shanghai", "CST", 480),
        new("Australia/Sydney", "AEST", 600),
        new("Australia/Darwin", "ACST", 570),
        new("Australia/Perth", "AWST", 480),
        new("America/New_York", "EST", -300),
        new("America/Mexico_City", "CST", -360),
        new("America/Phoenix", "MST", -420),
        new("America/Los_Angeles", "PST", -480)
    ];
}
