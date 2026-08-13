# Blazor frontend security

## 認証境界

外部ログインを構成する場合の認証はOIDC Authorization CodeとPKCE S256を使用する。Misskey v12 local sign-inは別の同一origin `POST /api/signin`でASP.NET Core Identityのpassword、TOTP、lockout、passkey境界を通過させる。Authority、公開UI origin、callbackはruntime設定から取得し、Host header、現在位置、Tailscale hostnameから推測しない。
`/api/signin`成功時のMisskey `i` tokenとHttpOnly session cookieは責務を分離し、OIDC access tokenをMisskey tokenへ流用しない。

token、authorization code、Cookie、client secretはbrowser storage、URLの永続状態、ログ、telemetry、Playwright artifactへ保存しない。

password reset tokenとemail確認tokenはASP.NET Core Identityが生成し、PostgreSQLにはSHA-256 hash、期限、要求時刻、単回claim時刻だけを保存する。メールURLではtokenをquery/pathではなくfragmentへ置くため、HTTP request、reverse proxy、server access logには送られない。Razorは空のhidden fieldだけをserver-renderし、型付きES moduleがfragmentを検査してfieldへ移した直後に`history.replaceState`でaddress barから消去する。complete endpointはsame-origin POST、antiforgery、body-size上限、rate limit、`Cache-Control: no-store`を必須とする。

全7認証mutationには16 KiBの`IRequestSizeLimitMetadata`と個別の`IFormOptionsMetadata`を付与する。

KestrelはContent-Lengthの有無にかかわらずtransport上限を適用し、form parserにはkey、value、entry数、buffer、multipartの別上限を適用する。

OpenIddictがendpoint handlerより先にbody formを読む場合も、認証formに限定して`InvalidDataException`を413 Problem Detailsへ変換する。

未知長のchunked bodyを20 KiB送るAPI統合試験により、この前段経路が500にならず副作用前に拒否されることを検証する。

request endpointは存在しないusername/email、未確認でないaccount、cooldown中のaccountでも同じ202 bodyを返し、account enumerationをHTTP status/bodyへ出さない。SMTP失敗時は該当hashと一致する未claim reservationだけを削除し、別の再送reservationを消さない。reset・確認ともDBの条件付きUPDATEで単回claimし、replayと並行二重消費を拒否する。

Tailnetでは`Frontend:PublicBaseUri`と`Frontend:Authority`だけを外部HTTPS originへ切り替える。`Federation:PublicBaseUri=http://activitypub`は隔離Pasture内の不変Actor IRIとして維持する。

## 登録招待とCAPTCHA

招待専用登録は`RegistrationProtection:InvitationRequired`で明示的に有効化する。

管理APIは32文字alphabetへ偏りなく写像した暗号学的乱数から26文字、130 bitのcodeを発行し、PostgreSQLにはSHA-256 hash、発行者、発行時刻、期限、reservation、消費者だけを保存する。

Misskey 12.119.2上流の8文字codeとは意図的に異なるため、`admin/invite`のexact differential compatibilityはblockedとして記録する。

codeは発行時のresponseに一度だけ含まれ、監査ログ、構造化ログ、telemetryにはcodeもhashも記録しない。

reserveとconsumeは条件付きUPDATEで排他し、同時利用、再利用、期限切れ、失効したreservationを拒否する。

DBはhashの32 byte長、発行後の期限、reservation 3列のall-or-none、消費時刻と消費usernameのall-or-noneをcheck constraintでも強制する。

account作成前にASP.NET Core Identityへ登録された全password validatorを実行し、拒否時はreservationを解放する。

その後の招待consumeとIdentity user insertは、同じscoped `LocalIdentityDbContext`と同じPostgreSQL transactionで実行する。

password validatorの再評価、username・email一意制約、DB例外、process終了のいずれがcommit前に発生しても両変更をrollbackし、使用済み招待だけが残る状態を作らない。

両書込み成功後のcommitはHTTP request取消から分離して完了させ、commit済みか不明な状態を呼出元へ返さない。

tamper-evident audit ledgerへの追記はcommit後にbest-effortで行い、失敗時も招待row自身の発行・reservation・消費監査列を維持して安全なuser IDだけをerror logへ残す。

CAPTCHAは`Hcaptcha`、`Recaptcha`、`Turnstile`のいずれかを明示的に選び、site key、絶対pathのsecret file、期待hostname、1秒から30秒のtimeoutを設定する。

Productionでは期待hostnameと実在するsecret fileを必須とし、URL、port、空白を含むhostnameを起動時に拒否する。

browser scriptは固定したhCaptcha、reCAPTCHA、Turnstile originからだけ読込み、選択したproviderのoriginだけをCSPへ追加する。一時的なscript取得失敗は失敗済みPromiseとscript要素をcacheから除去し、登録dialogを閉じて再度開いた場合だけ安全に再取得できるようにする。

