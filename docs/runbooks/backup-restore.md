# Backup、PITR、restore

## Backup

1. PostgreSQLでbase backupと継続WAL archiveを別failure domainへ保存し、暗号化、immutability、retention、成功alertを有効にする。
2. S3 bucket versioningとobject lock/lifecycleを有効にし、DB backup時刻より前のversionを消さない。
3. Vault storage snapshot/KMS policyとactor key handle metadataを別管理権限でbackupする。Transit keyを平文exportしない。
4. Data Protection key ring、保護証明書、証明書passwordを別々のsecret backupへ保存する。
5. configuration、release image digest、migration artifact、OIDC/Vault/S3 policyを保存する。

## Point-in-time restore

1. 隔離networkへ新しいPostgreSQL/S3/Vaultを用意し、外向き配送をnetwork policyで遮断する。
2. base backupとWALを指定時刻までreplayする。元clusterを上書きしない。
3. 同じ時点以前のS3 object version、Vault snapshot、Data Protection keysを復元する。
4. applicationをglobal outbound pause状態で起動し、startup/ready、schema compatibility、row count、FK、activity/object hash、S3 checksum、key handle解決を検証する。
5. active環境とrestore環境のpending/leased/delivery attemptを比較する。重複配送の危険があればrestore側jobをcancelまたはreconcileする。
6. signed GETと署名生成をlocal probeで確認する。復元承認後だけegressを開け、canary domainから配送を再開する。

## Drill acceptance

指定RPO以内のtimestampへ復旧し、Actor/Object/Delivery、private media authorization、actor signing、Data Protection復号を照合する。RTO、欠損object、破損hash、手動補正を記録する。repositoryの自動testは`pg_dump`/`pg_restore`のschema/data round tripまでで、production PITR/S3/Vault restoreを代替しない。

環境固有の復元 command は `eng/production-recovery-drill.sh` の hook として実装する。

Hook contract と PostgreSQL failover drill は [障害注入 Runbook](fault-injection.md) に記載する。
