# Blazor frontend verification

## 判定範囲

frontendのvisual／behavior oracleはMisskey v12.119.2である。画面の機能到達範囲、APIの永続副作用、認可、ActivityPub連合、モデレーション、メディア、キューのbackend基準は`mei23/dolphin`であり、Misskey v12の画面実装から逆算しない。

この文書は、2026-08-04 UTCの基準試験、2026-08-12 UTCに追加したsupported範囲の再検証、および2026-08-13 UTCのstandalone WebAssembly移行検証を記録する。

現在の成果物はMisskey 12.119.2フロントエンドの完全移植ではない。

Inventoryは535 sourceを分類し、現在の生成mappingは`implemented` 330、`in-progress` 0、`blocked` 0、`planned` 0、`excluded` 205、`unclassified` 0である。excludedの内訳は、専用backend feature 34件と、Dolphinの未提供または不完全な契約をまとめた `remaining-dolphin-contract-gaps` 171件である。

したがって、以下の結果は検証済みの垂直スライスだけを裏付ける。

## 2026-08-13 UTC standalone WebAssembly移行検証

- `ActivityPub.Misskey.Blazor`をbrowser-safe Razor class libraryとし、server実装を`ActivityPub.Misskey.Blazor.Server`、実行入口を`ActivityPub.Misskey.Blazor.Client`へ分離した。ClientはApplication、Domain、Persistence、Identity、MisskeyApi、EF Core、Npgsqlを参照しない。
- ASP.NET Coreの本番経路は`/app/`へstandalone Clientを配信し、`blazor.webassembly.js`を起動する。`blazor.web.js`、`/_blazor`、Vue、Viteは通常経路へ含めない。`/app/_content`は静的web assetの`/_content`へ安全に解決し、deep linkはAPI、Streaming、Media、ActivityPub endpointを横取りしない。
- browser認証はHttpOnly session Cookieを正本とし、`/api/frontend/session`が返すantiforgery tokenをWASM memoryだけに保持する。変更要求はsame-origin、frontend marker、CSRF headerを要求し、tokenをWeb Storage、IndexedDB、URLへ保存しない。
- Timeline、通知、relationshipは単一WebSocketを多重化する。PostgreSQL durable cursor、checkpoint後だけのcursor確定、bounded queue、jitter付き再接続、slow consumer／cursor期限切れからの再同期を実装した。
- durable eventのprojection中にCookie security stampが失効する競合を再現した。projection後かつ送信直前にも認証を再検証し、失効後のpayloadを送らず`AUTHENTICATION_EXPIRED`とclose code 4401で終了する。決定的raceと実Cookie失効を含むStreaming統合試験16/16が成功した。
- `dotnet restore --locked-mode`、`dotnet format --verify-no-changes`、Release buildは成功し、buildは警告0／エラー0だった。
- Release全.NET testは973/973成功した。内訳はDomain 60、Federation 61、Media 41、Moderation 2、Property 3、Persistence 92、Misskey Blazor 540、API 174である。
- standalone WASM Chromium smokeは1/1成功した。実WASM boot、Cookie session、memory-only CSRF、cursor bootstrap、実Client WebSocket checkpoint、非透明なshell、初期化失敗時の安全な表示を確認し、console error、page error、404、CSP violationは0件だった。
- frontend inventoryは535 source、400 Vue SFC、115 route、262 static API endpoint、14 Streaming channelで差分なしだった。WASM境界監査、NuGet／frontend license、第三者通知検査は成功した。
- NuGetのdirect／transitive vulnerability検査とnpm high severity auditはいずれも既知脆弱性0件だった。

このcheckpointはstandalone WASM実行境界の完成を裏付けるが、Dolphin契約がない205 sourceの機能や、未実施の全ブラウザーvisual／長時間soak／rolling deploymentまで完了したという主張ではない。

## 2026-08-12 UTC 現行再検証

