# Security review — 2026-08-12

## Scope and method

This review covered the authentication and token boundaries, API permission matrices, WebSocket
upgrade boundary, Misskey JSON request parsing, dependency advisories, outbound HTTP/SSRF call
sites, private-media authorization, HTML rendering sinks, and repository secret hygiene. Findings
were treated as confirmed only when the current worktree reproduced the unsafe behavior or the
package resolver reported the advisory.

No production credential values, cookies, tokens, private keys, post bodies, or direct-message
contents were recorded.

## Confirmed findings and applied patches

### SEC-2026-001 — Cross-site login form could issue a session and API token

- Severity: Medium
- Affected route: `POST /api/signin`
- Evidence before the patch: a form-urlencoded request carrying `Origin: https://attacker.example`
  and `Sec-Fetch-Site: cross-site` returned HTTP 200 and issued credentials.
- Cause: the Misskey compatibility route accepted browser-simple form submissions but did not
  validate browser provenance. CORS is not a defense against top-level form submissions.
- Patch: reject cross-site or same-site/cross-origin browser mutations before password validation;
  require an exact configured `Frontend:PublicBaseUri` origin when `Origin` is present; continue to
  support native Misskey clients that do not send browser provenance headers.
- Regression test: `V12SigninRejectsCrossSiteBrowserFormPostsBeforeIssuingCredentials`.
- Compatibility test: `V12SigninAcceptsTheMultipartContractUsedByMkSignin` now exercises an
  accepted same-origin browser request.

### SEC-2026-002 — Sign-in response disclosed whether a username existed

- Severity: Low
- Affected route: `POST /api/signin`
- Evidence before the patch: a known username with a bad password returned HTTP 403 and error id
  `932c904e-9460-45b7-9ce6-7ed33be7eb2c`; an unknown username returned HTTP 404 and a different id.
- Patch: both cases now return the same HTTP status and response body.
- Regression test: `V12SigninDoesNotRevealWhetherAnInvalidUsernameExists`.
- Residual risk: database lookup and password hashing still have different timing profiles. See
  the follow-up proposal below.

### SEC-2026-003 — Chunked Misskey JSON requests bypassed the intended body limit

- Severity: Medium
- Affected routes: JSON endpoints under `/api`.
- Evidence before the patch: a valid, unknown-length request larger than 2,000,000 bytes reached
  `POST /api/users/show` and returned HTTP 200.
- Cause: the authentication handler checked `Content-Length`, but parsed an unbounded stream when
  the header was absent; endpoint JSON readers were also unbounded relative to the 100 MiB global
  media-upload limit.
- Patch: both token extraction and endpoint deserialization count the bytes actually read and stop
  at 2,000,000 bytes. Oversized endpoint input returns HTTP 413. The Drive multipart media path is
  unaffected and remains governed by the media service limit.
- Regression test: `MisskeyJsonApiRejectsAnUnknownLengthBodyAboveTheJsonLimit`.

### SEC-2026-004 — Vulnerable transitive SSH.NET package stopped the security gate

