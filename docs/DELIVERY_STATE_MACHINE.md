# Delivery state machine

## States

```text
Pending -> Leased -> Succeeded
   ^         |          
   |         +-> Pending (retryable failure)
   |         +-> DeadLettered (terminal or exhausted)
   |         +-> Cancelled
   +------------ lease expired
```

## Invariants

- A database transaction creates the local activity and all known delivery records atomically.
- A lease has an owner, acquisition time, and expiry time.
- Only the current lease owner may complete or reschedule a delivery.
- A crashed worker cannot hold a delivery after lease expiry.
- Each attempt is recorded before the lease is released.
- Success is terminal and cannot be replayed without an audited operator action.
- Dead-letter replay creates a new pending execution while retaining the original history.
- Retry delay uses exponential backoff with full jitter and honors a bounded `Retry-After` value.
- Domain concurrency and circuit state prevent retries from amplifying an outage.

## Status classification

2xx succeeds. 408, 425, 429, 5xx, timeouts, and network failures retry. 404 and 410 mark the cached endpoint gone and trigger actor rediscovery. 401 and 403 trigger one key、signature profile、block-state recheck and actor rediscovery. Other 4xx responses are terminal unless a documented interoperability rule overrides them.

## Endpoint replacement

Actor rediscovery が新しい inbox または sharedInbox を返した場合、Worker は現在失敗中の Delivery の endpoint を同じ attempt transaction 内で置き換える。

同じ Activity と新 endpoint を持つ active Delivery が既に存在する場合、既存 Delivery の target を現在の Delivery へ merge し、既存 Delivery を cancel する。

一つの Delivery に含まれた Actor が複数の新 endpoint へ分かれた場合、現在の Delivery を一つの endpoint に更新し、残りを新しい Delivery へ split する。

`DeliveryEndpointChange` は old endpoint、新 endpoint、reason、時刻を保持する。

成功済み target は再作成せず、active Delivery だけを `activity_id` と `endpoint_iri` の partial unique index で一意化する。