- `node eng/generate-dolphin-contract.mjs --check`：成功（145 Dolphin endpoint、61 local route、103 missing adapter）。
- `npm --prefix frontend/misskey-v12 run inventory:check --silent`：成功（535 source、115 route、262 static API、14 streaming channel、0 unclassified）。
- `npx --prefix frontend/misskey-v12 vitest run src/frontend-inventory.test.ts --run`：11/11 成功。planned、blocked、in-progressを0として検証し、206 exclusionのAPI／Streaming evidenceを検査した。
- 正規の `npm --prefix frontend/misskey-v12 test -- --run`：2 test files、18/18 成功。`nanoid` は `^3.3.17` overrideで固定し、`npm audit --audit-level=high --omit=optional` は脆弱性0件。
- `dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore`：成功。
- `dotnet build ActivityPubServer.slnx --configuration Release --no-restore`：成功、警告0／エラー0。
- `dotnet test ActivityPubServer.slnx --configuration Release --no-build`：901/901 成功（Domain 60、Media 33、Federation 61、Moderation 2、Property 3、Persistence 74、Misskey Blazor 536、API 132）。
- Chromium smoke（fresh publish）：`signin-parity.spec.ts` 4/4、`background-opacity.spec.ts` 56/56（20 theme、5 viewport、overlay、transparent-theme rejectionを含む）、`supported-soak.spec.ts` 1/1 成功。console/pageerror、未分類HTTP失敗、透明なhtml/body/shell surfaceは0件。
- 追加のsupported fresh-publish smoke：`settings-theme-parity.spec.ts`、`settings-general-parity.spec.ts`、`settings-privacy-parity.spec.ts`、`user-follow-relations-parity.spec.ts` は各1/1成功。テーマのdarkMode・旧storage形状、一般設定のdevice/localStorage、privacyのlock/discoverable永続化、followers/followingのDolphin relation projectionを実データで確認した。
- 現行TestHost publishで検出された `/preview` の二重Blazor route（`PreviewPage` と旧V12 wrapper）は、旧wrapperのroute属性を除去して解消した。再publish後のsignin smokeとbackground全56件でroute-table例外0件を確認した。

この再検証は、excluded sourceを実装済みと宣言するものではない。Dolphin契約が追加されるまでは、excluded画面を固定値や空配列で起動可能に見せず、capability unavailableとして扱う。
機械可読な記録は `artifacts/frontend-verification/20260812-current.json` に保存する。

## 現行の実行方式

本番frontendはstandalone Blazor WebAssemblyであり、ASP.NET Coreが同一originの`/app/`へ静的shellと`_framework`成果物を配信する。

`/app/`のHTMLは`blazor.webassembly.js`を読み込む。`blazor.web.js`、`blazor.server.js`、`/_blazor`、Vue runtime、Vite clientは本番経路へ含めない。

認証済みブラウザーはHttpOnly session Cookieを使用する。`GET /api/frontend/session`がviewerとCSRF contractを返し、request tokenはWASM memoryだけに保持する。変更要求は同一origin、`X-ActivityPub-Frontend: 1`、antiforgery headerを要求する。

Timeline、通知、relationshipは一つのbrowser WebSocketを多重化し、`POST /api/streaming/cursor`から開始する。cursorはcheckpoint受信時だけ確定し、切断後はPostgreSQLのdurable event logから再開する。

Tailnet公開URLは`https://exekey-net.tail319568.ts.net:9443/app/`である。

## 検証済みのUIスライス

次の表示と操作は、上流SFCから生成したCSS、実DB状態、Razor componentを組み合わせて検証した。

