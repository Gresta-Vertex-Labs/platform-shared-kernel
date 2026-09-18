# 01.Core

Foundational building blocks for the Platform.SharedKernel ecosystem. Twelve independently publishable NuGet packages (thirteen minus `SharedKernel.Guards`, merged into `SharedKernel.Core` by P-505/WO-082; guard clauses now live in the single `SharedKernel.Guards` namespace inside `SharedKernel.Core`) — every one but `SharedKernel.Validation.FluentValidation`, `SharedKernel.Cryptography.KeyVault.Azure`, `SharedKernel.Cryptography.Argon2`, and `SharedKernel.Localization` (a first-party Microsoft dependency, not a third-party one) has zero third-party NuGet dependencies.

| Package | Purpose |
|---------|---------|
| `SharedKernel.Primitives` | `Result<T>`, `Error`, `IClock`, `IIdGenerator`, `SmartEnum`, `ValidationResult` |
| `SharedKernel.Core` | Railway extensions for `Result`/`Result<T>` (sync, `Task`, `ValueTask`), `ResultTry`, `ResultCombine`, base exceptions, BCL helpers, and the two-path guard system: `Guard.Against.*` (functional) + `Guard.Throw.*` (imperative) |
| `SharedKernel.Configuration` | `AddValidatedOptions` startup-validation pattern |
| `SharedKernel.FeatureManagement` | `IFeatureManager` abstraction over Microsoft.FeatureManagement |
| `SharedKernel.Cryptography` | AES-256-GCM encryption, key rotation, envelope encryption, HKDF subkeys, RSA/ECDSA and HMAC signing, PHC password hashing, fixed-time comparison, secure random, HOTP/TOTP |
| `SharedKernel.Compression` | Generic payload compression (`IPayloadCompressor`): framed Brotli default, gzip keyed alternate; truncation-detecting frame, raw mode for external interop, bounded decompression |
| `SharedKernel.Validation` | Culture-independent IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT validators + pluggable national-ID registry |
| `SharedKernel.Validation.FluentValidation` | `IRuleBuilder<T,string>` adapter over `SharedKernel.Validation` (a third-party dependency — `FluentValidation`) |
| `SharedKernel.Cryptography.KeyVault.Azure` | Azure Key Vault encryption keys (data keys as secret versions, envelope provider, readiness probe) and signing keys (a third-party dependency — `Azure.Security.KeyVault.Keys` + `Azure.Security.KeyVault.Secrets` + `Azure.Identity`) |
| `SharedKernel.Cryptography.Argon2` | Argon2id one-way hash algorithm for `IOneWayHasher`, selected by configuration (a third-party dependency — `Konscious.Security.Cryptography.Argon2`) |
| `SharedKernel.DataPrivacy` | `DataClassificationAttribute`/`SensitiveDataCategoryAttribute` pure-metadata markers, `PiiMasking.*` deterministic masking helpers, `IDataSubjectRequestHandler` export/erasure contract |
| `SharedKernel.Localization` | Typed message definitions (`LocalizedMessage.Define<T1…T4>`) whose errors carry their arguments, named-placeholder templates formatted per culture, and an immutable catalog loaded from JSON or `.resx` and validated at startup (a first-party dependency — `Microsoft.Extensions.Localization.Abstractions`) |

All packages target `net10.0`. Options binding is reflective, so `SharedKernel.Configuration` and the registration methods that bind options are not trim- or AOT-safe.

---

## DI Registration Conventions (SK.01.P518)

Every `AddSharedKernelXxx(...)`/`AddXxx(...)` DI extension method across `01.Core` registers its
services via `TryAddSingleton`/`TryAddKeyedSingleton` (or `TryAddEnumerable` — see the two named
exceptions below), never a plain `AddSingleton`/`AddKeyedSingleton`. Two consequences follow from
this, consistently across every package in this domain:

- **Calling the same `AddSharedKernelXxx(...)` method more than once never double-registers.**
  A microservice that references two `01.Core` packages which both, transitively, call
  `AddSharedKernelCryptography()` (for example) gets exactly one registration of each service —
  not two competing ones racing to be "the" resolved instance.
- **A consumer registration made *before* the platform's `AddSharedKernelXxx(...)` call always
  wins.** Register your own fake/override implementation first (e.g. in a test host, or to swap
  in a bespoke implementation), then call the platform's registration method — `TryAdd*` sees the
  service type already claimed and leaves your registration alone. This is the idiom every
  package's own DI extension method documents in its XML remarks; look there for the exact set of
  services a given method registers.

**Two deliberate, load-bearing exceptions to plain `TryAddSingleton`, both because the underlying
service type is a genuine, intentional *multi-implementation collection* rather than a
single-winner service:**

1. **`SharedKernel.Validation`'s `AddNationalIdValidator<TValidator>()`** registers
   `INationalIdValidator` via `TryAddEnumerable(ServiceDescriptor.Singleton<INationalIdValidator, TValidator>())`,
   never a plain `TryAddSingleton`. `INationalIdValidatorRegistry` is built by iterating
   *every* registered `INationalIdValidator` via `IServiceProvider.GetServices<INationalIdValidator>()`
   — a plain `TryAddSingleton` would collapse that to a single winner and silently drop every
   country validator registered after the first one ever registered. `TryAddEnumerable` still
   prevents the identical `(INationalIdValidator, TValidator)` pair from registering twice (e.g.
   calling `AddNationalIdValidator<T>()` for the same `T` more than once), while preserving the
   multi-country collection semantics for every distinct `TValidator`.
2. **Options validators** (for example `SharedKernel.Cryptography.KeyVault.Azure`'s encryption and signing
   options validators) register against `IValidateOptions<T>` with `TryAddEnumerable`, through
   `AddValidatedOptions<TOptions, TValidator>(configuration, validateDataAnnotations: true)`.
   `Microsoft.Extensions.Options` runs *every* registered `IValidateOptions<T>` for a type, and the Data
   Annotations validator is registered against the same service type, so a plain `TryAddSingleton` would
   silently drop the cross-field checks.

3. **`SharedKernel.Configuration`'s own `AddValidatedOptions<TOptions, TValidator>`** registers
   `TValidator` against `IValidateOptions<TOptions>` via `TryAddEnumerable` for exactly the reason
   given in (2) — added by P-530, which found that this overload had been using `TryAddSingleton`
   all along. That is the same defect (2) describes, at its source: a caller's validator was
   silently dropped whenever any other validator for that options type already existed, and
   measured, a cross-property rule never ran while configuration violating it started the host
   cleanly. Every package in this domain now registers its validators through that overload.

   **One further wrinkle, worth knowing before applying `TryAddEnumerable` anywhere by reflex:** the
   Data Annotations half of the same package canNOT use it. `TryAddEnumerable` de-duplicates on
   *implementation type*, every named options instance shares the single implementation type
   `DataAnnotationValidateOptions<TOptions>`, and that validator is itself **name-scoped** — it
   returns `Skip` for any other name. Registering it through `TryAddEnumerable` would therefore
   cover the first name registered and leave every other named instance of that type completely
   unvalidated. `SharedKernel.Configuration` instead registers a pre-built immutable instance
   behind a per-*name* duplicate check, which is only possible because an instance descriptor
   exposes an inspectable `Name` where a factory descriptor does not. **`TryAddEnumerable` is not a
   drop-in for `TryAddSingleton` when the service is keyed on something finer than its
   implementation type.**

**One deliberate exception to "a second call is harmless":** `SharedKernel.Localization`'s
`AddLocalizationCatalog(...)` and `AddStringLocalizerCatalog<TResource>()` throw
`InvalidOperationException` when an `ILocalizationCatalog` is already registered, and only then call
`TryAddSingleton`. A silently ignored second catalog would mean its translations never appear, so
the conflict is reported instead. Combine several translation sources in one
`LocalizationCatalogBuilder`.

If you are adding a new `01.Core` DI extension method: default to `TryAddSingleton`/
`TryAddKeyedSingleton`. Reach for `TryAddEnumerable` only when the service type is genuinely meant
to be resolved as a collection (via `IEnumerable<T>`/`GetServices<T>()`) by something else in the
same package — never as a way to "be extra safe" on an ordinary single-winner service, since it
changes the double-registration semantics in a way a plain `TryAddSingleton` reader would not
expect.

---

## SharedKernel.Primitives

### Result\<T\> — Operation Outcomes

`Result<T>` represents the outcome of an operation that either succeeds with a value or fails with a structured `Error`. It is a sealed class (not a struct) to avoid the zero-value problem with generic payloads.

```csharp
// Success path
Result<Order> result = Result<Order>.Success(order);
// or via implicit operator:
Result<Order> result = order;

// Failure path
Result<Order> result = Result<Order>.Failure(Error.NotFound("order.not_found", "Order 42 does not exist."));
// or via implicit operator:
Result<Order> result = Error.NotFound("order.not_found", "Order 42 does not exist.");

// Inspecting the result
if (result.IsSuccess)
{
    Console.WriteLine(result.Value.Id);   // safe — IsSuccess is true
}
else
{
    Console.WriteLine(result.Error.Code); // safe — IsFailure is true
}

// Accessing Value on a failure result throws InvalidOperationException.
// Accessing Error on a success result throws InvalidOperationException.
// Always check IsSuccess / IsFailure first, or use Match (see railway section).
```

#### Non-generic Result (void operations)

Use `Result` (non-generic, `readonly struct`) for operations that have no success payload:

```csharp
Result result = Result.Success();
Result result = Result.Failure(Error.Unauthorized("auth.denied", "Access denied."));

// Implicit from Error:
Result result = Error.Conflict("order.duplicate", "Order already submitted.");
```

---

### Error — Structured Failures

