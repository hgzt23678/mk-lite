# 配送障害、誤配送、domain障害

## 緊急の全配送停止

1. admin tokenを安全に取得する。
2. `PUT /admin/operations/outbound-delivery`へ`{"paused":true,"reason":"INC-..."}`を送る。
3. GETで永続pauseを確認し、active leaseが完了または期限切れになるまで監視する。必要ならnetwork egressも遮断する。
4. 誤配送の場合は対象Activity、recipient、既送信attempt、未送信jobを固定し、対象範囲を保全する。remote削除は相手が保持していない保証にはならない。

未送信jobを再開不能な`Cancelled`へ移す必要がある場合は、pause確認後に`POST /admin/operations/domains/{domain}/cancel-deliveries`へ`{"reason":"INC-..."}`を送る。既にHTTP送信中のleaseは取り消せないため、即時停止にはegress遮断も併用する。取消件数と理由はauditへ残る。

## Domain単位停止

`POST /admin/domain-policies`で`kind: "PauseOutbound"`、domain、incident reason、必要ならexpiryを登録する。queue age、status分布、DNS/TLS、Retry-After、署名profile、circuit stateを調べる。復旧後はpolicy IDを`DELETE /admin/domain-policies/{id}`でrevokeし、少数canaryを観察してから並列度を戻す。

## 再開

原因とidempotency影響を確認し、Dead Letterを一括再送しない。global pauseは同endpointへ`paused:false`と復旧理由を送る。success/retry/401/403/429/5xx、queue oldest、remote latencyを30分以上監視する。操作はhash-chained audit eventへ記録される。
