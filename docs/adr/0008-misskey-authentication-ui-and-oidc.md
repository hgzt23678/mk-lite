# ADR 0008: Misskey認証UIとOIDC境界

## Status

Accepted for implementation on 2026-08-04. Local Misskey v12 sign-in is implemented through `POST /api/signin`; browser authenticator and external-provider coverage remain `in-progress`.

## Context

固定したMisskey 12.119.2 clientは`MkSignin.vue`と`MkSignup.vue`の中でusername、password、TOTP、WebAuthn、招待、CAPTCHA、email確認を扱い、`MkModalWindow.vue`のDOMとmotionで表示する。

Blazor welcomeはlocal accountが有効な構成ではMisskeyのcredential formを表示し、`POST /api/signin`へ同一originで送信する。外部OIDCは明示設定時だけ別経路として利用する。元formを描画してpasswordを捨てる実装、ROPC/password grantへ変換する実装、Keycloak既定画面をMisskey画面と数える実装は採用しない。

ASP.NET Core Identityはuser、password、claim、token、email確認、lockout、2FAを管理する公式基盤である。OpenIddict 7.6は.NET 10をサポートし、custom authorization endpointのpass-through、authorization request caching、ASP.NET Core Identityと同じ`IdentityDbContext`または別storeとの統合を提供する。

参照：

- [ASP.NET Core Identity overview](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
- [ASP.NET Core passkeys/WebAuthn](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0)
- [ASP.NET Core TOTP](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-enable-qrcodes?view=aspnetcore-10.0)
- [ASP.NET Core account confirmation and recovery](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm?view=aspnetcore-10.0)
- [OpenIddict ASP.NET Core integration](https://documentation.openiddict.com/integrations/aspnet-core)
- [OpenIddict EF Core integration](https://documentation.openiddict.com/integrations/entity-framework-core)

## Decision

Misskey UIはRazor Componentsが所有し、`MkSignin`、`MkSignup`、`MkForgotPassword`、各dialogと`signup-complete`を上流DOM/class/CSS/state transitionから移植する。

local credentialの正本にはASP.NET Core Identityを使用する。password hash、lockout counter、email confirmation token、password reset token、TOTP、recovery code、passkey credentialを独自実装しない。

Blazor circuitからresponse cookieを書き換えない。credentialを含むformはsame-originの通常HTTP POSTとし、antiforgery、body size、rate limitを適用する。password、TOTP、WebAuthn assertionはlog、telemetry、URL、Blazor render batchへ含めない。失敗時は安全なerror codeだけをqueryへ戻す。

Misskey local browser sessionは次の順序で確立する。

1. `/app/auth/login`がMisskeyの`MkSigninDialog`相当routeを表示する。
2. formがJSONまたはmultipartで`POST /api/signin`へ送信され、rate limitとbody制限を通過する。
3. ASP.NET Core Identityがpassword、lockout、TOTPを検証し、passkey選択時は既存のWebAuthn challenge/assertion endpointへ分岐する。
4. 成功時に専用Misskey `mk_` tokenをhash-backed storeへ発行し、同時にHttpOnly/Secure frontend session cookieを発行する。
5. browserはcookieをsessionとして使用し、未改変Misskey clientはレスポンスの`i`をREST/Streaming Bearer tokenとして使用する。

外部OIDCを構成した場合のAuthorization Code + PKCEはこのlocal pathとは別の明示的な認証境界として維持する。

外部OIDC providerはIdentity external loginとしてlinkできるようにする。外部providerを利用する場合も、account linking、Actor対応、consentをlocal identity recordへ記録し、provider tokenをbrowser storageへ渡さない。

Identity tableはPostgreSQLへ永続化し、Federation aggregateと同じbackup/PITR対象にする。LocalActorとVault keyの作成は`Pending -> Provisioning -> Active | Failed`のdurable provisioning recordで調停する。Activeになる前のaccountは投稿、配送、token発行を行えない。再試行は冪等であり、username、identity user、Actor IRIにDB一意制約を置く。外部Vault操作をDB transaction成功と偽らない。

password resetとemail確認はASP.NET Core Identity tokenを外部用Base64url tokenへ包み、外部tokenのSHA-256 hashだけを専用PostgreSQL tableへ保存する。URLはupstreamのpath codeからfragmentへ意図的に変更し、proxy/access logへtokenを渡さない。browserがfragmentを空のhidden fieldへ移して即座にhistoryから除去し、same-origin antiforgery POSTで完了する。cooldown、expiry、送信失敗時のhash一致reservation解放、条件付きUPDATEによる単回claimを共通方針とする。

## UI fidelity gate

- signin/signupを開くbutton、overlay stack、370x400と366x500のwindow、header、focus trap、focus復帰をoracleと比較する。
- input、caption、availability、password strength、retype、ToS、CAPTCHA、2FA、WebAuthn、processing、error、email pendingを状態別に比較する。
- modal enter/leaveの開始、中間、終了、Escape、連続close、route離脱、reduced motionを3 browserで測る。
- normal POST後にIdentity state、OIDC request/code/session、LocalActor provisioning stateを照合する。
- invalid password、unknown user、lockout、replayed 2FA ticket、state/nonce mismatch、redirect URI攻撃を自動試験する。
- reset・確認tokenのexpiry、replay、並行消費、再送cooldown、SMTP失敗、URL/log非露出を自動試験する。

## Consequences

元UIと安全なOIDCを両立できる一方、registration/loginはfrontendだけでは完結しない。Identity schema、provisioning worker、email sender、Vault failure recovery、authorization endpointを一つの縦スライスとして完成させる必要がある。

ASP.NET Core IdentityまたはOpenIddictの既定UIはproduction routeへ出さず、protocolとcredential検証だけを利用する。Misskeyと異なる見た目をfallbackとして表示しない。
