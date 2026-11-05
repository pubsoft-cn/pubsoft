# PubSoft Security and Trust-Boundary Analysis

**English** | [简体中文](security-boundary-analysis.zh-CN.md)

Review baseline: `main` / `origin/main`, commit `81a7f52` (2026-11-04). This document summarizes security mechanisms, trust boundaries, host responsibilities, and evidence gaps supported by the repository. It is not a penetration test or security certification of any production deployment.

## Scope and Interpretation

PubSoft is a set of explicitly enabled .NET infrastructure libraries and adapters, not a complete application identity, authorization, secret-management, or network policy. Findings are based on current implementation, package documentation, tests, and workflows. Third-party behavior, production configuration, and consumer application code cannot generally be established from this repository.

Evidence labels used here:

- **Library-enforced:** validation, rejection, or resource bound directly implemented by the current package.
- **Host-configured:** a mechanism the consumer must register, order, or provide; the library cannot ensure correct integration.
- **Deployment-dependent:** requires a gateway, secret store, Redis, database, identity provider, or operational configuration.
- **Not established:** repository evidence is insufficient to support a definitive conclusion.

Risk levels describe impact and exploitability under stated preconditions: High, Medium, or Low. Conditional risks must be read with their deployment assumptions. Passing tests show expected behavior for covered cases; they do not prove absence of other defects.

## System and Trust Boundaries

Typical request and data flow:

```text
External client / service / device
  -> reverse proxy or API gateway (source IP, forwarded headers, TLS trust)
  -> PubSoft ASP.NET Core adapters (authentication, tenant context, authorization, limits, audit)
  -> host business logic and permission/credential resolvers
  -> EF Core database / Redis / message broker / external HTTP service
```

Scheduled HTTP and Outbound can also call external systems directly from host background work, bypassing the HTTP ingress. Request-entry controls therefore do not replace task-level authorization, egress network policy, or idempotency for external side effects.

### Assets and Attacker Capabilities

Primary assets include tenant and member data, identities and permissions, passwords and service credentials, signing/encryption keys, nonces and sessions, audit records, messages and external side effects, and the integrity of application and build artifacts. Relevant attackers may be anonymous Internet requesters, authenticated users with limited permissions, malicious or compromised tenant/service clients, operators able to edit low-trust job configuration, or callers able to supply arbitrary headers and payloads to a public endpoint.

Request data, client headers, query parameters, message payloads, and job configuration editable by low-trust users are untrusted by default. Trusted proxies, host identity providers, secret stores, distributed stores, and production network topology must be declared and validated by consumers. If a host lets a user choose a tenant, client code, Outbound profile, or job target, that choice needs separate authorization; a library API is not proof of authorization.

## Controls and Responsibility Matrix

| Boundary | Repository mechanisms | Primary responsibility and limitations |
|---|---|---|
| JWT, OAuth, subject | Separate RS256/HS256 validation paths; subject resolver can require a trusted issuer; OAuth providers use explicit configuration and adapters | Host configures issuer/audience, authorization code/callback, account linking, session lifecycle, and mapping from subject to local account. Third-party compatibility requires separate review. |
| Permissions, client credentials | Grants are filtered exactly by subject, tenant, client code, and module; undefined permissions and missing verifiers fail closed | Host must cover every protected endpoint, provide trusted grant/verifier implementations, and define administrator exceptions and tenant-switch rules. MVC/endpoint filters require explicit opt-in. |
| Tenant/member and EF | `HttpContextTenantContext` does not read tenant headers by default; EF named filters cover tenant/member/soft-delete; missing member ID in a member scope returns no rows | Enable header-based selection only after a trusted edge strips external values and the principal is authorized for the chosen tenant. `IgnoreScopeFilters()` and EF `IgnoreQueryFilters()` are privileged bypasses whose call sites need control. |
| Passwords, crypto, signatures | PBKDF2 password hashing; AES-GCM writes; CBC migration-only reads; constant-time HMAC comparison; nonce reserved after signature verification | Host supplies `IEncryptionKeyRing`, app-secret resolver, rotation/backup/recovery, and access policy. In-memory nonces cover one process only; distributed stores need atomic operations and failure policy. |
| Ingress, proxy, rate limits | Forwarded-header adapter supports trusted proxies; fixed-window limiter bounds identifier capacity; rate-limit policies can stack | Proxy must strip spoofed forwarded headers, and `UseForwardedHeaders` must precede IP consumers. Limiting is process-local, not a cluster-wide quota or concurrency boundary. |
| Scheduled HTTP and outbound | Scheduled HTTP blocks private addresses by default, resolves and pins at connect time, revalidates each redirect hop, and does not forward credentials across origins; retries default to safe methods | Host controls job authoring, allowlists, internal-probe exceptions, external HTTP credentials/transport/logging, and idempotency. Outbound metadata `IsEncrypted` does not encrypt data. |
| Edge RPC and sessions | Handlers explicitly declare schemes; session store and limiting are pluggable; schemes include HMAC, session, none, and legacy MD5 | `none` routes must be intentionally public; MD5 is compatibility-only, not for new deployments. In-memory sessions are unsuitable for multiple instances; host controls pseudo-paths and OpenAPI/Swagger UI exposure. |
| EF, audit, messaging | EF writes check scope; audit value capture is opt-in with sensitive-field masking; Outbox/Inbox provide transactional database handling and declare at-least-once delivery | Host owns DB accounts/migrations, cross-tenant operational authorization, audit access/retention, broker permissions, and idempotency for consumer side effects. Cleanup/archival is not automatically scheduled. |
| Build and supply chain | CI includes Gitleaks, hard gates for high/critical NuGet advisories, tests/coverage threshold, dependency updates, package and AOT consumer checks | The current workflow is not equivalent to full SAST/DAST, signed releases, or production artifact provenance. Coverage and consumer builds do not prove security properties or deployment safety. |

## Security-Relevant Public API Inventory

The following inventory maps security-relevant contracts from the per-project API ledgers to decisions a consumer must make. It is not a symbol-by-symbol inventory: each row groups related contracts and highlights options that materially change a trust boundary. The package reconciliation below accounts for every source project represented by a ledger and identifies where its security-relevant surface is discussed; utility APIs with no direct security boundary are not individually analyzed. Public API presence is not itself a vulnerability. Defaults and behavior below were checked against implementation or package README where noted.

