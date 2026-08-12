using ActivityPub.Misskey.Blazor.Client;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MisskeyBootstrapUtilitiesTests
{
    [Fact]
    public void LanguageSelectionPreservesSavedExactThenBrowserPrimaryAndEnglishFallback()
    {
        string[] supported = ["ja-JP", "en-US", "fr-FR"];
        Assert.Equal("fr-FR", MisskeyBootstrapUtilities.ChooseLanguage(supported, "fr-FR", "ja-JP"));
        Assert.Equal("ja-JP", MisskeyBootstrapUtilities.ChooseLanguage(supported, null, "ja-JP"));
        Assert.Equal("fr-FR", MisskeyBootstrapUtilities.ChooseLanguage(supported, null, "fr-CA"));
        Assert.Equal("en-US", MisskeyBootstrapUtilities.ChooseLanguage(supported, null, "de-DE"));
    }

    [Fact]
    public void ShellSelectionMatchesPinnedBootstrapPrecedence()
    {
        Assert.Equal(MisskeyShellKind.Zen, MisskeyBootstrapUtilities.SelectShell(true, "deck", true));
        Assert.Equal(MisskeyShellKind.Visitor, MisskeyBootstrapUtilities.SelectShell(false, "deck", false));
        Assert.Equal(MisskeyShellKind.Deck, MisskeyBootstrapUtilities.SelectShell(true, "deck", false));
        Assert.Equal(MisskeyShellKind.Classic, MisskeyBootstrapUtilities.SelectShell(true, "classic", false));
        Assert.Equal(MisskeyShellKind.Universal, MisskeyBootstrapUtilities.SelectShell(true, "unknown", false));
    }

    [Fact]
    public void InitializationErrorsAreSafeCodesWithoutSecretPayloads()
    {
        Assert.Equal("CLIENT_INIT_AUTHORITY_REJECTED", MisskeyBootstrapUtilities.SafeInitializationErrorCode(new UnauthorizedAccessException()));
        Assert.Equal("CLIENT_INIT_NETWORK_FAILURE", MisskeyBootstrapUtilities.SafeInitializationErrorCode(new HttpRequestException()));
        Assert.Equal("CLIENT_INIT_FAILED", MisskeyBootstrapUtilities.SafeInitializationErrorCode(new InvalidOperationException("secret")));
    }
}
