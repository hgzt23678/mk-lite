# Blazor frontend state

## State ownership

認証viewer、Timeline subscription、compose draft、overlay stackはWASM application scope内でaccount generationごとに分離する。logoutとaccount変更ではsubscriptionとoverlayを閉じ、古いcallbackを破棄する。

投稿、follow、reaction、notification、Deliveryをbrowser stateだけで確定しない。これらの事実はserverのApplication commandがPostgreSQL transactionで確定し、browserはHTTP responseとdurable stream eventから再投影する。

## Bootstrap

初期render前に`/api/frontend/config`を取得し、明示設定されたPublicBaseUri、API base、Authorityを検証する。続いて`/api/frontend/session`からviewerとCSRF contractを取得する。SSR stateやserver DTOをHTMLへ埋め込まない。

session CookieはHttpOnlyのためC#やJavaScriptへ公開しない。CSRF request tokenはWASM memoryとES module closureだけに保持し、reload時は再取得する。

Timelineの初期cursorは`/api/streaming/cursor`から取得し、WebSocket checkpoint受信時だけ再開cursorを更新する。event payloadを受信しただけではcursorを進めない。

## Browser persistence

端末設定は型付きstorage境界を介して保存する。storage keyは制御文字と`token`、`authorization`、`cookie`、`secret`を含む名前を拒否する。

theme、locale、Deck配置、下書きなど上流互換の端末設定だけを保存する。access token、refresh token、MiAuth token、session Cookie、CSRF token、private API response、private media URLはlocalStorage、sessionStorage、IndexedDB、Service Worker cacheへ保存しない。

`lang`は対応localeだけを受理し、`html.lang`と`html.dir`へ反映する。Vue版の旧storage値はversion付きmigrationで読むが、改竄可能なbrowser stateを認証・認可の根拠にしない。
