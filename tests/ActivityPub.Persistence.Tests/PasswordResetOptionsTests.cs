using ActivityPub.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActivityPub.Persistence.Tests;

public sealed class PasswordResetOptionsTests
{
    [Fact]
    public void ProductionRejectsPlaintextSmtp()
    {
        var options = ValidOptions(PasswordResetTlsMode.None);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            options.Validate(
                isProduction: true,
                new LocalAccountOptions { Enabled = true },
                new Uri("https://social.example")));

        Assert.Contains("STARTTLS", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledResetRequiresLocalIdentity()
    {
        var options = ValidOptions(PasswordResetTlsMode.StartTls);

        Assert.Throws<InvalidOperationException>(() => options.Validate(
            isProduction: false,
            new LocalAccountOptions { Enabled = false },
            new Uri("https://social.example")));
    }

    [Fact]
    public void DisabledResetDoesNotRequireSmtpConfiguration()
    {
        new PasswordResetOptions().Validate(
            isProduction: true,
            new LocalAccountOptions { Enabled = false },
            new Uri("https://social.example"));
    }

    [Fact]
    public void IdentityCryptographicTokenLifetimeCoversConfiguredConfirmationExpiry()
    {
        PasswordResetOptions options = ValidOptions(
            PasswordResetTlsMode.StartTls,
            emailConfirmationLifetime: TimeSpan.FromDays(3));
        using ServiceProvider services = new ServiceCollection()
            .AddActivityPubPasswordReset(options)
            .BuildServiceProvider();

        Assert.Equal(
            TimeSpan.FromDays(3),
            services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
    }

    private static PasswordResetOptions ValidOptions(
        PasswordResetTlsMode tlsMode,
        TimeSpan? emailConfirmationLifetime = null) => new()
        {
            Enabled = true,
            SenderAddress = "no-reply@social.example",
            SenderName = "Social",
            SmtpHost = "smtp.social.example",
            SmtpPort = 587,
            TlsMode = tlsMode,
            TokenLifetime = TimeSpan.FromMinutes(30),
            RequestCooldown = TimeSpan.FromMinutes(20),
            EmailConfirmationTokenLifetime = emailConfirmationLifetime ?? TimeSpan.FromDays(1),
            SendTimeout = TimeSpan.FromSeconds(15)
        };
}
