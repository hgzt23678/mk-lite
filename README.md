# mk-lite

[![CI](https://github.com/hgzt23678/mk-lite/actions/workflows/ci.yml/badge.svg)](https://github.com/hgzt23678/mk-lite/actions/workflows/ci.yml)
[![License: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0--only-blue.svg)](LICENSE)

`mk-lite`は、.NET 10、ASP.NET Core 10、PostgreSQLで構築するActivityPubサーバーです。
耐久性のある連合処理と、Misskey v12の画面をBlazorへ移植したWebフロントエンドを同じリポジトリで開発しています。

> [!WARNING]
> このプロジェクトは開発中です。
> Mastodon API、Misskey API、Misskey v12フロントエンドの完全互換や、本番導入の準備完了を宣言するものではありません。

## 提供する機能

- ActivityPubのInbox、Outbox、Actor、Object、Collectionを扱います。
- Create、Update、Delete、Follow、Accept、Reject、Like、Announce、Undo、Blockの受信と送信を実装しています。
- Inboxの重複排除、配送の再試行、リース、Dead LetterをPostgreSQLへ永続化します。
- Public、Unlisted、Followers-only、Mentioned-onlyの可視性と、署名付きprivate dereferenceを扱います。
- S3互換ストレージ、ClamAV、ffmpegを使ったメディア検査、変換、キャッシュを提供します。
- ローカルアカウント、セッションCookie、MiAuth、権限付きアプリトークンを提供します。
- APIとWorkerを分離し、それぞれを水平に増やせる構成を採用しています。
- OpenTelemetry、readiness、配送停止、監査、バックアップと復旧の運用境界を備えています。

実装済みの範囲と自動試験、外部実装との接続結果は[仕様適合表](docs/CONFORMANCE.md)で区別しています。

## 互換性の境界

フロントエンドとバックエンドは、別の参照実装を基準にしています。

- **画面の基準**：Misskey v12.119.2のDOM、CSS、画面挙動を参照します。
- **バックエンドの基準**：`mei23/dolphin`のAPI、永続化、認可、連合挙動を参照します。
- **本番フロントエンド**：ASP.NET Coreのstatic SSRとInteractive Server Blazorで動作します。
- **比較用Vueコード**：移植の比較とinventory生成にだけ使い、本番の実行経路には含めません。

2026年8月12日時点の生成inventoryでは、Misskey v12由来の535 sourceを329件の`implemented`と206件の`excluded`へ分類しています。
`implemented`は、対応するRazor実装と自動試験の証拠があることを示します。
`excluded`には、Dolphin側に完全な契約がないDrive管理、Chart、Gallery、Antenna、Clip、Channelなどが含まれます。

この分類は「Misskey v12の全機能を移植済み」という意味ではありません。
API inventoryでも、Mastodon 4.6.2は331経路中23件、Misskey 12.119.2は321 endpoint中23件を実装済みとしており、残りは未対応です。

最新の分類は[フロントエンド残タスク](docs/frontend-blazor/REMAINING_TASKS.md)、[Mastodon API inventory](docs/compatibility/MASTODON_4_6_2.md)、[Misskey API inventory](docs/compatibility/MISSKEY_12_119_2.md)で確認できます。

## ソフトウェア構成

| パス | 役割 |
| --- | --- |
| `src/ActivityPub.Domain` | フレームワークに依存しない集約、値、状態遷移 |
| `src/ActivityPub.Application` | ユースケースと外部依存のポート |
| `src/ActivityPub.Federation` | ActivityStreams変換、HTTP署名、安全な外部HTTP通信 |
| `src/ActivityPub.Persistence` | EF Core、PostgreSQL制約、マイグレーション、耐久キュー |
| `src/ActivityPub.Media` | メディア検査、変換、保存、キャッシュ、GC |
| `src/ActivityPub.Identity` | ローカル認証、セッション、MiAuth、アプリトークン |
| `src/ActivityPub.MastodonApi` | 対応範囲を限定したMastodon REST API |
| `src/ActivityPub.MisskeyApi` | Misskey v12 APIのprojectionとcommand adapter |
| `src/ActivityPub.Api` | 公開API、管理API、health、OpenTelemetry |
| `src/ActivityPub.Workers` | Inbox処理と配送処理 |
| `frontend/ActivityPub.Misskey.Blazor` | 本番用のMisskey v12 Blazorフロントエンド |
| `frontend/misskey-v12` | 固定したVue比較元とinventory入力 |
| `tests/` | 単体、統合、property、ブラウザー試験 |

依存方向とブラウザー境界は[フロントエンドアーキテクチャ](docs/frontend-blazor/ARCHITECTURE.md)に記録しています。

## 必要な環境

- Git
- .NET SDK 10.0.302
- Docker Engine
- Docker Compose 2.24.4以降
- Node.js 22.23.1（フロントエンドの検証時）

.NET SDKのバージョンは[`global.json`](global.json)、Node.jsのバージョンは[CI workflow](.github/workflows/ci.yml)で固定しています。

## ビルドと自動試験

リポジトリ全体の.NETコードは次の手順で検証できます。

```bash
dotnet tool restore
dotnet restore ActivityPubServer.slnx --locked-mode
dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore
dotnet build ActivityPubServer.slnx --configuration Release --no-restore
dotnet test ActivityPubServer.slnx --configuration Release --no-build
```

Misskey v12比較元と生成inventoryは次の手順で検証できます。

```bash
npm --prefix frontend/misskey-v12 ci --ignore-scripts
npm --prefix frontend/misskey-v12 run typecheck
npm --prefix frontend/misskey-v12 test
npm --prefix frontend/misskey-v12 run inventory:check
npm --prefix frontend/misskey-v12 run verify:upstream
```

CIはRelease build、自動試験、ブラウザー回帰試験、migration script生成、依存関係監査、ライセンス検査、コンテナスキャン、SBOM生成を実行します。
2026年8月13日の検証記録では、.NET試験913件が成功し、失敗とskipはありません。
再現環境と未検証項目は[検証記録](docs/VERIFICATION.md)を参照してください。

## ローカル連合環境

日常の連合試験には、Mastodon、Misskey、Pleromaと`mk-lite`を隔離Dockerネットワークで動かす`fediverse-pasture`を使います。
この環境はローカル相互運用試験用であり、本番構成や公開インターネット上の安全性を証明するものではありません。

最初に開発用設定を作成します。

```bash
cp .env.example .env
```

`.env`内の仮値はすべてローカル専用の値へ置き換えてください。
Vault tokenはリポジトリ外のmode `0400`ファイルへ保存し、その絶対パスを設定します。
`.env`、token file、証明書、秘密鍵はコミットしないでください。

設定後、ローカル連合環境を起動します。

```bash
bash eng/pasture.sh fetch
bash eng/pasture.sh config
bash eng/pasture.sh up
bash eng/pasture.sh create-actor alice "Alice"
```

| サービス | URL |
| --- | --- |
| Mastodon | `http://localhost:2970` |
| mk-lite | `http://localhost:2971` |
| Pleroma | `http://localhost:2972` |
| Misskey | `http://localhost:2973` |

状態確認と停止には次のコマンドを使います。

```bash
bash eng/pasture.sh status
bash eng/pasture.sh down
```

固定バージョン、ネットワーク制約、相互運用の確認手順は[ローカル連合開発](docs/LOCAL_FEDERATION.md)に記載しています。

## マイグレーションとデプロイ

DBマイグレーションはWebプロセスの起動から分離しています。

```bash
dotnet run --project src/ActivityPub.Api -- migrate
```

本番では接続文字列、S3資格情報、Vault token、Data Protection証明書をSecret Storeから注入します。
公開IRIは固定した`Federation__PublicBaseUri`から生成し、`Host`ヘッダーやブラウザーの現在URLから推測しません。

導入前に[デプロイ手順](docs/DEPLOYMENT.md)と[本番運用チェックリスト](docs/PRODUCTION_CHECKLIST.md)を環境ごとに確認してください。

## 開発状況を確認する文書

- [仕様適合と相互運用範囲](docs/CONFORMANCE.md)
- [検証記録](docs/VERIFICATION.md)
- [Misskey v12フロントエンドの残タスク](docs/frontend-blazor/REMAINING_TASKS.md)
- [脅威モデル](docs/THREAT_MODEL.md)
- [データモデル](docs/DATA_MODEL.md)
- [配送状態遷移](docs/DELIVERY_STATE_MACHINE.md)
- [依存関係とライセンス境界](docs/DEPENDENCIES.md)
- [運用Runbook](docs/runbooks/README.md)
- [ADR一覧](docs/adr/README.md)

## 上流コードとライセンス

Misskey v12の比較元は、`misskey-dev/misskey`のtag `12.119.2`、commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`へ固定しています。
該当するフロントエンドコードには[`LICENSE`](frontend/misskey-v12/LICENSE)と[`NOTICE.md`](frontend/misskey-v12/NOTICE.md)を同梱しています。

特記がないコードは、ルートの[`LICENSE`](LICENSE)に基づくGNU Affero General Public License version 3 onlyで提供します。
Misskey由来部分の帰属と変更範囲は[`NOTICE.md`](NOTICE.md)および各frontendディレクトリのNOTICEに記載しています。
第三者ライブラリと同梱アセットには、それぞれのライセンスが適用されます。
