# 検証記録

基礎検証の記録は 2026-08-04 UTC である。認証sliceとinventoryの追補は 2026-08-12 UTC、PostgreSQL queue管理・Redis高速化・Cloudflare対応は2026-08-13 UTCに検証した。

判定基準は、frontendをMisskey v12.119.2、backendを`mei23/dolphin`として分離する。ユーザー確認済みの`.cache/meidolphin`（commit `3ce200269f814547dc7dfc6b246abadf8a9c00ed`）をbackend differential evidenceの固定参照とする。

環境は Ubuntu 24.04.4、.NET SDK 10.0.302、runtime 10.0.10、Docker である。

| 検証 | 結果 | 証拠の内容 |
| --- | --- | --- |
| locked restore | 成功 | 全 project の `packages.lock.json` を locked mode で復元 |
| Release build | 成功 | analyzer と nullable を含め、警告 0、error 0 |
| .NET 自動テスト | 成功 | 現行Release全体で956 passed、0 failed、0 skipped。Domain 60、Federation 61、Media 41、Misskey Blazor 537、Moderation 2、API 161、Property 3、Persistence 91 |
| API inventory | 一部実装 | Mastodon 4.6.2は331経路中23 implemented／308 blocked。Misskey 12.119.2は321 endpoint中25 implemented／295 blocked／1 excluded。frontend ASTは静的262、streaming 14、動的14、未分類静的呼出し0 |
| PostgreSQL queue／Redis acceleration | 成功 | outbound／Inboxのstats・safe job listing、Dolphin管理API projection、Redis Pub/Sub wake-up、timeline候補ID、未読通知count、Redis未設定・接続不能時のDB fallback、cached private candidateのvisibility再検証をPostgreSQL 17／Redis 7 Testcontainersで確認 |
| Cloudflare Turnstile／R2／Proxy | 自動試験成功、live未試験 | Turnstileのhostname／action／cdata／idempotent retry／fail-closed、v12 signup DOM、R2 endpoint／upload契約、`CF-Connecting-IP`のtrusted peer／spoof防止をfixtureとChromiumで確認。実Turnstileおよび実R2 credentialによるlive通信は未実施 |
| fediverse-pasture composition | 成功 | compose commit `fecd3977`、Mastodon `v4.6.2`、Misskey `2026.6.0`、Pleroma `v2.10.0`を固定。internal `172.29.0.0/24`、実Fediverse向けrouteなし、API/Workerの完全一致host allow-listを確認 |
| fediverse-pasture実instance相互運用 | 一部成功、一部失敗 | 双方向Discovery/Follow/Accept、公開Create、Announce、Mastodon Like/Undo、Misskey reaction変更/Undo、Pleroma EmojiReact/Undo、mediaを実測。全三値表は`artifacts/interop/pasture/20260803T061125Z/interop-matrix.md` |
| remote media実測 | 成功 | Mastodon画像を.NETへ配送し、初回proxy後にS3-backed cache 1件、2回目も同じmedia ID/SHA-256。peer Delete後はcache rowが残っても404 |
| private authorization実instance | 成功 | follower署名Object/media GETは200、instance actor署名は403/404、未署名media 404、public timeline/featured/outboxと未認証REST/API projectionに混入なし、本文markerのAPI/Worker log hit 0 |
| non-public peer projection | 一部成功、一部失敗 | Followers-onlyはMastodon/Pleromaで永続化、Misskeyは202後に未保存。Mentioned-onlyはMastodon/Misskeyで永続化、PleromaはMention tag付き200後に未保存 |
| 実配送部分障害 | 成功 | Pleroma停止中にMastodon/Misskeyがattempt 1で成功。SIGKILL後の期限切れleaseを回収し、Pleromaだけattempt 2で成功、成功済み宛先は再送なし |
| delivery lease実障害試験 | 不具合検出・修正 | batchを逐次処理して後方deliveryのleaseが処理前に切れる問題、transport中heartbeat欠落、DNS `SocketException`でattemptを記録しない問題を検出。batch並行開始、delivery/domain lease heartbeat、network failure正規化を追加 |
| development federation isolation | 成功 | HTTPは列挙hostだけ、RFC1918/ULAは列挙hostだけ、未列挙public host、metadata、link-local、loopback、special rangeを拒否し、Productionが全development例外を拒否する自動テスト |
| emoji reaction integration | 成功 | Misskey Like、LitePub EmojiReact、alias EmojiReaction、custom Emoji metadata、複数reaction、重複ID、衝突ID、送受信Undo、sharedInbox/Inbox別配送をPostgreSQL/API fixtureで検証 |
| frontend 自動テスト | 一部成功 | Vue oracle inventory Vitest 11件、Blazor component 536件、現行Chromium smoke 2件が成功。生成mapping 535 source中`implemented` 329、`in-progress` 0、`blocked` 0、`planned` 0、`excluded` 206、`unclassified` 0であり、完全移植の証拠ではない |
| frontend upstream parity | 成功 | Misskey 12.119.2 client manifest 573/573 files存在、537 byte-identical、36 reviewed modifications、上流src 530/530、locale 38/38、server static asset 25/25をCI検査 |
| Vue oracle production build | 成功 | Misskey 12.119.2の比較用client sourceをViteで変換。Vue build成功はBlazor移植済みの判定へ使用しない |
| Blazor production frontend | 一部成功 | static SSR + Interactive Serverで`/app/`を公開し、Vue/Viteを実行経路から除外。`MkSignin`の`/api/signin` JSON/multipart、Misskey token、HttpOnly session、MiAuth `session:null`、`/settings/api`、`/settings/apps`、client bootstrap/directive/page-block/Nirax utilityをAPI/componentで検証。excluded 206 source、全routeの完全同等性、Vue挙動、motion全量、実browser authenticator success、外部OIDC E2Eは未完了 |
| Misskey v12 authentication slice | 成功（一部未検証） | `POST /api/signin`のJSON／multipart、Identity password、TOTP、lockout、suspended account、Misskey error IDs、専用`mk_` tokenのhash保存、HttpOnly Secure session cookie、protected legacy WebAuthn challengeのchallenge cookie・単回消費・malformed assertion拒否、MiAuth `session:null`の内部session隔離、`/settings/api`／`/settings/apps`の発行・一覧・失効をAPI／Blazor／Chromium focused testで確認。2026-08-12のsignin/settings smokeは9/9成功（console、page error、diagnostic、意図しない透明panelなし）。外部provider live統合と実browser authenticator enrollmentは未検証 |
| Property test | 成功 | 3 property、各 500 case |
| PostgreSQL migration | 成功 | clean DB に全 migration を適用し、model 差分なし |
| User Block aggregate | 成功 | inbound／outbound Blockと正確なUndo、双方向Follow解除、Mastodon／Misskey cross projection、Object／notification／通常配送抑止、Block／Undo自身の配送をDomain・PostgreSQL API試験で確認 |
| backup restore | 成功 | Testcontainers PostgreSQL で custom-format dump を別 DB へ restore し、marker と delivery を照合 |
| DB 接続切断 | 成功 | `pg_terminate_backend` 後、pool 再接続で durable delivery を claim |
| Worker lease recovery | 成功 | lease expiry 後に別 owner が claim し、旧 owner の完了を拒否 |
| emergency pause | 成功 | global pause 中は claim 0、解除後は同じ pending delivery を claim |
| delivery endpoint 変更 | 成功 | failing Delivery 自身を新 endpoint へ変更し、active delivery collision の target を merge |
| moderation policy | 成功 | Actor と domain の Reject、Limit、Silence、RejectMedia、Mute を対応する query と delivery へ適用 |
| raw JSON retention | 成功 | legal hold を除外し、`SKIP LOCKED` batch で original raw JSON を purge |
| remote media cache | 成功 | Safe Federation HTTP、policy、ClamAV、ffmpeg、S3、期限 GC を検証 |
| private authorization | 成功 | 未署名 401、signed recipient 200、private/no-store、featured 非掲載 |
| Safe HTTP failure | 成功 | unresponsive peer の overall timeout、gzip 展開後 size 超過を拒否 |
| local fault injection | 成功 | Toxiproxy 2.12.0 で S3、ClamAV、Vault、PostgreSQL の停止、資格情報拒否、Vault 15 秒遅延と各復旧を検証 |
| fault-state durability | 成功 | 依存障害後に inspect 可能な quarantined media を 3 件保持 |
| Data Protection after DB recovery | 成功 | DB 遮断前の暗号文を復旧後の key ring で復号 |
| rolling orchestration smoke | 成功 | 同一 image digest を old/new tag に使い、138 probe、失敗 0、Activity/Delivery count 不変 |
| container build | 成功 | clean npm lock restore、全frontend source build、locked .NET restore/publishから非root imageを作成。実試験の最終local image ID `sha256:70a9a78aa9d1881876060c3e75fa7bef7db1db91dca97dcbbe43291ce57663f4`、264,743,859 bytes |
| runtime hardening | 成功 | read-only root、tmpfs `/tmp`、UID 1654、ffmpeg と curl を実行 |
| NuGet vulnerability audit | 成功 | direct と transitive に検証日時点の既知脆弱 package なし |
| npm vulnerability audit | 成功 | exact lockfile 214 dependency recordsに検証日時点の既知脆弱packageなし。deprecated querystringと旧broadcast-channel cleanup依存を除去 |
| license gate | 成功 | frontend 214 recordsを含め許可license外なし。metadata欠落2packageはexact versionとarchive内MIT本文を固定監査 |
| local HTTP load | 成功 | 13,183/13,183、878.41 req/s、p95 57.44 ms。以前の同一環境での基準値 |

