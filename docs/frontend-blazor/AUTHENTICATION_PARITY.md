# Misskey 12.119.2認証UIの移植状態

## 判定基準

認証UIの基準は、commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`のSFCである。

現行Vue接続版の`MkSignin.vue`と`MkSignup.vue`は比較oracleであり、認証wire契約はMisskey v12の`POST /api/signin`と既存ASP.NET Core Identity境界を基準にする。

inventory生成処理は、該当する変更済みSFCについて固定upstreamを別に解析し、`upstreamContract`へprops、emits、slots、directives、API、browser API、DOM class、SCSS selectorとdeclarationを保存する。

## Sourceごとの状態

| Upstream source | Razor target | 状態 | 現在の自動証拠 | 未完了条件 |
|---|---|---|---|---|
| `components/MkSignin.vue` | `Components/MkSignin.razor` | in-progress | 固定DOM/class/CSS、native validationとautocomplete、username hint、same-origin avatar、password reveal、Caps Lock、TOTP、passwordless条件DOM、password-bound WebAuthn、safe alert、全props、`/api/signin` multipart、専用Misskey token、HttpOnly session cookie、3 browserのfocus・responsive・opaque panel・normal/reduced motion | provider固有Twitter/GitHub/Discord capability、実browser authenticator enrollment、Tailnet実資格情報でのlockout/session試験 |
| `components/MkSigninDialog.vue` | `Components/MkSigninDialog.razor` | implemented | 370×400 geometry、localized header、`autoSet`、`message`、header pointer・Escape・背景pointer・route破棄の差異、`done`→`closed`と`cancelled`→`closed`、normal/reduced motion、focus復帰を3 browserで確認 | なし |
| `components/MkSignup.vue` | `Components/MkSignup.razor` | in-progress | invitation-firstのDOM順序、hash化・期限付き・単回招待、Identity作成との同一transaction、usernameとemailの実Identity可用性、password強度、再入力、全locale caption、`autoSet`、CAPTCHA-aware submit gating、失敗時reset、hash化・期限付き・単回email確認、Chromium・Firefox・WebKit | disposable・MX・SMTP policy、外部live CAPTCHA、live SMTP、3 browserでのemail確認完了、実Actor作成副作用 |
| `components/MkCaptcha.vue` | `Components/MkCaptcha.razor` | in-progress | waiting spanとprovider containerのDOM順序、dark theme、hCaptchaとreCAPTCHA分岐、callback、expiry・error・登録失敗時reset、dispose、固定originのserver検証、Chromium・Firefox・WebKit | 外部live hCaptcha・reCAPTCHAによるprovider availabilityとproduction keyの検証 |
| `components/MkSignupDialog.vue` | `Components/MkSignupDialog.razor` | in-progress | 366×500 geometry、localized header、`_monolithic_ > _section`、`autoSet`、`done`、`closed` | 実登録とemail待ち後の全event順序 |
| `components/MkForgotPassword.vue` | `Components/MkForgotPassword.razor` | implemented | `bafeceda`と`bafecedb`、上流CSS、focus、keyboard、responsive、opaque panel、motion、25 locale、fixture配信、hash化した期限付きtoken、cooldown、並行単回消費 | 外部live SMTPは未実測であり、成功を宣言しない |
| `pages/signup-complete.vue` | `Pages/SignupComplete.razor` | implemented | processing root、alert dialog、focus、leave後のAPI実行、25 locale、fragment token消去、hash化・期限付き・単回確認、session確立、replay拒否 | 外部live SMTPは未実測であり、成功を宣言しない |
| `pages/reset-password.vue` | `Pages/ResetPassword.razor` | implemented | sticky headerとform階層、responsive、keyboard submit、25 locale、fragment token消去、password更新、単回消費、tokenなしのdialog遷移 | 外部live SMTPは未実測であり、成功を宣言しない |

password recoveryとemail確認の3 sourceは、UI契約、HTTP契約、fixture配信、PostgreSQL副作用を分離して検証したため`implemented`へ変更した。

`MkSigninDialog.vue`は表示、motion、focus、全終了経路のevent契約を独立検証したため`implemented`へ変更した。

`MkSignin.vue`は入力と2FAのUI契約を移植し、通常送信を`/api/signin`へ接続した。Misskey v12 legacy WebAuthn payloadも`/api/signin`でIdentityの保護済みchallenge stateへ接続し、Blazorのbrowser credential serializationは同じIdentity境界の専用passkey endpointを利用する。provider固有外部login、実authenticator enrollment、Tailnet実資格情報でのlockout/session試験が残るため`in-progress`のままである。

登録componentは、外部live CAPTCHA、外部live SMTP、実Actor作成など各行の未検証契約があるため`in-progress`のままである。

route存在、上流CSS取込み、modal表示のいずれか一つだけでは、認証状態と永続副作用を裏付けない。

## 固定upstreamから抽出した契約

`MkSignin.vue`は`/api/signin`と`users/show`を呼び、username、password、TOTP、WebAuthn security key、Twitter、GitHub、Discordの条件分岐を持つ。`/api/signin`成功時はMisskey `i` tokenとHttpOnly browser sessionを別責務として発行する。

DOM契約には`eppvobhk`、`normal-signin`、`2fa-signin`、`tap-group`、`totp-group`、`social`が含まれる。

SCSS契約は`.eppvobhk > .auth > .avatar`の寸法、背景、位置、cover、円形borderを含む。

`MkSignup.vue`は`username/available`、`email-address/available`、`signup`、`signin`を呼ぶ。

DOM契約には招待code、usernameとemailの全状態、password強度、再入力、利用規約、CAPTCHA、submitが含まれる。

SCSS契約は`.qlvuhzng .captcha`のmarginを含む。

招待制ではwelcomeの登録入口を閉じず、上流と同じく登録dialogを開いて招待codeを最初に入力させる。

招待codeは平文をDBへ保存せず、期限、予約、消費、取消、監査状態をPostgreSQLへ保存する。

codeは偏りのない32文字alphabetから26文字、130 bitで生成する。上流Misskey 12.119.2の8文字codeとのwire差異は安全性のための意図的な差異であり、exact differential compatibilityには数えない。

PostgreSQL migrationはhash長、期限順序、reservation 3列、消費2列の整合性を4 check constraintで強制し、実PostgreSQLへの不正INSERT拒否を回帰試験する。

password policyを含む事前validationの完了後、招待消費とIdentity user作成を同じtransactionで確定し、競合、Identity拒否、DB例外では両方をrollbackする。

`MkCaptcha.vue`のwaiting span、`MkEllipsis`、provider containerの順序を維持し、browser callbackをserver認可の代用にはしない。

hCaptchaは固定upstreamと同じ`js.hcaptcha.com`を使用する。

reCAPTCHAは本番標準providerの`google.com`と`gstatic.com`へ固定し、固定upstreamの`recaptcha.net`とは意図的に異なる。

この差はprovider originを暗黙切替しないためのセキュリティ境界であり、DOM、CSS class、callback、reset挙動は変更しない。

これらは`artifacts/frontend-inventory/vue-to-blazor-mapping.json`から検査でき、現行Vue接続版の差分とは分離されている。

password resetのHTTP adapterはsame-originの`/app/auth/password-reset/request`と`/app/auth/password-reset/complete`を使用する。

`MkForgotPassword.razor`は上流と同じ`bafeceda > .main._formRoot`、2個の`MkFormInput._formBlock`、`bafeceda > .sub`、`bafecedb`を描画する。

上流に存在しないemail prefix iconを削除し、usernameのpattern、autofocus、送信buttonのclassを固定SFCへ揃えた。

表示文言とaccessible labelは`IMisskeyLocalizer`を通し、25 localeの上流fallbackを利用する。

reset tokenはupstreamのpath parameterへ置かずURL fragmentで受け取り、JavaScriptでformへ移した直後にhistoryから消去する。

proxyとaccess logへのtoken露出を避けるためのセキュリティ差分であり、`knownGaps`と回帰試験へ明示する。

email確認も同じ理由で、upstreamの`/signup-complete/:code`をproduction routeへそのまま移さず、`/app/signup-complete#token`で受け取る。

