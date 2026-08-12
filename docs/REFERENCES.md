# 調査根拠と採用判断

2026-08-02 から 2026-08-03 に一次資料を再確認した。

仕様の文言だけでなく、Mastodon が要求する legacy 署名、signed GET、content type、WebFinger を互換境界として分離した。

| 資料 | 実装へ反映した判断 |
| --- | --- |
| [W3C ActivityPub](https://www.w3.org/TR/activitypub/) | S2S inbox/outbox、sharedInbox、202後の非同期処理、C2Sを設定可能な別認可面として実装 |
| [ActivityStreams 2.0 Core](https://www.w3.org/TR/activitystreams-core/) / [Vocabulary](https://www.w3.org/TR/activitystreams-vocabulary/) | compact/expandedに依存せずscalar/array、IRI/Object/Link、未知type/extensionを保持。外部contextは取得しない |
| [RFC 7033 WebFinger](https://www.rfc-editor.org/rfc/rfc7033.html) | `acct:` subjectとJRD self link、canonical host検証 |
| [RFC 9421 HTTP Message Signatures](https://www.rfc-editor.org/rfc/rfc9421.html) | NSign 1.2.4をadapter内に隔離し、required component/parameterと公式RSA fixtureを検証 |
| [RFC 9530 Content-Digest](https://www.rfc-editor.org/rfc/rfc9530.html) | exact raw bytesに対する`sha-256` Structured Field検証 |
| [Mastodon ActivityPub](https://docs.joinmastodon.org/spec/activitypub/) / [Security](https://docs.joinmastodon.org/spec/security/) | legacy Cavage profile、Digest、signed GET、WebFinger、sharedInbox、key refreshを独立互換adapterで実装。Linked Data Signatureは新規送信しない |
| [OWASP SSRF Prevention](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html) | application allow/denyだけに依存せず、全DNS answer検査、validated IPへの接続固定、redirect再検証、network egress制限を組み合わせる |
| [SharpFuzz](https://github.com/Metalnem/sharpfuzz/tree/7dde242459c5459cb988fb8ace4397a9485d720f) | AFL++ の out-of-process harness を parser と sanitizer 用 tool project に隔離 |
| [Caddy reverse_proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy) | active health check、retry、primary と canary の upstream 分離を rolling drill に反映 |
| [Misskey 12.119.2](https://github.com/misskey-dev/misskey/tree/12.119.2) | Vue 3/Vite client と Misskey 固有 `/api/*` 依存を確認し、Mastodon REST とは別の移植対象と判定 |
| [Misskey license](https://github.com/misskey-dev/misskey/blob/12.119.2/LICENSE) | frontend の改変と network 提供を AGPL v3 の別 license 境界として扱う |
| [Misskey ActivityPub extension](https://misskey-hub.net/ns/) | `_misskey_reaction` をLikeのreaction値として解析・送信し、custom Emoji tagを保持 |
| `mei23/dolphin` backend baseline | backendのAPI、連合、モデレーション、メディア、キュー、認証の挙動を比較する正本。frontendのMisskey v12 oracleとは別の基準として扱う。固定checkoutは`.cache/meidolphin`、commitは`3ce200269f814547dc7dfc6b246abadf8a9c00ed` |
| [Akkoma ActivityPub extensions](https://docs.akkoma.dev/stable/development/ap_extensions/) | `EmojiReact` はLikeと別であり、actor/objectごとに異なるemojiを複数保持できるaggregateとして実装 |
| [FEP-c0e0 EmojiReact](https://fep.swf.pub/fep/c0e0/fep-c0e0.html) | `EmojiReact`、`content`、Emoji tag、Undoを隔離した互換dialectとして受理・送信 |
| [fediverse-pasture](https://pasture.funfedi.dev/) / [source](https://codeberg.org/funfedidev/fediverse-pasture) | Mastodon、Misskey、Pleromaのversion固定localverseを日常interop環境にする。HTTP化、固定test user、揮発DBを本番証拠に使わない |

ActivityPubSharpは本番依存に採用しなかった。KristofferStrube.ActivityStreamsも今回は採用せず、System.Text.Jsonを使う小さな通信DTO/変換層を所有する。理由はscalar/array、未知extension、raw bytes、blind recipientの扱いを明示制御し、Domainを外部DTO型から分離するためである。

配送の再試行は`Microsoft.Extensions.Http.Resilience`のprocess-local retryへ委ねず、PostgreSQLのDelivery/Attemptを必ず先に記録する。HTTP resilience handlerはVault HTTPの短時間障害吸収に限定し、federation配送の二重retryを避ける。
