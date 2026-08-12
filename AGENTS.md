# AGENTS.md

## 適用範囲

この文書は`/root/new-project`以下の全作業へ適用する。

このリポジトリは、.NET 10、ASP.NET Core、PostgreSQLによるActivityPubサーバーと、Misskey 12.119.2のUIをRazorへ移植したサーバーサイドInteractive Blazorフロントエンドを含む。

最優先目標は、既存機能を整理し直すことではなく、未完了の移植を機能単位で完了させることである。

## 指示の優先順位

判断が競合した場合は、次の順序で新しい内容を優先する。

1. 現在のユーザー指示。
2. この`AGENTS.md`。
3. 機械可読なinventoryとport map。
4. 日付の新しい検証文書とADR。
5. `README.md`と古い引き継ぎ文書。

`OPENCODE_HANDOFF_AP_IMPLEMENTATION.md`と2026-08-04以前の文書には、現在より古い移植率、テスト数、OIDC前提が含まれる。

それらを現状の根拠として無条件に転記しないこと。

OpenCode作業後の認証方針は、外部OIDCとKeycloakを本番経路から除去し、Misskey v12型のローカルアカウント、セッションCookie、MiAuth、アプリトークンを使用する構成である。

この方針と矛盾するADRや文書を発見した場合は、実コードと自動試験を確認したうえで文書も同じ変更内で更新する。

## 作業開始時の確認

作業を始めるたびに、少なくとも次を確認する。

```bash
cd /root/new-project
git status --short
sed -n '1,220p' README.md
jq '{total:(.mappings|length), statuses:(.mappings|group_by(.migrationStatus)|map({status:.[0].migrationStatus,count:length}))}' \
  frontend/ActivityPub.Misskey.Blazor/upstream-port-map.json
```

必要な範囲の`docs/frontend-blazor/`、`docs/compatibility/`、ADR、テストも読む。

このworktreeは多数の未追跡ファイルを含む。

未追跡であることを、削除、上書き、再生成、初期化の許可と解釈してはならない。

ユーザーの既存変更と、自分の作業に無関係な差分を変更しない。

`.env`、token file、証明書、秘密鍵、Cookie、アクセストークン、パスワードを読み上げ、ログ出力、artifact保存、コミットしてはならない。

テスト用`alice`の資格情報が必要な場合も、値を表示せず、既存の安全な設定境界を使う。

## 移植元と正本

移植基準は二つに分離する。

- フロントエンドのデザイン・DOM・CSS・挙動：Misskey v12.119.2、commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`
- バックエンドの機能・API・連合・モデレーション・メディア・キュー挙動：`mei23/dolphin`

フロントエンドの見た目をバックエンド実装から推測したり、バックエンドの契約をMisskey v12の画面だけから推測したりしない。両者の差分はadapter、Application、Domainの境界で明示する。

参照元は次のとおりである。

- 固定upstream：`.cache/upstream/misskey-12.119.2/packages/client/src`
- 現行Vue oracle：`frontend/misskey-v12`
- backend baseline：`mei23/dolphin`
- 固定checkout：`.cache/meidolphin`
- Razor実装：`frontend/ActivityPub.Misskey.Blazor`
- port map：`frontend/ActivityPub.Misskey.Blazor/upstream-port-map.json`
- 生成inventory：`artifacts/frontend-inventory/`

UI、DOM、class、CSS、responsive layout、focus、keyboard、pointer、touch、drag and drop、overlay、scroll、transition、animationはMisskey v12を基準にする。

バックエンド機能の到達範囲は、`mei23/dolphin`の挙動を第一基準にし、既存ActivityPub実装との意図的な差分を記録する。

ユーザー確認済みの固定backend sourceは、origin `https://github.com/mei23/dolphin`、branch `mei-dolphin`、commit `3ce200269f814547dc7dfc6b246abadf8a9c00ed`である。

簡素な自作UI、似た画面、固定データ、空配列、固定件数、常時成功、無副作用の200または204、未実装画面への一律リダイレクトを移植として扱わない。

Vue SFCをiframe、microfrontend、JavaScript wrapperとして本番経路へ残してはならない。

Vueが担っていた状態機械とライフサイクルはRazorとC#へ移し、JavaScriptはブラウザーAPI用の型付きES module境界に限定する。

## 現在の優先順位

一つの垂直スライスを完成させてから次へ進む。

現在の認証sliceは次まで実装済みである。