browser moduleはtokenをhidden formへ移した直後にfragmentをhistoryから消去し、serverはhashだけを永続化する。

email確認APIは、上流の`await os.alert(...)`と同じくpromptのleave完了後にだけ開始する。

この順序により、古いdialogの完了callbackが次のerror dialogを削除する競合を防ぐ。

fragment-only routeはproxyとaccess logへsecretを渡さない意図的なセキュリティ差分であり、表示DOMとmotionは変更しない。

## Welcomeからの入口

現在のproduction経路は`welcome.entrance.a.vue`を`Pages/Home.razor`へ対応させる。

登録buttonはserver由来の登録policyに従う。

招待制ではbuttonを無効化せず、対応dialog内で招待codeを要求する。

ログインbuttonはlocal accountが有効な場合に対応dialogを開く。

browser試験はbuttonからdialogまでのDOM、focus、geometry、motion、背景alphaを検査する。

`welcome.entrance.b.vue`と`welcome.entrance.c.vue`、Visitor shell内の全入口はplannedのままであり、現行のA入口だけを根拠にwelcome全体を完了とは判定しない。

## CSSとanimationの証拠

Blazor stylesheetは固定upstreamの`MkSignin.vue`、`MkSignup.vue`、`MkForgotPassword.vue`から生成する。

