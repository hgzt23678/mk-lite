# Misskey v12 frontend 移植記録

対象は Misskey `12.119.2`、commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2` である。

この文書のfrontend oracleはデザイン、DOM、CSS、画面挙動だけを扱う。バックエンドの機能・API・連合・モデレーション・メディア・キュー挙動の基準は`mei23/dolphin`であり、Misskey v12 frontendの実装から逆算しない。

ローカルに存在する`.cache/meidolphin`は、ユーザー確認済みの`https://github.com/mei23/dolphin`、`mei-dolphin` branch、commit `3ce200269f814547dc7dfc6b246abadf8a9c00ed`であり、backend differential evidenceの固定参照とする。

上流`packages/client`の全ソース、locale、テーマ、アイコンと静的資産を`frontend/misskey-v12`へ比較oracleとして固定する。
Vue版は移行中のvisual/behavior oracleであり、production artifactではない。
production frontendは`frontend/ActivityPub.Misskey.Blazor`の.NET 10 static SSR + Interactive Server Razor Componentsである。
`UPSTREAM_FILES.sha256` は上流573 client filesを固定し、CIは全573 filesの存在、未変更537 filesのbyte一致、36 filesの明示的な変更allow-listを検査する。
同じgateが上流locale 38 filesとserver static asset 25 filesのbyte一致も検査する。
上流 `assets/*` はURL契約に合わせて `public/client-assets/*` へ一対一で配置する。

repositoryにVue sourceが存在すること、またはVite buildが成功することを「Blazorへ移植済み」と数えない。
現在のinventoryは535 source、400 Vue SFC、115 routeを分類済みである。2026-08-12 UTCの生成mappingは`implemented` 329、`in-progress` 0、`blocked` 0、`planned` 0、`excluded` 206、`unclassified` 0である。excludedは専用backend feature 34件とDolphin contract gaps 172件で、完全移植の宣言ではない。
APIが未実装の画面は契約に沿った明示的エラーとし、仮データや常時成功するstubへフォールバックしない。

完全移植の判定は[UI/CSS/挙動の完全同等性要件](frontend-blazor/PARITY_REQUIREMENTS.md)に従う。

## Oracleとして固定した上流範囲

- classic、deck、mobile を含む UI shell と router
- timeline、note detail、profile、search、notification、messaging
- emoji picker、MFM、custom emoji、poll、media/drive
- list、antenna、channel、clip、favourite、gallery、page
- widget、theme editor、plugin、AiScript、registry、settings
- moderation と admin の全画面ソース
- locale generator、theme、sound、image、font と public asset

上流依存のうち、既知脆弱性を持つ古い build tool は固定せず、Vue 3.5、Vite 8、Rollup 4、TypeScript 5.9、Vitest 4 へ更新した。
Vue reactivity transform は隔離した compiler plugin で維持する。
旧ソース全体を Vite が実際に変換するproduction buildを品質ゲートとし、移植 adapter 自身は `tsconfig.port.json` の strict typecheck を別ゲートにする。
上流 v12全体を現在の `vue-tsc` で再型検査したという主張はしない。

## この backend 向けの変更

- 初期化と runtime config を `activitypub-runtime.ts` と `bootstrap.ts` に隔離
- Misskey v12 の `POST /api/signin` を同一originのローカル認証へ接続し、成功時に専用 `i` token と HttpOnly browser session cookie を発行
- Blazor browser sessionはCookie、Misskey API/Streamingは専用 `mk_` Bearer tokenとし、OIDC access tokenをMisskey tokenへ流用しない
- token、authorization code、refresh token を `localStorage` と Cookie に保存しない
- mutation に `Idempotency-Key` を付与できる中央 `os.api` adapter
- 同一 origin の ASP.NET Core `/api/*` だけを API 対象にする
- service worker の旧 server 前提処理を無効化
- About画面に上流と、配信buildの完全な対応sourceを別linkとして表示
- Host header から federation IRI を組み立てない
- custom emoji と remote media は server の同一 origin proxy/cache URLだけを表示
- Vite 8 で全画面を build するための JSON import、reactivity transform、Sass 構文更新

## 実装済み Misskey API

