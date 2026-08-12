# 秘密鍵漏洩とrotation

1. 影響Actorまたは全配送を直ちにpauseし、incident IDを作る。
2. Vault Transit keyをdisable/revokeし、DBのActorKeyをrevokedへ遷移する。秘密鍵materialをlog/exportしない。
3. 新しいactor別keyを作成し、public key documentを切り替える。通常rotationは`POST /admin/local-actors/{username}/rotate-key`へoverlap時間を指定し、新旧public keyを重複公開してから旧keyをretireする。
4. compromise時はoverlapを最小化し、旧keyで署名された受信なりすましと、露出期間中のoutbound attempt/auditを調査する。
5. signed GETと少数domainへの配送をcanaryし、401/403時の一度だけのkey refreshを確認して再開する。
6. Vault token、admin、DB、S3、Data Protection credentialsの露出を除外できなければ同時rotateする。

Vault snapshotとpolicy restoreを隔離環境で定期検証する。DBだけを復元してkey handleが解決できない状態では配送を再開しない。
