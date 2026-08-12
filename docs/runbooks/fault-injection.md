# 障害注入 Runbook

## Local dependency drill

`eng/fault-injection.sh` は通常の Docker Compose 構成に Toxiproxy を追加する。

対象は PostgreSQL、S3 互換 MinIO、ClamAV、Vault Transit である。

次の環境変数を設定する。

```bash
export AP_POSTGRES_PASSWORD='local-only-random-value'
export AP_MINIO_ROOT_USER='local-only-access-key'
export AP_MINIO_ROOT_PASSWORD='local-only-secret-key'
export AP_VAULT_TOKEN='local-only-random-token'
bash eng/fault-injection.sh
```

script は一時 token file を作り、終了時に service、volume、一時 token を削除する。

既に検証済みの image を使う場合だけ `CHAOS_SKIP_BUILD=true` を指定できる。

判定は `artifacts/chaos-*/result.json` に出力する。

各想定障害が成功してしまった場合、復旧後の probe が失敗した場合、quarantine record が残らない場合は非 0 で終了する。

## Production PostgreSQL failover

`eng/postgres-failover-drill.sh` は HA provider を直接操作しない。

次の executable hook を運用環境で用意する。

- `APPLICATION_PROBE`：HA endpoint を使って `dependency-probe` を呼ぶ wrapper
- `FAILOVER_HOOK`：provider control plane から standby を promote し、JSON を 1 行返す command

`MAXIMUM_RECOVERY_SECONDS` を RTO の判定値に設定する。

drill は failover 前に Data Protection payload を作り、endpoint 復旧後に DB 接続と同じ payload の復号を照合する。

## Production integrated restore

`eng/production-recovery-drill.sh` は次の executable hook を要求する。

- `SOURCE_PROBE`
- `RESTORED_PROBE`
- `POSTGRES_RESTORE_HOOK`
- `S3_RESTORE_HOOK`
- `VAULT_RESTORE_HOOK`
- `DATA_PROTECTION_RESTORE_HOOK`
- `EGRESS_ASSERT_HOOK`

PostgreSQL hook は PITR target time、S3 hook は object version restore、Vault hook は snapshot restore、Data Protection hook は key ring と保護証明書の restore を JSON で報告する。

復元先は外向き通信を遮断した隔離環境でなければならない。

script は source で作った media ID、Vault 署名、Data Protection 暗号文を restored environment で照合する。

Provider 固有 hook がない状態では実行できない。

これは未実施を成功として扱わないための fail-closed 条件である。