| v12 endpoint | 認証 | backend の意味 |
| --- | --- | --- |
| `meta`、`stats` | 不要 | node metadata とDB集計。metaの永続ads／emoji／themeは未実装 |
| `signin`、`i` | `signin`は匿名、`i`は専用token必須 | `signin`はIdentityのpassword／TOTP／lockoutを検証し、`i`は対応するlocal Actorと実際のnotification／follow未読状態を返す |
| `users/show`、`users/notes` | 条件付き | Actor と可視な Object の projection |
| `notes/show` | 条件付き | 非公開 recipient authorization を維持した note |
| `notes/global-timeline`、`notes/local-timeline` | 不要 | Silence、domain policy、可視性を適用 |
| `notes/timeline`、`notes/hybrid-timeline` | 必須 | follow と Mute を適用した home projection |
| `notes/create` | 必須 | Object、Create/Announce、Delivery を同じDB transactionで確定 |
| `notes/delete` | 必須 | 所有権検証後に Delete/Undo Announce を生成 |
| `notes/reactions` | 条件付き | Like と EmojiReact aggregate を時系列に統合 |
| `notes/reactions/create`、`notes/reactions/delete` | 必須 | peer dialectを選択し、Undoを含むdurable federation deliveryを生成 |
| `miauth/gen-token`、`miauth/{session}/check` | 認証済みIdentity／一回限りsession | 専用Misskey tokenを発行し、`session:null`は内部UUIDへ隔離。hashだけをDBへ保存。一回限りのtoken受取はData Protectionで保護 |
| `i/apps`、`i/revoke-token` | 専用Misskey token | 発行済みtokenの永続ID付き一覧と失効。所有Actor以外のtoken操作を拒否 |
| `i/notifications`、`notifications/read`、`notifications/mark-all-as-read` | 必須 | 共通Notification aggregate、共有既読状態、Mute／Block抑止 |
| `following/create`、`following/delete`、`users/relation` | 必須 | 共通Follow aggregate、正確なUndo、Mastodon relationshipとの共有投影 |
| `mute/create`、`mute/delete` | 必須 | 期限、notification抑止、解除履歴を持つ共通UserMute |
| `blocking/create`、`blocking/delete` | 必須 | 専用UserBlock、Follow解除、通常配送抑止、正確なUndo Block |
| `/streaming` main、global／local／home／hybrid timeline、Note Capture | 必須またはpublic | PostgreSQL durable cursor、再接続、reaction／delete／notification |

`notes/create` は通常投稿、返信、renote、quote、Public/Home/Followers/Directを扱う。
Channel、Poll、`localOnly` は現在明示的に拒否し、値を無視して通常投稿に変換しない。
上流ソースには管理、drive、messaging、antenna、channel等のAPI呼び出しも残っているが、対応する server endpoint は未実装である。
したがって、source inventoryは完了しているが、画面のBlazor移植もMisskey v12 server API完全互換も宣言しない。

上流12.119.2のclient sourceには、refactoring memoや未完成のadmin actionを含む106件の`TODO`/`FIXME` markerが元から存在する。
source parityのためこれらを削除して完成を装わない。
新規ActivityPub adapterとemoji federation実装には未処理TODO、stub、常時成功fallbackを置いていないが、上流由来markerの機能別triageと未実装server APIが残るため、frontendを含むシステム全体の本番完成はまだ宣言しない。

## 絵文字リアクションの連合

2つの互換dialectを別aggregateで扱う。

| dialect | outbound activity | 内部意味 |
| --- | --- | --- |
| Misskey | `Like` + `_misskey_reaction`、`content`、Emoji tag | actor/objectごとに現在値1つ。変更時は旧LikeをUndoして置換 |
| LitePub/Akkoma系 | `EmojiReact`、aliasとして受信`EmojiReaction` | actor/object/emojiごとに独立。同じObjectへ複数の異なるemojiを保持 |

