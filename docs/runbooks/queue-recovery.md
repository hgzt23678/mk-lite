# Queue再構築とDead Letter再処理

## Lease recovery

Worker crash時は新Workerを起動し、lease expiry後の自動回収を待つ。DBの`deliveries`を直接Pendingへ更新しない。heartbeat、lease owner/expiry、attempt history、global/domain pauseを確認する。

`GET /admin/federation/queue/stats`でwaiting/delayed/stalledと最古jobを確認し、`GET /admin/federation/queue/jobs`をstate/domainで絞る。Redis wake-upが停止してもWorkerは`Workers:PollInterval`ごとにPostgreSQLをclaimする。Redis復旧のためにdelivery rowを再作成しない。

## Dead Letter

1. `GET /admin/dead-letters?limit=100`でreason、source、attemptを確認する。
2. remote endpoint/key/signature/block状態を再評価し、原因を修復する。
3. 対象ごとに`POST /admin/dead-letters/{id}/replay`を実行する。元Dead Letterとattempt historyは保持され、二重replayは拒否される。
4. 新attemptの相関ID、status、remote response sizeを確認する。

同じDead Letterの二重replayは拒否される。大量replayはremote障害を増幅するため、domain policyとcircuit状態を確認し、少数canaryから段階的に行う。

## Queue reconstruction

DB損傷やbugでDeliveryだけを失った場合に限り、読み取り専用診断でActivityRecipient、Activityの保存済みUTF-8 bytes、RemoteEndpoint、既存Delivery/Attemptを照合する。専用のreview済みrepair migrationで欠落した`activity + endpoint`だけを一意制約下にinsertする。Activityを再serializeしたり、成功済みendpointを再生成したりしない。実行前後の件数/hashとSQL artifactを監査保管する。
