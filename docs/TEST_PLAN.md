# テスト計画

## 自動テスト

| 層 | 主な対象 |
| --- | --- |
| Domain | visibility、ownership、Follow、retry、state machine、moderation |
| Federation | scalar/array、IRI/Object/Link、type配列、拡張保持、blind recipient除去、legacy/RFC 9421署名、digest、actor/key binding、SSRF、overall timeout、展開後size、sanitizer |
| Persistence | 一意制約、transactional outbox、複数claimer、lease回収、followers recipient snapshot、moderation enforcement、緊急停止、DB接続切断、migration、pg_dump/pg_restore |
| API | discovery、content negotiation、conditional GET、認証認可、CORS、rate limit、private Objectの未署名拒否・署名済み受信者許可・collection非掲載 |
| Media | magic-byte MIME、filename、quarantine、private authorization、GC claim/purge |
| Moderation | domain/actor policy、hash-chained audit |
| Property | scalar/array 正規化、blind recipient 除去、sanitizer の不変条件を各 500 case |
| Fuzz | ActivityStreams parser と HTML sanitizer の coverage-guided harness |

全テストはskipなしで実行する。TestcontainersテストはDocker上の実PostgreSQLを使い、InMemory providerで代替しない。fixtureは出典とlicenseを同梱する。

## Release gate

```bash
dotnet tool restore
dotnet restore ActivityPubServer.slnx --locked-mode
dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore
dotnet build ActivityPubServer.slnx --configuration Release --no-restore
dotnet test ActivityPubServer.slnx --configuration Release --no-build
dotnet list ActivityPubServer.slnx package --vulnerable --include-transitive --no-restore
bash eng/check-licenses.sh
dotnet ef migrations script --no-build --configuration Release \
  --project src/ActivityPub.Persistence --startup-project src/ActivityPub.Persistence \
  --context FederationDbContext --output migrations-federation.sql
dotnet ef migrations script --no-build --configuration Release \
  --project src/ActivityPub.Persistence --startup-project src/ActivityPub.Persistence \
  --context LocalIdentityDbContext --output migrations-identity.sql
docker build --tag activitypub-server:verify .
docker compose config --quiet
```

CIはlock file、format、analyzer/nullable、全テスト、NuGet/OSV脆弱性、license、migration script、container build、non-root/read-only起動、SPDX SBOM、High/Critical image scanをgateにする。警告は`TreatWarningsAsErrors`で失敗させる。

## 手動・環境試験

次はrelease candidateごとに隔離環境で実行し、日時、commit、image digest、依存サービスversion、結果、artifact URIを記録する。

- 日常のMastodon、Misskey、Pleroma双方向interopは、version固定の[fediverse-pasture localverse](LOCAL_FEDERATION.md)で実施する
- GoToSocialはPasture adapter追加後、PeerTubeは公式image用adapter追加後に同じ記録形式で実施する
- MinIO/S3、ClamAV、Vault 停止、遅延、権限拒否。local Toxiproxy drill は自動化済みであり、production service の試験は別に行う
- Worker kill、API rolling replacement、migration前後versionの同時稼働
- PostgreSQL failover、pool exhaustion、PITR、object version restore
- inbox/outboundそれぞれの負荷、queue recovery、24時間以上のsoak
- parser と sanitizer の長時間 coverage-guided fuzzing

未実施項目は[検証記録](VERIFICATION.md)に未検証として残し、成功とみなさない。
