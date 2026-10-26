# PubSoft.AspNetCore

**English** | [简体中文](README.zh-CN.md)


`PubSoft.AspNetCore` contains opt-in middleware and endpoint helpers that adapt the framework-neutral PubSoft packages to ASP.NET Core. Nothing is enabled automatically. The host remains responsible for authentication/authorization policy, trusted proxy configuration, secret storage, and middleware order.

## Exception Handling and Security Headers

Register exception handling early so it wraps downstream middleware and endpoints:

```csharp
using PubSoft.AspNetCore;

app.UsePubSoftExceptionHandling();
app.UsePubSoftSecurityHeaders(new SecurityHeadersOptions
{
    ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'",
    StrictTransportSecurity = "max-age=31536000; includeSubDomains"
});
```

The exception middleware maps `DomainException` and argument errors to 400 (`DomainException.StatusCode` may declare another 4xx, e.g. 409 for conflicts — non-4xx values fall back to 400), authorization exceptions to 401, and unexpected errors to a generic 500 in production. It does not rewrite caller cancellation or exceptions after response headers have started. Development responses may include exception detail/stack trace; do not expose development mode publicly. Security header values reject CR/LF. HSTS is emitted only for HTTPS requests by default; configure forwarded headers before these middleware when TLS terminates at a trusted proxy, and do not enable HSTS blindly for development.

## Request Finalization and Audit

The scoped save coordinator can run host-registered callbacks before a successful response starts:

```csharp
services.AddPubSoftRequestSaveChanges();
app.UsePubSoftRequestSaveChanges();
```

The middleware discovers every DI registration implementing `IPubSoftSaveParticipant` and saves each once before a successful response starts; `PubSoftDbContext` and `PubSoftUnitOfWork<T>` implement the contract, and `AddPubSoftSaveParticipant<T>`/`AddPubSoftRequestUnitOfWork<T>` register custom participants. Explicit callbacks registered through the scoped `PubSoftRequestSaveChanges` service still run. Saves run sequentially and at most once, only for responses below 400. Multiple saves are not an atomic transaction; a later failure can follow an earlier successful save.

Audit finalization is also opt-in and requires the host's audit scope and sink registrations:

```csharp
app.UsePubSoftAudit(new AuditMiddlewareOptions
{
    CaptureClientIp = false,
    CaptureUserAgent = false,
    CaptureActor = false
});
```

IP, user-agent, and actor capture are disabled by default. Enable them only after reviewing privacy, proxy, identity, and retention requirements. The host supplies actor mapping through `IAuditActorAccessor`; this adapter does not interpret claims. Audit sink failures are logged and do not replace the application response.

## HMAC Request Validation

Register a secret resolver and HMAC validation, then place the middleware before protected endpoints:

```csharp
services.AddScoped<IAppSecretResolver, SecretManagerAppSecretResolver>();
services.AddPubSoftHmacValidation(
    options =>
    {
        options.RequireBodyHash = true;
        options.MaximumBodyBytes = 1_048_576;
        options.ExcludedPathPrefixes = ["/health"];
    },
    signature => signature.MaxTimestampSkewSeconds = 300);

app.UsePubSoftHmacValidation();
```

The request uses one each of `X-Fend-App-Id`, `X-Fend-Signature`, `X-Fend-Timestamp`, and `X-Fend-Nonce`. The HMAC-SHA256 signature covers the HTTP method, `Request.Path`, timestamp, nonce, and optionally the lowercase SHA-256 body hash, separated by newlines. The query string is not included by this middleware's current signature format; do not make authorization or security-sensitive behavior depend on unsigned query parameters. Including query data requires a versioned client/server signature-format change. The middleware rejects missing/ambiguous headers, invalid signatures, expired timestamps, and nonce replays. Body hashing buffers the request and enforces `MaximumBodyBytes` (default 1 MiB, maximum 16 MiB), then rewinds it for downstream code.

`AddPubSoftHmacValidation` defaults to `MemoryNonceStore`, which only prevents replay within one process. Multi-instance production deployments need a reviewed atomic shared `INonceStore`; secret resolution must use an approved secret manager. Excluded paths bypass signature validation, so keep the list narrow and explicit. The middleware returns 503 when secret resolution is unavailable and does not expose the secret.

