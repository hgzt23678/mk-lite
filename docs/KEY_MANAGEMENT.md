# 鍵管理方針

## Local actor keys

ActorごとにRSA 2048-bit以上の鍵を分離する。production providerはVault Transitで、DBにはkey IRI、owner、public PEM、Vaultのopaque handle、作成/有効化/retire/revoke時刻を保存し、private exponentを保存しない。署名はVaultへdigestを送り`rsa-v1_5-sha256`で実行する。

鍵作成はTransit key作成後にpublic keyを読み、ActorKey行とaudit eventをtransactionで確定する。rotationは新keyをactiveにして送信を切り替え、設定されたoverlap期間は旧public keyをActor documentへ残す。overlap後にretireし、compromise時は直ちにrevokeする。旧private keyでの新規署名は許可しない。

Vault tokenはabsolute secret-file pathから読み、DB/config/logへ保存しない。productionではHTTPS Vault endpointを必須とする。Vault policyは対象mountのcreate/read/signに絞り、operator用rotation権限とruntime sign権限を分離する。

## Remote keys

key IDをSafeFederationHttpClientだけで取得し、HTTPS origin、owner、Activity actor、Object owner originを照合する。public key cacheはexpiryとrefresh cooldownを持つ。署名失敗時は同一検証につき一度だけoriginから再取得し、無限refreshしない。RSA 2048-bit未満と不正PEMを拒否する。

## Recovery

Vault storage snapshot/KMS復旧、ActorKey metadata、public documentの3点を整合させる。DB restoreだけでkey handleが解決しなければoutboundをpauseしたままにする。Data Protection key ringはPostgreSQLへ保存し、production証明書で暗号化するため、証明書とpassword secretも別failure domainへbackupする。

通常rotationと漏洩対応の具体手順は[key compromise runbook](runbooks/key-compromise.md)に記載する。実Vaultを使った停止・restore・rotation試験は未実施であり、本番承認前の必須項目である。
