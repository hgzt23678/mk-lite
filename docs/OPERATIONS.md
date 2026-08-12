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

Npgsql、ASP.NET Core、HttpClient、.NET runtime metricsもexportする。traceにはActivity ID、Delivery ID、remote domainを相関値として使うが、投稿本文、DM、token、cookie、署名全体、秘密鍵、blind recipientを属性にしない。

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

アラートにはrunbook URL、環境、release、相関IDを含める。domain labelは高cardinalityなので保持期間と集約規則を設定する。