Local fault injection の `result.json` は次の判定を記録した。

```json
{
  "scenarios": [
    "s3-outage",
    "s3-denied",
    "clamav-outage",
    "vault-outage",
    "vault-denied",
    "vault-latency",
    "postgres-outage",
    "postgres-denied",
    "data-protection-after-db-recovery"
  ],
  "quarantinedMedia": 3,
  "passed": true
}
```

## 未検証またはblocked

- Mastodon、Misskey、Pleromaの未完了項目はPasture artifactの三値表を参照。特にReject、Undo Follow、Reply、Poll、Block、鍵更新、peer signed GETの一部、実Dead Letter再処理がblocked
- GoToSocial、PeerTube の実 instance との双方向 interoperability
- production S3、ClamAV、Vault、PostgreSQL HA service に対する障害注入
- production PostgreSQL failover と PITR
- S3 object version、Vault snapshot、Data Protection key と証明書の統合復元
- inbox 署名検証と outbound delivery を対象にした目標負荷
- queue recovery time と 1 時間以上の soak
- 異なる旧 image と新 image を使った schema 互換 rolling deployment。同一 digest の orchestration smoke のみ通過済み
- SharpFuzz harness の長時間 AFL++ campaign
- Misskey v12 Blazor移植の生成mappingは535 source中`implemented` 329、`in-progress` 0、`blocked` 0、`planned` 0、`excluded` 206、`unclassified` 0。登録・ログイン、runtime utility、supported vertical sliceを証拠付きで検証したが、excluded source、全route、全CSS、Vue lifecycle相当、全motion、実browser authenticator／OIDC E2Eとvisual regressionは未完了。比較用Vue oracleのproduction buildを移植証拠とは扱わない
- Mastodon APIは308経路、Misskey 12.119.2 APIは295 endpointがblocked、1 endpointがexcludedであり、Mastodon／Misskey完全互換を宣言しない
- Mastodon実client、移植済みMisskey frontendのbrowser E2E、固定版Mastodon 4.6.2／Misskey 12.119.2とのAPI differential test
- 上流12.119.2 clientに元からある106件のTODO/FIXME markerの機能別triage。新規adapter/backendに未処理TODOはないが、上流markerを削除しただけで完成扱いにはしない

これらが残るため、この記録は本番導入の承認ではない。

CI run URL、commit SHA、container registry digest、external interoperability artifact、drill artifact URI を release ごとに追記する。
