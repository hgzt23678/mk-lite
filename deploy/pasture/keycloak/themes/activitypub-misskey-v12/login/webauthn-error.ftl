<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=true; section>
    <#if section = "header">
        ${msg("doLogIn")}
    <#elseif section = "form">
        <div class="eppvobhk _monolithic_ totpLogin" data-misskey-component="MkSignin">
            <div class="auth _section _formRoot">
                <div class="2fa-signin securityKeys">
                    <div class="twofa-group tap-group">
                        <p>${kcSanitize(msg("webauthn-error-title"))?no_esc}</p>
                        <form id="kc-error-credential-form" action="${url.loginAction}" method="post">
                            <input type="hidden" id="executionValue" name="authenticationExecution">
                            <input type="hidden" id="isSetRetry" name="isSetRetry">
                        </form>
                        <button class="bghgjjyj _button primary" type="button" id="kc-try-again">
                            <span class="ripples" aria-hidden="true"></span>
                            <span class="content">${msg("doTryAgain")}</span>
                        </button>
                        <#if isAppInitiatedAction??>
                            <form action="${url.loginAction}" id="kc-webauthn-settings-form" method="post">
                                <button class="_textButton" type="submit" id="cancelWebAuthnAIA"
                                        name="cancel-aia" value="true">${msg("doCancel")}</button>
                            </form>
                        </#if>
                    </div>
                </div>
            </div>
        </div>
        <script type="module">
            <#outputformat "JavaScript">
            document.getElementById("kc-try-again").addEventListener("click", () => {
                document.getElementById("isSetRetry").value = "retry";
                document.getElementById("executionValue").value = ${execution?c};
                document.getElementById("kc-error-credential-form").requestSubmit();
            }, { once: true });
            </#outputformat>
        </script>
    </#if>
</@layout.registrationLayout>
