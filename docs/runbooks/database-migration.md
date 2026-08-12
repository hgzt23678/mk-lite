# Database migration

現在のmigrationはinitial schemaの後、signature replay、domain execution control、operational control、media authorization、Data Protection key ring、C2S idempotency、media GC、Inbox recipient snapshot、semantic aggregate／retention／mute、active Delivery endpoint index、Misskey Like reaction metadata、LitePub EmojiReact aggregateを順に追加する。

Local Identity用migration historyは`identity.__ef_migrations_history`へ分離する。`AddPasswordResetRequests`と`AddEmailConfirmationRequests`は空の新規table、主キー、期限検索index、token hash一意index、`identity.users`へのcascade FKだけを追加する。`AddRegistrationInvitations`も空の新規tableへcode hash、発行・期限・reservation・消費監査列、3 index、hash長・期限・reservation・消費状態の4 check constraintだけを追加する。いずれも既存user tableのrewrite、default、backfill、column contractはない。適用後も旧binaryは新規tableを参照しないため、新旧APIの共存が可能である。

## Apply

1. `dotnet ef migrations script`で順序付きSQLを生成し、lock、rewrite、推定時間、rollbackをreviewする。
2. backup/PITR、DB free space、long transaction、replication lagを確認する。
3. CIの`migrations.sql`はreview用の順序付きscriptであり、idempotent再実行用ではない。`CREATE INDEX CONCURRENTLY`を条件付き`DO` blockへ包むとPostgreSQLで実行不能になるため、本番適用はmigration historyを確認する専用`migrate` commandを一度だけ実行する。
4. 一回限りのmigration jobとして`activitypub-server migrate`を実行する。Web起動時に適用しない。
5. `/health/startup`と`/health/ready`、schema compatibility row、migration historyを確認する。

## Expand / Migrate / Contract

nullable column/new tableをexpandで追加し、defaultとbackfillを分離する。bounded batchでbackfillし、旧新両表現を読めるbinaryへ切り替える。indexは必要に応じて`CREATE INDEX CONCURRENTLY`を独立したnon-transactional migrationにする。古いcolumn/constraintの削除は少なくとも次releaseのcontractへ送る。

rollbackはexpand段階だけを原則とする。data変換またはcontract後はroll-forwardする。失敗時にDDLを自動で逆適用せず、DBAがmigration historyと実schemaを照合する。

Identity token tableの`Down`は未使用時だけtableをdropできるが、運用開始後はreset・確認reservationと監査上の時刻を失う。Productionでは`Down`を実行せず、新binaryの修正または機能無効化でroll-forwardする。migration失敗時は`identity.__ef_migrations_history`と両tableの存在、index/FKの作成状況を照合し、DDL transactionがrollback済みであることを確認してから専用`migrate` commandを再実行する。

`AddRegistrationInvitations`のindexは新規空table上で作成するため、既存の大規模tableを走査しない。

`Up`はtransaction-localの`lock_timeout=5s`と`statement_timeout=60s`を設定し、`Down`も`lock_timeout=5s`を設定する。

DDL競合時は待ち続けずmigration transaction全体をrollbackし、blockerとmigration historyを照合してから再実行する。

適用順序はmigration job、新binary、`InvitationRequired=true`の設定変更とする。

`Down`は発行済みcode、reservation、消費監査を全削除するため、招待発行後のProductionでは実行しない。

切戻し時は招待登録を停止し、tableを維持したまま旧binaryまたは修正版へroll-forwardする。

`ActiveDeliveryEndpointUniqueness` の `Down` は、同じ Activity と endpoint を持つ terminal Delivery が複数作られた後には full unique index を再作成できない。

この場合は rollback を試行せず、partial active index を維持したまま application を roll-forward する。

`Up` は旧 full unique index が存在する間に新 partial unique index を `CONCURRENTLY` で作るため、active duplicate を取り込まない。

## Emoji reaction expansion

`AddFederatedEmojiReactions` は既存 `like_relations` にnullable columnだけを追加し、defaultやtable全体のbackfillを行わない。
旧binaryは追加columnを無視でき、新binaryはnullを標準Like `👍` として読む。

`AddLitePubEmojiReactionAggregate` は新規 `emoji_reaction_relations` tableとindexだけを作成し、既存tableやrowを書き換えない。
導入順序は migration job、新binary API、Worker の順とする。
rollbackが必要なら新binaryを停止して旧binaryへ戻せるが、migrationの `Down` は受信済みEmojiReact関係を全削除するため本番では実行しない。
dataが作成された後はtableを維持したままroll-forwardする。
大規模運用でindex構築時間が許容できない場合は、空table作成直後にindexを構築してから書込みを有効化する同じ順序を守る。

## Remote Actor media cache expansion

`AddRemoteActorMediaCache` は空の `remote_actor_media_cache` table、外部キー、一意制約、期限・lease indexだけを追加するExpand migrationである。
既存tableのrewrite、default、backfillはなく、indexは書込み開始前の空table上で作成される。
旧binaryは新tableを参照しないためmigration適用後も稼働できる。

空tableの作成であっても、外部キーを追加する瞬間には参照先の`media`と`remote_actors`へ短時間のlockが必要になる。
このmigrationはtransaction内で`lock_timeout=5s`と`statement_timeout=60s`を設定し、長いtransactionやDDL競合がある場合はWeb書込みを待たせ続けずmigration job全体をrollbackする。
適用前に両tableのlong transactionとlock waitを確認し、timeout時はmigration historyとtable不在を確認してから、blocking transactionが解消した時間帯に同じ専用jobを再実行する。
timeoutを無効化して成功扱いにしてはならない。

混在期間は必ず `Media:UnreferencedRetention` より短くし、`RemoteMediaCacheRetention` をそれ以下に保つ。
新binaryは取得時と304再検証時に参照Mediaの更新時刻をrefreshするため、この条件下では旧binaryのGCも有効なActor画像を回収しない。
導入順序はmigration job、新binary API、旧binaryのdrainである。

書込み開始後の `Down` はcache metadataとleaseを失い、同じ画像の再取得を発生させる。
S3上のMedia本体は削除しないが、本番ではDownを実行せず、新binaryの停止または機能設定で切り戻してroll-forwardする。
