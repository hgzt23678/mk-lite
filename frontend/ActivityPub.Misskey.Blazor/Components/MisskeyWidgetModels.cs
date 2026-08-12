using System.Text.Json;

namespace ActivityPub.Misskey.Blazor.Components;

public sealed record MisskeyWidgetModel
{
    public string Name { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string? Place { get; init; }

    public Dictionary<string, JsonElement> Data { get; init; } = new(StringComparer.Ordinal);
}

public sealed record MisskeyWidgetUpdate(
    string Id,
    IReadOnlyDictionary<string, JsonElement> Data);

internal sealed record MisskeyWidgetTimezone(string Name, string Abbreviation, int OffsetMinutes)
{
    public static readonly IReadOnlyList<MisskeyWidgetTimezone> All =
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

    public static MisskeyWidgetTimezone? Find(string? name) => string.IsNullOrWhiteSpace(name)
        ? FindLocal()
        : All.FirstOrDefault(candidate => string.Equals(
            candidate.Name,
            name,
            StringComparison.OrdinalIgnoreCase));

    private static MisskeyWidgetTimezone? FindLocal() => All.FirstOrDefault(candidate => string.Equals(
        candidate.Name,
        TimeZoneInfo.Local.Id,
        StringComparison.OrdinalIgnoreCase));
}

internal static class MisskeyWidgetData
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool Boolean(MisskeyWidgetModel widget, string property, bool fallback) =>
        widget.Data.TryGetValue(property, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    public static double Number(MisskeyWidgetModel widget, string property, double fallback) =>
        widget.Data.TryGetValue(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double number) &&
        double.IsFinite(number)
            ? number
            : fallback;

    public static string? String(MisskeyWidgetModel widget, string property, string? fallback) =>
        widget.Data.TryGetValue(property, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null => null,
                _ => fallback
            }
            : fallback;

    public static Dictionary<string, JsonElement> Merge(
        MisskeyWidgetModel widget,
        IReadOnlyDictionary<string, object?> values)
    {
        var data = new Dictionary<string, JsonElement>(widget.Data, StringComparer.Ordinal);
        foreach ((string key, object? value) in values)
        {
            data[key] = JsonSerializer.SerializeToElement(value, JsonOptions);
        }

        return data;
    }
}
