using ActivityPub.Identity;

namespace ActivityPub.Persistence.Tests;

public sealed class RegistrationProtectionOptionsTests
{
    private static readonly LocalAccountOptions Accounts = new()
    {
        Enabled = true,
        RegistrationEnabled = true
    };

    [Fact]
    public void CaptchaConfigurationRequiresAnExplicitProvider()
    {
        var options = new RegistrationProtectionOptions
        {
            CaptchaExpectedHostname = "social.example"
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(Accounts, isProduction: false));
    }

    [Fact]
    public void ProductionCaptchaRequiresAConfiguredHostnameAndSecretFile()
    {
        var options = new RegistrationProtectionOptions
        {
            CaptchaProvider = RegistrationCaptchaProvider.Hcaptcha,
            CaptchaSiteKey = "site-key",
            CaptchaSecretFile = "/run/secrets/hcaptcha-secret"
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            options.Validate(Accounts, isProduction: true));

        Assert.Contains("CaptchaExpectedHostname", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://social.example")]
    [InlineData("social.example:443")]
    [InlineData("social example")]
    public void CaptchaExpectedHostnameRejectsUrlsPortsAndWhitespace(string hostname)
    {
        var options = new RegistrationProtectionOptions
        {
            CaptchaProvider = RegistrationCaptchaProvider.Recaptcha,
            CaptchaSiteKey = "site-key",
            CaptchaSecretFile = "/run/secrets/recaptcha-secret",
            CaptchaExpectedHostname = hostname
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(Accounts, isProduction: false));
    }

    [Theory]
    [InlineData("contains space", "activitypub_signup")]
    [InlineData("signup", "contains.dot")]
    [InlineData("signup/registration", "activitypub_signup")]
    [InlineData("signup", "")]
    public void TurnstileRequiresBoundedProviderCompatibleActionAndCdata(string action, string cdata)
    {
        var options = new RegistrationProtectionOptions
        {
            CaptchaProvider = RegistrationCaptchaProvider.Turnstile,
            CaptchaSiteKey = "site-key",
            CaptchaSecretFile = "/run/secrets/turnstile-secret",
            CaptchaExpectedHostname = "social.example",
            CaptchaExpectedAction = action,
            CaptchaExpectedCdata = cdata
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(Accounts, isProduction: false));
    }

    [Fact]
    public void TurnstileAcceptsExplicitSignupBindings()
    {
        var options = new RegistrationProtectionOptions
        {
            CaptchaProvider = RegistrationCaptchaProvider.Turnstile,
            CaptchaSiteKey = "site-key",
            CaptchaSecretFile = "/run/secrets/turnstile-secret",
            CaptchaExpectedHostname = "social.example",
            CaptchaExpectedAction = "signup",
            CaptchaExpectedCdata = "activitypub_signup"
        };

        options.Validate(Accounts, isProduction: false);
    }

    [Fact]
    public void UnknownCaptchaProviderFailsStartupValidation()
    {
        var options = new RegistrationProtectionOptions
        {
            CaptchaProvider = (RegistrationCaptchaProvider)999,
            CaptchaSiteKey = "site-key",
            CaptchaSecretFile = "/run/secrets/captcha-secret",
            CaptchaExpectedHostname = "social.example"
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(Accounts, isProduction: false));
    }
}
