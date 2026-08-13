using System.Globalization;

namespace ActivityPub.Misskey.Blazor.Localization;

public interface IMisskeyLocalizer
{
    event EventHandler? LocaleChanged;

    string CurrentLocale { get; }

    string Direction { get; }

    CultureInfo Culture { get; }

    IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales { get; }

    string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null);

    bool TrySelectLocale(string? locale);
}
