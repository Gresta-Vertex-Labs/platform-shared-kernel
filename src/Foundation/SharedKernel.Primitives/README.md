# SharedKernel.Primitives

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Foundation](https://img.shields.io/badge/tier-Foundation-2ea44f)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Trim and AOT: clean](https://img.shields.io/badge/trim%20%26%20AOT-clean-success)

> **The base every SharedKernel package depends on: failures as values (`Result<T>`, `Error`), a testable clock, a
> richer enum, the readiness-probe contract, and the registries that stop two packages disagreeing about a wire
> identifier.**

| You get | So that |
| --- | --- |
| `Result` / `Result<T>` / `ValidationResult`, `Error` + `ErrorType` + `ErrorCodes` | Expected failures travel as values with a stable code, and map mechanically to HTTP/gRPC status at the edge |
| `IClock` / `SystemClock` + `AddClock()` | Time is injectable and testable; `DateTime.UtcNow` is never read inline (`SK0001`) |
| `SmartEnum<TEnum, TValue>` + `SmartEnumJsonConverter<TEnum, TValue>` | Enumerations carry behaviour, lookups fail loudly, and the underlying value is the JSON contract |
| `IIdGenerator` / `UuidV7IdGenerator` | Time-ordered ids that keep B-tree inserts local |
| `IReadinessProbe`, `ReadinessReport`, `AddReadinessProbe` | Every provider reports readiness through one contract, with no health-checks dependency |
| `WellKnownHeaders`, `WellKnownBaggageKeys`, `WellKnownTagKeys`, `LoggingEventIdRanges` | Header, baggage, tag names and `EventId` blocks exist exactly once platform-wide |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Primitives" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Foundation — reference it from **any** project |
| Depends on | `Microsoft.Extensions.DependencyInjection.Abstractions` only (for `AddClock()` and `AddReadinessProbe()`) |
| Namespaces | `SharedKernel.Primitives.Results`, `.Errors`, `.Clocks`, `.Enums`, `.Identifiers`, `.Health`, `.Propagation`, `.Logging` |

The operations on these types — railway chaining (`Map`, `Bind`, `Ensure`, `Tap`), exception boundaries, guard clauses
and the exception hierarchy — live in
[`SharedKernel.Core`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Foundation/SharedKernel.Core/README.md).
Most services reference both.

## Quick start

```csharp
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Identifiers;

builder.Services.AddClock();                                        // IClock -> SystemClock
builder.Services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();   // no extension ships, by design
```

```csharp
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

public sealed class OrderLookup(IOrderStore store, IClock clock)
{
    public Result<Order> Find(Guid id)
    {
        var order = store.Get(id);
        if (order is null)
            return Error.NotFound("order.not_found", "The order does not exist.");   // implicit conversion
        if (order.ExpiresAt <= clock.UtcNow)
            return Error.BusinessRule("order.expired", "The order has expired.");
        return order;                                                              // ...both ways
    }
}

var result = lookup.Find(id);
if (result.IsFailure)
    return result.Error;
Use(result.Value);
```

No configuration section: the package has no options.

## How it works

### Results and errors

| Use | When |
| --- | --- |
| `Result<T>` (sealed class) | The operation returns a value on success |
| `Result` (readonly struct) | The operation returns nothing on success — a command, a side effect |
| `ValidationResult` / `ValidationResult<T>` | Several things can fail **at once** and the caller needs all of them |

- **Accessing the wrong side throws.** `Value` on a failure and `Error` on a success throw `InvalidOperationException` —
  a silent default would hide the bug. Branch on `IsSuccess`/`IsFailure`, or use `SharedKernel.Core`'s railway extensions.
- **`Error` is a `sealed record` of `(Code, Message, Type)`** with value equality, never `null` (`Error.None` means "no
  error"), and one factory per `ErrorType`.
- **`Code` is the stable identity** that consumers branch on, dashboards filter on, and `SharedKernel.Localization`
  translates by: dot-separated lowercase, general to specific, never interpolated data. Check `ErrorCodes` first.
- **`Message` may reach an end user** as the fallback when no translation exists — no secrets, raw exception text or
  "see logs".
- **`Details`** holds field errors (`Error.Validation(errors)` → code `validation.failed`). It survives
  `System.Text.Json`, so `14.Presentation` maps it to the ProblemDetails `errors` map and `11.Communication.Rest`
  rebuilds it on the calling side.
- **`MessageArguments`** holds translation values, filled only by `SharedKernel.Localization`'s `LocalizedMessage.ToError`.
  It is not part of equality and not serialized — `Message` already contains the values in the default text.
- `ValidationResult` snapshots the errors you pass and compares by value; `Failure` rejects an empty sequence and a
  `null` element.

### Which `ErrorType`

| Type | Means | HTTP |
| --- | --- | --- |
| `Validation` | Input malformed or missing, caught before the domain ran | 400 |
| `Unauthorized` | Caller may not attempt this **at all** — no/invalid credentials | 401 |
| `Forbidden` | Caller is authenticated but not permitted **this instance** | 403 |
| `NotFound` | Resource does not exist | 404 |
| `Conflict` | Clashes with existing state — duplicate, or concurrency | 409 |
| `BusinessRule` | Well-formed, but violates a domain invariant | 422 |
| `Unexpected` | Unclassified fault | 500 |
| `Unavailable` | A dependency or the service cannot serve right now; retry later | 503 |
| `Timeout` | The operation ran out of time; its outcome may be unknown | 504 |

The pairs people get wrong: **`Unauthorized` vs `Forbidden`** ("who are you?" vs "may you do *this*?"); **`Validation`
vs `BusinessRule`** (could the caller fix a field?); **`Unexpected` vs `Unavailable`/`Timeout`** — a dependency that is
down or slow is an operational condition, not a defect; reporting it as `Unexpected` turns every outage into a 500 that
reads like a bug.

### Readiness probes

`SharedKernel.Primitives.Health` is the one readiness contract every provider package implements. Providers register
their own probes when they are registered, so a probe exists exactly when its dependency does, and no provider references
a health-checks library. The host maps them: `SharedKernel.ServiceDefaults`' `services.AddHealthChecks().AddSharedKernelReadiness()`
turns every probe into a `ready`-tagged check. A host without ASP.NET Core resolves `IEnumerable<IReadinessProbe>` itself.

- **Construction must be cheap.** The host constructs every probe to read its `Name`; resolve clients inside `ProbeAsync`.
- **Failures are reports, not exceptions.** Only cancellation throws. A report may be shown on a health endpoint, so it
  never carries connection strings, credentials, tenant data or exception messages.

### Platform registries

| Registry | Holds | Call-site shape |
| --- | --- | --- |
| `WellKnownHeaders` | `X-Correlation-Id`, `X-Tenant-Id`, `Idempotency-Key`, `x-sk-actor-id`, `x-sk-actor-kind`, `x-sk-client-id` | HTTP headers, gRPC metadata, message and workflow headers |
| `WellKnownBaggageKeys` | `correlation.id`, `TenantId` | `Activity.SetBaggage` / `AddBaggage` |
| `WellKnownTagKeys` | `tenant.id`, `correlation.id`, `error.type`, `error.code` | `Activity.SetTag` |
| `LoggingEventIdRanges` | One 1000-wide `EventId` block per domain (`Core` = 1000 … `Reporting` = 20000), `PackageSubBlockWidth` = 100 | `[LoggerMessage(EventId = …)]` |

`SharedKernel.Execution`'s `RequestContextPropagation` writes and reads the correlation, tenant and caller headers on every
hop. Tags are span-local; baggage crosses process boundaries on every outbound call, so its registry stays small. The
tenant **baggage** key is `"TenantId"` while the tenant **tag** key is `"tenant.id"` on purpose: `13.ServiceDefaults`
copies baggage onto log records under its own key, so the baggage string is the emitted log property name.

## Recipes

### 1. Fold many validation errors into one `Error`

```csharp
using SharedKernel.Guards;   // SharedKernel.Core

ValidationResult validation = Guard.Collect(
    Guard.Against.NullOrWhiteSpace(cmd.Name),
    Guard.Against.NegativeOrZero(cmd.Quantity));

if (!validation.IsValid)
    return Result<OrderDraft>.Failure(Error.Validation(validation.Errors));   // code "validation.failed", Details = every error
```

### 2. Declare a SmartEnum and serialize it

```csharp
using SharedKernel.Primitives.Enums;

public sealed class OrderStatus : SmartEnum<OrderStatus, int>
{
    public static readonly OrderStatus Pending  = new(nameof(Pending),  1);
    public static readonly OrderStatus Shipped  = new(nameof(Shipped),  2);
    public static readonly OrderStatus Complete = new(nameof(Complete), 3);

    private OrderStatus(string name, int value) : base(name, value) { }

    public bool IsTerminal => this == Complete;
}

OrderStatus.List;                             // every member, declaration order
OrderStatus.FromValue(2);                     // throws if absent
OrderStatus.TryFromName(input, out var s);    // false if absent; never throws, even for null

public sealed record OrderDto(
    [property: JsonConverter(typeof(SmartEnumJsonConverter<OrderStatus, int>))] OrderStatus Status);
```

`static readonly` fields, a `private` constructor and a `sealed` class make each member a singleton, so `==` is correct.
Equality is by reference, ordering by value. The JSON wire form is the **underlying value**, so renaming a member keeps
stored payloads readable; an unknown value fails as a `JsonException`. Nothing is registered globally.

### 3. Write a readiness probe for your own dependency

```csharp
using SharedKernel.Primitives.Health;

public sealed class OrderStoreProbe(IServiceProvider services) : IReadinessProbe
{
    public string Name => "order-store";

    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var store = services.GetRequiredService<IOrderStore>();   // resolve inside ProbeAsync
        return await store.PingAsync(cancellationToken)
            ? ReadinessReport.Healthy()
            : ReadinessReport.Unhealthy("The order store did not answer.");
    }
}

builder.Services.AddReadinessProbe<OrderStoreProbe>();
```

### 4. Take an `EventId` from the domain's block

```csharp
[LoggerMessage(
    EventId = LoggingEventIdRanges.Caching + 100,
    Level = LogLevel.Warning,
    Message = "Cache backplane reconnected after {AttemptCount} attempts")]
public static partial void BackplaneReconnected(ILogger logger, int attemptCount);
```

### 5. Work with a `TResponse` you cannot name (pipeline code)

```csharp
static TValue? ReadValue<TResponse, TValue>(TResponse response)
    where TResponse : IResultOfT<TValue>
    => response.IsSuccess ? response.Value : default;

static TResponse BuildFailure<TResponse>(Error error)
    where TResponse : IFailureFactory<TResponse>
    => TResponse.Failure(error);
```

`IHasSuccessFlag` (both result types), `IResultOfT<T>` and `IFailureFactory<TSelf>` (`Result<T>` only) exist for pipeline
behaviors; application code should not need them. `where TResponse : IResultOfT<TResponse>` does not compile (`CS0311`).

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddClock()` | `IClock` → `SystemClock` (singleton, `TryAdd`; a registered `TimeProvider` is picked up) |
| `AddReadinessProbe<TProbe>()` | A singleton probe; registering the same type twice is a no-op |
| `AddReadinessProbe(Func<IServiceProvider, IReadinessProbe>)` | One probe per call, for a provider with several targets |
| `IServiceProvider.GetRequiredReadinessProbe(name)` | Resolves one probe by name; throws when none or several match |

### Main types

| Type | Members |
| --- | --- |
| `Result` / `Result<T>` | `Success()`, `Success(value)`, `Failure(error)`, `IsSuccess`, `IsFailure`, `Value`, `Error`; implicit from `Error` (and `T`) |
| `ValidationResult` / `ValidationResult<T>` | `Success()`, `Success(value)`, `Failure(errors)`, `IsValid`, `Errors`, `Value` (generic) |
| `Error` | `Code`, `Message`, `Type`, `Details`, `MessageArguments`; `Error.None`; `Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `BusinessRule`, `Unexpected`, `Unavailable`, `Timeout`, `Validation(errors)` |
| `IClock` | `UtcNow` (`DateTimeOffset`), `Today` (`DateOnly`) — both UTC |
| `ReadinessReport` | `Status` (`Healthy`/`Degraded`/`Unhealthy`), `Latency`, `Description`, `Data`; `Healthy(…)`, `Degraded(…)`, `Unhealthy(…)` |

### Error codes (`ErrorCodes`)

| Class | Constants |
| --- | --- |
| `Validation` | `Failed` (`validation.failed`), `Required`, `InvalidFormat`, `MaxLength`, `MinLength`, `OutOfRange` |
| `NotFound` / `Unexpected` | `not_found.default` / `unexpected.exception` |
| `Conflict` | `Default` (`conflict.default`), `Duplicate` (`conflict.duplicate`) |
| `Unauthorized` | `Default` (`unauthorized.default`), `Expired` (`unauthorized.expired`) |
| `Forbidden` | `Default` (`forbidden.default`), `InsufficientPermission` (`forbidden.insufficient_permission`) |
| `Unavailable` / `Timeout` | `unavailable.default` / `timeout.default` |
| `Idempotency` | `KeyRequired`, `KeyInvalid`, `KeyReused`, `InProgress` (`idempotency.*`) |
| `Domain` | `RuleViolated` (`domain.rule.violated`) |

### Logging

The package does not log; it owns the `LoggingEventIdRanges` registry every other package logs from.

## Testing

Reference [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
from your test project. It ships `FakeClock` (`SharedKernel.Testing.Clocks`: `Set`, `Advance`, settable `UtcNow`):

```csharp
var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
services.AddSingleton<IClock>(clock);
clock.Advance(TimeSpan.FromHours(2));
```

`Result`, `Error` and `SmartEnum` need no fakes — assert on `IsFailure` and `Error.Code`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Read `DateTime.UtcNow` / `DateTimeOffset.UtcNow` | Inject `IClock` | Inline reads make logic untestable (`SK0001`) |
| Inject `TimeProvider` in application code | Inject `IClock` | Its two members stay the only thing tests substitute |
| Return `default(Result)` or leave a `Result` field unassigned | Use `Result.Success()` / `Result.Failure(error)` | `Result` is a struct; `default` reports `IsFailure` with no error, and `Error` throws naming this cause |
| Use `null` for "no error" | `Error.None` | The factories reject `null` |
| Interpolate ids into `Error.Code` | Put identifiers in the message | A code with an id cannot be aggregated, alerted on or translated |
| Retype a header, baggage or tag literal | Reference the registry constant | Drift between packages is not a compile error (`SK0022`) |
| Hand-pick an `EventId` literal | Derive it from `LoggingEventIdRanges` | Keeps every id inside its domain's block |
| Test `IHasSuccessFlag` on a hot path with `Result` | `response is Result r` | The interface boxes the struct (32 bytes per check) |
| Give two SmartEnum members the same value or name | Keep both distinct | Lookup tables build on first use, so the duplicate throws late |
| Renumber `ErrorType` | Treat its values as a wire contract | It is serialized and persisted |
| Use a v7 id as a token or nonce, or treat id order as event order | `ISecureRandomGenerator` / a timestamp from `IClock` | v7 ids are predictable; ids in the same millisecond have no defined order |

## Design decisions

**Why no metadata bag on `Error`?** It would break value equality and raise serialization questions. Field errors go in
`Details`, placeholder values in `MessageArguments`; everything else is shaped at the ProblemDetails boundary.

**Why `ErrorCodes` as nested string constants, not an enum?** Services add their own constants without forking the kernel.

**Why no DI extension for `IIdGenerator`?** One implementation, one line to register, and the generator stays opt-in —
nothing silently replaces `Guid.NewGuid()`.

**Why do the registries live here and not in `SharedKernel.Contracts`?** The gRPC packages may not reference Contracts,
and Primitives is the one package every consumer already references.

**Why no local-time member on `IClock`?** Local time is a presentation concern.

**Is it AOT-safe?** Yes — no reflection, `dynamic`, expression trees or runtime code generation; `SmartEnum` lookups use
lists built at type initialization and are verified under a `TrimMode=full` publish.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Core domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Foundation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
