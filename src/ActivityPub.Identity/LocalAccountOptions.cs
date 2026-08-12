namespace ActivityPub.Identity;

public sealed class LocalAccountOptions
{
    public const string SectionName = "LocalAccounts";

    public bool Enabled { get; init; }

    public bool RegistrationEnabled { get; init; }

    public bool RequireConfirmedEmail { get; init; }

    public int RequiredPasswordLength { get; init; } = 8;

    public int MaximumFailedAccessAttempts { get; init; } = 5;

    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(8);

    public void Validate(bool isProduction, bool keyManagementEnabled)
    {
        if (!Enabled)
        {
            if (RegistrationEnabled)
            {
                throw new InvalidOperationException("LocalAccounts:RegistrationEnabled requires LocalAccounts:Enabled.");
            }

            return;
        }

        if (RequiredPasswordLength is < 8 or > 128 ||
            MaximumFailedAccessAttempts is < 3 or > 20 ||
            LockoutDuration < TimeSpan.FromMinutes(1) || LockoutDuration > TimeSpan.FromDays(1) ||
            SessionLifetime < TimeSpan.FromMinutes(15) || SessionLifetime > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException("Local account password, lockout, or session settings are outside the supported range.");
        }

        if (RegistrationEnabled && !keyManagementEnabled)
        {
            throw new InvalidOperationException("Local account registration requires KeyManagement:Enabled so every account receives an ActivityPub signing key.");
        }

        if (isProduction && RegistrationEnabled && !RequireConfirmedEmail)
        {
            throw new InvalidOperationException("Production self-registration requires LocalAccounts:RequireConfirmedEmail=true.");
        }
    }
}
