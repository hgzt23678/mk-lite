using System.Net.Mail;

namespace ActivityPub.Identity;

public enum PasswordResetTlsMode
{
    None = 0,
    StartTls = 1,
    SslOnConnect = 2
}

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public bool Enabled { get; init; }

    public string SenderAddress { get; init; } = string.Empty;

    public string SenderName { get; init; } = string.Empty;

    public string SmtpHost { get; init; } = string.Empty;

    public int SmtpPort { get; init; } = 587;

    public PasswordResetTlsMode TlsMode { get; init; } = PasswordResetTlsMode.StartTls;

    public string? SmtpUsername { get; init; }

    public string? SmtpPasswordFile { get; init; }

    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan RequestCooldown { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan EmailConfirmationTokenLifetime { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan EmailConfirmationRequestCooldown { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public void Validate(bool isProduction, LocalAccountOptions localAccounts, Uri publicBaseUri)
    {
        ArgumentNullException.ThrowIfNull(localAccounts);
        ArgumentNullException.ThrowIfNull(publicBaseUri);
        if (!Enabled)
        {
            if (localAccounts.RegistrationEnabled && localAccounts.RequireConfirmedEmail)
            {
                throw new InvalidOperationException("Confirmed-email registration requires PasswordReset:Enabled email delivery.");
            }

            return;
        }

        if (!localAccounts.Enabled)
        {
            throw new InvalidOperationException("PasswordReset:Enabled requires LocalAccounts:Enabled.");
        }

        if (isProduction && !string.Equals(publicBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Password reset links require an HTTPS Frontend:PublicBaseUri in Production.");
        }

        if (!IsSafeHost(SmtpHost) || SmtpPort is < 1 or > 65_535 ||
            TokenLifetime < TimeSpan.FromMinutes(5) || TokenLifetime > TimeSpan.FromHours(2) ||
            RequestCooldown < TimeSpan.FromMinutes(1) || RequestCooldown > TokenLifetime ||
            EmailConfirmationTokenLifetime < TimeSpan.FromMinutes(15) || EmailConfirmationTokenLifetime > TimeSpan.FromDays(7) ||
            EmailConfirmationRequestCooldown < TimeSpan.FromMinutes(1) ||
            EmailConfirmationRequestCooldown > EmailConfirmationTokenLifetime ||
            SendTimeout < TimeSpan.FromSeconds(2) || SendTimeout > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException("Password reset SMTP, lifetime, cooldown, or timeout configuration is invalid.");
        }

        if (!MailAddress.TryCreate(SenderAddress, out MailAddress? sender) ||
            !string.Equals(sender.Address, SenderAddress, StringComparison.OrdinalIgnoreCase) ||
            SenderName.Length > 200 || SenderName.Any(char.IsControl))
        {
            throw new InvalidOperationException("PasswordReset:SenderAddress or SenderName is invalid.");
        }

        bool hasUsername = !string.IsNullOrWhiteSpace(SmtpUsername);
        bool hasPasswordFile = !string.IsNullOrWhiteSpace(SmtpPasswordFile);
        if (hasUsername != hasPasswordFile)
        {
            throw new InvalidOperationException("PasswordReset SMTP username and password secret-file must be configured together.");
        }

        if (hasUsername)
        {
            if (SmtpUsername!.Length > 512 || SmtpUsername.Any(char.IsControl) ||
                !Path.IsPathFullyQualified(SmtpPasswordFile!) || !File.Exists(SmtpPasswordFile))
            {
                throw new InvalidOperationException("PasswordReset SMTP credentials are invalid or the password secret-file does not exist.");
            }
        }

        if (isProduction && TlsMode == PasswordResetTlsMode.None)
        {
            throw new InvalidOperationException("Password reset SMTP must use STARTTLS or implicit TLS in Production.");
        }

    }

    private static bool IsSafeHost(string value) => value.Length is > 0 and <= 253 &&
        !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '/' or '\\' or '@');
}
