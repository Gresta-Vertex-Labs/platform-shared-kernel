# SharedKernel.Primitives

The foundation layer of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). Every other package in the platform depends on this one, directly or transitively, so it stays small, stable, and opinionated.

It gives you four things: a way to return failures without exceptions (`Result<T>`, `Error`), a testable clock (`IClock`), a richer enum (`SmartEnum<TEnum, TValue>`), and the registries that stop two packages from disagreeing about a wire identifier.

```shell
dotnet add package SharedKernel.Primitives
```

**One NuGet dependency:** `Microsoft.Extensions.DependencyInjection.Abstractions`, used only by `AddClock()`.

This package defines the types. The operations on them live one layer up, in [`SharedKernel.Core`](../SharedKernel.Core/README.md): railway chaining (`Map`, `Bind`, `Ensure`, `Tap`), exception boundaries, guard clauses, and the exception hierarchy. Most services reference both.

**Trim- and AOT-clean.** Compiles with zero `IL2026`/`IL3050`/`IL2059` under both `EnableTrimAnalyzer` and `EnableAotAnalyzer`, and `SmartEnum` lookups are verified working against a self-contained `TrimMode=full` publish. No reflection, no `dynamic`, no expression trees, no runtime code generation anywhere.

---

## The rules

These are the non-negotiables. Most are mechanically enforced; all of them exist because breaking them has caused a real defect on this platform.

