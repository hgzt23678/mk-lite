# WASM Streaming / browser state audit

監査日: 2026-08-13
対象commit: `361979363ec9060dde2d1180d1d9d67a121b5947`

この文書は、Interactive Serverで動作していたStreamingとbrowser stateをBlazor
WebAssemblyへ移すために実施した事前監査である。以下の問題一覧と「必要」の記述は、実装判断の履歴として保持する。

## 2026-08-13 実装checkpoint

- `/api/streaming/cursor`と`/streaming?resume=v1&cursor=N`を実装し、最初のsubscription確立前にdurable eventを消費しない。
- payloadにcursorを付与し、同じdurable eventの最後にcheckpointを送る。filterされたeventもcheckpointを進め、Clientはcheckpoint時だけcursorを確定する。
- slow consumer、cursor期限切れ、認証失効を安定したerror／close codeで区別し、Clientはbounded queueをdropせずHTTP再同期と新subscriptionへ回復する。
- Timeline、通知、relationshipは一つのbrowser WebSocketを共有する。query tokenを使わずHttpOnly Cookieで認証し、component破棄時にsubscriptionを解除する。
- native tokenとbrowser sessionはhandshake、heartbeat、event処理前、durable payload送信直前に再検証する。projection中にsecurity stampが変わるTOCTOUでも、失効後のpayloadを送らず4401で終了する。
- resume、checkpoint、filter、slow consumer、native token revoke、Cookie security-stamp revoke、projection raceを含むStreaming統合試験16/16と、実WASM Chromiumのcursor／WebSocket smokeが成功した。

このcheckpointにより、後述するbrowser streaming contractのblockerは現行supported channelについて解消済みである。長時間soak、複数instance rolling deployment、Dolphinに存在しないchannelは別の未完了範囲である。

## 結論

既存のMisskey `/streaming` endpointは、WASMからそのまま接続できるwire形式をすでに
持つ。ただし、現在のfrontend subscription serviceをClient assemblyへ移すことはできず、
HttpOnly session Cookie認証、cursorの配信確認、subscription確立前のevent欠落防止、
token失効時の切断をbackend側で追加しなければ、productionのbrowser streamとしては
使用できない。

最小sliceは、認証不要の`globalTimeline`を対象に、HTTP初期queryが返すcursorから
`/streaming`を再生する経路である。このsliceだけでWASM Streaming全体を完了扱いには
しない。`homeTimeline`、`hybridTimeline`、`main`、notification、relationshipは、
Cookie認証とCSRF境界の完成後に同じ一つのbrowser connectionへ追加する。

## 現行実装の分類

