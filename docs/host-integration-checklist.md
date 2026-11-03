# Host security integration checklist

PubSoft packages are fail-closed by default, but several security boundaries are
intentionally host-owned. This checklist covers the integration tests every
host should run before deploying to production. Where an item is marked
"invariant", a host-side regression test should assert it continuously.

## Replay protection (ClientSigner / client credentials)

- [ ] Nonces are stored in a *shared* store (e.g. Redis), not instance memory —
      replay across replicas must be rejected. Invariant: the same
      `(nonce, timestamp, signature)` replayed against a second instance fails.
- [ ] Nonce reservations are atomic (`SET key 1 NX EX`), not check-then-set.
- [ ] Nonce TTL ≥ `ClientSigner.NonceTtl` so an expired signature can never be
      replayed inside the timestamp window.
- [ ] `X-Timestamp` is Unix **milliseconds** — asserting with seconds silently
      disables the freshness check.

## Forwarded headers / rate limiting

- [ ] The host runs only behind a gateway or load balancer that strips external
      `X-Forwarded-For`. With rate limiting enabled, PubSoft clears
      `KnownProxies`/`KnownIPNetworks` — deploying without a trusted edge makes
      all IP-based keys client-spoofable.
- [ ] Invariant: a forged `X-Forwarded-For` header does not change the identity
      bucket when the request did not pass the trusted proxy.

## Tenant and member scoping

- [ ] `HttpContextTenantOptions.ReadHeader` is only enabled when the gateway
      strips client-supplied tenant headers. Invariant: `X-Tenant-Id` on a
      direct request cannot cross-tenant-read.
- [ ] Member scope with `IsMemberScoped=true` and no member ID returns an
      empty result set (fail-closed), verified against a real DbContext.
- [ ] `IgnoreScopeFilters()` is invoked only on intentionally cross-tenant
      paths (e.g. anonymous relay lookups), never inside ordinary app services.

## JWT / JWKS

- [ ] `Jwt:SecretKey` is ≥ 32 bytes of real entropy and rotated per
      environment; the same key is shared only with issuers that are meant to
      be trusted.
- [ ] When `AuthServer` is configured, RS256 tokens validate only against
      `JwksUri` keys — a token with a forged `alg`/key must 401.
- [ ] Invariant: disabling JWKS leaves HS256 validation intact (no downgrade).

## Configuration / secrets

- [ ] DB-first config providers cache no longer than their TTL and honor
      `IsEnabled`; disabling a row takes effect without restart.
- [ ] `SlowQueryInterceptor` logs SQL truncated to 500 characters — no full
      statement bodies or parameter values are written to logs.

## Outbox / messaging

- [ ] Outbox consumer handlers are idempotent — at-least-once delivery means
      the same message can arrive twice; invariant: redelivery does not
      double-apply side effects.
