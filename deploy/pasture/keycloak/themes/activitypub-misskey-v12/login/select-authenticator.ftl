<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=false; section>
    <#if section = "header">
        ${msg("doLogIn")}
    <#elseif section = "form">
        <div class="eppvobhk _monolithic_ totpLogin" data-misskey-component="MkSignin">
            <div class="auth _section _formRoot">
                <div class="avatar" aria-hidden="true"></div>
                <div class="2fa-signin securityKeys">
                    <div class="twofa-group">
                        <p>${msg("loginChooseAuthenticator")}</p>
                        <form id="kc-select-credential-form" action="${url.loginAction}" method="post">
                            <div class="mk-keycloak-authentication-selections">
                                <#list auth.authenticationSelections as authenticationSelection>
                                    <button class="_borderButton _gap mk-keycloak-authentication-selection"
                                            type="submit" name="authenticationExecution"
                                            value="${authenticationSelection.authExecId}">
                                        <strong>${msg('${authenticationSelection.displayName}')}</strong>
                                        <span>${msg('${authenticationSelection.helpText}')}</span>
                                    </button>
                                </#list>
                            </div>
                        </form>
                    </div>
                </div>
            </div>
        </div>
    </#if>
</@layout.registrationLayout>
