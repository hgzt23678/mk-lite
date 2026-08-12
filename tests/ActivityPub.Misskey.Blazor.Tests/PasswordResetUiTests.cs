using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class PasswordResetUiTests : BunitContext
{
    [Fact]
    public void ForgotPasswordPreservesUpstreamEmailFormHierarchy()
    {
        var overlays = new MisskeyOverlayService();
        Guid id = overlays.ShowForgotPassword();
        RegisterCommon(overlays, emailEnabled: true);

        IRenderedComponent<MkForgotPassword> component = Render<MkForgotPassword>(parameters =>
            parameters.Add(dialog => dialog.Id, id));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".qzhlnise > .content > .ebkgoccj > .body > form.bafeceda"));
            Assert.NotNull(component.Find(".ebkgoccj > .header > button[data-mk-dialog-close=true]"));
            Assert.Equal(2, component.FindAll(".bafeceda > .main._formRoot > .matxzzsk._formBlock").Count);
            Assert.NotNull(component.Find(".bafeceda input[name=username][pattern='^[a-zA-Z0-9_]+$'][required]"));
            Assert.Equal("true", component.Find(".bafeceda input[name=username]").GetAttribute("data-mk-autofocus"));
            Assert.NotNull(component.Find(".bafeceda input[name=email][type=email][required]"));
            Assert.Null(component.Find(".bafeceda input[name=email]").GetAttribute("data-mk-autofocus"));
            Assert.Equal(
                string.Empty,
                component.Find(".bafeceda input[name=email]").ParentElement?.QuerySelector(":scope > .prefix")?.TextContent ?? string.Empty);
            Assert.NotNull(component.Find(".bafeceda > .main > button.bghgjjyj._formBlock.primary[type=submit]"));
            Assert.NotNull(component.Find(".bafeceda > .sub > a._link[href='/about']"));
            Assert.DoesNotContain("現在利用できません", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ForgotPasswordKeepsUpstreamUsernameOnlyAutofocusContract()
    {
        var overlays = new MisskeyOverlayService();
        Guid id = overlays.ShowForgotPassword();
        RegisterCommon(overlays, emailEnabled: true);
        var formInputInterop = new RecordingFormInputInterop();
        Services.AddSingleton<IFormInputInterop>(formInputInterop);

        using IRenderedComponent<MkForgotPassword> component = Render<MkForgotPassword>(parameters =>
            parameters.Add(dialog => dialog.Id, id));

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, formInputInterop.AutofocusValues.Count);
            Assert.Equal([true, false], formInputInterop.AutofocusValues);
        });
    }

    [Fact]
    public void ForgotPasswordPreservesUpstreamContactAdministratorBranchWhenEmailIsDisabled()
    {
        var overlays = new MisskeyOverlayService();
        Guid id = overlays.ShowForgotPassword();
        RegisterCommon(overlays, emailEnabled: false);

        IRenderedComponent<MkForgotPassword> component = Render<MkForgotPassword>(parameters =>
            parameters.Add(dialog => dialog.Id, id));

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".ebkgoccj > .body > .bafecedb"));
            Assert.Empty(component.FindAll("form.bafeceda"));
            Assert.Contains("管理者までお問い合わせください", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ResetPagePreservesUpstreamStickyHeaderSpacerAndFormDom()
    {
        RegisterCommon(new MisskeyOverlayService(), emailEnabled: true);
        Services.AddSingleton<IStickyContainerInterop, DisconnectedBrowserInterop>();
        Services.AddSingleton<ISpacerInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<IPizzaxDeviceState, DefaultDeviceState>();
        Services.AddSingleton<IPageHeaderInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<ICurrentAccountPresentationService, UnusedCurrentAccountService>();

        IRenderedComponent<ResetPassword> component = Render<ResetPassword>();

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(component.Find(".fdidabkb .titleContainer .title .title"));
            Assert.NotNull(component.Find("form._formRoot[action='/auth/password-reset/complete']"));
            Assert.NotNull(component.Find("input[name=resetToken][type=hidden]"));
            Assert.NotNull(component.Find(".matxzzsk._formBlock input[name=password][type=password]"));
            Assert.NotNull(component.Find("button.bghgjjyj._formBlock.primary[type=submit]"));
        });
    }

    [Fact]
    public void SignupCompletePreservesPinnedProcessingRootAndSecretFreeConfirmationForm()
    {
        RegisterCommon(new MisskeyOverlayService(), emailEnabled: true);

        IRenderedComponent<SignupComplete> component = Render<SignupComplete>();

        Assert.Contains("処理中", component.Markup, StringComparison.Ordinal);
        Assert.NotNull(component.Find("form[action='/auth/email-confirmation/complete'][hidden]"));
        Assert.NotNull(component.Find("input[name=confirmationToken][type=hidden][value='']"));
        Assert.DoesNotContain("confirmationToken=", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AlertDialogPreservesUpstreamIconBodyAndButtonHierarchy()
    {
        RegisterCommon(new MisskeyOverlayService(), emailEnabled: true);

        IRenderedComponent<MkAlertDialog> component = Render<MkAlertDialog>(parameters => parameters
            .Add(dialog => dialog.Type, "info")
            .Add(dialog => dialog.Text, "[わかった]を押して、メールアドレスの確認を完了してください。"));

        Assert.NotNull(component.Find(".qzhlnise.dialog > .content > .mk-dialog > .icon.info > i.fas.fa-info-circle"));
        Assert.NotNull(component.Find(".mk-dialog > .body"));
        Assert.NotNull(component.Find(".mk-dialog > .buttons > button.bghgjjyj.inline.primary"));
    }

    [Fact]
    public void GenericDialogPreservesPinnedIconMfmButtonsAndCompletionOrder()
    {
        RegisterCommon(new MisskeyOverlayService(), emailEnabled: true);
        var events = new List<string>();
        MisskeyDialogResult? result = null;

        IRenderedComponent<MkDialog> component = Render<MkDialog>(parameters => parameters
            .Add(dialog => dialog.Type, "warning")
            .Add(dialog => dialog.Title, "確認")
            .Add(dialog => dialog.Text, "この操作を続けますか？")
            .Add(dialog => dialog.ShowCancelButton, true)
            .Add(dialog => dialog.Done, value =>
            {
                result = value;
                events.Add("done");
            })
            .Add(dialog => dialog.Closed, () => events.Add("closed")));

        Assert.NotNull(component.Find(".qzhlnise.dialog > .content > .mk-dialog > .icon.warning > i.fas.fa-exclamation-triangle"));
        Assert.Equal("確認", component.Find(".mk-dialog > header").TextContent);
        Assert.Equal("この操作を続けますか？", component.Find(".mk-dialog > .body").TextContent);
        Assert.Equal(2, component.FindAll(".mk-dialog > .buttons > button.bghgjjyj.inline").Count);

        component.Find(".mk-dialog > .buttons > button.primary").Click();

        Assert.Equal(new MisskeyDialogResult(Canceled: false, Result: true), result);
        Assert.Equal(["done", "closed"], events);

        int actionCalls = 0;
        int actionDoneCalls = 0;
        int actionClosedCalls = 0;
        using IRenderedComponent<MkDialog> actionDialog = Render<MkDialog>(parameters => parameters
            .Add(dialog => dialog.Title, "操作")
            .Add(dialog => dialog.Actions,
            [
                new MisskeyDialogAction("実行", () =>
                {
                    actionCalls++;
                    return Task.CompletedTask;
                }, Primary: true)
            ])
            .Add(dialog => dialog.Done, _ => actionDoneCalls++)
            .Add(dialog => dialog.Closed, () => actionClosedCalls++));

        actionDialog.Find(".mk-dialog > .buttons > button.primary").Click();

        Assert.Equal(1, actionCalls);
        Assert.Equal(0, actionDoneCalls);
        Assert.Equal(1, actionClosedCalls);
    }

    [Fact]
    public async Task GenericDialogPreservesPasswordAndGroupedSelectResultBranches()
    {
        var overlays = new MisskeyOverlayService();
        RegisterCommon(overlays, emailEnabled: true);
        MisskeyDialogResult? passwordResult = null;
        using IRenderedComponent<MkDialog> password = Render<MkDialog>(parameters => parameters
            .Add(dialog => dialog.Title, "パスワード")
            .Add(dialog => dialog.Input, new MisskeyDialogInput("password", "入力してください", "before"))
            .Add(dialog => dialog.Done, value => passwordResult = value));

        AngleSharp.Dom.IElement input = password.Find(".mk-dialog > .matxzzsk input[type=password][placeholder='入力してください']");
        Assert.Equal("before", input.GetAttribute("value"));
        Assert.NotNull(password.Find(".matxzzsk > .input > .prefix > .fa-lock"));
        input.Input("after");
        input.KeyDown(new KeyboardEventArgs { Code = "Enter", Key = "Enter" });
        Assert.Equal(new MisskeyDialogResult(Canceled: false, Result: "after"), passwordResult);

        MisskeyDialogResult? selectResult = null;
        using IRenderedComponent<MkDialog> select = Render<MkDialog>(parameters => parameters
            .Add(dialog => dialog.Title, "公開範囲")
            .Add(dialog => dialog.Select, new MisskeyDialogSelect(
            [
                MkFormSelectItem.Group("公開", [
                    MkFormSelectItem.Option("public", "パブリック"),
                    MkFormSelectItem.Option("home", "ホーム")
                ])
            ], "home"))
            .Add(dialog => dialog.Done, value => selectResult = value));

        select.Find(".vblkjoeq > .input").Click();
        MisskeyOverlayEntry popup = Assert.Single(overlays.Entries);
        await select.InvokeAsync(popup.MenuItems.Single(item => item.Text == "パブリック").Action!);
        select.Find(".mk-dialog > .buttons > button.primary").Click();

        Assert.Equal(new MisskeyDialogResult(Canceled: false, Result: "public"), selectResult);
    }

    [Fact]
    public async Task SignupCompleteWaitsForPromptLeaveBeforeRenderingFailureDialog()
    {
        RegisterCommon(new MisskeyOverlayService(), emailEnabled: true);
        IRenderedComponent<SignupComplete> component = Render<SignupComplete>();

        await component.Instance.NotifyConfirmationReady();
        component.WaitForAssertion(() =>
            Assert.Equal("メール", component.Find("[role=alertdialog]").GetAttribute("aria-label")));

        await component.Instance.NotifyConfirmationFailure("INVALID_OR_EXPIRED_TOKEN");
        component.WaitForAssertion(() =>
            Assert.Equal("メール", component.Find("[role=alertdialog]").GetAttribute("aria-label")));

        IRenderedComponent<MkAlertDialog> prompt = component.FindComponent<MkAlertDialog>();
        await prompt.InvokeAsync(prompt.Instance.NotifyClosed);
        component.WaitForAssertion(() =>
        {
            Assert.Equal("エラー", component.Find("[role=alertdialog]").GetAttribute("aria-label"));
            Assert.Contains("有効な値ではありません", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task PasswordRecoveryScreensUseThePinnedCatalogForAllTwentyFiveLocales()
    {
        var overlays = new MisskeyOverlayService();
        MisskeyLocalizer localizer = RegisterCommon(overlays, emailEnabled: true);
        Services.AddSingleton<IStickyContainerInterop, DisconnectedBrowserInterop>();
        Services.AddSingleton<ISpacerInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<IPizzaxDeviceState, DefaultDeviceState>();
        Services.AddSingleton<IPageHeaderInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<ICurrentAccountPresentationService, UnusedCurrentAccountService>();

        foreach (MisskeyLocaleDefinition locale in localizer.SupportedLocales)
        {
            Assert.True(localizer.TrySelectLocale(locale.Locale));

            Guid id = overlays.ShowForgotPassword();
            using IRenderedComponent<MkForgotPassword> forgot = Render<MkForgotPassword>(parameters =>
                parameters.Add(dialog => dialog.Id, id));
            forgot.WaitForAssertion(() =>
            {
                Assert.Equal(
                    localizer.Translate("forgotPassword"),
                    forgot.Find(".ebkgoccj > .header > .title").TextContent);
                Assert.Equal(
                    localizer.Translate("username"),
                    forgot.Find("input[name=username]").ParentElement?.ParentElement?.QuerySelector(":scope > .label")?.TextContent.Trim());
                Assert.Equal(
                    localizer.Translate("emailAddress"),
                    forgot.Find("input[name=email]").ParentElement?.ParentElement?.QuerySelector(":scope > .label")?.TextContent.Trim());
                Assert.Equal(
                    localizer.Translate("send"),
                    forgot.Find("button[data-password-reset-submit] .content").TextContent.Trim());
                Assert.Contains(
                    localizer.Translate("_forgotPassword.ifNoEmail"),
                    forgot.Find(".bafeceda > .sub").TextContent,
                    StringComparison.Ordinal);
            });

            using IRenderedComponent<ResetPassword> reset = Render<ResetPassword>();
            Assert.Equal(
                localizer.Translate("resetPassword"),
                reset.Find(".fdidabkb .titleContainer .title .title").TextContent.Trim());
            Assert.Equal(
                localizer.Translate("newPassword"),
                reset.Find("input[name=password]").ParentElement?.ParentElement?.QuerySelector(":scope > .label")?.TextContent.Trim());
            Assert.Equal(
                localizer.Translate("save"),
                reset.Find("button[data-password-reset-submit] .content").TextContent.Trim());

            using IRenderedComponent<SignupComplete> signup = Render<SignupComplete>();
            Assert.Contains(localizer.Translate("processing"), signup.Markup, StringComparison.Ordinal);
            await signup.Instance.NotifyConfirmationReady();
            signup.WaitForAssertion(() =>
            {
                Assert.Equal(localizer.Translate("email"), signup.Find("[role=alertdialog]").GetAttribute("aria-label"));
                Assert.Equal(localizer.Translate("gotIt"), signup.Find(".mk-dialog > .buttons button .content").TextContent.Trim());
                Assert.Contains(
                    localizer.Translate(
                        "clickToFinishEmailVerification",
                        new Dictionary<string, object?> { ["ok"] = localizer.Translate("gotIt") }),
                    signup.Find(".mk-dialog > .body").TextContent,
                    StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public async Task SafeFailureCodesAreLocalizedWithoutRenderingSecrets()
    {
        var overlays = new MisskeyOverlayService();
        MisskeyLocalizer localizer = RegisterCommon(overlays, emailEnabled: true);
        Services.AddSingleton<IStickyContainerInterop, DisconnectedBrowserInterop>();
        Services.AddSingleton<ISpacerInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<IPizzaxDeviceState, DefaultDeviceState>();
        Services.AddSingleton<IPageHeaderInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<ICurrentAccountPresentationService, UnusedCurrentAccountService>();
        Assert.True(localizer.TrySelectLocale("en-US"));
        Guid id = overlays.ShowForgotPassword();
        using IRenderedComponent<MkForgotPassword> forgot = Render<MkForgotPassword>(parameters =>
            parameters.Add(dialog => dialog.Id, id));

        await forgot.Instance.NotifyRequestFailure("RATE_LIMIT_EXCEEDED");
        forgot.WaitForAssertion(() => Assert.Contains("Rate limit exceeded", forgot.Markup, StringComparison.Ordinal));

        using IRenderedComponent<ResetPassword> reset = Render<ResetPassword>();
        await reset.Instance.NotifyResetFailure("PASSWORD_TOO_SHORT");
        reset.WaitForAssertion(() => Assert.Contains("Too short", reset.Markup, StringComparison.Ordinal));
        await reset.Instance.NotifyResetFailure("INVALID_OR_EXPIRED_TOKEN");
        reset.WaitForAssertion(() => Assert.Contains("Invalid value", reset.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("token", reset.Find(".fdidabkb").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForgotPasswordRaisesDoneBeforeLeaveAndClosedOnlyAfterTheModalFinishes()
    {
        var overlays = new MisskeyOverlayService();
        RegisterCommon(overlays, emailEnabled: true);
        var dialogInterop = new RecordingDialogInterop();
        Services.AddSingleton<IDialogWindowInterop>(dialogInterop);
        var events = new List<string>();
        Guid id = overlays.ShowForgotPassword();
        using IRenderedComponent<MkForgotPassword> forgot = Render<MkForgotPassword>(parameters => parameters
            .Add(component => component.Id, id)
            .Add(component => component.Done, () => events.Add("done"))
            .Add(component => component.Closed, () => events.Add("closed")));

        await forgot.Instance.NotifyRequestAccepted();

        Assert.Equal(["done"], events);
        Assert.Equal(["close"], dialogInterop.Handle.Invocations);
        Assert.Single(overlays.Entries);

        IRenderedComponent<MkModalWindow> modal = forgot.FindComponent<MkModalWindow>();
        await modal.InvokeAsync(modal.Instance.NotifyClosed);

        Assert.Equal(["done", "closed"], events);
        Assert.Empty(overlays.Entries);
    }

    [Fact]
    public async Task AlertAndButtonDisposalTreatOnlyTheirOwnLifetimeCancellationAsExpected()
    {
        RegisterCommon(new MisskeyOverlayService(), emailEnabled: true);
        var handle = new CancellationOnDisposeJsObjectReference();
        var dialogInterop = new CancellationOnDisposeDialogInterop(handle);
        var buttonInterop = new CancellationOnDisposeButtonInterop(handle);
        Services.AddSingleton<IDialogWindowInterop>(dialogInterop);
        Services.AddSingleton<IButtonRippleInterop>(buttonInterop);

        IRenderedComponent<MkAlertDialog> component = Render<MkAlertDialog>(parameters => parameters
            .Add(dialog => dialog.Text, "fixture"));
        component.WaitForAssertion(() => Assert.Equal(2, handle.AttachmentCount));

        await component.FindComponent<MkButton>().Instance.DisposeAsync();
        await component.Instance.DisposeAsync();

        Assert.Equal(2, handle.DisposeAttempts);
        component.Dispose();
    }

    private MisskeyLocalizer RegisterCommon(MisskeyOverlayService overlays, bool emailEnabled)
    {
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IInstancePresentationService>(new FixedInstanceService(emailEnabled));
        Services.AddSingleton<DisconnectedBrowserInterop>();
        Services.AddSingleton<IDialogWindowInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<IFormInputInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<IButtonRippleInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        Services.AddSingleton<IMfmParserInterop, PlainMfmParser>();
        Services.AddSingleton<IPasswordResetFormInterop>(provider => provider.GetRequiredService<DisconnectedBrowserInterop>());
        var catalog = new MisskeyLocaleCatalog();
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "ja-JP";
        var localizer = new MisskeyLocalizer(
            catalog,
            new MisskeyLocaleRequestResolver(catalog),
            new HttpContextAccessor { HttpContext = context });
        Services.AddSingleton<IMisskeyLocalizer>(localizer);
        return localizer;
    }

    private sealed class FixedInstanceService(bool emailEnabled) : IInstancePresentationService
    {
        public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new InstanceSummaryViewModel(
            "identity-tests.example",
            "Identity tests",
            "12.119.2",
            "/static-assets/favicon.png",
            null,
            null,
            DisableRegistration: false,
            EmailRequiredForSignup: true,
            EnableEmail: emailEnabled,
            TosUrl: null));

        public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>([]);
    }

    private sealed class UnusedCurrentAccountService : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PlainMfmParser : IMfmParserInterop, IDisposable
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MfmNode>>(
            [new MfmNode("text", System.Text.Json.JsonSerializer.SerializeToElement(new { text }), null)]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class DisconnectedBrowserInterop :
        IDialogWindowInterop,
        IFormInputInterop,
        IButtonRippleInterop,
        IPasswordResetFormInterop,
        IStickyContainerInterop,
        ISpacerInterop,
        IPageHeaderInterop,
        IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => Disconnected();

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken) => Disconnected();

        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            Disconnected();

        public ValueTask<IJSObjectReference> AttachRequestAsync(
            ElementReference form,
            DotNetObjectReference<MkForgotPassword> receiver,
            CancellationToken cancellationToken) => Disconnected();

        public ValueTask<IJSObjectReference> AttachCompletionAsync(
            ElementReference form,
            DotNetObjectReference<ResetPassword> receiver,
            CancellationToken cancellationToken) => Disconnected();

        public ValueTask<IJSObjectReference> AttachEmailConfirmationAsync(
            ElementReference form,
            DotNetObjectReference<SignupComplete> receiver,
            CancellationToken cancellationToken) => Disconnected();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference body,
            double parentTop,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => Disconnected();

        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => Disconnected();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => Disconnected();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }

        private static ValueTask<IJSObjectReference> Disconnected() =>
            ValueTask.FromException<IJSObjectReference>(new JSDisconnectedException("bUnit has no browser runtime."));
    }

    private sealed class DefaultDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingDialogInterop : IDialogWindowInterop
    {
        public RecordingJsObjectReference Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(Handle);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingFormInputInterop : IFormInputInterop
    {
        public List<bool> AutofocusValues { get; } = [];

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken)
        {
            AutofocusValues.Add(autofocus);
            return ValueTask.FromResult<IJSObjectReference>(new RecordingJsObjectReference());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingJsObjectReference : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellationOnDisposeDialogInterop(CancellationOnDisposeJsObjectReference handle)
        : IDialogWindowInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class
        {
            handle.AttachmentCount++;
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellationOnDisposeButtonInterop(CancellationOnDisposeJsObjectReference handle)
        : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken)
        {
            handle.AttachmentCount++;
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            bool autofocus,
            CancellationToken cancellationToken) => AttachAsync(element, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellationOnDisposeJsObjectReference : IJSObjectReference
    {
        public int AttachmentCount { get; set; }

        public int DisposeAttempts { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier.Equals("dispose", StringComparison.Ordinal))
            {
                DisposeAttempts++;
                return ValueTask.FromException<TValue>(new OperationCanceledException());
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
