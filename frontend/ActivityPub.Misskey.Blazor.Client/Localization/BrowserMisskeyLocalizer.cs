using System.Globalization;
using ActivityPub.Misskey.Blazor.Localization;

namespace ActivityPub.Misskey.Blazor.Client.Localization;

public sealed class BrowserMisskeyLocalizer : IMisskeyLocalizer
{
    private const string DefaultLocale = "ja-JP";
    private readonly IMisskeyLocaleCatalog catalog;

    public BrowserMisskeyLocalizer(IMisskeyLocaleCatalog catalog)
    {
        this.catalog = catalog;
        CurrentLocale = ResolveInitialLocale(CultureInfo.CurrentUICulture);
        MisskeyLocaleDefinition definition = catalog.GetRequiredDefinition(CurrentLocale);
        Direction = definition.Direction;
        Culture = CultureInfo.GetCultureInfo(CurrentLocale);
    }

    public event EventHandler? LocaleChanged;

    public string CurrentLocale { get; private set; }

    public string Direction { get; private set; }

    public CultureInfo Culture { get; private set; }

    public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => catalog.Locales;

    public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
        catalog.Translate(CurrentLocale, key, arguments);

    public bool TrySelectLocale(string? locale)
    {
        if (!catalog.TryCanonicalize(locale, out string canonicalLocale))
        {
            return false;
        }

        if (string.Equals(CurrentLocale, canonicalLocale, StringComparison.Ordinal))
        {
            return true;
        }

        MisskeyLocaleDefinition definition = catalog.GetRequiredDefinition(canonicalLocale);
        CurrentLocale = canonicalLocale;
        Direction = definition.Direction;
        Culture = CultureInfo.GetCultureInfo(canonicalLocale);
        LocaleChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private string ResolveInitialLocale(CultureInfo culture)
    {
        if (catalog.TryCanonicalize(culture.Name, out string exact))
        {
            return exact;
        }

        MisskeyLocaleDefinition? languageMatch = catalog.Locales.FirstOrDefault(locale =>
            locale.Locale.StartsWith(culture.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase));
        return languageMatch?.Locale ?? DefaultLocale;
    }
}
