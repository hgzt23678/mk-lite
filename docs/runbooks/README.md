# Runbooks

- [PostgreSQL・object・keyのbackup/restore/PITR](backup-restore.md)
- [DB migration](database-migration.md)
- [配送障害・誤配送停止・domain障害](delivery-incident.md)
- [Dead Letterとqueue再構築](queue-recovery.md)
- [秘密鍵漏洩とrotation](key-compromise.md)
- [データ削除・Actor削除・Tombstone](data-deletion.md)
- [server廃止とDelete配送](decommission.md)
- [Local 障害注入、PostgreSQL failover、production integrated restore](fault-injection.md)

全操作はticket/incident ID、operator、理由、開始・終了時刻、対象、実行結果を残す。管理APIは`activitypub.admin` roleの短寿命tokenを使い、tokenをshell historyやlogへ残さない。