| 対象 | 現在の責務 | WASM分類 | 必要な変更 |
| --- | --- | --- | --- |
| `Streaming/ServerTimelineStream.cs`の`TimelineSubscriptionService` | PostgreSQL stream store/pump、viewer認可、note projectionを直接呼ぶ | server-only、Clientから除外 | browser実装をtyped HTTP clientとbrowser WebSocket clientで新設する。interfaceとmutation DTOはbrowser-safe projectへ移せる |
| `NotificationSubscriptionService` | durable pump、`IAuthenticatedActorContext`、notification projectionを直接呼ぶ | server-only、Clientから除外 | shared `main` channelのnotification eventを受けるbrowser adapterへ置換する。mark-readはCSRF保護したHTTP mutationを使う |
| `RelationshipSubscriptionService` | durable pumpとauthenticated viewerを直接呼ぶ | server-only、Clientから除外 | `main` channelのfollow/unfollowを一つのrelationship storeへ集約する。buttonごとのsocket/pumpを作らない |
| `TimelineView.razor` | 初期query、cursor、sessionStorage、stream consumption、UI更新 | 大部分browser-safe | `PersistentComponentState`を除去し、HTTP response cursorと共有browser streamを使う。component固有generation/cancellationは維持する |
| `MkNotifications.razor` | pagination、filter、stream、visible時mark-read | browser-safe UI | connection ownershipをcomponentからaccount-scoped stream/storeへ移す。cursor expiry/slow consumer時はHTTP再同期する |
| `MkFollowButton.razor` | follow操作と対象ごとのrelationship subscription | browser-safe UI | main channelを共有し、対象IDでstoreを購読する。現在の対象変更generationとdisposeは維持する |
| `MisskeyStreamConnectionStatus` / `MisskeyStreamIndicator.razor` | timelineの切断を単一boolで表示 | browser-safeだが再設計必要 | `Connecting`、`Connected`、`Reconnecting`、`Offline`、`AuthenticationExpired`、`ResyncRequired`を表す。timeline以外の切断も一元化する |
| `PersistentComponentState` | SSR query結果とcursorをcircuitへ一度だけ渡す | standalone WASMでは削除 | browser初回queryを一回だけ実行する。server-only DTOの埋込みや秘密値のbootstrapは行わない |
| `IClientStorage` / `BrowserStorage` | local/session storageのtyped JSON境界 | browser-safe | そのまま再利用できる。cursor keyはinstance、viewer、stream集合へbindし、未来値・壊れた値を信用しない |
| `PizzaxDeviceState` | `pizzax::base`の端末設定 | browser-safe | 再利用できる。複数componentのread-modify-write競合とcross-tab同期は別途schema ownerで直す |
| `IMisskeyIndexedStorage` | IndexedDBとlocalStorage fallback | browser-safeな汎用境界 | token以外のcache/stateに限定する |
| `MisskeyAccountStore` / `MisskeyStoredAccount.Token` | tokenをIndexedDBまたはlocalStorage fallbackへ保存 | 削除 | HttpOnly session方針と矛盾する。WASM production経路から除外し、account switchはserver-managed session識別子だけを扱う |
| `MisskeyLocaleCatalog` | pinned locale catalogと翻訳 | browser-safe | Client assemblyへ置ける |
| `MisskeyLocaleRequestResolver` / localization middleware | `HttpContext`、Accept-Language、cookie初期化 | server-only | document/bootstrap責務としてhostに残す |
| `MisskeyLocalizer` | request resolverから初期localeを取得しscoped stateを持つ | 分割 | state/catalog部分をbrowser用にし、初期localeは検証済みbootstrapまたはallowlist済み`lang`から受ける |
| `MisskeyLocalizationHost.razor` / `localization.js` | storage event、`html.lang/dir`、listener dispose | browser-safe | 再利用できる。`misskey.lang`は非秘密のlocale cookieであり、認証Cookieとは分離する |
| `MisskeyOverlayService` / `OverlayHost.razor` | overlay stack、callback、focus/close順序 | browser-safe | WASMではscoped serviceが実質app lifetimeになる。logout/account switch時の`CloseAll`とgeneration resetを追加する |
| `overlay-stack.js` | page-global focus、z-index、scroll/source lock | browser-safe | page lifetimeのdocument listenerは一回だけ登録し、entryごとのhandle disposeを維持する。Interactive Server固有のpaint-gap workaroundは別試験で削除可否を判断する |

### Browser state lifetime上の注意

- `BrowserStorage`、locale interop、overlay serviceなどの`AddScoped`は、standalone WASMでは
  一つのapp scopeに長時間残る。circuit切断で自動的に破棄されるという前提を除去する。
- logoutとaccount switchでは、socket、subscription、cursor、notification/relationship store、
  overlay callback、compose中のaccount-bound stateを同じgenerationで無効化する。
- theme、locale、device preference、draftのような端末所有stateはaccount sessionより長く残せる。
  token、Cookie、authorization codeはlocalStorage、sessionStorage、IndexedDBへ保存しない。
- 現在の`MisskeyAccountState.AddAccountAsync`は`MisskeyStoredAccount.Token`をIndexedDBへ保存し、
  IndexedDBが使えない場合はlocalStorageへfallbackする。現時点でcall siteはないが、WASM移行時に
  誤って有効化してはならない。

## `/streaming`の現行contract

`src/ActivityPub.MisskeyApi/MisskeyStreamingEndpoints.cs`は次を実装している。