- Visitorのwelcome shell、背景、メニュー。
- `MkFeaturedPhotos`の実instance metadata背景と、`MkMarquee`の上流DOM、反復、速度算出、hover停止。
- `/about-misskey`の上流DOM、32個の物理emoji、version、credit、translation、donation、patron表示。
- `MkStickyContainer`、`MkPageHeader`、`FormLink`、`FormSection`、`MkLink`の現在の到達範囲。
- 固定したMatter.js 0.18.0による物理演出の開始、transform更新、route離脱時の完全な破棄。
- `I $[jelly ❤] #Misskey`を初期値にした実`MkPostForm`と、単一の永続create副作用。
- PostgreSQLのremote actor、object、follow、delivery、domain policy、circuit stateから投影する`federation/instances` welcome query。
- Universal shell、navbar、widgets、timeline header。
- 認証済みUniversalの最上段は固定版`ui/universal.vue`どおりstatus bar専用slotだけとし、インスタンス名／ドメインのidentity barを挿入しない。Chromiumで未設定status barが空かつ高さ0、後続のタイムライン`MkPageHeader`が残ることを確認する。Visitor shellはこの検証対象から分離する。
- Home、Local、Hybrid、Global timelineの切替。
- `MkNote`相当の本文、CW、media、poll、renote、visibility。
- `MkVisibility.vue`の二つのspan、home、followers、specified、localOnlyのiconとCSS Modules class、および`MkUsersTooltip.vue`の最大10ユーザー、avatar、name、超過件数。
- specified tooltipの300ms hover/touch、focus、Enter、Space、Escape、leave取消、interactive hydration前からpointerが乗っていた場合の回復、および全listener、timer、requestAnimationFrame、overlayの破棄。
- 2026-08-12 UTCのsupported soakで検出した `MkVisibility` のattach／dispose競合は、componentごとのlifetime cancellationとattachment gateで修正した。破棄中にdisposed `DotNetObjectReference` をJSへ渡さないことを、Visibility focused testとChromium soakで再確認した。
- `pages/note.vue` の supported vertical slice は `notes/show` 境界と `MkNoteDetailed` で実測した。未知note遷移では、遅延してrootを生成する `MkError` とfade attachの競合を検出し、`MISSKEY_NOTE_PAGE_TRANSITION_TARGET_MISSING` だけをサーバー側の即時state適用へフォールバックする修正を入れた。unknown noteのエラー画面、console/page error 0、回路診断0を `note-page-parity.spec.ts` で確認した。
- `pages/user/home.vue` と `pages/user/index.timeline.vue` の supported slice は、v12の `ftskorzw` profile/banner/avatar/status hierarchy、`users/show`境界、`users/notes`実note list、follow表示を `user-page-parity.spec.ts` で確認した。Dolphinにないclips/pages/gallery/activityやpinned/profile fieldsは capability=false／非表示として、仮データを表示しない。
- `pages/explore.vue` と `pages/explore.users.vue` の supported slice は、`users/search` の query／local／remote origin filter、viewer-aware UserPreview、空queryの非表示、featured/users の capability=false を `explore-page-parity.spec.ts` と `RelationshipCompatibilityTests.MisskeyUsersSearchUsesTheDolphinPrefixContractAndViewerSafeUserProjection` で確認した。Dolphinにないfeatured／ranked usersは捏造していない。
- `pages/settings/reaction.vue` は、v12既定リアクションの並べ替え、`pizzax::base`のpicker設定永続化、共有EmojiPicker境界、背景の非透明性を `settings-reaction-parity.spec.ts` で確認した。Dolphin APIを必要としない端末設定のみを実装し、サーバー機能を固定値で代替していない。
- `pages/settings/navbar.vue`、`settings/deck.vue`、`settings/custom-css.vue` は `settings-device-parity.spec.ts` でChromium 2件を確認した。`pizzax::base` の menu／menuDisplay／navWindow／alwaysShowMainColumn／columnAlign、`customCss` raw string、CSS textContent適用、危険なCSS拒否を実測した。
- `pages/settings/theme.vue` は `settings-theme-parity.spec.ts` でChromium smokeを実施する。20件のupstream catalogからlight/dark optionを生成し、darkMode、旧v12の`miux:lightTheme`/`miux:darkTheme` object shape、validated CSS variables、opaque panel、registry/install/editor/wallpaper capability=falseを確認する。
- `pages/settings/general.vue` と `pages/settings/privacy.vue` は `settings-general-parity.spec.ts` と `settings-privacy-parity.spec.ts` で、v12の端末キー永続化、フォント境界、Dolphinのlock/discoverable更新、未提供privacy capabilityの明示、opaque surfaceを確認する。

- `pages/settings/notifications.vue` は `settings-notifications-parity.spec.ts` で、mark-all-notificationsのdurable command、未提供bulk actionのcapability=false、opaque surfaceを確認する。`pages/user-info.vue` は `user-info-page-parity.spec.ts` でusers/show-backed UserPreview、安定ID、件数、follow projection、管理/IP/chart/Drive capability gapを確認する。

