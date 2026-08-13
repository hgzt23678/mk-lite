namespace ActivityPub.Identity;

public enum RegistrationCaptchaProvider
{
    None = 0,
    Hcaptcha = 1,
    Recaptcha = 2,
    Turnstile = 3
}

public sealed class RegistrationProtectionOptions
{
    public const string SectionName = "RegistrationProtection";

    public bool InvitationRequired { get; init; }

    public TimeSpan InvitationLifetime { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan InvitationReservationLifetime { get; init; } = TimeSpan.FromMinutes(5);

    public RegistrationCaptchaProvider CaptchaProvider { get; init; }

    public string CaptchaSiteKey { get; init; } = string.Empty;

    public string? CaptchaSecretFile { get; init; }

    public string CaptchaExpectedHostname { get; init; } = string.Empty;

    public string CaptchaExpectedAction { get; init; } = "signup";

    public string CaptchaExpectedCdata { get; init; } = "activitypub_signup";

    public TimeSpan CaptchaVerificationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public bool RegistrationAvailable(LocalAccountOptions accounts) =>
        accounts.Enabled && (accounts.RegistrationEnabled || InvitationRequired);

    public void Validate(LocalAccountOptions accounts, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (InvitationLifetime < TimeSpan.FromMinutes(5) || InvitationLifetime > TimeSpan.FromDays(365) ||
            InvitationReservationLifetime < TimeSpan.FromSeconds(30) ||
            InvitationReservationLifetime > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException("Registration invitation lifetimes are outside the supported range.");
        }

        if (InvitationRequired && !accounts.Enabled)
        {
            throw new InvalidOperationException("RegistrationProtection:InvitationRequired requires LocalAccounts:Enabled.");
        }

        if (!Enum.IsDefined(CaptchaProvider))
        {
            throw new InvalidOperationException("RegistrationProtection:CaptchaProvider is not supported.");
        }

        if (CaptchaProvider == RegistrationCaptchaProvider.None)
        {
            if (!string.IsNullOrWhiteSpace(CaptchaSiteKey) ||
                !string.IsNullOrWhiteSpace(CaptchaSecretFile) ||
                !string.IsNullOrWhiteSpace(CaptchaExpectedHostname))
            {
                throw new InvalidOperationException("Captcha keys require an explicit RegistrationProtection:CaptchaProvider.");
            }

            return;
        }

        if (!RegistrationAvailable(accounts))
        {
            throw new InvalidOperationException("Captcha protection requires an enabled registration path.");
        }

        if (string.IsNullOrWhiteSpace(CaptchaSiteKey) || CaptchaSiteKey.Length > 256 || CaptchaSiteKey.Any(char.IsControl))
        {
            throw new InvalidOperationException("RegistrationProtection:CaptchaSiteKey is required and must be bounded.");
        }

        if (string.IsNullOrWhiteSpace(CaptchaSecretFile) || !Path.IsPathFullyQualified(CaptchaSecretFile))
        {
            throw new InvalidOperationException("RegistrationProtection:CaptchaSecretFile must be an absolute path.");
        }

        bool hostnameMissing = string.IsNullOrWhiteSpace(CaptchaExpectedHostname);
        if (CaptchaExpectedHostname.Length > 253 || CaptchaExpectedHostname.Any(char.IsControl) ||
            !hostnameMissing && (CaptchaExpectedHostname.Any(char.IsWhiteSpace) ||
                Uri.CheckHostName(CaptchaExpectedHostname) == UriHostNameType.Unknown) ||
            isProduction && hostnameMissing)
        {
            throw new InvalidOperationException(
                "RegistrationProtection:CaptchaExpectedHostname must be the configured public hostname in Production.");
        }

        if (isProduction && !File.Exists(CaptchaSecretFile))
        {
            throw new InvalidOperationException("RegistrationProtection:CaptchaSecretFile does not exist.");
        }

        if (CaptchaProvider == RegistrationCaptchaProvider.Turnstile &&
            (!IsTurnstileLabel(CaptchaExpectedAction, 32) || !IsTurnstileLabel(CaptchaExpectedCdata, 255)))
        {
            throw new InvalidOperationException(
                "RegistrationProtection Turnstile action and cdata must be non-empty alphanumeric, underscore, or hyphen values within provider limits.");
        }

        if (CaptchaVerificationTimeout < TimeSpan.FromSeconds(1) ||
            CaptchaVerificationTimeout > TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException("RegistrationProtection:CaptchaVerificationTimeout is outside the supported range.");
        }
    }

    private static bool IsTurnstileLabel(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