| Package/API surface | Security-significant contract | Required consumer decision and evidence boundary |
|---|---|---|
| `PubSoft.AspNetCore`: `AddPubSoftPermissionAuthorization`, `AddPubSoftClientCredential`, `PermissionEndpointFilter`, `ClientCredentialEndpointFilter`, `IClientCredentialVerifier` | Authorization filters are explicit opt-in; client credentials are delivered through configurable headers and delegated to a host verifier; verifier results can set client ID, tenant ID, and claims. | Enumerate protected routes and prove each route is covered. Verify secrets without logging them; bind returned tenant/claims to the validated credential and least-privilege scope. Public API declares the boundary, not the host's verifier correctness. |
| `PubSoft.Authentication.Jwt.AspNetCore`: `PubSoftJwtBearerOptions`, `DualJwtOptions`, `JwksOptions`, `JwksKeyProvider` | Public settings include issuer, audience, metadata/JWKS source and symmetric-key alternatives; JWKS cache lifetime is configurable. Dual JWT AuthServer had the separately reported audience-validation and key-cache lifecycle findings R2-R3, both fixed after the baseline. | Pin accepted issuers, audiences, algorithms and key sources; keep metadata endpoints under administrative control; exercise key rotation and cross-audience rejection. Do not infer the ordinary bearer path's checks apply to Dual JWT. |
| `PubSoft.AspNetCore`: `HttpContextTenantOptions.ReadHeader`, `ClaimNames`, `HeaderName`; `PermissionEndpointFilter` | Tenant header reading defaults off; an enabled header is a tenant selector, not identity proof. Authorization takes subject and tenant context as inputs. | Allow only server-verified principals to select authorized tenants. Test cross-tenant object access, missing tenant/member context and privileged filter bypasses on real repository paths. |
| `PubSoft.AspNetCore`: `PubSoftForwardedHeadersOptions` and gateway/custom rate-limit options | Forwarded-for/proto processing defaults on in the adapter; `TrustAnyProxy` defaults false, `ForwardLimit` defaults null, and exact trusted proxy/network lists are available. The custom policy accepts `IdentifierSelector` and caps identifiers; Gateway has separate anonymous/client/failure buckets. | Configure only actual proxy peers and the proxy-chain limit; isolate the backend and run the middleware before IP consumers. Treat custom identifiers as untrusted unless derived after authentication; retain aggregate limits. Gateway's separate trust assumptions are captured in R1 and R6. |
| `PubSoft.Security`: `HmacSignatureValidator`, `INonceStore`, `MemoryNonceStore`, `RateLimiter`; ASP.NET Core `HmacValidationOptions` | Signature input includes method, path, query and optional body digest; excluded path prefixes and maximum body size are configurable. Body hashing is on by default (`RequireBodyHash`, default 1 MiB, hard cap 16 MiB). Memory nonce store defaults to 100,000 entries and is process-local. | Keep bypass prefixes narrow, require body digests when the request semantics need them, bound request bodies, protect secret resolution, and use an atomic shared nonce store across replicas. Ensure every signed field agrees with proxy normalization and client canonicalization. |
| `PubSoft.Scheduling.Http`: `HttpExecutorOptions`, `HttpExecutorConfig`, `TokenRef` | Private-network blocking defaults on; host allowlist defaults empty; redirects and system proxy default off; unsafe retries require opt-in. Options validation rejects `UseSystemProxy` combined with `BlockPrivateNetworks`. Credentials and arbitrary headers can be supplied through job config. | Restrict who can create/edit jobs; set an explicit host allowlist when feasible; avoid enabling private targets, proxy or unsafe retries without a documented exception. A safe executor cannot authorize job authors or secure the network outside its connection path. |
| `PubSoft.Contracts` / `PubSoft.Outbound`: `ApiFieldAttribute.IsEncrypted`, `IOutboundSdk.ExecuteAsync(request, profileId, tenantId)` | `IsEncrypted` is metadata, not cryptographic transformation. Outbound calls accept tenant and profile selectors but do not by themselves authorize them. | Enforce tenant/profile entitlement before invocation and protect credentials, transport, logs and retries in the concrete provider. Do not treat metadata or required parameters as enforcement. |
| `PubSoft.Edge.Client` / `PubSoft.Edge.Core` / `PubSoft.Edge`: `EdgeClient` constructors, `EdgeAuthScheme.None/Md5`, `EnablePseudoPaths`, `TimestampToleranceSeconds`, OpenAPI options | One client constructor defaults to `None`; handler scheme defaults are HMAC + session per package README. MD5 and `None` remain public compatibility/anonymous schemes. Pseudo-paths default off; OpenAPI is development-only by default and Swagger UI uses configurable CDN assets. | Select an explicit scheme in clients; expose `None` only for intentionally public handlers and MD5 only for legacy interoperability. Gate method catalogs and docs in production, review CDN policy, and avoid returning `subMessage` diagnostics to end users. |
| `PubSoft.Messaging.Mqtt`: `MqttOutboxOptions`, `MqttInboxOptions` | Public options include broker URI, client identity, username/password, QoS, persistent session, topic/filter and payload bounds. README requires MQTT 5 for Inbox dedup metadata; MQTT 3.1.1 can silently lose the dedup identity. | Use `mqtts://` and broker ACLs, source credentials from a secret provider, scope topic permissions by service/tenant as required, and test reconnect, duplicate delivery, offline queue and broker-version behavior. Outbox remains at-least-once; idempotency is still required. |
| `PubSoft.Cryptography`: `IEncryptionKeyRing`, `AesGcmEncryptor`, `CredentialEncryptionService`, `Sm4.EncryptEcb/DecryptEcb`; audit capture options | AES-GCM uses a host key ring; low-level SM4 ECB APIs are public and documented as compatibility-only because ECB reveals block patterns. Audit value/actor/IP/UA capture is configurable and can contain sensitive data. | Keep key custody/rotation outside source control; prefer authenticated encryption for new storage; restrict ECB to a mandated legacy protocol. Minimize captured data, mask secrets, and enforce audit access and retention. |
| `PubSoft.Tokens`: `MachineTokenOptions`, `MachineTokenCredential`, `TokenCredential`, `TokenRequestOptions`, `HttpTokenProvider`, `TokenDelegatingHandler` | Supports local HS256 machine tokens and HTTP token acquisition from configurable URL/method/body/headers and JSON token/expiry paths. Missing selected credentials/tokens fail closed; no selected credential passes through unchanged. 401 replay is explicit opt-in and limited to one retry, but buffers request content; the token response has no package-enforced size cap. | Load signing keys and endpoint credentials from a secret manager, use HTTPS and trusted endpoint URLs, isolate cache credential keys, and enforce token-response and replay-body size limits at the host/transport boundary. Only opt into 401 replay for semantically idempotent operations with bounded bodies. |
| `PubSoft.Authentication.OAuthProviders`: `OAuthAuthorizeUrlRequest`, `IOAuthProvider`, `IOAuthProviderRegistry`, `IOAuthChannelConfigProvider` | Public flow inputs include `State`, `CodeChallenge`, `RedirectUri`, provider/channel/app selectors and scope. Providers return vendor identity data; host configuration supplies credentials and redirect URI. | Generate and bind one-time state/nonce to the user-agent transaction, use PKCE where supported, allowlist exact redirect URIs, and authorize provider/channel/app selection. Host owns callback validation, account linking, session/token issuance and identity-to-account mapping. |
| `PubSoft.MultiTenancy`: `ITenantAppService`, `CreateTenantInput`, `UpdateTenantInput`, `Tenant` | Management is a framework-independent contract/model only; there is no persistence implementation, tenant resolver, or admin authorization. Tenant code is stable through the model API; lifecycle/deletion semantics are host-defined. | Protect create/list/update/delete as administrative functions, validate tenant lifecycle and identifier uniqueness, and never treat a caller-supplied tenant ID as authority to switch scope. |
| `PubSoft.Auditing` / `PubSoft.AspNetCore` / `PubSoft.EntityFrameworkCore`: `AuditChangeCollectorOptions`, `AuditScopeOptions`, `AuditMiddlewareOptions`, `AddPubSoftAuditChangeCapture`, retention cleanup | Capture of property values, actor, client IP and user agent is configurable; value limits and sensitive-property masking are available. Audit and retention APIs do not establish who may read/export/delete records or when cleanup runs. | Minimize and mask collected data, validate redaction configuration, restrict audit access/export, set retention/deletion policy, and schedule cleanup under a least-privilege operator identity. Treat client IP and actor values as claims requiring trusted middleware/identity provenance. |
| `PubSoft.Messaging.Outbox`: `IOutboxScopeProvider`, `OutboxBackgroundService`, `OutboxProcessor` | Worker is opt-in. Without a scope provider it processes one ordinary DI scope; a multi-tenant provider can enumerate scopes. Delivery is at-least-once and database Inbox transactions cannot include external effects. | Authorize tenant enumeration in the host, initialize tenant context before resolving processors, dispose yielded scopes, monitor retries/dead letters, and make external effects idempotent. |
| `PubSoft.Notifications`: `DingTalkWebhookOptions`, `TeamsWebhookOptions`, dispatcher/deduplication options | Webhook URL and DingTalk secret are configurable credentials; built-in channels use bounded HTTP resilience. Dispatch deduplication is bounded and process-local; multi-channel dispatch is sequential and non-atomic. | Keep URLs/secrets in approved secret storage, restrict destination changes to trusted operators, enforce egress policy, redact recipient/provider data, and use provider-side idempotency for durable or cross-instance delivery guarantees. |
| `PubSoft.Resilience.Http`: `HttpResilienceOptions`, `PubSoftHttpResilienceHandler` | Configures total/per-attempt timeouts, retry, and optional in-process circuit breaker. Retries are limited to bodyless GET/HEAD/OPTIONS/TRACE; no unsafe retry override exists. | Set budgets to match upstream capacity and business deadlines. Do not infer application-level idempotency from HTTP method alone; circuit-breaker state is not distributed. |
| `PubSoft.Caching.Redis`: `IFencedLockHandle`, distributed lock/cache options | Redis lock acquisition uses an owner token and monotonic fencing token; lease extension is explicit and not automatic. A lease does not by itself protect a downstream resource from a stale holder. | Pass fencing tokens to the protected resource and require it to reject stale tokens; choose lease/extension behavior deliberately and test Redis failover/topology. Cache key prefixes and tenant isolation remain host design decisions. |