| Rule | Why, and what enforces it |
| --- | --- |
| Never read `DateTime.UtcNow` / `DateTimeOffset.UtcNow`. Inject `IClock`. | Inline clock reads make the surrounding logic untestable. Enforced by analyzer `SK0001`. |
| Never use `null` for "no error". Use `Error.None`. | The failure factories reject a null error, and `Result.Error` throws rather than return one. |
| Never build a `Result` any way but through its factories. | `Result` is a struct, so `default(Result)` is reachable and is **not** a valid result — see [Traps](#traps-worth-knowing). |
| Never retype a header, baggage, or tag literal. Reference the registry constant. | These are contracts between packages that cannot reference each other, so drift is not a compile error. Enforced by analyzer `SK0022`. |
| Never interpolate variable data into an `Error.Code`. | A code with an id in it cannot be aggregated, alerted on, or translated. Put identifiers in the message. |
| Never derive an `EventId` from a bare literal. Use `LoggingEventIdRanges`. | Two packages independently collided on EventId 4001/4002 before this registry existed. |
| Pick `ErrorType` by meaning, not convenience. | It maps mechanically to an HTTP status at `14.Presentation`. See [the table](#which-errortype). |
| Give every `SmartEnum` member a distinct value **and** name. | A duplicate makes lookup ambiguous and throws on first use, not at declaration. |
| Don't inject `TimeProvider`. | `SystemClock` is the only type allowed to depend on it, so `IClock`'s two members stay the only thing tests substitute. |

---

## Results and errors

`Result<T>` and `Result` model an outcome that either succeeded or failed, without using exceptions for expected failures.

```csharp
Result<Order> Find(Guid id)
{
    var order = _repository.Get(id);
    return order is null
        ? Error.NotFound("order.not_found", $"Order {id} does not exist.")   // implicit conversion
        : order;                                                            // ...both ways
}

var result = Find(id);
if (result.IsFailure)
{
    return result.Error;
}
Use(result.Value);
```

| Use | When |
| --- | --- |
| `Result<T>` | The operation returns a value on success. |
| `Result` | The operation returns nothing on success — a command, a side effect. |
| `ValidationResult` / `<T>` | Several things can fail **at once** and the caller needs all of them. |

**Accessing the wrong side throws.** `Value` on a failure and `Error` on a success both throw `InvalidOperationException`. That is deliberate — a silent default would hide the bug. Check `IsSuccess`/`IsFailure` first, or let `SharedKernel.Core` do the branching:

```csharp
using SharedKernel.Core.Extensions;

Result<OrderDto> dto = Find(id)
    .Ensure(order => order.IsOpen, OrderErrors.Closed)
    .Map(order => order.ToDto());

Order order = Find(id).GetValueOrThrow();   // throws NotFoundException for the NotFound error
```

`Error` is a `sealed record` of `(Code, Message, Type)` with value equality, and one factory per `ErrorType`:

```csharp
Error.Validation(ErrorCodes.Validation.Required, "Name is required.");
Error.NotFound(...);   Error.Conflict(...);      Error.Unauthorized(...);
Error.Forbidden(...);  Error.BusinessRule(...);  Error.Unexpected(...);
```

**`Code` is the field that matters most.** It is the stable identity of the failure, and three separate things key off it: consumers branch on it, dashboards and alert rules filter on it, and `SharedKernel.Localization` looks up a translated message by it. So: dot-separated lowercase, general to specific; stable once shipped; never any interpolated data. Check `ErrorCodes` first — a suitable constant often already exists.

**`Message` may reach an end user.** It is the fallback shown when no translation is registered for the code, so keep secrets and raw exception text out of it, and don't write "see logs".

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

The two pairs people get wrong:

- **`Unauthorized` vs `Forbidden`** — "who are you?" versus "may you do *this*?". Substituting one makes an authorization failure indistinguishable from a missing credential in logs, and tells the client to re-authenticate when that cannot help.
- **`Validation` vs `BusinessRule`** — if the caller could fix it by correcting a field, it's validation. If the request is well-formed and the domain is refusing, it's a business rule.

### `ValidationResult` — many errors, not one

```csharp
var errors = new List<Error>();
if (string.IsNullOrWhiteSpace(cmd.Name)) errors.Add(Error.Validation("name.required", "Name is required."));
if (cmd.Quantity <= 0)                   errors.Add(Error.Validation("quantity.positive", "Quantity must be positive."));

return errors.Count == 0
    ? ValidationResult<OrderDraft>.Success(draft)
    : ValidationResult<OrderDraft>.Failure(errors);
```

You rarely need to build the list by hand. `SharedKernel.Core` produces a `ValidationResult` from guards or from several results:

```csharp
using SharedKernel.Core.Extensions;   // ResultCombine
using SharedKernel.Guards;            // Guard

ValidationResult validation = Guard.Collect(
    Guard.Against.NullOrWhiteSpace(cmd.Name),
    Guard.Against.NegativeOrZero(cmd.Quantity));

// or, from independent Result<T> checks:
ValidationResult<IReadOnlyList<LineItem>> lines = ResultCombine.Combine(cmd.Lines.Select(ParseLine));
```

Both types **snapshot** the errors you pass, so continuing to mutate your own list afterwards cannot change the result, and both compare **by value**, so two results built from equal errors are equal. `Failure` rejects an empty sequence and a `null` element.

### The three result interfaces

These exist so a MediatR pipeline behavior can work with a `TResponse` it cannot name, with no reflection. **Application code should not need them.**

- **`IHasSuccessFlag`** — read the outcome. Both result types implement it.
- **`IResultOfT<T>`** — read the value. `Result<T>` only.
- **`IFailureFactory<TSelf>`** — *construct* a failure of an unnamed shape. `Result<T>` only.

The constraint shapes differ, and this is the part that trips people up:

```csharp
// IResultOfT needs TWO type parameters: the response shape AND the value it carries.
static TValue? ReadValue<TResponse, TValue>(TResponse response)
    where TResponse : IResultOfT<TValue>
    => response.IsSuccess ? response.Value : default;

// IFailureFactory IS self-referential, so one parameter is correct here.
static TResponse BuildFailure<TResponse>(Error error)
    where TResponse : IFailureFactory<TResponse>
    => TResponse.Failure(error);
```

Writing `where TResponse : IResultOfT<TResponse>` does **not** compile (`CS0311`): `Result<int>` implements `IResultOfT<int>`, not `IResultOfT<Result<int>>`.

---

## Time

```csharp
services.AddClock();

public sealed class ExpireSessionHandler(IClock clock)
{
    public bool HasExpired(Session s) => s.ExpiresAt <= clock.UtcNow;   // also clock.Today
}
```

Three behaviours of `AddClock()`, all verified by running them against a real container:

- **Calling it twice is harmless** — it uses `TryAddSingleton`.
- **Your own `IClock` wins if you register it *first*.** `TryAdd` is first-registration-wins, not last.
- **A registered `TimeProvider` is picked up automatically.** The container selects the greediest constructor it can satisfy, so if you register a custom `TimeProvider` — for coordinated simulation or deterministic replay — the clock reads from it with no further wiring. Order doesn't matter for this one.

Both members are UTC. There is no local-time member and none should be added: local time is a presentation concern. In tests, register a fake `IClock`; `16.Testing/SharedKernel.Testing` ships one.

---

## SmartEnum

A type-safe enumeration that can carry behaviour and data per member, and whose lookups fail loudly instead of silently accepting `(Status)999`. A plain `enum` is still right for a simple flag set.

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

`static readonly` fields, a `private` constructor, a `sealed` class — that shape is what makes each member a singleton and reference equality correct.

```csharp
OrderStatus.List;                              // every member, declaration order
OrderStatus.FromValue(2);                      // throws if absent
OrderStatus.FromName("Shipped");               // ordinal, case-sensitive
OrderStatus.TryFromValue(input, out var s);    // false if absent; never throws, even for null
OrderStatus.TryFromName(input, out var s);
OrderStatus.List.Order();                      // IComparable, ordered by Value
```

Use the `Try` pair for anything parsed from outside the process — a request field, a database column — and the throwing pair only where a miss is a bug.

**Equality is reference equality; ordering is by value.** `FromValue(2) == OrderStatus.Shipped` holds, so `==` is the right comparison.

### Serializing a SmartEnum

Without a converter a SmartEnum is **write-only** over JSON: the default serializer emits `{"Name":"Shipped","Value":2}` and cannot read it back, because the constructor is private. Opt in per property or per options instance:

```csharp
public sealed record OrderDto(
    [property: JsonConverter(typeof(SmartEnumJsonConverter<OrderStatus, int>))]
    OrderStatus Status);

// or:
options.Converters.Add(new SmartEnumJsonConverter<OrderStatus, int>());
```

The wire form is the **underlying value**, not the name — so renaming a member leaves persisted and in-flight payloads readable, the same trade-off a numerically-serialized `enum` makes. An unrecognized value fails as a `JsonException` naming the type and value. Nothing is registered globally.

---

## Identifiers

```csharp
services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();   // no DI extension ships, by design
```

`UuidV7IdGenerator` produces RFC 9562 UUID v7 values, which embed a millisecond timestamp in their high bits. `Guid.NewGuid()`'s fully random v4 is a well-known clustered-index anti-pattern: each insert lands at a random point in the B-tree, causing page splits that worsen as the table grows. v7 restores insert locality while still needing no central coordinator.

Opt-in and additive — nothing calls it automatically, and existing `Guid.NewGuid()` call sites need not change.

Two limits worth knowing: values generated in the **same millisecond** have no defined order relative to each other (only the random bits differ), so never treat id order as event order — store a timestamp from `IClock` if you need that. And a v7 id is **predictable by design**; never use one as a token, secret, or nonce. That is `SharedKernel.Cryptography`'s `ISecureRandomGenerator`.

---

## Platform registries

Three compile-time constant registries, so two packages cannot independently hardcode the same wire identifier and drift apart. Each has already prevented, or was created because of, a real mismatch.

| Registry | Holds | Call-site shape |
| --- | --- | --- |
| `WellKnownHeaders` | `X-Correlation-Id`, `X-Tenant-Id` | HTTP / gRPC metadata |
| `WellKnownBaggageKeys` | correlation id, tenant id | `Activity.SetBaggage` / `AddBaggage` |
| `WellKnownTagKeys` | `tenant.id`, `correlation.id`, `error.type`, `error.code` | `Activity.SetTag` |

**Pick the registry matching your call-site shape.** Tags are span-local attributes. Baggage propagates across process boundaries and rides on every outbound call, so keep that registry small. They are not interchangeable, and two of them holding the same literal for the same concept does not make them so.

> The tenant **baggage** key is `"TenantId"` while the tenant **tag** key is `"tenant.id"`. That asymmetry is deliberate and pinned by a test: `13.ServiceDefaults`' `BaggageLogRecordProcessor` copies baggage onto log records *generically*, so a baggage key string becomes the emitted log property name. Renaming it would silently rename a field that deployed dashboards and alert rules filter on.

**`LoggingEventIdRanges`** reserves a 1000-wide `EventId` block per capability domain (`{domain number} * 1000`), subdivided into 100-wide per-package sub-blocks. Derive every `[LoggerMessage]` EventId from it as an expression, so the number stays traceable to the domain that owns it:

```csharp
[LoggerMessage(
    EventId = LoggingEventIdRanges.Caching + 100,
    Level = LogLevel.Warning,
    Message = "Cache backplane reconnected after {AttemptCount} attempts")]
public static partial void BackplaneReconnected(ILogger logger, int attemptCount);
```

All three live here rather than in `04.Contracts` because `SharedKernel.Communication.Grpc` is mechanically barred from referencing `04.Contracts`, and `01.Core` is the one layer every consumer already references.

---

## Traps worth knowing

Each of these was found by executing the assembly, and each is pinned by a test.

**`default(Result)` is not a valid result.** `Result` is a struct, so the runtime can hand you an all-zero instance that ran neither factory — `default(Result)`, an unassigned field, an element of `new Result[n]`, or the `out` value of a failed `TryGetValue`. It reports `IsFailure` while carrying no error. Reading `Error` throws an `InvalidOperationException` naming this cause rather than returning `null`. (`Result<T>` is a class, so its `default` is simply `null` and fails immediately.)

**`IHasSuccessFlag` boxes the non-generic `Result`.** Measured at **32 bytes per check** for `Result` against **0 bytes** for `Result<T>`. It's AOT-clean either way — no reflection — but on a per-request hot path that only needs a non-generic `Result`, test the concrete type (`response is Result r`) instead, which matches without boxing.

**A duplicate `SmartEnum` value surfaces late.** Lookup tables build on first use, so a duplicate value or name throws at the first `FromValue`/`FromName` call rather than at the declaration — it can sit undetected until something reads it. The exception names the type, the key, and both colliding members.

**`ErrorType` is a wire contract.** Its numeric values are explicit and must never be renumbered — the enum is serialized and persisted, so changing a value reinterprets stored data. Adding a member is safe at runtime but breaks an exhaustive `switch` with no discard arm at compile time.

---

## Deliberately not here

- **Railway combinators** (`Map`, `Bind`, `Ensure`, `Tap`, `TapError`, `Match`, `GetValueOrThrow`) for both `Result<T>` and `Result`, with `Task` and `ValueTask` overloads → `SharedKernel.Core`, alongside `ResultTry` (exception boundaries) and `ResultCombine` (aggregating several results).
- **Guard clauses** (`Guard.Against.*`, `Guard.Throw.*`, `Guard.Collect`) → `SharedKernel.Core`, in the `SharedKernel.Guards` namespace.
- **Exceptions** (`DomainException`, `ValidationException`, `NotFoundException`, `ConflictException`, `UnauthorizedException`, `ForbiddenException`) and `error.ToException()`, which picks the one matching an `ErrorType` → `SharedKernel.Core`. Primitives deliberately contains no exception types: it models failures as values.
- **A metadata bag on `Error`** — evaluated and declined. It breaks the type's value-equality contract and raises AOT and cross-process-serialization questions. Both motivating needs (per-field validation errors, a retry-after hint) are solved at the `ProblemDetails` boundary in `14.Presentation` instead.
- **A DI extension for `IIdGenerator`** — one implementation, one line to register.
- **A local-time member on `IClock`** — presentation concern.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the [01.Core README](../README.md) for the full capability overview.
