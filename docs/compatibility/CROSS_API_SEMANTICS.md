# Mastodon APIとMisskey APIの共通意味論

## 判定範囲

Mastodon HTTP契約とMisskey HTTP契約は、同じApplication commandまたはqueryへ変換する。

片方のadapterから他方のendpointやDTOを呼ぶ構成は採用しない。

Mastodon adapterとMisskey adapterの相互参照は除去済みであり、投稿の作成・取得は共通の`IClientApiCommandService`と`IClientApiQueryService`を使う。

ただし、Misskey reactionと一部のviewer依存projectionはまだPersistenceへ直接問い合わせている。
Application境界へ移すまでは依存方向全体を完了とは判定しない。

## 外部ID

APIへ公開するIDは、内部UUIDと分離した永続mappingを使う。

mappingの識別子はdialect、entity type、internal UUIDの組であり、同じ内部entityへMastodon IDとMisskey IDを別々に割り当てる。

Mastodon IDはPostgreSQL sequenceから割り当てる10進文字列である。

このIDは単調増加し、数値比較によるカーソルpaginationに利用できる。

Misskey IDは12.119.2の既定方式であるAIDを使う。

AIDは2000-01-01T00:00:00Zからの経過ミリ秒をbase36で表した8文字と、DB sequenceに基づく2文字から構成する。

いずれのIDも再起動、バックアップ復元、rolling deploymentで再計算しない。

## 投稿本文

内部投稿はsource text、source format、sanitize済みHTMLを別々に保持する。

Mastodonから作成した投稿では、受信した`status`をsource textとして保存し、Mastodon契約に従ってHTMLを生成する。

Misskeyから作成した投稿では、MFMをsource textとして保存し、安全なrendererでHTMLへ変換する。

変換には次の損失がある。

| 入力 | Mastodonへの投影 | Misskeyへの投影 | 損失 |
| --- | --- | --- | --- |
| MFMの装飾構文 | allow-list HTML | 元のMFM | Mastodon clientはMFM ASTを復元できない |
| Mastodon HTML | 元のsanitize済みHTML | plain textまたは安全なMFM subset | HTMLのclass、属性、未知elementは復元しない |
| 未対応MFM node | escape済みplain text | 元のsource text | Mastodon側の装飾が失われる |
| content warning | `spoiler_text` | `cw` | なし |
| language | `language` | Note契約に直接fieldがない画面では省略 | Misskey clientの表示で欠落する場合がある |

「Mastodon HTMLから元のMFMへ完全復元できる」とは扱わない。

完全復元を行うにはMFM固有のASTをHTMLだけから推定する必要があり、一意に決まらないためである。

## 可視性と受信者

Mastodonの`private`とMisskeyの`followers`は、内部ではFollowers-onlyへ正規化する。

Mastodonの`direct`とMisskeyの`specified`は、内部ではMentioned-onlyへ正規化する。

ただし、名前の対応だけでは閲覧権限を決めない。

Create時に確定したrecipient snapshotを配送と履歴閲覧の基準とし、Followers-onlyのhome timeline表示など、仕様が現在のfollow関係を要求するqueryだけで現在関係を参照する。

次の経路は同じviewer authorization serviceを通す。

- public timelineとhome timeline
- account statusesとuser notes
- search、notification、conversation
- favourite、bookmark、reaction一覧
- featured、media、Streaming
- response cacheとETag再検証

## Reaction

Mastodon favouriteとMisskey reactionは、共通Reaction aggregateを利用するが同じ値へ潰さない。

| 操作 | 保存値 | ActivityPub副作用 | 他方への投影 |
| --- | --- | --- | --- |
| Mastodon favourite | `Like`、絵文字指定なし | `Like` | Misskeyでは既定reactionとして表示する |
| Misskey Unicode reaction | Unicode scalar列 | `Like`と`_misskey_reaction`、またはpeer能力に応じた`EmojiReact` | Mastodonでは`favourited`へ変換しない |
| Misskey custom reaction | shortcode、origin、画像IRI | 同上 | Mastodon公式Statusへ独自fieldを追加しない |
| Misskey reaction変更 | 旧reactionを終了して新reactionを作成 | 旧Activityの`Undo`後に新Activity | viewer状態は新reactionだけを示す |

Mastodon公式APIがcustom reactionを表現できない場合、Mastodon responseへ非標準fieldを捏造しない。

## Media

Mastodon media attachmentとMisskey Drive fileは共通Media aggregateを参照する。

Mastodon v2 uploadの`202 Accepted`は非同期処理状態を表し、完了前のmediaを投稿へ添付できることを意味しない。

Misskey Driveのfolder、quota、hash lookup、duplicate検出はActivityPub配送を発生させないローカル状態として保存する。

どちらのAPIから投稿へ添付しても、同じmedia rowとobject attachmentを参照し、複製uploadや二重配送を作らない。

## Notificationと既読状態

notificationはAPI responseを読む時点で合成せず、原因eventと同じtransaction内で永続化する。

Mastodon notification typeとMisskey notification typeは同じeventから別々に投影する。

既読、dismiss、clear、markerはユーザー単位の状態であり、一方のAPIで更新した結果を他方のAPIとStreamingへ反映する。

Mute、Block、Silence、notification mute、visibilityによる抑止は、REST projectionとStreaming projectionの両方へ適用する。

現在はInbox副作用とローカルFollow、Like、reaction、Announce、Mentionから`UserNotification`とdurable stream eventを原因操作と同じtransactionで保存する。

Mastodonのfavourite通知はMisskeyの既定`reaction`へ投影するが、Misskey custom reactionはMastodonの`favourite`通知へ偽装せずMastodon projectionから除外する。

既読、個別dismiss、全件clear、Misskey mark-all-as-readは同じ永続行を更新する。

User Muteと利用者単位Block、管理Actor Block/MuteはRESTとStreamingの取得時にも再評価する。

Domain Silenceのnotification抑止と、全notification typeの固定版differential fixtureは未完了である。

## Follow、Mute、Block

Follow、Mute、Blockは両adapterから共通Application command/queryへ入る。

FollowとUndo Follow、BlockとUndo Blockは、元Activity IRIを保持した専用aggregateとtransactional deliveryを生成する。

Blockは双方向のFollow関係を解除し、当事者間のhome timeline、Object projection、notification、通常配送を抑止する。

Block自身とUndo Blockは相手へ届かなければ状態を同期できないため、通常配送のblock filterとは分離した明示経路を使う。

Misskeyの`users/relation`とMastodonの`accounts/relationships`は同じFollow、Mute、Block行を投影する。

## Idempotency

Mastodonと移植済みMisskey frontendの`Idempotency-Key`は、subject、client、endpoint、key、request hashの組で保存する。

同じrequest hashの再送は保存済みresponseを返し、Activity、Delivery、Notification、Mediaを再作成しない。

同じkeyへ異なるrequest hashを送った場合は競合として拒否する。

response保存だけが失敗した場合は、同じtransactionで保存した操作結果からresponseを再構築する。

## 現在の検証状態

固定版inventory、外部IDのDomain試験、PostgreSQL同時割当試験に加え、両方向の投稿cross-projectionとActivity／Deliveryの一重生成をPostgreSQL API統合試験で確認した。

notificationの両API投影と共有既読状態、Follow／Undo、Mute／Unmute、Block／Undo Block、custom reactionをMastodon notificationへ偽装しないこともPostgreSQL API統合試験で確認した。

実client試験、固定版実serverとの差分試験、media・pollを含む全cross-projection試験は未実施である。

したがって、現時点では「Mastodon互換」「Misskey互換」「両対応」のいずれも宣言しない。
