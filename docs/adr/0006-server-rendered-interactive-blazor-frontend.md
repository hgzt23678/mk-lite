# ADR 0006: Standalone Blazor WebAssembly frontend

## Status

Superseded and replaced on 2026-08-13.

The 2026-08-03 Interactive Server decision is no longer active. The filename remains stable so existing documentation links continue to resolve.

## Context

Interactive Server reproduced the Razor UI but made every interaction, resize callback and stream update depend on a server circuit. Misskey's transition-heavy timeline exposed avoidable latency and required connection affinity during rolling deployment.

The application already exposes Misskey HTTP contracts and a durable PostgreSQL stream event log. A browser-safe RCL can therefore retain the exact Razor DOM, CSS and animation implementation while moving presentation state and rendering into the browser.

## Decision

The production frontend uses standalone .NET 10 Blazor WebAssembly under `/app/`.

- `ActivityPub.Misskey.Blazor` contains only browser-safe Components and contracts.
- `ActivityPub.Misskey.Blazor.Client` owns WebAssembly bootstrap, same-origin HTTP adapters, authentication state and browser WebSocket streaming.
- `ActivityPub.Api` serves the static Client and remains the security and persistence boundary.
- Vue, Vite, Interactive Server, `blazor.web.js` and `/_blazor` are absent from the production path.
- The Interactive Server project remains only as a comparison and component-test oracle.

Authentication uses a Secure HttpOnly session Cookie. The session bootstrap returns a short-lived antiforgery request token that is held in process memory only. Browser storage and URL query strings never contain an access token, refresh token, MiAuth token or Cookie.

The browser obtains an initial durable cursor over authenticated HTTP and multiplexes timeline, notification and relationship subscriptions over one native Misskey WebSocket. PostgreSQL remains the reliable record. Redis notifications may wake readers but cannot replace the log.

## Consequences

API instances no longer need SignalR circuit affinity. Reconnect can land on another instance and resume from a checkpoint cursor. API authorization, visibility filtering and mutation side effects remain server-side.

The first load includes the WebAssembly runtime. Payload size, cold start, memory, CSP (`wasm-unsafe-eval` without general `unsafe-eval`) and Service Worker behavior must be measured in browser tests.

All Presentation services used by Razor must have real HTTP or WebSocket adapters. Missing backend contracts cannot be hidden with empty or constant client implementations.

## Rejected alternatives

- Keeping Interactive Server in production preserves circuit latency and deployment affinity.
- Storing bearer tokens in browser storage weakens the existing session boundary.
- Wrapping Vue or loading it as a microfrontend violates the Vue-removal requirement.
