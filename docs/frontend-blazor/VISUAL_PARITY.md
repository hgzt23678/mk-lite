# Visual parity

## 判定単位

Visual parityはroute、viewport、theme、browser engineの組み合わせごとに判定する。

一つのrouteの成功を、未試験のrouteへ外挿しない。

静止状態、motion lifecycle、操作結果を別の試験として扱う。

## 上流CSS

通常UIのCSSは、固定したMisskey 12.119.2 sourceから`tools/generate-blazor-upstream-css.mjs`で生成する。

対象source、commit、source set digestを生成CSSの先頭へ記録する。

`app.css`は背景の安全なfallback、認証障害、Blazor circuit障害だけを扱う。

簡易timeline、簡易note、簡易composerを`app.css`で再設計することは禁止する。

Inventory testは、旧簡易componentと禁止selectorが本番sourceへ戻っていないことを検査する。

## 背景の不透明性

Misskey clientの最上位surfaceは透明であってはならない。

`html`、`body`、`.mk-app`、`.dkgtipfy`、`.gbhvwtnk`へ不透明な`--bg` fallbackを設定する。

custom themeの`bg`、`panel`、`popup`はbrowserのCSS parserで検証し、alphaが255未満の値を拒否する。

不正なthemeは部分適用せず、既定themeへ戻す。

## Browser evidence

`tests/frontend-blazor-e2e/background-opacity.spec.ts`は次を検査する。

- Chromium、Firefox、WebKit。
- 360x800、390x844、768x1024、1440x900、1920x1080。
- light、dark、opaque custom theme。
- 透明なroot surfaceとpopupの拒否。
- Misskey 12.119.2に含まれる20 theme。
- 未認証Visitor shellと認証済みUniversal shell。
- computed backgroundをCanvasへ描画したalpha=255。
- `/about-misskey` panelを同じ全theme、viewport、engineで描画したalpha=255。
- console error、page error、HTTP 4xx/5xxが0件。

2026-08-04 UTCの結果は159件成功だった。

`tests/frontend-blazor-e2e/upstream-dom-parity.spec.ts`は3 engineで33件成功した。

この試験はwelcome、`MkFeaturedPhotos`、`MkMarquee`、popup、timeline、note、reaction picker、post form、visibility picker、`/about-misskey`のDOM階層と操作結果に加え、Marqueeの反復・幅依存duration・hover停止、Matter.jsによるtransformと破棄、popupのJS attachment確立前に5回連続でEscapeを入力してもcircuitが継続することを確認する。`MkModalWindow`については初期opacity=0、200msのopacity/scale、enter完了、leave開始・完了、enter途中のclose取消、computed-duration fallback、focus復帰を3 engineで実測する。さらにaboutとwelcomeを5往復し、server renderer/circuitの未処理例外が0件であることを機械判定する。

`tests/frontend-blazor-e2e/tailnet-signin-contract.spec.ts`は公開Tailnet originでlocal `MkSignin`の`/api/signin` action、初期focus、panel alpha、Keycloak/Vue/Vite非混入をChromiumで確認する。

## 未検証範囲

現在の試験は、34個の`implemented` sourceと35個の`in-progress` sourceが構成する検証済み垂直スライスだけを対象とする。

`updated-parity.spec.ts`は`MkUpdated`のpanel背景alphaをdesktop、390px幅、narrow touch drawer、reduced motionで検査する。いずれかが透明なら、DOMやbutton操作が成功していてもvisual parity失敗とする。

115 route、400 Vue SFC、全locale、全animation frameのbaseline比較は未完了である。

大きな画面領域をmaskしたvisual testや、Vue sourceを表示するiframeを証拠として使用しない。
