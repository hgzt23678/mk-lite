# デプロイ

## Artifact

DockerfileはNode 22 build baseと.NET 10 SDK/runtime baseをdigest固定し、npm/NuGetのlock fileを使用する。
frontendはbuild stageだけで生成し、runtimeには静的assetだけを配置する。
runtimeはUID 1654、read-only root、`/tmp` tmpfs、全capability dropで動作する。
Ubuntu packageの直接versionは固定しているが、APTのtransitive packageとCompose dependency imageはregistry更新の影響を受けるため、release時にSBOMと最終image digestを保存する。

## 配置順序

1. DB backup/PITR状態とschema compatibility rangeを確認する。
2. expansion migrationを一回だけ専用jobで適用する。
3. 新Workerが旧形式work itemを読めることを確認し、少数canaryを起動する。受付時に新しいwork metadataを生成するreleaseではWorkerをAPIより先に更新する。
4. lease、retry、remote statusを確認して旧WorkerをSIGTERMする。
5. 新APIをcanary投入し、readiness、署名、Inbox、queue ageを確認してrolling replacementする。
6. migration/backfillが必要ならbounded jobで実施し、両version読取を検証する。
7. 次release以降でcontract migrationを適用する。

API設定は`Workers__InboxEnabled=false`、`Workers__DeliveryEnabled=false`、`Media__GarbageCollectionEnabled=false`とする。
Workerは公開trafficを受けないserviceとして必要なworkerのみ有効化する。
同じbinaryで両方を有効化する構成も可能である。

## Secrets and network

- `ConnectionStrings__ActivityPub`、AWS credentials、Vault token、Data Protection証明書passwordをSecret Store/fileから注入する。production DB接続は`SSL Mode=VerifyFull`、mediaはHTTPS endpointとserver-side encryptionを必須にする。
- TLSはtrusted reverse proxyで終端し、`Http__TrustedProxies`に固定IPだけを列挙する。
- federation egressはTCP 443に限定し、localhost、RFC1918、link-local、metadata rangeをnetwork policyでも拒否する。
- PostgreSQL、S3、Vault、ClamAV、OTLPはprivate networkと相互TLS/認証を使う。
- productionで`RequireHttps=false`、`AllowDevelopmentLoopback=true`、`DevelopmentRestrictToAllowedHosts=true`、空でない`DevelopmentAllowedHosts`、placeholder origin、HTTP authorityを許可しない。

`deploy/pasture`は開発専用であり、本番deploy overlayとして使用しない。

[本番設定例](../deploy/appsettings.Production.example.json)に秘密値は含まれない。
実環境ではS3/Vault endpoint、OIDC/CORS/AllowedHosts、retention、timeout、resource limitを明示する。

## Frontend と OIDC

`Frontend__Enabled=true` のとき `/app/` から frontend を配信し、`/api/frontend/config` は client secret を含まない公開設定だけを返す。
callback URI と logout return URI はrequest Hostから生成しない。
通常は`Federation__PublicBaseUri`と`Authentication__Authority`を使用し、TLS終端originが別の場合だけ`Frontend__PublicBaseUri`と`Frontend__Authority`を明示する。
前者はpath、query、fragmentを持たないcanonical originでなければならず、本番では両方にHTTPSを要求する。

Misskey v12 clientのローカル認証は`POST /api/signin`を使用する。JSONとmultipart formを受理し、ASP.NET Core Identityのpassword、TOTP、lockout、passkey境界を通過した成功時だけ専用`mk_` tokenと`__Host-activitypub-oauth-session` HttpOnly/Secure cookieを発行する。
ブラウザーはcookieをsession責務に、Misskey REST/Streaming clientは専用tokenをAPI責務に使用する。OIDC access tokenを`i`へ流用せず、token値をlocalStorage、ログ、telemetryへ保存しない。
外部OIDC providerを有効にする場合だけ、public client `Frontend__ClientId`へAuthorization Code + PKCEと上記callback URIを登録する。client secretはbrowser frontendへ設定しない。

以前のTailnet Keycloak theme資料は移行履歴として残すが、現在のlocal account有効構成の認証経路では使用しない。現行試験は`/api/signin`、既存passkey endpoint、PostgreSQL Identity状態、session cookie、Misskey tokenを直接照合する。

`Frontend__SourceUrl` は、運用中の改変版と完全に一致する commit/tag の対応 source を HTTPS で示す。
本番では placeholder host を拒否する。
AGPL notice と source link は左 navigation に常時表示する。

frontend response には CSP、`frame-ancestors 'none'`、`Referrer-Policy: no-referrer`、`nosniff`、Permissions-Policy を付ける。
CSP の `connect-src` は同一 origin と構成済み OIDC authority に限定する。

## Rollback

schema expansion中は旧binaryへrollbackできる。新形式へのdata migration後はroll-forwardを優先し、旧binaryが読めることを事前に確認する。contract後のbinary rollbackは許可しない。配送はPostgreSQLに残るため、Workerを停止してもjobは失われず、lease expiry後に互換Workerが回収する。

`eng/rolling-deployment-test.sh` は Caddy の readiness health check、canary API/Worker、専用 migration job、連続 HTTP probe、Activity と Delivery の件数照合を実行する。

同一 digest の orchestration smoke は通過済みである。

Release 判定では `OLD_IMAGE` と `NEW_IMAGE` に実際の異なる registry digest を指定する。