- `GET /streaming`のWebSocket upgrade。
- `?cursor=<long>`。省略時は接続時点のlatest cursor、保持期間外はupgrade前にHTTP 410。
- `connect` / `disconnect`。
- `globalTimeline`、`localTimeline`、認証済み`homeTimeline`、`hybridTimeline`、`main`。
- `pong: true`に対する`connected` acknowledgement。
- Note Captureの`subNote` / `s` / `sr`と`unsubNote` / `un`。
- server eventの`channel` envelopeと、capture eventの`noteUpdated` envelope。
- `main` channelのnotificationとfollow/unfollow projection。
- PostgreSQL connection leaseによるuser/IP単位の接続数制限。
- `WebSocketOptions` keep-aliveとOrigin allowlist。
- durable pumpの有界buffer。slow consumer時にもevent自体はPostgreSQLに残る。
- event送信直前のviewer、visibility、Mute、Block、local-only再検証。

固定Misskey 12.119.2の`misskey-js` 0.0.14も、一つのWebSocket上で同じ
`connect` / `disconnect`とchannel IDを使用し、再接続後にactive channelを再送する。
したがってwire dialectはbrowser接続に流用できる。

### そのままでは使えない点

1. **session Cookieが`/streaming`の認証schemeへ到達しない。**
   `ApiAuthenticationExtensions`はAuthorization headerが空でpathが`/api`または
   `/streaming`の場合、Misskey token handlerを選ぶ。raw browser WebSocketは任意の
   Authorization headerを設定できない。HttpOnly Cookieはhandshakeへ送られても、現在の
   policy selectorではfrontend session schemeに渡されない。WASMから`?i=`へtokenを載せる
   回避策は禁止する。

2. **送信frameにcursorがない。**
   serverは`?cursor=`から再生できるが、clientはどのdurable cursorまで完全に適用したかを
   知れない。現在の`channel` / `noteUpdated` envelopeだけでは安全なresume pointを保存できない。

3. **subscription確立前にeventを消費し得る。**
   endpointはsocket accept直後にpumpの`MoveNextAsync`を開始し、その後で`connect` messageと競争する。
   subscription dictionaryが空の間に読んだeventは送信されず、cursorだけが内部で進む。
   `?cursor=`を追加するだけではquery-to-subscribe gapを閉じられない。

4. **filterされたevent用checkpointがない。**
   viewerに送るpayloadがないeventでもcursorは進むが、clientには進捗が通知されない。
   長時間該当eventがないviewerは古いcursorのままとなり、再接続時に不要な再生やcursor expiryを
   起こし得る。

5. **slow consumerとcursor expiryをWebSocket close contractとして識別できない。**
   pumpの例外は接続を終了させるが、browser clientが`resync`と通常のnetwork failureを区別できる
   close code/messageは定義されていない。upgrade前HTTP 410のstatusもbrowser WebSocket APIからは
   通常取得できない。

6. **接続中のsession/token失効を再検証しない。**
   viewerはupgrade時に一度解決される。heartbeat branchは待機するだけで、session expiration、
   token revocation、account suspensionを再照会していない。`docs/STREAMING_DESIGN.md`の
   「heartbeatごとの失効再検証」は現行コードでは未達である。

7. **reconnect方針が三系統に分裂している。**
   `TimelineView`はfailureを表示して終了、`MkNotifications`はcursor expiry/slow consumer時に
   latestへ飛ばしてreload、`MkFollowButton`は指数backoff+jitterで再接続する。WASMでは一つの
   account-scoped connection ownerへ統合する必要がある。

8. **既存integration testはresumeをMisskey WebSocketで検証していない。**
   `StreamingIntegrationTests`はhome channel、Note Capture、main follow/unfollow、Origin、credential
   conflictを検証するが、cursor resume testはMastodon SSEだけである。Cookie認証、Misskey resume、
   slow-consumer close、token失効、複数instance reconnectも未検証である。

## 必要なbrowser streaming contract

既存Misskey wireを壊さず、opt-inのresume extensionを同じ`/streaming`へ追加する。
別のprocess-local event基盤は作らない。

### Backend extension

- HTTP timeline queryは、query開始直前に取得したlatest durable cursorをresponse header
  （例: `X-ActivityPub-Stream-Cursor`）またはtyped browser responseへ含める。
