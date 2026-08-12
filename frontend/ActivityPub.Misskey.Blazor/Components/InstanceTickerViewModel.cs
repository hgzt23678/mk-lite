namespace ActivityPub.Misskey.Blazor.Components;

public sealed record InstanceTickerViewModel(
    string Name,
    string? FaviconUrl = null,
    string? ThemeColor = null);