### Package-by-Package Ledger Reconciliation

This reconciliation is complete at the package level, not at the individual-symbol level. A declaration is counted as a nonblank, noncomment line in the ledger. In this checkout, all 43 `PublicAPI.Shipped.txt` ledgers contain zero declarations; the 43 `PublicAPI.Unshipped.txt` ledgers contain 3,558 declarations. Thus the checked-in ledger snapshot records the listed API contracts as unshipped; this is not a claim about package publication status. The package column is linked to its unshipped ledger. "Analyzed" means the security-relevant contract groups are discussed above or in the cited report sections; "context only" means the package is accounted for but no direct security-specific contract review is claimed.

| Package / ledger | Reconciliation in this report |
|---|---|
| [PubSoft.Analyzers](../src/analyzers/PubSoft.Analyzers/PublicAPI.Unshipped.txt) | Context only: build-time diagnostics; no runtime trust boundary assessed. |
| [PubSoft.Contracts](../src/core/PubSoft.Contracts/PublicAPI.Unshipped.txt) | Analyzed: DTO/property metadata and descriptive `IsEncrypted` boundary. |
| [PubSoft.Core](../src/core/PubSoft.Core/PublicAPI.Unshipped.txt) | Context only: general primitives; no package-specific security contract claimed. |
| [PubSoft.Cryptography](../src/core/PubSoft.Cryptography/PublicAPI.Unshipped.txt) | Analyzed: authenticated encryption, key-ring boundary, compatibility ECB and audit capture. |
| [PubSoft.DependencyInjection](../src/core/PubSoft.DependencyInjection/PublicAPI.Unshipped.txt) | Context only: registration composition; host owns effective service graph. |
| [PubSoft.Domain](../src/core/PubSoft.Domain/PublicAPI.Unshipped.txt) | Analyzed: ambient tenant/member scope is covered under tenancy and EF boundaries. |
| [PubSoft.Events](../src/core/PubSoft.Events/PublicAPI.Unshipped.txt) | Context only: event contracts/dispatch; event authorization and handler trust are application-specific. |
| [PubSoft.Identity](../src/core/PubSoft.Identity/PublicAPI.Unshipped.txt) | Analyzed by domain: account/credential lifecycle and host identity mapping; see identity boundary details. |
| [PubSoft.Logging](../src/core/PubSoft.Logging/PublicAPI.Unshipped.txt) | Context only: no dedicated logging API security review; secret and personal-data redaction remains a host obligation. |
| [PubSoft.Mapping](../src/core/PubSoft.Mapping/PublicAPI.Unshipped.txt) | Context only: mapping configuration can affect property exposure; DTO authorization is host-specific. |
| [PubSoft.MultiTenancy](../src/core/PubSoft.MultiTenancy/PublicAPI.Unshipped.txt) | Analyzed: management contracts, stable tenant identifiers and absence of built-in admin authorization. |
| [PubSoft.Resilience](../src/core/PubSoft.Resilience/PublicAPI.Unshipped.txt) | Analyzed by domain: retry/idempotency and in-process breaker considerations; HTTP integration details below. |
| [PubSoft.Auditing](../src/data/PubSoft.Auditing/PublicAPI.Unshipped.txt) | Analyzed: value capture, sensitive-property masking and bounded collection. |
| [PubSoft.Auditing.Outbox](../src/data/PubSoft.Auditing.Outbox/PublicAPI.Unshipped.txt) | Analyzed by domain: durable audit delivery/retention is covered under audit and messaging; host schedules and authorizes operations. |
| [PubSoft.EntityFrameworkCore](../src/data/PubSoft.EntityFrameworkCore/PublicAPI.Unshipped.txt) | Analyzed: tenant/member/soft-delete filters, explicit bypasses, audit capture and retention. |
| [PubSoft.AspNetCore](../src/host/PubSoft.AspNetCore/PublicAPI.Unshipped.txt) | Analyzed: auth filters, tenant context, forwarded headers, HMAC, rate limits and audit middleware. |
| [PubSoft.AspNetCore.Docs](../src/host/PubSoft.AspNetCore.Docs/PublicAPI.Unshipped.txt) | Analyzed by domain: documentation/OpenAPI exposure is a host deployment concern. |
| [PubSoft.Edge](../src/host/PubSoft.Edge/PublicAPI.Unshipped.txt) | Analyzed: handler schemes, pseudo-paths, diagnostics and OpenAPI exposure. |
| [PubSoft.Edge.Client](../src/host/PubSoft.Edge.Client/PublicAPI.Unshipped.txt) | Analyzed: client authentication-scheme default and explicit scheme selection. |
| [PubSoft.Edge.Core](../src/host/PubSoft.Edge.Core/PublicAPI.Unshipped.txt) | Analyzed: handler contracts, session/auth schemes and compatibility paths. |
| [PubSoft.Messaging](../src/messaging/PubSoft.Messaging/PublicAPI.Unshipped.txt) | Context only: generic message contracts; authorization and data classification depend on consumers. |
| [PubSoft.Messaging.Mqtt](../src/messaging/PubSoft.Messaging.Mqtt/PublicAPI.Unshipped.txt) | Analyzed: broker credentials, topic/QoS/session/payload controls and delivery semantics. |
| [PubSoft.Messaging.Outbox](../src/messaging/PubSoft.Messaging.Outbox/PublicAPI.Unshipped.txt) | Analyzed: tenant-scope provider, worker registration, retries and at-least-once side effects. |
| [PubSoft.Caching](../src/platform/PubSoft.Caching/PublicAPI.Unshipped.txt) | Context only: cache isolation and key namespace are host responsibilities. |
| [PubSoft.Caching.Redis](../src/platform/PubSoft.Caching.Redis/PublicAPI.Unshipped.txt) | Analyzed: cache namespace and distributed lock fencing/lease semantics. |
| [PubSoft.Modularity](../src/platform/PubSoft.Modularity/PublicAPI.Unshipped.txt) | Context only: module composition/configuration; no independent security guarantee assessed. |
| [PubSoft.Monitoring](../src/platform/PubSoft.Monitoring/PublicAPI.Unshipped.txt) | Context only: observability exposure and access policy are host-specific. |
| [PubSoft.Monitoring.Scheduling](../src/platform/PubSoft.Monitoring.Scheduling/PublicAPI.Unshipped.txt) | Context only: scheduled monitoring registration; operational endpoint authorization is host-specific. |
| [PubSoft.Notifications](../src/platform/PubSoft.Notifications/PublicAPI.Unshipped.txt) | Analyzed: webhook URL/secret, destination trust, bounded deduplication and dispatch semantics. |
| [PubSoft.Outbound](../src/platform/PubSoft.Outbound/PublicAPI.Unshipped.txt) | Analyzed: tenant/profile selectors, credentials, transport and retry ownership. |
| [PubSoft.Resilience.Http](../src/platform/PubSoft.Resilience.Http/PublicAPI.Unshipped.txt) | Analyzed: timeout/retry safety constraints and process-local circuit breaker. |
| [PubSoft.Scheduling](../src/platform/PubSoft.Scheduling/PublicAPI.Unshipped.txt) | Analyzed by domain: job authoring, permissions and operational bounds are host-owned. |
| [PubSoft.Scheduling.EntityFrameworkCore](../src/platform/PubSoft.Scheduling.EntityFrameworkCore/PublicAPI.Unshipped.txt) | Analyzed by domain: persisted job configuration and authorization are host responsibilities. |
| [PubSoft.Scheduling.Http](../src/platform/PubSoft.Scheduling.Http/PublicAPI.Unshipped.txt) | Analyzed: SSRF controls, redirects, proxy/private-network switches and job-supplied credentials. |
| [PubSoft.Tokens](../src/platform/PubSoft.Tokens/PublicAPI.Unshipped.txt) | Analyzed: machine-token signing keys, HTTP token endpoints, cache identity and opt-in replay; token-response size is not bounded by the package. |
| [PubSoft.Authentication](../src/security/PubSoft.Authentication/PublicAPI.Unshipped.txt) | Analyzed by domain: authentication abstractions and subject resolution; host identity lifecycle remains external. |
| [PubSoft.Authentication.Jwt](../src/security/PubSoft.Authentication.Jwt/PublicAPI.Unshipped.txt) | Analyzed: signing algorithms and token claims; see JWT risks and deployment checklist. |
| [PubSoft.Authentication.Jwt.AspNetCore](../src/security/PubSoft.Authentication.Jwt.AspNetCore/PublicAPI.Unshipped.txt) | Analyzed: bearer/Dual JWT validation, JWKS caching and DI lifecycle findings. |
| [PubSoft.Authentication.OAuthProviders](../src/security/PubSoft.Authentication.OAuthProviders/PublicAPI.Unshipped.txt) | Analyzed: state/PKCE/redirect inputs, provider credentials and host callback/account-linking boundary. |
| [PubSoft.Authorization](../src/security/PubSoft.Authorization/PublicAPI.Unshipped.txt) | Analyzed: grant filtering, verifier contracts and explicit endpoint coverage. |
| [PubSoft.Security](../src/security/PubSoft.Security/PublicAPI.Unshipped.txt) | Analyzed: HMAC, nonce, rate-limit and secret-resolution contracts. |
| [PubSoft.Security.Redis](../src/security/PubSoft.Security.Redis/PublicAPI.Unshipped.txt) | Analyzed: distributed nonce reservation and Redis availability/failover boundary. |
| [PubSoft.Testing](../src/testing/PubSoft.Testing/PublicAPI.Unshipped.txt) | Context only: test helpers are not runtime controls or evidence of consumer deployment security. |

