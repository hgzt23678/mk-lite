# Misskey 12.119.2 UI/CSS/挙動の完全同等性要件

## 完成判定

完全移植とは、固定commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`の利用者到達可能なclient挙動をRazor Componentsへ置換し、同じfixtureでVue oracleとの差分を自動検証した状態を指す。

この文書のfrontend oracleはMisskey v12.119.2である。API、永続副作用、認可、ActivityPub連合、モデレーション、メディア、キューのbackend oracleは`mei23/dolphin`であり、Misskey v12 frontendから推測しない。ユーザー確認済みの`.cache/meidolphin` commit `3ce200269f814547dc7dfc6b246abadf8a9c00ed`をbackend比較へ使用する。

簡素な自作UI、似た画面、routeだけ存在する画面、固定データ、空配列、iframe、Vue wrapperは移植として数えない。上流sourceがrepositoryに存在することやVue build成功も移植証拠ではない。

画面を独自componentへ置き換えて機能名だけ一致させることを禁止する。固定版SFCのcomponent境界、DOM階層、class名、SCSS/CSS custom property、assetを移植元として再利用し、Vue依存のtemplate/state/lifecycle部分だけを対応するRazor Componentと型付き状態機械へ移す。意図的な差分は、セキュリティまたはアクセシビリティ上の根拠、oracle差分、回帰試験を揃えない限り許可しない。

## 登録とログイン

- welcomeから登録・ログインへ至るbutton、modal、dialog、説明、validation、focus順序を同じDOM/class/CSSで再現する。
- 登録・ログイン画面のpage/modal階層、`_monolithic_`/`_section`/`_formRoot`構造、`MkInput`/`MkButton`/`MkInfo`相当component、背景・透過度・影・余白・幅・高さを固定版oracleと一致させる。
- 登録可否、招待、利用規約、CAPTCHA、username/password/error表示、処理中、成功、取消を上流状態遷移どおり扱う。
- OIDC Authorization Code + PKCE境界を維持しつつ、上流UIのlogin/account switching/logout/session-expiry表示を再現する。
- 認証成功は画面遷移だけでなくOIDC response、session、local Actor対応、認可後queryで検証する。
- `MkSignin.vue`、`MkSigninDialog.vue`、`MkSignup.vue`、`MkSignupDialog.vue`、`MkForgotPassword.vue`、`signup-complete.vue`を個別にsource mappingし、通常、入力途中、validation失敗、2FA、WebAuthn、email確認待ち、完了、取消の各状態をfixture化する。
- `signing`、`totpLogin`、username availability、password strength/retype、processing、modal open/closeのclass/state変化を同じ入力系列とanimation frameで比較する。
- 外部Identity Providerへ遷移できるだけのbutton、Keycloak既定画面、password入力を無視するformは、Misskey登録・ログイン移植の証拠として扱わない。認証情報を安全に検証する正規のIdentity/OIDC経路と元画面の操作結果を両立させる。

## ページ構造

115 routeすべてについて、route評価順、guard、query/hash、history、deep link、refresh、scroll restorationを一致させる。

各pageはDOM階層、要素種別、class、slot相当位置、header/action/tab、empty/loading/error状態、responsive分岐、mobile/desktop/Deck配置をoracleと比較する。見た目が近くても構造と操作結果が異なる場合は未完了とする。

各routeについて、親shellからpage rootまでの構造manifestと、主要interaction後の構造manifestを保存する。別の単純なcontainerや共通placeholderへ置換したrouteは未移植とする。

## CSS

上流SFC/SCSSのselector、specificity、pseudo class/element、media query、CSS variable、theme継承、font、icon、background、border、shadow、blur、z-index、sticky、overflow、safe-areaを維持する。

Vue scoped CSSとBlazor CSS isolationのscope差をDOM単位で検証する。適用不能なselectorだけを根拠付きで変換し、global CSSや独自デザインへ逃がさない。light、dark、custom、全20同梱themeと指定viewportを3 browserで比較する。

全style blockはsource path、source hash、変換後selector、利用component、visual testへ紐付ける。背景色と背景画像については各page root、panel、modal、drawer、popupの計算済みalphaも検査し、透明化を再発させない。

## Vue挙動のRazor移植

props、emits、slots、computed、watch、watchEffect、mount/update/unmount、Teleport、directive、keyed renderingを、型付きpresentation stateとRazor lifecycleへ明示的に写像する。

`v-if`と`v-show`、computed cache、watchの実行順、nextTick境界、keyによるcomponent再生成、イベント修飾子のprevent/stop/self/onceを区別する。DOMが残る状態と破棄される状態を勝手に同一化しない。

focus、keyboard、pointer、touch、drag and drop、hover、context menu、overlay stack、outside click、Escape、scroll anchor、pagination、storage、BroadcastChannel、Streaming subscribe/reconnect/unsubscribeを同じ入力系列で比較する。

Vue runtime、Vue Router、Pizzax、Vue lifecycleをJavaScriptへ残してはならない。browser primitiveと固定parser/engineだけを型付きES module境界に隔離する。

## Animation

inventory上の全Transition/TransitionGroup、keyframes、animation、transitionをsource mappingへ紐付ける。

enter-from/active/to、leave-from/active/to、appear、out-in、in-out、duration、delay、easing、transform-origin、FLIP、stagger、nested durationを維持する。開始、中間、終了、連続操作、enter取消、leave取消、route離脱、reduced-motionを実frameで検証する。

animationをvisual testで無効化した結果をproduction同等性の証拠にしない。古いcallbackが新しいDOMを変更しないこと、timer/RAF/listener/observer/engineが破棄されることも必須とする。

## ファイル単位の証拠

全535 sourceにtarget、props/emits/slots/directives、route、API、Streaming、storage、DOM class、style、motion、browser API、認証/認可、test、statusを記録する。

`implemented`へ昇格できるのは、次をすべて満たすsourceだけである。

- contract/component test。
- Chromium、Firefox、WebKitのbehavior test。
- 固定環境のvisual differential。
- 実API、DB state、Domain state、ActivityPub副作用、Streaming eventの必要箇所での照合。
- console error、未処理Promise、未処理circuit exception、未分類404が0件。

2026-08-12 UTCの生成mappingは`implemented=329`、`in-progress=0`、`blocked=0`、`planned=0`、`excluded=206`、`unclassified=0`であり、完全移植ではない。excluded 206件は、Dolphinに存在しないまたは完全契約が未検証の機能を明示したものである。

認証対象sourceは`implemented`へ昇格済みである。ただし、外部provider live統合と実browser authenticator enrollmentは未検証のため、互換性の成功宣言には含めない。

`signup-complete`はfragment-only token、hash永続化、単回確認、確認後sessionまで実装済みだが、live SMTPと固定Vue routeに対する3 browser visual/motion比較が未完了のため`in-progress`である。

upstreamの`/signup-complete/:code`はaccess logへのtoken露出を避けるため、productionでは`/signup-complete#token`へ意図的に変更する。この差分はtoken非露出、history消去、replay拒否を自動試験し、未実施のvisual比較をmappingのknown gapへ残す。
