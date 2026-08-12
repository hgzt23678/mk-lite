<#import "template.ftl" as layout>
<#import "passkeys.ftl" as passkeys>
<@layout.registrationLayout
    displayMessage=!messagesPerField.existsError('username','password')
    displayInfo=realm.password && realm.registrationAllowed && !registrationDisabled??; section>
    <#if section = "header">
        ${msg("doLogIn")}
    <#elseif section = "form">
        <#if realm.password>
            <form id="kc-form-login" class="eppvobhk _monolithic_"
                  data-misskey-component="MkSignin"
                  action="${url.loginAction}" method="post" autocomplete="on">
                <div class="auth _section _formRoot">
                    <div class="avatar" aria-hidden="true"></div>
                    <div class="normal-signin">
                        <#if !usernameHidden??>
                            <div class="matxzzsk _formBlock">
                                <div class="label"></div>
                                <div class="input">
                                    <div class="prefix" aria-hidden="true">@</div>
                                    <input id="username" name="username" type="text"
                                           value="${(login.username!'')}"
                                           placeholder="<#if realm.loginWithEmailAllowed>${msg("usernameOrEmail")}<#else>${msg("username")}</#if>"
                                           <#if !realm.loginWithEmailAllowed>pattern="^[a-zA-Z0-9_]+$"</#if>
                                           spellcheck="false" required autofocus
                                           autocomplete="${(enableWebAuthnConditionalUI?has_content)?then('username webauthn', 'username')}"
                                           aria-invalid="<#if messagesPerField.existsError('username','password')>true<#else>false</#if>"
                                           aria-describedby="<#if messagesPerField.existsError('username','password')>input-error-username</#if>"
                                           dir="ltr">
                                    <div class="suffix" data-host-suffix aria-hidden="true">@</div>
                                </div>
                                <div class="caption">
                                    <#if messagesPerField.existsError('username','password')>
                                        <span id="input-error-username" class="mk-keycloak-field-error" aria-live="polite">
                                            ${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}
                                        </span>
                                    </#if>
                                </div>
                            </div>
                        </#if>

                        <div class="matxzzsk _formBlock">
                            <div class="label"></div>
                            <div class="input">
                                <div class="prefix" aria-hidden="true"><i class="fas fa-lock"></i></div>
                                <input id="password" name="password" type="password"
                                       placeholder="${msg("password")}" required autocomplete="current-password"
                                       aria-invalid="<#if messagesPerField.existsError('username','password')>true<#else>false</#if>"
                                       aria-describedby="<#if usernameHidden?? && messagesPerField.existsError('username','password')>input-error-password</#if>"
                                       <#if usernameHidden??>autofocus</#if>>
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
                                <#if usernameHidden?? && messagesPerField.existsError('username','password')>
                                    <span id="input-error-password" class="mk-keycloak-field-error" aria-live="polite">
                                        ${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}
                                    </span>
                                </#if>
                                <#if realm.resetPasswordAllowed>
                                    <a class="_textButton" href="${url.loginResetCredentialsUrl}">${msg("doForgotPassword")}</a>
                                </#if>
                            </div>
                        </div>

                        <input type="hidden" id="id-hidden-input" name="credentialId"
                               <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>>
                        <button class="bghgjjyj _button _formBlock primary" name="login"
                                id="kc-login" type="submit"
                                data-default-label="${msg("doLogIn")}"
                                data-pending-label="${msg("loggingIn")}">
                            <span class="ripples" aria-hidden="true"></span>
                            <span class="content">${msg("doLogIn")}</span>
                        </button>
                    </div>
                </div>
                <#if social?? && social.providers?has_content>
                    <div class="social _section">
                        <#list social.providers as provider>
                            <a class="_borderButton _gap" id="social-${provider.alias}"
                               href="${provider.loginUrl}">${provider.displayName!}</a>
                        </#list>
                    </div>
                </#if>
            </form>
            <@passkeys.conditionalUIData />
        </#if>
    <#elseif section = "info">
        <#if realm.password && realm.registrationAllowed && !registrationDisabled??>
            <span>${msg("noAccount")} <a class="_textButton" href="${url.registrationUrl}">${msg("doRegister")}</a></span>
        </#if>
    </#if>
</@layout.registrationLayout>
