namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record EmojiPickerCustomEmoji(
    string Name,
    string Url,
    string? Category,
    IReadOnlyList<string> Aliases);