- `filters/bytes.ts`、`filters/number.ts`、`filters/note.ts`、`filters/user.ts` は `MisskeyFilterTests` で、v12のnull/単位/文化依存数値、note route検証、acct/name/IDN hostを確認する。

- `const.ts` は `MisskeyFilterTests` でbrowser-safe media type集合とSVG/HTML/JavaScript拒否を確認する。
- `components/global/MkA.vue` は `MkATests` でhref、active-class、child content、navigation境界、context-menu用link copyを確認する。`modalWindow` のpage-window hostは未提供のため同一origin navigationへ明示的に劣化する。
- `pages/mfm-cheat-sheet.vue` は `MfmCheatSheetTests` でv12の29 feature順序、section DOM、全preview textarea、MfmView接続を確認する。
- `pages/settings/sounds.vue` は `SettingsSoundsTests` でmaster volume、7 rows、reset、sound_* device key永続化を確認する。音声previewは音源/service boundary未提供のため有効化していない。
- `scripts/array.ts`、`format-time-string.ts`、`get-user-name.ts`、`get-static-image-url.ts`、`safe-uri-decode.ts`、`get-note-summary.ts`、`check-word-mute.ts`、`shuffle.ts`、`keycode.ts`、`time.ts`、`url.ts`、`login-id.ts`、`twemoji-base.ts` は `MisskeyScriptUtilitiesTests` で、純粋関数のv12出力とURL安全境界を確認する。DOM `contains.ts` はブラウザーinterop実装が必要なためplannedである。
- `components/MkNotificationSettingWindow.vue` は通知種別の順序、global toggle、bulk操作、nullable resultを `MkNotificationSettingWindow.razor` と `MkNotificationSettingWindowTests` で確認する。
- `components/MkPagePreview.vue` は `MkPagePreview.razor` でv12のblock/thumbnail/summary/footer構造を再現し、安全でないthumbnail schemeを拒否する。
- `scripts/extract-mentions.ts`、`extract-url-from-mfm.ts`、`mfm-tags.ts`、`timezones.ts` は安全なMFM AST投影と固定catalogとして `MisskeyMfmUtilitiesTests` で検証する。`is-device-darkmode.ts` と `sound.ts` は既存のtyped theme/device settings boundaryへ接続済みである。
- `components/MkUserSelectDialog.vue` は検索・recentユーザー・選択・recentlyUsedUsers persistenceを `MkUserSelectDialogTests` で確認し、`components/MkSample.vue` と `pages/preview.vue` は実overlayと明示的Drive capability errorを `MkSampleTests` で確認する。
- `scripts/collect-page-vars.ts` は `MisskeyPageVariableUtilitiesTests` でnested page block variableの種類・既定値・順序を確認し、`scripts/emojilist.ts` は埋込み1,782件catalogを `EmojiPickerTests` で検証する。
- `scripts/get-note-summary.ts` と `scripts/check-word-mute.ts` は `MisskeyScriptUtilitiesTests` でsummary再帰、CW/file/poll、reply/renote、viewer exemption、keyword/regex muteを確認する。
- mentioned-only noteのApplication viewer認可、`visibleUserIds`と`localOnly`のMisskey API投影、および匿名viewerと`bto`、`bcc` recipientへの非開示。
- `MkPostForm`相当のcompose、preview、visibility picker、emoji picker、media attachment UI。
- Misskey任意絵文字reactionの作成、変更、正確なUndo。
- popupのenter/leave、Escape、focus復帰。
- 上流`form/input.vue`、`form/switch.vue`、`MkInfo.vue`のDOM/class、入力更新、focus、debounce、size variant、破棄境界。
- 上流`MkButton.vue`、`MkCwButton.vue`、`MkKeyValue.vue`、`MkLoading.vue`、`MkEllipsis.vue`、`MkSpacer.vue`、`MkStickyContainer.vue`のDOM、CSS Modules、状態分岐、root属性fallthrough、破棄境界。
- `MkCwButton`のstringz 2.1.0互換文字数、Vue `v-show`と同じDOM保持、展開状態の再render維持。
- `MkSpacer`の初期0px、desktop/tablet/smartphone、上書きdevice kind、強制最小余白、実About画面のresponsive geometry。
- 入れ子`MkStickyContainer`の親48pxと子32pxの80px合成、resize後96pxと32pxの128px合成、およびhydrate順序競合からの回復。
- `MkModalWindow.vue`のDOM、370x400 geometry、開始frame、200ms opacity/scale、leave、enter途中のclose取消、computed-duration fallback、上流`MkInput autofocus`の再現、入れ子dialogのfocus隔離と復帰。
- `MkUpdated.vue`の固定DOMとCSS、runtime version、全locale label、既存`MkSparkle`、middle priority modal、narrow touch drawer、release link、focus、Escape、背景click、200ms leave後の`closed`順序。
- Vue互換のraw `lastVersion`と`theme`更新、compare-versions 5.0.1順序、および実AuthenticationStateを用いた認証済みupgradeだけの表示条件。
- JS attachment確立前にpopupをEscapeで閉じる競合と、circuitの継続。
- 20個の上流themeと、透明なcustom themeの拒否。
- `locales/index.js`が列挙する25言語、各1632 effective key、上流fallback、dot-path、単一置換interpolation。
- safe locale cookieと`Accept-Language`によるSSR culture、旧Vue `lang`のhydrate、`html.lang`と`html.dir`の同期。
- 改竄した`localStorage.locale`を翻訳源へ使用せず、既存値を削除しない移行境界。
- `MkForgotPassword.vue`、`signup-complete.vue`、`reset-password.vue`の固定上流DOM、class、生成CSS、responsive form、focus、keyboard、alertとmodalのenter、leave、取消。
- `MkSignup.vue`と`MkCaptcha.vue`のinvitation-first DOM、hash化・期限付き・単回招待、Identity作成との同一transaction、hCaptcha・reCAPTCHA分岐、fail-closed submit、callback、登録失敗時reset。
- password resetとemail確認のfragment-only token取込み、historyからの即時消去、同一pageへの新token再取込み、PostgreSQL上のhash化、有効期限、cooldown、単回消費、replay拒否。
- Interactive ServerのJS attachment前に新しいdialogをEscapeで閉じる際、明示された実Razor close操作だけを起動し、背面dialogへ入力を漏らさないpending-overlay境界。
- dialogとbuttonの破棄時に、そのcomponent自身のlifetime cancellationだけを正常終了として扱う境界。
- 全35個の遅延ES module importを同じ文書base URI解決とpage lifecycle境界へ集約し、WebKitが破棄中のdocumentで返すimport失敗だけを`JSDisconnectedException`へ変換する。通常のnetwork、CSP、構文、module評価失敗は元の`JSException`として維持する。
- `MkDateSeparatedList`のFLIPで全要素の新座標を読み終えてからinverse transformを書き、子要素の同期layout readを0回、rootのlayout確定を1回に限定する境界。日付partは既存ID＋時刻を再利用し、prepend時は新規項目だけをbrowserへ問い合わせる。
- `NoteView`の500、450、350、300px状態をCSS container queryで投影し、各NoteのResizeObserverからInteractive Serverへrender callbackを送らない境界。
- Paginationのtop状態をbrowser側でscroll containerの変更を含めて追跡し、状態変化時だけServerへ通知する境界。away-from-topの各stream Noteごとに同期JS queryを行わない。

