namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record VisitorAnnouncementViewModel(
    string Id,
    string Title,
    string Text,
    string? ImageUrl);