`Error` is a sealed record with a machine-readable `Code`, a human-readable `Message`, and an `ErrorType` discriminator. Use `Error.None` as the sentinel — never `null`.

```csharp
// Factory methods — one per ErrorType variant
Error e1 = Error.Validation("user.email_required", "Email address is required.");
Error e2 = Error.NotFound("product.not_found", "Product SKU-42 was not found.");
Error e3 = Error.Conflict("order.duplicate", "An order for this customer already exists.");
Error e4 = Error.Unauthorized("auth.token_expired", "Your session has expired.");
Error e5 = Error.Unexpected("infra.db_timeout", "Database query timed out.");
Error e6 = Error.Forbidden("approval.self_approval_denied", "You cannot approve your own request.");

// Sentinel — no error
Error none = Error.None;

// Discriminating by type
string response = error.Type switch
{
    ErrorType.NotFound     => "404 Not Found",
    ErrorType.Validation   => "400 Bad Request",
    ErrorType.Unauthorized => "401 Unauthorized",
    ErrorType.Forbidden    => "403 Forbidden",
    ErrorType.Conflict     => "409 Conflict",
    _                      => "500 Internal Server Error",
};
```

#### Error.Forbidden vs. Error.Unauthorized — Choosing the Right One

Both map to a rejected request, but they mean different things. `Error.Unauthorized` says the caller is **not permitted to attempt this at all** — no or invalid credentials (HTTP 401). `Error.Forbidden` says the caller **is generally permitted to attempt this kind of operation, but this specific instance/condition is not satisfied** — e.g. a maker-checker dual-approval gate rejecting the same user who submitted the request, or a role/permission check rejecting an authenticated-but-under-privileged caller (HTTP 403).

```csharp
// Unauthorized — the caller has no valid credentials at all
Error.Unauthorized("auth.token_expired", "Your session has expired.");

// Forbidden — the caller is authenticated, but this specific action is not allowed for them
Error.Forbidden("approval.self_approval_denied", "You cannot approve your own request.");
```

Substituting `Unauthorized` for a genuinely `Forbidden` condition is a platform anti-pattern — pick the factory that matches the actual reason for rejection, not the one that happens to be more familiar.

#### ErrorCodes — Well-Known Code Constants

`ErrorCodes` provides stable string constants for common error codes:

```csharp
// Use constants instead of magic strings
Error.Validation(ErrorCodes.Validation.Required, "Name is required.");
Error.Validation(ErrorCodes.Validation.OutOfRange, "Quantity must be between 1 and 100.");
Error.NotFound(ErrorCodes.NotFound.Default, "The requested resource was not found.");
Error.Conflict(ErrorCodes.Conflict.Duplicate, "A record with this key already exists.");
Error.Unauthorized(ErrorCodes.Unauthorized.Expired, "Authentication token has expired.");

// Consuming packages may define their own constants locally without forking SharedKernel:
public static class OrderErrorCodes
{
    public const string DuplicateOrder         = "order.duplicate";
    public const string InsufficientInventory  = "order.insufficient_inventory";
}
```

---

### IClock — Time Abstraction

All time reads must go through `IClock`. Direct use of `DateTime.UtcNow` or `DateTimeOffset.UtcNow` in production code is a violation — it makes tests non-deterministic.

```csharp
// Register in DI (production)
services.AddClock(); // wires SystemClock as singleton IClock

// Inject and use
public sealed class OrderService(IClock clock)
{
    public Order CreateOrder(Guid customerId)
    {
        return new Order
        {
            Id         = Guid.NewGuid(),
            CustomerId = customerId,
            CreatedAt  = clock.UtcNow,   // DateTimeOffset
            OrderDate  = clock.Today,    // DateOnly
        };
    }
}

// In tests — use a fake that returns a fixed time:
public sealed class FakeClock(DateTimeOffset fixedTime) : IClock
{
    public DateTimeOffset UtcNow => fixedTime;
    public DateOnly Today => DateOnly.FromDateTime(fixedTime.UtcDateTime);
}

// Test setup:
var clock = new FakeClock(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
var service = new OrderService(clock);
```

---

### SystemClock — TimeProvider Interop

`SystemClock` (the production `IClock` implementation) internally sources `UtcNow` from an injected `System.TimeProvider` (shipped in the BCL since .NET 8) instead of calling `DateTimeOffset.UtcNow` directly. `IClock`'s own public contract — `UtcNow`, `Today` — is completely unchanged by this: `TimeProvider` is purely an internal implementation detail of `SystemClock`, never an alternative time source that domain/application call sites should reference directly.

```csharp
// Default — backed by TimeProvider.System (the real system clock). Behaviorally identical to
// before this internal change; AddClock() still just registers this.
services.AddClock(); // wires new SystemClock() as singleton IClock

// Equivalent explicit form, if you construct it yourself:
services.AddSingleton<IClock>(new SystemClock(TimeProvider.System));
```

A host that already has its own shared, custom `TimeProvider` registered — for coordinated simulation, deterministic replay, or a single time source shared with other `TimeProvider`-aware libraries in the process — can wire `SystemClock` to reuse that same instance instead of `TimeProvider.System`:

```csharp
// Register your shared custom TimeProvider once, at the composition root...
services.AddSingleton<TimeProvider>(mySimulationTimeProvider);

// ...then resolve it into SystemClock's constructor:
services.AddSingleton<IClock>(sp => new SystemClock(sp.GetRequiredService<TimeProvider>()));
```

This package ships no dedicated DI extension for the `TimeProvider`-accepting constructor — the plain `AddSingleton` calls above are the sanctioned pattern, consistent with `IIdGenerator`'s own no-extension precedent above. `SK0001` (the analyzer flagging direct `DateTime.UtcNow`/`DateTimeOffset.UtcNow` usage) is unaffected: it still only needs to recognize `IClock` at call sites, never `TimeProvider`.

---

### IIdGenerator — Time-Ordered Identifiers

`IIdGenerator` is an opt-in alternative to calling `Guid.NewGuid()` directly when generating a new primary-key-shaped identifier. A fully-random UUID v4 (what `Guid.NewGuid()` produces) is a well-documented Postgres/SQL Server clustered/primary-key index anti-pattern: random insert points across the B-tree cause page splits and fragmentation as a table grows. The default implementation, `UuidV7IdGenerator`, generates RFC 9562 UUID version 7 values instead — a 48-bit millisecond timestamp in the high bits followed by random bits, so values generated close together in time sort close together, restoring sequential-insert locality while still requiring no central coordinator.

This is purely additive — no existing `Guid.NewGuid()` call site is forced to change.

```csharp
// Register in DI (production) — this package ships no AddIdGenerator() extension;
// register the plain interface/implementation pair at your own composition root:
services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();

// Inject and use — e.g., as the identifier source inside an IAggregateFactory implementation
public sealed class OrderFactory(IIdGenerator idGenerator) : IAggregateFactory<Order>
{
    public Order Create(Guid customerId) => new(idGenerator.NewId(), customerId);
}
```

Ordering guarantee: two values whose embedded millisecond timestamps differ always compare as non-decreasing under the default `Guid` comparer (`CompareTo`/`<`). Two values generated within the *same* millisecond carry no ordering guarantee relative to each other — the remaining bits are cryptographically random, not a monotonic counter — but that is still exactly what restores index locality in practice: real production inserts are spread across many milliseconds, and same-millisecond ties still land immediately adjacent to each other in the index regardless of the random tie-break.

```csharp
// In tests — IIdGenerator is a one-method interface; a fixed-sequence fake is trivial:
public sealed class FakeIdGenerator(params Guid[] ids) : IIdGenerator
{
    private int _index;
    public Guid NewId() => ids[_index++];
}
```

---

### SmartEnum — Type-Safe Enumeration

`SmartEnum<TEnum, TValue>` is an abstract base for type-safe enumerations that avoid the limitations of plain C# `enum`. Value lookups use a static compile-time list — no reflection in the hot path.

```csharp
// Define a SmartEnum
public sealed class OrderStatus : SmartEnum<OrderStatus, int>
{
    public static readonly OrderStatus Pending    = new(nameof(Pending),    1);
    public static readonly OrderStatus Processing = new(nameof(Processing), 2);
    public static readonly OrderStatus Shipped    = new(nameof(Shipped),    3);
    public static readonly OrderStatus Cancelled  = new(nameof(Cancelled),  4);

    private OrderStatus(string name, int value) : base(name, value) { }
}

// Value lookup (throws InvalidOperationException if not found)
OrderStatus status = OrderStatus.FromValue(2);        // OrderStatus.Processing
OrderStatus status = OrderStatus.FromName("Shipped"); // OrderStatus.Shipped

// Safe lookup (no throw)
if (OrderStatus.TryFromValue(99, out var found))
{
    // found is the matching member
}
else
{
    // found is null
}

// Enumerate all members (declaration order)
foreach (OrderStatus s in OrderStatus.List)
{
    Console.WriteLine($"{s.Name}: {s.Value}");
}

// ToString() returns the Name
Console.WriteLine(OrderStatus.Shipped); // "Shipped"
```

> **Static-initialization safety (SK.01.P515):** `FromValue`/`TryFromValue`/`FromName`/`List` all
> resolve correctly even when a `SmartEnum`-derived type is first touched *exclusively* through one
> of these inherited static members — e.g. calling `OrderStatus.FromValue(2)` as the very first
> reference to `OrderStatus` anywhere in the process, with no prior reference to
> `OrderStatus.Pending`/`.Processing`/etc. `SmartEnum<TEnum,TValue>` forces `TEnum`'s own static
> constructor to run exactly once, as part of `SmartEnum<TEnum,TValue>`'s own static initialization,
> closing an ECMA-335 static-initialization gap where reaching an inherited static member through a
> derived type name does not otherwise guarantee the derived type's own field initializers (the ones
> that populate the member list) have already run. This costs nothing on the hot lookup path — the
> force happens once per closed generic type, during that type's one-time static initialization,
> never inside `FromValue`/`TryFromValue`/`FromName` themselves.

