# Misskey v12 Blazor移植の残タスク（確定版）

## 基準

- frontend oracle: Misskey v12.119.2、commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`
- backend oracle: `mei23/dolphin`、`.cache/meidolphin`、commit `3ce200269f814547dc7dfc6b246abadf8a9c00ed`
- production UI: ASP.NET Core static SSR + Interactive Server Blazor
- 対象範囲の正本: `artifacts/frontend-inventory/vue-to-blazor-mapping.json`、`artifacts/api-inventory/misskey-12.119.2.json`

Misskey v12のDOM、CSS、画面挙動と、Dolphin基準のAPI・永続副作用・認可・連合を別々に検証する。画面だけを先に作らない。

## 現在地（2026-08-13 UTC）

| 区分 | source数 | 扱い |
|---|---:|---|
| implemented | 330 | 実在するRazor target、Dolphin契約、永続副作用、認可、回帰証拠を持つsource |
| in-progress | 0 | 初期12件、Dolphin契約確認、現行契約で可能なsupported vertical sliceを完了 |
| planned | 0 | 現在のworktreeで未分類の実装待ちはない |
| blocked | 0 | 旧blocked 2件は明示的capability exclusionへ移し、危険なstubを追加していない |
| excluded | 205 | Dolphinに完全契約がないsource。専用34件と残りの契約gapを明示的に記録 |
| unclassified | 0 | 新規source追加時以外は発生させない |

生成mappingのsource分類を正本とする。API、Streaming、storage、DOMのみのsourceが重複していても、各sourceは一つのstatusだけを持つ。

### 今回の消化結果

- 初期のin-progress 12 source：client bootstrap、directives、page-block、Nirax、note presentationのfocused sliceを実装し、Release／bUnit／Chromium evidenceを追加した。
- planned source：Dolphinの完全契約が確認できるものは実装へ昇格し、残りは171件の `remaining-dolphin-contract-gaps` または既存34件の専用scope exclusionへ移した。plannedは0である。
- blocked 2 source（AiChan／button）：CSPやAiScript sandboxを緩和せず、根拠付きscope exclusionへ移した。blockedは0である。
- excluded：205 sourceすべてにAPI／Streaming evidence、Dolphin status、理由を生成し、placeholderや常時成功レスポンスは追加していない。

`pages/welcome.setup.vue` は `Components/WelcomeSetup.razor` として、`meta.requireSetup`、初期管理者作成、管理者role、local actorと署名鍵、専用token、HttpOnly session Cookieへ接続した。通常のWelcome Entranceでは代用せず、同時実行時はPostgreSQLのidentity table lockで一件だけを確定し、完了後の再実行を拒否する。固定上流のDOM/CSSと不透明背景、初回送信listenerのSSR/hydration境界、作成後の認証済みtimeline遷移をChromiumで確認した。

`pages/settings/theme.vue` は `Pages/SettingsTheme.razor` として、v12のテーマ切替DOM・アニメーション、20件の埋込みcatalog、`pizzax::base`のdarkMode、`miux:*Theme`の旧オブジェクト形状、検証済みThemeInteropをChromiumで確認した。Dolphinにないregistry・インストール・editor・wallpaper操作は capability=false と理由を表示し、成功を偽装しない。

`pages/about.federation.vue` は `Components/AboutFederation.razor` として、Dolphin の `federation/instances` 永続投影・offset pagination・host/state/sort filter を実データで検証済みのため implemented へ昇格した。チャートを必要とする instance detail は契約外のまま表示しない。

`pages/note.vue` は `Pages/NotePage.razor` として、`notes/show` のviewer境界、`MkNoteDetailed` の実データ投影、未知noteのエラー／再試行を検証済みのため implemented へ昇格した。Dolphinにないclips・replies/conversation paginationは表示しない。MkErrorの遅延rootとfade attachが競合して回路を落とす不具合は、対象エラーだけtransition fallbackへ切り替えて解消した。

`pages/user/home.vue` と `pages/user/index.timeline.vue` は `Pages/UserPage.razor` として、`users/show`／`users/notes` のviewer境界、v12 profile hierarchy、実note list、follow表示を検証済みのため implemented へ昇格した。Dolphinにないpinned notes、profile fields、clips/pages/gallery/activityは capability=false または非表示であり、固定データを返さない。`pages/user/index.vue` の未対応tabsは `remaining-dolphin-contract-gaps` へ明示的に移し、plannedのまま残していない。

## 確定した作業順

### 1. Inventoryと既存実装の同期

1. `upstream-port-map.json`へ、実装済みだがplannedのまま残る認証・設定sourceを、実在するRazor targetとfocused evidence付きで登録する。
2. `npm run inventory:check`とmapping completeness testを通し、status、target、test、backend contractを一致させる。
3. endpoint inventoryの`implemented`判定はroute存在ではなく、Dolphin契約、DB副作用、認可、error、API testを根拠にする。

完了条件: source 535件に未分類がなく、implemented mappingの全件に実在targetと自動証拠がある。

### 2. 完了した初期12 source

次の12 sourceは、upstream DOM/class/SCSS、実データ、認可、focus、Escape、pointer、motion、dispose、Chromium smoke を完了し、inventory上 `implemented` になった。

- `MkContextMenu.vue`
- `MkEmojiPickerWindow.vue`
- `MkPageWindow.vue`
- `MkSuperMenu.vue`
- `MkTagCloud.vue`
- `MkTokenGenerateWindow.vue`
- `pages/admin/index.vue`
- `pages/settings/index.vue`
- `pages/settings/profile.vue`
- `ui/universal.vue`
- `widgets/memo.vue`
- `widgets/unix-clock.vue`

証拠は各 port map の `automatedTests` と、`tests/frontend-blazor-e2e/` の focused Chromium specs に記録する。Firefox/WebKitと全viewportは最終段階で一度だけ実行する。

### 3. Dolphin backend契約の先行確定（完了）

mei23/dolphin 固定 checkout の endpoint metadata と現行 C# adapter route を機械照合した。結果は docs/frontend-blazor/DOLPHIN_CONTRACT.md と artifacts/backend-contract/dolphin-misskey-12.json が正本である。

- 145 endpoint source を解析
- 61 local route を解析
- Dolphin endpoint と一致する route は40
- 未対応 adapter route は103
- supported screen contract は i、i/update、i/apps、i/revoke-token、timeline、note、reaction、notification、admin announcement/invite/relay、federation/instances、users/show、users/notes、users/search
- Drive、Charts、Antennas、Channels、Clips、Gallery、i/2fa/* は backend contract 不足として excluded/blocked のまま
- node eng/generate-dolphin-contract.mjs --check をCIで実行する

一致するrouteがあるだけで実装済みとはせず、既存の API compatibility、DB副作用、認可、stream 回帰テストを根拠にする。今回の確認ではこのsupported contract群について、Release全テスト（再実行結果を下記VERIFICATIONへ記録）、inventory check、Dolphin contract check、Chromium parity 66件、設定device parity 2件、settings theme/general/privacy と followers/following のfresh-publish smoke各1件、12回×10 routeのsupported soakを再実行した。

### 3.1 supported画面とruntimeの現在地

`pages/about.vue`／`about.federation.vue`、`pages/follow.vue`、`pages/note.vue`、`pages/user/home.vue`／`index.timeline.vue`、`pages/explore.vue`／`explore.users.vue`、通知、設定API／apps／profile／navbar／deck／custom-css／reaction、admin announcement／relay、および共通visibility runtimeは実データ境界へ接続済みである。`pages/note.vue`のunknown-note遷移競合と、`MkVisibility`のdispose競合は修正し、Chromiumで回帰確認した。

`settings/notifications` は、通知種別・未読ノート・メッセージの未提供契約を capability=false として明示し、提供済みの `notifications/mark-all-as-read` だけをdurable notification aggregateへ接続した。`pages/user-info.vue` は `users/show` のviewer-aware UserPreview、安定Misskey ID、件数、follow操作を表示し、Dolphinにない管理・IP・raw・chart・Drive操作は成功を偽装しない。

`filters/bytes.ts`、`filters/number.ts`、`filters/note.ts`、`filters/user.ts` は、純粋な表示・IRI生成のC# helperへ移植し、null/単位/文化依存数値/ID検証/IDN hostのfixtureを通した。これらは認証やデータ取得を行わず、既存の明示的PublicBaseUri境界を維持する。

`const.ts` のbrowser-safe media type契約も `MisskeyFileTypes` へ移植した。SVG、HTML、JavaScriptはブラウザー表示対象から除外し、既存のmedia security boundaryを緩和していない。

`components/global/MkA.vue` は `Components/global/MkA.razor` として、明示的なhref、active-class、通常／browser／windowのnavigation、context menu、link copyを既存のoverlay・clipboard境界へ接続した。未提供のpage-window hostを暗黙に成功させず、`modalWindow` は同一origin navigationへ明示的に劣化する。

`pages/mfm-cheat-sheet.vue` は `Pages/MfmCheatSheetPage.razor` として、v12の29個のMFM feature、説明、editable preview、sticky section DOMを安全な `MfmView` と型付き `MkFormTextarea` へ接続した。旧 `V12RoutePage` の未実装表示は通常経路から除去した。

`pages/settings/sounds.vue` は `Pages/SettingsSounds.razor` として、master volume、7種類のsound row、enum/range edit dialog、reset、`pizzax::base`相当の`sound_*` device keysを実装した。audio previewは音源と安全な再生境界が未提供のためcapability gapとして記録し、常時成功のボタンは追加していない。

`scripts/array.ts`、`scripts/format-time-string.ts`、`scripts/get-user-name.ts`、`scripts/get-static-image-url.ts`、`scripts/safe-uri-decode.ts`、`scripts/get-note-summary.ts`、`scripts/check-word-mute.ts`、`scripts/shuffle.ts`、`scripts/keycode.ts`、`scripts/time.ts`、`scripts/url.ts`、`scripts/login-id.ts`、`scripts/twemoji-base.ts` は `Client/MisskeyScriptUtilities.cs` の型付き純粋関数へ移植し、v12の配列順序、日付token、表示名fallback、明示instance URIによるproxy生成、malformed URI保持、note summary、word mute、key alias、UTC時刻、query、loginId、Twemoji codepoint生成をfixtureで固定した。`scripts/contains.ts` はDOM実装を捏造せずplannedのまま保持する。

`scripts/get-note-summary.ts` と `scripts/check-word-mute.ts` も同じutility boundaryへ移植した。summaryはCW/text、file、poll、reply、renote、deleted/hidden labelsを保持し、word muteは自分の投稿免除、keyword-all、正規表現、無効pattern拒否を保持する。

`components/MkNotificationSettingWindow.vue` は `Components/MkNotificationSettingWindow.razor` として、v12のmodal dimensions、global toggle、12 notification type order、bulk enable/disable、nullable `includingTypes` resultを既存のtyped modal lifecycleへ接続した。通知種別の永続化はauthenticated notification settings boundaryへ委譲し、未提供のサーバー契約を常時成功で補っていない。

`components/MkPagePreview.vue` は `Components/MkPagePreview.razor` として、v12のvhpxefrj block、thumbnail、85文字summary、author footer、page hrefを実projectionから描画する。thumbnail schemeはHTTP(S)かつuserinfoなしに限定する。`scripts/extract-mentions.ts`、`extract-url-from-mfm.ts`、`mfm-tags.ts`、`timezones.ts` は `Client/MisskeyMfmUtilities.cs` の安全なMFM AST投影と固定catalogへ移植した。`is-device-darkmode.ts` と `sound.ts` は既存のtyped browser/device settings boundaryで検証済みである。

`components/MkUserSelectDialog.vue` は `Components/MkUserSelectDialog.razor` として `users/search-by-username-and-host` 相当の既存presentation service、recent users、選択、double-click、device-scoped `recentlyUsedUsers` persistenceへ接続した。`components/MkSample.vue` と `pages/preview.vue` は v12のsample cardsと`/preview` routeを移植し、未提供Drive pickerは capability errorを表示する。

`scripts/collect-page-vars.ts` は `Client/MisskeyPageVariableUtilities.cs` へ移植し、nested page blocksを文字列・数値・booleanへ変換する。`scripts/emojilist.ts` は既存の埋込み1,782件Unicode emoji catalogと9カテゴリ順を正本として再利用する。

これをMisskey v12全体の移植完了とは扱わない。planned/blockedは0だが、excluded 205件はDolphinの未提供または不完全な契約により、画面stubを作らずcapability unavailableとして明示している。

planned sourceを次の契約群へ分解し、Dolphinの挙動とC#実装をdifferential fixtureで固定する。

- account/profile/security: `users/show`、`users/stats`、`i/update`、`i/2fa/*`、`i/change-password`、`i/signin-history`
- social graph: follow request、followers/following、mute/block list、user list
- notes/timelines: `notes/show/create/delete/state/search/mentions/featured`、home/local/global/user-list timeline
- notifications: list、read、mark-all、messaging unread state
- moderation/admin: abuse report、suspend/silence、domain policy、emoji、queue、relay、audit
- media boundary: 既存の共通media upload/proxy/cacheだけ。Drive管理は除外のまま
- streaming: `main`、home/local/global/hybrid、notification、reaction、note update/delete、Note Capture。cursor、reconnect、visibility、Mute/Blockを含む

contractがDolphin基準と一致しない機能は、Blazor側で仮データを返さず、先にApplication/Domain/Persistence/APIを修正する。契約済みの pages/follow.vue は users/show と following/create の共有 presentation command を使う垂直スライスとして完了した。

### 4. backend契約が確認できた画面・componentの移植

次の順で、route単位ではなく共通componentと実データを垂直に完成させる。

1. universal shell、classic、deck、zen、visitorの残りとrouter/history/scroll restoration
2. account/profile、follow request、followers/following、mute/block、user page
3. note detail、search、tag、explore、share、preview、reply/quote/renote
4. notification、conversation、timeline、streaming channel表示と再接続
5. settings（profile、security、privacy、notifications、general、theme、navbar、sounds、2FA）
6. admin/moderation（reports、users、domain、emoji、queue）
7. page/AiScript関連はruntimeとbackend capabilityがそろった範囲だけ

対象sourceの完全な一覧はinventoryのplanned recordsを使用する。新しいRazorを作るだけでstatusをimplementedへ変更しない。

### 5. 共通runtimeの残作業

- `router.ts`/nirax相当の順序付きroute registry、guard、query/hash、deep link、history、modal navigation
- `store.ts`/Pizzax相当のaccount、settings、theme、locale、Deck、draft、overlay、streaming state
- localStorage、sessionStorage、IndexedDB、BroadcastChannelのschema migrationとcross-tab同期
- 全20 theme、locale、font、icon、background、CSS variable、responsive shell
- Transition/TransitionGroup、FLIP、cancel、nested duration、reduced motion、generation ID
- streaming subscription、cursor resume、bounded queue、slow consumer、token expiry、account switch
- PWA、Service Worker、offline shell、update versioning、push notification
- MFM AST安全render、custom emoji、KaTeX、malformed input。AiScriptはsandboxとcapability制限を満たすまでblocked

### 6. 検証と昇格

各vertical sliceで次を通す。

- focused bUnit／API／PostgreSQL test
- Chromium 1種類のsmoke（DOM、computed background alpha、console、pageerror、未分類HTTP error、focus、motion）
- DB state、Domain state、Activity/Delivery/Notification/stream eventの照合
- token、Cookie、DM、本文をartifact/log/telemetryへ出さないこと

全in-scope sourceがimplementedになった後、一度だけFirefox/WebKit、全viewport、visual/accessibility、load/soak、rolling deploymentを実行する。

## 旧blockedからcapability exclusionへ移したsource

- `widgets/aichan.vue`: 外部iframeを許可する安全な同一origin配信境界がない。CSPを緩和せず `remaining-dolphin-contract-gaps` へ移した。
- `widgets/button.vue`: 任意AiScript interpreterとAPI sandboxがない。常時成功や固定dialogで代替せず `remaining-dolphin-contract-gaps` へ移した。

## 現時点で移植しないexcluded（206 source）

backend契約が追加されるまで、次は移植タスクに含めない。

- Drive management: 12
- API-backed charts: 3
- Gallery: 4
- Antennas: 4
- Clips: 2
- Channels: 5
- Registry: 3
- Favourites: 1

共通media表示・既存note/profileで使うupload primitiveはDrive管理除外に含めない。

上記34 sourceに加えて、`remaining-dolphin-contract-gaps` の172 sourceを明示的に除外している。
各sourceのAPI／Streaming証拠、Dolphin inventoryのstatus、既存adapterの実装有無は
`artifacts/frontend-inventory/vue-to-blazor-mapping.json` に保存する。未提供または不完全な契約を
空配列・固定値・常時成功レスポンスで補わない。

## 完了判定

次の全条件を満たすまで、Misskey v12移植完了とは判定しない。

1. planned/in-progressが0、unclassifiedが0である。
2. blockedは0であり、旧2件は根拠付きscope exclusionへ移され、無害なfallbackを残していない。
3. backendのclaimed endpointがDolphin fixture、DB副作用、認可、error、回帰testを持つ。
4. 全supported route、API、Streaming、storage、motion、theme、localeがmapping済みである。
5. REST、Streaming、media、cache、ログ、telemetryからprivate情報が漏れない。
6. OAuth/MiAuth/session/tokenの責務分離、Activity/Delivery/Notificationの重複防止が通る。
7. Release build、全.NET test、inventory、frontend test、Chromium smokeが成功する。
8. 最終matrixでbrowser E2E、visual parity、accessibility、soak、rollingを証拠付きで記録する。
