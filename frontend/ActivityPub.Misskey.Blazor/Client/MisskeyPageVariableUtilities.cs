using System.Text.Json;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed record MisskeyPageVariable(string Name, string Type, object? Value);

public static class MisskeyPageVariableUtilities
{
    public static IReadOnlyList<MisskeyPageVariable> Collect(JsonElement content)
    {
        List<MisskeyPageVariable> result = [];
        CollectChildren(content, result);
        return result;
    }

    private static void CollectChildren(JsonElement value, List<MisskeyPageVariable> result)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string type = ReadString(item, "type") ?? string.Empty;
            string? name = ReadString(item, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                switch (type)
                {
                    case "textInput":
                    case "textareaInput":
                    case "radioButton":
                        result.Add(new(name, "string", ReadString(item, "default") ?? string.Empty));
                        break;
                    case "numberInput":
                        result.Add(new(name, "number", ReadNumber(item, "default") ?? 0));
                        break;
                    case "switch":
                        result.Add(new(name, "boolean", ReadBoolean(item, "default") ?? false));
                        break;
                    case "counter":
                        result.Add(new(name, "number", 0));
                        break;
                }
            }

            if (item.TryGetProperty("children", out JsonElement children))
            {
                CollectChildren(children, result);
            }
        }
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadNumber(JsonElement item, string property) =>
        item.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double result)
            ? result
            : null;

    private static bool? ReadBoolean(JsonElement item, string property) =>
        item.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