認証dialogは共通`MkModalWindow`のenter、leave、取消、focus復帰を利用する。

password recovery試験は開始、entered、leave、rapid closeによるenter取消、再open、focus復帰を検査する。

email確認試験はalertのenter、keyboard acknowledgement、leave完了後のHTTP送信、failure dialogへの交換を検査する。

WebAuthn試験はchallenge、assertion serialization、利用者取消、retry、TOTP fallback、成功時のclose完了後redirectを検査する。

password値のTOTP遷移中の保持、表示切替、Caps Lock検出は`auth-form.js`のbrowser-local stateだけで行い、Blazor component state、HTML value属性、telemetryへ渡さない。

認証失敗は固定されたsafe error codeだけを上流型alertへ投影し、unknown payloadやcredentialを表示しない。

modal wrapperはupstream同様に透明であり、表示面である`.body`の計算済みalphaが255であることを検査する。

この区別により、wrapperの透明性を背景透明化bugと誤判定せず、実際のpanel透過を見逃さない。

## 実測結果

2026-08-04 UTCに`auth-ui-parity.spec.ts`をPlaywright 1.62.1で実行し、Chromium、Firefox、WebKitの各3件、合計9件が成功した。

成功した範囲は、welcomeからのdialog起動、上流DOM階層、初期focus、username hintとavatar、TOTP formへの切替、usernameとpassword validation、mobile幅、modal bodyのalpha 255、openからenteredとcloseのmotionである。

追加したWebAuthn browser試験は、mock credentialを用いてchallenge、assertion serialization、取消、retry、TOTP fallback、close完了後redirectを3 engineで操作する。

2026-08-12 UTCの現行認証slice再確認では、`signin-parity.spec.ts`と`settings-admin-parity.spec.ts`をChromium 1種類で実行し、9/9成功した。`POST /api/signin`のnative Misskey error id（suspended accountを含む）のsafe presentation mapping、設定API／Apps画面、panelの計算済み背景alpha、console/pageerror/diagnostics 0を含む。これは外部provider live統合、実hardware authenticator enrollment、Misskey 12.119.2実serverとのdifferential試験を証明しない。

HTTP統合試験は、実Identity passkeyから設定済みRP IDとallow credentialを生成し、protected state Cookieの属性、不正assertionの安全な拒否、state再利用拒否を確認する。

招待制登録とCAPTCHAに限定した追加実行では、`auth-ui-parity.spec.ts`をChromium、Firefox、WebKitで各2件、合計6件実行し、6件すべて成功した。

hCaptchaとreCAPTCHAの各分岐について、invitation-firstの表示、upstream DOM順序、公式script origin、初期fail-closed、callback後の送信許可、API payload、登録失敗後のwidget resetと再送不可、上流と同じerror alert、focus、acknowledgementを照合した。