The reconciliation does not claim that every one of the 3,558 declarations was security-reviewed individually. It establishes package-level accounting and points security-relevant groups to the analysis; "context only" is an explicit limitation, not a clean bill of health. Deployed HTTP endpoints are not enumerable from these library ledgers because consuming hosts define them.

This inventory makes the responsibility split explicit: the NuGet public API defines available mechanisms and opt-in switches; the consuming host chooses registrations, endpoint coverage, identity/secret providers and middleware order; deployment controls proxy reachability, broker/DB ACLs, egress, TLS and operational monitoring. The API ledgers are useful review inputs, but they are not security specifications and do not prove all combinations are safe.

## External Guidance Crosswalk

The OWASP API Security Top 10 (2023) is used here as a taxonomy for coverage questions, not as a finding count or a claim that every category is applicable.

| OWASP API risk | PubSoft surface to assess | Assessment / remaining proof |
|---|---|---|
| API1 Broken Object Level Authorization | Permission filters, tenant context, EF scope filters, Outbound tenant IDs | Mechanisms exist, but route/resource authorization, filter bypasses and consumer business lookups require host-level negative tests. |
| API2 Broken Authentication | JWT, Dual JWT/JWKS, HMAC, client credentials, Edge sessions | Several authentication mechanisms are provided; validate issuer/audience/key lifecycle, secret verification, replay behavior and session storage per deployment. R2-R4 were concrete conditional findings, fixed after the baseline. |
| API3 Broken Object Property Level Authorization | `ApiFieldAttribute.IsEncrypted`, DTO/contracts, audit value capture | Encryption metadata is descriptive and does not enforce field-level confidentiality or authorization. Review serialization, mass assignment and sensitive-field policy in each host. |
| API4 Unrestricted Resource Consumption | Gateway/custom rate limits, password work factor, request body bounds, queues and worker pools | Some controls are bounded and process-local; they do not establish cluster-wide fairness, concurrency limits, login abuse resistance or all payload/queue limits. R1 was a conditional quota-partition risk, fixed after the baseline. |
| API5 Broken Function Level Authorization | Permission endpoint/MVC filters, client-credential filters, Edge handler schemes | Opt-in filters and per-handler schemes must be applied consistently. Inventory endpoints and test unauthenticated, wrong-role, wrong-client and cross-tenant calls. |
| API6 Unrestricted Access to Sensitive Business Flows | Login/validate, OAuth flows, outbound tasks, Edge methods, administrative APIs | Business-flow throttling and transaction authorization are application-specific; verify quotas, abuse detection and step-up/approval controls in the consumer. |
| API7 Server Side Request Forgery | Scheduled HTTP, JWKS metadata/JWKS URI, OAuth provider configuration | Scheduled HTTP documents connect-time address pinning and per-hop redirect validation; host allowlists and disabling unsafe proxy combinations remain deployment choices. Keep identity metadata URLs trusted and fixed. |
| API8 Security Misconfiguration | `TrustAnyProxy`, forwarded header chain, `ReadHeader`, `UseSystemProxy`, OpenAPI, `None` scheme | High-impact options are public and some are explicitly documented as unsafe outside constrained topology. Verify effective production configuration, not just source defaults. R6 is conditional on proxy topology and was partially addressed after the baseline. |
| API9 Improper Inventory Management | Per-package public API ledgers, API docs, Swagger/OpenAPI, package consumers | Per-package ledgers and consumer smoke projects aid inventory. `scripts/sync-public-api.sh --check` exists, but the reviewed CI workflow does not explicitly run it; route inventories and external API versions remain host responsibilities. |
| API10 Unsafe Consumption of APIs | OAuth provider adapters, MQTT/Outbox, outbound HTTP, error propagation | Validate upstream TLS/identity, response schemas and trust boundaries; sanitize upstream errors and make external side effects idempotent. MQTT README records MQTT 5 dependency and at-least-once semantics. |

The external recommendations most directly relevant to the findings are: RFC 8725 requires application-selected JWT algorithms and issuer/audience validation where those claims distinguish recipients; Microsoft ASP.NET Core guidance says forwarded headers must come only from trusted proxies/networks and be processed before middleware that relies on them; OWASP multi-tenant guidance treats client-supplied tenant identifiers as selectors requiring server-side authorization; OWASP password guidance recommends adaptive hashing and balancing work factor against login-time denial-of-service risk. These recommendations inform the host checklist; they do not independently establish a PubSoft defect.

## Evidence Method and Limits

Public surface discovery used the per-project `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` ledgers, package READMEs, implementation and nearby tests. The ledgers are maintained by `Microsoft.CodeAnalysis.PublicApiAnalyzers`; [packaging guidance](packaging.md) documents `scripts/sync-public-api.sh --check` as an available drift check, while the reviewed [CI workflow](../.github/workflows/ci.yml) does not explicitly invoke it. This is a process gap to consider, not evidence that the checked-in ledger is currently wrong.

The generated [coverage matrix](test-coverage-matrix.md) reports 2,195/2,608 members (84%) and explicitly excludes interface members. Uncovered concrete implementations include EF permission/repository/data-scope providers, audit sinks/retention, OAuth registry/provider paths, Edge handlers/session store and Outbox/Inbox components. Treat this as a prioritization aid: uncovered does not mean vulnerable, and aggregate coverage does not measure security assertions. Security-critical follow-up should target invariants such as cross-tenant denial, missing filter behavior, JWT key removal, redirect revalidation, replay across replicas, and failure-closed secret/broker behavior.

External material was consulted on 2026-11-04. It supplies normative or community guidance, not evidence about consumer configuration. OWASP API Top 10 is a risk taxonomy, OWASP Cheat Sheets are implementation guidance, RFC 8725 is an IETF Best Current Practice, Microsoft Learn describes ASP.NET Core 10 behavior, and NIST SSDF is a development-process framework. No production deployment, runtime traffic, cloud account, or consumer package integration was tested.

## Prioritized Risks and Gaps

