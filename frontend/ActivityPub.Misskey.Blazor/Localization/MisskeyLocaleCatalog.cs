using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;

namespace ActivityPub.Misskey.Blazor.Localization;

public sealed record MisskeyLocaleDefinition(
    string Locale,
    string LanguageName,
    string Direction,
    IReadOnlyList<string> FallbackChain);

public interface IMisskeyLocaleCatalog
{
    IReadOnlyList<MisskeyLocaleDefinition> Locales { get; }

    bool TryCanonicalize(string? candidate, out string locale);

    MisskeyLocaleDefinition GetRequiredDefinition(string locale);

    string Translate(
        string locale,
        string key,
        IReadOnlyDictionary<string, object?>? arguments = null);

    int GetTranslationCount(string locale);
}

public sealed class MisskeyLocaleCatalog : IMisskeyLocaleCatalog
{
    private const string ResourceName = "ActivityPub.Misskey.Blazor.LocaleCatalog.json";
    private const int ExpectedLocaleCount = 25;
    private const int ExpectedTranslationCount = 1632;
    private readonly ReadOnlyDictionary<string, MisskeyLocaleDefinition> definitionsByLocale;
    private readonly ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> translationsByLocale;

    public MisskeyLocaleCatalog()
    {
        Assembly assembly = typeof(MisskeyLocaleCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded locale resource {ResourceName} is missing.");
        JsonObject document = JsonNode.Parse(stream)?.AsObject()
            ?? throw new InvalidOperationException("The embedded locale catalog is empty.");
        ValidateCatalogIdentity(document);

        JsonArray definitionDocuments = document["localeDefinitions"]?.AsArray()
            ?? throw new InvalidOperationException("The embedded locale definitions are missing.");
        JsonObject rawLocales = document["rawLocales"]?.AsObject()
            ?? throw new InvalidOperationException("The embedded raw locales are missing.");
        var definitions = new Dictionary<string, MisskeyLocaleDefinition>(StringComparer.Ordinal);
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        foreach (JsonNode? definitionNode in definitionDocuments)
        {
            JsonObject definitionDocument = definitionNode?.AsObject()
                ?? throw new InvalidOperationException("The embedded locale definition is invalid.");
            string locale = RequiredString(definitionDocument, "locale");
            string languageName = RequiredString(definitionDocument, "languageName");
            string direction = RequiredString(definitionDocument, "direction");
            string[] fallbackChain = definitionDocument["fallbackChain"]?.AsArray()
                .Select(value => value?.GetValue<string>()
                    ?? throw new InvalidOperationException($"Locale {locale} has an invalid fallback layer."))
                .ToArray()
                ?? throw new InvalidOperationException($"Locale {locale} has no fallback chain.");
            ValidateDefinition(locale, languageName, direction, fallbackChain, rawLocales);

            var definition = new MisskeyLocaleDefinition(
                locale,
                languageName,
                direction,
                Array.AsReadOnly(fallbackChain));
            if (!definitions.TryAdd(locale, definition))
            {
                throw new InvalidOperationException($"The embedded locale catalog contains duplicate locale {locale}.");
            }

            JsonObject merged = Merge(fallbackChain.Select(layer => rawLocales[layer]!.AsObject()));
            var flattened = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(merged, null, flattened);
            if (flattened.Count != ExpectedTranslationCount)
            {
                throw new InvalidOperationException(
                    $"Locale {locale} has {flattened.Count} effective translations instead of {ExpectedTranslationCount}.");
            }

            translations.Add(locale, new ReadOnlyDictionary<string, string>(flattened));
        }

        if (definitions.Count != ExpectedLocaleCount || rawLocales.Count != ExpectedLocaleCount)
        {
            throw new InvalidOperationException(
                $"Expected {ExpectedLocaleCount} Misskey 12.119.2 locales, but found {definitions.Count} definitions and {rawLocales.Count} sources.");
        }

        definitionsByLocale = new ReadOnlyDictionary<string, MisskeyLocaleDefinition>(definitions);
        translationsByLocale = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(translations);
        Locales = Array.AsReadOnly(definitionDocuments
            .Select(definition => definitions[RequiredString(definition!.AsObject(), "locale")])
            .ToArray());
    }

    public IReadOnlyList<MisskeyLocaleDefinition> Locales { get; }

    public bool TryCanonicalize(string? candidate, out string locale)
    {
        locale = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 35 || candidate.Any(char.IsControl))
        {
            return false;
        }

        MisskeyLocaleDefinition? definition = Locales.FirstOrDefault(
            item => string.Equals(item.Locale, candidate.Trim(), StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            return false;
        }

        locale = definition.Locale;
        return true;
    }

    public MisskeyLocaleDefinition GetRequiredDefinition(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        return definitionsByLocale.TryGetValue(locale, out MisskeyLocaleDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown Misskey locale {locale}.");
    }

    public string Translate(
        string locale,
        string key,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!translationsByLocale.TryGetValue(locale, out IReadOnlyDictionary<string, string>? translations))
        {
            throw new KeyNotFoundException($"Unknown Misskey locale {locale}.");
        }

        if (!translations.TryGetValue(key, out string? translation))
        {
            return key;
        }

        return MisskeyInterpolation.Apply(translation, arguments);
    }

    public int GetTranslationCount(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        return translationsByLocale.TryGetValue(locale, out IReadOnlyDictionary<string, string>? translations)
            ? translations.Count
            : throw new KeyNotFoundException($"Unknown Misskey locale {locale}.");
    }

    private static void ValidateCatalogIdentity(JsonObject document)
    {
        if (document["schemaVersion"]?.GetValue<int>() != 1 ||
            !string.Equals(document["misskeyVersion"]?.GetValue<string>(), "12.119.2", StringComparison.Ordinal) ||
            !string.Equals(
                document["upstreamCommit"]?.GetValue<string>(),
                "a5a74f4434b179cdb1f97af98bf294c8b18de0e2",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The embedded locale catalog has an unsupported schema or source version.");
        }
    }

    private static void ValidateDefinition(
        string locale,
        string languageName,
        string direction,
        string[] fallbackChain,
        JsonObject rawLocales)
    {
        if (string.IsNullOrWhiteSpace(languageName) || direction is not ("ltr" or "rtl") ||
            fallbackChain.Length == 0 || !string.Equals(fallbackChain[^1], locale, StringComparison.Ordinal) ||
            fallbackChain.Any(layer => !rawLocales.ContainsKey(layer)))
        {
            throw new InvalidOperationException($"Locale definition {locale} is invalid.");
        }
    }

    private static string RequiredString(JsonObject document, string property)
    {
        string? value = document[property]?.GetValue<string>();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Locale catalog property {property} is missing.");
    }

    private static JsonObject Merge(IEnumerable<JsonObject> sources)
    {
        var result = new JsonObject();
        foreach (JsonObject source in sources)
        {
            MergeInto(result, source);
        }

        return result;
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach ((string key, JsonNode? sourceValue) in source)
        {
            if (sourceValue is JsonObject sourceObject && target[key] is JsonObject targetObject)
            {
                MergeInto(targetObject, sourceObject);
            }
            else
            {
                target[key] = sourceValue?.DeepClone();
            }
        }
    }

    private static void Flatten(JsonObject source, string? prefix, IDictionary<string, string> target)
    {
        foreach ((string key, JsonNode? value) in source)
        {
            string path = prefix is null ? key : $"{prefix}.{key}";
            if (value is JsonObject child)
            {
                Flatten(child, path, target);
            }
            else if (value is JsonValue jsonValue && jsonValue.TryGetValue(out string? translation) && translation is not null)
            {
                target.Add(path, translation);
            }
            else
            {
                throw new InvalidOperationException($"Locale value {path} is not a string or an object.");
            }
        }
    }
}

internal static class MisskeyInterpolation
{
    public static string Apply(string value, IReadOnlyDictionary<string, object?>? arguments)
    {
        if (arguments is null)
        {
            return value;
        }

        string result = value;
        foreach ((string key, object? argument) in arguments)
        {
            string token = $"{{{key}}}";
            int index = result.IndexOf(token, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            string replacement = argument switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => argument.ToString() ?? string.Empty
            };
            result = string.Concat(result.AsSpan(0, index), replacement, result.AsSpan(index + token.Length));
        }

        return result;
    }
}
