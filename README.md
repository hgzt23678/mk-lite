# ActivityPub Server

.NET 10、ASP.NET Core 10、PostgreSQLを使ったActivityPubバックエンドです。APIとWorkerを独立して水平スケールでき、Inbox、配送、リース、再試行、Dead Letterの信頼できる記録はPostgreSQLに置きます。外部ブローカーは必須ではありません。

## 現在の判定

Release ビルド、現行ソリューション全体806件の.NET単体・PostgreSQL/API/Worker/Blazor統合テスト、既存のローカルfrontend browser試験、3件のTailnet試験、PostgreSQLバックアップ復元、DB接続切断後の配送回収、非rootコンテナ、ローカルHTTP負荷測定を再現済みです。認証sliceの今回のChromium smokeは9件を実行し、9件成功しました。

Toxiproxy を使った MinIO、ClamAV、Vault、PostgreSQL の停止、遅延、資格情報拒否と復旧も通過しました。

同一 image digest を old と new に割り当てた rolling orchestration smoke は、138 回の連続 probe で失敗 0 を確認しました。

fediverse-pastureではMastodon 4.6.2、Misskey 2026.6.0、Pleroma 2.10.0との一部双方向連合を実測済みですが、全matrixは完了していません。
GoToSocial、PeerTube、Misskey 12.119.2 serverの実instance試験、1時間以上のsoak、異なる旧新binaryのrolling deployment、production PITRとS3、Vault、Data Protection keysの統合復元は未実施です。

Misskey v12 frontendは、`12.119.2`のVue版を比較oracleとして固定し、production経路を.NET 10 static SSR + Interactive Server Blazorへ移植中です。
移植基準は、フロントエンドのデザイン・DOM・CSS・挙動をMisskey v12.119.2、バックエンドの機能・API・連合・モデレーション・メディア・キュー挙動を`mei23/dolphin`として分離しています。
backend sourceはユーザー確認済みの`.cache/meidolphin`（origin `https://github.com/mei23/dolphin`、commit `3ce200269f814547dc7dfc6b246abadf8a9c00ed`）に固定しています。
現在は535 sourceすべてを分類済みです。生成mappingの内訳は`implemented` 152、`in-progress` 12、`blocked` 2、`planned` 335、`excluded` 34、`unclassified` 0です。これは個別sourceの証拠付き分類であり、Misskey v12 frontend全体の完成や互換宣言を意味しません。
`POST /api/signin`（JSON／multipart、TOTP、lockout、protected legacy WebAuthn challenge）、MiAuth `session:null`、`/settings/api`、`/settings/apps`は現行認証sliceとして自動試験済みです。実browser authenticator enrollment、外部OIDC provider live統合、Misskey 12.119.2実serverとのdifferential試験は未完了です。
登録、ログイン、全115 route、全400 Vue SFC相当、全CSS、Vue固有の状態遷移、全motionの移植は未完了であり、完全移植とは宣言しません。

絵文字reactionの連合は、Misskeyの `Like` + `_misskey_reaction` と、LitePub/Akkoma系の `EmojiReact`（受信alias `EmojiReaction`）を別aggregateで扱います。
custom emojiのmetadata、Undo、重複排除、同一Objectへの複数LitePub reaction、transactional deliveryを永続化します。

この状態を本番導入完了または完全互換とは宣言しません。

固定tagから生成した現在のinventoryでは、Mastodon 4.6.2は331経路中23、Misskey 12.119.2は321 endpoint中23が自動試験付きimplementedです。
残る308／298項目に加え、実clientと固定serverのdifferential testが未完了です。

実装済み範囲と未検証事項は[適合表](docs/CONFORMANCE.md)と[検証記録](docs/VERIFICATION.md)に固定しています。
frontend/backendの基準と、利用可能なDolphin checkoutの識別差異は[baseline manifest](artifacts/baselines.json)に記録しています。

## 構成

- `Domain`: フレームワーク非依存の集約、値、状態遷移
- `Application`: ユースケースとポート
- `Federation`: ActivityStreams変換、署名、Safe HTTP、Inbox/Outbox
- `Persistence`: EF Core、PostgreSQL制約、リース、マイグレーション
- `Media`: S3、MIME検査、ClamAV、ffmpeg、GC
- `Moderation`: ポリシー、Report、スパム判定、監査
- `Identity`: OAuth/OIDC認証・認可
- `MastodonApi`: Mastodon REST の限定互換 API
- `Api`: 公開API、管理API、health、OpenTelemetry
- `Workers`: Inbox処理と配送処理
- `Operations`: readinessと運用制御
- `MisskeyApi`: Misskey v12 REST projection と durable command adapter
- `frontend/ActivityPub.Misskey.Blazor`: productionのMisskey v12 Blazor移植先
- `frontend/misskey-v12`: 固定したVue visual/behavior oracleとinventory入力。本番実行経路には含めない

## ローカル起動

