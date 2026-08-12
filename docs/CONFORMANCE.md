# 仕様適合と相互運用範囲

この表は、コードが存在すること、自動テストが通ること、外部実装と接続したことを区別する。

移植基準は分離して判定する。frontendのデザイン、DOM、CSS、motion、responsive挙動はMisskey v12.119.2を基準にし、backendのAPI、永続副作用、連合、認可、モデレーション、メディア、キュー挙動は`mei23/dolphin`を基準にする。`.cache/meidolphin`のcommit `3ce200269f814547dc7dfc6b246abadf8a9c00ed`を固定参照する。

「保存」は、署名、所有関係、宛先を検証して raw JSON と正規化値を永続化することを指す。

## W3C ActivityPub

| 項目 | 実装 | 自動検証 | 判定 |
| --- | --- | --- | --- |
| S2S Inbox/Outbox | durable Inbox、transactional outbox、非同期副作用、受信者 snapshot | PostgreSQL、API、Federation tests | 実装済み |
| C2S Outbox | OIDC 保護、設定による無効化、Idempotency-Key | API、PostgreSQL tests | 実装済み。外部 C2S client 未検証 |
| Actor、Object、Activity dereference | canonical IRI、content negotiation、条件付き GET | API tests | 実装済み |
| Collection | cursor pagination、ETag、Last-Modified、Cache-Control | API tests | 実装済み。汎用 collection の種類は限定的 |
| Audience と可視性 | Public、Unlisted、Followers-only、Mentioned-only | Domain、API tests | 実装済み |
| blind recipient 除去 | outbound bytes の生成前に `bto` と `bcc` を除去 | golden、property tests | 実装済み |
| private dereference | signed GET と recipient authorization | API tests | 実装済み |
| unknown Activity | raw 保存後、副作用なしで完了 | Inbox tests | 実装済み |

## Activity 処理

| Activity | 受信 | 送信 | Activity 固有の副作用 |
| --- | --- | --- | --- |
| Create | 対応 | 対応 | Object 作成、所有者検証 |
| Update | 対応 | 対応 | 同一 origin Object の revision 追加 |
| Delete | 対応 | 対応 | 所有者検証後の Tombstone 化 |
| Follow、Accept、Reject | 対応 | 対応 | FollowRelation の状態遷移 |
| Add、Remove | 対応 | 対応 | CollectionMembership aggregate の追加と解除 |
| Like | 対応 | 対応 | LikeRelation aggregate。Misskey `_misskey_reaction`、content、Emoji tagを正規化し、actor/objectごとに置換 |
| EmojiReact / EmojiReaction | 対応 | 対応 | LitePub系の専用EmojiReactionRelation。actor/object/emojiごとに複数の異なるreactionを保持 |
| Announce | 対応 | 対応 | AnnounceRelation aggregate の適用 |
| Move | 対応 | 対応 | alsoKnownAs と双方向 alias を検証した ActorMove aggregate |
| Undo | 対応 | 対応 | Follow、Like、EmojiReact、Announce のaggregate別・冪等な解除 |
| Flag | 対応 | 対応 | Report 作成と隔離 |
| Block | 対応 | 対応 | 利用者単位UserBlock aggregate、正確なUndo、双方向Follow解除、通知・表示・通常配送抑止 |

`Person`、`Service`、`Application`、`Group`、`Note`、`Article`、`Page`、`Question`、`Tombstone`、`Document`、`Image`、`Audio`、`Video`、`Mention`、`Hashtag`、`Emoji`、`PropertyValue`を解析できる。

文字列または配列の `type`、IRI、埋め込み Object、Link を受理する。

`sensitive`、`blurhash`、`featured`、`manuallyApprovesFollowers`、`discoverable`、`indexable`、`alsoKnownAs`、quote 系を含む未知の拡張値は raw JSON に保持する。

JSON-LD context はネットワーク取得しない。

## Discovery と HTTP 互換

実装済み公開 endpoint は次のとおりである。

- `/.well-known/webfinger`、`/.well-known/host-meta`、`/.well-known/nodeinfo`、`/nodeinfo/2.0`
- `/users/{username}`、actor inbox/outbox、followers/following/liked/featured
- shared `/inbox`
- `/objects/{id}`、`/activities/{id}`、`/collections/{id}`

WebFinger subject、`application/activity+json`、ActivityStreams JSON-LD profile、sharedInbox、secure mode 向け signed GET を扱う。

Mastodon REST API は独立モジュールに実装したが、対応 endpoint は限定される。

完全互換は宣言しない。

対応範囲は [Mastodon API 互換表](MASTODON_API.md)に記載する。

## HTTP 署名

| Profile | Receive | Send | Evidence |
| --- | --- | --- | --- |
| Cavage、legacy Mastodon | RSA-SHA256、`(request-target)`、Host、Date、Digest | 既定互換方式 | deterministic fixture、round trip tests |
| RFC 9421 | `created`、`expires`、`keyid`、`@method`、`@target-uri`、`content-digest`、RSA v1.5 SHA-256 | peer capability と再評価で選択 | RFC Appendix RSA vector、NSign round trip |

POST では Digest または Content-Digest を必須とする。

署名失敗時の remote key 再取得は一度だけで、owner、actor、origin の結び付けと時間窓を検証する。

Linked Data Signatures は送受信とも未実装であり、新規送信にも使わない。

