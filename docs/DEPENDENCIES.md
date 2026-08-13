# Dependency policy and inventory

All NuGet versions are centralized in `Directory.Packages.props`; every project commits a `packages.lock.json`, and CI restores in locked mode. The frontend commits `package-lock.json` and restores with `npm ci --ignore-scripts`. CI inventories direct and transitive licenses, audits NuGet and npm advisories, scans lock files with OSV, generates an SPDX JSON image SBOM, and fails the image scan on High or Critical findings. GitHub Actions are pinned to immutable commit SHAs.

| Dependency family | Version | Purpose | License | Maintenance / replacement boundary |
|---|---:|---|---|---|
| ASP.NET Core / EF Core | 10.0.10 / 10.0.11 | API, auth, Data Protection, persistence | MIT | Microsoft-supported .NET 10 LTS; isolated behind application repositories and auth configuration |
| Npgsql EF provider | 10.0.3 | PostgreSQL provider | PostgreSQL | Active Npgsql project; SQL-specific queue operations are isolated in Persistence |
| StackExchange.Redis | 3.1.3 | delivery/stream wake-up、timeline candidate ID、notification count acceleration | MIT | `IFederationQueueSignal`と`IClientProjectionCache`の背後へ隔離。Redisはoptionalかつdisposableで、PostgreSQLが全durable stateの正本 |
| OpenIddict | 7.6.0 | Mastodon OAuth 2.0 authorization server、reference token、PKCE、revocation | Apache-2.0 | ASP.NET Core/EF adapter内へ隔離。Authorization Code、client credentials、rolling refresh tokenをPostgreSQLへ永続化し、置換時はOAuth endpointとstore interfaceを維持する |
| MailKit / MimeKit | 4.17.0 | password reset・email確認のSMTP送信とmultipart message生成 | MIT | `IPasswordResetEmailSender`と`IEmailConfirmationSender`の背後へ隔離。ProductionはSTARTTLSまたはimplicit TLSを必須とし、SMTP passwordはsecret fileから読む。別providerへ置換してもtoken生成・永続化には影響しない |
| Microsoft.Extensions.Http.Resilience | 10.9.0 | Vault HTTPの短時間resilience | MIT | federation配送retryはDB状態機械が所有し、process-local二重retryをしない |
| NSign | 1.2.4 | RFC 9421 message-signature parsing/signing | MIT | Only the signature adapter depends on it; legacy Cavage and key primitives remain local |
| HtmlSanitizer | 9.2.995 | allow-list HTML sanitization | MIT | Wrapped by `IIncomingHtmlSanitizer`; replaceable without domain changes |
| AWSSDK.S3 | 4.0.102.1 | S3-compatible and Cloudflare R2 object storage | Apache-2.0 | Hidden behind `IMediaObjectStore`; provider-specific endpoint and upload settings remain isolated in Media |
| OpenTelemetry | 1.17.0 | OTLP traces and metrics | Apache-2.0 | Standard OTLP boundary; exporter can be replaced by configuration |
| xUnit / test SDK / coverlet | 2.9.3 / 18.8.1 / 10.0.1 | automated tests and coverage collection | Apache-2.0 / MIT | Test-only |
| Testcontainers PostgreSQL | 4.13.0 | real PostgreSQL integration tests | MIT | Test-only; Docker CLI fixtures are a viable replacement |
| Testcontainers Redis | 4.13.0 | Redis Pub/Sub、cache、fallback integration tests | MIT | Test-only; external disposable Redis can replace it |
| FsCheck.Xunit | 3.3.4 | domain/protocol invariant property tests | BSD-3-Clause | Test-only; generators are confined to `ActivityPub.Property.Tests` |
| SharpFuzz / CommandLine | 2.3.0 | AFL++ coverage-guided fuzz harness and assembly instrumentation | MIT | Tool-only; never loaded by production projects and can be replaced by libFuzzer/Atheris-style harnesses |
| Vue | 3.5.40 | Misskey v12 client runtime | MIT | Frontend-only; domain and federation projects do not reference Vue or Misskey DTOs |
| Vite / Vue plugin / Rollup | 8.2.0 / 6.0.8 / 4.62.4 | Full upstream client production build | MIT | Build-only; emitted static assets have no Vite runtime dependency |
| Vue Macros reactivity transform | 3.1.4 | Compile the v12 reactivity-transform syntax on current Vue | MIT | Compiler-only compatibility boundary; can be removed after a mechanical source migration |
| oidc-client-ts | 3.5.0 | Authorization Code with PKCE, refresh-token renewal, logout | Apache-2.0 | Isolated in `activitypub-auth.ts`; tokens remain in session storage and are sent only to same-origin API calls |
| misskey-js / mfm-js / AiScript | 0.0.14 / 0.23.0 / 0.11.1 | Upstream v12 DTO, MFM and plugin runtime | MIT | Frontend-only; exact v12-facing versions remain replaceable behind `os.api` and renderer boundaries |
| Matter.js | 0.18.0 | `about-misskey`の上流重力演出 | MIT | 固定lockfileから生成したbrowser artifactを、型付き`IAboutMisskeyPhysicsInterop`の背後で必要時だけ読み込む。Vue/Vite runtimeはproductionへ同梱しない |
| broadcast-channel | 7.3.0 | Cross-tab reload coordination | MIT | Upgraded from v12's deprecated transitive cleanup stack; isolated in `unison-reload.ts` and replaceable with the browser BroadcastChannel API |
| Twemoji / Font Awesome | 14.0.2 / 6.1.2 | Emoji and icon assets used by the upstream client | MIT + CC-BY-4.0 / MIT + OFL-1.1 + CC-BY-4.0 | Asset attribution and redistribution terms must accompany releases |
| Vitest | 4.1.10 | Runtime/config/auth adapter tests | MIT | Test-only |
| fediverse-pasture | compose commit `fecd3977`; Mastodon `v4.6.2`、Misskey `2026.6.0`、Pleroma `v2.10.0` | Local federation interoperability environment | compose: MIT; application images retain upstream licenses | Development-only; source and images are not linked into production artifact, versions are isolated in `deploy/pasture/versions.env`, and official instances can replace each node |

The NuGet and npm allow-lists are explicit. `eng/check-licenses.sh` and `eng/check-frontend-licenses.mjs` fail closed for a missing, unknown, or newly introduced license. `escape-regexp@0.0.1` and `misskey-js@0.0.14` omit the npm lock metadata field; the gate accepts only those exact versions after inspecting the MIT declaration shipped in each package archive. An upgrade does not inherit that exception.

The repository is AGPL-3.0-only unless a local license or third-party notice states otherwise. The complete AGPLv3 text and attribution are stored in the root `LICENSE` and `NOTICE.md`; the direct dependency inventory and included standard license texts are indexed by `THIRD_PARTY_NOTICES.md`. Each Misskey-derived frontend source tree also includes its own `LICENSE` and `NOTICE.md`, exact upstream commit, and corresponding-source requirement. Browser-distributed third-party artifacts include their license text under `frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor`, and the deterministic generators verify those copies. License acceptance is not a substitute for legal review before redistribution.

Container base images, including Node 22.23.1 used only by the frontend build stage, are pinned by manifest digest. Ubuntu packages needed for media processing and the container health probe are version-pinned. Updating a digest, OS package, NuGet/npm lock file, or CI action SHA requires the same build, test, license, SBOM, and vulnerability gates.
