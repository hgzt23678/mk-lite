using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActivityPub.Misskey.Blazor.State;

public sealed record UnicodeEmojiDefinition(
    string Category,
    [property: JsonPropertyName("char")] string Value,
    string Name,
    IReadOnlyList<string> Keywords);

public interface IEmojiCatalog
{
    IReadOnlyList<UnicodeEmojiDefinition> Emojis { get; }

    IReadOnlyList<string> Categories { get; }
}

public sealed partial class EmojiCatalog : IEmojiCatalog
{
    private const string ResourceName = "ActivityPub.Misskey.Blazor.EmojiList.json";
    private static readonly string[] ExpectedCategories =
    [
        "face",
        "people",
        "animals_and_nature",
        "food_and_drink",
        "activity",
        "travel_and_places",
        "objects",
        "symbols",
        "flags"
    ];

    public EmojiCatalog()
    {
        Assembly assembly = typeof(EmojiCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded emoji resource {ResourceName} is missing.");
        UnicodeEmojiDefinition[] values = JsonSerializer.Deserialize(
                stream,
                EmojiCatalogJsonContext.Default.UnicodeEmojiDefinitionArray)
            ?? throw new InvalidOperationException("The embedded Misskey emoji catalog is empty.");
        if (values.Length != 1_782 || values.Any(value =>
                string.IsNullOrWhiteSpace(value.Category) ||
                string.IsNullOrWhiteSpace(value.Value) ||
                string.IsNullOrWhiteSpace(value.Name)))
        {
            throw new InvalidOperationException("The embedded Misskey 12.119.2 emoji catalog is incomplete.");
        }

        string[] categories = values.Select(value => value.Category).Distinct(StringComparer.Ordinal).ToArray();
        if (!categories.SequenceEqual(ExpectedCategories, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The embedded Misskey emoji category order changed.");
        }

        Emojis = Array.AsReadOnly(values);
        Categories = Array.AsReadOnly(categories);
    }

    public IReadOnlyList<UnicodeEmojiDefinition> Emojis { get; }

    public IReadOnlyList<string> Categories { get; }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(UnicodeEmojiDefinition[]))]
    private sealed partial class EmojiCatalogJsonContext : JsonSerializerContext;
}