## 外部実装との相互運用

| 実装 | バージョン | 結果 | 成功項目 | 既知の差異 |
| --- | --- | --- | --- | --- |
| Mastodon | 4.6.2 | 一部成功 | 双方向Discovery/Follow/Accept、公開Create、Followers-only、Mentioned-only、Like/Undo、Announce、peer Delete/Tombstone、signed GET、media、remote media proxy/cache、鍵付きprivate GET | Reject、Undo Follow、Reply、Poll、Block等はblocked。2 CPU上のRails development processが2回無応答になり、DB/Redis/volumeを保持してapp containerだけを再作成 |
| Misskey | 2026.6.0 | 一部成功、一部失敗 | 双方向Discovery/Follow/Accept、.NETからの公開Create/Announce/media、Mentioned-only、`Like + _misskey_reaction`、reaction置換/Undo | Followers-onlyはpersonal inboxへ202後もNote未保存。Misskey v12 serverの結果ではない |
| Pleroma | 2.10.0 | 一部成功、一部失敗 | 双方向Discovery/Follow/Accept、公開Create、Followers-only、Announce/media、peer EmojiReact/Undo | Mentioned-onlyはMention tag付きpersonal inboxへ200後もObject未保存。固定imageのUI assetがoffline環境になくAPI/DBでprojectionを検証 |
| Akkoma/LitePub | fixture | fixture成功、実instance未検証 | `EmojiReact`、alias `EmojiReaction`、複数reaction、custom emoji、Undo | capability判定はActor contextの既知語彙に基づく。実instance試験なし |
| GoToSocial | 未実施 | 未検証 | なし | 実 instance 試験なし |
| PeerTube | 未実施 | 未検証 | なし | media と Activity の実 instance 試験なし |

Actor 検索、Follow、Accept、Reject、Create、Reply、Update、Delete、Like、Announce、Undo、Block、Mention、Hashtag、Media、Poll、各非公開範囲、鍵更新、signed GET、sharedInbox をバージョン固定の instance 間で双方向に通すまで、相互運用完了とは判定しない。

2026-08-03の実測で未完了または失敗した項目を含む全三値表は`artifacts/interop/pasture/20260803T061125Z/interop-matrix.md`に保存した。

HTTP受理だけを成功とはしていない。

送信Activity、Delivery/Attempt、受信DB、受信APIのうち取得可能な四点を照合し、受信永続化がなかったMisskey Followers-onlyとPleroma Mentioned-onlyは失敗としている。

Mastodon `v4.6.2`、Misskey `2026.6.0`、Pleroma `v2.10.0`の実測には[fediverse-pasture localverse](LOCAL_FEDERATION.md)を使う。環境を構成しただけでは成功欄へ移さない。

## 周辺機能の境界

| 領域 | 実装済み | 未実装または未検証 |
| --- | --- | --- |
| Moderation | 管理Actor block、利用者単位Block、domain Allow、Limit、Reject、Silence、RejectMedia、PauseOutbound、user Mute、通知抑止、Flag、audit、spam quarantine、Dead Letter 再処理 | Silenceのnotification全経路と固定版実clientでの表示効果は未検証 |
| Media | S3、MIME、size、dimension、duration、ClamAV、ffmpeg、thumbnail、quarantine、private authorization、GC、remote proxy と期限付き cache | blurhash 生成、非同期 remote fetch job、production S3 実環境試験 |
| Retention | Activity と Object の original raw JSON を batch purge、legal hold、hash-chain audit | production retention 値と法務手順の承認 |
| Delivery endpoint 更新 | failing Delivery 自身の endpoint 置換、target merge、split、監査、active 一意制約 | 実 peer が Inbox を変更する相互運用試験 |
| Identity | ASP.NET Core Identityのpassword、lockout、TOTP、protected legacy WebAuthn challenge、Misskey `POST /api/signin`（JSON／multipart）、専用hash-backed `mk_` token、HttpOnly session cookie、MiAuth `session:null`、外部OIDCの明示的分離 | 実browser authenticator enrollment、外部provider live統合、refresh rotationのproduction運用は未検証 |
| Mastodon REST | inventory 331経路中23経路を自動試験付き実装。OAuth、instance、account、status、timeline、favourite、reblog、Follow、Mute、Block、notification、Streamingの一部 | 残り308経路、実client、4.6.2 differential test |
| Misskey 12.119.2 API | inventory 321 endpoint中23 endpointを自動試験付き実装。投稿、reaction、MiAuth、Follow、Mute、Block、notification、Streamingの一部、および`POST /api/signin` | 残り298 endpoint、実12.119.2 client、固定server differential test |
| Misskey v12 frontend | static SSR + Interactive Server基盤、上流CSS生成、Visitor／Universal、supported note/timeline/profile/settings/admin vertical slice、`MkSignin`の`/api/signin`送信、設定API/Apps、client bootstrap/directive/page-block/Nirax utilityを実データで検証。生成mapping 535 source中`implemented` 329、`in-progress` 0、`blocked` 0、`planned` 0、`excluded` 206、`unclassified` 0 | excluded 206 source（専用backend feature 34 + Dolphin contract gaps 172）はstubを作らずcapability unavailable。Misskey v12.119.2実server differential、実browser authenticator success、全画面visual regressionは未検証。Vue oracle buildは移植証拠に含めない |