## Forwarded Headers

`RemoteIpAddress` is the canonical client-IP source across the stack; components must not read raw `X-Forwarded-For` themselves. Behind a reverse proxy, insert forwarded-header processing once, before any IP consumer:

```csharp
app.UsePubSoftForwardedHeaders(options =>
{
    // Default: trust the platform defaults (loopback peers) only.
    // Pin the proxy peer set when the backend is directly reachable:
    // options.KnownProxies.Add("10.0.0.1");
    // options.KnownNetworks.Add("172.16.0.0/12");
    // X-Real-IP-only proxies (no X-Forwarded-For):
    // options.RealIp = true;
    // Only when the socket is exclusively reachable from the proxy
    // (loopback-bound port / private network):
    // options.TrustAnyProxy = true;
});
app.UsePubSoftRateLimiting();
```

`TrustAnyProxy` on a directly reachable backend lets any client spoof its source IP — rate-limit evasion and audit poisoning. `RealIp = true` applies `X-Real-IP` when no `X-Forwarded-For` is present and the socket peer is trusted; it is off by default and rejected by `AddPubSoftForwardedHeaders` because the platform `ForwardedHeadersOptions` cannot express it. Malformed `KnownProxies`/`KnownNetworks` entries fail startup instead of being silently dropped. `AddPubSoftForwardedHeaders` is the service-side counterpart registering `ForwardedHeadersOptions` for hosts that call `UseForwardedHeaders()` themselves. `ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders` detects the misconfiguration at runtime (private socket peer + public advertised client): both rate-limiting middlewares emit a one-time warning naming the missing call.

## IP Rate Limiting and Health Endpoint

The rate-limiting adapter is a bounded, process-local fixed-window limiter keyed only by normalized `HttpContext.Connection.RemoteIpAddress`:

```csharp
services.AddPubSoftRateLimiting(options =>
{
    options.RequestsPerWindow = 100;
    options.WindowSize = TimeSpan.FromMinutes(1);
    options.MaxIdentifiers = 100_000;
});

app.UseForwardedHeaders(); // Configure ForwardedHeadersOptions with trusted proxies/networks.
app.UsePubSoftRateLimiting();
```

It does not read `X-Forwarded-For` itself; forwarded-header processing must be configured for trusted proxies before rate limiting. Missing remote IP and exhausted identifier capacity fail closed. The limiter is not a cluster-wide quota and currently rejects user-ID mode in this adapter.

For route-specific quotas, register one or more named policies and opt into the policy middleware instead:

```csharp
services.AddPubSoftRateLimitPolicy(options =>
{
    options.Name = "login-failures";
    options.RequestsPerWindow = 10;
    options.WindowSize = TimeSpan.FromMinutes(5);
    options.CountFailuresOnly = true;
    options.AppliesTo = context => context.Request.Path.StartsWithSegments("/api/login");
    options.IdentifierSelector = context =>
        IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString());
});

app.UsePubSoftRateLimitPolicies();
```

Policies stack when more than one matches a request. The default identifier is the normalized transport remote IP; custom selectors are host code and must use a trusted identity or proxy-processed value, never an unvalidated forwarded/client header. Failure-only policies check the existing count before the endpoint and record the response when the downstream pipeline returns or throws. Simultaneous in-flight failures may all pass before their counts are committed, so this is abuse throttling rather than a strict concurrency limit. Both middleware variants are process-local; use a reviewed distributed quota provider for cluster-wide enforcement. Hosts must define route categories and decide whether 4xx/5xx, 404, or particular authentication failures count — `IsFailureResponse` receives `(HttpContext, statusCode)` so predicates can exclude e.g. 401s on requests that already carried credentials.

The 429 rejection body defaults to `{"error":"rate_limit_exceeded"}`; hosts with an API error envelope can override it once for both middlewares:

```csharp
services.AddPubSoftRateLimitRejection(context =>
    context.Response.WriteAsJsonAsync(new { code = 429, message = "rate limit exceeded" }));
```

For aggregate health, register `PubSoft.Monitoring` checks and its aggregator, then explicitly map `app.MapPubSoftHealthEndpoint()`. It returns only the overall status (`Unhealthy` is 503; `Healthy`/`Degraded` is 200). Apply endpoint authorization and rate limiting as required by the host.

