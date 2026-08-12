<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('password'); section>
    <#if section = "header">
        ${msg("doLogIn")}
    <#elseif section = "form">
        <form id="kc-form-login" class="eppvobhk _monolithic_"
              data-misskey-component="MkSignin" action="${url.loginAction}" method="post">
            <div class="auth _section _formRoot">
                <div class="avatar" aria-hidden="true"></div>
                <div class="normal-signin">
                    <div class="matxzzsk _formBlock">
                        <div class="label"></div>
                        <div class="input">
                            <div class="prefix" aria-hidden="true"><i class="fas fa-lock"></i></div>
                            <input id="password" name="password" type="password"
                                   placeholder="${msg("password")}" required autocomplete="current-password"
                                   autofocus aria-invalid="<#if messagesPerField.existsError('password')>true<#else>false</#if>"
                                   aria-describedby="<#if messagesPerField.existsError('password')>input-error-password</#if>">
                            <div class="suffix">
                                <button class="_button password-toggle" type="button"
                                        data-mk-password-toggle aria-controls="password"
                                        aria-label="${msg("showPassword")}" aria-pressed="false"
                                        data-label-show="${msg("showPassword")}"
                                        data-label-hide="${msg("hidePassword")}">
                                    <i class="fas fa-eye" aria-hidden="true"></i>
                                </button>
                            </div>
                        </div>
                        <div class="caption">
                            <#if messagesPerField.existsError('password')>
                                <span id="input-error-password" class="mk-keycloak-field-error" aria-live="polite">
                                    ${kcSanitize(messagesPerField.get('password'))?no_esc}
                                </span>
                            </#if>
                            <#if realm.resetPasswordAllowed>
                                <a class="_textButton" href="${url.loginResetCredentialsUrl}">${msg("doForgotPassword")}</a>
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
        </form>
    </#if>
</@layout.registrationLayout>