旧来の簡易`NoteComposer`と、そのための自作timeline/note CSSは本番sourceから除去した。

この除去は、上流`MkPostForm`のRazor移植を通常の投稿経路として使用するためである。

## .NET品質ゲート

次のコマンドは成功した。

```bash
dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore
dotnet build ActivityPubServer.slnx --configuration Release --no-restore
```

Release buildは警告0件、エラー0件だった。

テストは次の409件が成功した。

| Test assembly | 成功 |
|---|---:|
| Domain | 55 |
| Federation | 55 |
| Media | 23 |
| Misskey Blazor | 116 |
| Moderation | 2 |
| Persistence | 50 |
| Property | 3 |
| API | 105 |

`dotnet test ActivityPubServer.slnx --configuration Release --no-build`を修正後に全assemblyへ一括実行し、409件すべてが成功した。asset fingerprint中の偶然の文字列ではなく、実行可能script tag、`data-v-`、`.vue`参照を直接検査してVue runtime除去を判定する。

上記409件は2026-08-04の基準試験である。2026-08-12の現行再検証では、同じRelease設定で全assemblyを再実行し、Domain 60、Media 33、Federation 61、Property 3、Misskey Blazor 536、Moderation 2、Persistence 74、API 132の合計901件が成功した（failed 0、skipped 0）。この901件を現行コードの品質ゲート結果として扱い、基準試験の件数と混同しない。

