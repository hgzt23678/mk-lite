# Mastodon REST API 互換表

Mastodon REST API は `ActivityPub.MastodonApi` に隔離する。

このモジュールは ActivityPub の内部モデルを直接公開せず、Mastodon の JSON 契約へ投影する。

## 対応 endpoint

| Method | Path | 認証 | 自動検証 | 制約 |
| --- | --- | --- | --- | --- |
| GET | `/api/v1/instance` | 不要 | API test | DB集計。完全な4.6.2 entityは未検証 |
| GET | `/api/v2/instance` | 不要 | API test | DB集計。contact、rules等の永続設定は未実装 |
| GET | `/api/v1/accounts/lookup` | 不要 | API test | DB に既知の actor のみ |
| GET | `/api/v1/accounts/{id}` | 不要 | API test | 永続的なMastodon数値ID |
| GET | `/api/v1/accounts/{id}/statuses` | 条件付き | API test | 永続数値ID cursor |
| GET | `/api/v1/statuses/{id}` | 可視性による | API test | private status は viewer 権限を検査 |
| GET | `/api/v1/timelines/public` | 不要 | API test | Silence、RejectMedia、admin Mute を反映 |
| GET | `/api/v1/accounts/verify_credentials` | `mastodon.read` | API test | external OIDC claim を local actor に対応付ける |
| GET | `/api/v1/timelines/home` | `mastodon.read` | API test | user Mute と admin Mute を除外 |
| POST | `/api/v1/statuses` | `mastodon.write` | API test | transactional outbox、Idempotency-Key |
| DELETE | `/api/v1/statuses/{id}` | `mastodon.write` | API test | local owner のみ |
| POST | `/api/v1/statuses/{id}/favourite` | `mastodon.write` | API test | Like aggregate を更新 |
| POST | `/api/v1/statuses/{id}/unfavourite` | `mastodon.write` | API test | Undo Like |
| POST | `/api/v1/statuses/{id}/reblog` | `mastodon.write` | API test | Announce aggregate を更新 |
| POST | `/api/v1/statuses/{id}/unreblog` | `mastodon.write` | API test | Undo Announce |
| POST | `/api/v1/accounts/{id}/mute` | `mastodon.write` | API test | expiry と notification flag を保存 |
| POST | `/api/v1/accounts/{id}/unmute` | `mastodon.write` | API test | revoke 履歴を保存 |
| GET | `/api/v1/accounts/relationships` | `read:follows` | cross-API test | Follow、Mute、Blockの共有状態 |
| POST | `/api/v1/accounts/{id}/follow`、`unfollow` | `write:follows` | cross-API test | Followと正確なUndoを配送 |
| POST | `/api/v1/accounts/{id}/block`、`unblock` | `write:blocks` | Domain、Inbox、cross-API test | 専用UserBlock、Follow解除、通常配送抑止、正確なUndo |
| GET／POST | `/api/v1/notifications*` | `read:notifications`／`write:notifications` | cross-API、Streaming test | durable通知、unread、dismiss、clear。`since_id`／`min_id`は未完了 |
| GET | `/api/v1/streaming*` | stream依存 | PostgreSQL Streaming test | user、public、local、SSE resume、notification。hashtag／list／directは未完了 |
| POST | `/api/v1/apps` | 不要 | OAuth PostgreSQL integration test | client secretはOpenIddictのhashだけを保存 |
| GET | `/.well-known/oauth-authorization-server` | 不要 | OAuth contract test | code、PKCE S256、refresh、revocation metadata |
| GET／POST | `/oauth/authorize` | 外部OIDC session | Cookie・CSRFを含むOAuth integration test | 明示consentとscope narrowing |
| POST | `/oauth/token` | client認証／authorization code | OAuth integration test | code + PKCE、client credentials、rolling refresh token |
| POST | `/oauth/revoke` | client認証 | OAuth integration test | reference tokenの失効を後続requestで検証 |
| GET | `/api/v1/apps/verify_credentials` | `mastodon.read` | OAuth integration test | client secretをresponseへ含めない |

## 互換を宣言しない理由

次の API 群は未実装である。

- media upload、update、attachment 処理
- marker
- search、trends、suggestions
- report の REST 操作、follow request一覧と判断
- list、bookmark、pin、filter
- poll vote
- hashtag、list、direct Streaming
- scheduled status と draft
- preferences、endorsement、featured tag

既存 endpoint にも差異がある。

内部UUIDとは別にPostgreSQL sequence由来の永続的な10進文字列IDを返すが、全endpointの`max_id`、`since_id`、`min_id`と`Link` headerはまだ揃っていない。

`/api/v1/custom_emojis`は永続custom emoji aggregateが未実装のため、空配列を返すrouteを撤去した。
実装前のため404となり、機能が存在するように装わない。

外部 client の fixture と実 client を用いた互換試験が完了するまで、`Mastodon compatible` を instance metadata や README に表示しない。
