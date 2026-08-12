namespace ActivityPub.Misskey.Blazor.Components;

public sealed record MisskeyMediaCaptionInput(string? Placeholder = null, string? Default = null);

public sealed record MisskeyMediaCaptionResult(bool Canceled, string? Result);
