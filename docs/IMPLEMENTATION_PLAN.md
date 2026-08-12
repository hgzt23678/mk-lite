# Implementation plan

## Delivery strategy and current state

Misskey v12 Blazor移植の残タスクは`docs/frontend-blazor/REMAINING_TASKS.md`で確定する。frontendはMisskey v12.119.2、backendは`mei23/dolphin`固定checkoutを基準とし、backend契約のない画面をstubで埋めない。

Work proceeds in vertical slices that each include schema, application behavior, API behavior, telemetry, tests, and operations documentation.

1. [done] Establish immutable public identifiers, actors, WebFinger, NodeInfo, and paged collections.
2. [done] Persist raw inbound activities after digest, signature, ownership, addressing, and replay checks.
3. [done] Apply inbox side effects idempotently. Create/Update/Delete/Follow/Accept/Reject/Flag、Add/Remove、Like、Announce、Move と対応する Undo を aggregate と DB 制約へ反映する。
4. [done] Persist outbound activities and per-endpoint deliveries atomically, then deliver them with leased workers.
5. [done locally] Add remote discovery, safe dereferencing, key refresh, legacy signatures, and RFC 9421. External peers remain unverified.
6. [done locally] Visibility、Silence、RejectMedia、Mute、remote media proxy/cache、raw JSON purge、legal hold、admin recovery、audit を実装する。Notification subsystem と production policy approval は残る。
7. [partial] PostgreSQL、接続切断、backup restore、署名、SSRF、API、property test、fault injection、baseline load test は通過した。Mastodon、Misskey、Pleroma用のversion固定fediverse-pasture localverseを標準開発環境として構成した。双方向interopの実測、GoToSocial/PeerTube adapter、soak、rolling、production integrated restore は残る。
8. [done as procedures] Deployment and incident runbooks exist; production-environment drills remain operator gates.
9. [partial] Misskey v12 frontend の移植は別 release train で継続中。今回の認証sliceでは、ローカルIdentityを根とする`POST /api/signin`（JSON／multipart）、password／TOTP／lockout／suspended account、protected legacy WebAuthn challenge、専用Misskey tokenとHttpOnly session Cookieの分離、MiAuth `session:null`、`/settings/api`／`/settings/apps`、API／component／Chromium smokeを実装・検証した。外部provider live統合、実browser authenticator enrollment、残りの画面・API・E2Eは未完了であり、旧OIDC／Keycloak記録を現行実装の根拠にしない。

## Completion rule

A feature is not complete when only its happy-path endpoint exists. It is complete when authorization, idempotency, failure recovery, telemetry, data migration, tests, and operator controls are present.

## Performance targets

Capacity claims require explicit values for local actors, remote actors, inbox requests per second, outbound deliveries per second, recovery time, and retention. Until those are supplied, the repository reports repeatable benchmark results rather than a capacity guarantee.