The following items were rechecked at baseline `81a7f52`. Severity is a consumer risk ranking, not a vulnerability advisory or CVSS score.

### R1: Unauthenticated Client ID Influences Gateway Rate-Limit Partitioning

**Severity: Medium; confidence: High.** In [GatewayRateLimitingExtensions.cs](../src/host/PubSoft.AspNetCore/GatewayRateLimitingExtensions.cs), the `/api/auth/validate` route reads `X-Client-Id` (a `Basic` authorization username is accepted as an equivalent source); any non-empty value selects the `validate-client` policy instead of the anonymous policy. The defaults are 2,000 and 10 requests per minute, respectively, and the client bucket is keyed by `clientId@IP`. The same identity also contributes to failure-limit and default-policy identity. Neither source proves a client credential was validated, so a caller can change its value to create separate partitions and weaken aggregate throttling. The general RateLimiter caps identifiers at 100,000; reaching that capacity rejects new identifiers. The risk is quota fragmentation and capacity consumption, not unbounded bucket creation.

**Preconditions/impact:** The host enables this Gateway policy, the validate route is reachable from an untrusted network, and the edge does not remove or authoritatively rewrite the header. This weakens anonymous abuse protection; the limiter alone does not demonstrate authentication bypass. **Recommendation:** Bind anonymous limiting to a trusted source; permit authenticated principal/client-based partitioning only after credential validation, and retain independent IP/global limits. Add regression tests for arbitrary header values and failure responses. **Status:** fixed after the review baseline — forged client identities are now bounded twice: a per-IP cardinality cap collapses over-budget IDs into the IP-only bucket, and unconditional `validate-ip`/`oauth-ip` aggregate policies bound total requests from one address.

### R2: JWKS Key-ID Cache Bypasses the JWKS TTL

**Severity: Medium; confidence: High.** `_keyCache` in [JwksKeyProvider.cs](../src/security/PubSoft.Authentication.Jwt.AspNetCore/JwksKeyProvider.cs) is a `ConcurrentDictionary` with no expiry. A key ID hit returns the cached key directly; `CacheMinutes` applies only to the JWKS set in `IMemoryCache`. After an old key is removed from JWKS, that process can therefore continue accepting tokens signed by the old key indefinitely, provided the other validation checks pass.

**Preconditions/impact:** The remote-JWKS Dual JWT path is used and the key has already been resolved in that process. Revocation response time is not bounded by configured `CacheMinutes`. **Recommendation:** Make per-key caching share JWKS lifetime or evict revoked keys after refresh; add simulated key-rotation/revocation tests and define an overlap period. **Status:** fixed after the review baseline — the per-key dictionary was removed; resolved keys now share the JWKS set's `CacheMinutes` lifetime and empty JWKS responses are cached for the TTL as well.

### R3: AuthServer JWT Scheme Does Not Validate Audience

**Severity: Medium; confidence: High, with deployment-dependent impact.** [DualJwtAuthenticationExtensions.cs](../src/security/PubSoft.Authentication.Jwt.AspNetCore/DualJwtAuthenticationExtensions.cs) sets `ValidateAudience = false` for `AuthServerJwks`. If one issuer issues tokens with different audiences for multiple APIs, this API may accept a token not intended for it.

**Preconditions/impact:** The scheme is enabled, the issuer uses audience separation, and no upper layer enforces the intended audience. Issuer, signature, and lifetime are still validated; this finding does not mean arbitrary tokens can be forged. **Recommendation:** Configure and validate the expected audience, or document why AuthServer tokens share an equivalent audience boundary, and add cross-audience rejection tests. **Status:** fixed after the review baseline — `JwksOptions.Audience` was added; when set the scheme validates `aud` (and `ValidAlgorithms` is pinned to RS256). An unset audience still skips validation, so the residual exposure is an explicit host choice.

### R4: JWKS Provider Is Resolved Through a Second DI Container

**Severity: Low to Medium; confidence: High.** The same extension captures a `Lazy<JwksKeyProvider>` in the authentication options callback and creates it via `services.BuildServiceProvider()`. This constructs a provider from service descriptors instead of resolving from the actual host container; singleton, cache, and resource-disposal lifetimes can split. Possible consequences include inconsistent dependency instances/configuration and provider/HttpClient resources not being disposed with the application container.

**Preconditions/impact:** Applies only to Dual JWT configured with AuthServer JWKS; concrete resource impact depends on registrations. **Recommendation:** Use an authentication runtime service resolved by the real host container, or another integration that does not build a second provider; verify lifecycle and disposal. **Status:** fixed after the review baseline — the `Lazy`/`BuildServiceProvider()` path was replaced by `AddOptions<JwtBearerOptions>(AuthServerScheme).Configure<JwksKeyProvider>(...)`, so the provider resolves through the host container with normal DI lifetimes.

### R5: HMAC Query-String Documentation Conflicts with Behavior

**Severity: Low (documentation consistency); confidence: High.** The [ASP.NET Core README](../src/host/PubSoft.AspNetCore/README.md) says query strings are not signed. However, [HmacValidationMiddleware.cs](../src/host/PubSoft.AspNetCore/HmacValidationMiddleware.cs) passes `Request.QueryString.Value`, [HmacSignatureValidator.cs](../src/security/PubSoft.Security/HmacSignatureValidator.cs) includes it in the signing string (raw `?…` form concatenated to the path), and `ValidateAsync_BindsQueryStringIntoSignature` in [HmacSignatureValidatorTests.cs](../tests/PubSoft.Host.Tests/HmacSignatureValidatorTests.cs) verifies that a different query does not match.

**Impact:** The README can mislead client implementations and security reviews, causing interoperability problems or incorrect assumptions. The current implementation does bind the query; this is not a finding that the query is unsigned. **Recommendation:** Align the ASP.NET Core README, Security Model, signing-format description, and consumer examples. Any format change should be versioned. **Status:** resolved in the checkout carrying this document — the AspNetCore and `PubSoft.Security` READMEs (en/zh-CN) now describe the signed `path+rawQuery` format.

### R6: Forwarded Headers and IP Limiting Depend on a Trusted Gateway

**Severity: Medium (conditional); confidence: High.** Gateway limiting clears Forwarded Headers `KnownIPNetworks`/`KnownProxies` and removes the hop limit (`ForwardLimit = null`) to support a trusted front-gateway topology; code comments and the [ASP.NET Core README](../src/host/PubSoft.AspNetCore/README.md) require the gateway to strip externally supplied `X-Forwarded-For`. If the backend is directly reachable or the proxy passes through client-supplied values, callers may influence the limiting identity and audit logic that relies on IP. `ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders` detects one common misconfiguration at runtime (private socket peer combined with a public advertised client) and both rate-limiting middlewares emit a one-time warning, but detection is not prevention.

**Recommendation:** Restrict backend reachability to trusted proxies; overwrite rather than append untrusted headers; verify middleware order and trust for each proxy hop; validate the effective server-observed `RemoteIpAddress` through a real proxy deployment test. **Status:** partially addressed after the review baseline — `GatewayRateLimitOptions.TrustedProxies`/`TrustedNetworks`/`ForwardLimit` now allow pinning trusted peers (malformed entries fail startup); with both lists empty the trust-any behavior remains for gateway-only topologies, so the deployment preconditions still apply.

## Additional Boundary Details

### Identity, Authorization, and Tenancy

- The [JWT ASP.NET Core guide](../src/security/PubSoft.Authentication.Jwt.AspNetCore/README.md) describes the ordinary bearer adapter's issuer, audience, algorithm, and key-source validation. AuthServer Dual JWT is a separate implementation path; do not assume the ordinary adapter's guarantees apply to it.
- `AuthenticatedSubjectResolver` maps an authenticated principal to a subject; account status, role interpretation, tenant authorization, and endpoint coverage remain host concerns. An authorization filter that is not explicitly registered does not protect an endpoint.
- [HttpContextTenantContext.cs](../src/host/PubSoft.AspNetCore/HttpContextTenantContext.cs) resolves ambient scope first, then — when `ReadHeader` is enabled — the configured tenant header, then claims. Header reading is explicit opt-in, and even if the proxy sanitizes the header, the host must verify that the principal may select the tenant.
- [PubSoftDbContext.cs](../src/data/PubSoft.EntityFrameworkCore/PubSoftDbContext.cs) establishes named tenant/member/soft-delete filters. The member filter is `!IsMemberScoped || (MemberId.HasValue && MemberId == current)`, so a member-scoped context without a member ID returns no rows. Scope filters can be explicitly bypassed; parameterless EF `IgnoreQueryFilters()` also bypasses soft-delete filtering. Tests cover representative queries and repository scope, but cannot prevent host business code from intentionally using a bypass.

