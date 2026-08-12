<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('totp'); section>
    <#if section = "header">
        ${msg("doLogIn")}
    <#elseif section = "form">
        <form id="kc-otp-login-form" class="eppvobhk _monolithic_ totpLogin"
              data-misskey-component="MkSignin" action="${url.loginAction}" method="post">
            <div class="auth _section _formRoot">
                <div class="avatar" aria-hidden="true"></div>
                <div class="2fa-signin">
                    <div class="twofa-group totp-group">
                        <p>${msg("twoStepAuthentication")}</p>

                        <#if otpLogin.userOtpCredentials?size gt 1>
                            <fieldset class="mk-keycloak-otp-choices _formBlock">
                                <legend>${msg("loginChooseAuthenticator")}</legend>
                                <#list otpLogin.userOtpCredentials as otpCredential>
                                    <label class="mk-keycloak-otp-choice" for="kc-otp-credential-${otpCredential?index}">
                                        <input id="kc-otp-credential-${otpCredential?index}" type="radio"
                                               name="selectedCredentialId" value="${otpCredential.id}"
                                               <#if otpCredential.id == otpLogin.selectedCredentialId>checked</#if>>
                                        <span>${kcSanitize(otpCredential.userLabel)?no_esc}</span>
                                    </label>
                                </#list>
                            </fieldset>
                        </#if>

                        <div class="matxzzsk _formBlock">
                            <div class="label">${msg("token")}</div>
                            <div class="input">
                                <div class="prefix" aria-hidden="true"><i class="fas fa-gavel"></i></div>
                                <input id="otp" name="otp" type="text" pattern="^[0-9]{6}$"
                                       inputmode="numeric" maxlength="6" autocomplete="one-time-code"
                                       spellcheck="false" required autofocus
                                       aria-invalid="<#if messagesPerField.existsError('totp')>true<#else>false</#if>"
                                       aria-describedby="<#if messagesPerField.existsError('totp')>input-error-otp-code</#if>"
                                       dir="ltr">
                                <div class="suffix"></div>
                            </div>
                            <div class="caption">
                                <#if messagesPerField.existsError('totp')>
                                    <span id="input-error-otp-code" class="mk-keycloak-field-error" aria-live="polite">
                                        ${kcSanitize(messagesPerField.get('totp'))?no_esc}
                                    </span>
                                </#if>
                            </div>
                        </div>

                        <button class="bghgjjyj _button _formBlock primary" name="login"
                                id="kc-login" type="submit"
                                data-default-label="${msg("doLogIn")}"
                                data-pending-label="${msg("loggingIn")}">
                            <span class="ripples" aria-hidden="true"></span>
                            <span class="content">${msg("doLogIn")}</span>
                        </button>
                    </div>
                </div>
            </div>
        </form>
    </#if>
</@layout.registrationLayout>