## Vue oracleと生成物

Vue版は移行中のoracleとしてだけbuildする。

次の検査は成功した。

- 固定upstreamとの照合：573/573 client files、537 byte-identical、36 reviewed modifications。
- AST inventory：535 source、400 Vue SFC、115 routes、262 static API endpoints、14 Streaming channels。
- Vue TypeScript typecheck。
- Vitest 14件。
- Vue oracle production build。
- Blazor向け上流CSS、emoji data、MFM browser module、Matter.js 0.18.0 browser artifactの再生成差分0件。
- npm high severity audit：0 vulnerabilities。

Vue oracleのbuild成功はBlazor移植成功として数えない。

## Browser試験

Playwright 1.62.1でローカル195ケースを列挙し、Tailnet専用3ケースを除く192ケースが成功した。

Chromium、Firefox、WebKitは、それぞれ64件のローカルケースに成功した。

内訳は背景とthemeが159件、上流DOMと実操作が33件である。後者はwelcomeの
`MkFeaturedPhotos`と`MkMarquee`を含み、Marqueeの描画幅から上流と同じ式で算出した
animation duration、2反復、実instance link、hover中の停止を3 engineで照合する。さらに
`/about-misskey`のDOM、物理演出、破棄、実投稿フォームと永続create回数を照合する。さらに`MkModalWindow`相当の開始、終了、取消frameとfocus復帰を全3 engineで照合する。

加えて、`/about-misskey`とwelcomeを5回連続で往復し、test hostが記録するBlazor circuit/rendererの未処理例外が0件であることを3 engineで確認する。

Tailnet公開環境では、Chromium、Firefox、WebKitの3件が別実行で成功した。

認証UIに限定した追加実行では、`auth-ui-parity.spec.ts`のChromium、Firefox、WebKit各3件、合計9件が成功した。

この追加実行は通常login form、TOTP切替、signup validation、mobile geometry、modal bodyのalpha 255を裏付けるが、実credential成功、WebAuthn、email確認、password resetは対象外である。

招待制登録とCAPTCHAに限定した追加実行では、同じspecのChromium、Firefox、WebKit各2件、合計6件が成功した。

この実行はinvitation-firstの表示、hCaptchaとreCAPTCHAのupstream DOM順序、公式script origin、初期fail-closed、callback後の送信許可、API payload、失敗後のwidget resetと再送不可、上流と同じerror alert、focus、acknowledgementを照合した。

2026-08-13 UTCの登録操作回帰では、`auth-ui-parity.spec.ts`と`signup-dialog-parity.spec.ts`のChromium 10件が成功した。Turnstile scriptの初回取得を故障注入し、dialogを閉じて再度開いた際の再取得、widget生成、token callback、modal背景のalpha 255、TestHostの未処理例外0件を確認した。修正前は再表示後もwidgetが0件のままになることを同じ試験で再現した。

同日の初回インストール回帰では、固定版`welcome.vue`と`welcome.setup.vue`を正本として、`meta.requireSetup=true`時だけ通常Entranceの代わりに`mk-setup`を描画することを確認した。`HomeTests`はDOM分岐と不要なfederation query抑止を、`InitialAdministratorSetupIntegrationTests`は実PostgreSQL上の同時2要求から管理者・local actor・署名鍵が一組だけ確定することを、`PublicEndpointTests`は完了後の`/api/admin/accounts/create`拒否を検証した。`welcome-setup-parity.spec.ts`のChromium 1件は上流DOM/CSS、panelとaccent headingのalpha 255、Interactive Server listener確立後の実JSON送信、session Cookieによる認証済みtimeline遷移を確認した。