### Keys, Passwords, HMAC, and Replay

- [Security and Cryptography](security-cryptography.md) documents AES-GCM envelopes, PBKDF2, migration-only CBC reads, and constant-time comparisons. Key generation, custody, access audit, rotation, backup, and recovery belong to the host/key-management service.
- [HMAC middleware](../src/host/PubSoft.AspNetCore/HmacValidationMiddleware.cs) excluded paths are explicit bypasses matched on path-segment boundaries; body hashing is on by default (`RequireBodyHash`) and bounded (default 1 MiB, hard cap 16 MiB). The host supplies secrets through `IAppSecretResolver`; an unavailable resolver fails closed with 503 and an unresolved app ID fails closed with the same 401 as a signature mismatch, avoiding an appId-enumeration oracle.
- The default `MemoryNonceStore` prevents replay only within one process. [RedisNonceStore](../src/security/PubSoft.Security.Redis/README.md) atomically reserves one nonce with NX/TTL, but instances must share a Redis cluster, database, and prefix. Redis failover may lose acknowledged writes, so deployment validation is still required.
- Edge's MD5 scheme is a legacy compatibility surface; new clients should use HMAC. Timestamp window, signing path, raw query encoding, nonce scope, and proxy rewrites must match between both sides.

### Data, Audit, and Messaging

- EF scope filters and repository checks provide mechanisms, not least-privilege DB accounts, migration authorization, tenant-catalog protection, or cross-tenant operational approval.
- Audit actor mapping belongs to the host; IP, UA, actor, and property-value capture should be enabled only for an explicit privacy purpose. Sensitive-field configuration, audit access, and retention need host governance.
- The [Outbox guide](../src/messaging/PubSoft.Messaging.Outbox/README.md) explicitly states at-least-once delivery: a database Inbox transaction cannot include email, HTTP, or another system's commit. External operations need idempotency keys or separate coordination; multi-tenant worker scope providers must be authorized and initialized correctly. `InboxRetentionCleanup` provides a bounded deletion primitive but is not automatically scheduled.

### External I/O, Error Responses, and Resource Bounds

- [Scheduled HTTP](../src/platform/PubSoft.Scheduling.Http/README.md) blocks private addresses by default, resolves and pins targets at connect time, and validates each manual redirect hop (up to 8). Enabling internal targets or system proxies changes the boundary and requires review; options validation rejects `UseSystemProxy` together with `BlockPrivateNetworks` so a proxy cannot silently bypass connect-time checks. Host controls over job creation/editing and sensitive header injection remain necessary.
- [Outbound](../src/platform/PubSoft.Outbound/README.md) requires an explicit tenant ID but does not authorize tenant/profile selection; transport, credentials, retry, log redaction, and provider lifecycle belong to the host.
- The [Edge guide](../src/host/PubSoft.Edge/README.md) describes exposure boundaries for OpenAPI method catalogs, Swagger UI CDN assets, and upstream diagnostic details in `subMessage`. Production hosts should restrict visibility and avoid returning internal transport details to end users.
- Many queues, caches, audit collectors, and limiters have capacity limits and reject when full. These are availability bounds, not cluster-wide fairness or denial-of-service protection. Hosts also need request-body, concurrency, timeout, connection, and global quota controls.

## Potential Vulnerability Inventory

This section extends the audit from confirmed findings (R1-R6) to a systematic inventory of *potential* vulnerability points, compiled per package from its functionality and public API surface, plus upstream threat intelligence for third-party dependencies. A listed point is a review target with stated preconditions — not a confirmed defect. Items already covered by the findings or the controls matrix are cross-referenced rather than repeated. CWE/OWASP tags aid external tooling alignment.

### Vulnerability-Class Crosswalk

| Class (CWE / OWASP) | Primary PubSoft surfaces | Status and boundary |
|---|---|---|
| Auth bypass / confused deputy (CWE-287/-441; API2) | `PermissionEndpointFilter`, `ClientCredentialEndpointFilter`, Dual JWT, Edge schemes, `IClientCredentialVerifier` | Library enforces only what is registered; uncovered routes and host verifier correctness are the residual risk. |
| Tenant isolation breach (CWE-639; API1) | `HttpContextTenantContext`, EF named filters, `IOutboundSdk` tenant selector, Outbox scope provider | Read-side is filtered; write-side stamping only fills *null* TenantIds — host code assigning a foreign `TenantId` on `Added` entities persists cross-tenant (host trust boundary). Filter bypasses remain privileged. |
| Injection (CWE-89/-94; API3/API8) | Path/header inputs, job config, topic strings, EF `IgnoreQueryFilters`, raw SQL in host code | No library query builder accepts raw input; Redis Lua is fixed-parameter. Host raw SQL/Redis scripting is out of scope. |
| Crypto misuse (CWE-327/-329) | SM4 ECB (documented compat-only), CBC migration reads, `IEncryptionKeyRing`, PBKDF2 work factor, HS256 secrets | AES-GCM envelopes are the sanctioned path; weak key-ring implementations, low iteration counts, and ECB/legacy reuse are host-controlled. |
| SSRF (CWE-918; API7) | `PubSoft.Scheduling.Http` targets, `HttpTokenProvider` URL, `JwksUri`, notification webhook URLs, OAuth provider endpoints | Scheduled HTTP is the hardened path; the other outbound URLs are operator-config trust surfaces — restrict who edits them. |
| Replay (CWE-294) | HMAC nonce stores, JWT lifetime, Edge timestamp tolerance, token 401 retry | Atomic shared nonce store + skew bounds covered; JWT `nbf/exp` enforced; in-memory stores are single-process only. |
| DoS / resource exhaustion (CWE-400; API4) | Rate-limit identifier capacity, bounded collectors/queues, PBKDF2 cost, Scheduled-HTTP response body, body-hash buffer | Most sinks are bounded and fail closed. Gaps: Scheduled-HTTP has no package-level response-body cap; identifier-capacity exhaustion rejects new identifiers (attacker can starve *new* clients); PBKDF2 cost trades against login DoS. |
| Deserialization / schema (CWE-502) | MQTT payloads, Outbox/Inbox payloads, token JSON path extraction, Edge envelopes, Redis-cached blobs | Payload bounds exist on MQTT/Edge; library does not polymorphic-deserialize; host serializers decide content safety. |
| Info disclosure / log leakage (CWE-532) | `subMessage` diagnostics, dev exception detail, audit value capture, correlation headers, `X-Fend-*` handling | Sanitized defaults exist; operator-facing diagnostics and audit capture need review before reaching end users/logs. |
| Concurrency / TOCTOU (CWE-367/-362) | Fencing tokens, nonce atomicity, in-flight failure counting, `Lazy` JWKS provider (removed), `Change()` ambient scopes | Atomic Redis scripts and fencing are provided; in-memory variants are single-process; scope leaks across `async` flows are host-managed. |
| Misconfiguration (CWE-16; API8) | `TrustAnyProxy`, empty gateway trust lists, `ReadHeader`, `UseSystemProxy`, `AllowInsecureJwksUri`, `none` scheme, MD5 | High-impact options are opt-in or documented; several now validate at startup. Production config review remains the control. |
| Supply chain / build (CWE-494; API9) | NuGet advisories, gitleaks, pinned actions, public API ledgers, consumer smoke | NU1903/NU1904 are hard errors; ledger drift check now runs in CI. No signed releases/SBOM yet. |
| Availability via upstream failure | JWKS fetch failure → all AuthServer tokens rejected; Redis outage → nonce store fails closed; broker outage → Outbox retries | Fail-closed by default — secure, but an availability lever worth runbook coverage. |

