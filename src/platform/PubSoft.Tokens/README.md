---
purpose: package-level hosting guide for PubSoft.Tokens
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Tokens

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/platform/PubSoft.Tokens/README.zh-CN.md)


`PubSoft.Tokens` obtains service tokens through `HttpClient`, caches them through `ICache`, and can add them to a named API client. It does not decide how a caller's identity maps to a service credential or whether an API operation is safe to replay.

## Registration

Register an `ICache` implementation and one or more named credentials. The example uses the process-local cache and is intended for development or a single process. Production hosts should register their approved cache and load endpoint credentials from a secret manager rather than source-controlled configuration.

```csharp
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Caching;
using PubSoft.Tokens;

services.AddPubSoftMemoryCache();
services.AddPubSoftTokens(options =>
{
    options.Credentials["billing-api-v1"] = new TokenCredential
    {
        Url = tokenEndpoint,
        Method = "POST",
        Body = JsonSerializer.Serialize(new
        {
            grant_type = "client_credentials",
            client_id = clientId,
            client_secret = clientSecret
        }),
        TokenPath = "access_token",
        ExpiryPath = "expires_in",
        Expiry = TimeSpan.FromMinutes(10),
        RefreshBefore = TimeSpan.FromMinutes(1),
        Timeout = TimeSpan.FromSeconds(5)
    };
});

services.AddHttpClient("billing-api")
    .AddTokenHandler("billing-api-v1");
```

POST bodies are sent as JSON. `TokenPath` and `ExpiryPath` are dot-separated JSON paths; dynamic expiry is read in seconds and never extends beyond configured `Expiry`. Token endpoint calls have their own `Timeout`. Credentials travel to `Url`, so non-loopback `http` endpoints are rejected at options validation; loopback stays exempt for local development and a per-credential `AllowInsecureUrl` opt-in covers deliberate exceptions. Keep credentials and token responses out of logs, and do not put secrets in URL user-info or query strings. The provider logs fixed failure events/status codes without attaching transport or JSON exceptions, since exception details can include endpoint URIs or response-derived data.

The host must register `ICache` before resolving the token provider. Cache keys derive from the credential key, not the URL or client identity. Give each application/cache namespace a distinct credential key; when changing the identity represented by a key, version the key or remove its old cached token. The built-in refresh single-flight is process-local, so multiple instances may call the token endpoint concurrently. A shared cache does not add distributed locking.

If a credential is selected but no non-empty token can be obtained, `TokenDelegatingHandler` throws `HttpRequestException` without sending the protected request. With no credential selected at all, the handler passes the request through unchanged; do not rely on that behavior for an endpoint that requires authentication.

## Per-Request Overrides

Credential and header settings may be overridden on an individual request:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "/invoices")
    .SetTokenCredentialKey("billing-api-v1")
    .SetTokenHeaderName("Authorization")
    .SetTokenHeaderPrefix("Bearer ");
```

Without explicit opt-in, a 401 response is returned and no token refresh/replay is attempted. For an operation whose endpoint is confirmed safe to repeat, opt in on that request:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "/invoices")
    .EnableTokenUnauthorizedRetry();
```

The handler invalidates the cached token and retries at most once. When opted in, it buffers the request content in memory to make the retry possible. Only use this for operations whose application semantics are idempotent, and keep replayable request bodies bounded. If refreshing fails, the original 401 is returned and no second request is sent.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Tokens`: `HttpTokenProvider` `ITokenProvider` `TokenCredential` `TokenDelegatingHandler` `TokenHandlerOptions` `TokenManagerOptions` `TokenRequestOptions` `TokenServiceCollectionExtensions`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~TokenTests
bash scripts/verify-package-consumer.sh
```

Tests cover token caching, dynamic expiry, concurrent refresh within one process, endpoint timeout/cancellation, fail-closed missing tokens, endpoint exception log redaction, and the request-level 401 opt-in. They do not validate a production secret manager, shared cache isolation, distributed refresh coordination, or endpoint idempotency.

## Self-signed machine tokens

`MachineTokenProvider` issues HS256 access tokens locally (via `PubSoft.Authentication.Jwt`) and caches them per credential key until `Lifetime - RefreshBefore` — the standard machine-to-machine credential when no external STS is involved. Compose with `TokenDelegatingHandler` for outbound `HttpClient` Bearer injection.

```csharp
// Binds Jwt:Issuer / Jwt:Audience / Jwt:SecretKey (SigningKey is accepted as an alias)
// plus optional KeyId, Lifetime, RefreshBefore, and Credentials; validated at startup.
services.AddPubSoftMachineTokens(configuration);
```

Issuer and audience are compared ordinally (case-sensitive) against the validator's
`ValidIssuer`/`ValidAudience`, so signers and validators must read the same configured
values — never hardcode literals on one side. The signing key must hold 256–1024 bits
of key material; keep production keys in a secret manager, not source-controlled config.

For explicit options instead of configuration binding:

```csharp
services.AddPubSoftMachineTokens(o =>
{
    o.Issuer = "orders"; o.Audience = "inventory";
    o.SigningKey = configuration["Jwt:SecretKey"]!;
    o.Credentials["inventory"] = new MachineTokenCredential { Claims = { ["caller_type"] = "Machine" } };
});
```
When both an HTTP provider and machine tokens coexist, resolve the local issuer deterministically via keyed DI: `[FromKeyedServices(MachineTokenProvider.ServiceKey)]` (`"machine"`).