招待入力は26文字上限と26文字payloadを照合する。server側は130 bit code、SHA-256保存、単回消費を試験し、migration試験は実PostgreSQLでhash長、期限、reservation、消費状態の4制約違反を拒否する。

provider callbackとserver応答はfixtureであり、外部live providerのavailabilityまたはproduction keyを検証した結果ではない。

password recoveryに限定した追加実行では、`password-recovery-parity.spec.ts`のChromium、Firefox、WebKit各3件、合計9件が成功した。

この実行は、固定上流のform階層とclass、上流生成CSS、autofocus、keyboard submit、responsive geometry、modal bodyのalpha 255、enter、leave、rapid Escapeによる取消、fragment tokenの即時消去、同一pageへの新token取込みを照合した。

`overlay-stack.spec.ts`と`browser-lifecycle.spec.ts`を合わせた追加実行では、Chromium、Firefox、WebKitの18件すべてが成功した。入れ子dialogのusername初期focus、背面入力の遮断、Escape、focus復帰、即時close/reopen、および破棄中importを持つ旧documentから新circuitへの遷移を照合した。WebKitの破棄試験はさらに5回連続で成功し、rapid close/reopenはWebKitで10件連続成功した。各破棄試験後のTestHost circuit/renderer未処理例外は0件である。

同じbrowser lifecycle試験は、active documentで評価時に失敗するtest-only moduleのエラーが`MISSKEY_INTEROP_PAGE_DISPOSAL`へ変換されないことも照合する。`BrowserModuleImporterTests`はmarkerだけを切断へ変換し、通常の`JSException`とcancellationを再分類しないことを個別に検証する。

背景回帰専用の`background-opacity.spec.ts`はChromium、Firefox、WebKit各56件、合計168件が成功した。SSRからhydrate、enhanced navigation、login/signup overlay、5 viewport、20上流theme、custom theme拒否を検査し、`html`、`body`、shell、panelのalphaは255、backdropだけが意図したalpha 128だった。安定frameのfocus証拠9枚は`artifacts/frontend-audit/focus-lifecycle-20260804T114317Z/`へ保存し、全画像を目視確認した。

email確認はalertのleave完了後だけHTTP送信を開始し、password request、email確認、password resetの期待した失敗statusと成功statusを分類した。

browser console error、page error、未分類HTTP 4xxまたは5xx、TestHostの未処理circuit例外は0件だった。

関連する`PasswordResetUiTests`は10件、HTTP adapter試験は5件、PostgreSQL fixture試験は6件が成功した。

fixture配送ではfragment linkのtokenとPostgreSQLへ保存したhashを照合したが、外部live SMTP serverには接続していない。

Tailnet試験はstatic SSR、背景alpha=255、popupとabout panelのalpha=255、表示直後のEscapeを5回連続した後のcircuit継続、Matter.jsのproduction CSP下での起動、Vue/Vite不在、runtime config、OIDC discovery、PKCE challengeを確認する。

この試験は、内部連合IRI `http://activitypub`を変更せず、Tailnet UI originを`Frontend:PublicBaseUri`として分離した構成で実施した。Blazor既定の再接続UIがFirefoxでinline stylesheetを生成する問題も検出し、同一origin CSSの明示的な再接続hostへ置換後に3 engineで再検証した。

Browser試験中のconsole error、page error、未分類HTTP 4xx/5xxは0件だった。

`MkUpdated`専用の追加実行では、`updated-parity.spec.ts`のChromium、Firefox、WebKit各10ケース、合計30件が成功した。固定DOM、CSS、通常・reduced motion、Sparkle、focus、Escape、背景・確認button、release popup、narrow geometry、touch drawer、raw Vue storage移行、認証済みupgrade、guest非表示、storage拒否時の安全な診断を照合した。panel背景はdesktop、390px幅、reduced motionの全経路でalpha 1を要求し、透明背景の再発を失敗として扱う。

