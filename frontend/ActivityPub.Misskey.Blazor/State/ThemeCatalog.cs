using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActivityPub.Misskey.Blazor.State;

public sealed record ThemeDefinition(
    string Id,
    string Name,
    string? Author,
    string? Description,
    string Base,
    bool Selectable,
    string SourceFile,
    string SourceSha256,
    IReadOnlyDictionary<string, string> Properties);

public interface IThemeCatalog
{
    IReadOnlyList<ThemeDefinition> Themes { get; }

    ThemeDefinition GetRequired(string id);
}

public sealed partial class ThemeCatalog : IThemeCatalog
{
    private const string ResourceName = "ActivityPub.Misskey.Blazor.ThemeCatalog.json";
    private readonly ReadOnlyDictionary<string, ThemeDefinition> themesById;

    public ThemeCatalog()
    {
        Assembly assembly = typeof(ThemeCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded theme resource {ResourceName} is missing.");
        ThemeCatalogDocument document = JsonSerializer.Deserialize(
                stream,
                ThemeCatalogJsonContext.Default.ThemeCatalogDocument)
            ?? throw new InvalidOperationException("The embedded theme catalog is empty.");

        if (document.SchemaVersion != 1 || !string.Equals(document.MisskeyVersion, "12.119.2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The embedded theme catalog has an unsupported schema or Misskey version.");
        }

        var validated = new Dictionary<string, ThemeDefinition>(StringComparer.Ordinal);
        foreach (ThemeDefinition theme in document.Themes)
        {
            Validate(theme);
            if (!validated.TryAdd(theme.Id, theme))
            {
                throw new InvalidOperationException($"The embedded theme catalog contains duplicate id {theme.Id}.");
            }
        }

        if (validated.Count != 20)
        {
            throw new InvalidOperationException($"Expected 20 Misskey 12.119.2 themes, but found {validated.Count}.");
        }

        themesById = new ReadOnlyDictionary<string, ThemeDefinition>(validated);
        Themes = Array.AsReadOnly(document.Themes);
    }

    public IReadOnlyList<ThemeDefinition> Themes { get; }

    public ThemeDefinition GetRequired(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return themesById.TryGetValue(id, out ThemeDefinition? theme)
            ? theme
            : throw new KeyNotFoundException($"Unknown Misskey theme id {id}.");
    }

    private static void Validate(ThemeDefinition theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Id) || string.IsNullOrWhiteSpace(theme.Name) ||
            (theme.Base is not "light" and not "dark") ||
            theme.Properties.Count == 0 ||
            !theme.Properties.ContainsKey("bg") ||
            !theme.Properties.ContainsKey("panel") ||
            !theme.Properties.ContainsKey("popup"))
        {
            throw new InvalidOperationException($"Theme {theme.Id} is incomplete.");
        }
    }

    private sealed record ThemeCatalogDocument(
        int SchemaVersion,
        string MisskeyVersion,
        string UpstreamCommit,
        ThemeDefinition[] Themes);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ThemeCatalogDocument))]
    private sealed partial class ThemeCatalogJsonContext : JsonSerializerContext;
}
