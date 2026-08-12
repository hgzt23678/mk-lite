# Dolphin backend contract review

基準は `mei23/dolphin` の固定 checkout（commit `3ce200269f814547dc7dfc6b246abadf8a9c00ed`、`.cache/meidolphin`）です。Misskey v12 の画面コードだけから API の意味を推測せず、Dolphin の endpoint metadata と現在の C# adapter route を照合しています。

機械生成された全 endpoint の一覧と照合結果は [dolphin-misskey-12.json](/root/new-project/artifacts/backend-contract/dolphin-misskey-12.json) にあります。再生成は次で行います。

```bash
node eng/generate-dolphin-contract.mjs
node eng/generate-dolphin-contract.mjs --check
```

## 現在の差分

| 項目 | 実測値 |
| --- | ---: |
| Dolphin endpoint source | 145 |
| 現行 C# Misskey route | 61 |
| Dolphin endpoint に対応する現行 route | 40 |
| 未対応 Dolphin endpoint | 103 |

「route がある」だけで契約適合とは判定していません。各 endpoint は request validation、認証／権限、エラー、永続副作用、ActivityPub／通知／stream side effect、回帰 fixture を確認してから `implemented` へ昇格します。

## 現在の supported 画面契約

現行バックエンドと永続副作用が揃っているため、Blazor では次を実データで使用できます。

- `i`、`i/update`
- `i/apps`、`i/revoke-token`
- `notes/timeline`、`notes/global-timeline`、`notes/local-timeline`
- `notes/show`、`notes/create`、`notes/delete`
- `notes/reactions/create`、`notes/reactions/delete`
- `i/notifications`、`notifications/mark-all-as-read`
- `admin/invite`
- `admin/announcements/list|create|update|delete`
- `admin/relays/list|add|remove`
- `federation/instances` (About の federation tab: bounded host/state/sort query と offset pagination)
- `users/show`、`users/notes` (User profile home: viewer-aware profile projection and bounded note pagination)
- `users/search` (Explore search tab: bounded prefix search, local/remote origin filter, and viewer-aware user projection)

端末設定の `settings/navbar`、`settings/deck`、`settings/custom-css` は Dolphin API を要求せず、v12 の `pizzax::base` と `customCss` のブラウザー永続化境界を保った Interactive Server コンポーネントとして実装している。Custom CSS は `textContent` のみで適用し、外部 import、URL fetch、script、markup、サイズ超過を拒否する。Dolphin のサーバー状態を必要とする設定はこの画面群へ混在させていない。

`settings/reaction` も同じ端末設定境界で実装している。v12 の既定リアクション、ドラッグ順、pickerサイズ・列数・高さ、モバイルdrawer設定を `pizzax::base` へ保存し、共有EmojiPickerを使っている。設定値はChromium parityで再読込後も確認しており、Dolphinにないサーバー副作用を捏造していない。

`settings/theme` はDolphin APIを必要としない組み込みテーマ選択として実装している。Misskey 12.119.2の20テーマを検証済みcatalogからlight/darkへ分け、`pizzax::base.darkMode`と`miux:lightTheme`/`miux:darkTheme`の旧Vue保存形状を維持する。`i/registry`とDrive/media uploadを必要とするtheme install/manage/editor/wallpaperは capability=false と理由を明示し、空配列や常時成功で代替しない。

`settings/general` はv12の端末設定キーを、`settings/privacy` はDolphinが実際に永続化できるlock/discoverableだけを対象にしている。サーバー契約がないprivacy項目は capability=false と理由を表示し、成功レスポンスや仮の初期値で隠していない。

`settings/notifications` は `notifications/mark-all-as-read` のみdurable notification aggregateへ接続し、`i/read-all-unread-notes`、`i/read-all-messaging-messages`、`mutingNotificationTypes` は capability=false としている。`user-info` は `users/show` のviewer-aware projectionとfollow commandを使い、Dolphinにない管理・IP・raw・chart・Drive契約は表示しない。

プロフィールは現在の Dolphin 契約で扱える名前・説明と、永続化された same-origin avatar/banner の表示に限定しています。Drive からの avatar/banner 差し替え、プロフィール補足 fields、birthday、locale は契約がないため UI で成功を偽装しません。

`pages/about.federation.vue` の移植先は `Components/AboutFederation.razor` です。`AboutPage` の federation tab から同じコンポーネントを実データで利用し、`AboutPageTests` と Chromium parity で表示・filter・pagination・link を確認しています。`instance-info` のチャートや未提供の詳細契約はこの画面の成功条件に含めません。

`pages/user/home.vue` と `pages/user/index.timeline.vue` の移植先は `Pages/UserPage.razor` です。`users/show` と `users/notes` の viewer boundary、v12 profile CSS/DOM、実note list、follow状態をChromium parityで確認しています。pinned notes、profile fields、clips、pages、gallery、activity/chartはDolphin契約がないため capability=false または非表示です。

## 明示的な除外

Drive 管理、charts、antennas、channels、clips、gallery、2FA などは現行 backend の契約が揃うまで移植対象外です。該当 route を直接開いた場合は空配列や固定値ではなく `capability=false` と理由を返します。

## 重要な不足

Dolphin の endpoint metadata と比べると、未対応には次が含まれます。

- account security、2FA、signin history
- follow request／user list の全操作
- notes の replies、conversation、featured、search、poll の全契約
- moderation、abuse report、queue、emoji 管理
- `users/search` の `offset` は現行 query 境界が最大100件の bounded prefix search であり、100件を超えるoffsetの完全互換は未提供
- Drive 管理と charts
- streaming channel の全 cursor／reconnect 契約

これらを実装済みとは宣言しません。次の作業は endpoint ごとに C# Application／Domain／Persistence の副作用と Dolphin fixture を追加し、Blazor screen を昇格させることです。
