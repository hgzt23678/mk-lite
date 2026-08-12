# Motion移植

## 基準

Motionのsource of truthはMisskey 12.119.2 commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`である。

`artifacts/frontend-inventory/motion.json`はVue SFCとSCSSを構文解析し、42個のTransition要素、23個のkeyframes、35個のanimation宣言、105個のtransition宣言、7個の`requestAnimationFrame`利用を記録する。

件数だけでは移植済みと判定しない。source mappingごとに開始、中間、終了、取消、route離脱後の破棄を証明する。

## 現在の実装範囲

- popupは上流のduration、opacity、scaleを使い、enter完了前のEscapeも取消できる。
- `MkModalWindow`はSSR時点で`modal-enter-from`を付与して初回描画のflashを防ぎ、2回のRAF後に上流の200ms opacity/scaleへ遷移する。
- modal leaveは対象elementごとのtransition/animation duration、delay、iterationを計算し、早いpropertyの`transitionend`だけでは完了させない。generationでenter callbackを取消し、event欠落時だけ計算時間+80msのfallbackを使う。
- `MkMarquee`は実DOM幅から上流式でdurationを計算し、2反復とhover停止を維持する。
- `MkLoading`は固定版の`_spinner_13vug_35` keyframe名、500ms linear infinite、38pxと32pxのvariantを維持する。
- `MkEllipsis`は固定版の`ellipsis-abe8165c` keyframe名、1400ms ease-in-out、0ms、160ms、320msのstaggerを維持する。
- `MkDigitalClock`は上流の1秒または10ms ticker、秒境界の30ms `showColon` pulse、その後の1秒opacity遷移を維持する。timerは型付きES module内だけで動作し、Server circuitへ10msごとのrenderを送らない。parameter交換では旧handleを破棄し、generationで遅延attachを棄却する。
- `MkToast`は300msのopacity/translateY enter・leaveと4秒の表示期間を維持し、対象propertyを確認した完了eventと計算duration fallbackを使う。
- `MkSparkle`は上流の500〜1000ms生成間隔、1000〜2000ms particle寿命、SMIL scale/rotate、ResizeObserverによる実寸座標を維持する。
- `MkUpdated`はdesktopで200msのopacity/scale、narrow touchで200msのopacity/translate drawerを使用する。leaveの完了前には`closed`を発火せず、連続closeは一度だけ処理する。通常設定のtimingは変えず、reduced motionだけ0.001msへ短縮する。
- `/about-misskey`は固定Matter.js 0.18.0を同一originから読み込み、上流と同じemoji body、境界、mouse constraint、step処理を使う。
- Matter.jsのrunner、animation frame、interval、mouse event、world、engineはroute離脱時に破棄する。
- `prefers-reduced-motion: reduce`では非本質motionを短縮するが、通常設定のtimingは変更しない。

Matter.js browser artifactは`tools/generate-matter-browser.mjs`がlockfileのversion、license、SHA-256を検証して生成する。CDNや実行時package取得は行わない。

## 検証

`upstream-dom-parity.spec.ts`はChromium、Firefox、WebKitで物理演出開始、emoji transform更新、navigation後のengine破棄を確認した。modalでは開始、終了、leave、enter取消、上流autofocus、入れ子focus隔離、focus復帰を確認した。`browser-lifecycle.spec.ts`は破棄中module importを持つ旧documentから新documentへ遷移し、3 browserでserver circuit未処理例外0件、WebKitで5回連続成功を確認する。active documentの実module評価失敗は切断へ再分類しない。

`loading-parity.spec.ts`は`MkLoading`の開始後frame、variant、reduced motionと、`MkEllipsis`のkeyframe、stagger、中間opacityを3 browserで検査する。

`digital-clock-parity.spec.ts`は条件付きspan DOM、UTC offset、1秒/10ms更新、30ms colon class lifecycle、route破棄、背景alphaをChromium、Firefox、WebKitで検査し、9/9件が成功した。`toast-parity.spec.ts`と`sparkle-parity.spec.ts`もそれぞれ通常motion、取消・早期破棄、reduced motionを3 browserで検査する。`updated-parity.spec.ts`はnormalとdrawerのenter/leave、Escape、背景click、focus復帰、`closed`順序、reduced motionを3 browserで検査する。

Tailnet試験はproduction CSPと`/app/` path baseの下でも同じ演出が開始することを確認した。

未移植のTransition、TransitionGroup、FLIP、staggerは`planned`のままであり、全motion完了とは判定しない。
