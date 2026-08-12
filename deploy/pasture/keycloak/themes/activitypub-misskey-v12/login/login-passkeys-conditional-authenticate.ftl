<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=realm.registrationAllowed && !registrationDisabled??; section>
    <#if section = "header">
        ${msg("doLogIn")}
    <#elseif section = "form">
        <form id="webauth" action="${url.loginAction}" method="post" hidden>
            <input type="hidden" id="clientDataJSON" name="clientDataJSON">
            <input type="hidden" id="authenticatorData" name="authenticatorData">
            <input type="hidden" id="signature" name="signature">
            <input type="hidden" id="credentialId" name="credentialId">
            <input type="hidden" id="userHandle" name="userHandle">
            <input type="hidden" id="error" name="error">
        </form>
        <#if authenticators??>
            <form id="authn_select" hidden>
                <#list authenticators.authenticators as authenticator>
                    <input type="hidden" name="authn_use_chk" value="${authenticator.credentialId}">
                </#list>
            </form>
        </#if>

        <div id="kc-passkey-signin" class="eppvobhk _monolithic_ totpLogin"
             data-misskey-component="MkSignin">
            <div class="auth _section _formRoot">
                <div class="avatar" aria-hidden="true"></div>
                <div class="2fa-signin securityKeys">
                    <div class="twofa-group tap-group">
                        <p>${msg("tapSecurityKey")}</p>
                        <#if authenticators?? && shouldDisplayAuthenticators?? && shouldDisplayAuthenticators>
                            <div class="mk-keycloak-authenticator-list">
                                <#list authenticators.authenticators as authenticator>
                                    <div class="mk-keycloak-authenticator">
                                        <strong>${kcSanitize(authenticator.label)?no_esc}</strong>
                                        <#if authenticator.transports?? && authenticator.transports.displayNameProperties?has_content>
                                            <span>
                                                <#list authenticator.transports.displayNameProperties as nameProperty>
                                                    ${msg(nameProperty)}<#sep>, </#sep>
                                                </#list>
                                            </span>
                                        </#if>
                                    </div>
                                </#list>
                            </div>
                        </#if>

                        <form id="kc-form-login" action="${url.loginAction}" method="post" hidden>
                            <div class="matxzzsk _formBlock">
                                <div class="label"></div>
                                <div class="input">
                                    <div class="prefix" aria-hidden="true">@</div>
                                    <input id="username" name="username" type="text"
                                           value="${(login.username!'')}"
                                           placeholder="<#if realm.loginWithEmailAllowed>${msg("usernameOrEmail")}<#else>${msg("username")}</#if>"
                                           autocomplete="username webauthn" autofocus dir="ltr"
                                           aria-invalid="<#if messagesPerField.existsError('username')>true<#else>false</#if>">
                                    <div class="suffix" data-host-suffix aria-hidden="true">@</div>
                                </div>
                                <div class="caption">
                                    <#if messagesPerField.existsError('username')>
                                        <span class="mk-keycloak-field-error" aria-live="polite">
                                            ${kcSanitize(messagesPerField.get('username'))?no_esc}
                                        </span>
                                    </#if>
                                </div>
                            </div>
                        </form>

                        <button id="authenticateWebAuthnButton" class="bghgjjyj _button primary"
                                type="button" hidden>
                            <span class="ripples" aria-hidden="true"></span>
                            <span class="content">${msg("retry")}</span>
                        </button>
                    </div>
                </div>
            </div>
        </div>

        <script type="module">
            <#outputformat "JavaScript">
            import { authenticateByWebAuthn } from ${(url.resourcesPath + "/js/webauthnAuthenticate.js")?c};
            import { initAuthenticate } from ${(url.resourcesPath + "/js/passkeysConditionalAuth.js")?c};
            const authButton = document.getElementById("authenticateWebAuthnButton");
            const input = {
                isUserIdentified: ${isUserIdentified},
                challenge: ${challenge?c},
                userVerification: ${userVerification?c},
                rpId: ${rpId?c},
                createTimeout: ${createTimeout?c},
                errmsg: ${msg("webauthn-unsupported-browser-text")?c}
            };
            authButton.addEventListener("click", () => authenticateByWebAuthn(input), { once: true });
            document.addEventListener("DOMContentLoaded", () => initAuthenticate({
                isUserIdentified: ${isUserIdentified},
                challenge: ${challenge?c},
                userVerification: ${userVerification?c},
                rpId: ${rpId?c},
                createTimeout: ${createTimeout?c},
                errmsg: ${msg("passkey-unsupported-browser-text")?c}
            }, available => {
                if (available) {
                    document.getElementById("kc-form-login").hidden = false;
                } else {
                    authButton.hidden = false;
                }
            }));
            </#outputformat>
        </script>
    <#elseif section = "info">
        <#if realm.registrationAllowed && !registrationDisabled??>
            <span>${msg("noAccount")} <a class="_textButton" href="${url.registrationUrl}">${msg("doRegister")}</a></span>
        </#if>
    </#if>
</@layout.registrationLayout>