### Per-Package Potential-Vulnerability Points

**`src/core/`**

| Package | Potential points (precondition → impact) |
|---|---|
| PubSoft.Contracts | `ApiFieldAttribute.IsEncrypted` mistaken for enforcement → sensitive fields serialized unprotected (host misuse). |
| PubSoft.Core | Sequential/snowflake identifiers aid IDOR enumeration → object-level authorization must not rely on unguessable IDs (host). |
| PubSoft.Cryptography | Custom SM4/AES implementations outside AEAD path → self-implemented crypto needs review; ECB reveals block patterns (compat-only). **CBC migration oracle — verified clean**: every legacy-decrypt failure (base64, length, PKCS7, UTF-8) collapses into one `InvalidLegacyCiphertext`; no type/message differentiation. Residual CBC timing is bounded by the trusted-stored-data contract — never expose `Decrypt` on untrusted input. |
| PubSoft.DependencyInjection | Registration order/substitution — a host registering `MemoryNonceStore` over Redis silently downgrades replay protection to single-process (composition risk). |
| PubSoft.Domain | `AmbientTenantContext`/`Change()` leaks across async continuations or pooled tasks → wrong-tenant reads/writes in background flows (host lifecycle). |
| PubSoft.Events | Handler exceptions/malicious handlers run in-process; event payloads crossing tenant scope → context mismatch (application-specific). |
| PubSoft.Identity | Account/credential contract — error differentiation enables user enumeration; password policy and lockout live host-side. |
| PubSoft.Logging | Structured-log injection via untrusted values; secrets in message templates (host discipline). |
| PubSoft.Mapping | Mapping configuration exposing sensitive properties → over-posting/field leakage (host-specific). |
| PubSoft.MultiTenancy | Management contract has no admin authorization → unprotected create/update/delete if wired naively. |
| PubSoft.Resilience | Retry/breaker state is process-local → split-brain decisions across replicas; retry storms if `ShouldRetry` configured permissively. |

**`src/data/`**

| Package | Potential points (precondition → impact) |
|---|---|
| PubSoft.Auditing | Mis-configured sensitive-property masking captures secrets/PII; bounded collector drops overflow events → audit-integrity gap under flood (availability vs. integrity trade-off to size). |
| PubSoft.Auditing.Outbox | Audit rows tamperable via DB access; delivery/replay ordering with retention cleanup (operational control). |
| PubSoft.EntityFrameworkCore | **Write guard now enforced**: `Added`/`Modified` entities outside the current tenant/member scope throw; `Deleted` entities carrying an explicit foreign scope id also throw. Residual: key-only delete stubs carry no scope to verify — scope-safe deletes need a filter-verified load or host check. Parameterless `IgnoreQueryFilters()` also lifts soft-delete; raw SQL/`FromSql` bypasses all filters. |

**`src/host/`**

| Package | Potential points (precondition → impact) |
|---|---|
| PubSoft.AspNetCore | Exception detail/stack in Development leaks internals if dev mode reaches production; correlation/client-ID headers trusted post-verification only; `ExcludedPathPrefixes` too broad silently unsigns routes; `DevelopmentOnly` convention mistaken for authorization; `IsEncrypted`-style metadata misread as enforcement. |
| PubSoft.AspNetCore.Docs | Method catalog = full route table — `DevelopmentOnly` bypassed or misconfigured exposes surface map. |
| PubSoft.Edge | Pseudo-path method override — if any proxy rewrites path, signed method and routed method diverge (canonicalization risk, documented); `none` scheme on non-public handlers; MD5 for new clients; `subMessage` upstream detail to end users. |
| PubSoft.Edge.Client | Constructor defaulting to `None` → silently unauthenticated client when secret-bearing ctor is skipped (choose scheme explicitly). |
| PubSoft.Edge.Core | Envelope/payload bounds and canonical-signing divergence between gateway and client after proxy normalization (see HMAC boundary notes). |

**`src/messaging/`**

| Package | Potential points (precondition → impact) |
|---|---|
| PubSoft.Messaging | Contract-only: untrusted payload shapes, oversized envelopes, handler authorization — consumer-specific. |
| PubSoft.Messaging.Mqtt | `mqtt://` plaintext exposes credentials/messages (must use `mqtts://`); password held as managed string (upstream design note — cannot be scrubbed from heap); MQTT 3.1.1 silently drops dedup metadata → replay/duplicate processing; topic strings containing tenant data leak in wildcard subs (broker ACLs). |
| PubSoft.Messaging.Outbox | At-least-once duplicates external effects without idempotency keys; poison message retries indefinitely if no dead-letter policy; `IOutboxScopeProvider` enumerating wrong tenants → cross-tenant dispatch. |

**`src/platform/`**

| Package | Potential points (precondition → impact) |
|---|---|
| PubSoft.Caching | Shared cache namespace across tenants → key collision/data bleed (host key design). |
| PubSoft.Caching.Redis | Fencing token discarded → stale lock holder writes after lease loss; untrusted blob deserialization from shared cache; cluster `KEYS`-based cleanup patterns scan keyspace under load; TLS/AUTH misconfig in connection string. |
| PubSoft.Modularity | Module registration order/config composition changes effective services (composition review). |
| PubSoft.Monitoring / PubSoft.Monitoring.Scheduling | Unauthenticated health/metrics endpoint leaks topology and job internals → map and authorize it. |
| PubSoft.Notifications | Webhook URL is a low-trust-config SSRF/exfil vector — **now enforced**: non-loopback `http` webhook URLs fail options validation, per-channel `AllowInsecureUrl` is the documented opt-out; destination changes must remain operator-gated. DingTalk secret in config; dispatch content may carry PII to third-party endpoints; process-local dedup duplicates sends across replicas. |
| PubSoft.Outbound | Tenant/profile selectors without entitlement checks → cross-tenant invocation; provider credentials/log redaction host-side; upstream response trusted implicitly. |
| PubSoft.Resilience.Http | Total-timeout misconfiguration against business deadlines; per-attempt retry multiplies upstream load (budget review). |
| PubSoft.Scheduling / PubSoft.Scheduling.EntityFrameworkCore | Low-trust job editing → arbitrary recurring side effects; persisted job rows tamperable via DB access; cron/config validation gaps (host authoring control). |
| PubSoft.Scheduling.Http | Covered: SSRF controls, redirect re-validation, credential-drop on origin change, proxy/private-network validation. Residual: no package-level response-body cap (memory); job-supplied headers may carry secrets to attacker-chosen URLs (authoring control); DNS answers changing between resolve and connect mitigated by pinning — verify resolver trust (DoH/hosts). |
| PubSoft.Tokens | Configurable token endpoint URL = credential exfiltration target if config is low-trust — **now enforced**: non-loopback `http` endpoints fail options validation, per-credential `AllowInsecureUrl` is the documented opt-out; token response has no size cap; 401-replay buffers bodies (memory); HS256 machine-token secret entropy below 256-bit would weaken forgery resistance (validate key material). |

**`src/security/`**

| Package | Potential points (precondition → impact) |
|---|---|
| PubSoft.Authentication | Subject resolution trusts issuer claim mapping; account-status/role semantics host-side. |
| PubSoft.Authentication.Jwt | Symmetric signing keys below recommended entropy; `kid` collisions across key sources (pinning covers algorithm; key-source separation remains host config). |
| PubSoft.Authentication.Jwt.AspNetCore | Post-fix residuals: unset `AuthServer:Audience` still skips validation — now logs a startup warning naming the exposure (explicit host choice); `ForwardDefaultSelector` routes on attacker-chosen `alg` — strict validation contains it, but any future scheme added must pin its own algorithm; JWKS outage → all AuthServer tokens rejected (availability). |
| PubSoft.Authentication.OAuthProviders | State/nonce not bound to user-agent transaction → CSRF/login-CSRF; missing PKCE; un-allowlisted redirect URIs → code exfiltration; auto account-linking on unverified provider emails → account takeover (all host flow decisions). |
| PubSoft.Authorization | Grant/verifier implementations returning broad scopes; module/flag key typos fail closed (safe direction); wildcard-like semantics must not be introduced by host resolvers. |
| PubSoft.Security | Timestamp skew 300 s widens replay window on clock drift; `MemoryNonceStore` registered where replicas exist; identifier-capacity exhaustion rejects *new* identifiers — **verdict: fail-closed deny retained by design**; starvation hits only newcomers while established budgets stay enforced, whereas LRU eviction would let an attacker flush every window (bound identifiers to verified sources); PBKDF2 minimum iteration vs login CPU cost. |
| PubSoft.Security.Redis | Redis outage returns deny (fail closed — availability lever); shared Redis without prefix/database isolation → nonce key collision across services. |

