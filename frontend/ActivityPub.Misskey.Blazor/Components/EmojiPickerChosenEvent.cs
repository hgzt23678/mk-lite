using Microsoft.AspNetCore.Components.Web;

namespace ActivityPub.Misskey.Blazor.Components;

public sealed record EmojiPickerChosenEvent(string Value, MouseEventArgs Event);
