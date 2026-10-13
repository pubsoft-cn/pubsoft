# PubSoft.Mapping

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Mapping` is a dependency-free object mapper that compiles and caches expression-based delegates. It does not scan assemblies or discover mappings automatically. Register explicit configuration during application startup when name matching is not sufficient.

## Automatic Mapping

Matching public source/destination properties are mapped by exact name. The mapper also supports the common `Ms` suffix in either direction: destination `CreatedAtMs` can read source `CreatedAt`, and destination `CreatedAt` can read source `CreatedAtMs`.

```csharp
var dto = ObjectMapper.Map<Order, OrderDto>(order);
var dtos = ObjectMapper.MapList<Order, OrderDto>(orders);
```

`MapList` enumerates the source once and preserves null elements for reference-type destinations. A null source passed to `Map` returns the destination default.

## Explicit Configuration

Register destination expressions, constants, or ignored properties before mapping:

```csharp
ObjectMapper.Register<Order, OrderDto>(mapping => mapping
    .Map(destination => destination.DisplayName, source => source.Customer.Name)
    .Const(destination => destination.Currency, "USD")
    .Ignore(destination => destination.InternalNotes));
```

Selectors must point directly to publicly writable destination properties. Invalid destination selectors fail during registration. Re-registering a type pair replaces its compiled mapping, so perform configuration at startup rather than racing configuration with application requests.

## Type Converters

Register a converter for a source/destination type pair before the first map of that pair:

```csharp
ObjectMapper.Convert<Money, string>(money => money.ToString());
```

Converters are process-wide static registrations. A converter is consulted for a property when the source and destination types differ and the mapper cannot use a built-in conversion. Built-in conversions include assignable types, nullable wrapping/unwrapping (a null nullable value maps to the destination default), numeric/enum conversions supported by expression conversion, `int` to `bool`, and invariant-culture `long` to `string`. Unsupported conversions fail when the mapping is compiled; add a converter or explicit source expression.

## Operational Notes

Mapping delegates and converters are cached statically by type pair. Avoid request-specific configuration and do not capture scoped services in converters. The mapper creates a new destination instance and maps public writable properties; it is not a deep-copy framework and does not recursively map nested objects unless an explicit expression or converter does so.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Mapping`: `MappingBuilder<TSource, TDestination>` `ObjectMapper`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~MappingTests
bash scripts/verify-package-consumer.sh
```