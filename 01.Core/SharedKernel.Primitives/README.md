# SharedKernel.Primitives

The foundation layer of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). Every other package in the platform depends on this one, directly or transitively, so it deliberately stays small and stable.

**One NuGet dependency:** `Microsoft.Extensions.DependencyInjection.Abstractions`, used only by the optional `AddClock()` extension. Nothing else.

**Trim- and AOT-clean.** The package compiles with zero `IL2026`/`IL3050`/`IL2059` under both `EnableTrimAnalyzer` and `EnableAotAnalyzer`, and its `SmartEnum` lookups are verified working against a self-contained `TrimMode=full` publish. No reflection, no `dynamic`, no expression trees, no runtime code generation anywhere in the package.

---

## What's in here

| Area | Types |
| --- | --- |
| **Results** | `Result<T>`, `Result`, `ValidationResult`, `ValidationResult<T>`, `IHasSuccessFlag`, `IResultOfT<T>`, `IFailureFactory<TSelf>` |
| **Errors** | `Error`, `ErrorType`, `ErrorCodes` |
| **Time** | `IClock`, `SystemClock`, `ClockExtensions.AddClock()` |
| **Enumerations** | `SmartEnum<TEnum, TValue>`, `SmartEnumJsonConverter<TEnum, TValue>` |
| **Identifiers** | `IIdGenerator`, `UuidV7IdGenerator` |
| **Logging** | `LoggingEventIdRanges` |
| **Propagation** | `WellKnownHeaders`, `WellKnownBaggageKeys`, `WellKnownTagKeys` |

---

## Results and errors

`Result<T>` and `Result` model an outcome that either succeeded or failed, without exceptions for expected failures.

```csharp
Result<Order> Find(Guid id)
{
    var order = _repository.Get(id);
    return order is null
        ? Error.NotFound("order.not_found", $"Order {id} does not exist.")
        : order;                       // implicit conversions both ways
}

var result = Find(id);
if (result.IsFailure)
{
    _logger.LookupFailed(result.Error.Code);
    return result.Error;
}
Use(result.Value);
```

`Result` (non-generic) is the void counterpart, for operations with nothing to return on success.

**Accessing the wrong side throws.** `Value` on a failure and `Error` on a success both throw `InvalidOperationException` — check `IsSuccess`/`IsFailure` first. This is intentional: a silent default would hide the bug.

**Never construct a `Result` any way but through its factories.** `Result` is a struct, so the runtime can hand you an all-zero instance that ran neither factory — `default(Result)`, an unassigned field, an element of `new Result[n]`, or the `out` value of a failed `TryGetValue`. Such an instance reports `IsFailure` but carries no error, and reading `Error` throws an `InvalidOperationException` that names this cause. `Result<T>` is a class, so `default` is simply `null` and fails immediately.

`Error` is a `sealed record` of `(Code, Message, Type)` with value equality and one factory per `ErrorType`:

```csharp
Error.Validation(ErrorCodes.Validation.Required, "Name is required.");
Error.NotFound(...);      Error.Conflict(...);     Error.Unauthorized(...);
Error.Forbidden(...);     Error.BusinessRule(...); Error.Unexpected(...);
```

Use `Error.None` for "no error" — **never `null`**, which the failure factories reject outright.

`ErrorType` is what `14.Presentation` maps to an HTTP status code, so pick it by meaning, not by convenience. The two most often confused:

| Type | Meaning | HTTP |
| --- | --- | --- |
| `Unauthorized` | The caller may not attempt this at all — missing or invalid credentials. | 401 |
| `Forbidden` | The caller may generally attempt this, but not this instance under these conditions. | 403 |
| `Validation` | Input was malformed or missing, caught before the domain ran. | 400 |
| `BusinessRule` | Input was well-formed but violates a domain invariant. | 422 |

