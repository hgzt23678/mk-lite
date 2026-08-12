# Streaming設計

Mastodon StreamingとMisskey Streamingはwire形式だけを分け、PostgreSQLの`stream_events`を共通の信頼できる記録として使う。

## 不変条件

- Domain状態を変更したtransactionがcommitしなければeventも存在しない。
- 同じActivityの再処理では`deduplication_key`一意制約によりeventを重複生成しない。
- event payloadへ投稿本文、access token、Cookieを保存しない。
- eventは内部resource IDだけを参照し、送信直前にviewer権限と現在のMute、Block、Silenceを再検証する。
- 接続ごとのcursorは単調増加し、再接続時は最後に確認したcursorより後だけを読む。
- PostgreSQLの通知を導入してもwake-up用途に限定し、pollによるcursor回復を止めない。
- 複数API instanceはprocess-local共有状態を必要とせず、同じDB cursorから独立して回復できる。

## Queue処理checklist

| 項目 | 方針 |
| --- | --- |
| 冪等性 | `deduplication_key`をDBで一意にし、Activity受信再送とAPIのIdempotency-Key再実行からeventを一件だけ作る |
| retry | client再接続とserver pollをretry境界とし、同じcursorをack済みとして飛ばさない |
| visibility timeout | push専用leaseは使わず、clientが保持するcursorから再読する |
| poison event | projection不能eventは本文を記録せずevent IDとerror分類を計測し、接続を終了して同じcursorを再現可能にする |
| dead letter | Domain event自体を捨てず、管理対象のprojection failureとして記録する |
| lag | 最新cursorと接続cursorの差、最古未配信時刻、poll latencyを計測する |
| slow consumer | 有界bufferを超えた接続を明示的に切断し、再接続cursorから回復させる |
| retention | 保持期間より古いcursorは暗黙に飛ばさず、cursor expired errorと再同期を要求する |

## Wire adapter

Mastodon SSEは`id`、`event`、`data`を出力し、WebSocketは同じeventをMastodonのJSON envelopeへ変換する。

Misskey WebSocketは`connect`、`disconnect`、Note Captureを接続内subscription状態として扱い、Domain eventをchannel固有messageへ変換する。

subscription状態は再接続で失われるため、clientは再接続後に再送する。

## 認証

Mastodon access tokenとMisskeyの`i`は接続確立前に通常の認証handlerで検証する。

WebSocket query tokenは認証より前にAuthorization headerへ移し、QueryStringから除去する。

接続中はheartbeatごとにtokenの期限と失効状態を再検証し、失効後のeventを送らない。

## 完了判定

SSE、両WebSocket、resume、slow consumer、失効、visibility、複数instanceの統合試験が揃うまではStreaming互換を宣言しない。
