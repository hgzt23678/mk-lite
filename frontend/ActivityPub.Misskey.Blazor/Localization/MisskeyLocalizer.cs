using System.Globalization;
using Microsoft.AspNetCore.Http;

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

public sealed class MisskeyLocalizer : IMisskeyLocalizer
{
    private readonly IMisskeyLocaleCatalog catalog;

    public MisskeyLocalizer(
        IMisskeyLocaleCatalog catalog,
        MisskeyLocaleRequestResolver requestResolver,
        IHttpContextAccessor httpContextAccessor)
    {
        this.catalog = catalog;
        CurrentLocale = requestResolver.Resolve(httpContextAccessor.HttpContext);
        Culture = CultureInfo.GetCultureInfo(CurrentLocale);
        Direction = catalog.GetRequiredDefinition(CurrentLocale).Direction;
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

        CurrentLocale = canonicalLocale;
        Culture = CultureInfo.GetCultureInfo(CurrentLocale);
        Direction = catalog.GetRequiredDefinition(CurrentLocale).Direction;
        LocaleChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