Visibility専用の追加実行では、`visibility-tooltip-parity.spec.ts`のChromium、Firefox、WebKit各3件、合計9件が成功した。ここでは送信先12ユーザーから10件だけを実表示し`+2`を示す実データlookup、未認証viewerへの非表示、localOnlyの独立表示、hover、focus、keyboard、touch、leave取消、およびstatic SSRからInteractive Serverへの接続競合を照合した。

2026-08-12 UTCに追加した `supported-soak.spec.ts` はChromiumで12反復×10 supported route（`/about`、federation tab、local timeline、notifications、profile settings、API settings、admin relays、user profile、unsupported user clips/followers）を独立起動で実行し、1件が成功した。各遷移でUniversal/Visitor shellの不透明背景、console/page error、HTTP 4xx/5xx、TestHostの未処理回路例外とtransport failureを確認した。supported画面を先行実行した統合コマンドでは、前テストの切断回路が遅延して記録したKestrel `ApplicationNeverCompleted` がsoak開始直前に診断へ混入したため、統合64件と独立soak 1件を別証跡に分離している。

その後の現行コード再検証では、About、settings/admin、follow、note、user profile、背景opacityのChromium合計66件に加え、settings theme/general/privacy と followers/following のfresh-publish 4件が成功し、`supported-soak.spec.ts`も独立起動で再度1件成功した。User profileの`/@alice`、`/@alice/clips`、`/@alice/followers`を含む遷移でconsole/page/HTTP/回路診断を確認した。

2026-08-13 UTCのtimeline motion性能回帰では、focused component test 24件とChromiumの`date-separated-list-parity.spec.ts`、`note-view-parity.spec.ts`、`pagination-parity.spec.ts`、`timeline-parity.spec.ts`が成功した。FLIPのlayout read回数、container queryの各breakpoint計算値、日付の差分取得、scroll-away時のqueueとtop復帰を確認した。これは局所回帰の結果であり、Vueとの包括的なFPS、INP、memory、長時間soak比較ではない。

同じChromium specを連続実行した際、前画面の`MisskeyWidgets`がInteractive Server circuit切断後にdevice storageを読む競合を再現した。`JSDisconnectedException`をcomponent lifecycleの終了として扱う局所修正後、`WidgetsTests` 4件と、再現順序を維持した`date-separated-list-parity.spec.ts`→`note-view-parity.spec.ts` 2件が成功し、TestHostの未処理circuit診断は0件になった。

Localization専用の追加実行では、`localization-parity.spec.ts`のChromium、Firefox、WebKit各2件、合計6件が成功した。

この実行はSSRのlocaleとdirection、cookie優先順位、25言語API完全性、旧Vue storage移行、改竄locale JSONの非採用、RTL切替、`MkContainer`の`showMore`を照合した。

Firefoxで初回documentの`Accept-Language`が`ja-JP`、Interactive Server接続が`en-US`となる差を検出し、document応答でsafe locale cookieを確定してcircuitへ引き継ぐ修正後に3 engineで再検証した。

## 未完了範囲

次の項目は、この検証結果から除外する。

- 115 routeすべてのRazor移植。
- 400 Vue SFCすべてのRazor component対応。
- Classic、Deck、Zenの完全な操作同等性。
- Drive、admin、moderation、plugin、AiScript、pages、gallery、channelsの全画面。
- 25言語共通catalogへ未接続のRazor画面と、各画面のlocale別visual試験。
- 全transitionの開始、中間、終了、取消frame比較。
- 全routeのvisual screenshot differential。
- accessibility全項目、performance比較、memory leak、長時間soak。
- upstreamと同一の登録・ログイン画面、validation、成功・失敗・取消状態遷移。
- WebAuthn security keyの実authenticator、hCaptcha・reCAPTCHAの外部live provider、およびemail確認の外部live SMTP配送。
- 各pageのDOM階層、class、responsive breakpoint、scroll/focus/historyのVue oracle differential。
- scoped selector、pseudo state、CSS variable、z-index、overflow、Teleport相当overlayを含むCSS全量の同等性。
- watcher、computed、lifecycle、directive、storage、Streaming再購読を含むVue挙動のRazor状態機械への置換。

これらが成功するまで「Misskey v12フロントエンド完全移植」とは判定しない。