`ErrorCodes` holds the well-known code constants (`Validation.Required`, `NotFound.Default`, `Conflict.Duplicate`, `Unauthorized.Expired`, `Forbidden.InsufficientPermission`, `Unexpected.Default`, `Domain.RuleViolated`). They are `const string`, not an enum, so your own package can add its own codes without forking anything.

### `ValidationResult` — many errors, not one

Use `ValidationResult` when a single operation can fail several ways at once and the caller needs all of them. Use `Result<T>` for a single-error outcome. They model different failure cardinalities; do not substitute one for the other.

```csharp
var errors = new List<Error>();
if (string.IsNullOrWhiteSpace(command.Name))
    errors.Add(Error.Validation("name.required", "Name is required."));
if (command.Quantity <= 0)
    errors.Add(Error.Validation("quantity.positive", "Quantity must be positive."));

return errors.Count == 0
    ? ValidationResult<OrderDraft>.Success(draft)
    : ValidationResult<OrderDraft>.Failure(errors);
```

Both types **snapshot** the errors you pass, so continuing to mutate your own list afterwards cannot change the result, and both compare **by value**, so two results built from equal errors are equal. `Failure` rejects an empty sequence and a `null` element.

### The three result interfaces

These exist so a MediatR pipeline behavior can inspect an unknown `TResponse` with no reflection at all. Application code rarely touches them.

- **`IHasSuccessFlag`** — implemented by both result types. `if (response is IHasSuccessFlag f && f.IsSuccess)`.
- **`IResultOfT<out T>`** — `Result<T>` only. Constrain `where TResponse : IResultOfT<TResponse>` to read `Value` directly.
- **`IFailureFactory<TSelf>`** — `Result<T>` only. A static abstract member, so `TResponse.Failure(error)` constructs a failure of a shape the caller never names.

---

## Time

Never call `DateTime.UtcNow` or `DateTimeOffset.UtcNow` in platform code — the `SK0001` analyzer flags it. Inject `IClock`:

```csharp
services.AddClock();                       // TryAddSingleton, so your own registration wins

public sealed class Handler(IClock clock)
{
    public void Handle() => _stamp = clock.UtcNow;   // also clock.Today (DateOnly)
}
```

`SystemClock` reads from a `TimeProvider` internally. If your host registers its own `TimeProvider` in DI, `AddClock()` picks it up automatically — the container selects the greediest constructor it can satisfy — which is how coordinated simulation or deterministic replay works without a custom clock. Outside `SystemClock` itself, inject `IClock`, never `TimeProvider` directly.

In tests, register a fake `IClock` returning a fixed time. `16.Testing/SharedKernel.Testing` ships one.

---

## SmartEnum

A type-safe enumeration with behaviour, ordering, and lookups — reflection-free.

```csharp
public sealed class OrderStatus : SmartEnum<OrderStatus, int>
{
    public static readonly OrderStatus Pending  = new(nameof(Pending),  1);
    public static readonly OrderStatus Shipped  = new(nameof(Shipped),  2);
    public static readonly OrderStatus Complete = new(nameof(Complete), 3);

    private OrderStatus(string name, int value) : base(name, value) { }

    public bool IsTerminal => this == Complete;
}
```

```csharp
OrderStatus.List;                                  // all members, declaration order
OrderStatus.FromValue(2);                          // throws if absent
OrderStatus.TryFromValue(99, out var status);      // false; never throws, even for null
OrderStatus.FromName("Shipped");                   // ordinal, case-sensitive
OrderStatus.TryFromName(input, out var parsed);
OrderStatus.List.Order();                          // IComparable, ordered by Value
```

**Member values and names must be distinct.** A duplicate makes lookup ambiguous, so the first lookup throws an `InvalidOperationException` naming the type, the duplicated key, and both colliding members. Because the lookup tables build on first use, that error appears at the first lookup rather than at the declaration.

