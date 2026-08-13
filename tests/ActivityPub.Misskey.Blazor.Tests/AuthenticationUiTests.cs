using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class AuthenticationUiTests : BunitContext
{
    [Fact]
    public async Task SignInRevealsSecurityKeyUiOnlyAfterPasswordAuthentication()
    {
        var authentication = new RecordingAuthenticationInterop();
        var browser = new NoOpBrowserInterop();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));
        int loginEvents = 0;

        IRenderedComponent<MkSignin> component = Render<MkSignin>(parameters => parameters
            .Add(signin => signin.WithAvatar, false)
            .Add(signin => signin.AutoSet, true)
            .Add(signin => signin.Message, "Authenticate to continue")
            .Add(signin => signin.Login, () => loginEvents++));

        component.WaitForAssertion(() => Assert.True(authentication.Attached));
        Assert.Equal("/auth/passkey/options", authentication.PasskeyOptionsUrl);
        Assert.Equal("/auth/passkey/assertion", authentication.PasskeyAssertionUrl);
        Assert.Equal("true", component.Find("form.eppvobhk").GetAttribute("data-auto-set"));
        Assert.Equal("false", component.Find("form.eppvobhk").GetAttribute("aria-busy"));
        Assert.Contains("display: none", component.Find(".auth > .avatar").GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("margin-bottom: 1.5em", component.Find(".auth > .avatar").GetAttribute("style"), StringComparison.Ordinal);
        Assert.Equal("Username", component.Find("input[name=username]").GetAttribute("placeholder"));
        Assert.Equal("Password", component.Find("input[name=password]").GetAttribute("placeholder"));
        Assert.Null(component.Find("input[name=username]").GetAttribute("maxlength"));
        Assert.Null(component.Find("input[name=password]").GetAttribute("autocomplete"));
        Assert.Equal("false", component.Find("[data-password-toggle]").GetAttribute("aria-pressed"));
        Assert.True(component.Find("[data-caps-lock-warning]").HasAttribute("hidden"));

        await component.InvokeAsync(component.Instance.NotifyPasskeyAvailable);
        await component.InvokeAsync(component.Instance.NotifyTwoFactorRequired);
        Assert.Contains("securityKeys", component.Find("[class~='2fa-signin']").ClassList);
        Assert.Equal("Touch the security key", component.Find(".tap-group > p").TextContent);
        Assert.Equal("Or", component.Find(".or-hr > .or-msg").TextContent);
        Assert.Equal("Two-factor authentication", component.Find(".totp-group > p").TextContent);
        Assert.NotNull(component.Find(".totp-group input[name=token][autocomplete=off]"));
        Assert.Null(component.Find(".totp-group input[name=token]").GetAttribute("maxlength"));
        Assert.Empty(component.FindAll(".totp-group input[name=password]"));
        Assert.Equal("Retry", component.Find(".tap-group button .content").TextContent);

        await component.InvokeAsync(() => component.Instance.NotifyPasskeyQuerying(querying: true));
        Assert.Empty(component.FindAll(".tap-group button"));
        await component.InvokeAsync(() => component.Instance.NotifyPasskeyQuerying(querying: false));
        Assert.Single(component.FindAll(".tap-group button"));

        await component.InvokeAsync(component.Instance.NotifyAuthenticationSucceeded);
        Assert.Equal(1, loginEvents);

        await component.InvokeAsync(() => component.Instance.NotifyAuthenticationFailure("INVALID_TWO_FACTOR_CODE"));
        Assert.Single(component.FindAll(".normal-signin"));
        Assert.Empty(component.FindAll("[class~='2fa-signin']"));
        MisskeyOverlayEntry failure = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.Alert, failure.Kind);
        Assert.Equal("Sign-in failed", failure.Alert?.Title);
        Assert.Equal("Sign-in failed", failure.Alert?.Text);
        Assert.Equal("Got it", failure.Alert?.AcknowledgementLabel);
    }

    [Fact]
    public async Task SignInDoesNotPreDisclosePasswordlessOrAvatarState()
    {
        var authentication = new RecordingAuthenticationInterop();
        var browser = new NoOpBrowserInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));

        IRenderedComponent<MkSignin> component = Render<MkSignin>();
        Assert.Single(component.FindAll(".normal-signin input[name=password]"));
        Assert.DoesNotContain("background-image", component.Find(".avatar").GetAttribute("style"), StringComparison.Ordinal);

        await component.InvokeAsync(component.Instance.NotifyPasskeyAvailable);
        await component.InvokeAsync(component.Instance.NotifyTwoFactorRequired);
        Assert.Empty(component.FindAll(".totp-group input[name=password]"));
        Assert.Empty(component.FindAll(".totp-group [data-password-toggle]"));
        Assert.DoesNotContain("password=", component.Markup, StringComparison.OrdinalIgnoreCase);

        await component.InvokeAsync(() => component.Instance.NotifyAuthenticationFailure("ACCOUNT_NOT_ACTIVE"));
        MisskeyOverlayEntry alert = Assert.Single(overlays.Entries);
        Assert.Equal("This account is suspended", alert.Alert?.Title);
        Assert.Equal("The account was suspended", alert.Alert?.Text);
    }

    [Fact]
    public void SignInProjectsBackendLockoutAndOidcFailureThroughPinnedAlerts()
    {
        var authentication = new RecordingAuthenticationInterop();
        var browser = new NoOpBrowserInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));

        IRenderedComponent<MkSignin> locked = Render<MkSignin>(parameters => parameters
            .Add(signin => signin.ErrorCode, "RATE_LIMIT_EXCEEDED"));

        MisskeyOverlayEntry lockout = null!;
        locked.WaitForAssertion(() => lockout = Assert.Single(overlays.Entries));
        Assert.Equal(MisskeyOverlayKind.Alert, lockout.Kind);
        Assert.Equal("Sign-in failed", lockout.Alert?.Title);
        Assert.Equal("Rate limit exceeded", lockout.Alert?.Text);

        overlays.Close(lockout.Id);
        IRenderedComponent<MkSignin> oidc = Render<MkSignin>(parameters => parameters
            .Add(signin => signin.ErrorCode, "OIDC_CALLBACK_FAILED"));

        MisskeyOverlayEntry callbackFailure = null!;
        oidc.WaitForAssertion(() => callbackFailure = Assert.Single(overlays.Entries));
        Assert.Equal(MisskeyOverlayKind.Alert, callbackFailure.Kind);
        Assert.Equal("Sign-in failed", callbackFailure.Alert?.Title);
        Assert.Equal("Sign-in failed", callbackFailure.Alert?.Text);
    }

    [Fact]
    public void LocalOnlySignInAlwaysCollectsCredentialsWithoutAnExternalOidcBranch()
    {
        var authentication = new RecordingAuthenticationInterop();
        var browser = new NoOpBrowserInterop();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));

        IRenderedComponent<MkSignin> component = Render<MkSignin>(parameters => parameters
            .Add(signin => signin.ReturnUrl, "/settings/profile"));

        AngleSharp.Dom.IElement form = component.Find("form.eppvobhk._monolithic_");
        Assert.Equal("local", form.GetAttribute("data-auth-mode"));
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Equal("/api/signin", form.GetAttribute("action"));
        Assert.NotNull(form.QuerySelector(":scope > .auth._section._formRoot > .avatar"));
        Assert.Single(component.FindAll("input[name=username]"));
        Assert.Single(component.FindAll("input[name=password]"));
        Assert.Empty(component.FindAll(".social._section > a._borderButton._gap[data-auth-external]"));
    }

    [Fact]
    public async Task SignInDialogEmitsPinnedDoneCancelledAndClosedOrder()
    {
        var authentication = new RecordingAuthenticationInterop();
        var browser = new NoOpBrowserInterop();
        var dialogInterop = new RecordingDialogInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IDialogWindowInterop>(dialogInterop);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));

        var cancelledEvents = new List<string>();
        Guid cancelledId = overlays.ShowSignIn();
        IRenderedComponent<MkSigninDialog> cancelled = Render<MkSigninDialog>(parameters => parameters
            .Add(dialog => dialog.Id, cancelledId)
            .Add(dialog => dialog.ReturnUrl, "/settings/security")
            .Add(dialog => dialog.ErrorCode, "OIDC_CALLBACK_FAILED")
            .Add(dialog => dialog.Message, "Authenticate to continue")
            .Add(dialog => dialog.AutoSet, true)
            .Add(dialog => dialog.Cancelled, () => cancelledEvents.Add("cancelled"))
            .Add(dialog => dialog.Closed, () => cancelledEvents.Add("closed")));
        IRenderedComponent<MkSignin> cancelledSignIn = cancelled.FindComponent<MkSignin>();
        Assert.Equal("/settings/security", cancelledSignIn.Instance.ReturnUrl);
        Assert.Equal("OIDC_CALLBACK_FAILED", cancelledSignIn.Instance.ErrorCode);
        Assert.Equal("Authenticate to continue", cancelledSignIn.Instance.Message);
        Assert.True(cancelledSignIn.Instance.AutoSet);
        IRenderedComponent<MkModalWindow> cancelledWindow = cancelled.FindComponent<MkModalWindow>();
        await cancelledWindow.InvokeAsync(cancelledWindow.Instance.BeginCloseAsync);
        await cancelledWindow.InvokeAsync(cancelledWindow.Instance.NotifyClosed);
        Assert.Equal(["cancelled", "closed"], cancelledEvents);

        var completedEvents = new List<string>();
        Guid completedId = overlays.ShowSignIn();
        IRenderedComponent<MkSigninDialog> completed = Render<MkSigninDialog>(parameters => parameters
            .Add(dialog => dialog.Id, completedId)
            .Add(dialog => dialog.Done, () => completedEvents.Add("done"))
            .Add(dialog => dialog.Cancelled, () => completedEvents.Add("cancelled"))
            .Add(dialog => dialog.Closed, () => completedEvents.Add("closed")));
        await completed.FindComponent<MkSignin>().InvokeAsync(
            completed.FindComponent<MkSignin>().Instance.NotifyAuthenticationSucceeded);
        IRenderedComponent<MkModalWindow> completedWindow = completed.FindComponent<MkModalWindow>();
        await completedWindow.InvokeAsync(completedWindow.Instance.NotifyClosed);
        Assert.Equal(["done", "closed"], completedEvents);

        var escapeEvents = new List<string>();
        Guid escapeId = overlays.ShowSignIn();
        IRenderedComponent<MkSigninDialog> escaped = Render<MkSigninDialog>(parameters => parameters
            .Add(dialog => dialog.Id, escapeId)
            .Add(dialog => dialog.Cancelled, () => escapeEvents.Add("cancelled"))
            .Add(dialog => dialog.Closed, () => escapeEvents.Add("closed")));
        IRenderedComponent<MkModalWindow> escapedWindow = escaped.FindComponent<MkModalWindow>();
        escaped.Find(".qzhlnise > .bg").Click();
        Assert.Empty(escapeEvents);
        await escapedWindow.InvokeAsync(escapedWindow.Instance.NotifyClosed);
        Assert.Equal(["closed"], escapeEvents);

        var disposedEvents = new List<string>();
        Guid disposedId = overlays.ShowSignIn();
        IRenderedComponent<MkSigninDialog> disposed = Render<MkSigninDialog>(parameters => parameters
            .Add(dialog => dialog.Id, disposedId)
            .Add(dialog => dialog.Done, () => disposedEvents.Add("done"))
            .Add(dialog => dialog.Cancelled, () => disposedEvents.Add("cancelled"))
            .Add(dialog => dialog.Closed, () => disposedEvents.Add("closed")));
        disposed.Dispose();
        Assert.Empty(disposedEvents);
    }

    [Fact]
    public async Task SignUpPreservesEmailAvailabilityLocalizationAndAutoSetContract()
    {
        var authentication = new RecordingAuthenticationInterop();
        var browser = new NoOpBrowserInterop();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton<IInstancePresentationService>(new RegistrationInstanceService());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://xn--bcher-kva.example"),
            LocalAccountsEnabled: true));
        int succeededEvents = 0;
        int emailPendingEvents = 0;

        IRenderedComponent<MkSignup> component = Render<MkSignup>(parameters => parameters
            .Add(signup => signup.AutoSet, true)
            .Add(signup => signup.Succeeded, () => succeededEvents++)
            .Add(signup => signup.EmailPending, () => emailPendingEvents++));

        component.WaitForAssertion(() => Assert.True(authentication.SignUpAttached));
        Assert.Equal("/auth/username-available", authentication.UsernameAvailabilityUrl);
        Assert.Equal("true", component.Find("form.qlvuhzng").GetAttribute("data-auto-set"));
        Assert.Equal("false", component.Find("form.qlvuhzng").GetAttribute("aria-busy"));
        Assert.Equal("@bücher.example", component.Find("input[name=username]").Closest(".matxzzsk")?.QuerySelector(".suffix")?.TextContent.Trim());
        Assert.Equal("Email address", component.Find("input[name=email]").Closest(".matxzzsk")?.QuerySelector(".label")?.TextContent.Trim());
        Assert.Equal("Terms of Service", component.Find(".tou a._link").TextContent);
        Assert.Equal("DIV", component.Find("input[name=username]").Closest(".matxzzsk")?.QuerySelector("._help")?.TagName);
        Assert.Equal("DIV", component.Find("input[name=email]").Closest(".matxzzsk")?.QuerySelector("._help")?.TagName);
        Assert.True(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled"));

        component.Find("input[name=username]").Closest(".matxzzsk")?.QuerySelector("._help")?.Click();
        MisskeyOverlayEntry information = Assert.Single(overlays.Entries);
        Assert.Equal("info", information.Alert?.Type);
        Assert.Equal("Username information", information.Alert?.Text);
        overlays.Close(information.Id);

        component.Find(".tou > .button").Click();
        Assert.False(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled"));

        await component.InvokeAsync(() => component.Instance.NotifyEmailState("unavailable:format"));
        Assert.Contains("Email format is invalid", component.Find("input[name=email]").Closest(".matxzzsk")?.QuerySelector(".caption")?.TextContent, StringComparison.Ordinal);
        await component.InvokeAsync(component.Instance.NotifyRegistrationStarted);
        Assert.Equal("true", component.Find("form.qlvuhzng").GetAttribute("aria-busy"));
        Assert.True(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled"));
        Assert.Equal("Begin", component.Find("button[data-cy-signup-submit] .content").TextContent);
        await component.InvokeAsync(() => component.Instance.NotifyEmailPending("alice@social.example"));
        Assert.Equal("false", component.Find("form.qlvuhzng").GetAttribute("aria-busy"));
        Assert.Equal(1, emailPendingEvents);
        MisskeyOverlayEntry alert = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.Alert, alert.Kind);
        Assert.Equal("success", alert.Alert?.Type);
        Assert.Equal("Almost there", alert.Alert?.Title);
        Assert.Contains("alice@social.example", alert.Alert?.Text, StringComparison.Ordinal);

        await component.InvokeAsync(component.Instance.NotifyRegistrationSucceeded);
        Assert.Equal(1, succeededEvents);
    }

    [Fact]
    public async Task SignUpProjectsInviteOnlyHcaptchaWithUpstreamOrderAndFailClosedSubmit()
    {
        var authentication = new RecordingAuthenticationInterop();
        var captcha = new RecordingCaptchaInterop();
        var browser = new NoOpBrowserInterop();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<ICaptchaInterop>(captcha);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton<IInstancePresentationService>(new ProtectedRegistrationInstanceService());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));

        IRenderedComponent<MkSignup> component = Render<MkSignup>();
        component.WaitForAssertion(() => Assert.True(captcha.Attached));
        var inputs = component.FindAll("form.qlvuhzng input").ToArray();
        int inviteIndex = Array.FindIndex(inputs, input => input.GetAttribute("name") == "invitationCode");
        int usernameIndex = Array.FindIndex(inputs, input => input.GetAttribute("name") == "username");
        Assert.True(inviteIndex >= 0 && inviteIndex < usernameIndex);
        Assert.Equal("26", inputs[inviteIndex].GetAttribute("maxlength"));
        Assert.Equal("hcaptcha", captcha.Provider);
        Assert.Equal("10000000-ffff-ffff-ffff-000000000001", captcha.SiteKey);
        Assert.Equal("hcaptcha-response", component.Find("input[data-captcha-response]").GetAttribute("name"));
        Assert.True(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled"));

        IRenderedComponent<MkCaptcha> captchaComponent = component.FindComponent<MkCaptcha>();
        await captchaComponent.InvokeAsync(() => captchaComponent.Instance.NotifyResponseChanged(true));
        component.WaitForAssertion(() =>
            Assert.False(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled")));
        await component.InvokeAsync(() => component.Instance.NotifyRegistrationFailure("INVALID_CAPTCHA"));
        component.WaitForAssertion(() =>
            Assert.True(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled")));
        MisskeyOverlayEntry failure = Assert.Single(overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.Alert, failure.Kind);
        Assert.Equal("error", failure.Alert?.Type);
        Assert.Null(failure.Alert?.Title);
        Assert.Equal("Something happened", failure.Alert?.Text);
    }

    [Fact]
    public async Task SignUpProjectsTurnstileAtThePinnedUpstreamCaptchaPosition()
    {
        var captcha = new RecordingCaptchaInterop();
        var browser = new NoOpBrowserInterop();
        Services.AddSingleton<IAuthenticationFormInterop>(new RecordingAuthenticationInterop());
        Services.AddSingleton<ICaptchaInterop>(captcha);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton<IInstancePresentationService>(new TurnstileRegistrationInstanceService());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));

        IRenderedComponent<MkSignup> component = Render<MkSignup>();
        component.WaitForAssertion(() => Assert.True(captcha.Attached));

        Assert.Equal("turnstile", captcha.Provider);
        Assert.Equal("turnstile-site-key", captcha.SiteKey);
        Assert.Equal("signup", captcha.Action);
        Assert.Equal("activitypub_signup", captcha.Cdata);
        Assert.Equal("cf-turnstile-response", component.Find("input[data-captcha-response]").GetAttribute("name"));
        Assert.True(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled"));

        IRenderedComponent<MkCaptcha> captchaComponent = component.FindComponent<MkCaptcha>();
        await captchaComponent.InvokeAsync(() => captchaComponent.Instance.NotifyResponseChanged(true));
        component.WaitForAssertion(() =>
            Assert.False(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled")));
        await component.InvokeAsync(() => component.Instance.NotifyRegistrationFailure("INVALID_CAPTCHA"));
        component.WaitForAssertion(() =>
            Assert.True(component.Find("button[data-cy-signup-submit]").HasAttribute("disabled")));
    }

    [Fact]
    public void SignUpDialogPassesActualStringParametersWithoutInventingAnInitialFailure()
    {
        var authentication = new RecordingAuthenticationInterop();
        var browser = new NoOpBrowserInterop();
        var dialogInterop = new RecordingDialogInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IDialogWindowInterop>(dialogInterop);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton<IInstancePresentationService>(new RegistrationInstanceService());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));

        Guid id = overlays.ShowSignUp("/settings/profile");
        IRenderedComponent<MkSignupDialog> component = Render<MkSignupDialog>(parameters => parameters
            .Add(dialog => dialog.Id, id)
            .Add(dialog => dialog.ReturnUrl, "/settings/profile")
            .Add(dialog => dialog.ErrorCode, null));

        IRenderedComponent<MkSignup> signup = component.FindComponent<MkSignup>();
        Assert.Equal("/settings/profile", signup.Instance.ReturnUrl);
        Assert.Null(signup.Instance.ErrorCode);
        component.WaitForAssertion(() =>
        {
            Assert.True(authentication.SignUpAttached);
            Assert.Single(overlays.Entries);
            Assert.Equal(MisskeyOverlayKind.SignUp, overlays.Entries[0].Kind);
        });
    }

    [Fact]
    public async Task SignUpDialogPreservesPinnedGeometryAutoSetAndResolvedEventOrder()
    {
        var authentication = new RecordingAuthenticationInterop();
        var browser = new NoOpBrowserInterop();
        var dialogInterop = new RecordingDialogInterop();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<IAuthenticationFormInterop>(authentication);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
        Services.AddSingleton<IDialogWindowInterop>(dialogInterop);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new AuthenticationLocalizer());
        Services.AddSingleton<IInstancePresentationService>(new RegistrationInstanceService());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));

        var successEvents = new List<string>();
        Guid successId = overlays.ShowSignUp();
        IRenderedComponent<MkSignupDialog> success = Render<MkSignupDialog>(parameters => parameters
            .Add(dialog => dialog.Id, successId)
            .Add(dialog => dialog.AutoSet, true)
            .Add(dialog => dialog.Done, () => successEvents.Add("done"))
            .Add(dialog => dialog.Cancelled, () => successEvents.Add("cancelled"))
            .Add(dialog => dialog.Closed, () => successEvents.Add("closed")));
        IRenderedComponent<MkModalWindow> successWindow = success.FindComponent<MkModalWindow>();
        IRenderedComponent<MkSignup> successForm = success.FindComponent<MkSignup>();
        Assert.Equal(366, successWindow.Instance.Width);
        Assert.Equal(500, successWindow.Instance.Height);
        Assert.True(successForm.Instance.AutoSet);
        Assert.Equal("true", success.Find("form.qlvuhzng").GetAttribute("data-auto-set"));
        Assert.NotNull(success.Find(".ebkgoccj._narrow_ > .body > ._monolithic_ > ._section > form.qlvuhzng._formRoot"));

        await successForm.InvokeAsync(successForm.Instance.NotifyRegistrationSucceeded);
        Assert.Equal(["done"], successEvents);
        await successWindow.InvokeAsync(successWindow.Instance.NotifyClosed);
        Assert.Equal(["done", "closed"], successEvents);
        Assert.DoesNotContain(overlays.Entries, entry => entry.Id == successId);

        var pendingEvents = new List<string>();
        Guid pendingId = overlays.ShowSignUp();
        IRenderedComponent<MkSignupDialog> pending = Render<MkSignupDialog>(parameters => parameters
            .Add(dialog => dialog.Id, pendingId)
            .Add(dialog => dialog.Done, () => pendingEvents.Add("done"))
            .Add(dialog => dialog.Cancelled, () => pendingEvents.Add("cancelled"))
            .Add(dialog => dialog.Closed, () => pendingEvents.Add("closed")));
        IRenderedComponent<MkSignup> pendingForm = pending.FindComponent<MkSignup>();
        IRenderedComponent<MkModalWindow> pendingWindow = pending.FindComponent<MkModalWindow>();
        await pendingForm.InvokeAsync(() => pendingForm.Instance.NotifyEmailPending("pending@example.test"));
        Assert.Empty(pendingEvents);
        Assert.Contains(overlays.Entries, entry => entry.Id == pendingId);
        MisskeyOverlayEntry pendingAlert = Assert.Single(
            overlays.Entries,
            entry => entry.Kind == MisskeyOverlayKind.Alert);
        await pendingWindow.InvokeAsync(pendingWindow.Instance.NotifyClosed);
        Assert.Equal(["closed"], pendingEvents);
        Assert.DoesNotContain(overlays.Entries, entry => entry.Id == pendingId);
        Assert.Contains(overlays.Entries, entry => entry.Id == pendingAlert.Id);
        overlays.Close(pendingAlert.Id);

        var cancelledEvents = new List<string>();
        Guid cancelledId = overlays.ShowSignUp();
        IRenderedComponent<MkSignupDialog> cancelled = Render<MkSignupDialog>(parameters => parameters
            .Add(dialog => dialog.Id, cancelledId)
            .Add(dialog => dialog.Done, () => cancelledEvents.Add("done"))
            .Add(dialog => dialog.Cancelled, () => cancelledEvents.Add("cancelled"))
            .Add(dialog => dialog.Closed, () => cancelledEvents.Add("closed")));
        IRenderedComponent<MkModalWindow> cancelledWindow = cancelled.FindComponent<MkModalWindow>();
        await cancelledWindow.InvokeAsync(cancelledWindow.Instance.BeginCloseAsync);
        Assert.Equal(["cancelled"], cancelledEvents);
        await cancelledWindow.InvokeAsync(cancelledWindow.Instance.NotifyClosed);
        Assert.Equal(["cancelled", "closed"], cancelledEvents);

        var escapeEvents = new List<string>();
        Guid escapeId = overlays.ShowSignUp();
        IRenderedComponent<MkSignupDialog> escaped = Render<MkSignupDialog>(parameters => parameters
            .Add(dialog => dialog.Id, escapeId)
            .Add(dialog => dialog.Done, () => escapeEvents.Add("done"))
            .Add(dialog => dialog.Cancelled, () => escapeEvents.Add("cancelled"))
            .Add(dialog => dialog.Closed, () => escapeEvents.Add("closed")));
        IRenderedComponent<MkModalWindow> escapedWindow = escaped.FindComponent<MkModalWindow>();
        await escapedWindow.InvokeAsync(escapedWindow.Instance.NotifyClosed);
        Assert.Equal(["closed"], escapeEvents);
    }

    private sealed class RecordingAuthenticationInterop : IAuthenticationFormInterop
    {
        public bool Attached { get; private set; }

        public string? PasskeyOptionsUrl { get; private set; }

        public string? PasskeyAssertionUrl { get; private set; }

        public bool SignUpAttached { get; private set; }

        public string? UsernameAvailabilityUrl { get; private set; }

        public ValueTask<IJSObjectReference> AttachSignInAsync(
            ElementReference form,
            DotNetObjectReference<MkSignin> receiver,
            string passkeyOptionsUrl,
            string passkeyAssertionUrl,
            CancellationToken cancellationToken)
        {
            Attached = true;
            PasskeyOptionsUrl = passkeyOptionsUrl;
            PasskeyAssertionUrl = passkeyAssertionUrl;
            return ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());
        }

        public ValueTask<IJSObjectReference> AttachSignUpAsync(
            ElementReference form,
            DotNetObjectReference<MkSignup> receiver,
            string usernameAvailabilityUrl,
            CancellationToken cancellationToken)
        {
            SignUpAttached = true;
            UsernameAvailabilityUrl = usernameAvailabilityUrl;
            return ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());
        }

        public ValueTask<IJSObjectReference> AttachInitialSetupAsync(
            ElementReference form,
            DotNetObjectReference<WelcomeSetup> receiver,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpBrowserInterop : IFormInputInterop, IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool autofocus,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpJsObject : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AuthenticationLocalizer : IMisskeyLocalizer
    {
        private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        {
            ["username"] = "Username",
            ["password"] = "Password",
            ["forgotPassword"] = "Forgot password",
            ["login"] = "Sign in",
            ["loggingIn"] = "Signing in",
            ["signinWith"] = "Sign in with {x}",
            ["tapSecurityKey"] = "Touch the security key",
            ["retry"] = "Retry",
            ["or"] = "Or",
            ["twoStepAuthentication"] = "Two-factor authentication",
            ["token"] = "Token",
            ["signinFailed"] = "Sign-in failed",
            ["loginFailed"] = "Sign-in failed",
            ["rateLimitExceeded"] = "Rate limit exceeded",
            ["yourAccountSuspendedTitle"] = "This account is suspended",
            ["yourAccountSuspendedDescription"] = "The account was suspended",
            ["gotIt"] = "Got it",
            ["usernameInfo"] = "Username information",
            ["emailAddress"] = "Email address",
            ["_signup.emailAddressInfo"] = "Email information",
            ["checking"] = "Checking",
            ["available"] = "Available",
            ["unavailable"] = "Unavailable",
            ["error"] = "Error",
            ["usernameInvalidFormat"] = "Username format is invalid",
            ["tooShort"] = "Too short",
            ["tooLong"] = "Too long",
            ["_emailUnavailable.used"] = "Email is already used",
            ["_emailUnavailable.format"] = "Email format is invalid",
            ["_emailUnavailable.disposable"] = "Disposable email is unavailable",
            ["_emailUnavailable.mx"] = "Email MX is unavailable",
            ["_emailUnavailable.smtp"] = "Email SMTP is unavailable",
            ["weakPassword"] = "Weak password",
            ["normalPassword"] = "Average password",
            ["strongPassword"] = "Strong password",
            ["retype"] = "Enter again",
            ["passwordMatched"] = "Matches",
            ["passwordNotMatched"] = "Does not match",
            ["agreeTo"] = "I agree to {0}",
            ["tos"] = "Terms of Service",
            ["start"] = "Begin",
            ["processing"] = "Processing",
            ["_signup.almostThere"] = "Almost there",
            ["_signup.emailSent"] = "Confirmation sent to {email}",
            ["somethingHappened"] = "Something happened",
            ["registration"] = "Registration",
            ["invitationCode"] = "Invitation code",
            ["waiting"] = "Waiting"
        };

        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "en-US";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-US");

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            string value = Values.TryGetValue(key, out string? translated) ? translated : key;
            if (arguments is null)
            {
                return value;
            }

            foreach ((string name, object? replacement) in arguments)
            {
                value = value.Replace($"{{{name}}}", Convert.ToString(replacement, CultureInfo.InvariantCulture), StringComparison.Ordinal);
            }

            return value;
        }

        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class RecordingDialogInterop : IDialogWindowInterop
    {
        public int AttachCalls { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            AttachCalls++;
            return ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RegistrationInstanceService : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new InstanceSummaryViewModel(
            "social.example",
            "Test instance",
            "12.119.2",
            "/static-assets/favicon.png",
            BackgroundImageUrl: null,
            LogoImageUrl: null,
            DisableRegistration: false,
            EmailRequiredForSignup: true,
            EnableEmail: true,
            TosUrl: "https://social.example/terms"));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class ProtectedRegistrationInstanceService : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new InstanceSummaryViewModel(
            "social.example",
            "Test instance",
            "12.119.2",
            "/static-assets/favicon.png",
            BackgroundImageUrl: null,
            LogoImageUrl: null,
            DisableRegistration: true,
            EmailRequiredForSignup: false,
            EnableEmail: false,
            TosUrl: null,
            EnableHcaptcha: true,
            HcaptchaSiteKey: "10000000-ffff-ffff-ffff-000000000001"));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class TurnstileRegistrationInstanceService : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new InstanceSummaryViewModel(
            "social.example",
            "Test instance",
            "12.119.2",
            "/static-assets/favicon.png",
            BackgroundImageUrl: null,
            LogoImageUrl: null,
            DisableRegistration: true,
            EmailRequiredForSignup: false,
            EnableEmail: false,
            TosUrl: null,
            EnableTurnstile: true,
            TurnstileSiteKey: "turnstile-site-key",
            TurnstileAction: "signup",
            TurnstileCdata: "activitypub_signup"));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class RecordingCaptchaInterop : ICaptchaInterop
    {
        public bool Attached { get; private set; }

        public string? Provider { get; private set; }

        public string? SiteKey { get; private set; }

        public string? Action { get; private set; }

        public string? Cdata { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            DotNetObjectReference<MkCaptcha> receiver,
            string provider,
            string siteKey,
            string? action,
            string? cdata,
            bool darkMode,
            CancellationToken cancellationToken)
        {
            Attached = true;
            Provider = provider;
            SiteKey = siteKey;
            Action = action;
            Cdata = cdata;
            return ValueTask.FromResult<IJSObjectReference>(new NoOpJsObject());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