## Development-Only Controllers

`AddPubSoftDevelopmentOnlyControllerConvention(environment)` is an opt-in MVC convention. Controllers marked with `[DevelopmentOnly]` are removed outside Development. It is not an authorization substitute; do not use it to protect non-controller endpoints or replace access control.

## MVC Permission Authorization

Register the MVC filter explicitly after adding controllers:

```csharp
services.AddControllers().AddPubSoftPermissionAuthorization();
```

The global filter enforces only explicit `[RequirePermission]`, `[RequireCustomPermission]`, or `[RequireExtPermission]` metadata on a controller/action. Each requirement must pass; unannotated actions continue unchanged. Missing resolved subject returns 401; tenant-context mismatch or missing grant returns 403. The host must register the trusted subject accessor, tenant context, permission definitions, and grant provider. `PermissionExemptAttribute`/`IAllowAnonymous` take precedence over requirement metadata on the same endpoint. This does not implement role/admin bypass or a general `[AllowAnonymous]` policy beyond that precedence; decide those semantics in the host before combining policies.

## Machine Client Credentials

`[RequireClientCredential]` marks endpoints accepting service-to-service credentials via `X-Client-Id`/`X-Client-Secret` headers. The host registers an `IClientCredentialVerifier` that resolves and validates the pair (the credential store is entirely host-defined). On success the filter attaches a machine principal (`client_id` + `caller_type` claims plus verifier-supplied claims), stores the verified client ID under `PubSoft.ClientCredential.ClientId`, and scopes the ambient tenant context when the verifier returns a tenant. Missing or invalid credentials return an undifferentiated 401; a marked endpoint without a registered verifier fails closed with 403. Set `RequireClientCredentialAttribute.AllowAuthenticatedUser` to also admit already-authenticated principals (default: machine credentials only).

```csharp
services.AddSingleton<IClientCredentialVerifier, MyClientVerifier>();
services.AddControllers().AddPubSoftClientCredentialAuthorization();  // MVC
// or minimal API: endpoint.AddPubSoftClientCredential()
```

Header names and claim types are configurable via `ClientCredentialOptions`. The tenant scope wraps the endpoint execution and is reverted afterward.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.AspNetCore`: `AuditMiddleware` `AuditMiddlewareExtensions` `AuditMiddlewareOptions` `DevelopmentOnlyAttribute` `DevelopmentOnlyControllerConvention` `DevelopmentOnlyControllerConventionExtensions` `ExceptionHandlingMiddleware` `ExceptionHandlingMiddlewareExtensions` `HealthEndpointRouteBuilderExtensions` `HmacValidationMiddleware` `HmacValidationOptions` `HmacValidationServiceCollectionExtensions` `IAppSecretResolver` `IpNormalizer` `PubSoftRateLimitPolicy` `PubSoftRateLimitPolicyExtensions` `PubSoftRateLimitPolicyMiddleware` `PubSoftRateLimitPolicyOptions` `PubSoftRateLimitingExtensions` `PubSoftRateLimitingMiddleware` `PubSoftRequestSaveChanges` `PubSoftRequestSaveChangesExtensions` `PubSoftRequestSaveChangesMiddleware` `SecurityHeadersMiddleware` `SecurityHeadersMiddlewareExtensions` `SecurityHeadersOptions`
- `PubSoft.AspNetCore.Authentication`: `ClientCredentialAuthorizationExtensions` `ClientCredentialEndpointFilter` `ClientCredentialMvcAuthorizationFilter` `ClientCredentialOptions` `ClientCredentialVerificationResult` `IClientCredentialVerifier` `PrincipalSnapshotServiceCollectionExtensions` `RequireClientCredentialAttribute`
- `PubSoft.AspNetCore.Authorization`: `PermissionEndpointFilter` `PermissionEndpointFilterExtensions` `PermissionMvcAuthorizationExtensions` `PermissionMvcAuthorizationFilter`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~ExceptionHandlingMiddlewareTests
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~RateLimitingMiddlewareTests
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~RequestSaveChangesMiddlewareTests
```