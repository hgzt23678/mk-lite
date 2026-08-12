# Blazor frontend architecture

## Runtime

本番frontendはASP.NET Core host内のRazor Componentsとして実行する。

`App.razor`がstatic SSRのHTML documentを生成し、`Routes.razor`以下をInteractive Server render modeで動かす。

外部pathは`/app/`であり、component routeは`UsePathBase("/app")`の内側で評価する。

framework boot scriptは`blazor.web.js`であり、`blazor.webassembly.js`とWebAssembly runtimeを配信しない。

## Dependency direction

移植基準はfrontendとbackendで分離する。RazorのDOM、class、CSS、motion、responsive挙動はMisskey v12.119.2をoracleとし、Applicationから下のAPI、永続化、連合、モデレーション、メディア、キュー挙動は`mei23/dolphin`をbackend baselineとする。画面が表示できても、Dolphin基準の永続副作用や認可を満たさなければ実装済みとは判定しない。

依存方向は次のとおりである。

```text
Razor Component
  -> Presentation service and scoped UI state
  -> Application query/command contract
  -> Domain
  -> PostgreSQL, Federation, Media
```

Misskey HTTP adapterとRazor Componentsは同じApplication contractを使う。

一方のadapterがもう一方のendpointをHTTPで呼ぶ構成にはしない。

## Authentication

外部OIDC loginはAuthorization CodeとPKCE S256を使用し、callbackは`/app/auth/callback`へ固定する。

認証結果はHttpOnly、Secure、SameSite=Laxのserver session cookieへ保存する。

Razor Componentsは`AuthenticationStateProvider`からusernameを得た後、DB上のlocal Actorへ解決する。

OIDCの`actor` claimだけをlocal Actorの根拠として信用しない。

## Durable streaming

Timelineは初期queryの直前にPostgreSQL stream event logの最新cursorを取得する。

初期query後は、そのcursorより新しいeventだけを購読するため、queryとsubscriptionの間に発生した更新を失わない。

各eventはviewer、Mute、Silence、visibility、local-only条件を再検証してから表示modelへ変換する。

購読bufferが上限を超えた場合は接続を閉じ、保存済みcursorから回復する。

## JavaScript boundary

JavaScriptはbrowser APIと、同等性を独自再実装できない固定version parser/engineの薄い型付きadapterに限定する。

現在の境界はstorage、Service Worker登録、ResizeObserver、IntersectionObserver、page header計測、popup/focus、animation frame、MFM parser、Matter.js物理演算である。

Vue lifecycle、Vue Router、Pizzax、component renderingをJavaScriptへ残さない。

各moduleは`IJSObjectReference`をscoped serviceが所有し、observer、event listener、timer、animation frame、Matter.js world/engineをcomponentまたはcircuit終了時にdisposeする。
