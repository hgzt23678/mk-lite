namespace ActivityPub.Misskey.Blazor.Components;

using ActivityPub.Misskey.Blazor.Presentation;

public sealed record MisskeyPagePreviewViewModel(
    string Name,
    string Title,
    string? Summary,
    NoteAuthorViewModel User,
    string? EyeCatchingImageThumbnailUrl);
