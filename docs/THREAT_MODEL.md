# Threat model

## Protected assets

- Local actor identities and private keys
- Private and mentioned-only content
- Follow graph and moderation state
- PostgreSQL data and durable delivery records
- Object storage and media access tokens
- Administrative credentials and audit records
- Service availability and outbound network authority

## Trust boundaries

Every remote actor, domain, inbox payload, URL, key document, media document, JSON-LD extension, and HTTP header is untrusted. Reverse proxies are untrusted unless explicitly listed. PostgreSQL, the configured key store, and object storage are privileged dependencies.

## Principal threats and controls

| Threat | Required controls |
| --- | --- |
| Activity spoofing | exact-body digest validation, HTTP signature validation, key-owner and actor binding, origin ownership checks |
| Replay and duplicate side effects | unique activity IRI, payload hash comparison, replay records, idempotent handlers, database constraints |
| SSRF and DNS rebinding | HTTPS-only production policy, address classification, connect-to-validated-IP, redirect revalidation, egress firewall |
| Private-content disclosure | normalized audience, dereference authorization, signed GET, cache separation, log redaction |
| Delivery amplification | recipient deduplication, shared inbox aggregation, recursion limits, per-domain concurrency and circuit breakers |
| Resource exhaustion | body limits, decompression limits, timeouts, rate limits, bounded JSON depth, bounded remote fetches |
| Malicious HTML and media | allow-list sanitizer, URI-scheme filtering, MIME sniffing, metadata removal, quarantine and scanning |
| Key compromise | external encrypted key store, rotation overlap, revocation records, audit trail, compromise runbook |
| Operator abuse | separate admin authorization, append-only hash-chained audit events, reason and expiry requirements |
| Poison work item | bounded attempts, terminal dead-letter state, inspection and audited replay |

## Explicit non-assumptions

TLS alone does not authenticate an Activity actor. A valid HTTP signature alone does not authorize mutation of an object. A successful delivery response does not prove that a remote server retained or deleted content.

## Residual risks requiring deployment controls

- Application-level SSRF validation cannot replace egress firewalling or protect a compromised process.
- Legacy HTTP Signature behavior varies between implementations; exact peer-version interoperability evidence is still required.
- Hash chaining makes audit deletion/reordering detectable only when chain heads are exported to an independently protected store.
- Remote recipients may copy private content or ignore Delete; protocol delivery cannot provide remote erasure guarantees.
- A compromised OIDC issuer, Vault token, PostgreSQL superuser, or reverse proxy remains a privileged compromise path.
- Limit/Silence/RejectMedia semantics and remote media proxying are not complete, so those policies must not be represented to operators as enforced.