Equality is reference equality. Every member is a singleton in a `static readonly` field, so `FromValue(2) == OrderStatus.Shipped` holds, and with distinct values reference equality and ordering agree.

### Serializing a SmartEnum

Without a converter a `SmartEnum` is **write-only** over JSON: the default object serializer emits `{"Name":"Shipped","Value":2}` and cannot read it back, because members are singletons behind a private constructor. Opt in per property or per options instance:

```csharp
public sealed record OrderDto(
    [property: JsonConverter(typeof(SmartEnumJsonConverter<OrderStatus, int>))]
    OrderStatus Status);

// or:
options.Converters.Add(new SmartEnumJsonConverter<OrderStatus, int>());
```

The wire form is the **underlying value**, not the name — so renaming a member leaves persisted and in-flight payloads readable, the same trade-off a numerically-serialized `enum` makes. An unrecognized value fails as a `JsonException` naming the type and value. Nothing is registered globally and no `JsonSerializerContext` is shipped; the converter resolves `TValue` through the options' own `JsonTypeInfo`, so it composes with a source-generated context.

---

## Identifiers

```csharp
services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();   // no DI extension shipped, by design
```

`UuidV7IdGenerator` produces RFC 9562 UUID v7 values, which embed a millisecond timestamp in their high bits. A fully-random UUID v4 (what `Guid.NewGuid()` gives) is a well-known clustered-index anti-pattern: each insert lands at a random point in the B-tree, causing page splits that worsen as the table grows. v7 restores insert locality while still needing no central coordinator.

Opt-in and additive — nothing calls it automatically, and existing `Guid.NewGuid()` call sites need not change. Values generated in the *same* millisecond have no defined order relative to each other, but still land adjacent in the index.

---

## Platform registries

Three compile-time constant registries exist so that two packages cannot independently hardcode the same wire identifier and drift apart — a mismatch that has happened on this platform and is what these replace.

**`LoggingEventIdRanges`** reserves a 1000-wide `EventId` block per capability domain (`{domain number} * 1000`). Every `[LoggerMessage]` on the platform derives its `EventId` from this, never an ad hoc literal. A multi-package domain subdivides its block into 100-wide sub-blocks per package.

**`WellKnownHeaders`** — `X-Correlation-Id`, `X-Tenant-Id`. The propagation header names shared by `11.Communication`, `13.ServiceDefaults`, and `14.Presentation`.

**`WellKnownBaggageKeys`** — `Activity` baggage keys for correlation id and tenant id.

**`WellKnownTagKeys`** — OpenTelemetry `Activity.SetTag` attribute keys (`tenant.id`, `correlation.id`, `error.type`, `error.code`).

> The tenant **baggage** key is `"TenantId"` while the tenant **tag** key is `"tenant.id"`. That asymmetry is deliberate and documented on the constant: the baggage processor copies baggage generically, so the baggage key becomes the emitted log property name, and renaming it would break deployed dashboards and alert rules.

These live here rather than in `04.Contracts` because `SharedKernel.Communication.Grpc` is barred from referencing `04.Contracts`, and `01.Core` is the one layer every consumer already references.

---

## What is deliberately *not* here

- **`Map`/`Bind`/`Tap` and other railway combinators** — `SharedKernel.Core`, alongside `ResultTry` (exception boundaries) and `ResultCombine` (aggregating several results).
- **Guard clauses** — `SharedKernel.Core`, under the `SharedKernel.Guards` namespace.
- **A metadata bag on `Error`** — evaluated and declined. `Error` is the platform's most-depended-upon type; a generic bag breaks its `record` equality contract and raises AOT and cross-process-serialization questions. The needs that motivated it (per-field validation errors, a retry-after hint) are solved at the `ProblemDetails` boundary in `14.Presentation` instead.
- **A DI extension for `IIdGenerator`** — one implementation, one line to register; a package-owned extension would add surface without removing work.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the [01.Core README](../README.md) for the full capability overview.
