# データモデル

公開IRIは連合境界の不変な自然キー、内部IDはUUIDである。公開IRIには一意制約を置き、DB主キーやrequest Hostから生成しない。ドメイン変更は設定変更ではなくMove/移行手順を必要とする。

| Aggregate | 目的 | 主要な整合性制約 |
| --- | --- | --- |
| LocalActor | ローカルActor | canonical IRI、正規化usernameを一意化 |
| RemoteActor | リモートActor cache | canonical IRI一意、origin、refresh metadata |
| ActorKey | local key lifecycle | key IRI一意、owner、active/retired/revoked、有効期間、外部handle |
| FederatedObject | Objectの現在値 | object IRI一意、owner origin、visibility、Tombstone |
| ObjectRevision | 監査用immutable履歴 | object/version一意、raw JSON |
| Activity | 正規化Activityと正確な送信bytes/raw JSON | activity IRI一意、payload hash |
| ActivityRecipient | 正規化audience | activity/recipient/audience field一意 |
| FollowRelation | Follow状態機械 | follower/followed pair一意 |
| CollectionMembership | Add と Remove の状態 | actor、object、target collection 一意 |
| LikeRelation | Like とUndo、Misskey置換型reaction | actor/object pair一意、reactionとcustom Emoji metadata |
| EmojiReactionRelation | LitePub `EmojiReact` / `EmojiReaction` とUndo | activity IRI一意、active actor/object/reaction partial unique。異なるemojiは共存 |
| AnnounceRelation | Announce と Undo の状態 | actor/object pair 一意 |
| ActorMove | 検証済み Actor migration | old/new actor pair、alsoKnownAs 検証結果 |
| InboxItem | durable inbound work | activity IRI一意、payload hash、lease、terminal state |
| InboxItemRecipient | 受付時に解決したlocal recipient snapshot | inbox item/actor一意、followers展開後も保持 |
| Delivery | inbox ごとの durable outbound work | active activity/endpoint の partial unique、lease、retry、terminal state |
| DeliveryTarget | Delivery に含めた受信 Actor snapshot | delivery/actor 一意 |
| DeliveryEndpointChange | endpoint 再発見の監査 | delivery、old/new endpoint、時刻、理由 |
| DeliveryAttempt | immutable配送監査 | delivery/attempt number一意、status分類 |
| DeadLetter | 終端work item | source work item一意、audited replay |
| RemoteEndpoint | inbox/sharedInbox cache | actor/type/URI一意、stale metadata |
| RemoteKeyCache | remote public key cache | key IRI一意、owner binding、refresh cooldown |
| SignatureReplay | 署名リプレイ抑止 | signature fingerprint/nonce/time window一意 |
| DomainExecutionState | domain制御 | domain一意、active lease数、circuit、pause |
| ClientIdempotencyRecord | C2S重複抑止 | actor/key一意、request hash、response reference |
| MediaResource | S3 object metadata | storage key/content hash一意、quarantine、purge lifecycle |
| MediaAttachment | ObjectとMediaの参照 | object/media一意。GCの参照判定 |
| RemoteMediaCacheEntry | remote media proxy cache | source URI 一意、MediaResource 参照、expiry |
| RemoteActorMediaCacheEntry | remote Actor avatar/banner cache | Actor/source token一意、MediaResource参照、ETag/Last-Modified、fetch lease、negative-cache expiry |
| DomainPolicy | Allow/Limit/Reject等 | 正規化domain/kind一意、理由、期限 |
| ActorPolicy | Actor固有policy | actor/kind一意、理由、期限 |
| ModerationAction | 操作履歴 | immutable ID、operator、reason、expiry、reversal |
| Report | Flagの管理対象 | report IRI一意（存在する場合） |
| AuditEvent | 管理監査 | 前event hashを含むhash chain |
| LegalHold | raw JSON の削除停止 | subject type と subject IRI、理由、期限、解除履歴 |
| UserMute | local user ごとの表示抑止 | owner/target の active 一意、notification flag、expiry、revoke |
| OperationalControl | 緊急制御 | global outbound pause等の永続状態 |

## トランザクション境界

- ローカル状態変更、Activity bytes、ActivityRecipient、Deliveryを同じtransactionで確定する。
- Inbox受付は検証済みActivity、InboxItem、followers展開済みlocal recipient snapshotを同じtransactionで確定し、202を返した後にWorkerが副作用を処理する。
- side effectとInboxItem完了は同じtransactionで確定する。
- claimは`FOR UPDATE SKIP LOCKED`を用い、lease ownerとexpiryをDBで記録する。
- 同一activity IDで別hashを受信した場合は通常の重複とせず隔離・監査する。

raw JSON、blind recipients、非公開Objectは通常のログ、metrics、公開collection、公開cacheへ出さない。保持日数と削除batchは運用設定で定め、法的保全が必要な場合は削除処理と分離する。