server検証先はhCaptchaの`https://api.hcaptcha.com/siteverify`、reCAPTCHAの`https://www.google.com/recaptcha/api/siteverify`、Turnstileの`https://challenges.cloudflare.com/turnstile/v0/siteverify`へ固定し、設定から検証URLを差し替えられない。

serverはhCaptchaへsite keyも送信し、provider応答のhostnameと、応答にsite keyがある場合はその値を照合する。

secret fileは4 KiB、provider応答は展開後32 KiBへ制限し、timeout、通信失敗、非200、oversize、不正JSONをfail-closedで`CAPTCHA_UNAVAILABLE`へ分類する。

CAPTCHA responseはhidden form fieldだけに保持し、C#、SignalR、ログ、telemetryへ渡さず、失敗時とwidget破棄時に消去する。

実hCaptcha・reCAPTCHA・Turnstile serviceとのlive検証は未実施であり、fixture試験だけからprovider実運用成功を宣言しない。

## Browser policy

frontend responseは`script-src 'self' 'wasm-unsafe-eval'`、same-origin `style-src-elem`、制限した`connect-src`、`frame-ancestors 'none'`、`object-src 'none'`を設定する。一般の`unsafe-eval`と`unsafe-inline` scriptは許可しない。

Misskeyのlayout、theme、motionに必要なstyle attributeだけを許可し、値は型付きinteropで数値化またはallow-list検証する。

WASMのAPI／WebSocket接続状態は既存のMisskey status barとstream indicatorで表示する。Interactive Serverの`components-reconnect-modal`や`/_blazor`は本番経路へ含めない。

MFMは固定version parserの検証済みASTからRenderTreeへ変換し、未信頼HTMLを`MarkupString`へ直接渡さない。link、画像、theme値は危険なscheme、userinfo、CSS injectionを拒否する。

動的画像URLは`SameOriginMediaUrl`を通し、`/`で始まる同一originの保存済みmediaまたはproxy/cache pathだけをDOMとCSSへ出力する。

Announcement、instance背景、logo、avatar、custom emoji、reaction emoji、note mediaが絶対HTTP(S) URLを受け取っても、browserからremote originへ直接接続しない。

remote mediaを表示する場合はbackendが`SafeFederationHttpClient`、policy、MIME/size検査、ClamAV、S3保存を通した同一origin URLへ投影する必要がある。

管理者がAnnouncementへ絶対HTTP(S)画像URLを指定した場合は、Announcementの永続化前に共通の安全なFederation HTTP境界で取得し、`Reject`と`RejectMedia`を適用してから通常のMedia処理へ渡す。

取込成功時だけ`/media/{id}`をAnnouncementへ保存し、Mediaが無効な環境では`MEDIA_UNAVAILABLE`を返してAnnouncementを作成しない。

有効期限内かつ未削除のAnnouncementが参照するMediaはGC候補から除外し、期限切れまたは削除後は通常の保持期間を経て回収できる。

`AnnouncementImageImporterTests`、`AnnouncementCompatibilityTests`、`MediaGarbageCollectionProtectsOnlyLiveAnnouncementImages`が、無通信のpolicy拒否、タイムアウトの制御済み失敗、503時の副作用0件、参照中のGC保護を検証する。

CSPの`img-src 'self' data:`を外部画像表示のために緩和しない。

## JavaScript依存

MFMとMatter.jsはlockfileへ固定し、生成スクリプトがversion、license、source digestを検証する。実行時の外部context、CDN、任意origin importは禁止する。

全JS moduleは型付きC# interfaceの背後に置き、Vue lifecycle、Vue Router、Pizzax、動的文字列評価、`eval`を含めない。

`MkUpdated`のrelease notesは固定HTTPS origin `misskey-hub.net`と固定pathだけを許可する。native click task内で`noopener,noreferrer`付きの別windowを開くため、Server event roundtripによるpopup拒否とreverse-tabnabbingを避ける。`lastVersion`と`theme`だけを専用typed interopで扱い、token、Cookie、認証claimをJavaScriptへ渡さない。

## 検証済み範囲と残件

API試験はWASM CSP、外部runtime config、Vue/Vite/Interactive Server非混入を検証する。旧Interactive Server Tailnet試験は移行前の履歴であり、WASM本番経路の成功証拠へ流用しない。

password reset・email確認についてはPostgreSQL上のexpiry、cooldown、replay、並行claim、SMTP失敗後のreservation解放と、Blazor DOM・leave transition競合、API antiforgeryを自動検証した。実SMTP providerでのTLS証明書、配送性、bounce、Chromium/Firefox/WebKitのリンク操作は未検証である。

全routeの認可、private media、Service Worker cache、plugin/AiScript sandboxは個別の試験結果だけを根拠にし、WASM起動結果から安全性を外挿しない。
