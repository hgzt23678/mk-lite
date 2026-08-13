# Cloudflare deployment

Cloudflare support is optional. PostgreSQL, the ActivityPub delivery queues, authentication, private-media authorization, and SSRF policy remain application responsibilities when Cloudflare is enabled.

## Turnstile registration protection

Turnstile is available on the fixed Misskey v12 signup form. It occupies the same `_formBlock captcha` position as the upstream CAPTCHA component; no separate login widget or replacement authentication screen is introduced. The fixed v12/Dolphin login contract has no CAPTCHA field, so login abuse remains protected by the existing IP rate limit and account lockout rather than an invented UI extension.

First enable a real registration path (`LocalAccounts:RegistrationEnabled=true` or `RegistrationProtection:InvitationRequired=true`), then configure:

```json
{
  "RegistrationProtection": {
    "InvitationRequired": true,
    "CaptchaProvider": "Turnstile",
    "CaptchaSiteKey": "replace-with-the-public-widget-site-key",
    "CaptchaSecretFile": "/run/secrets/cloudflare-turnstile-secret",
    "CaptchaExpectedHostname": "social.example.com",
    "CaptchaExpectedAction": "signup",
    "CaptchaExpectedCdata": "activitypub_signup",
    "CaptchaVerificationTimeout": "00:00:10"
  }
}
```

Store only the secret in the mounted file; the site key is public configuration. The browser uses Cloudflare's explicit-render script and keeps one canonical `cf-turnstile-response` field. The API always calls Siteverify and binds a successful response to the configured hostname, action, and cdata. Tokens over 2,048 characters, expired or duplicate tokens, mismatched responses, provider timeouts, and malformed replies fail closed. A transient provider error is retried once with the same UUID idempotency key so the verification is not duplicated.

The generated frontend CSP allows scripts, frames, and verification connectivity only to `https://challenges.cloudflare.com` when Turnstile is selected. Turnstile tokens, the secret, and provider error details are not written to application logs.

References:

- [Turnstile server-side validation](https://developers.cloudflare.com/turnstile/get-started/server-side-validation/)
- [Turnstile explicit rendering](https://developers.cloudflare.com/turnstile/get-started/client-side-rendering/widget-configurations/)
- [Turnstile CSP guidance](https://developers.cloudflare.com/turnstile/reference/content-security-policy/)

## R2 object storage

Create a private R2 bucket and an R2 S3 API token restricted to Object Read & Write for that bucket. Supply the Access Key ID and Secret Access Key through the process secret store as `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY`; do not place either value in JSON or the repository.

```json
{
  "Media": {
    "Enabled": true,
    "Provider": "CloudflareR2",
    "Bucket": "activitypub-production-media",
    "ServiceUrl": null,
    "Region": "auto",
    "ForcePathStyle": true,
    "UseServerSideEncryption": true,
    "CloudflareAccountId": "0123456789abcdef0123456789abcdef",
    "CloudflareJurisdiction": "Default"
  }
}
```

`CloudflareJurisdiction` accepts `Default`, `Eu`, or `FedRamp`. The application derives the corresponding official endpoint (`https://<account-id>[.<jurisdiction>].r2.cloudflarestorage.com`); an arbitrary `ServiceUrl` is rejected in R2 mode so credentials cannot accidentally be sent to a different host.

Cloudflare's AWS SDK for .NET guidance requires payload signing and the SDK's unsupported default checksum to be disabled for R2 uploads. The adapter does this only in `CloudflareR2` mode. Uploads still use HTTPS, and the media pipeline records and verifies its own SHA-256 content digest.

R2 automatically encrypts every object and its metadata at rest with AES-256. R2 currently rejects the generic `x-amz-server-side-encryption` field on `PutObject`, so `UseServerSideEncryption=true` remains the production encryption invariant while the R2 adapter deliberately omits that unsupported header.

Keep the bucket private. The application does not publish the R2 S3 endpoint, custom-domain object URLs, or presigned URLs. Both public and private media continue through the same-origin `/media/...` endpoints; private objects therefore retain application viewer authorization and `private, no-store`. This also means no R2 origin needs to be added to the frontend CSP.

Cloudflare Cache Rules must honor these origin cache headers. Do not apply `Cache Everything` indiscriminately to `/media/*`, and never cache authenticated/private media responses, Inbox/Outbox POSTs, OAuth, MiAuth, or account API responses.

The fixed Dolphin backend also models object storage as an S3 endpoint, region, bucket, and forced path-style client. This adapter preserves that backend boundary. Dolphin's optional direct `baseUrl` publication is intentionally not copied because it would bypass this server's private-media authorization contract.

References:

- [Cloudflare R2 AWS SDK for .NET](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/)
- [Cloudflare R2 data security](https://developers.cloudflare.com/r2/reference/data-security/)
- [Cloudflare R2 S3 compatibility](https://developers.cloudflare.com/r2/api/s3/api/)
- [Cloudflare R2 data location and jurisdiction endpoints](https://developers.cloudflare.com/r2/reference/data-location/)

## Cloudflare proxy and Tunnel

`CF-Connecting-IP` must never be accepted merely because the header exists. Enable Cloudflare mode only when the application's direct peer is a Cloudflare Tunnel process or an origin proxy that is explicitly listed by address or CIDR.

Example for a locally connected `cloudflared` or a dedicated ingress proxy:

```json
{
  "Http": {
    "TrustedProxies": [
      "10.20.0.10"
    ],
    "TrustedProxyNetworks": [],
    "Cloudflare": {
      "Enabled": true
    }
  }
}
```

With this mode enabled, the application:

1. checks the TCP peer against the explicit proxy list before processing forwarded headers;
2. rejects an untrusted direct-origin request if it attempts to spoof `CF-Connecting-IP`;
3. requires exactly one syntactically valid `CF-Connecting-IP` value;
4. uses that value as the client IP for rate limiting and request context;
5. continues to consume `X-Forwarded-Proto` and `X-Forwarded-Host` only from that same trusted peer.

The ingress proxy must remove and overwrite incoming `CF-Connecting-IP`, `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host`. Do not add `0.0.0.0/0`, `::/0`, an entire container platform address space, or ranges that can contain untrusted workloads.

This mode requires `CF-Connecting-IP`; do not enable Cloudflare's **Remove visitor IP headers** Managed Transform for this origin. If Pseudo IPv4 overwrites the header, the resulting Cloudflare-provided pseudo address remains the rate-limit identity; the application never falls back to a caller-supplied `X-Forwarded-For` value.

Direct requests without a Cloudflare header remain available using their actual TCP address so Kubernetes/Docker health probes can work. The application peer check is therefore header-spoof protection, not an origin firewall.

For Cloudflare Tunnel, bind the origin service to a private interface/network, make `cloudflared` the only permitted peer, and list the stable tunnel container or sidecar address in `TrustedProxies`. Use an origin firewall or private network so the public Internet cannot reach the origin. If direct Cloudflare edge-to-origin traffic is used instead, restrict the firewall and `TrustedProxyNetworks` to Cloudflare's currently published origin-facing ranges and automate review of range changes; do not copy a stale list into application source.

TLS from Cloudflare to the origin must use Full (strict), an Origin CA or publicly trusted certificate, and hostname validation. Authenticated Origin Pulls or an equivalent mTLS boundary is recommended in addition to the application peer check.

The public hostname must exactly match `Federation:PublicBaseUri`, `Frontend:PublicBaseUri`, `AllowedHosts`, and the configured OAuth redirect URIs. Do not infer these values from Cloudflare headers or the request Host.

Before enabling traffic, verify:

```text
direct request without CF headers                   -> actual peer IP; restrict at firewall
request through the configured Tunnel/proxy         -> accepted
spoofed CF-Connecting-IP from an untrusted peer      -> rejected
missing or multiple CF-Connecting-IP values          -> rejected
/.well-known/webfinger and signed ActivityPub GET    -> preserve public HTTPS IRIs
private /media/{id} without an authorized viewer     -> 404
```

References:

- [Cloudflare HTTP request headers](https://developers.cloudflare.com/fundamentals/reference/http-headers/)
- [Cloudflare IP addresses](https://developers.cloudflare.com/fundamentals/concepts/cloudflare-ip-addresses/)
