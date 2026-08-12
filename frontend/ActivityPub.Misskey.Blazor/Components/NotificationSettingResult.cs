using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Components;

public sealed record NotificationSettingResult(
    IReadOnlySet<MisskeyNotificationType>? IncludingTypes);
