# アクセシビリティ移植

## 判定範囲

アクセシビリティ適合は、画面が表示されたことだけでは判定しない。

各componentについて、semantic HTML、accessible name、keyboard操作、visible focus、focus trap、focus復帰、Escape、live region、zoom 200%、reduced motion、touch target、form errorの関連付けを個別に検証する。

現在は共通primitive、認証dialog、popup、modal window、Visitor shellの一部だけが試験対象である。

全routeやscreen reader実機を検証した状態ではないため、フロントエンド全体の適合は宣言しない。

## 固定版からの意図的な差分

固定版の見た目と操作意図を維持しながら、支援技術へ必要な属性だけを追加する。

`MkLoading`は固定版と同じ二つのSVG、CSS Modules class、寸法、色、spinner timingを使用する。

固定版には読み上げ可能な状態名がないため、Blazor版のrootへ`role="status"`、`aria-label`、`aria-busy="true"`を追加し、装飾SVGを`aria-hidden="true"`にした。

`MkEllipsis`は周囲の「待機中」などの文だけを読み上げるため、三つの装飾dotを`aria-hidden="true"`にした。

`MkVisibility`のspecified iconは固定版のspanとicon階層、CSS Modules class、配置を維持したまま、`role="button"`、`tabindex="0"`、accessible name、`aria-haspopup`、`aria-expanded`、`aria-describedby`を追加した。hoverとtouchに加えてEnter、Space、Escape、focusとblurでも同じtooltip stateを操作し、Escape後も起点spanへfocusを保持する。装飾iconは`aria-hidden="true"`とした。

`MkUpdated`は固定版のDOM階層とclassを変えず、rootへ`role="dialog"`、`aria-modal="true"`、localized accessible nameを追加した。固定版の`MkModal`は`esc`をemitするが`MkUpdated.vue`側が購読していないため閉じない。移植要件のkeyboard契約に従い、Blazor版は最前面のときだけEscapeで閉じ、Tabを二つのbutton内へ封じ、leave完了後に起点へfocusを戻す。この差分は見た目と通常motionを変更しないアクセシビリティ修正として記録する。

これらの属性はDOM階層、class、layout、通常motionを変更しない。

## Motion

通常設定では固定版のduration、delay、easing、iterationを維持する。

`prefers-reduced-motion: reduce`では、非本質的なanimationとtransitionを1回かつ最短時間へ短縮する。

静止画試験のためにanimationを無効化する設定を、本番CSSへ流用しない。

## Overlay

dialog、popup、menuはoverlay stackの最前面だけがkeyboardとpointer入力を受け取る。

開いたときにfocusを内部へ移し、Escapeまたはclose後は起点要素へ戻す。

背面overlayは`inert`相当の入力隔離とscroll lockを受ける。

連続した開閉やenter途中の取消でも、古いcallbackが新しいoverlayを閉じないことを3 browserで検証する。

## 未検証項目

- 全115 routeのheading順序とlandmark。
- Chromium、Firefox、WebKitとscreen readerの組み合わせ。
- 全localeのaccessible nameと複数形。
- 全formのerror summaryとfield関連付け。
- 360×800から1920×1080までのtouch targetとzoom 200%。
- Deck、media editor、AiScript、admin画面のkeyboard操作。
- WCAG contrastの全theme自動検査。

未検証項目はsource mapping上で`implemented`へ変更しない。
