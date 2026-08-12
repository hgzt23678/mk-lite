# 本番運用チェックリスト

`[x]`はrepository内で自動検証した項目、`[ ]`は導入環境でoperatorが完了させる項目である。

## Security

- [x] canonical public originを設定から生成し、productionのplaceholder/localhostを起動時に拒否する
- [x] SafeFederationHttpClientがHTTPS、userinfo、private/link-local/multicast/metadata IP、redirect、DNS変更、size、timeoutを制御する
- [x] local private keyをDBへ保存せずVault Transit handleを保持する
- [x] OIDC authority/audience、admin role、CORS、trusted proxyを明示設定する
- [x] 本文、token、cookie、private audience、key materialをtelemetry labelにしない
- [x] password reset・email確認tokenをhashだけ永続化し、URL fragmentから即時消去してsame-origin antiforgery POSTで単回消費する
- [x] Productionで平文SMTP、HTTP公開URL、不正なsecret-file組合せを起動時に拒否する
- [ ] egress firewallでもinternal/metadata networkを拒否する
- [ ] SMTP providerの証明書chain、STARTTLS downgrade拒否、送信元SPF/DKIM/DMARC、bounce監視、実配送をstagingで確認する
- [ ] production Vault policy、OIDC issuer、MFA、break-glass adminを組織要件に合わせて検証する
- [ ] log storageのPII保持期間と削除を設定する

## Data and recovery

- [x] startup migrationを行わず、専用`migrate` commandを提供する
- [x] Identityのpassword reset・email確認reservationを既存rowのrewriteなしの新規tableとしてexpandする
- [x] nullable expansionとconcurrent indexを別migrationに分離する
- [x] Testcontainers上で`pg_dump`/`pg_restore`し、deliveryとmarkerを検証する
- [x] raw JSON の期限付き batch purge と legal hold を同一 transaction の audit event とともに保存する
- [ ] WAL archive/PITR、S3 versioning、Vault backup、Data Protection key backupをproductionで有効化する
- [ ] production相当の隔離環境でRPO/RTOを満たすrestore drillを完了する
- [ ] retention、legal hold、利用者削除SLAを確定する

## Federation

- [x] legacy/RFC 9421のfixtureとround tripが通る
- [x] duplicate、payload conflict、signature replayをDB制約と隔離で制御する
- [x] retry、lease expiry、per-domain concurrency/circuit、global/domain pause、Dead Letterを永続化する
- [ ] Mastodon、Misskey、GoToSocial、PeerTubeの固定versionで相互運用表を埋める
- [ ] signed GET secure mode、key rotation overlap、sharedInboxを実peerで確認する
- [ ] semantic side effectが限定的なActivityを導入要件と照合する

## Deployment and operations

- [x] containerをnon-root、read-only root filesystem、cap-drop、graceful stopで起動できる
- [x] `/health/live`、`/health/ready`、`/health/startup`とOTLP/Prometheus経路を提供する
- [x] APIとWorkerを別設定で起動できる
- [x] emergency global pauseのclaim停止・再開を自動テストする
- [x] local Toxiproxy 環境で S3、ClamAV、Vault、PostgreSQL の停止、遅延、資格情報拒否、復旧を検証する
- [ ] orchestratorのPodDisruptionBudget、resource request/limit、termination graceを実測調整する
- [ ] alert routing、on-call contact、status page、incident escalationを登録する
- [x] 同一 image digest の rolling orchestration smoke で連続 probe の失敗 0 と DB count 不変を確認する
- [ ] 異なる旧新 image の schema 同時稼働試験を完了する
- [ ] `eng/production-recovery-drill.sh` を環境固有 hook で実行し、PITR、S3 version、Vault snapshot、Data Protection key と証明書を同じ復元点で照合する
- [ ] exact production target値と負荷・soak試験結果を承認する

## Release decision

- [ ] 外部interop、障害試験、restore drill、rolling deployment、security reviewの証拠URIをrelease recordへ添付する
- [ ] 未実装機能と既知のリスクをproduct ownerとoperatorが承認する