- `/streaming?cursor=N&resume=v1`は、最初の`connect`を受けるまでpump eventを捨てない。
  最小sliceは一channelだけなので、最初のack後にpumpを開始すればよい。
- reconnect時に複数channelを一括復元する段階では、全subscriptionを一つのmessageで確定する
  batch handshakeを追加する。最初の一channelだけを確定した時点でpumpを進めない。
- payload frameへtop-level cursorを追加し、一つのdurable eventに由来する全payload送信後、
  `checkpoint` frameを送る。payloadがfilterされたeventでもcheckpointだけは送る。
- clientはcheckpoint受信後だけ`lastAppliedCursor`を進める。切断がpayloadとcheckpointの間なら、
  replayされたnote/notificationをIDでupsertし、reaction patch等は`cursor + event identity`でdedupeする。
- slow consumer、cursor expiry、authentication expiryへ安定したclose/error分類を付ける。
  handshake時のexpiryをbrowserが識別できない場合は、同一originのcursor validationまたはHTTP再同期で
  latest/oldestを取り直す。
- session Cookieを明示的なfrontend/browser schemeで認証する。同一origin Origin検査を維持し、
  browser appは`i`、`access_token`、Authorization headerを生成しない。
- 接続中もsession expiration、revocation、suspensionを周期的に再検証し、無効化後のeventを送らず
  socketを閉じる。

### Browser owner

Clientにはaccount scope相当の`BrowserMisskeyStream`を一つだけ置く。

- 一つのphysical WebSocketと、stable channel IDを持つsubscription registryを所有する。
- `globalTimeline` / `localTimeline` / `homeTimeline` / `hybridTimeline` / `main`を同じsocketへmultiplexする。
- receive側に有界`Channel`を置く。満杯ならdropせずsocketを閉じ、最後のcheckpointから回復する。
- reconnectはcancellation可能なexponential backoff + full jitterとする。接続直後ではなく、
  handshake/ack後にattemptをresetする。
- reconnect後はactive subscriptionとNote Captureを全て再送する。
- `cursor <= lastAppliedCursor`を拒否する。ただし同じcursorに複数payloadがあるため、cursorだけで
  payload単位をdedupeしない。checkpoint単位のcommitとstable event identityを併用する。
- page/component disposal、target変更、account switchはgenerationを進め、古いcallbackを無効化する。
- logout/account switchではsocketを先に閉じ、account-bound store/overlayをclearし、新sessionのviewer
  確認後だけ再接続する。
- `sessionStorage`はcrash recovery hintに限定する。serverが返すinstance/viewer/session epochと
  cursor範囲に一致しない値は削除する。初期HTTP queryを飛ばす根拠にはしない。

## Iceshrimp.NETとの差

2026-08-13時点のIceshrimp.NET `dev` branchは、`Microsoft.NET.Sdk.BlazorWebAssembly`、
`Microsoft.AspNetCore.SignalR.Client`、MessagePackを使う。frontendのsingleton
`StreamingService`は`/hubs/streaming`へ接続し、`WithAutomaticReconnect()`と
`WithStatefulReconnect()`を要求する。これは「WASMから一つのtyped connectionを共有する」実例として
参考になる。

ただし、次はこのrepositoryへコピーしない。

- Iceshrimpはuser tokenをlocalStorageの`Users`へ保存し、SignalRの`AccessTokenProvider`へ渡す。
  browser WebSocket/SSEではtokenがquery stringへ流れる。このrepositoryのHttpOnly session方針と
  両立しない。
- Iceshrimp backendのstream stateはprocess-local `ConcurrentDictionary`とevent handlerであり、
  PostgreSQL durable cursorではない。restart、instance変更、buffer保持期間外のresume保証にはならない。
- SignalR stateful reconnectはserver/clientの一時bufferとACKによる短時間のtransport回復であり、
  durable event logの代替ではない。
- Iceshrimpの現行`MapHub<StreamingHub>("/hubs/streaming")`には
  `AllowStatefulReconnects = true`が見当たらない。client側の`WithStatefulReconnect()`だけを、
  server側のdurable resume保証と解釈してはならない。
