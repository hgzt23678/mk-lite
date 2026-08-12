namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record MisskeyFrontendRuntimeConfiguration(
    string Version,
    Uri? SourceUrl,
    Uri? PublicBaseUri = null,
    bool LocalAccountsEnabled = false)
{
    public const string PortVersion = "12.119.2-port.1";

    public static MisskeyFrontendRuntimeConfiguration Default { get; } = new(PortVersion, null);
}
