# OpenCode 引き継ぎノート（ActivityPub 実装）

最終更新: 2026-08-05
対象: `/root/new-project`

このノートは、Sol が残した作業を前提に、OpenCode での次アクションに直結する形で整理した要約です。

## 0) 目的
- `/root/new-project` の ActivityPub 実装現状を短時間で引き継ぎ、次の継続ポイントを明確化する。
- 完了済みの実装領域・検証済み箇所・未完了の運用ブロッカーを分離して提示する。

## 1) 現在の全体像（完了度）
- プラットフォーム: .NET 10 / ASP.NET Core 10 / PostgreSQL。
- 構成は `Domain` / `Application` / `Federation` / `Persistence` / `Api` / `Workers` / `MastodonApi` / `MisskeyApi` / `frontend`。
- API と Worker は独立スケール可能なモジュール設計。
- テストは `Release build` と `263 passed` まで到達（詳細は [VERIFICATION](docs/VERIFICATION.md)）。

## 2) 実装済み（重要機能）
### 2.1 ActivityPub/Federation
- `src/ActivityPub.Federation`
- Inbox/Outbox 基盤（durable inbox、transactional outbox、非同期副作用、recipient snapshot）
- 受信サイドでの検証と保存（署名、所有権、addressing、replay 対策）
- Activity side effect: Create/Update/Delete/Follow/Accept/Reject/Flag/Add/Remove/Like/Announce/Move と Undo（actor/object 粘り込み）
- remote discovery、safe dereference、鍵更新、レガシー署名、RFC 9421 送受信検証
- blind recipient 除去（bto/bcc）
- 署名検証後の private GET と受信者許可
- JSON-LD context はネットワーク取得しない方針

### 2.2 配送/耐久
- 配送のレース制御（lease、retry、dead letter、再試行）
- 外部障害注入での復旧検証（delivery lease回収、attempt 記録、transport heartbeat）
- endpoint 更新（失敗先の Delivery endpoint 置換・target merge）
- 実配送側の耐久性（障害後の再claim）

### 2.3 Moderation/Policy
- actor/domain の Reject/Limit/Silence/RejectMedia/Mute/Admin Mute
- UserBlock aggregate（Follow解除・Undo・配送抑止・通知抑止）
- audit と recovery flow の骨子

### 2.4 Media
- S3, MIME/size/dimension/duration 検証
- ClamAV + ffmpeg + quarantine
- remote media proxy/cache
- private media への認証/許可、GC

### 2.5 認証/配布API
- OIDC + OpenIddict を用いた OAuth（authorization / token / revoke 含む）
- Mastodon API module は独立で限定実装
- Misskey 12 frontend の移植基盤（Blazor SSR+Interactive）は進行中

## 3) 実装完了だが未完了と見なすべき残作業
`docs/IMPLEMENTATION_PLAN.md` の評価に合わせ、次を未完と扱う。
- Notification subsystem（内部実装は一部存在だが、全経路完了ではない）。
- production policy approval（運用判断・承認フローの定着）
- GoToSocial / PeerTube の実instance双方向interop
- soak（1時間以上）
- 異バイナリ rolling（old/new imageでの schema 互換）
- production integrated restore（S3/PITR/Vault/DP keys の一体検証）
- Misskey v12 frontend 全体（AGPL境界、OIDC login、本番相当E2E、未実装route/DOM/motion）

## 4) 明示的不足（互換観点）
### 4.1 Mastodon 4.6.2（部分実装）
- 実装は [MASTODON_API](docs/MASTODON_API.md) で表明（23/331）
- 未実装は多く、`since_id/min_id` など一部ページング周辺が未完
- `custom_emojis` は永続aggregate未実装のため 404/簡易返却なし

### 4.2 Misskey 12.119.2
- 実装 20/321（固定差分）
- frontend 535 source 中 `in-progress` 40、`planned` 495、`implemented` 0（Blazor 側移植進行表）

### 4.3 外部連合
- 2026-08-03の pasture 実測で未完/失敗が残る項目あり（`artifacts/interop/pasture/20260803T061125Z/interop-matrix.md`）
- 特に Misskey/Pleroma で特定非公開範囲の永続化不一致が残存

## 5) 参照して着手すべき最短チェックリスト
1. 先に `docs/CONFORMANCE.md` を読む
2. `docs/VERIFICATION.md` の「未検証またはblocked」を順に潰す
3. `docs/MASTODON_API.md` + `docs/compatibility/MASTODON_4_6_2.md` で API 追加順を決める
4. `docs/compatibility/MISSKEY_12_119_2.md` + `docs/MISSKEY_V12_FRONTEND.md` でフロント進行を固定
5. `docs/LOCAL_FEDERATION.md` と `eng/pasture.sh` で interop 再現環境を起動

## 6) いまの次アクション（優先順）
- まずは「未完了・高リスク」カテゴリの1件ずつを片付ける
  - Interop matrix の差分（Misskey Followers-only / Pleroma Mentioned-only）
  - Notification subsystem の実運用カバレッジ
  - production restore シナリオ（S3/Vault/DP/PG）
- その後 `MASTODON_API` の blocking endpoint を 1ブロックずつ追加していく
  - stream / marker / search など順に増やす

## 7) 現場用コマンド
```bash
# 品質ゲート（READMEで明示）
dotnet restore ActivityPubServer.slnx --locked-mode
dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore
dotnet build ActivityPubServer.slnx --configuration Release --no-restore
dotnet test ActivityPubServer.slnx --configuration Release --no-build

# 連合検証再現
bash eng/pasture.sh up
# Interop matrix 確認時は artifacts 配下の該当ファイルを参照
```

## 8) 重要なファイルの入口
- `docs/IMPLEMENTATION_PLAN.md`
- `docs/CONFORMANCE.md`
- `docs/VERIFICATION.md`
- `docs/MASTODON_API.md`
- `docs/compatibility/CROSS_API_SEMANTICS.md`
- `docs/LOCAL_FEDERATION.md`
- `src/ActivityPub.Api/Program.cs`
- `src/ActivityPub.Federation/FederationServiceCollectionExtensions.cs`
- `src/ActivityPub.Federation/Inbound/InboundActivityReceiver.cs`
- `src/ActivityPub.Federation/Outbound/ClientOutboxService.cs`
- `src/ActivityPub.MastodonApi/MastodonEndpoints.cs`

## 9) OpenCode での進め方（提案）
- Chat からこのMDを最初に読み、当面は `VERIFICATION` の blocked 項目→`MASTODON_API` blocking endpoint→interop差分の順に短いサイクルで再開する。
- 各作業は「実装」「テスト」「記録（docs更新）」を同一コミットでセットにする。