1. Misskey v12の`POST /api/signin`を同一originへ実装し、JSONとmultipartを受理する。
2. `MkSignin.razor`の通常送信を`/api/signin`へ接続し、password、TOTP、lockout、suspended account、Misskey形式error、専用token、HttpOnly session Cookieを検証する。
3. `POST /api/signin`のlegacy WebAuthn payloadをprotected ASP.NET passkey stateとchallenge cookieへ接続する。Blazor browser credential経路は専用`/auth/passkey/*` adapterを使うが、Identity実装は共有する。
4. `miauth/gen-token`の`session: null`を内部UUIDへ隔離し、token hash、専用token、単回session責務を維持する。
5. `/settings/api`と`/settings/apps`を実token一覧、発行、失効へ接続する。

次の優先sliceは、確定済み残タスク文書に従う。調査時の設計メモだけを実装済みとして扱わない。

確定した残タスク、Dolphin backend契約待ち、blocked、scope exclusion、完了条件は`docs/frontend-blazor/REMAINING_TASKS.md`を正本とする。

Driveやchartなどバックエンド契約が存在しない機能は、`docs/frontend-blazor/BACKEND_SCOPE_EXCLUSIONS.md`の証拠規則を満たす場合だけ除外できる。

機能が後からバックエンドへ追加された場合は除外を再評価する。

除外対象をstubで表示可能にしてはならない。

既存コードの全面的な整理、命名統一、抽象化、性能改善、依存更新は、現在の移植に不可欠でない限り行わない。

30分以上、移植対象の実装差分が増えない場合は作業を止め、調査範囲、sliceの大きさ、検証方法を見直す。

## フロントエンドの不変条件

主要UIはASP.NET Coreのstatic SSRとInteractive Serverを使用する。

Blazor WebAssemblyへ変更してはならない。

アプリケーションの基準pathは`/app/`を維持する。

Hostヘッダーや現在のブラウザーURLからPublicBaseUriや認証設定を推測しない。

移植初期は、upstreamのDOM階層、class名、CSS custom property、duration、delay、easing、transform、z-index、overflowを可能な限り維持する。

見栄えを独自に再設計してはならない。

登録画面とログイン画面も例外ではない。

背景透明化は既知の致命的回帰である。

各UI sliceのChromium smokeでは、少なくとも`html`、`body`、表示shell、主要panelのcomputed backgroundまたはalphaが意図せず透明でないことを検査する。

modal wrapperなど意図的に透明な要素と、実際の表示面を区別する。

`IJSObjectReference`、`DotNetObjectReference`、observer、listener、timer、Blob URLはcomponent破棄時に解放する。

古いcallbackが新しいDOMへ作用しないよう、generation、取消、component lifetimeを扱う。

## アーキテクチャの不変条件

依存方向は次を維持する。

```text
Razor Component
  -> Presentation State
  -> Application Client Service
  -> Misskey HTTP/Streaming Contract
  -> ActivityPub.MisskeyApi
  -> Application
  -> Domain
  -> Persistence / Federation / Media
```

BlazorからMastodon adapterを呼び、Misskey機能を実現してはならない。

DomainへBlazor、ASP.NET Core、EF Core、Misskey DTO、Mastodon DTOの型を持ち込まない。

一つの利用者操作から、Domain変更、Activity、Delivery、Notification、reaction、mediaを重複生成しない。

API routeの存在だけを実装完了としない。

実データ、永続状態、認可、副作用、投影、エラー契約を確認する。

PostgreSQLを永続状態とdurable event、配送処理の信頼できる記録として維持する。

起動時の無条件migrationを追加せず、Expand、Migrate、Contractと専用migration commandを使う。

## セキュリティ

認証迂回、固定JWT、常時成功handler、共通token、無条件admin付与を禁止する。

MiAuth tokenとアプリtokenは平文をDBへ保存しない。

permissionを暗黙に拡張せず、失効、期限、単回消費、監査を維持する。

password、token、authorization code、Cookie、署名全体、DM本文をログやtelemetryへ含めない。

private、followers-only、specified情報は、REST、Streaming、media、cache、ETag、検索、ログの各経路でviewer認可を通す。

外部URL取得は共通のSSRF防御境界を使用する。

Pasture用localhost例外をProductionへ広げてはならない。

Tailnet公開はDevelopment試験用であり、内部ActivityPub IRIやProductionのSSRF設定を変更して実現してはならない。

現在の公開先は`https://exekey-net.tail319568.ts.net:9443/app/`だが、利用前に`docs/frontend-blazor/VERIFICATION.md`、Tailscale状態、実healthを再確認する。

## 実装と検証の進め方

一つのsliceでは、次を連続して扱う。

