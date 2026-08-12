using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.MisskeyApi;

public sealed record MisskeyRegistrationPolicy(
    bool Enabled,
    bool EmailRequired,
    bool EmailEnabled = false,
    bool OpenRegistration = true,
    bool InvitationRequired = false,
    string CaptchaProvider = "None",
    string CaptchaSiteKey = "");

public sealed class MisskeyMetadataService(FederationOptions options, MisskeyRegistrationPolicy registration)
{
    private static readonly string[] SupportedLanguages = ["ja-JP", "en-US"];

    public MisskeyInstanceMetadata GetMetadata() => new()
    {
        Version = "12.119.2-activitypub-dotnet",
        Name = options.PublicBaseUri.IdnHost,
        ShortName = options.PublicBaseUri.IdnHost,
        Uri = options.PublicBaseUri.AbsoluteUri.TrimEnd('/'),
        Description = "ActivityPub .NET federated server",
        Langs = SupportedLanguages,
        DisableRegistration = !registration.OpenRegistration,
        DisableLocalTimeline = false,
        DisableGlobalTimeline = false,
        DriveCapacityPerLocalUserMb = 0,
        DriveCapacityPerRemoteUserMb = 0,
        EmailRequiredForSignup = registration.EmailRequired,
        EnableEmail = registration.EmailEnabled,
        EnableHcaptcha = string.Equals(registration.CaptchaProvider, "Hcaptcha", StringComparison.Ordinal),
        EnableRecaptcha = string.Equals(registration.CaptchaProvider, "Recaptcha", StringComparison.Ordinal),
        HcaptchaSiteKey = string.Equals(registration.CaptchaProvider, "Hcaptcha", StringComparison.Ordinal)
            ? registration.CaptchaSiteKey
            : null,
        RecaptchaSiteKey = string.Equals(registration.CaptchaProvider, "Recaptcha", StringComparison.Ordinal)
            ? registration.CaptchaSiteKey
            : null,
        EnableTurnstile = false,
        EnableServiceWorker = true,
        TranslatorAvailable = false,
        ThemeColor = "#86b300",
        IconUrl = "/static-assets/favicon.png",
        MaxNoteTextLength = 5_000,
        Emojis = [],
        Ads = []
    };
}

public sealed class MisskeyInstanceMetadata
{
    public string? MaintainerName { get; init; }
    public string? MaintainerEmail { get; init; }
    public required string Version { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required string Uri { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Langs { get; init; }
    public string? TosUrl { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? FeedbackUrl { get; init; }
    public required bool DisableRegistration { get; init; }
    public required bool DisableLocalTimeline { get; init; }
    public required bool DisableGlobalTimeline { get; init; }
    public required long DriveCapacityPerLocalUserMb { get; init; }
    public required long DriveCapacityPerRemoteUserMb { get; init; }
    public required bool EmailRequiredForSignup { get; init; }
    public required bool EnableEmail { get; init; }
    public required bool EnableHcaptcha { get; init; }
    public required bool EnableRecaptcha { get; init; }
    public string? HcaptchaSiteKey { get; init; }
    public string? RecaptchaSiteKey { get; init; }
    public required bool EnableTurnstile { get; init; }
    public required bool EnableServiceWorker { get; init; }
    public required bool TranslatorAvailable { get; init; }
    public string? ProxyAccountName { get; init; }
    public required string ThemeColor { get; init; }
    public required string IconUrl { get; init; }
    public string? BackgroundImageUrl { get; init; }
    public string? LogoImageUrl { get; init; }
    public required int MaxNoteTextLength { get; init; }
    public required IReadOnlyList<object> Emojis { get; init; }
    public required IReadOnlyList<object> Ads { get; init; }
    public string? DefaultDarkTheme { get; init; }
    public string? DefaultLightTheme { get; init; }
}