**`src/testing/`** — `PubSoft.Testing`: helpers must never ship into production wiring; internals visible only to test assemblies (verified by InternalsVisibleTo targets).

### Dependency Threat-Intelligence Watchlist

Runtime third-party surface (central versions from `Directory.Packages.props`), checked against public advisories at review time; NU1903/NU1904 already hard-gates these on restore:

| Package | Version | Advisory status / note |
|---|---|---|
| MQTTnet | 5.2.0.1603 | No package-level GHSA/CVE found. Note: vulnerable-history code paths were `MQTTnet.AspNetCore`/`MQTTnet.Extensions.WebSocket4Net` — **not referenced**; client password is a managed string (cannot be heap-scrubbed). |
| StackExchange.Redis | 3.4.29 | No client-side GHSA/CVE found; scanner hits for CVE-2021-32626/2022-24735 etc. are Redis *server* issues (false positives). Configure `ssl`, AUTH, certificate validation deliberately. |
| Microsoft.IdentityModel.* / System.IdentityModel.Tokens.Jwt | 8.23.0 | CVE-2024-21319 (JWE decompression DoS) patched ≥ 7.1.2 — current versions are above the floor. |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.14 | No applicable advisory at check time; metadata fetch is operator-configured. |
| Microsoft.EntityFrameworkCore(.Relational/.Sqlite) | 10.0.14 | No applicable advisory at check time; host raw SQL outside EF filters is the real boundary. |
| BouncyCastle.Cryptography | 2.7.0 | Test scope only — not shipped in any runtime package. |
| MySql.EntityFrameworkCore | 10.0.12 | Test/integration scope only. |

Watchlist hygiene: re-run `dotnet list PubSoft.sln package --vulnerable --include-transitive` on dependency bumps; GHSA/NVD feeds should be rechecked before each release since advisory state is volatile.

## Test and Supply-Chain Evidence

The following checks were run at this review baseline:

| Evidence | Result | What it does not prove |
|---|---:|---|
| `tests/PubSoft.Security.Tests` (SDK 10.0.402) | 557 passed, 0 failed | Does not cover every Dual JWT/JWKS revocation case, production identity provider, or secret-store configuration. |
| `SchedulingHttpTests` in `tests/PubSoft.Platform.Tests` (SDK 10.0.402) | 62 passed, 0 failed | Does not prove production DNS, proxy, cloud network policy, or target-service behavior. |
| `tests/PubSoft.Host.Tests`, `tests/PubSoft.Data.Tests`, and CI | Configured to run in CI; not rerun in full during this review | Should not be described as fully verified by this review. |

The [CI workflow](../.github/workflows/ci.yml) configures Gitleaks (image pinned by digest), Redis Cluster and MySQL 8.4, solution tests, a 70% union line coverage gate, pack/consumer/AOT checks, and dependency vulnerability reporting. [Directory.Build.props](../Directory.Build.props) treats high/critical NuGet advisories NU1903/NU1904 as errors. These gates are not equivalent to dedicated SAST/DAST, SBOM/artifact signing, third-party audit, or production validation of secrets, proxies, and failover. This review did not establish whether those controls exist outside the repository.

## Host Deployment Checklist

- Define accepted issuers, audiences, signing algorithms, key rotation/revocation goals, and token purpose; test ordinary bearer and Dual JWT paths separately.
- Verify authentication and permission filters for every endpoint; validate the relation among subject, tenant, client code, and business resource.
- Do not treat tenant/client identity headers as trusted by default. If required, use them only after proxy sanitization and identity verification, and retain IP/global limits.
- Restrict backend reachability to trusted proxies, overwrite external forwarding headers, and process them in the correct order. From an external network, verify spoofed headers cannot change the server-observed source IP.
- Use approved secret/KMS storage and exercise rotation, compromise response, and recovery. For multi-instance HMAC, use an atomic shared nonce store and validate failover.
- Review `IgnoreScopeFilters()`, `IgnoreQueryFilters()`, background workers, and admin endpoints for tenant data access; protect cross-tenant operational paths.
- Apply egress allowlists/network policy to job targets. Do not let low-trust job authors choose internal addresses, credential headers, or unsafe retries by default.
- Define audit fields, sensitive-value masking, access control, retention, and deletion. Make external message side effects idempotent.
- When pairing cookie-based authentication with `PubSoft.AspNetCore`, add ASP.NET Core antiforgery yourself — the package ships none; state-changing cookie-auth routes without it are CSRF-exposed.
- Disable or protect non-development Edge OpenAPI/Swagger, `none` methods, and detailed errors; do not use legacy MD5 for production clients.
- Include Redis/MySQL failure, proxy-chain behavior, limiter capacity, timeouts/concurrency, and dependency advisories in runbooks and monitoring.

## Blind Spots and Follow-up

This analysis does not verify a specific consumer application, cloud account, real gateway configuration, production key permissions, database accounts, third-party OAuth provider, broker ACL, Redis durability, container image provenance, or release operations. Source review and current tests also cannot establish real middleware order, endpoint coverage, operator permissions, or runtime alert quality.

All six findings were resolved or mitigated after the review baseline (per-finding Status notes above); revalidating host deployments against the new options remains necessary. Further follow-up: consider static analysis, dependency source mapping/supply-chain provenance, and integration tests against production topology.

## Primary Evidence

- [Design Principles](design-principles.md) · [Security and Cryptography Model](security-cryptography.md) · [Permission Client Scope](permission-client-scope.md) · [Security Policy](../SECURITY.md)
- [JWT signing](../src/security/PubSoft.Authentication.Jwt/JwtSigning.cs) · [Dual JWT](../src/security/PubSoft.Authentication.Jwt.AspNetCore/DualJwtAuthenticationExtensions.cs) · [JWKS provider](../src/security/PubSoft.Authentication.Jwt.AspNetCore/JwksKeyProvider.cs) · [Permission authorization](../src/security/PubSoft.Authorization/PermissionAuthorization.cs)
- [HMAC middleware](../src/host/PubSoft.AspNetCore/HmacValidationMiddleware.cs) · [Gateway rate limiting](../src/host/PubSoft.AspNetCore/GatewayRateLimitingExtensions.cs) · [Tenant context](../src/host/PubSoft.AspNetCore/HttpContextTenantContext.cs) · [EF scope context](../src/data/PubSoft.EntityFrameworkCore/PubSoftDbContext.cs)
- [Security tests](../tests/PubSoft.Security.Tests) · [Host tests](../tests/PubSoft.Host.Tests) · [Data tests](../tests/PubSoft.Data.Tests) · [Platform tests](../tests/PubSoft.Platform.Tests) · [CI](../.github/workflows/ci.yml)

## External References

- [OWASP API Security Top 10 (2023)](https://owasp.org/API-Security/editions/2023/en/0x11-t10/)
- [OWASP REST Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html) · [Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html) · [Multi-Tenant Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.html)
- [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html) · [Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [Microsoft: Configure ASP.NET Core behind proxies and load balancers (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) · [JWT bearer authentication (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0) · [Rate limiting (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [RFC 8725: JSON Web Token Best Current Practices](https://www.rfc-editor.org/rfc/rfc8725) · [RFC 9700: OAuth 2.0 Security Best Current Practice](https://www.rfc-editor.org/rfc/rfc9700)
- [NIST SP 800-218: Secure Software Development Framework (SSDF) v1.1](https://csrc.nist.gov/pubs/sp/800/218/final)
