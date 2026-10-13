# PubSoft.Resilience.Http

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Resilience.Http` adds bounded timeout, retry, and optional circuit-breaker behavior to a named `HttpClient`. The handler does not retry unsafe methods or requests with content, and there is no unsafe-retry override.

## Registration

```csharp
using PubSoft.Resilience;
using PubSoft.Resilience.Http;

services.AddHttpClient("catalog", client =>
    client.BaseAddress = new Uri("https://catalog.internal/"))
    .AddPubSoftHttpResilience(options =>
    {
        options.Timeout = TimeSpan.FromSeconds(8);
        options.PerAttemptTimeout = TimeSpan.FromSeconds(3);
        options.Retry = new RetryPolicy
        {
            MaxRetries = 2,
            InitialDelay = TimeSpan.FromMilliseconds(200),
            RetryOn5xx = true,
            RetryOnConnectionFailure = true,
            RetryOnTimeout = true
        };
        options.CircuitBreaker = new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            OpenTimeout = TimeSpan.FromSeconds(30)
        };
    });
```

The default total timeout is eight seconds; the default per-attempt timeout is five seconds. Per-attempt timeout must not exceed total timeout. The handler disables `HttpClient.Timeout` and owns these timeout budgets. Caller cancellation is propagated and is not treated as a retryable timeout.

## Retry Safety

Retries are limited to requests with no content and one of these methods: GET, HEAD, OPTIONS, or TRACE. The retry policy can select 5xx responses, connection failures, and timeouts; 4xx responses are not retried. `MaxRetries` counts retries after the initial attempt. Backoff doubles from `InitialDelay` and is capped at 30 seconds; all delays still consume the total timeout budget. Intermediate retry responses are disposed.

The method/body safety check cannot establish application-level idempotency. Only enable retries for endpoints whose semantics are safe to repeat. POST/PUT/DELETE and any request carrying content are never retried by this handler, even if a retry policy is configured. Use a separate, explicitly reviewed design if a non-safe operation needs replay; do not assume this handler can be overridden through options.

## Circuit Breaker

When configured, the breaker is keyed by request host. Successful responses close/reset the host's failure state; failed responses, exhausted transport/timeouts, and total-timeout failures count toward the threshold. An open breaker rejects the request with `HttpRequestException`. Caller cancellation releases a half-open probe so a canceled request does not strand the breaker. Breaker state is in process and tied to the handler's `CircuitBreaker` instance; it is not a distributed coordination mechanism.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Resilience.Http`: `HttpResilienceOptions` `HttpResilienceServiceCollectionExtensions` `PubSoftHttpResilienceHandler`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~HttpResilienceHandlerTests
bash scripts/verify-package-consumer.sh
```