custom emojiは `:shortcode@origin-host:` に正規化し、Emoji tag の `id`、`icon.url`、media typeを保持する。
送信時はpeer Actorの既知contextからdialectを選び、未知peerにはMisskey互換Likeへ安全に劣化させる。
Inboxでは署名、所有関係、宛先、activity ID重複を検証してから保存し、Workerが冪等に副作用を反映する。
同一activity ID・同一bytesは重複として処理せず、同一ID・異なるbytesは隔離する。
LikeとEmojiReactのUndoは対応するaggregateだけを解除する。

## Security boundary

`POST /api/signin` はJSONとMisskey v12 browserのmultipart formを受理し、`Cache-Control: no-store`、IP rate limit、ASP.NET Core Identityのpassword、lockout、TOTP、passkey経路を利用する。Misskey v12のlegacy WebAuthn payload（`credentialId`、`challengeId`、`clientDataJSON`、`authenticatorData`、`signature`）も同じrouteで受理し、challenge cookieとASP.NET Coreの保護済みpasskey stateを照合して単回検証する。Blazorのbrowser APIは同じIdentity境界を持つ専用`/auth/passkey/*` endpointも使用するが、Misskey wire互換routeを迂回して別の認証実装を持たない。
ブラウザー用sessionは `__Host-activitypub-oauth-session` HttpOnly/Secure cookieで保持し、Misskeyクライアント用 `i` は専用hash-backed tokenとしてのみ使用する。
OIDCは外部ログインを明示的に構成した場合の別経路であり、OIDC access tokenを`i`へ変換したりlocalStorageへ保存したりしない。

未改変Misskey client向けのMiAuth tokenはOIDC access tokenと別に発行する。
DBへ保存するのはSHA-256 hash、scope、所有Actor、失効状態であり、平文tokenは保存しない。
`/api/miauth/{session}/check`へ返す一回限りの値だけを永続Data Protection key ringで暗号化し、最初の取得と同じtransactionで消去する。
JSON bodyの`i`は認証handlerがbuffering後にbody位置を戻して読み取り、query stringの`i`はStreaming endpoint以外で受理しない。

受信HTMLのallow-list sanitizeはbackendが権威を持つ。
remote attachment、avatar、custom emojiをbrowserからremote originへ直接取得させず、利用者IPとauthorization headerを漏らさない。
frontend runtime configはOIDC authority、callback、source URLを検証し、productionでplaceholder source URLを拒否する。

## License とsource提供

リポジトリとfrontendはGNU AGPL v3 onlyである。
ルートと各Misskey由来frontendディレクトリの`LICENSE`と`NOTICE.md`を配布sourceに含める。
`Frontend:SourceUrl` は配信中の変更を再現できる完全な対応sourceのexact revisionを指す。
release ownerはnetwork useと配布条件を法務確認する。

依存licenseはexact lockfileからfail-closedで検査する。
npm metadataにlicense fieldがないpackageは、配布archive内のlicenseをexact versionごとに監査した場合だけ許可する。
browserへ配布する第三者artifactのlicense本文は`frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor`へ同梱し、生成scriptの`--check`で欠落を拒否する。

## 検証方法

```bash
npm --prefix frontend/misskey-v12 ci --ignore-scripts
npm --prefix frontend/misskey-v12 run typecheck
npm --prefix frontend/misskey-v12 test
npm --prefix frontend/misskey-v12 run verify:upstream
npm --prefix frontend/misskey-v12 run build
npm --prefix frontend/misskey-v12 run audit
node eng/check-frontend-licenses.mjs
docker build --tag activitypub-server:frontend .
```

2026-08-13 UTCの再現では、固定upstream 573/573 files、inventory 535 source、Blazor component test 536件、solution全体の.NET試験913件、認証・登録・passkeyのChromium smoke 11件を通した。
mappingは`implemented` 329、`excluded` 206、`in-progress`、`planned`、`blocked`、`unclassified`はいずれも0である。`excluded`はDolphin側に必要なbackend契約が存在しない機能であり、完全互換の宣言には含めない。
登録、ログイン、全route visual differential、全Vue挙動、全animation、未実装Misskey APIの契約試験は未完了である。

参照元は [Misskey 12.119.2](https://github.com/misskey-dev/misskey/tree/12.119.2/packages/client) と [同tagのlicense](https://github.com/misskey-dev/misskey/blob/12.119.2/LICENSE) である。
