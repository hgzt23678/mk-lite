<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=realm.registrationAllowed && !registrationDisabled??; section>
    <#if section = "header">
        ${msg("doLogIn")}
    <#elseif section = "form">
        <div id="kc-form-webauthn" class="eppvobhk _monolithic_ totpLogin"
             data-misskey-component="MkSignin">
            <div class="auth _section _formRoot">
                <div class="avatar" aria-hidden="true"></div>
                <div class="2fa-signin securityKeys">
                    <div class="twofa-group tap-group">
                        <p>${msg("tapSecurityKey")}</p>

                        <form id="webauth" action="${url.loginAction}" method="post">
                            <input type="hidden" id="clientDataJSON" name="clientDataJSON">
                            <input type="hidden" id="authenticatorData" name="authenticatorData">
                            <input type="hidden" id="signature" name="signature">
                            <input type="hidden" id="credentialId" name="credentialId">
                            <input type="hidden" id="userHandle" name="userHandle">
                            <input type="hidden" id="error" name="error">
                        </form>

                        <#if authenticators??>
                            <form id="authn_select" class="mk-keycloak-authenticator-list">
                                <#list authenticators.authenticators as authenticator>
                                    <input type="hidden" name="authn_use_chk" value="${authenticator.credentialId}">
                                    <#if shouldDisplayAuthenticators?? && shouldDisplayAuthenticators>
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
                                    </#if>
                                </#list>
                            </form>
                        </#if>

                        <button id="authenticateWebAuthnButton" class="bghgjjyj _button primary"
                                type="button" autofocus>
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
            const authButton = document.getElementById("authenticateWebAuthnButton");
            authButton.addEventListener("click", () => {
                authenticateByWebAuthn({
                    isUserIdentified: ${isUserIdentified},
                    challenge: ${challenge?c},
                    userVerification: ${userVerification?c},
                    rpId: ${rpId?c},
                    createTimeout: ${createTimeout?c},
                    errmsg: ${msg("webauthn-unsupported-browser-text")?c}
                });
            }, { once: true });
            </#outputformat>
        </script>
    <#elseif section = "info">
        <#if realm.registrationAllowed && !registrationDisabled??>
            <span>${msg("noAccount")} <a class="_textButton" href="${url.registrationUrl}">${msg("doRegister")}</a></span>
        </#if>
    </#if>
</@layout.registrationLayout>
