<#import "template.ftl" as layout>
<#import "passkeys.ftl" as passkeys>
<@layout.registrationLayout
    displayMessage=!messagesPerField.existsError('username')
    displayInfo=realm.password && realm.registrationAllowed && !registrationDisabled??; section>
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
                            <div class="prefix" aria-hidden="true">@</div>
                            <input id="username" name="username" type="text"
                                   value="${(login.username!'')}"
                                   placeholder="<#if realm.loginWithEmailAllowed>${msg("usernameOrEmail")}<#else>${msg("username")}</#if>"
                                   <#if !realm.loginWithEmailAllowed>pattern="^[a-zA-Z0-9_]+$"</#if>
                                   spellcheck="false" required autofocus
                                   autocomplete="${(enableWebAuthnConditionalUI?has_content)?then('username webauthn', 'username')}"
                                   aria-invalid="<#if messagesPerField.existsError('username')>true<#else>false</#if>"
                                   aria-describedby="<#if messagesPerField.existsError('username')>input-error-username</#if>"
                                   dir="ltr">
                            <div class="suffix" data-host-suffix aria-hidden="true">@</div>
                        </div>
                        <div class="caption">
                            <#if messagesPerField.existsError('username')>
                                <span id="input-error-username" class="mk-keycloak-field-error" aria-live="polite">
                                    ${kcSanitize(messagesPerField.get('username'))?no_esc}
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
        </form>
        <@passkeys.conditionalUIData />
    <#elseif section = "info">
        <#if realm.password && realm.registrationAllowed && !registrationDisabled??>
            <span>${msg("noAccount")} <a class="_textButton" href="${url.registrationUrl}">${msg("doRegister")}</a></span>
        </#if>
    </#if>
</@layout.registrationLayout>
