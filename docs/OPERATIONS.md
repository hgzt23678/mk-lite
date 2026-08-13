# 監視とアラート

ApplicationはOTLPでtrace/metricを送る。ComposeのcollectorはPrometheus endpointを`127.0.0.1:9464`へ公開する。health endpointは`/health/live`、`/health/ready`、`/health/startup`である。readyはDB、schema compatibility、必要なWorker heartbeatを確認し、remote Fediverseの可用性は条件に含めない。

## Metrics

| Metric | 意味 | 主なlabel |
| --- | --- | --- |
| `activitypub.inbox.accepted` | Inbox受理 | なし |
| `activitypub.activities.processed` | 副作用処理完了 | `activity.type` |
| `activitypub.signature.verified/failed` | 署名結果 | `profile` |
| `activitypub.inbox.duplicates` | 同一ID/同一payloadの重複 | なし |
| `activitypub.inbox.processing_delay` | 受付から処理までの秒数 | なし |
| `activitypub.delivery.queue_depth` | 未配送件数 | なし |
| `activitypub.delivery.oldest_pending` | 最古未配送秒数 | なし |
| `activitypub.delivery.succeeded/retries` | 配送結果 | domain、HTTP status |
| `activitypub.dead_letters` | Dead Letter化 | domain、HTTP status |
| `activitypub.remote.latency/status` | remote HTTP | domain、HTTP status |
| `activitypub.keys.cache_hits/misses` | remote key cache | なし |
| `activitypub.ssrf.rejected` | Safe HTTPによる拒否 | なし |
| `activitypub.rate_limited` | rate limiter拒否 | policy |
| `activitypub.worker.active_leases` | active lease | worker type |
| `activitypub.redis.cache_hits/misses` | timeline候補ID・未読通知count cache | `cache` |
| `activitypub.redis.failures` | Redis処理失敗（DB fallback継続） | `failure.type` |
| `activitypub.redis.wakeups` | delivery/inbox worker wake-up送信 | `queue` |

Npgsql、ASP.NET Core、HttpClient、.NET runtime metricsもexportする。traceにはActivity ID、Delivery ID、remote domainを相関値として使うが、投稿本文、DM、token、cookie、署名全体、秘密鍵、blind recipientを属性にしない。

## Queue管理とRedis

- `GET /admin/federation/queue/stats`はwaiting、active lease、delayed retry、lease切れstalled、Dead Letter、cancelled、最古job、次回実行時刻、delay上位domainを返す。
- `GET /admin/federation/queue/jobs?state=Pending&remoteDomain=example.org&limit=50`はpayloadや署名を含めず、job ID、Activity ID、endpoint、lease、attempt、status/error codeだけを返す。Inboxは`/admin/federation/queue/inbox-jobs`で分離する。
- Dolphin互換管理client向けには`POST /api/admin/queue/stats`、`jobs`、`deliver-delayed`、`inbox-delayed`を提供する。Bullの`clear`は監査・復旧履歴を破壊するため実装しない。
- emergency pause、domain停止、Dead Letter replayは既存の`/admin/operations`と`/admin/dead-letters`を使う。queue全消去endpointは提供しない。
- `Redis:ConnectionString`が設定されるとdelivery/stream wake-up、timeline候補ID、未読通知数を高速化する。Redisを停止またはflushしてもPostgreSQL polling/queryへ戻る。Redis障害そのものをready失敗条件にはしない。
- timeline cache hitでもPostgreSQLからrowを再取得し、visibility、follow、mute、block、Silenceを再評価する。Redisへ本文、DM、token、Cookie、署名を保存しない。

## 初期alert条件

次は初期値であり、baselineと明示されたSLOに合わせて調整する。

- readyが連続2分失敗、startupがdeploy deadlineを超過、または稼働中instanceのlive失敗
- `delivery.oldest_pending`が15分超を10分継続、またはqueue depthが30分連続増加
- Dead Letterが5分間に1件以上、再試行率が配送試行の20%超を10分継続
- signature failure率が受信署名の10%超かつ20件以上/5分
- HTTP 401/403、429、5xxのdomain別急増、またはdomain circuit openが15分超
- required Worker heartbeat欠落、active leaseの突然の0化または飽和
- key cache miss率80%超、SSRF拒否またはrate limitのbaseline比5倍
- Npgsql pool待ち/timeout、DB error、disk/WAL容量、S3/Vault/ClamAV error
- Redis errorの継続と同時にDB query latencyまたはpoll負荷が上昇した場合。Redis単独停止は配送消失alertではなくdegraded alertとする

アラートにはrunbook URL、環境、release、相関IDを含める。domain labelは高cardinalityなので保持期間と集約規則を設定する。
