namespace ActivityPub.Misskey.Blazor.BrowserInterop;

/// <summary>
/// Provides the small browser boundary needed by the v12 theme settings page.
/// Device color-scheme state stays in the browser; it is never inferred from the
/// request host or persisted in the server circuit.
/// </summary>
public interface IThemeDeviceInterop
{
    ValueTask<bool> PrefersDarkAsync(CancellationToken cancellationToken = default);
}
