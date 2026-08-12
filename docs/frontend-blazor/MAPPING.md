# VueからRazorへのmapping

## 判定ファイル

機械可読な対応関係は`frontend/ActivityPub.Misskey.Blazor/upstream-port-map.json`へ記録する。

各entryは上流source、実際のRazor target、migration status、自動試験を持つ。

`eng/generate-frontend-inventory.mjs`はこのファイルを読み、source、target、testが存在しないentryを拒否する。

明示entryがないsourceは、同名に近いRazor fileが存在しても自動的に`implemented`へ昇格しない。

この規則により、簡易UIや偶然一致したファイル名を完全移植として数えない。

## 状態の意味

- **planned**：sourceを分類し、移植先を決めたが、実装証拠がない。
- **in-progress**：Razor targetと自動試験が存在するが、上流の全props、events、slots、motion、routeからの到達性をまだ検証していない。
- **implemented**：上流契約、実データ操作、visualとbehaviorの比較をすべて通過した。
- **blocked**：外部条件が不足し、理由を`blockedReason`へ記録した。

routeが表示できるだけでは`implemented`へ変更しない。

CSS classが一致するだけでも`implemented`へ変更しない。

## 現在値

2026-08-04 UTCの現在値は次のとおりである。

| 状態 | Source数 |
|---|---:|
| implemented | 34 |
| in-progress | 35 |
| blocked | 0 |
| planned | 466 |
| unclassified | 0 |

対象sourceは合計535件であり、そのうちVue SFCは400件である。

route inventoryは115件、static API endpointは262件、Streaming channelは14件である。

現行Vue接続版で固定upstreamから変更された明示mappingは、`contractSource`を`pinned-upstream-with-local-delta`とし、`upstreamContract`を別に持つ。

これにより、現行接続版の`MkSignin.vue`と`MkSignup.vue`がOIDC開始buttonへ変更されていても、固定upstreamのpassword、2FA、WebAuthn、招待、CAPTCHA、email検証を移植対象から消せない。

`planned`と`blocked`はtarget名を移植先として予約できるが、実ファイルの存在を要求しない。

存在しないRazor pageを作成済みとして扱わないためである。

明示mappingには検査testを必須とし、`blocked`では`blockedReason`も必須にする。

## 完了判定

`implemented`へ移す前に、少なくとも次を確認する。

- DOM階層、class、responsive layoutが上流oracleと一致する。
- 入力、focus、keyboard、pointer、touch、drag and dropが同じ結果になる。
- enter、leave、move、取消のmotionが一致する。
- 実API、PostgreSQL state、ActivityPub副作用、Streaming eventを照合する。
- private情報が未認可viewerへ投影されない。
- Chromium、Firefox、WebKitでconsole errorと未分類404が0件になる。

一つでも未確認なら`in-progress`を維持する。

登録、ログイン、password reset、email確認のsource別判定は`AUTHENTICATION_PARITY.md`へ記録する。

localizationのsource別判定とstorage移行境界は`LOCALIZATION.md`へ記録する。