---

### ValidationResult — Multi-Error Validation

`ValidationResult` and `ValidationResult<T>` model compound input validation where multiple errors may be collected in a single pass. This is distinct from `Result<T>` — do not conflate the two.

| Type | Cardinality | When to use |
|------|-------------|------------|
| `Result<T>` | Single error | Operation outcome (command/query handler return) |
| `ValidationResult` | Multiple errors | Input validation (validate entire command object) |

```csharp
// Non-generic: validate a command, collect all errors
var errors = new List<Error>();

if (string.IsNullOrWhiteSpace(command.Name))
    errors.Add(Error.Validation(ErrorCodes.Validation.Required, "Name is required."));

if (command.Quantity is < 1 or > 1000)
    errors.Add(Error.Validation(ErrorCodes.Validation.OutOfRange, "Quantity must be 1-1000."));

ValidationResult validation = errors.Count > 0
    ? ValidationResult.Failure(errors)
    : ValidationResult.Success();

if (!validation.IsValid)
{
    foreach (Error e in validation.Errors)
        Console.WriteLine(e.Message);
}

// Returning the whole set through the Result railway: one Error carrying every field failure
// in Error.Details, which 14.Presentation renders as the ProblemDetails "errors" map.
Result<Order> failed = Result<Order>.Failure(Error.Validation(validation.Errors));

// Generic: validation that also produces a parsed/transformed value
ValidationResult<ParsedAddress> result = ParseAddress(rawInput);
if (result.IsValid)
{
    ParsedAddress address = result.Value; // safe — IsValid is true
}
```

---

### LoggingEventIdRanges — EventId Registry

`LoggingEventIdRanges` is a compile-time `const int` registry reserving a contiguous 1000-wide `EventId` block per root folder-map domain (00.Governance through 20.Reporting). Every package anywhere in the repo that authors `[LoggerMessage]` methods must derive its `EventId` values from this registry — never an ad hoc numeric literal.

```csharp
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

internal static partial class ApplicationLogMessages
{
    // 05.Application's reserved block starts at LoggingEventIdRanges.Application (5000).
    // A downstream package computes its own EventId as "domain base + local offset" —
    // the offset is a compile-time constant expression, which [LoggerMessage] requires.
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Application + 42,
        Level = LogLevel.Information,
        Message = "Handled request {RequestName} in {ElapsedMilliseconds}ms")]
    public static partial void RequestHandled(
        this ILogger logger, string requestName, long elapsedMilliseconds);
}
```

