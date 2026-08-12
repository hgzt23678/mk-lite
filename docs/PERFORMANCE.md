# 負荷試験

## 2026-08-02 baseline

`eng/measure-load.sh`が一時PostgreSQLを起動し、全migrationを適用し、Release版Kestrelへ実トラフィックを送る。実行条件は15秒、並行度32、`GET /nodeinfo/2.0`、Ubuntu 24.04.4、.NET 10.0.10、2 CPU。

| 指標 | 結果 |
| --- | ---: |
| requests | 13,183 |
| succeeded / failed | 13,183 / 0 |
| throughput | 878.41 requests/s |
| p50 / p95 / p99 | 34.45 / 57.44 / 73.18 ms |
| maximum | 125.24 ms |

これは軽量なpublic read endpointのbaselineであり、capacity保証ではない。署名検証、Inbox DB write、recipient expansion、S3 media、outbound remote latencyを含まない。

## 再現

```bash
DOTNET_COMMAND=/root/.dotnet/dotnet \
LOAD_DURATION_SECONDS=15 \
LOAD_CONCURRENCY=32 \
bash eng/measure-load.sh
```

`TARGET_LOCAL_ACTORS`、`TARGET_REMOTE_ACTORS`、`TARGET_INBOX_REQUESTS_PER_SECOND`、`TARGET_OUTBOUND_DELIVERIES_PER_SECOND`、`TARGET_QUEUE_RECOVERY_TIME`、`TARGET_DATA_RETENTION_DAYS`は導入者が明示する。次のcapacity runでは各値、dataset generator seed、instance size、PostgreSQL/S3構成、latency distribution、CPU/memory/DB pool、queue age、recovery timeをartifactへ保存する。