- default `WithAutomaticReconnect()`は有限回で停止する。production ownerにはinitial start failure、
  long outage、online/offline、account切替を含む明示的policyが必要である。

一次ソース:

- [Iceshrimp.NET Frontend project](https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/branch/dev/Iceshrimp.Frontend/Iceshrimp.Frontend.csproj)
- [Iceshrimp.NET browser StreamingService](https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/branch/dev/Iceshrimp.Frontend/Core/Services/StreamingService.cs)
- [Iceshrimp.NET StreamingHub](https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/branch/dev/Iceshrimp.Backend/SignalR/StreamingHub.cs)
- [Iceshrimp.NET backend Startup](https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/branch/dev/Iceshrimp.Backend/Startup.cs)
- [ASP.NET Core SignalR stateful reconnect](https://learn.microsoft.com/aspnet/core/signalr/configuration?view=aspnetcore-10.0#configure-stateful-reconnect)
- [ASP.NET Core SignalR authentication](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0)

## 最小browser streaming slice

最初の実装sliceは`globalTimeline`だけに限定する。認証不要のため、WASM runtime、HTTP adapter、
WebSocket protocol、cursor/backpressure/reconnectをCookie auth作業と分離して検証できる。

1. `notes/global-timeline`のtyped HTTP clientを作り、初期note一覧とquery直前のdurable cursorを返す。
2. `/streaming`へ`resume=v1` extension、subscription確立待ち、cursor付きpayload、checkpoint、
   slow-consumer/error分類を追加する。既存Misskey clientのframeは壊さない。
3. Clientへ一つの`BrowserMisskeyStream`とbounded receive queueを実装する。tokenは引数にもstorageにも
   持たせず、同一origin `wss://<authority>/streaming`へ接続する。
4. `TimelineView`の`PersistentComponentState`とserver `TimelineSubscriptionService`依存を外し、
   initial HTTP cursorから`globalTimeline`をsubscribeする。
5. note IDでupsert/removeし、checkpoint後だけcursorをcommitする。forced disconnect後に同じeventを
   二重表示しないことを検証する。
6. component disposal、timeline kind変更、page navigationでunsubscribeとgeneration cancellationを確認する。
7. Chromiumで初期query、live note、network切断、reconnect、cursor expiryからのHTTP resync、console error 0を
   focused smokeする。

このsliceの後に、次の順で広げる。

1. frontend session Cookieを`/api`と`/streaming`へ安全に適用するbrowser auth adapter、viewer endpoint、
   JSON mutation用CSRF header/token。
2. `homeTimeline` / `hybridTimeline`とprivate visibility回帰試験。
3. shared `main` channel、notification store、relationship store。
4. Note Capture、reaction/update/delete/poll、account switch、session expiry/revocation。

### 最小sliceの必須試験

- server: subscription ack前のeventが欠落しない。
- server: payload順序の最後に同cursorのcheckpointが来る。
- server: filterされたeventでもcheckpointが進む。
- server: retained cursorから再接続すると以降のeventだけが届く。
- server: slow consumerを識別可能に閉じ、durable eventは再生できる。
- client: bounded queue overflowでdropせずresyncする。
- client: reconnect backoffにjitterがあり、dispose/account generationで停止する。
- client: 同じnote payloadのreplayでDOM itemが重複しない。
- security: browser成果物、storage、URL、logにtoken/Cookie値が存在しない。
- security: Origin不一致を拒否し、private noteをpublic channelへ出さない。

## 完了を妨げる明確なblocker

`globalTimeline`最小sliceには外部blockerはない。authenticated streamingへ進む前には、次を同じ
vertical sliceで解決する必要がある。

- `/api`と`/streaming`がfrontend session Cookieを選択する認証境界。
- Cookie認証したJSON mutationのCSRF contract。
- session/token失効をactive WebSocketへ反映するserver-side再検証。
- cursor付きcheckpointとsubscription batch handshake。

これらを実装せず、WASMへ移したcomponentから`?i=<token>`を生成したり、latest cursorへ無条件に
飛ばして欠落を隠したりしてはならない。