招待入力は26文字上限と26文字payloadを3 engineで照合する。

provider callbackとserver応答はfixtureであり、外部live hCaptcha・reCAPTCHAへ接続していない。

これらの試験は、実browser authenticatorの署名成功、passkey enrollment、外部live CAPTCHA、live SMTPを操作していない。

したがって、`MkSignin.vue`は`in-progress`のままであり、終了eventだけを責務とする`MkSigninDialog.vue`とは判定を分離する。

2026-08-04 UTCの専用sign-in回帰試験では、`signin-parity.spec.ts`をPlaywright 1.62.1のChromium、Firefox、WebKitで各3件、合計9件実行し、9件すべて成功した。

検証対象は、64×64の円形avatar、370×400 dialog、上流classとinput属性、password表示切替、Caps Lock、TOTP failureからのsafe alert、browser-local password復元、秘密値のDOM非直列化、header close、Escape、focus復帰、背景alpha、通常motionとreduced motionである。

機械可読な実行記録は`artifacts/frontend-signin/verification.json`、目視確認用の秘密値を含まないChromium captureは`artifacts/signin-parity-chromium.png`へ保存した。

password recoveryのcomponent testは、上流DOM、email無効branch、全25 locale、safe error、event順序を対象にする。

`PasswordResetUiTests`は、上流DOMとclass、全25 locale、秘密値非表示、dialog event順序、component lifetimeに限定した取消処理を10件で検証し、10件すべて成功した。

PostgreSQL統合試験は、hash化した期限付きtoken、password validation失敗後の再利用、成功後のreplay拒否、cooldown、expiry、並行消費で成功が1件だけになることを検証した。

設定試験はProductionで平文SMTPを拒否し、StartTLS設定を受理する。

email確認のcomponent、HTTP、PostgreSQL試験は、secretをDOMやqueryへ置かないform、dialog leave待機、fragment-only token、hash永続化、有効期限、delivery失敗時のreservation除去、cooldown、並行単回消費、確認後session、replay拒否を検証する。

メール配送は`TestPasswordResetEmailSender`によるfixtureで宛先とfragment linkを捕捉し、そのhashがPostgreSQLへ保存されたことを照合した。

外部live SMTP serverへの接続は実行していないため、live SMTP deliveryの成功は主張しない。

`password-recovery-parity.spec.ts`はPlaywright 1.62.1でChromium、Firefox、WebKitを各3件実行し、合計9件すべて成功した。

この実行は、`MkForgotPassword.vue`の`bafeceda`と`bafecedb`、`_formRoot`と`_formBlock`、username pattern、autofocus、responsive padding、上流生成CSS、modal bodyのalpha 255を照合した。

email確認では、fragmentをhistoryから直ちに除去し、alertのkeyboard操作、focus、enterとleave、leave後だけのHTTP送信、失敗後の同一pageへの新token取込み、成功後のsame-origin遷移を照合した。

password resetでは、sticky header、最大700pxのform、fragment除去、keyboard submit、localized error、成功後遷移、tokenなしでのforgot-password遷移を照合した。

rapid Escapeでは、JS attachment前の新しいdialogが背面dialogへ入力を漏らさず、enter取消後も正しい最上位overlayだけを閉じることを照合した。

browser console error、page error、未分類HTTP 4xxまたは5xx、TestHostが記録する未処理circuit例外は0件だった。

HTTP adapterの対象試験5件とPostgreSQL fixtureの対象試験6件もすべて成功した。

## 昇格条件

`implemented`へ変更するには、次の証拠をすべて揃える。

- 固定upstreamとのDOM、class、SCSS、responsive、visual differential。
- username、password、2FA、WebAuthn、招待、CAPTCHA、email確認、password resetの状態遷移。
- Chromium、Firefox、WebKitでのkeyboard、focus、pointer、motionと取消。
- HTTP response、Identity state、LocalActor、audit、session、ActivityPub副作用の照合。
- token、password、Cookie、authorization codeを含まないbrowser artifactとtelemetry。
- console error、page error、未分類404、circuit exceptionが0件。