A domain composed of multiple packages (e.g. `02.Caching`'s Redis role-split) subdivides its own 1000-wide block into 100-wide sub-blocks, one per package, in declaration order — `LoggingEventIdRanges` reserves only the domain-level boundary; the sub-block assignment is each domain's own documentation responsibility.

---

### Well-Known Propagation Constants — WellKnownHeaders / WellKnownBaggageKeys / WellKnownTagKeys

`WellKnownHeaders`, `WellKnownBaggageKeys`, and `WellKnownTagKeys` are compile-time `const string` registries reserving the platform's cross-service propagation identifier literals — HTTP/gRPC-metadata header names, `System.Diagnostics.Activity` baggage keys, and `Activity` tag/attribute keys. Every domain that reads or writes a correlation-id or tenant-id header, an `Activity` baggage entry, or an `Activity` span tag for the same concepts, must reference these constants instead of redeclaring the literal locally.

```csharp
using SharedKernel.Primitives.Propagation;

// A downstream typed REST client reads the tenant header instead of a local literal:
internal sealed class TenantIdDelegatingHandler(ITenantProvider tenantProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(
            WellKnownHeaders.TenantId, tenantProvider.TenantId.ToString());

        return base.SendAsync(request, cancellationToken);
    }
}

// Presentation-layer middleware writes Activity baggage under the shared correlation key,
// and 13.ServiceDefaults's log-record processor reads the identical key back out — both
// sides reference WellKnownBaggageKeys.CorrelationId so they can never drift apart again:
Activity.Current?.AddBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);

// A span emitted anywhere in the request pipeline tags itself with the shared tenant/error
// attribute keys instead of a local literal — Activity.SetTag is a distinct call-site shape
// from Activity.AddBaggage above, even where a literal value (CorrelationId) coincides:
using Activity? activity = ActivitySource.StartActivity("ProcessOrder");
activity?.SetTag(WellKnownTagKeys.TenantId, tenantProvider.TenantId.ToString());
if (result.IsFailure)
{
    activity?.SetTag(WellKnownTagKeys.ErrorType, result.Error.Type.ToString());
    activity?.SetTag(WellKnownTagKeys.ErrorCode, result.Error.Code);
}
```

`WellKnownHeaders.CorrelationId` = `"X-Correlation-Id"`, `WellKnownHeaders.TenantId` = `"X-Tenant-Id"`, `WellKnownBaggageKeys.CorrelationId` = `"correlation.id"`, `WellKnownTagKeys.TenantId` = `"tenant.id"`, `WellKnownTagKeys.CorrelationId` = `"correlation.id"`, `WellKnownTagKeys.ErrorType` = `"error.type"`, `WellKnownTagKeys.ErrorCode` = `"error.code"`. These values are a pure promotion of today's de facto platform standard — changing one is a breaking, cross-domain change, never a routine edit. `WellKnownTagKeys` is introduced pre-emptively (P-294/WO-049): no domain has adopted it yet, so retrofitting an existing `Activity.SetTag(...)` call site to reference it is that consuming domain's own future responsibility.

---

## SharedKernel.Core — Railway-Oriented Extensions

Package reference: [SharedKernel.Core/README.md](SharedKernel.Core/README.md).

### Result\<T\> Railway Pattern

Railway-oriented programming chains `Result`-returning operations without nested `if` blocks. When a step fails, every later step is skipped and the original error flows to the end of the chain.

```
Input ──► [Step 1] ──success──► [Step 2] ──success──► [Step 3] ──success──► Output
                    failure                  failure                  failure
                       │                       │                        │
                       └───────────────────────┴────────────────────────► Error propagates
```

| Method | On success | On failure |
|--------|------------|------------|
| `Map` | Transforms the value | Passes the error through |
| `Bind` | Runs the next `Result`-returning step (`Result<TOut>` or non-generic `Result`) | Passes the error through |
| `Ensure` | Fails with the supplied error when the predicate is false | Passes the error through |
| `Tap` | Runs a side effect, returns the result unchanged | Skipped |
| `TapError` | Skipped | Runs a side effect, returns the result unchanged |
| `MapError` | Passes the value through | Transforms the error |
| `Match` | Folds to one value (or runs one of two actions) | Folds to one value (or runs one of two actions) |
| `GetValueOrThrow` / `ThrowIfFailure` | Returns the value / does nothing | Throws `error.ToException()` |

Every method exists for `Result<T>` and for the non-generic `Result`.

```csharp
Result<Order> result = repository.FindOrder(orderId)          // Result<Order>
    .Ensure(order => order.Status == OrderStatus.Open, OrderErrors.NotOpen)
    .Bind(order => inventory.Reserve(order))                  // Result<Order>
    .Tap(order => Log.OrderReserved(logger, order.Id))
    .TapError(error => Log.ReservationFailed(logger, error.Code))
    .MapError(error => error.Type == ErrorType.NotFound
        ? Error.NotFound("order.not_found", $"Order {orderId} was not found.")
        : error);

// Commands that return the non-generic Result chain the same way.
Result saved = command.Validate()
    .Bind(() => repository.Save(command))
    .Tap(() => Log.CommandSaved(logger));
```

#### Async chains

A chain may start from a plain result, a `Task<Result…>`, or a `ValueTask<Result…>`. Each source accepts synchronous steps, plus asynchronous steps of **its own awaitable type**:

| Source | Synchronous steps | Asynchronous steps |
|--------|-------------------|--------------------|
| `Result<T>` / `Result` | yes | `Task`-returning |
| `Task<Result<T>>` / `Task<Result>` | yes | `Task`-returning |
| `ValueTask<Result<T>>` / `ValueTask<Result>` | yes | `ValueTask`-returning |

Offering both `Task` and `ValueTask` steps on one source would make every `async` lambda ambiguous, so the awaitable type never mixes.

```csharp
public async Task<IResult> PlaceOrderAsync(PlaceOrderCommand command, CancellationToken ct)
{
    Result<Order> result = await repository.FindCustomerAsync(command.CustomerId, ct)  // Task<Result<Customer>>
        .Ensure(customer => customer.IsActive, CustomerErrors.Inactive)
        .Bind(customer => BuildOrder(command, customer))               // Result<Order>
        .Bind(order    => inventory.ReserveAsync(order, ct))           // Task<Result<Order>>
        .Tap(order     => Log.OrderReserved(logger, order.Id))
        .Bind(order    => repository.SaveAsync(order, ct));            // Task<Result<Order>>

    return result.ToProblemDetailsResult(order => Results.Created($"/orders/{order.Id}", order));
}
```

`ToProblemDetailsResult` comes from `14.Presentation`'s `SharedKernel.Presentation.WebApi`, which turns a failure into an RFC 9457 response whose status follows `Error.Type`.

#### Error propagation rules

1. The first failure makes every later `Map`, `Bind`, `Ensure`, and `Tap` a no-op.
2. `MapError` and `TapError` are the only steps that run on the failure path.
3. `Match` always runs exactly one branch; it is the natural end of a chain.
4. Don't throw inside a step. Return a failed result instead.
5. Awaiting an async chain rethrows the original exception of a faulted task and `OperationCanceledException` for a cancelled one. Neither is turned into a failed result.
6. A lambda whose body only throws (`() => throw …`) has no return type and matches both the synchronous and the asynchronous overload. Give it an explicit return type: `Result () => throw …`.

---

### Result Exception Boundary and Multi-Result Aggregation

#### ResultTry — Wrapping a Throwing Call

`ResultTry` runs a delegate and converts a thrown exception into a failed result. Use it wherever result-oriented code calls a throwing third-party SDK or a BCL method with no result-returning equivalent, instead of hand-writing `try`/`catch`.

| Member | Returns |
|--------|---------|
| `Try<T>(Func<T> [, onException])` | `Result<T>` |
| `Try(Action [, onException])` | `Result` |
| `TryAsync<T>(Func<Task<T>> [, onException])` | `Task<Result<T>>` |
| `TryAsync(Func<Task> [, onException])` | `Task<Result>` |
| `TryAsync<T>(Func<CancellationToken, Task<T>> [, onException], CancellationToken)` | `Task<Result<T>>` |
| `TryAsync(Func<CancellationToken, Task> [, onException], CancellationToken)` | `Task<Result>` |

```csharp
// Default mapping
Result<Customer> customer = ResultTry.Try(() => thirdPartySdk.GetCustomer(customerId));

// Custom mapping for a known exception
Result<Customer> mapped = ResultTry.Try(
    () => thirdPartySdk.GetCustomer(customerId),
    ex => ex is SdkNotFoundException
        ? Error.NotFound("customer.not_found", $"Customer {customerId} was not found.")
        : Error.Unexpected(ErrorCodes.Unexpected.Default, ResultTry.DefaultUnexpectedMessage));

// With a cancellation token: throws OperationCanceledException without running the delegate
// if the token is already cancelled.
Result<Invoice> invoice = await ResultTry.TryAsync(ct => paymentGateway.ChargeAsync(order, ct), ct);
```

- **Safe default message.** Without a mapper, the error is `Error.Unexpected(ErrorCodes.Unexpected.Default, ResultTry.DefaultUnexpectedMessage)`. The exception's type and message are never copied into it, because they can carry connection-string fragments, user names, or internal host names, and an error message can reach an HTTP response (P-510/WO-083).
- **Where the exception goes.** It is recorded on the current span with `Activity.Current?.AddException(exception)`, once per inner exception of a flattened `AggregateException`, and exported by `13.ServiceDefaults`'s tracing pipeline. With no current span it is recorded nowhere; pass an `onException` mapper when you need a guaranteed capture path.
- **Cancellation.** `OperationCanceledException` and `TaskCanceledException` always propagate, with or without a mapper. A client disconnect must never look like an ordinary failure.

#### ResultCombine and Guard.Collect — Reporting Every Failure

`ResultCombine.Combine` folds independent `Result`/`Result<T>` outcomes into a `ValidationResult` / `ValidationResult<IReadOnlyList<T>>`. Every input is evaluated, so a failure carries every failing `Error` in input order. For guard results, `Guard.Collect` does the same without wrapping each one in a `Result`.

```csharp
// Several independent guards
ValidationResult validation = Guard.Collect(
    Guard.Against.NullOrWhiteSpace(command.Email),
    Guard.Against.OutOfRange(command.Age, 0, 150));

if (!validation.IsValid)
    throw new ValidationException(validation.Errors);

// Several Result<T>-returning steps, collecting every success value in order
ValidationResult<IReadOnlyList<LineItem>> lineItems = ResultCombine.Combine(
    request.Lines.Select(line => ParseLineItem(line)));   // IEnumerable<Result<LineItem>>
```

---

### Base Exceptions

The exception hierarchy connects the result railway to code that works with exceptions. Every exception carries a structured `Error`, and every constructor rejects a `null` one; string-only constructors are not provided (analyzer `SK0005`).

```csharp
throw new DomainException(Error.BusinessRule("order.max_items", "Orders cannot exceed 50 items."));
throw new ValidationException(validationResult.Errors);          // or a single Error
throw new NotFoundException(Error.NotFound("product.not_found", "SKU-42 not found."));
throw new ConflictException(Error.Conflict("order.duplicate", "Duplicate order detected."));
throw new UnauthorizedException(Error.Unauthorized("auth.expired", "The token has expired."));
throw new ForbiddenException(Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission, "Not permitted."));

// Or let the error pick the exception type
throw error.ToException();
Order order = result.GetValueOrThrow();
```

`14.Presentation` derives the HTTP status from `Error.Type`, not from the exception class. Keep the two consistent; `error.ToException()` does that for you.

| Exception | Pair it with | HTTP status (from `Error.Type`) |
|-----------|--------------|---------------------------------|
| `ValidationException` | `ErrorType.Validation` | 400 |
| `NotFoundException` | `ErrorType.NotFound` | 404 |
| `ConflictException` | `ErrorType.Conflict` | 409 |
| `UnauthorizedException` | `ErrorType.Unauthorized` | 401 |
| `ForbiddenException` | `ErrorType.Forbidden` | 403 |
| `DomainException` (not sealed) | `ErrorType.BusinessRule`, or any other | per `Error.Type` |

---

### BCL Extension Methods

```csharp
// Casing: one word splitter shared by all four, invariant culture
"HTMLParser".ToSnakeCase()     // "html_parser"
"HTMLParser".ToKebabCase()     // "html-parser"
"user_profile".ToPascalCase()  // "UserProfile"
"UserProfile".ToCamelCase()    // "userProfile"

// Sequences
List<string>? names = null;
if (!names.IsNullOrEmpty())
    Console.WriteLine(names.Count);   // no nullable warning: IsNullOrEmpty is [NotNullWhen(false)]

IEnumerable<string> clean = new string?[] { "a", null, "b" }.WhereNotNull();   // ["a", "b"]

// Whole-day ranges: use an exclusive end, not 23:59:59.999
DateTimeOffset start = clock.UtcNow.StartOfDay();
var todays = orders.Where(o => o.PlacedAt >= start && o.PlacedAt < start.AddDays(1));
```

The BCL already covers batching (`Enumerable.Chunk`), Unix time (`DateTimeOffset.ToUnixTimeMilliseconds`), and `string.IsNullOrWhiteSpace`, so this package does not duplicate them.

---

## SharedKernel.Configuration — Validated Options

Binds an options class to configuration and makes `IHost.StartAsync()` throw when it is invalid, so a
misconfigured service fails its deployment instead of its first request. The full reference, with every
measured trap, is the package's own [README](SharedKernel.Configuration/README.md). The essentials:

```csharp
public sealed class EmailOptions : ISectionBoundOptions
{
    public static string SectionName => "Email";

    [Required]         public string SmtpHost { get; set; } = string.Empty;
    [Range(1, 65535)]  public int    SmtpPort { get; set; } = 587;
}

builder.Services.AddValidatedOptions<EmailOptions>(
    builder.Configuration,
    strictness: OptionsStrictness.RequireSection | OptionsStrictness.RejectUnknownKeys);
```

| Need | Use |
| --- | --- |
| Per-property rules | Data Annotations on the options class (the default overloads) |
| A rule spanning two properties, or no validation reflection | `AddValidatedOptions<TOptions, TValidator>` with a hand-written or `[OptionsValidator]`-generated `IValidateOptions<TOptions>`; `validateDataAnnotations: true` adds the attributes back for a hand-written one |
| The section path declared once | Implement `ISectionBoundOptions` and pass the root `IConfiguration`; a null or blank `SectionName` throws at registration |
| A misspelled section path or key to fail startup | `OptionsStrictness.RequireSection` / `.RejectUnknownKeys` — both opt-in, because each rejects configuration that is sometimes legitimate |
| Nested objects or collection items validated | `[ValidateObjectMembers]` / `[ValidateEnumeratedItems]` on the property — without them, nested attributes never run |
| Two instances of one options type | The trailing `name` argument; consume through `IOptionsMonitor<T>.Get(name)` |

Validators register with `TryAddEnumerable` (never `TryAddSingleton`, which silently dropped a second
validator before P-530), and the Data Annotations and `RequireSection` validators are de-duplicated per
name, so a repeated call never duplicates a failure message. Binding is reflective, so every overload
declares `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`; this package is not trim- or AOT-safe.

---

## SharedKernel.FeatureManagement

### IFeatureManager — Feature Flags

`IFeatureManager` is the only permitted feature-flag interface in consuming services. Never inject `Microsoft.FeatureManagement.IFeatureManager` directly — doing so couples callers to the implementation package and prevents swapping providers.

#### Registration

```csharp
// Program.cs
builder.Services.AddSharedKernelFeatureManagement(builder.Configuration);
```

Feature flags are read from the `FeatureManagement` configuration section by convention:

```json
{
  "FeatureManagement": {
    "NewCheckoutFlow": true,
    "BetaDashboard": false
  }
}
```

#### Basic Usage

```csharp
public sealed class CheckoutService(IFeatureManager features)
{
    public async Task<CheckoutResult> CheckoutAsync(Cart cart, CancellationToken ct)
    {
        if (await features.IsEnabledAsync("NewCheckoutFlow", ct))
            return await NewCheckoutAsync(cart, ct);

        return await LegacyCheckoutAsync(cart, ct);
    }
}
```

#### Context-Aware Evaluation

The generic overload passes a context to context-aware filters (tenant targeting, user-based rollouts, etc.):

```csharp
public sealed class DashboardService(IFeatureManager features)
{
    public async Task<DashboardView> GetDashboardAsync(UserContext user, CancellationToken ct)
    {
        if (await features.IsEnabledAsync("BetaDashboard", user, ct))
            return await GetBetaDashboardAsync(user, ct);

        return await GetStandardDashboardAsync(user, ct);
    }
}
```

#### Define Feature Names as Constants

```csharp
public static class Features
{
    public const string NewCheckoutFlow = "NewCheckoutFlow";
    public const string BetaDashboard   = "BetaDashboard";
}

// Usage — no magic strings
if (await features.IsEnabledAsync(Features.NewCheckoutFlow, ct)) { ... }
```

#### Testing with IFeatureManager

```csharp
var features = Substitute.For<IFeatureManager>();
features.IsEnabledAsync(Features.NewCheckoutFlow, Arg.Any<CancellationToken>())
        .Returns(ValueTask.FromResult(true));

var service = new CheckoutService(features);
```

### Feature Variants — Gradual Rollout

Added in P-298/WO-049. `GetVariantAsync`/`GetVariantAsync<TContext>` bridge `Microsoft.FeatureManagement`'s variant/allocation support — weighted, named variants of a feature, not just on/off — the same way `IsEnabledAsync` bridges plain boolean evaluation. Both members return a neutral `FeatureVariant` (`.Name`, `.Configuration`); no `Microsoft.FeatureManagement` type ever appears on `IFeatureManager`'s surface. This is purely additive — the boolean `IsEnabledAsync` members above are completely unaffected.

#### Configuration — the Microsoft Feature Management schema

Plain boolean flags stay in the `FeatureManagement` section shown above. Weighted variants require `Microsoft.FeatureManagement`'s own [Microsoft Feature Management schema](https://github.com/microsoft/FeatureManagement/blob/main/Schema/FeatureManagement.v2.0.0.schema.json) — a `feature_management:feature_flags` array, distinct from and *in addition to* the `FeatureManagement` dictionary. Both schemas coexist in the same configuration and are resolved by the same `AddSharedKernelFeatureManagement(configuration)` call — no extra registration is needed, provided `configuration` is the application's **root** configuration (never a value already scoped to `configuration.GetSection("FeatureManagement")`; a pre-scoped section makes the `feature_management:feature_flags` schema unreachable, since it lives under an entirely different, unscoped root key):

```json
{
  "FeatureManagement": {
    "NewCheckoutFlow": true
  },
  "feature_management": {
    "feature_flags": [
      {
        "id": "PricingExperiment",
        "enabled": true,
        "variants": [
          { "name": "ControlGroup", "configuration_value": "control-config" },
          { "name": "DiscountedPrice", "configuration_value": "discounted-config" }
        ],
        "allocation": {
          "default_when_enabled": "ControlGroup",
          "percentile": [ { "variant": "DiscountedPrice", "from": 0, "to": 25 } ]
        }
      }
    ]
  }
}
```

The example above assigns roughly 25% of evaluated tenants to `DiscountedPrice` and the rest to `ControlGroup` — a classic percentage-based gradual rollout / A-B experiment.

#### Percentage-Based Enablement Across Tenants

```csharp
public sealed class PricingService(IFeatureManager features)
{
    public async Task<decimal> GetPriceAsync(string tenantId, decimal basePrice, CancellationToken ct)
    {
        // The context (here, a stable tenant id) determines which percentile bucket the caller
        // falls into. Repeated calls with the same tenantId always resolve to the same variant.
        var variant = await features.GetVariantAsync("PricingExperiment", tenantId, ct);

        return variant.Name switch
        {
            "DiscountedPrice" => basePrice * 0.9m,
            _ => basePrice, // "ControlGroup", or FeatureVariant.Unassigned if unconfigured — same price
        };
    }
}
```

`GetVariantAsync`'s no-context overload evaluates only the feature's `default_when_enabled`/`default_when_disabled` allocation (no percentile/user/group targeting is possible without a context):

```csharp
var variant = await features.GetVariantAsync("PricingExperiment", ct);
```

#### Deterministic Fallback — Never Throws

An unconfigured feature, an unknown feature name, or a context that resolves to no allocation branch never throws — `GetVariantAsync` returns the documented sentinel `FeatureVariant.Unassigned` (`Name == "Unassigned"`, `Configuration == null`) instead:

```csharp
var variant = await features.GetVariantAsync("SomeFeatureThatDoesNotExist", ct);
// variant == FeatureVariant.Unassigned — safe to branch on, never an exception
```

#### Declaring Variants — FeatureVariantDefinition

`FeatureVariantDefinition` is the variant-allocation sibling of `FeatureDefinition` — a typed, discoverable declaration of a feature's variants and their relative weights. Like `FeatureDefinition`, it documents intent; it does not itself drive evaluation (the configured allocation in `appsettings.json`/App Configuration remains authoritative):

```csharp
public static class PricingExperimentVariants
{
    public static readonly FeatureVariantDefinition ControlGroup = new("ControlGroup", Weight: 75);
    public static readonly FeatureVariantDefinition DiscountedPrice = new("DiscountedPrice", Weight: 25, Configuration: "10-percent-off");
}
```

#### Context Determinism for Non-String Contexts

`GetVariantAsync<TContext>`'s targeting identity is derived from `context?.ToString()`. A `string` context (a tenant id, a user id) is the most direct and predictable choice. A custom `TContext` works too, provided its `ToString()` override returns the stable identity you want to target on — without an override, every instance of that type collapses to the same targeting bucket (still deterministic, just not usefully distributed):

```csharp
public sealed record TenantContext(string TenantId)
{
    public override string ToString() => TenantId; // required for meaningful per-tenant distribution
}
```

> **Caveat (non-blocking, flagged per this domain's AOT posture):** `Microsoft.FeatureManagement` 4.5.0 does not ship a full AOT-trimming manifest for *any* of its API surface (see the `SharedKernel.FeatureManagement.csproj` comment) — the variant/allocation API (`IVariantFeatureManager`) inherits this same pre-existing status, not a worse one. No new AOT gap was introduced by this phase; verify on each `Microsoft.FeatureManagement` upgrade as already documented for the boolean path.

---

## SharedKernel.Core — Guard Clauses

`SharedKernel.Core` provides a two-path guard system for validating input and enforcing invariants. Everything lives in one namespace:

```csharp
using SharedKernel.Guards;
```

| Path | Entry point | Returns | Use when |
|------|-------------|---------|---------|
| Functional | `Guard.Against.*` | `Error?`: `null` on pass, an `Error` on violation | Factory methods, handlers returning `Result<T>` |
| Imperative | `Guard.Throw.*` | `void`; throws `DomainException` on violation | Constructors, domain invariants |

Both paths have the same guards with the same names and parameters (a reflection test keeps them in step). Every guard captures the parameter name from the argument expression, as `ArgumentNullException.ThrowIfNull` does, so `nameof(...)` is optional.

### Functional Path — `Guard.Against.*`

A functional guard never throws. A `null` input is reported as a violation, so guards are safe on unvalidated input.

```csharp
// First failure wins, then build the value only if everything passed
Result<Money> money = (Guard.Against.NegativeOrZero(amount)
                       ?? Guard.Against.NullOrWhiteSpace(currency)
                       ?? Guard.Against.LongerThan(currency, maxLength: 3))
    .ToResult(() => new Money(amount, currency!));

// Every failure collected
ValidationResult validation = Guard.Collect(
    Guard.Against.NullOrWhiteSpace(request.Name),
    Guard.Against.Email(request.Email),
    Guard.Against.InvalidEnumValue(request.Channel),
    Guard.Against.NotUtc(request.ScheduledAt));

// Custom rule
Error? tooMany = Guard.Against.False(order.Lines.Count > 50,
    Error.BusinessRule("order.max_lines", "An order can have at most 50 lines."));
```

### Imperative Path — `Guard.Throw.*`

```csharp
public sealed class Payment
{
    public Payment(decimal amount, string? currency)
    {
        Guard.Throw.NegativeOrZero(amount);
        Guard.Throw.NullOrWhiteSpace(currency);
        Guard.Throw.LongerThan(currency, maxLength: 3);

        Amount = amount;
        Currency = currency;   // no nullable warning: the guards carry [NotNull]
    }

    public decimal Amount { get; }
    public string Currency { get; }
}
```

### Guard Category Reference

| Category | Guard | Violation | `Error.Code` |
|----------|-------|-----------|--------------|
| Null | `Null<T>` (reference and `Nullable<T>`) | `null` | `validation.required` |
| Null/empty | `NullOrEmpty` | `null` or `""` | `validation.required` |
| Null/whitespace | `NullOrWhiteSpace` | `null`, `""`, or only whitespace | `validation.required` |
| Min length | `ShorterThan(value, minLength)` | `Length < minLength` | `validation.min_length` |
| Max length | `LongerThan(value, maxLength)` | `Length > maxLength` | `validation.max_length` |
| Non-negative | `Negative<T>` (any numeric type) | `value < 0` or `NaN` | `validation.out_of_range` |
| Positive | `NegativeOrZero<T>` (any numeric type) | `value <= 0` or `NaN` | `validation.out_of_range` |
| Range | `OutOfRange<T>(value, min, max)` | outside `[min, max]` | `validation.out_of_range` |
| Lower bound | `LessThan<T>(value, min)` | `value < min` | `validation.out_of_range` |
| Upper bound | `GreaterThan<T>(value, max)` | `value > max` | `validation.out_of_range` |
| Default value | `Default<T>` | equals `default(T)` | `validation.required` |
| Empty GUID | `InvalidGuid` | `Guid.Empty` | `validation.required` |
| Enum | `InvalidEnumValue<TEnum>` | not a named member | `validation.out_of_range` |
| UTC | `NotUtc` (`DateTimeOffset`, `DateTime`) | non-zero offset / kind not `Utc` | `validation.invalid_format` |
| Regex format | `InvalidFormat(value, pattern)` | pattern not matched | `validation.invalid_format` |
| Email | `Email` | not `local@domain.tld` | `validation.invalid_format` |
| Empty collection | `Empty<T>` | no elements | `validation.required` |
| Max elements | `MaxCount<T>(source, max)` | more than `max` | `validation.out_of_range` |
| Min elements | `MinCount<T>(source, min)` | fewer than `min` | `validation.out_of_range` |
| Predicate | `True(condition, error)` / `False(condition, error)` | `false` / `true` | the supplied error |
| SmartEnum | `InvalidSmartEnum<TEnum, TValue>(value)` | not a known member | `validation.out_of_range` |

A `null` input to a length, format, range, or collection guard returns `validation.required`.

**Messages.** Messages are formatted with the invariant culture, so they are identical on every server; translate by `Error.Code` (see `SharedKernel.Localization`). `InvalidFormat` leaves the pattern out of its message, because the message can reach an HTTP response.

**Format guard cache (P-522/WO-083).** Each distinct `InvalidFormat` pattern is compiled once and cached, with a 250 ms match timeout. The cache holds at most 256 patterns and evicts the oldest first, so a pattern derived from configuration or user input cannot grow it without bound. `Email` uses a source-generated regular expression.

**Collections.** `Empty`, `MinCount`, and `MaxCount` use a collection's count when it has one; otherwise they stop reading as soon as the answer is known.

**Custom guards.** Write an extension method on `IGuardClause` that returns `Error?` and never throws. Analyzer `SK0006` flags a `throw` in guard code; `SharedKernel.Validation` adds its IBAN, BIC, PAN, and national-ID guards this way.

---

## SharedKernel.Cryptography — Encryption, Signing, Hashing and One-Time Passwords

Full reference, pitfalls and AI quick reference: [`SharedKernel.Cryptography/README.md`](SharedKernel.Cryptography/README.md).
Companions: [`SharedKernel.Cryptography.Argon2`](SharedKernel.Cryptography.Argon2/README.md) and
[`SharedKernel.Cryptography.KeyVault.Azure`](SharedKernel.Cryptography.KeyVault.Azure/README.md).

```csharp
services.AddSharedKernelCryptography(configuration)   // key-free: IOneWayHasher, ISecureRandomGenerator, IContentHasher,
                                                      // IHmacSigner, IHotpGenerator, ITotpGenerator, IRecoveryCodeGenerator
    .AddSymmetricEncryption()                         // ISymmetricEncryptionService          ← IEncryptionKeyProvider
    .AddSynchronousSymmetricEncryption()              // ISynchronousSymmetricEncryptionService ← ISynchronousEncryptionKeyProvider
    .AddEnvelopeEncryption()                          // IEnvelopeEncryptionService           ← IEnvelopeEncryptionProvider
    .AddAsymmetricSigning()                           // IAsymmetricSignatureService          ← ISigningKeyProvider
    .AddTotpVerification();                           // ITotpVerifier                        ← ITotpReplayGuard
```

| Need | Type |
| --- | --- |
| Encrypt data you decrypt later | `ISymmetricEncryptionService` / `ISynchronousSymmetricEncryptionService` (AES-256-GCM, required associated data) |
| Keys in memory / from a KMS | `StaticEncryptionKeyProvider` / `IEncryptionKeyProvider` + `CachedEncryptionKeyProvider` |
| Move old ciphertext to the current key | `IsEncryptedWithCurrentKeyAsync` + `ReEncryptAsync` |
| One data key per file | `IEnvelopeEncryptionService` |
| Per-tenant or per-purpose keys | `SubkeyDerivation`, `provider.ForPurpose(...)` |
| Digital signatures | `SigningKey` (PS/RS/ES 256–512) + `ISigningKeyProvider` + `IAsymmetricSignatureService` |
| Shared-secret MAC | `IHmacSigner` (keys ≥ 32 bytes) |
| Store passwords and API keys | `IOneWayHasher` (PHC; PBKDF2 default, Argon2id optional; pepper; rehash on verify) |
| Fingerprint non-secret content | `IContentHasher` |
| Compare secrets | `FixedTimeComparison` |
| Tokens, salts, random codes | `ISecureRandomGenerator` |
| Authenticator-app second factor | `TotpSecret`, `TotpProvisioningUri`, `ITotpVerifier`, `IRecoveryCodeGenerator` |

## SharedKernel.Compression — Generic Payload Compression

`SharedKernel.Compression` provides generic compress/decompress of an arbitrary byte payload or stream via `IPayloadCompressor`. It is the direct sibling of `SharedKernel.Cryptography`'s `ISymmetricEncryptionService` — same shape, same zero-third-party-NuGet-dependency constraint (pure BCL `System.IO.Compression`), orthogonal concern. There is no `.Abstractions`/`.{Provider}` package split — a single package with a keyed-DI algorithm choice, mirroring `SharedKernel.Cryptography`'s RSA/ECDSA keyed-singleton precedent.

**Ordering rule: always compress, then encrypt — never the reverse.** Compressing already-encrypted/high-entropy ciphertext wastes CPU for no size benefit, since ciphertext has no redundancy left to compress. Never call `Compress` on a payload that has already passed through `ISymmetricEncryptionService.Encrypt`, and never call `Compress` a second time on an already-compressed payload — double-compression wastes CPU and typically *increases* output size.

### Registration

```csharp
// Program.cs
builder.Services.AddSharedKernelCompression(builder.Configuration);
```

Configuration (`SharedKernel:Compression`, bound from the section `CompressionOptions` declares through `ISectionBoundOptions`, so no call site names the path):

```json
{
  "SharedKernel": {
    "Compression": {
      "Level": "Optimal",
      "MaxDecompressedSize": 67108864
    }
  }
}
```

Both values are validated at startup — an undefined `Level` or a non-positive `MaxDecompressedSize` throws from `IHost.StartAsync()`.

`AddSharedKernelCompression` registers five `TryAdd` singletons:

| Key | Algorithm | Framing | Use for |
|---|---|---|---|
| *(unkeyed)* | Brotli | Framed | Anything this platform writes and reads back |
| `"Brotli"` | Brotli | Framed | The same, addressable by name |
| `"GZip"` | gzip | Framed | gzip inside this platform |
| `"Brotli.Raw"` | Brotli | Raw | External interop |
| `"GZip.Raw"` | gzip | Raw | External interop — an ordinary `.gz` body |

Brotli is the unkeyed default because it compresses this platform's payloads better: measured on 283 KB of JSON, 15.6 KB with Brotli against 35.0 KB with gzip. gzip is never the unkeyed default, mirroring `EcdsaSignatureService`'s keyed-only registration in `SharedKernel.Cryptography`.

### Brotli (default) — byte[], span and stream usage

```csharp
public sealed class QueuePublishExample(IPayloadCompressor compressor)
{
    // byte[] overload — small in-memory payloads.
    public byte[] PrepareForQueue(byte[] jsonPayload) => compressor.Compress(jsonPayload);

    public Result<byte[]> RestoreFromQueue(byte[] received) => compressor.Decompress(received);

    // Stream overload — large payloads, never materializes the full content in memory.
    public async ValueTask CompressUploadAsync(Stream sourceFile, Stream destination, CancellationToken ct) =>
        await compressor.CompressAsync(sourceFile, destination, ct);

    // Allocation-sensitive path — no intermediate buffer and no result array.
    public void PrepareInto(ReadOnlySpan<byte> payload, IBufferWriter<byte> destination) =>
        compressor.Compress(payload, destination);
}
```

`Compress(byte[])` grows an internal buffer and then copies out of it, costing roughly twice the payload; prefer the span and `IBufferWriter<byte>` overloads in a hot loop.

Handling a failed payload via the railway pattern:

```csharp
Result<byte[]> decompressed = compressor.Decompress(received);

decompressed.Match(
    onSuccess: bytes => ProcessPayload(bytes),
    onFailure: error => logger.LogWarning(
        "Decompression failed: {Code} — {Message}", error.Code, error.Message));
```

Every failure is an `ErrorType.Validation` error, because the fault is in the supplied payload rather than in the service — it maps to HTTP 400 at the boundary, never a 500. The codes in `CompressionErrorCodes` are `DecompressionFailed`, `TruncatedPayload`, `PayloadTooLarge`, `MalformedPayload` and `AlgorithmMismatch`.

### Truncation detection, and why a payload carries a frame

**Neither `BrotliStream` nor `GZipStream` detects a truncated payload.** Both treat the end of the input as the end of the data, so a payload cut short in transit or in storage decompresses *without error* into a valid prefix of the original — and nothing downstream can tell it from complete data. Measured on a 283 KB payload, before this package framed its output:

| Compressed bytes kept | Result | Data returned |
|---|---|---|
| 25% | `Success` | 63,898 bytes — a valid prefix |
| 50% | `Success` | 127,863 bytes — a valid prefix |
| 99% | `Success` | 278,932 of 282,775 bytes |

A framed payload therefore carries a 13-byte header — magic marker, format version, algorithm, uncompressed length — and decompression verifies the length it produced against the recorded one, failing with `CompressionErrorCodes.TruncatedPayload`. The frame also records **which algorithm wrote the payload**, so gzip bytes handed to the Brotli compressor fail with `AlgorithmMismatch` rather than possibly decoding to garbage (Brotli has no magic number of its own). Reading a raw payload with a framed compressor fails with `MalformedPayload`.

The frame is **not** a checksum and not authentication: it detects truncation and a wrong-codec read, not deliberate tampering by someone who can rewrite the header. Where a payload must be tamper-evident, encrypt it after compressing — AES-GCM's authentication tag then covers the compressed bytes.

Two limits worth knowing:

- **Raw mode cannot detect truncation**, by definition — nothing in a bare Brotli or gzip stream records the original length. That is the cost of interop, and it is why `Framed` is the default. Raw gzip also decodes a second gzip payload joined onto the first (gzip allows concatenated members, so `cat a.gz b.gz` is a valid file); a framed payload rejects the same thing because its output no longer matches the recorded length. Any other bytes after a complete payload are ignored in every mode.
- **`Compress(Stream, Stream)` needs the length before it writes the header.** It takes it from the input stream when that is seekable, otherwise patches it in afterwards when the *output* is seekable. When neither is — a network stream straight to a network stream — the payload records no length and reading it back cannot detect truncation. `MemoryStream` and `FileStream` are both seekable, so this affects only genuinely streamed pipelines.

### Untrusted input and decompression bombs

Decompression is bounded by `MaxDecompressedSize` (default 64 MiB) and returns `CompressionErrorCodes.PayloadTooLarge` rather than allocating past it. This is a denial-of-service control, not a tuning knob: **102 bytes of Brotli expand to 64 MiB of zeroes**, and crafted input goes orders of magnitude further. The limit is enforced from the bytes actually produced, never from the frame's recorded length, so a payload that understates its own size is still stopped. On a `Stream` destination, up to the limit plus one copy buffer may already have been written when the failure is reported — discard the destination's contents unless the result is successful.

Compression has no equivalent bound, because it is CPU-bound work on data you supply. Do not compress an unbounded caller-supplied payload on a request path without limiting its size first.

### gzip — explicit keyed resolution

Use gzip only when interoperating with a system that specifically requires that format, and resolve the **raw** key when you do: framed output carries the 13-byte platform header, so it is not a `.gz` body any standard tool can open, which defeats the only reason to choose gzip here.

```csharp
public sealed class LegacyInteropExample(
    [FromKeyedServices(CompressionServiceCollectionExtensions.RawGZipPayloadCompressorKey)]
        IPayloadCompressor gzipCompressor)
{
    public byte[] CompressForLegacySystem(byte[] payload) => gzipCompressor.Compress(payload);
}

// Resolving explicitly from IServiceProvider:
IPayloadCompressor brotli = provider.GetRequiredService<IPayloadCompressor>(); // framed Brotli
IPayloadCompressor gzipForExternal = provider.GetRequiredKeyedService<IPayloadCompressor>(
    CompressionServiceCollectionExtensions.RawGZipPayloadCompressorKey);
```

gzip also inflates very small payloads noticeably — 1 byte becomes 21, against Brotli's 5.

### Compression level

`Level` applies to both algorithms. **`SmallestSize` is far more expensive on Brotli than the name suggests**, because it selects Brotli quality 11. Measured on 8 MiB of repetitive data:

| Level | Output | Time |
|---|---|---|
| `Fastest` | 3,757 bytes | 2 ms |
| `Optimal` | 1,061 bytes | 10 ms |
| `SmallestSize` | 1,047 bytes | 225 ms |

22× the cost of `Optimal` for 1.3% less output. Do not set it on a request-path payload without measuring your own data.

---

## SharedKernel.Validation — Culture-Independent Format Validators

`SharedKernel.Validation` provides culture-independent financial and identity format validators: IBAN (per-country length table + ISO 13616 mod-97 check digit), BIC/SWIFT, payment-card PAN (Luhn + card-network detection), ISO 4217 currency codes, ISO 3166-1 country codes, E.164 phone numbers, a baseline VAT/tax-identifier format check, and a pluggable per-country national-identity-number registry (`TckNationalIdValidator` — Turkey's TCKN — ships as the built-in default). Zero third-party NuGet dependencies. References `SharedKernel.Primitives` (for `Result`/`Error`) and `SharedKernel.Core` (extending `Guard.Against` with new members via extension methods — `SharedKernel.Core`'s Guard surface itself is never modified; re-pointed from the retired `SharedKernel.Guards` package by P-506/WO-082).

Every validator is dual-mode: a standalone `IsValid`/`Validate` call, and a `Guard.Against.*` extension. Both paths share the same underlying algorithm and the same `ValidationErrorCodes` constants — a failure surfaces an identical code whichever path reached it.

```csharp
using SharedKernel.Guards; // Guard.Against entry point
using SharedKernel.Validation.Validators;
using SharedKernel.Validation.Guards; // brings the Guard.Against.Invalid* extensions into scope

// Standalone Result call
Result validationResult = IbanValidator.Validate(request.Iban);
if (validationResult.IsFailure)
{
    return Result<Account>.Failure(validationResult.Error); // e.g. ValidationErrorCodes.Iban.InvalidCheckDigit
}

// Guard.Against.* extension — same validator, same error codes, chains with the rest of Guard.Against
Error? error = Guard.Against.InvalidIban(request.Iban);
if (error is not null)
{
    return Result<Account>.Failure(error);
}
```

**`Guard.Throw.*` parity is intentionally out of scope for this package.** The `Guard.Throw` nested class (now living in `SharedKernel.Core`, under the unchanged `SharedKernel.Guards` namespace, since P-505/WO-082) is a hand-enumerated static class hardcoded inside that class — a package outside `SharedKernel.Core` cannot add a member to it without modifying `SharedKernel.Core` itself, which is out of `SharedKernel.Validation`'s jurisdiction. Only the functional `Guard.Against.*` path is provided here.

### National ID registry

```csharp
// Register (Program.cs) — pre-seeded with TckNationalIdValidator ("TR")
builder.Services.AddSharedKernelValidation()
    .AddNationalIdValidator<MySecondCountryNationalIdValidator>();

public sealed class KycService(INationalIdValidatorRegistry registry)
{
    public Error? ValidateNationalId(string idNumber, string countryCode) =>
        Guard.Against.InvalidNationalId(idNumber, countryCode, registry);
}
```

`INationalIdValidatorRegistry.TryGetValidator` never throws for an unregistered country code — it returns `false`.

### `ValidationErrorCodes` is package-local

Format-validator error codes (`ValidationErrorCodes.Iban.*`, `.Pan.*`, `.NationalId.*`, etc.) live in a package-local static class inside `SharedKernel.Validation` itself — they are **never** added as a new nested category under `SharedKernel.Primitives.ErrorCodes`. `ErrorCodes`'s own documented rule already permits this ("consuming packages may add local constants without forking the SharedKernel"), and a whole country-algorithm error-code catalog does not belong bloating the platform's most-depended-upon primitives package.

### A note on `VatValidator`

`VatValidator` is a **baseline, non-exhaustive** cross-jurisdiction format check only — it confirms a value looks like a 2-letter country prefix followed by 2-12 alphanumeric characters, and performs **no** per-country checksum validation. VAT/tax-identifier formats vary enormously by country. A passing result is not proof of a real, registered VAT identifier.

---

## SharedKernel.Validation.FluentValidation — FluentValidation Rule Adapter

`SharedKernel.Validation.FluentValidation` is a thin `IRuleBuilder<T, string>` extension-method adapter over every `SharedKernel.Validation` static format validator: `.MustBeValidIban()`, `.MustBeValidBic()`, `.MustBeValidPan()`, `.MustBeValidCurrencyCode()`, `.MustBeValidCountryCode()`, `.MustBeValidPhoneNumber()`, `.MustBeValidVatNumber()`, and `.MustBeValidNationalId(countryCodeSelector, registry)`. It is a **separate package from `SharedKernel.Validation` on purpose** — a service that only wants the standalone `Result`/`Guard` surface (a Temporal activity, a lightweight worker with no MediatR pipeline) never pulls FluentValidation in transitively.

Each rule delegates to the matching validator's `Validate(string?)` and, on failure, attaches a single `FluentValidation.Results.ValidationFailure` whose `ErrorCode` is the *exact* `ValidationErrorCodes` constant the validator produced — never a single rule-fixed code. This matters for validators like `IbanValidator`, which can fail with three distinct codes (`InvalidFormat` / `InvalidCheckDigit` / `InvalidLength`) depending on what is wrong with the value:

```csharp
using FluentValidation;
using SharedKernel.Validation.FluentValidation;
using SharedKernel.Validation.NationalId;

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator(INationalIdValidatorRegistry nationalIdRegistry)
    {
        RuleFor(x => x.Iban).MustBeValidIban();
        RuleFor(x => x.Bic).MustBeValidBic();
        RuleFor(x => x.CurrencyCode).MustBeValidCurrencyCode();
        RuleFor(x => x.PayerNationalId)
            .MustBeValidNationalId(x => x.PayerCountryCode, nationalIdRegistry);
    }
}
```

Because every rule is built on FluentValidation's `Custom(...)` extension (it needs to inspect *which* code the underlying validator returned, not just pass/fail), chaining `.WithMessage(...)` or `.WithErrorCode(...)` afterward has **no effect** — the message and error code always come from the `SharedKernel.Validation` validator. `.When(...)`/`.Unless(...)` and other rule-level conditions still work normally.

### Composing with `05.Application.Behaviors`'s `ValidationBehavior`

No extra plumbing is required: `ValidationBehavior<TRequest,TResponse>` already runs every registered `IValidator<TRequest>` and aggregates every `ValidationFailure` it finds, regardless of how each rule was built. A validator using `.MustBeValidIban()` inside an `AbstractValidator<TCommand>` that is already resolved by that pipeline behavior participates automatically.

One nuance worth knowing: as of this writing, `ValidationBehavior` projects each failure via `Error.Validation(failure.PropertyName, failure.ErrorMessage)` — the FluentValidation **property name** becomes the downstream `Error.Code`, not `failure.ErrorCode`. The finer-grained `ValidationErrorCodes` constant this package attaches is still there on the raw `ValidationFailure.ErrorCode` — a consuming service (or a future `ValidationBehavior` revision) that wants it on the outward-facing `Error` instead of the property name reads `failure.ErrorCode` directly.

---

## SharedKernel.DataPrivacy — Classification Taxonomy, Masking, Data-Subject Requests

`SharedKernel.DataPrivacy` ships three independent, composable pieces: a pure-metadata classification taxonomy, deterministic PII masking helpers, and the `IDataSubjectRequestHandler` GDPR/KVKK export/erasure contract. It depends on `SharedKernel.Primitives` only (for `Result<T>`/`Error` on the request-handler contract) — the same single-package, zero-third-party-dependency reasoning as `SharedKernel.Cryptography`/`.Compression`.

### Classification attributes — metadata only, never reflected over at runtime

```csharp
using SharedKernel.DataPrivacy.Classification;

public sealed class CustomerProfile
{
    [DataClassification(DataClassification.Public)]
    public string DisplayName { get; init; } = string.Empty;

    [DataClassification(DataClassification.Restricted)]
    [SensitiveDataCategory(SensitiveDataCategory.Pii)]
    public string NationalId { get; init; } = string.Empty;
}
```

`DataClassificationAttribute`/`SensitiveDataCategoryAttribute` are **never read via reflection in production code** — their sole sanctioned consumers are a compile-time `00.Governance` analyzer and human documentation/code review. This is a hard design constraint, not a style preference: the platform already bans reflection-based property walks for structured logging, and a classification mechanism that itself needed runtime reflection to be useful would contradict the rule it exists to support. Usable on any type in any layer, including `03.Domain`/`04.Contracts`.

### PiiMasking — deterministic masking helpers

```csharp
using SharedKernel.DataPrivacy.Masking;

PiiMasking.Email("j.doe@example.com");   // "j***@example.com"
PiiMasking.Phone("+1 (555) 123-4567");   // "+* (***) ***-4567" — separators preserved, only digits masked
PiiMasking.Pan("4111-1111-1111-1111");   // "****-****-****-1111" — always exactly the last 4 digits
PiiMasking.Suppress("12345678901");      // "[REDACTED]" — the fixed sentinel, regardless of input
```

Every member is null/empty-safe and never throws — `null`/`""`/whitespace-only input returns `string.Empty` for `Email`/`Phone`/`Pan`, while `Suppress` returns its fixed sentinel for every input, including `null`. No member uses reflection.

### IDataSubjectRequestHandler — implemented by each service against its own data

```csharp
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

public sealed class CustomerDataSubjectRequestHandler(ICustomerRepository customers, IClock clock)
    : IDataSubjectRequestHandler
{
    public async Task<Result<DataSubjectExportBundle>> ExportDataAsync(string subjectId, CancellationToken ct = default)
    {
        Customer? customer = await customers.FindByIdAsync(subjectId, ct);
        if (customer is null)
        {
            return Error.NotFound("customer.not_found", $"No customer found for subject '{subjectId}'.");
        }

        var data = new Dictionary<string, object?>
        {
            ["email"] = customer.Email,
            ["displayName"] = customer.DisplayName,
        };

        return new DataSubjectExportBundle(subjectId, clock.UtcNow, data);
    }

    public async Task<Result<DataSubjectErasureReceipt>> RequestErasureAsync(string subjectId, CancellationToken ct = default)
    {
        int affected = await customers.AnonymizeBySubjectIdAsync(subjectId, ct);
        return new DataSubjectErasureReceipt(subjectId, clock.UtcNow, affected);
    }
}
```

This package ships no default implementation — there is no honest generic way to export or erase "everything about a subject" without knowing what a given service actually stores — and **no cross-service erasure orchestrator**. Coordinating a single data-subject request across every service that might hold data about that subject is explicitly out of scope; it is a plausible future composition (a `19.Scheduling` job or a `17.Workflows` durable workflow) built on top of this contract once real per-service handlers exist.

### Composing with `06.Persistence`'s audit trail

`06.Persistence`'s append-only audit trail (`IAuditTrailWriter`, P-456/WO-071) persists opaque, caller-serialized before/after snapshots with no knowledge of which fields are sensitive. Mask a classified field with `PiiMasking.*` before handing it to that writer:

```csharp
var auditSnapshot = new
{
    Email = PiiMasking.Email(customer.Email),
    CardNumber = PiiMasking.Pan(customer.CardNumber),
};

await auditTrailWriter.WriteAsync(entry with { After = auditSnapshot }, ct);
```

This is documentation guidance only — neither package takes a dependency on the other.

See [`SharedKernel.DataPrivacy`'s own README](SharedKernel.DataPrivacy/README.md) for the full usage guide.

---

## SharedKernel.Localization — Translated Messages With Typed Arguments

`SharedKernel.Localization` translates error messages and other user-facing text. Each message is defined once with a
stable code, a default text with named placeholders, and typed arguments, so the compiler checks every call site:

```csharp
public static readonly LocalizedMessage<Guid> OrderNotFound = LocalizedMessage.Define<Guid>(
    "order.not_found", "Order {orderId} was not found.", "orderId");

return OrderNotFound.ToError(ErrorType.NotFound, orderId);
```

The error's `Message` is the default text filled in (`"Order 3f2a… was not found."`), and `Error.MessageArguments`
carries `orderId`. `SharedKernel.Presentation.WebApi` looks the code up in the registered `ILocalizationCatalog` and
fills the translation with the same values, so a Turkish caller gets `"3f2a… numaralı sipariş bulunamadı."` in the
ProblemDetails `detail`.

Translations come from JSON files, one per culture (`tr.json`, `de-DE.json`), embedded JSON resources, code, or
`.resx` files. `AddLocalizationCatalog(catalog => catalog.AddJsonDirectory(path))` builds an immutable catalog during
registration and validates every file and template, so a broken translation fails startup. Placeholders are named
(`{orderId}`, `{total:N2}`) and formatted with the caller's culture; positional `{0}` is rejected. Lookups fall back
`tr-TR` → `tr` → invariant, and anything missing or unfillable falls back to the original message, never a blank or a
raw `{placeholder}`. One catalog per application: a second registration throws.

Configure ASP.NET Core's request localization with the catalog's `Cultures` as supported UI cultures, or requests
stay in the default culture. See [`SharedKernel.Localization`'s README](SharedKernel.Localization/README.md) for the
full guide.

---

## Dependency Graph

```
SharedKernel.Primitives              (no dependencies)
       |
       +──► SharedKernel.Core           (BCL extensions, railway extensions, exceptions, Guard.Against / Guard.Throw
       |       |                         two-path guard system — merged from SharedKernel.Guards, P-505/WO-082)
       |       |
       |       +──► SharedKernel.Validation  (IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT + national-ID registry)
       |               |
       |               +──► SharedKernel.Validation.FluentValidation  (IRuleBuilder<T,string> adapter; also pulls in the third-party FluentValidation package)
       |
       +──► SharedKernel.Configuration  (Options pattern + startup validation)
       |       |
       |       +──► SharedKernel.Cryptography  (one-way hashing, AES-GCM, RSA/ECDSA, HMAC, secure random)
       |       |       |
       |       |       +──► SharedKernel.Cryptography.KeyVault.Azure  (Azure Key Vault Keys provider; also pulls in the third-party Azure.Security.KeyVault.Keys + Azure.Security.KeyVault.Secrets + Azure.Identity packages)
       |       |       |
       |       |       +──► SharedKernel.Cryptography.Argon2  (keyed Argon2id IOneWayHasher; also pulls in the third-party Konscious.Security.Cryptography.Argon2 package)
       |       |
       |       +──► SharedKernel.Compression   (IPayloadCompressor: Brotli default, GZip keyed alternate)
       |
       +──► SharedKernel.FeatureManagement  (IFeatureManager + Microsoft.FeatureManagement adapter)
       |
       +──► SharedKernel.DataPrivacy  (DataClassification/SensitiveDataCategory attributes, PiiMasking, IDataSubjectRequestHandler)
       |
       +──► SharedKernel.Localization  (LocalizedMessage, ILocalizationCatalog, JSON catalogs; also pulls in the first-party Microsoft.Extensions.Localization.Abstractions package)
```

All twelve packages can be referenced independently. Downstream packages in the SharedKernel ecosystem reference `SharedKernel.Primitives` as the minimum baseline and add the others as needed. `SharedKernel.Validation.FluentValidation`, `SharedKernel.Cryptography.KeyVault.Azure`, `SharedKernel.Cryptography.Argon2`, and `SharedKernel.Localization` are the four exceptions to "zero third-party NuGet dependencies" in this domain: `.FluentValidation` depends on `SharedKernel.Validation` plus the third-party `FluentValidation` package, deliberately kept out of `SharedKernel.Validation` itself so a FluentValidation-free consumer never pulls it in transitively; `.KeyVault.Azure` depends on `SharedKernel.Cryptography` plus the third-party `Azure.Security.KeyVault.Keys`/`Azure.Identity` packages, deliberately kept out of `SharedKernel.Cryptography` itself for the identical reason; `.Argon2` depends on `SharedKernel.Cryptography` plus the third-party `Konscious.Security.Cryptography.Argon2` package (a pure-managed implementation, no native/P-Invoke binding), deliberately kept out of `SharedKernel.Cryptography` itself for the identical reason again; `SharedKernel.Localization` depends on the first-party (not third-party) `Microsoft.Extensions.Localization.Abstractions` package — a deliberate exception to the zero-dependency default because it is the platform's own vendor's abstraction, not an external one, and the alternative (a bespoke resx pipeline) was explicitly rejected.
