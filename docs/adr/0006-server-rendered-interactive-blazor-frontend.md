# ADR 0006: Server-rendered interactive Blazor frontend

## Status

Accepted on 2026-08-03.

This decision supersedes the earlier WebAssembly frontend direction.

## Context

Misskey 12.119.2 の画面をBlazorへ移植する方式として、WebAssemblyとサーバー実行の二案があった。

運用者は、ブラウザーへ.NET runtimeとAPI access tokenを配るWebAssembly方式を採用せず、ASP.NET Coreが描画とUIイベント処理を担う方式を指定した。

一方、投稿、配送、通知、Streaming cursorをBlazor circuitだけに保存すると、process障害とrolling deploymentで状態を失う。

そのため、UIの実行場所と信頼できる記録の保存場所を分ける必要がある。

## Decision

本番frontendは、.NET 10 Blazor Web Appのstatic SSRとglobal Interactive Server renderingを使用する。

Razor ComponentsはRazor Class Libraryへ置き、ASP.NET Core API hostが同一originの`/app/`で配信する。

初期HTMLはserverで描画し、`PersistentComponentState`で初期query結果をinteractive circuitへ引き渡す。

この引き渡しにより、prerenderとinteractive初期化が同じDB queryや副作用を二重実行する状態を避ける。

認証は既存OIDC境界とHttpOnly session cookieを使用する。

ブラウザーへaccess tokenとrefresh tokenを保存せず、変更操作にはBlazorの接続認証とASP.NET Core antiforgery境界を適用する。

UI commandは共通Application serviceを一度だけ呼び、Object、Activity、Deliveryを既存transactionで確定する。

UI更新はPostgreSQLのdurable stream event logを各circuitがcursor付きで購読する。

LISTEN/NOTIFYやBlazor SignalRは通知と表示の輸送に限り、失われてはならないeventの記録にはしない。

Deck配置、theme、下書きなどの端末設定は型付きJavaScript moduleを介してbrowser storageへ保存する。

token、Cookie、client secretをbrowser storageのkeyまたはvalueとして新規保存しない。

Vue runtime、Vue Router、Pizzax、Vue SFCはproduction imageと通常起動経路へ含めない。

固定したVue版は移行中のvisual oracleとbehavior oracleとして別のdevelopment gateに残す。

## Scaling and deployment consequences

Interactive Serverのcircuitはprocess-localなので、load balancerはWebSocket接続中のaffinityを維持する。

affinityは永続性の根拠ではない。

投稿、通知、配送、Streaming eventとcursor recoveryに必要な事実はPostgreSQLへ保存するため、切断後は別instanceで再接続できる。

rolling deploymentは、新規接続を新instanceへ送り、旧instanceのcircuitをgraceful shutdown期間内にdrainする。

drain期限を超えたcircuitは再接続し、保存済みcursor以降を再取得する。

processをまたぐ同一circuitのlive migrationは対応範囲に含めない。

外部SignalR serviceまたはRedis backplaneは必須にしない。

将来導入する場合も、PostgreSQL event logの代替にはせず、fan-outの高速化だけに使用する。

## Security consequences

serverは利用者ごとの表示modelとoverlay状態をscoped serviceに分離し、singletonへ格納しない。

private Objectは初期queryとstream eventの両方で同じviewer authorizationを通す。

切断circuitの保持時間、未確認render batch、SignalR受信message size、並列invocation数には上限を設定する。

Service Workerはnavigation、API response、media、認証情報をcacheしない。

## Rejected alternatives

Blazor WebAssemblyは運用者の実行方式指定に反するため採用しない。

Vueをiframeまたはmicrofrontendとして残す方式は、Vue runtimeを本番経路から除く完成条件を満たさないため採用しない。

Blazor Serverのcomponentから自分自身のMisskey HTTP endpointを呼ぶ方式は、token転送と二重adapterを生むため採用しない。

Razor Componentsは共有Application contractを直接使用し、HTTP固有のserializationとerror bodyはMisskey API adapterに残す。
