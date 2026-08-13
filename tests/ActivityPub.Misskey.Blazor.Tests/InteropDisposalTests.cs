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

public sealed class InteropDisposalTests : BunitContext
{
    public InteropDisposalTests() => Services.AddSingleton<IMisskeyLocalizer>(new SignInLocalizer());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormInputTreatsCancellationAsExpectedOnlyWhileDisposing(bool cancelInvocation)
    {
        var handle = CancellableJsObject.Create(cancelInvocation);
        Services.AddSingleton<IFormInputInterop>(new FormInputInteropFixture(handle));

        IRenderedComponent<MkFormInput> component = Render<MkFormInput>();
        component.WaitForAssertion(() => Assert.True(handle.Attached));

        await component.Instance.DisposeAsync();

        Assert.True(handle.DisposeRequested);
    }

    [Fact]
    public async Task FormInputDoesNotHideAGenuineJavaScriptDisposalFailure()
    {
        var handle = CancellableJsObject.CreateJavaScriptFailure();
        Services.AddSingleton<IFormInputInterop>(new FormInputInteropFixture(handle));

        IRenderedComponent<MkFormInput> component = Render<MkFormInput>();
        component.WaitForAssertion(() => Assert.True(handle.Attached));

        JSException failure = await Assert.ThrowsAsync<JSException>(
            () => component.Instance.DisposeAsync().AsTask());

        Assert.Equal("GENUINE_FORM_INPUT_DISPOSAL_FAILURE", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModalWindowTreatsCancellationAsExpectedOnlyWhileDisposing(bool cancelInvocation)
    {
        var handle = CancellableJsObject.Create(cancelInvocation);
        Services.AddSingleton<IDialogWindowInterop>(new DialogWindowInteropFixture(handle));

        IRenderedComponent<MkModalWindow> component = Render<MkModalWindow>();
        component.WaitForAssertion(() => Assert.True(handle.Attached));

        await component.Instance.DisposeAsync();

        Assert.True(handle.DisposeRequested);
    }

    [Fact]
    public async Task ModalWindowDoesNotHideAGenuineJavaScriptDisposalFailure()
    {
        var handle = CancellableJsObject.CreateJavaScriptFailure("GENUINE_MODAL_DISPOSAL_FAILURE");
        Services.AddSingleton<IDialogWindowInterop>(new DialogWindowInteropFixture(handle));

        IRenderedComponent<MkModalWindow> component = Render<MkModalWindow>();
        component.WaitForAssertion(() => Assert.True(handle.Attached));

        JSException failure = await Assert.ThrowsAsync<JSException>(
            () => component.Instance.DisposeAsync().AsTask());

        Assert.Equal("GENUINE_MODAL_DISPOSAL_FAILURE", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignInTreatsCancellationAsExpectedOnlyWhileDisposing(bool cancelInvocation)
    {
        var handle = CancellableJsObject.Create(cancelInvocation);
        RegisterSignInServices(handle);

        IRenderedComponent<MkSignin> component = Render<MkSignin>();
        component.WaitForAssertion(() => Assert.True(handle.Attached));

        await component.Instance.DisposeAsync();

        Assert.True(handle.DisposeRequested);
    }

    [Fact]
    public async Task SignInDoesNotHideAGenuineJavaScriptDisposalFailure()
    {
        var handle = CancellableJsObject.CreateJavaScriptFailure("GENUINE_SIGNIN_DISPOSAL_FAILURE");
        RegisterSignInServices(handle);

        IRenderedComponent<MkSignin> component = Render<MkSignin>();
        component.WaitForAssertion(() => Assert.True(handle.Attached));

        JSException failure = await Assert.ThrowsAsync<JSException>(
            () => component.Instance.DisposeAsync().AsTask());

        Assert.Equal("GENUINE_SIGNIN_DISPOSAL_FAILURE", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ButtonTreatsCancellationAsExpectedOnlyWhileDisposing(bool cancelInvocation)
    {
        var handle = CancellableJsObject.Create(cancelInvocation);
        Services.AddSingleton<IButtonRippleInterop>(new ButtonRippleInteropFixture(handle));

        IRenderedComponent<MkButton> component = Render<MkButton>();
        component.WaitForAssertion(() => Assert.True(handle.Attached));

        await component.Instance.DisposeAsync();

        Assert.True(handle.DisposeRequested);
    }

    [Fact]
    public async Task ButtonDoesNotHideAGenuineJavaScriptDisposalFailure()
    {
        var handle = CancellableJsObject.CreateJavaScriptFailure("GENUINE_BUTTON_DISPOSAL_FAILURE");
        Services.AddSingleton<IButtonRippleInterop>(new ButtonRippleInteropFixture(handle));

        IRenderedComponent<MkButton> component = Render<MkButton>();
        component.WaitForAssertion(() => Assert.True(handle.Attached));

        JSException failure = await Assert.ThrowsAsync<JSException>(
            () => component.Instance.DisposeAsync().AsTask());

        Assert.Equal("GENUINE_BUTTON_DISPOSAL_FAILURE", failure.Message);
    }

    private void RegisterSignInServices(CancellableJsObject handle)
    {
        Services.AddSingleton<IAuthenticationFormInterop>(new AuthenticationInteropFixture(handle));
        Services.AddSingleton<IFormInputInterop>(new FormInputInteropFixture(CancellableJsObject.CreateNoOp()));
        Services.AddSingleton<IButtonRippleInterop>(new ButtonRippleInteropFixture());
        Services.AddSingleton<IMisskeyLocalizer>(new SignInLocalizer());
        Services.AddSingleton<IMisskeyOverlayService>(new MisskeyOverlayService());
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://social.example"),
            LocalAccountsEnabled: true));
    }

    private sealed class CancellableJsObject(
        bool cancelInvocation,
        bool cancelDisposal,
        string? javascriptFailure) : IJSObjectReference
    {
        public bool Attached { get; set; }

        public bool DisposeRequested { get; private set; }

        public static CancellableJsObject Create(bool cancelInvocation) =>
            new(cancelInvocation, !cancelInvocation, null);

        public static CancellableJsObject CreateJavaScriptFailure(
            string message = "GENUINE_FORM_INPUT_DISPOSAL_FAILURE") => new(false, false, message);

        public static CancellableJsObject CreateNoOp() => new(false, false, null);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            DisposeRequested |= string.Equals(identifier, "dispose", StringComparison.Ordinal);
            return cancelInvocation && DisposeRequested
                ? ValueTask.FromException<TValue>(new TaskCanceledException("Circuit disposal cancelled JavaScript invocation."))
                : ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            DisposeRequested = true;
            if (cancelDisposal)
            {
                return ValueTask.FromException(new TaskCanceledException("Circuit disposal cancelled object disposal."));
            }

            return javascriptFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(new JSException(javascriptFailure));
        }
    }

    private sealed class FormInputInteropFixture(CancellableJsObject handle) : IFormInputInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken)
        {
            handle.Attached = true;
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DialogWindowInteropFixture(CancellableJsObject handle) : IDialogWindowInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class
        {
            handle.Attached = true;
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AuthenticationInteropFixture(CancellableJsObject handle) : IAuthenticationFormInterop
    {
        public ValueTask<IJSObjectReference> AttachSignInAsync(
            ElementReference form,
            DotNetObjectReference<MkSignin> receiver,
            string passkeyOptionsUrl,
            string passkeyAssertionUrl,
            CancellationToken cancellationToken)
        {
            handle.Attached = true;
            return ValueTask.FromResult<IJSObjectReference>(handle);
        }

        public ValueTask<IJSObjectReference> AttachSignUpAsync(
            ElementReference form,
            DotNetObjectReference<MkSignup> receiver,
            string usernameAvailabilityUrl,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IJSObjectReference> AttachInitialSetupAsync(
            ElementReference form,
            DotNetObjectReference<WelcomeSetup> receiver,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ButtonRippleInteropFixture(CancellableJsObject? handle = null) : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken)
        {
            CancellableJsObject created = handle ?? CancellableJsObject.CreateNoOp();
            created.Attached = true;
            return ValueTask.FromResult<IJSObjectReference>(created);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SignInLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "en-US";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;

        public bool TrySelectLocale(string? locale) => false;
    }
}
