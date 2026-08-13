# ADR 0009: PostgreSQL queue and optional Redis acceleration

## Status

Accepted.

## Context

Dolphin uses Bull/Redis for federation jobs and Redis Pub/Sub for client streams, while notifications and notes remain relational records. This server already requires stronger crash recovery and transactional-outbox guarantees: a local mutation, Activity, recipients, and deliveries must commit atomically, and a broker outage must not lose work.

## Decision

PostgreSQL remains the sole authoritative store for Inbox items, outbound deliveries, attempts, leases, retry schedule, dead letters, notes, notifications, and durable stream cursors. Workers claim rows with `FOR UPDATE SKIP LOCKED`; expired leases are recoverable and retry/dead-letter transitions remain database constrained.

Redis is optional and is used only for:

- Pub/Sub wake-up of delivery workers after a committed PostgreSQL delivery is added or replayed;
- existing durable-stream wake-up, after which each consumer resumes from PostgreSQL by cursor;
- short-lived timeline candidate-ID caches;
- short-lived unread-notification count caches.

Timeline cache entries contain UUIDs only. A hit is reloaded from PostgreSQL and current deletion, visibility, follow, mute, block, silence, and actor-policy rules are applied before projection. Notification caches contain only a count and are invalidated after committed notification mutations. Actor IRIs are SHA-256-derived key segments rather than Redis key plaintext. Redis errors and cache misses fall back to PostgreSQL polling and queries.

## Consequences

Redis improves hot-path latency and multi-instance wake-up but is not required for correctness. Flushing Redis may increase database load and polling latency, but cannot remove a delivery, expose a private object, lose a notification, or invalidate a stream cursor. Operators must scale PostgreSQL for the durable workload and treat Redis memory as disposable.
