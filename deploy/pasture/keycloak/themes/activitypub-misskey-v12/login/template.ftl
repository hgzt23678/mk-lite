<#--
  Keycloak 26.7.0 layout adapted to the pinned Misskey 12.119.2 MkSigninDialog shell.
  This file deliberately keeps Keycloak's url.loginAction and authentication-session
  scripts. It changes presentation only; it does not proxy or collect credentials.
-->
<#import "theme-resources.ftl" as themeResourceTags>
<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false>
<!DOCTYPE html>
<html lang="${lang}"<#if realm.internationalizationEnabled> dir="${(locale.rtl)?then('rtl','ltr')}"</#if>
      data-misskey-version="12.119.2">
<head>
    <meta charset="utf-8">
    <meta http-equiv="Content-Type" content="text/html; charset=UTF-8">
    <#if properties.meta?has_content>
        <#list properties.meta?split(' ') as meta>
            <meta name="${meta?split('==')[0]}" content="${meta?split('==')[1]}">
        </#list>
    </#if>
    <title>${msg("doLogIn")}</title>
    <link rel="icon" href="${url.resourcesPath}/img/favicon.png">
    <#if themeResources?? && themeResources.stylesCommon?has_content>
        <@themeResourceTags.renderStyles themeResources.stylesCommon url.resourcesCommonPath />
    <#elseif properties.stylesCommon?has_content>
        <#list properties.stylesCommon?split(' ') as style>
            <link href="${url.resourcesCommonPath}/${style}" rel="stylesheet">
        </#list>
    </#if>
    <#if themeResources?? && themeResources.styles?has_content>
        <@themeResourceTags.renderStyles themeResources.styles url.resourcesPath />
    <#elseif properties.styles?has_content>
        <#list properties.styles?split(' ') as style>
            <link href="${url.resourcesPath}/${style}" rel="stylesheet">
        </#list>
    </#if>
    <#if themeResources?? && themeResources.scripts?has_content>
        <@themeResourceTags.renderScripts themeResources.scripts url.resourcesPath "text/javascript" />
    <#elseif properties.scripts?has_content>
        <#list properties.scripts?split(' ') as script>
            <script src="${url.resourcesPath}/${script}" defer></script>
        </#list>
    </#if>
    <script type="importmap">
        { "imports": { "rfc4648": "${url.resourcesCommonPath}/vendor/rfc4648/rfc4648.js" } }
    </script>
    <#if scripts??>
        <#list scripts as script>
            <script src="${script}"></script>
        </#list>
    </#if>
    <script type="module">
        <#outputformat "JavaScript">
        import { startSessionPolling } from ${(url.resourcesPath + "/js/authChecker.js")?c};
        startSessionPolling(${url.ssoLoginInOtherTabsUrl?c});
        </#outputformat>
    </script>
    <#if authenticationSession??>
        <script type="module">
            <#outputformat "JavaScript">
            import { checkAuthSession } from ${(url.resourcesPath + "/js/authChecker.js")?c};
            checkAuthSession(${authenticationSession.authSessionIdHash?c});
            </#outputformat>
        </script>
    </#if>
</head>
<body class="mk-keycloak-page ${bodyClass}" data-page-id="login-${pageId}">
    <div class="qzhlnise dialog mk-keycloak-modal">
        <div class="bg _modalBg" aria-hidden="true"></div>
        <main class="content">
            <section class="ebkgoccj _narrow_ _shadow mk-keycloak-window"
                     role="dialog" aria-modal="true" aria-labelledby="kc-page-title">
                <header class="header">
                    <h1 class="title" id="kc-page-title"><#nested "header"></h1>
                </header>
                <div class="body" id="kc-content">
                    <#if displayMessage && message?has_content &&
                         (message.type != 'warning' || !isAppInitiatedAction??)>
                        <div class="mk-auth-global-feedback ${message.type}"
                             role="<#if message.type = 'error'>alert<#else>status</#if>"
                             aria-live="polite">
                            <i class="fas <#if message.type = 'error'>fa-circle-exclamation<#elseif message.type = 'warning'>fa-triangle-exclamation<#elseif message.type = 'success'>fa-circle-check<#else>fa-circle-info</#if>"
                               aria-hidden="true"></i>
                            <span class="mk-auth-global-feedback__text">${kcSanitize(message.summary)?no_esc}</span>
                        </div>
                    </#if>

                    <#nested "form">

                    <#if auth?has_content && auth.showTryAnotherWayLink()>
                        <form id="kc-select-try-another-way-form" class="mk-auth-flow-tools"
                              action="${url.loginAction}" method="post">
                            <input type="hidden" name="tryAnotherWay" value="on">
                            <button class="_textButton" id="try-another-way" type="submit">
                                ${msg("doTryAnotherWay")}
                            </button>
                        </form>
                    </#if>

                    <#if auth?has_content && auth.showUsername() && !auth.showResetCredentials()>
                        <div class="mk-auth-attempted-user">
                            <span id="kc-attempted-username">${kcSanitize(auth.attemptedUsername)?no_esc}</span>
                            <a id="reset-login" class="_textButton" href="${url.loginRestartFlowUrl}">
                                ${msg("restartLoginTooltip")}
                            </a>
                        </div>
                    </#if>

                    <#nested "socialProviders">

                    <#if displayInfo>
                        <div id="kc-info" class="mk-keycloak-info"><#nested "info"></div>
                    </#if>
                </div>
            </section>
        </main>
    </div>
</body>
</html>
</#macro>
