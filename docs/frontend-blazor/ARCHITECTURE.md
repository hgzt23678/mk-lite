# Blazor frontend architecture

## Runtime

本番frontendは.NET 10のstandalone Blazor WebAssemblyである。

ASP.NET Core API hostは同一originの`/app/`へ静的shell、Razor Class LibraryのCSS・JavaScript、`_framework`成果物を配信する。deep linkは`/app/{*path:nonfile}`から同じshellへ戻し、その後のroute、history、overlay、端末状態はbrowser内で処理する。

本番HTMLは`blazor.webassembly.js`を使用する。Interactive Server、server circuit、`blazor.web.js`、`/_blazor`、Vue、Viteは製品経路へ含めない。旧Interactive Server hostは比較用TestHostへ隔離する。

## Project boundary

```text
ActivityPub.Misskey.Blazor
  browser-safe Razor Components / Presentation contracts / CSS / JS modules
        ^
ActivityPub.Misskey.Blazor.Client
  WebAssembly bootstrap / HTTP adapters / browser streaming / auth state

ActivityPub.Api
  static WASM hosting / HTTP and WebSocket endpoints
        -> ActivityPub.MisskeyApi -> Application -> Domain -> Persistence
```

WASM ClientとRCLは`ActivityPub.MisskeyApi`、Application、Domain、Persistence、Identity、EF Core、Npgsqlを参照しない。browserは同一originのMisskey HTTP契約と`/streaming`だけを使用する。Mastodon adapterを経由しない。

## Authentication and request integrity

認証の正本はSecure、HttpOnly、SameSite=Laxのserver session Cookieである。WASM起動時に`GET /api/frontend/config`を読み、PublicBaseUri、API base、Authorityを明示設定から取得する。Host headerや現在位置から公開IRIやAuthorityを生成しない。現在位置のoriginは同一origin transportの検証だけに使う。

`GET /api/frontend/session`は認証viewerとantiforgery contractを`no-store`で返す。request tokenはC# memoryとJavaScript module closureだけに保持し、localStorage、sessionStorage、IndexedDB、URL、logへ保存しない。unsafeなCookie requestは`X-ActivityPub-Frontend: 1`とantiforgery headerを必須にする。

## Durable streaming

browserは単一WebSocket上でtimeline、notification、relationship channelを多重化する。初期cursorは`POST /api/streaming/cursor`で取得する。server payloadのcursorはcheckpoint受信時だけ永続的な再開位置として採用し、reconnectは指数backoffとjitterを使用する。

PostgreSQLのstream event logが信頼できる記録であり、RedisとLISTEN/NOTIFYはwake-up用途に限定する。tokenまたはCookie sessionはhandshakeだけでなくheartbeatとpayload送信前にも再検証する。

## JavaScript boundary

JavaScriptはbrowser APIと、同等性を独自再実装できない固定version parserの型付きadapterに限定する。storage、Service Worker、ResizeObserver、IntersectionObserver、animation frame、MFM parser、overlay、media操作が該当する。

Vue lifecycle、Vue Router、Pizzax、component renderingをJavaScriptへ残さない。`IJSObjectReference`、`DotNetObjectReference`、observer、listener、timer、Blob URLはcomponent破棄時に解放する。