1. 固定upstreamのSFC、import、API、Streaming、CSS、motionを読む。
2. 対応する既存Razorとバックエンド契約を確認する。
3. 必須の製品差分だけを実装する。
4. focused unitまたはcomponent testを追加する。
5. Chromium 1種類のfocused smokeを実行する。
6. port mapと検証文書を、証拠が揃った状態へだけ更新する。

移植途中のbrowser試験は原則Chromium 1種類、1つのfocused smokeに限定する。

Firefox、WebKit、複数viewport、全visual suiteは、対象機能群の移植完了後に一度だけ実行する。

同じ確認を毎sliceで全browserへ反復しない。

Playwrightは共有portの競合を避けて直列化する。

```bash
cd /root/new-project/tests/frontend-blazor-e2e
flock /tmp/activitypub-playwright-5099.lock bash -lc \
  'PATH=/root/.dotnet:$PATH npx playwright test <spec> --project=chromium --no-deps'
```

実行前にport `5099`を別processが使用していないか確認する。

inventory検査は次を使用する。

```bash
cd /root/new-project/frontend/misskey-v12
npm run inventory
npm run inventory:check
npx vitest run src/frontend-inventory.test.ts
```

.NET SDKは必要に応じて次で固定する。

```bash
export PATH="/root/.dotnet:$PATH"
```

通常のsliceでは、変更projectのRelease buildとfocused testを先に実行する。

安定checkpointと最終確認では次を実行する。

```bash
dotnet restore ActivityPubServer.slnx --locked-mode
dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore
dotnet build ActivityPubServer.slnx --configuration Release --no-restore
dotnet test ActivityPubServer.slnx --configuration Release --no-build
```

警告を無条件に抑制しない。

テスト失敗を既存不具合として決めつけず、変更前の基準、共有差分、製品不具合、環境不具合を分ける。

## Port mapと完了判定

移植状況の正本は`upstream-port-map.json`であり、Markdownに記載された古い件数ではない。

2026-08-12 UTCの現行生成mappingは535件のうち、`implemented` 329件、`in-progress` 0件、`blocked` 0件、`planned` 0件、`excluded` 206件、`unclassified` 0件である。excludedは専用backend feature 34件と、Dolphinの未提供または不完全な契約を明示する `remaining-dolphin-contract-gaps` 172件に分かれる。ここでの`implemented`は該当sourceに対する証拠付き分類であり、フロントエンド全体の完成を意味しない。

この数は作業開始時に必ず再生成または再集計し、固定値として扱わない。

同日時点の`npm run inventory:check`は成功している。失敗した場合は、現行port mapと実在する試験の不一致を調査し、証拠fileを捏造せずにmappingまたは試験を修正してからinventoryを再生成する。

古い`MkSignin`記録にあるOIDC／Keycloak前提は現行の正本ではない。現行mappingは、ローカルIdentity、`/api/signin` JSON／multipart、TOTP／lockout、protected legacy WebAuthn challenge、MiAuth、専用token、HttpOnly sessionの実装とテストを根拠にする。外部provider live統合と実browser authenticator enrollmentは未検証のまま`in-progress`として扱う。

`implemented`へ変更するには、少なくとも次が必要である。

- 実Razor targetが存在する。
- upstreamのDOM、class、CSS、状態分岐を確認した。
- 実APIまたは明示された純Presentation入力へ接続した。
- 固定値fallbackや隠れたstubがない。
- focused自動試験が成功した。
- Chromiumで操作、console、page error、未分類HTTP error、背景透明化を確認した。
- 永続操作ではDB状態とActivityPub副作用を確認した。

未確認項目があれば`in-progress`を維持する。外部backend契約が未提供で実装できない項目は、`planned`や`blocked`を消すために成功扱いへ変更せず、sourceごとのAPI／Streaming evidence付きscope exclusionへ移す。

backend欠如など独立した外部条件がある場合だけ、具体的な証拠と理由を付けて`blocked`またはscope exclusionにする。

「表示できた」「routeがある」「buildが通った」だけで完全移植や互換性を宣言しない。

## 文書と引き継ぎ

実装と同じsliceで、必要なport map、互換性表、検証文書を更新する。

過去の試験件数を現在の成功として流用しない。

最終報告または中断時の引き継ぎには、次を具体的に残す。

- 実装したupstream sourceとRazor target。
- 変更したバックエンド契約。
- 実行したコマンドと成功数、失敗数。
- Chromium smokeの対象と結果。
- DB状態またはActivityPub副作用の証拠。
- 未実装、blocked、scope exclusionと理由。
- 次に開くべきファイルと、最小の次作業。

「本番対応」「完全移植」「Mastodon互換」「Misskey互換」という表現は、対応matrixの必要項目が自動試験、実クライアント試験、差分試験、障害試験で裏付けられるまで使用しない。