前提は.NET SDK 10.0.302とDocker Composeです。

連合機能の日常開発と相互運用確認は、Mastodon、Misskey、Pleromaを同じ隔離Dockerネットワークで動かす`fediverse-pasture`を標準環境とします。
Pastureのcompose revisionと各実装versionはリポジトリで固定し、任意の`latest`へ追従しません。

```bash
cp .env.example .env
# AP_VAULT_TOKEN と同じ値を、AP_VAULT_TOKEN_FILE が指す
# repository 外の mode 0400 または container UID 1654 が読める file に保存する
bash eng/pasture.sh up
bash eng/pasture.sh create-actor alice "Alice"
```

Mastodonは`http://localhost:2970`、このサーバーは`http://localhost:2971`、Pleromaは`http://localhost:2972`、Misskeyは`http://localhost:2973`で開きます。
詳しい起動、固定version、アカウント、試験記録方法は[ローカル連合開発](docs/LOCAL_FEDERATION.md)を参照してください。

Tailnet内の別端末から移植済みfrontendを確認するときは`bash eng/pasture-tailscale.sh up`を使います。
この経路はTailscale Funnelを有効化せず、ActivityPubの永続IRIを変更せずにbrowser用OIDC frontchannelだけを明示的なHTTPS originへ分離します。

PastureモードはHTTPとRFC1918接続を列挙済みDockerホストにだけ許可し、外部Fediverseへの連合を拒否します。
この例外はDevelopment専用であり、Productionでは設定されているだけで起動を拒否します。
Pastureを必要としない単体のインフラ・障害試験では、従来どおり`docker compose up --build`を使用できます。

`.env.example`の値はローカル検証専用です。本番へ流用しないでください。公開IRIは不変の`Federation__PublicBaseUri`からのみ生成し、`Host`ヘッダーからは生成しません。

個別の品質ゲートは次のとおりです。

```bash
dotnet tool restore
dotnet restore ActivityPubServer.slnx --locked-mode
dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore
dotnet build ActivityPubServer.slnx --configuration Release --no-restore
dotnet test ActivityPubServer.slnx --configuration Release --no-build
bash eng/check-licenses.sh
npm --prefix frontend/misskey-v12 ci --ignore-scripts
npm --prefix frontend/misskey-v12 run inventory:check
npm --prefix frontend/misskey-v12 run typecheck
npm --prefix frontend/misskey-v12 test
npm --prefix frontend/misskey-v12 run verify:upstream
npm --prefix frontend/misskey-v12 run build
npm --prefix frontend/misskey-v12 audit --audit-level=high
node eng/check-frontend-licenses.mjs
npm --prefix tests/frontend-blazor-e2e test
```

DBマイグレーションはWeb起動と分離されています。

```bash
dotnet run --project src/ActivityPub.Api -- migrate
```

## 本番導入

[本番設定例](deploy/appsettings.Production.example.json)を基に、接続文字列、S3資格情報、Vaultトークン、Data Protection証明書はSecret Storeから注入してください。APIではWorkerを無効にし、Workerデプロイでは必要なWorkerだけを有効にできます。導入前に[本番チェックリスト](docs/PRODUCTION_CHECKLIST.md)を環境ごとに完了させます。

## 文書

- [実装計画](docs/IMPLEMENTATION_PLAN.md)
- [仕様適合・相互運用表](docs/CONFORMANCE.md)
- [Mastodon REST API 互換表](docs/MASTODON_API.md)
- [Mastodon 4.6.2 endpoint inventory](docs/compatibility/MASTODON_4_6_2.md)
- [Misskey 12.119.2 endpoint inventory](docs/compatibility/MISSKEY_12_119_2.md)
- [API間の意味論](docs/compatibility/CROSS_API_SEMANTICS.md)
- [Misskey v12 frontend の移植判定](docs/MISSKEY_V12_FRONTEND.md)
- [Misskey v12 UI/CSS/挙動の完全同等性要件](docs/frontend-blazor/PARITY_REQUIREMENTS.md)
- [fediverse-pastureによるローカル連合開発](docs/LOCAL_FEDERATION.md)
- [脅威モデル](docs/THREAT_MODEL.md)
- [データモデル](docs/DATA_MODEL.md)
- [配送状態遷移](docs/DELIVERY_STATE_MACHINE.md)
- [鍵管理](docs/KEY_MANAGEMENT.md)
- [テスト計画](docs/TEST_PLAN.md)
- [検証記録](docs/VERIFICATION.md)
- [負荷試験](docs/PERFORMANCE.md)
- [監視とアラート](docs/OPERATIONS.md)
- [デプロイ](docs/DEPLOYMENT.md)
- [依存関係](docs/DEPENDENCIES.md)
- [調査根拠と採用判断](docs/REFERENCES.md)
- [Runbook](docs/runbooks/README.md)
- [ADR](docs/adr/README.md)
# mk-lite