- Severity: High advisory; test/build-time exposure in this repository
- Advisory: [`GHSA-q939-rpr3-3284` / `CVE-2026-48798`](https://github.com/advisories/GHSA-q939-rpr3-3284)
- Evidence before the patch: NuGet resolved `SSH.NET 2025.1.0` through Testcontainers and emitted
  `NU1903` as an error.
- Patch: central transitive pinning now selects `SSH.NET 2026.0.0`, the first patched release.
  Package lock files were regenerated; the vulnerability scan reports no vulnerable .NET package.
- Exposure note: no product code path using `ScpClient.Download` was found, but leaving a known
  high-severity dependency in CI/test tooling was not accepted.

### SEC-2026-005 — Authentication capability hint disclosed MFA and passkey enrollment

- Severity: Low
- Affected route: `GET /auth/user-hint`
- Patch: the route and the browser-side username probe were removed. The sign-in form always asks
  for a password. A browser receives full WebAuthn request options only after the password has
  passed Identity validation; native Misskey clients retain their post-password v12 challenge
  shape. No avatar, TOTP, passkey, or passwordless state is fetched before authentication.
- Regression tests: `FrontendDoesNotExposePreAuthenticationMfaOrPasskeyHints`,
  `V12SigninPasskeyChallengeUsesMisskeyShapeAndRejectsReplayableMalformedAssertion`, and the
  Chromium security-key login tests.

### SEC-2026-006 — A redeemable MiAuth session credential was stored in the audit log

- Severity: Medium
- Affected operation: MiAuth token issue and `POST /api/miauth/{session}/check`.
- Evidence before the patch: the `misskey-auth/token-issued` audit JSON contained the raw MiAuth
  session UUID while that UUID could still be exchanged once for the protected access token. An
  operator or system with read access to audit events could race the intended client.
- Patch: retain the immutable internal authentication-session row id for correlation and never
  serialize the externally redeemable session key into the audit event. The access token remains
  hashed at rest and the encrypted one-time token is cleared on successful redemption.
- Regression test: `MiAuthTokenIsHashedConsumedOnceAndAuthenticatesJsonBody` now asserts that the
  audit event contains `sessionId` and does not contain the redeemable session value.

### SEC-2026-007 — Browser WebSocket upgrades accepted arbitrary origins

- Severity: Low
- Affected routes: Misskey `/streaming`, Mastodon WebSocket streaming, and the application-wide
  WebSocket middleware boundary.
- Evidence before the patch: a TestServer browser-style upgrade with
  `Origin: https://attacker.example` completed successfully.
- Patch: build an explicit origin set from the immutable federation origin, enabled frontend
  origin, and configured CORS origins. Requests carrying a malformed, multiple, opaque, or
  unconfigured Origin are rejected with HTTP 403 before reaching a streaming endpoint. Native
  clients that omit Origin remain supported.
- Reference: [ASP.NET Core WebSocket origin restriction](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0#websocket-origin-restriction).
- Regression tests: `BrowserWebSocketRejectsAnUnconfiguredCrossSiteOrigin` and
  `BrowserWebSocketAcceptsTheConfiguredFrontendOrigin`.

### SEC-2026-008 — Scoped API tokens could cross permission and protocol boundaries

- Severity: High
- Affected operations: Misskey profile/announcement/Drive mutations, Mastodon favourites, and
  cross-dialect Mastodon or raw ActivityPub operations authenticated with a Misskey token.
- Evidence before the patch:
  - a token granted only `read:account` received HTTP 200 from `POST /api/i/update` and could also
    reach Drive operations;
  - any Misskey `write:*` permission was projected as `activitypub.write`, so a `write:notes`
    token satisfied Mastodon Follow, Mute, Block, and Favourite policies;
  - Mastodon Favourite used the broad status-write policy rather than Mastodon 4.6.2's
    `write:favourites` scope.
- Patch:
  - apply exact `write:account`, `read:drive`, and `write:drive` policies to every implemented
    Misskey endpoint according to the pinned 12.119.2 endpoint metadata;
  - map cross-dialect operations explicitly (`write:notes` to Mastodon status mutation,
    `write:reactions` to Favourite, and matching notification/follow/mute/block permissions);
  - stop minting synthetic generic ActivityPub scope claims for Misskey credentials and reject
    Misskey authentication at the raw Client-to-Server ActivityPub policy boundary;
  - require Mastodon `write:favourites` for Favourite/Unfavourite while retaining generic
    Mastodon `write` compatibility.
- Regression tests: `AccountAndDriveEndpointsRequireTheirExactMisskeyPermissions`,
  `MastodonFavouriteDoesNotAcceptWriteStatusesScope`,
  `MisskeyAccountAndDrivePoliciesRequireExactPermissions`, and
  `MisskeyTokenPermissionsDoNotBecomeBroadMastodonOrActivityPubScopes`.

### SEC-2026-009 — Unknown usernames bypassed password hash verification

- Severity: Low
- Affected operations: local password authentication, passkey challenge initiation, and
  `POST /api/signin`.
- Cause: a missing Identity user returned immediately after lookup, while a known user executed
  the configured password hasher.
- Patch: a process-local synthetic account/hash is created with the active ASP.NET Core Identity
  `PasswordHasherOptions`; every valid unknown-user attempt verifies the supplied password against
  that hash. The Misskey endpoint no longer returns before reaching that path.
- Regression test: `UnknownUserSigninExecutesConfiguredPasswordVerificationWork`. The existing
  `V12SigninDoesNotRevealWhetherAnInvalidUsernameExists` test continues to require identical HTTP
  status and response bodies.
- Scope note: this equalizes password-hash work, not every database or network scheduling effect;
  statistical wall-clock assertions are deliberately not used as a correctness test.

### SEC-2026-010 — Registration UI exposed persisted email availability

- Severity: Medium when public registration is enabled
- Affected operations: `GET /auth/email-address-available`,
  `POST /api/email-address/available`, and the sign-up email input.
- Patch: public availability contracts now validate syntax only and return byte-identical results
  for registered and unregistered valid addresses. The browser performs local email syntax
  validation and makes no address-availability request. Duplicate email failures from the Blazor
  registration endpoint are projected as the generic `REGISTRATION_FAILED` code; PostgreSQL's
  unique constraint remains authoritative.
- Regression test: `EmailAvailabilityValidatesSyntaxWithoutDisclosingPersistedAddresses`.

### SEC-2026-011 — External identifier sequence query composed SQL text

- Severity: Low; the interpolated value was selected from an internal enum, not attacker input
- Affected operation: allocation of Mastodon and Misskey compatibility identifiers.
- Patch: `nextval` receives the allow-listed sequence name through a database parameter cast to
  `regclass`; no value is concatenated into command text.
- Preventive gate: `SqlInjectionGuardTests.ProductionSourceDoesNotUseRawOrDynamicallyComposedSql`
  rejects EF Core raw SQL APIs, dynamic `CommandText`, and dynamically supplied `FromSql` strings
  across production source. Existing interpolated `FromSql(FormattableString)` queries remain
  parameterized by EF Core.

## Follow-up patch proposals

The authentication enumeration and SQL-composition items identified in the first review pass are
now represented as fixed above. A future registration-hardening slice should still evaluate a
fully generic confirmation workflow for deployments that permit anonymous public registration;
the present patch removes the explicit availability oracle without claiming that all registration
timing and side-effect differences are indistinguishable.

## Verification

- `dotnet restore ActivityPubServer.slnx --force-evaluate`: passed.
- `.NET vulnerable package scan`: 0 after pinning.
- production dependency audits for both npm workspaces: 0 vulnerabilities.
- full npm audits for both npm workspaces: 0 vulnerabilities.
- `dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore`: passed.
- `dotnet build ActivityPubServer.slnx --configuration Release --no-restore`: passed with 0
  warnings and 0 errors.
- `dotnet test ActivityPubServer.slnx --configuration Release --no-build --no-restore`: 913 passed,
  0 failed, 0 skipped.
- focused authentication, permission, and WebSocket regression tests: passed.
- focused Chromium authentication/sign-up/passkey smoke: 11 passed, including assertions that
  username input performs no MFA/passkey hint request and valid email input performs no persisted
  availability request.
- secret-pattern filename/content scan: no private-key or provider-token pattern detected outside
  excluded build artifacts; `.env` is ignored and mode 0600.

This is a focused review, not a claim that the repository is vulnerability-free. The shared safe
federation HTTP client and private-media authorization path were inspected and their existing SSRF,
redirect, policy, and recipient checks passed the full suite; that is not a substitute for future
network-level or parser-specific adversarial and failure-injection testing. Federation signatures,
media decoders, authorization matrices, and operational secret stores should remain recurring
review targets.
