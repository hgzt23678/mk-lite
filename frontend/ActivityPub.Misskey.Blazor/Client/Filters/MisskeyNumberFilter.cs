using System.Globalization;

namespace ActivityPub.Misskey.Blazor.Client.Filters;

public static class MisskeyNumberFilter
{
    public static string Format(long? value, CultureInfo? culture = null) =>
        value is null ? "N/A" : value.Value.ToString("N0", culture ?? CultureInfo.InvariantCulture);
}
