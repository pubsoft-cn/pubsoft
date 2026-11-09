---
purpose: package-level hosting guide for PubSoft.Core
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Core

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/core/PubSoft.Core/README.zh-CN.md)


`PubSoft.Core` contains framework-neutral value types and helpers for common application code. It does not define HTTP response handling, persistence, or business-specific validation policy.

## Result Values

Use `Result<T>` when an operation returns an expected failure as data rather than throwing for it:

```csharp
using PubSoft.Core;

Result<int> parsed = int.TryParse(input, out var value)
    ? Result<int>.Ok(value)
    : Result<int>.Fail("invalid_number", "The value is not an integer.");

Result<string> label = parsed.Map(number => $"Item {number}");
```

`Map` and `Bind` propagate an existing error and reject a null selector. Exceptions thrown by a selector are not converted into failures. Accessing `Value` on a failed result throws `InvalidOperationException`; inspect `IsOk` or use `Unwrap` when a fallback is appropriate. Error codes and messages are caller-defined and are not automatically localized or sanitized.

## Money

`Money` stores a signed `long` count of minor units (`Fen`), avoiding binary floating-point arithmetic:

```csharp
var amount = Money.FromYuan(12.34m);
Money fee = amount.AllocatePercent(2.5m);
decimal yuan = amount.ToYuan();
```

`FromYuan` and `AllocatePercent` round midpoint values away from zero. `AllocateBps` truncates fractional minor units toward zero and does not validate a business-specific basis-point range. Addition, subtraction, unary negation, and integer multiplication are checked and throw `OverflowException` rather than wrapping. Currency identity and allocation remainder policies are outside this type.

## Guards and Specifications

`Guard` provides small throwing checks. `NotEmpty` rejects null, empty, or whitespace-only strings but returns the original string without trimming. `Phone` only checks that a value contains at most 16 digits; it does not validate a country-specific phone format. Failed checks throw `DomainException`.

Derive `Specification<T>` to collect criteria, includes, ordering, grouping, and optional paging for a repository or query adapter. `ApplyPaging` rejects negative skip/take before changing the paging state; zero is valid. The specification records expressions but does not execute or translate them. The consuming adapter owns provider translation and query limits.

## IDs and Response Shapes

`SnowflakeIdGenerator` generates JavaScript-safe 53-bit IDs using a 2024-01-01 UTC epoch, a 5-bit worker ID (0–31), and a 7-bit per-millisecond sequence. Inject a `TimeProvider` for deterministic tests. The clock must not move backwards, and the timestamp must remain within the generator's supported window. Assign worker IDs uniquely to concurrently active generators sharing an ID space; the generator's lock only coordinates calls on that one instance. Exhausting all 128 sequence values in the final supported millisecond throws instead of wrapping.

DI registration goes through `AddPubSoftIdGenerator` (`PubSoft.DependencyInjection`): `services.AddPubSoftIdGenerator(workerId)` registers a shared `SnowflakeIdGenerator` as `IIdGenerator` with eager 0–31 range validation, `services.AddPubSoftIdGenerator(o => o.WorkerId = …)` takes options, and `services.AddPubSoftIdGenerator(configuration)` binds `PubSoft:IdGenerator:WorkerId` and fails closed when the key is missing — a worker id is never guessed, because two replicas sharing one collide silently under load. Hosts that replace Snowflake entirely register their own `IIdGenerator` instead (TryAdd yields).

`ApiResponse<T>` and `ApiResponse` are mutable response-shape DTOs: code `0` represents success, and `Fail` rejects code `0`. They do not set HTTP status codes, define a global error-code registry, or enforce serialization conventions. `PagedResultDto<T>` carries a mutable item list and total count without imposing paging semantics.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Core`: `ApiResponse` `ApiResponse<T>` `DomainException` `Gender` `Guard` `ISpecification<T>` `Money` `PagedResultDto<T>` `Result<T>` `ResultError` `SnowflakeIdGenerator` `Specification<T>`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**(options & records)**

`Criteria`, `GroupBy`, `HelpLink`, `HResult`, `IsPagingEnabled`, `Items`, `Message`, `OrderBy`, `OrderByDescending`, `Source`, `StatusCode`, `TotalCount`, `TraceId`

<!-- pubsoft:api-surface:end -->

