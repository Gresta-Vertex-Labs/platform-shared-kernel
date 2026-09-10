# 01.Core

Foundational building blocks for the Platform.SharedKernel ecosystem. Twelve independently publishable NuGet packages (thirteen minus `SharedKernel.Guards`, merged into `SharedKernel.Core` — P-505/WO-082, shipped, breaking as a package retirement only; the `SharedKernel.Guards`/`SharedKernel.Guards.Clauses`/`SharedKernel.Guards.Descriptions` C# namespaces are completely unchanged) — every one but `SharedKernel.Validation.FluentValidation`, `SharedKernel.Cryptography.KeyVault.Azure`, `SharedKernel.Cryptography.Argon2`, and `SharedKernel.Localization` (a first-party Microsoft dependency, not a third-party one) has zero third-party NuGet dependencies.

| Package | Purpose |
|---------|---------|
| `SharedKernel.Primitives` | `Result<T>`, `Error`, `IClock`, `IIdGenerator`, `SmartEnum`, `ValidationResult` |
| `SharedKernel.Core` | Base exceptions, railway extensions, BCL helpers, and the two-path guard system: `Guard.Against.*` (functional) + `Guard.Throw.*` (imperative) — merged from the former `SharedKernel.Guards` package (P-505/WO-082) |
| `SharedKernel.Configuration` | `AddValidatedOptions` startup-validation pattern |
| `SharedKernel.FeatureManagement` | `IFeatureManager` abstraction over Microsoft.FeatureManagement |
| `SharedKernel.Cryptography` | Password hashing, AES-256-GCM symmetric encryption, RSA/ECDSA + HMAC signing, secure random/token generation |
| `SharedKernel.Compression` | Generic payload compression (`IPayloadCompressor`): Brotli default, GZip keyed alternate |
| `SharedKernel.Validation` | Culture-independent IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT validators + pluggable national-ID registry |
| `SharedKernel.Validation.FluentValidation` | `IRuleBuilder<T,string>` adapter over `SharedKernel.Validation` (a third-party dependency — `FluentValidation`) |
| `SharedKernel.Cryptography.KeyVault.Azure` | Azure Key Vault Keys implementation of `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider` (with a durable, cross-replica-shared version registry + explicit `MintNewVersionAsync` rotation, P-496/WO-081) and (P-494/WO-081) remote-signing `IAsymmetricKeyProvider` (a third-party dependency — `Azure.Security.KeyVault.Keys` + `Azure.Security.KeyVault.Secrets` + `Azure.Identity`) |
| `SharedKernel.Cryptography.Argon2` | Argon2id implementation of `IOneWayHasher`, registered as a keyed alternative alongside the unkeyed PBKDF2 default (a third-party dependency — `Konscious.Security.Cryptography.Argon2`) |
| `SharedKernel.DataPrivacy` | `DataClassificationAttribute`/`SensitiveDataCategoryAttribute` pure-metadata markers, `PiiMasking.*` deterministic masking helpers, `IDataSubjectRequestHandler` export/erasure contract |
| `SharedKernel.Localization` | `ILocalizationCatalog`, keyed on the same `code` string every `Error` factory requires (a first-party dependency — `Microsoft.Extensions.Localization.Abstractions`) |

All packages target `net10.0` and are AOT-compatible.

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
2. **`SharedKernel.Cryptography.KeyVault.Azure`'s `AzureKeyVaultCryptographyOptionsValidator`**
   registers itself against `IValidateOptions<AzureKeyVaultCryptographyOptions>` the same way —
   `Microsoft.Extensions.Options`' own validation pipeline runs *every* registered
   `IValidateOptions<T>` for a type, not just one, and the preceding
   `AddValidatedOptions<AzureKeyVaultCryptographyOptions>(section)` call already registers the
   BCL's own `DataAnnotationValidateOptions<T>` against that identical service type via
   `ValidateDataAnnotations()`. A plain `TryAddSingleton` here would see the service type already
   claimed and silently never register this validator's cross-field checks — a real regression
   caught during SK.01.P518 implementation (two host-startup tests stopped throwing until fixed).

**One documented, intentional behavior inversion — not a regression, an accepted side effect of
domain-wide standardization:** `SharedKernel.Localization`'s `AddInMemoryLocalizationCatalog()` and
`AddStringLocalizerCatalog<TResource>()` are mutually exclusive — both register the single-winner
`ILocalizationCatalog` via `TryAddSingleton`. Before this standardization, calling both on the same
`IServiceCollection` left whichever call ran *last* as the resolved implementation (plain
`AddSingleton`'s natural "last wins" behavior). Now it is whichever call runs *first* — the
standard `TryAdd` idiom. Call exactly one of the two per service, in whichever order you want to
win.

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

### Result\<T\> Railway Pattern

Railway-oriented programming (ROP) chains `Result<T>`-returning operations without nested `if` blocks. When any step fails, all subsequent steps are skipped and the original error propagates to the terminal `Match`.

```
Input ──► [Step 1] ──success──► [Step 2] ──success──► [Step 3] ──success──► Output
                    failure                  failure                  failure
                       │                       │                        │
                       └───────────────────────┴────────────────────────► Error propagates
```

#### Map — Transform the Success Value

`Map` projects the success value to a different type. Failures pass through unchanged.

```csharp
Result<string> name = GetUserName(userId);

Result<int> nameLength = name.Map(n => n.Length);
// Success:  nameLength.Value == n.Length
// Failure:  nameLength.Error == original GetUserName error (unchanged)
```

#### MapError — Transform the Error

`MapError` enriches or replaces an error on the failure path. Successes pass through unchanged.

```csharp
Result<Order> order = repository.FindOrder(orderId)
    .MapError(e => Error.NotFound(
        "order.not_found",
        $"Order {orderId} was not found. (inner: {e.Code})"));
```

#### Bind — Chain Operations That Can Fail

`Bind` chains a function that itself returns a `Result<TOut>`. The chain short-circuits on the first failure.

```csharp
Result<Order> result = repository.FindOrder(orderId)   // Result<Order>
    .Bind(order => inventory.Reserve(order))            // Result<Order>
    .Bind(order => payment.Charge(order));              // Result<Order>

// If FindOrder fails, Reserve and Charge are never called.
// The first failure propagates unchanged to the end of the chain.
```

#### Match — Fold to a Single Value

`Match` terminates the chain by folding both branches into one value. This is the primary bridge between the domain (railway world) and the presentation layer.

```csharp
IResult httpResult = result.Match(
    onSuccess: order => Results.Ok(order),
    onFailure: error => error.Type switch
    {
        ErrorType.NotFound   => Results.NotFound(new { error.Code, error.Message }),
        ErrorType.Conflict   => Results.Conflict(new { error.Code, error.Message }),
        ErrorType.Validation => Results.BadRequest(new { error.Code, error.Message }),
        _                    => Results.Problem(error.Message)
    });
```

#### Tap — Side Effects on Success

`Tap` runs a side-effecting action on success (logging, publishing events) and returns the original result unchanged.

```csharp
Result<Order> result = repository.FindOrder(orderId)
    .Tap(order => logger.LogInformation("Order {Id} loaded.", order.Id))
    .Bind(order => inventory.Reserve(order));
```

#### Void Match on non-generic Result

For `Result` (non-generic, void operations), `Match` accepts two `Action` delegates:

```csharp
Result commandResult = commandHandler.Handle(command, ct);

commandResult.Match(
    onSuccess: ()    => logger.LogInformation("Command completed."),
    onFailure: error => logger.LogWarning("Command failed: {Code}", error.Code));
```

#### Full Async Railway Chain Example

```csharp
public async Task<IResult> PlaceOrderAsync(PlaceOrderCommand command, CancellationToken ct)
{
    return await repository.FindCustomerAsync(command.CustomerId, ct)  // Task<Result<Customer>>
        .Bind(customer => ValidateCustomer(customer))                  // Result<Customer>
        .Bind(customer => BuildOrder(command, customer))               // Result<Order>
        .Bind(order    => inventory.ReserveAsync(order, ct))           // Task<Result<Order>>
        .Tap(order     => logger.LogInformation("Order {Id} reserved.", order.Id))
        .Bind(order    => repository.SaveAsync(order, ct))             // Task<Result<Order>>
        .Match(
            onSuccess: order => Results.Created($"/orders/{order.Id}", order),
            onFailure: error => error.Type switch
            {
                ErrorType.NotFound   => Results.NotFound(),
                ErrorType.Conflict   => Results.Conflict(),
                ErrorType.Validation => Results.BadRequest(error.Message),
                _                    => Results.Problem()
            });
}
```

#### Error Propagation Rules

1. The first failure terminates all subsequent `Map`, `Bind`, and `Tap` calls — they become no-ops.
2. `MapError` is the only railway method that executes on the failure path — use it to enrich or reclassify errors.
3. `Match` always executes exactly one branch — it is the safe terminal of every railway chain.
4. Never throw inside a railway lambda. Return `Result<T>.Failure(error)` instead.
5. Use `Error.None` as the sentinel — never `null`. Accessing `result.Error` on a success result throws `InvalidOperationException`.
6. Async overloads (`Task<Result<T>>` extensions) avoid unnecessary `async`/`await` on the outer extension body to minimize state machine allocation.

---

### Result Exception Boundary and Multi-Result Aggregation

Two everyday patterns that otherwise push developers toward hand-rolled code: wrapping a throwing third-party/BCL call as a `Result<T>`, and combining several independent `Result`/`Result<T>` checks into one aggregate outcome.

#### ResultTry — Wrapping a Throwing Call

`ResultTry.Try` / `ResultTry.TryAsync` invoke a delegate and convert any thrown exception into `Result<T>.Failure(...)` instead of letting it propagate. Use this as the sanctioned seam for the one legitimate place Result-oriented code still touches a throwing third-party SDK call or a BCL method with no `Result`-returning equivalent — never hand-roll `try`/`catch`-to-`Result` translation at the call site.

> **Breaking behavior change (P-510/WO-083).** Two narrow behavior changes, no signature changes:
> 1. **Default message content.** The default (no custom `onException`) mapping's `Error.Message` is now always the fixed, safe string `ResultTry.DefaultUnexpectedMessage` — it no longer interpolates the caught exception's raw `"{ExceptionType}: {ExceptionMessage}"`. A caught exception can carry sensitive text (a connection-string fragment, a username, an internal hostname) that must never reach an HTTP response via `Error.ToProblemDetails()`. A caller relying on the old raw-text message must now read exception detail from the ambient trace instead (see below), or supply its own `onException` mapper — which is completely unaffected by this change and still receives the raw exception.
> 2. **`OperationCanceledException` (and its subclass `TaskCanceledException`) now propagates uncaught** from all four members (`Try`, `Try` w/ mapper, `TryAsync`, `TryAsync` w/ mapper) instead of being silently converted into a `Result.Failure`. A genuine cancellation — e.g. an HTTP client disconnect — must never be observed as an ordinary failure result; it must always surface as a thrown exception, exactly like every other `async`/`await` call site on this platform. A caller relying on the old swallow-into-`Result` behavior must now catch `OperationCanceledException` itself around the `ResultTry` call.

```csharp
// Default mapping: Error.Unexpected(ErrorCodes.Unexpected.Default, ResultTry.DefaultUnexpectedMessage)
Result<Customer> result = ResultTry.Try(() => thirdPartySdk.GetCustomer(customerId));

// Custom mapping — translate a known SDK exception into a more specific Error
Result<Customer> result = ResultTry.Try(
    () => thirdPartySdk.GetCustomer(customerId),
    ex => ex is SdkNotFoundException
        ? Error.NotFound("customer.not_found", $"Customer {customerId} was not found.")
        : Error.Unexpected(ErrorCodes.Unexpected.Default, ex.Message));

// TryAsync — the one documented exception to this domain's async-avoidance railway rule:
// catching an exception thrown during an awaited operation requires the try/catch to wrap
// the await itself, which needs a genuine async state machine.
Result<Invoice> result = await ResultTry.TryAsync(() => paymentGateway.ChargeAsync(order, ct));
```

An `AggregateException` (e.g., caught from a `Task.Wait()`/`.Result`-style call) is flattened via `AggregateException.Flatten()` before recording, so every inner exception is individually represented — not just the generic outer aggregate. `ResultTry` never rethrows, except for a genuine `OperationCanceledException`/`TaskCanceledException`, which always propagates (see above).

**Reading exception detail from traces.** The default mapping never puts raw exception text into `Error.Message` — instead it calls `Activity.Current?.AddException(exception)` (a .NET 8+ BCL member, zero new dependency) once per (flattened) exception, recording it as a structured OTel-semantic-convention event on the ambient trace span:

```csharp
Result<Customer> result = ResultTry.Try(() => thirdPartySdk.GetCustomer(customerId));
// On failure: result.Error.Message == ResultTry.DefaultUnexpectedMessage — no raw exception text.
// The raw exception (type, message, stack trace) is recorded as an "exception" event on
// Activity.Current, flowing through the same ambient OTel trace-export pipeline that
// 13.ServiceDefaults already wires up — never serialized into the HTTP response.
```

When `Activity.Current` is `null` (no active span), the exception detail is recorded nowhere — a documented, accepted limitation. A caller that needs a guaranteed capture path should supply its own `onException` mapper.

#### ResultCombine — Aggregating Independent Checks

`ResultCombine.Combine` folds a batch of independent `Result`/`Result<T>` outcomes into a single `ValidationResult` / `ValidationResult<IReadOnlyList<T>>`. Every input is evaluated — there is no short-circuit on the first failure — so a failed aggregate always carries every failing `Error`, not just the first.

```csharp
// Non-generic: several independent field checks, each returning a plain Result
ValidationResult validation = ResultCombine.Combine(
    Guard.Against.NullOrWhiteSpace(command.Email, nameof(command.Email)) is { } e1
        ? Result.Failure(e1) : Result.Success(),
    Guard.Against.OutOfRange(command.Age, 0, 150, nameof(command.Age)) is { } e2
        ? Result.Failure(e2) : Result.Success());

if (validation.IsValid)
{
    // proceed
}
else
{
    foreach (var error in validation.Errors)
        logger.LogWarning("Validation failed: {Code} — {Message}", error.Code, error.Message);
}

// Generic: batch-validate/parse several independent Result<T>-returning steps and collect
// every success value, in input order, when all succeed
ValidationResult<IReadOnlyList<LineItem>> lineItems = ResultCombine.Combine(
    request.Lines.Select(line => ParseLineItem(line)));   // IEnumerable<Result<LineItem>>

Order order = lineItems.IsValid
    ? Order.Create(lineItems.Value)
    : throw new ValidationException(lineItems.Errors);
```

---

### Base Exceptions

The exception hierarchy bridges `Result<T>` (railway world) with callers that consume exceptions. Every exception carries a structured `Error` payload. String-only constructors are not provided.

```csharp
// At domain rule violations
throw new DomainException(Error.Validation("order.max_items", "Orders cannot exceed 50 items."));

// At validation pipeline boundaries (bridges ValidationResult to exception world)
throw new ValidationException(validationResult.Errors);

// At infrastructure boundaries
throw new NotFoundException(Error.NotFound("product.not_found", "SKU-42 not found."));
throw new ConflictException(Error.Conflict("order.duplicate", "Duplicate order detected."));
throw new UnauthorizedException(Error.Unauthorized("auth.forbidden", "Insufficient permissions."));
```

HTTP mapping guidance:

| Exception | HTTP Status |
|-----------|------------|
| `DomainException` | 422 Unprocessable Entity |
| `ValidationException` | 400 Bad Request |
| `NotFoundException` | 404 Not Found |
| `ConflictException` | 409 Conflict |
| `UnauthorizedException` | 401 Unauthorized / 403 Forbidden |

---

### BCL Extension Methods

#### String Extensions

```csharp
"UserProfileService".ToSnakeCase()   // "user_profile_service"
"user_profile".ToPascalCase()        // "UserProfile"
"UserProfile".ToCamelCase()          // "userProfile"
"  ".IsNullOrWhiteSpace()            // true
((string?)null).IsNullOrWhiteSpace() // true
```

#### IEnumerable Extensions

```csharp
// Split a large collection into batches for bulk processing
int[] ids = [1, 2, 3, 4, 5, 6, 7];
foreach (int[] batch in ids.ToBatches(3))
{
    // batch 1: [1, 2, 3], batch 2: [4, 5, 6], batch 3: [7]
}

// Null/empty guard at API boundaries
List<string>? names = null;
names.IsNullOrEmpty(); // true

// Filter nulls from a mixed collection
IEnumerable<string?> mixed = ["a", null, "b", null, "c"];
IEnumerable<string> clean = mixed.WhereNotNull(); // ["a", "b", "c"]
```

#### DateTimeOffset Extensions

```csharp
DateTimeOffset now = clock.UtcNow;

long ms             = now.ToUnixMilliseconds(); // e.g., 1747180800000
DateTimeOffset start = now.StartOfDay();        // 2026-05-14T00:00:00.000+00:00
DateTimeOffset end   = now.EndOfDay();          // 2026-05-14T23:59:59.999+00:00
```

#### Guid Extensions

```csharp
Guid.Empty.IsEmpty()     // true
Guid.NewGuid().IsEmpty() // false
```

---

## SharedKernel.Configuration — Validated Options

### AddValidatedOptions Startup-Validation Pattern

`AddValidatedOptions<TOptions>` binds an options class to a configuration section and validates it at host startup using Data Annotations. A misconfigured application fails at `IHost.StartAsync()` — not silently at first access.

#### Step 1 — Define the Options Class

```csharp
using System.ComponentModel.DataAnnotations;

public sealed class EmailOptions
{
    [Required]
    public string SmtpHost { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int SmtpPort { get; init; } = 587;

    [Required, MaxLength(100)]
    public string SenderName { get; init; } = string.Empty;

    [Required, EmailAddress]
    public string SenderEmail { get; init; } = string.Empty;
}
```

#### Step 2 — Add the Configuration Section

```json
{
  "Email": {
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "SenderName": "My App",
    "SenderEmail": "noreply@example.com"
  }
}
```

#### Step 3 — Register at Startup

```csharp
// Program.cs
builder.Services.AddValidatedOptions<EmailOptions>(
    builder.Configuration.GetSection("Email"));
```

This single call registers `IOptions<EmailOptions>`, `IOptionsSnapshot<EmailOptions>`, and `IOptionsMonitor<EmailOptions>`, and enables eager startup validation.

#### Step 4 — Inject and Use

```csharp
// Inject IOptions<T> for singleton-lifetime services (value is fixed at startup)
public sealed class EmailService(IOptions<EmailOptions> options)
{
    private readonly EmailOptions _opts = options.Value;
}

// Inject IOptionsMonitor<T> for live-reload support (e.g., Kubernetes ConfigMap updates)
public sealed class EmailService(IOptionsMonitor<EmailOptions> monitor)
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var opts = monitor.CurrentValue; // always the latest valid configuration
        // ...
    }
}
```

#### Startup failure when configuration is invalid

If any Data Annotations constraint is violated, `IHost.StartAsync()` throws `OptionsValidationException` immediately:

```
Microsoft.Extensions.Options.OptionsValidationException:
  DataAnnotation validation failed for 'EmailOptions' members:
  'SenderEmail' with the error: 'The SenderEmail field is not a valid e-mail address.'.
```

### Source-Generated Options Validation (opt-in, AOT-clean)

`AddValidatedOptions<TOptions>` (above) remains the platform **default** — every existing consumer
already depends on its Data Annotations + reflection-based validation, and this section changes
nothing about it.

An additive **`AddValidatedOptions<TOptions, TValidator>(IConfiguration section)`** overload is also
available, where `TValidator : class, IValidateOptions<TOptions>`. It binds the section with **no**
`.ValidateDataAnnotations()` call — the entire point is avoiding that reflection-based validator —
registers `TValidator` via `TryAddSingleton<IValidateOptions<TOptions>, TValidator>()`, and still calls
`.ValidateOnStart()`, so a misconfigured application fails at `IHost.StartAsync()` exactly like the
Data Annotations path.

`TValidator` is typically a `partial class` annotated with the in-box BCL `[OptionsValidator]` source
generator (part of the base `Microsoft.Extensions.Options` package, no extra NuGet reference needed).
The generator reads the same Data Annotations attributes on `TOptions` and emits the `Validate` method
body at compile time — zero reflection at validation time. Any other hand-written
`IValidateOptions<TOptions>` works too; this overload only depends on the resulting interface, never on
the generator itself.

```csharp
using Microsoft.Extensions.Options;

// Same EmailOptions class as Step 1 above — no changes needed.

[OptionsValidator]
public partial class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
}

// Program.cs — note the second generic argument, TValidator.
builder.Services.AddValidatedOptions<EmailOptions, EmailOptionsValidator>(
    builder.Configuration.GetSection("Email"));
```

Choose this path for a strict AOT/trimming posture, or simply to avoid startup-time reflection; keep
using the Data Annotations overload otherwise. Both call `.ValidateOnStart()` and fail identically at
`IHost.StartAsync()`.

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

## SharedKernel.Core — Guard Clauses (merged from `SharedKernel.Guards`, P-505/WO-082)

`SharedKernel.Core` provides a two-path guard system for validating inputs and enforcing invariants, merged in from the former standalone `SharedKernel.Guards` package — the `SharedKernel.Guards`/`SharedKernel.Guards.Clauses`/`SharedKernel.Guards.Descriptions` C# namespaces below are completely unchanged by that move; only the physical package changed, so an existing consumer's only required change is swapping the `PackageReference` from `SharedKernel.Guards` to `SharedKernel.Core`. Every guard is available via both paths:

| Path | Entry point | Returns | Use when |
|------|-------------|---------|---------|
| Functional | `Guard.Against.*` | `Error?` — `null` on pass, non-null on violation | Railway chains, explicit error handling |
| Imperative | `Guard.Throw.*` | `void` — throws `DomainException` on violation | Constructor guards, domain invariants |

### Functional Path — `Guard.Against.*`

The functional path returns `Error?`. `null` means the guard passed; a non-null `Error` means it was violated. This is the preferred path inside railway chains.

```csharp
// Null / empty checks
Error? e1 = Guard.Against.Null(order, nameof(order));
Error? e2 = Guard.Against.NullOrEmpty(request.Name, nameof(request.Name));
Error? e3 = Guard.Against.NullOrWhiteSpace(request.Email, nameof(request.Email));

// String length
Error? e4 = Guard.Against.ShorterThan(request.Name, minLength: 2, nameof(request.Name));
Error? e5 = Guard.Against.LongerThan(request.Bio, maxLength: 500, nameof(request.Bio));

// Numeric (overloaded for int, decimal, long)
Error? e6 = Guard.Against.NegativeOrZero(order.Quantity, nameof(order.Quantity));
Error? e7 = Guard.Against.Negative(account.Balance, nameof(account.Balance));
Error? e8 = Guard.Against.NotPositive(product.Price, nameof(product.Price));

// Range
Error? e9 = Guard.Against.OutOfRange(rating, min: 1, max: 5, nameof(rating));

// Default value / Guid
Error? e10 = Guard.Against.Default(customerId, nameof(customerId));
Error? e11 = Guard.Against.InvalidGuid(orderId, nameof(orderId));

// Format and email
Error? e12 = Guard.Against.InvalidFormat(code, pattern: @"^[A-Z]{3}-\d{4}$", nameof(code));
Error? e13 = Guard.Against.Email(request.Email, nameof(request.Email));

// Collections
Error? e14 = Guard.Against.Empty(order.Items, nameof(order.Items));
Error? e15 = Guard.Against.MaxCount(tags, max: 10, nameof(tags));
Error? e16 = Guard.Against.MinCount(recipients, min: 1, nameof(recipients));

// Boolean predicates — caller supplies the Error for arbitrary business rules
Error? e17 = Guard.Against.True(order.IsCancelled, Error.Conflict("order.cancelled", "Order is already cancelled."));
Error? e18 = Guard.Against.False(customer.IsActive, Error.Unauthorized("customer.inactive", "Customer account is inactive."));

// SmartEnum membership
Error? e19 = Guard.Against.InvalidSmartEnum<OrderStatus, int>(statusId);
```

#### Using Against.* inside a railway chain

```csharp
public Result<Order> PlaceOrder(PlaceOrderCommand cmd)
{
    if (Guard.Against.NullOrWhiteSpace(cmd.CustomerId, nameof(cmd.CustomerId)) is { } e1)
        return e1;

    if (Guard.Against.NegativeOrZero(cmd.Quantity, nameof(cmd.Quantity)) is { } e2)
        return e2;

    if (Guard.Against.InvalidSmartEnum<OrderStatus, int>(cmd.StatusId) is { } e3)
        return e3;

    // All guards passed — proceed with business logic
    var order = new Order(cmd.CustomerId, cmd.Quantity, OrderStatus.FromValue(cmd.StatusId));
    return Result<Order>.Success(order);
}
```

### Imperative Path — `Guard.Throw.*`

The imperative path throws `DomainException` on violation. It mirrors every `Against.*` extension as a void method. Use this in domain constructors and invariant methods where railway chains are not in use.

```csharp
public sealed class Order
{
    public string CustomerId { get; }
    public int Quantity { get; }
    public OrderStatus Status { get; }

    public Order(string customerId, int quantity, OrderStatus status)
    {
        Guard.Throw.NullOrWhiteSpace(customerId, nameof(customerId));
        Guard.Throw.NegativeOrZero(quantity, nameof(quantity));
        Guard.Throw.Null(status, nameof(status));

        CustomerId = customerId;
        Quantity   = quantity;
        Status     = status;
    }

    public void Ship()
    {
        Guard.Throw.False(
            Status == OrderStatus.Processing,
            Error.Conflict("order.invalid_state", "Only Processing orders can be shipped."));

        // proceed to ship
    }
}
```

#### Complete imperative example — service constructor

```csharp
public sealed class PaymentService
{
    private readonly string _apiKey;
    private readonly Uri _endpoint;

    public PaymentService(string apiKey, Uri endpoint)
    {
        Guard.Throw.NullOrWhiteSpace(apiKey, nameof(apiKey));
        Guard.Throw.Null(endpoint, nameof(endpoint));

        _apiKey   = apiKey;
        _endpoint = endpoint;
    }

    public Task<Result<PaymentReceipt>> ChargeAsync(
        decimal amount, string currency, CancellationToken ct)
    {
        Guard.Throw.NegativeOrZero(amount, nameof(amount));
        Guard.Throw.NullOrWhiteSpace(currency, nameof(currency));
        Guard.Throw.LongerThan(currency, maxLength: 3, nameof(currency));

        // proceed with payment API call
        throw new NotImplementedException();
    }
}
```

### Guard Category Reference

| Category | `Against.*` method | Throws on… |
|----------|--------------------|-----------|
| Null | `Null<T>` | reference is `null` |
| Null/empty | `NullOrEmpty` | `null` or `""` |
| Null/whitespace | `NullOrWhiteSpace` | `null`, `""`, or only whitespace |
| Min length | `ShorterThan(value, minLength)` | `value.Length < minLength` |
| Max length | `LongerThan(value, maxLength)` | `value.Length > maxLength` |
| Positive only | `NegativeOrZero` | `value <= 0` |
| Non-negative | `Negative` | `value < 0` |
| Positive only | `NotPositive` | `value <= 0` (alias with different message) |
| Range | `OutOfRange<T>(value, min, max)` | `value < min` or `value > max` |
| Default value | `Default<T>` | `EqualityComparer<T>.Default` match |
| Empty GUID | `InvalidGuid` | `value == Guid.Empty` |
| Regex format | `InvalidFormat(value, pattern)` | pattern not matched (cached `Regex`) |
| Email | `Email` | not a valid email address |
| Empty collection | `Empty<T>` | no elements |
| Max elements | `MaxCount<T>(source, max)` | `Count > max` |
| Min elements | `MinCount<T>(source, min)` | `Count < min` |
| Predicate true | `True(condition, error)` | `condition == false` |
| Predicate false | `False(condition, error)` | `condition == true` |
| SmartEnum | `InvalidSmartEnum<TEnum, TValue>(id)` | `id` not a known member |

**Bounded format-guard `Regex` cache (P-522/WO-083).** `InvalidFormat`/`Email`'s pattern-keyed compiled-`Regex` cache is capped at 256 distinct patterns (`MaxCachedPatterns`), evicting the oldest-inserted pattern first (FIFO, via a companion insertion-order queue) once the cap is exceeded. The existing 250ms ReDoS timeout is unaffected. Every current call site passes a literal, compile-time-known pattern, so eviction never triggers in practice — the bound exists purely against a hypothetical future call site deriving a pattern from configuration or user input.

---

## SharedKernel.Cryptography — Dependency-Free Crypto Primitives

`SharedKernel.Cryptography` provides secret-agnostic one-way hashing, authenticated symmetric encryption, asymmetric signing, HMAC signing, and secure random generation. It is pure BCL `System.Security.Cryptography` — zero third-party NuGet dependencies — and is deliberately decoupled from `12.Security`'s identity/JWT/OIDC concerns, so non-web worker services (background jobs, batch processors, internal tools) can consume it without pulling in an identity stack.

### Registration

```csharp
// Program.cs
builder.Services.AddSharedKernelCryptography(builder.Configuration);

// Consuming services must separately register their own key providers —
// this package ships no default implementation and holds no key material.
builder.Services.AddSingleton<IEncryptionKeyProvider, MyKeyVaultBackedKeyProvider>();
builder.Services.AddSingleton<IAsymmetricKeyProvider, MyCertificateStoreKeyProvider>();
```

Optional configuration (`SharedKernel:Cryptography` section, all fields optional with safe defaults):

```json
{
  "SharedKernel": {
    "Cryptography": {
      "Pbkdf2Iterations": 600000,
      "DefaultSigningKeyId": "primary-2026"
    }
  }
}
```

### One-Way Hashing

`IOneWayHasher` produces a self-describing encoded hash (algorithm marker, iteration count, salt, and subkey all in one string) so the iteration count can be raised later without invalidating hashes already in the database. It is **secret-agnostic** — a password is one example consumer, not the sole purpose. The exact same `Hash`/`Verify` contract applies to API keys, recovery codes, security-question answers, or any other one-way, slow, salted-hash-then-verify secret.

**Password usage:**

```csharp
public sealed class AccountService(IOneWayHasher hasher)
{
    public string RegisterUser(string plaintextPassword)
    {
        // Store the returned string verbatim — salt and iteration count travel with it.
        return hasher.Hash(plaintextPassword);
    }

    public bool TryLogin(string storedHash, string suppliedPassword, out bool needsRehash)
    {
        HashVerificationResult result = hasher.Verify(storedHash, suppliedPassword);
        needsRehash = result == HashVerificationResult.SuccessRehashNeeded;

        // SuccessRehashNeeded: the hash matched, but it was produced under an older
        // Pbkdf2Iterations value. Re-hash and persist the new value on this login.
        if (needsRehash)
        {
            string upgraded = hasher.Hash(suppliedPassword);
            // persist `upgraded` in place of storedHash
        }

        return result is HashVerificationResult.Success or HashVerificationResult.SuccessRehashNeeded;
    }
}
```

**Non-password usage (API key):** the same hasher, same contract — no parallel "API key hashing" type needed.

```csharp
public sealed class ApiKeyService(IOneWayHasher hasher)
{
    public string IssueApiKey(string plaintextApiKey)
    {
        // Store the hash, return the plaintext key to the caller exactly once.
        return hasher.Hash(plaintextApiKey);
    }

    public bool TryAuthenticate(string storedHash, string suppliedApiKey) =>
        hasher.Verify(storedHash, suppliedApiKey) is
            HashVerificationResult.Success or HashVerificationResult.SuccessRehashNeeded;
}
```

Never hash passwords, API keys, recovery codes, or any other one-way secret with raw `SHA256`/`SHA512`/`MD5` anywhere in the platform — only through `IOneWayHasher`.

**PBKDF2 iteration floor and verify-time ceiling (P-512/WO-083).** `CryptographyOptions.Pbkdf2Iterations` now carries a `[Range(CryptographyOptions.MinimumPbkdf2Iterations, int.MaxValue)]` — `MinimumPbkdf2Iterations` is `100,000`, enforced entirely through the existing `AddValidatedOptions`/`ValidateOnStart()` path this options type was already wired into. A configured value of `1` (or any value below `100,000`) now fails fast at host startup instead of silently defeating the entire point of a deliberately slow key-derivation function.

This floor applies **only at `Hash()` time**, on newly-configured values — **never retroactively at `Verify` time**. A hash already stored under an older, lower-than-100,000 configuration (this package shipped with no floor at all before this phase) continues to verify exactly as before; `Verify` still reports `SuccessRehashNeeded` so the caller can opportunistically re-hash it under the current configuration on next successful login, but it is never rejected outright by the new floor.

`Pbkdf2OneWayHasher.Verify` separately enforces a fixed `MaxVerifiableIterations` ceiling (`2,000,000`) against the iteration count embedded in the *hash being verified* — deliberately a constant independent of `CryptographyOptions.Pbkdf2Iterations`'s currently-configured value, since a stored hash's embedded iteration count is attacker-influenceable (anyone who can write a hash row can write an absurd one) and a future legitimate increase to the configured default must never require a simultaneous ceiling bump. The check happens **before** the expensive `Rfc2898DeriveBytes.Pbkdf2` call ever runs — checking afterward would defeat the purpose. `Verify` also now rejects a decoded subkey whose length is not exactly 32 bytes and a stored iteration count that is zero or negative — both previously reachable, un-validated inputs from a corrupted or adversarial hash blob. All three checks return `HashVerificationResult.Failed`, never throw.

### Argon2id Password Hashing — `SharedKernel.Cryptography.Argon2`

`Argon2idOneWayHasher` (from the sibling `SharedKernel.Cryptography.Argon2` package) implements the same `IOneWayHasher` contract above, using Argon2id (RFC 9106) via the pure-managed `Konscious.Security.Cryptography.Argon2` package — no native/P-Invoke dependency. It is the one `01.Core` package with a genuine third-party Argon2id dependency, kept out of this package for the identical reason `SharedKernel.Cryptography.KeyVault.Azure` keeps the Azure SDK out.

Registered as a **keyed** singleton ("Argon2id") — `Pbkdf2OneWayHasher` above remains the unkeyed default. **FIPS-mode is the deciding factor**: PBKDF2 is FIPS 140-3 approved and Argon2id is not, so PBKDF2 stays the default for FIPS-constrained deployments; choose Argon2id everywhere FIPS-mode is not a hard requirement — it is OWASP's current top recommendation for new password-storage designs.

```csharp
builder.Services.AddSharedKernelCryptography(builder.Configuration);       // unkeyed default: Pbkdf2OneWayHasher
builder.Services.AddSharedKernelArgon2Cryptography(builder.Configuration); // keyed "Argon2id": Argon2idOneWayHasher

IOneWayHasher argon2 = provider.GetRequiredKeyedService<IOneWayHasher>(
    Argon2CryptographyServiceCollectionExtensions.Argon2idOneWayHasherKey);

string hash = argon2.Hash(plaintextPassword);
HashVerificationResult result = argon2.Verify(hash, suppliedPassword);
// result == SuccessRehashNeeded when the stored $argon2id$v=19$m=...,t=...,p=... parameters
// differ from the currently configured Argon2CryptographyOptions — same rehash contract as
// Pbkdf2OneWayHasher, over the real, interoperable PHC string format instead of a bespoke encoding.
```

`Argon2CryptographyOptions` (`.MemorySizeKb` default 19456, `.Iterations` default 2, `.DegreeOfParallelism` default 1 — OWASP's current default row) carries a real `[Range]` floor and ceiling on every property, deliberately unlike `CryptographyOptions.Pbkdf2Iterations`'s original `[Range(1, int.MaxValue)]` nominal floor. See the [package README](SharedKernel.Cryptography.Argon2/README.md) for the full Argon2id-vs-PBKDF2 comparison table.

### IContentHasher — Non-Secret Content Fingerprinting

`IContentHasher` is the deliberate architectural opposite of `IOneWayHasher` above: a fast, non-salted, non-iterated SHA-256 digest for **non-secret** content-fingerprinting — object-storage ETags/checksums, content-addressable deduplication keys, and cache-key derivation from a payload body. `IOneWayHasher` is intentionally slow (600,000 PBKDF2 iterations) to resist brute-force attacks on secrets — exactly the wrong tool, both performance-wise and semantically, for hashing a 50MB upload to compute its ETag.

**Never use `IContentHasher` for passwords, API keys, recovery codes, or any other secret — use `IOneWayHasher` for those.** The two contracts must never be conflated.

```csharp
public sealed class BlobUploadExample(IContentHasher contentHasher)
{
    // Small in-memory payload — byte[] overload.
    public string ComputeETag(byte[] fileBytes) =>
        contentHasher.ComputeHashHex(fileBytes); // lowercase hex, ready to use as an ETag

    // Large upload — streaming overload never materializes the full content in memory.
    public async Task<string> ComputeChecksumAsync(Stream uploadStream, CancellationToken ct)
    {
        byte[] digest = await contentHasher.ComputeHashAsync(uploadStream, ct);
        return Convert.ToHexStringLower(digest);
    }

    // Content-addressable dedup key derived from a payload body.
    public string DeriveCacheKey(byte[] payload) =>
        $"payload:{contentHasher.ComputeHashBase64(payload)}";
}
```

`ComputeHash(byte[])`/`ComputeHash(Stream)`/`ComputeHashAsync(Stream, CancellationToken)` all return the raw digest bytes; `ContentHasherExtensions.ComputeHashHex`/`ComputeHashBase64` add convenience string encoding on top without introducing a second hashing strategy.

### Symmetric Encryption (AES-256-GCM)

`ISymmetricEncryptionService` is for general-purpose encryption of arbitrary payloads outside an EF Core column — before publishing to a queue, writing to blob storage, or returning from an API. It is distinct from `06.Persistence`'s `EncryptedValueConverter`, which remains the dedicated path for transparent EF Core column-level encryption.

**The AES-256-only guarantee is now structurally enforced, not merely documented (P-513/WO-083).** `AesGcmEncryptionService` rejects any `CryptographicKey.Material` whose length is not exactly 32 bytes — with a thrown `CryptographicException` naming both the expected and actual length — before any `AesGcm` instance is ever constructed, on every one of `Encrypt`/`EncryptAsync`/`Decrypt`/`DecryptAsync` (their shared `EncryptCore`/`DecryptCore` core enforces it once for all four). Previously, `AesGcm`'s own constructor silently accepted any BCL-legal AES key size — 16 or 24 bytes included — constructing AES-128-GCM or AES-192-GCM from a misconfigured `IEncryptionKeyProvider` with no complaint at all, despite every doc, XML comment, and NuGet package description on this platform promising AES-256. This is an exact-length check, not a minimum: a too-long key (24 bytes/AES-192) is rejected exactly like a too-short one (16 bytes/AES-128) — both are configuration defects, never soft failures. It throws rather than returning a `Result<T>` failure, because a wrong-size key is an infrastructure/provisioning defect the caller did not cause and cannot recover from at the call site — distinct from this class's `Result<T>` failures, which are reserved for genuine runtime/tampered-input conditions (a wrong key, tamper, mismatched associated data).

`IEncryptionKeyProvider` resolves the key material. Both of its members are asynchronous and `CancellationToken`-aware, so a genuine network-bound KMS/HSM implementation (Azure Key Vault, AWS KMS, HashiCorp Vault) never needs a blocking-on-async anti-pattern:

```csharp
// Consuming service supplies key material — SharedKernel.Cryptography holds none of its own.
// A synchronous/config-backed provider can still complete synchronously by returning an
// already-completed ValueTask, exactly like this one does. It ALSO implements
// ISynchronousEncryptionKeyProvider (P-492/WO-081) instead of the plain IEncryptionKeyProvider —
// an explicit, author-asserted claim that neither member ever performs blocking I/O — which is
// what makes the retained sync Encrypt/Decrypt/EncryptToString/DecryptToString members usable
// against this provider at all. See "Gating the synchronous members" below.
public sealed class MyConfigBackedKeyProvider : ISynchronousEncryptionKeyProvider
{
    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
        new(new CryptographicKey("key-v2", LoadKeyMaterialFromConfig("key-v2"))); // 32 bytes for AES-256

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
        new(TryLoadKeyMaterialFromConfig(keyId, out byte[] material) ? new CryptographicKey(keyId, material) : null);
}

public sealed class PayloadEncryptionExample(ISymmetricEncryptionService encryption)
{
    // Prefer the *Async overloads on hot/high-throughput paths — they never block a thread
    // while resolving the key, regardless of whether the provider completes synchronously
    // or asynchronously.
    //
    // associatedData (AAD) is authenticated but never encrypted and never persisted inside
    // EncryptedPayload — bind it to context the caller can reproduce byte-identically at
    // decrypt time (a queue message's type name, here). Pass Array.Empty<byte>() when no
    // natural context binding exists; there is no default value.
    public ValueTask<EncryptedPayload> EncryptForQueueAsync(byte[] plaintext, string messageType, CancellationToken ct) =>
        encryption.EncryptAsync(plaintext, Encoding.UTF8.GetBytes(messageType), ct); // fresh random nonce every call — never reused

    public ValueTask<Result<byte[]>> DecryptFromQueueAsync(EncryptedPayload payload, string messageType, CancellationToken ct) =>
        encryption.DecryptAsync(payload, Encoding.UTF8.GetBytes(messageType), ct); // Result<byte[]> — never throws CryptographicException directly

    // The synchronous members are retained for call sites that cannot easily become async
    // (e.g. a synchronous EF Core ValueConverter). They bridge onto the async key provider via
    // .GetAwaiter().GetResult() — but ONLY when the registered IEncryptionKeyProvider genuinely
    // never blocks (see "Gating the synchronous members" below). Against an unmarked provider —
    // notably any raw KMS/HSM provider — these members throw NotSupportedException instead of
    // silently blocking a thread.
    public string EncryptSecret(string plaintext) => encryption.EncryptToString(plaintext, Array.Empty<byte>());

    public Result<string> DecryptSecret(string encoded) => encryption.DecryptToString(encoded, Array.Empty<byte>());
}
```

Handling tamper/wrong-key/mismatched-AAD failures:

```csharp
Result<byte[]> decrypted = await encryption.DecryptAsync(payload, associatedData, ct);

if (decrypted.IsSuccess)
{
    ProcessPlaintext(decrypted.Value);
}
else
{
    logger.LogWarning("Decryption failed: {Code} — {Message}", decrypted.Error.Code, decrypted.Error.Message);
}
// error.Code is one of CryptographyErrorCodes.DecryptionFailed, .UnknownKeyId, or .MalformedPayload —
// a mismatched associatedData surfaces as DecryptionFailed, indistinguishable from a tampered
// ciphertext/tag or wrong key. There is no separate "AAD mismatch" error code.
```

`ISymmetricEncryptionService` always uses an AEAD cipher (AES-GCM) — never an unauthenticated mode such as CBC/ECB.

#### Migrating a custom `IEncryptionKeyProvider` implementer (P-446/WO-068, breaking)

`IEncryptionKeyProvider`'s synchronous `GetCurrentKey()`/`GetKey(string)` members were **removed outright** — not kept as a parallel overload. Every implementer must migrate to the asynchronous shape:

| Before (removed) | After |
|---|---|
| `CryptographicKey GetCurrentKey()` | `ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)` |
| `CryptographicKey? GetKey(string keyId)` | `ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)` |

A synchronous/config-backed implementer migrates mechanically — wrap the existing return value in `new ValueTask<CryptographicKey>(...)` (or `new ValueTask<CryptographicKey?>(...)`), exactly as shown in `MyConfigBackedKeyProvider` above. No behavioral change is required for that class of implementer, and **config-supplied keys remain the fully-supported default requiring no consumer-side opt-in.** A genuinely network-bound implementer (a real KMS/HSM call) can now `await` its SDK call directly instead of blocking a thread.

#### Migrating to associated data (AAD) (P-491/WO-081, breaking)

Every `ISymmetricEncryptionService` member gained a **required** `byte[] associatedData` parameter, positioned immediately after the primary payload parameter and before `CancellationToken ct = default` on the async members. There is **no default value on any overload, ever** — every call site across every consuming domain must add an explicit argument:

| Member | Before (removed) | After |
| --- | --- | --- |
| `Encrypt` | `EncryptedPayload Encrypt(byte[] plaintext)` | `EncryptedPayload Encrypt(byte[] plaintext, byte[] associatedData)` |
| `EncryptAsync` | `ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, CancellationToken ct = default)` | `ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct = default)` |
| `Decrypt` | `Result<byte[]> Decrypt(EncryptedPayload payload)` | `Result<byte[]> Decrypt(EncryptedPayload payload, byte[] associatedData)` |
| `DecryptAsync` | `ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, CancellationToken ct = default)` | `ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, byte[] associatedData, CancellationToken ct = default)` |
| `EncryptToString` | `string EncryptToString(string plaintext)` | `string EncryptToString(string plaintext, byte[] associatedData)` |
| `EncryptToStringAsync` | `ValueTask<string> EncryptToStringAsync(string plaintext, CancellationToken ct = default)` | `ValueTask<string> EncryptToStringAsync(string plaintext, byte[] associatedData, CancellationToken ct = default)` |
| `DecryptToString` | `Result<string> DecryptToString(string encoded)` | `Result<string> DecryptToString(string encoded, byte[] associatedData)` |
| `DecryptToStringAsync` | `ValueTask<Result<string>> DecryptToStringAsync(string encoded, CancellationToken ct = default)` | `ValueTask<Result<string>> DecryptToStringAsync(string encoded, byte[] associatedData, CancellationToken ct = default)` |

A call site with no natural context binding must pass `Array.Empty<byte>()` explicitly — never rely on an implicit default, because there isn't one. Associated data is authenticated (bound into the AES-GCM tag via `AesGcm.Encrypt`/`.Decrypt`'s own `associatedData` parameter) but **never encrypted and never persisted** inside `EncryptedPayload` — no new field was added, and the packed `EncryptToString`/`DecryptToString` string format never embeds it. The caller alone is responsible for reproducing byte-identical AAD at decrypt time from context already available then:

```csharp
// Binding a cached value's ciphertext to the exact key it was stored under — a value copied or
// replayed under a different key fails authentication instead of decrypting cleanly.
byte[] aad = Encoding.UTF8.GetBytes(cacheKey);
EncryptedPayload payload = await encryption.EncryptAsync(plaintextBytes, aad, ct);
// ... later, using the SAME cacheKey ...
Result<byte[]> decrypted = await encryption.DecryptAsync(payload, aad, ct);

// Other good AAD candidates, by call site: an owning row's primary key (column-level
// encryption), a message's CLR/CloudEvents type name (message-bus payload encryption), a
// webhook subscription id (webhook payload encryption), a workflow id (workflow payload
// encryption). Pick something the caller can always reconstruct without re-reading the
// ciphertext itself.
```

A mismatched (or omitted, when one was originally supplied) `associatedData` at decrypt time fails authentication exactly like a tampered ciphertext/tag or a wrong key — `Decrypt`/`DecryptAsync` return `Result.Failure` with `CryptographyErrorCodes.DecryptionFailed`. No new `ErrorCodes` constant was introduced for this case.

`IEncryptionKeyProvider` itself is **unaffected** by this change — only `ISymmetricEncryptionService`'s eight members gained the new parameter.

### Gating the synchronous members (P-492/WO-081, breaking behavior change)

`AesGcmEncryptionService`'s retained synchronous members — `Encrypt`, `Decrypt`, `EncryptToString`, `DecryptToString` — used to bridge onto the async `IEncryptionKeyProvider` via an unconditional `.GetAwaiter().GetResult()`. That was genuinely non-blocking against a config-backed provider, but silently blocked a real thread the moment the registered provider was a raw KMS/HSM call. This phase replaces the silent hazard with a structural gate.

`ISynchronousEncryptionKeyProvider` is a zero-member marker interface extending `IEncryptionKeyProvider`:

```csharp
public interface ISynchronousEncryptionKeyProvider : IEncryptionKeyProvider;
```

Implementing it is **an explicit, author-asserted safety claim — never inferred.** **A KMS/HSM-backed provider (Azure Key Vault, AWS KMS, HashiCorp Vault, or any other network-bound key resolution) must never implement this marker** — see `MyConfigBackedKeyProvider` above for a provider that honestly earns it (key material is already resolved from configuration, so every code path is genuinely synchronous).

`EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(IEncryptionKeyProvider)` is the single check both `AesGcmEncryptionService` and any consuming domain can use to answer "is this provider safe to call from a sync path":

```csharp
bool IsGenuinelySynchronous(IEncryptionKeyProvider provider) =>
    provider switch
    {
        ISynchronousEncryptionKeyProvider => true,
        CachedEncryptionKeyProvider cached => IsGenuinelySynchronous(cached.Inner), // recursive unwrap
        _ => false,
    };
```

This is a **static, provider-identity check** — evaluated once, at `AesGcmEncryptionService` construction time, and cached for the instance's lifetime. It is never re-evaluated per call, and it is never a per-call cache-warmth test: a `CachedEncryptionKeyProvider` wrapping a KMS-backed inner provider always reports `false`, even on a call that would in fact hit a warm cache entry, because the next call could just as easily miss. `CachedEncryptionKeyProvider` itself never directly implements `ISynchronousEncryptionKeyProvider` — it exposes a new `Inner` property specifically so the check can see through the decorator to the real leaf provider, unwrapping through any depth of nested `CachedEncryptionKeyProvider`s.

```csharp
// Safe — InMemory/config-backed leaf provider, direct or cached:
EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(new MyConfigBackedKeyProvider());                    // true
EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(
    new CachedEncryptionKeyProvider(new MyConfigBackedKeyProvider(), TimeProvider.System, ttl));              // true

// Unsafe — a raw KMS call, direct or cached (a cache miss still re-enters the inner provider):
EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(new AzureKeyVaultEncryptionKeyProvider(...));         // false
EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(
    new CachedEncryptionKeyProvider(new AzureKeyVaultEncryptionKeyProvider(...), TimeProvider.System, ttl));  // false
```

When the registered provider is **not** genuinely synchronous, `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString` throw `NotSupportedException` immediately — before attempting any bridge — directing the caller to the corresponding `*Async` overload:

```csharp
var service = new AesGcmEncryptionService(new AzureKeyVaultEncryptionKeyProvider(...));

service.Encrypt(plaintext, aad);
// throws NotSupportedException: "The registered IEncryptionKeyProvider does not implement
// ISynchronousEncryptionKeyProvider, ... Call EncryptAsync instead."

await service.EncryptAsync(plaintext, aad, ct); // always usable, regardless of provider marking
```

**This is a real, narrow breaking *behavior* change** — distinct from a compile-time API break, since no method signature changed. Any existing custom `IEncryptionKeyProvider` implementer that relied on the sync members silently blocking a thread against a network-bound provider now gets an immediate, structural `NotSupportedException` instead. There is **zero behavior change** for any provider that is genuinely synchronous and marks itself accordingly (or is wrapped in a `CachedEncryptionKeyProvider` over one) — those call sites are byte-for-byte unaffected. `SharedKernel.Cryptography.KeyVault.Azure`'s `AzureKeyVaultEncryptionKeyProvider` deliberately does **not** implement `ISynchronousEncryptionKeyProvider` — every call is a real Azure SDK round trip — so registering it directly (or wrapping it in `CachedEncryptionKeyProvider`) means the sync members are structurally unusable against it; use the `*Async` members.

### Envelope Encryption

`IEnvelopeEncryptionProvider` is an additive, KMS-idiomatic alternative to `IEncryptionKeyProvider`'s direct-retrieval shape: ask the KMS to generate-and-wrap a fresh data key (`GenerateDataKeyAsync`), use the plaintext key locally, persist only the wrapped form, and later ask the KMS to unwrap it (`UnwrapDataKeyAsync`) — the KMS's own master key material never leaves its boundary. A single provider (e.g. an Azure Key Vault-backed one) may implement both `IEncryptionKeyProvider` and `IEnvelopeEncryptionProvider`.

```csharp
public sealed class EnvelopeEncryptionExample(IEnvelopeEncryptionProvider envelope)
{
    public async Task<(byte[] Ciphertext, byte[] WrappedKey, string MasterKeyId)> EncryptLargePayloadAsync(
        byte[] plaintext, CancellationToken ct)
    {
        EnvelopeDataKey dataKey = await envelope.GenerateDataKeyAsync(ct);

        // Use dataKey.PlaintextKey immediately (e.g. seed a local AesGcm/ISymmetricEncryptionService
        // call) and then let it go out of scope — NEVER persist it anywhere.
        byte[] ciphertext = EncryptLocally(plaintext, dataKey.PlaintextKey);

        // Only the wrapped form is safe to persist alongside the ciphertext.
        return (ciphertext, dataKey.WrappedKey, dataKey.MasterKeyId);
    }

    public async Task<Result<byte[]>> DecryptLargePayloadAsync(
        byte[] ciphertext, byte[] wrappedKey, string masterKeyId, CancellationToken ct)
    {
        Result<byte[]> unwrapped = await envelope.UnwrapDataKeyAsync(wrappedKey, masterKeyId, ct);
        return unwrapped.Map(plaintextKey => DecryptLocally(ciphertext, plaintextKey));
    }
}
```

`SharedKernel.Cryptography` ships no default `IEnvelopeEncryptionProvider` implementation — like `IEncryptionKeyProvider`, the consuming service supplies its own (e.g. a real KMS-backed one, or `SharedKernel.Cryptography.KeyVault.Azure`'s `AzureKeyVaultEncryptionKeyProvider` once shipped).

### CachedEncryptionKeyProvider — Bounded-TTL Key Caching

`CachedEncryptionKeyProvider` is a decorator over any `IEncryptionKeyProvider` that avoids re-resolving key material (e.g. a network-bound KMS call) on every operation. It never serves an entry past its configured TTL, and a single-flight refresh ensures N concurrent callers past expiry trigger exactly one call to the inner provider rather than a thundering herd. It ships with **no package-owned DI extension** — compose it explicitly, mirroring the `IIdGenerator`/`SystemClock(TimeProvider)` no-extension precedent:

```csharp
// Program.cs — config-supplied keys remain the default and need no caching at all. Caching is
// an explicit opt-in for a provider whose resolution is genuinely expensive (a real KMS call).
builder.Services.AddSingleton<IEncryptionKeyProvider>(sp =>
    new CachedEncryptionKeyProvider(
        inner: new MyKmsBackedKeyProvider(sp.GetRequiredService<IMyKmsClient>()),
        timeProvider: TimeProvider.System,
        ttl: TimeSpan.FromMinutes(5)));
```

A failed refresh (an unreachable KMS) propagates the thrown exception to every caller awaiting that single-flight resolution — it never falls back to a stale cached value, matching this whole seam's structural fail-closed posture.

**Cross-caller-cancellation-safe single flight (P-511/WO-083).** When N callers race a shared refresh and one of them cancels its own `CancellationToken`, only THAT caller observes `OperationCanceledException` — every other still-waiting caller's await of the exact same in-flight resolution is completely undisturbed. This is driven by a cache-slot-owned `CancellationTokenSource` (never any individual caller's token) plus `Task.WaitAsync(callerCt)` per caller, with a per-slot waiter reference count: the owned source is cancelled — genuinely abandoning the inner KMS call — only once every currently-awaiting caller has departed, and a slot that did not complete successfully by that point is evicted immediately so a future caller never joins an already-doomed resolution. Prior to this phase, one caller cancelling its own request could fault or cancel every other caller's concurrent, otherwise-perfectly-healthy request against the same key — a real production hazard under any real HTTP-request-scoped cancellation (client disconnect, timeout middleware). The identical fix applies to `SharedKernel.Cryptography.KeyVault.Azure`'s two `AzureKeyVaultEncryptionKeyProvider` caches and its sibling `AzureKeyVaultAsymmetricKeyProvider` — see that package's own section below.

`CachedEncryptionKeyProvider` never itself implements `ISynchronousEncryptionKeyProvider` (P-492/WO-081) — its `Inner` property (the wrapped provider) is what `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous` recurses into. Wrapping a KMS-backed provider like this still leaves `AesGcmEncryptionService`'s sync members structurally unusable — a cache hit is fast, but a cache miss re-enters `Inner`, so nothing here ever "earns" the marker on the KMS provider's behalf. See "Gating the synchronous members" above.

### Azure Key Vault Key Provider — `SharedKernel.Cryptography.KeyVault.Azure`

`AzureKeyVaultEncryptionKeyProvider` (from the sibling `SharedKernel.Cryptography.KeyVault.Azure` package) implements `IEncryptionKeyProvider`, `IEnvelopeEncryptionProvider`, and `IEncryptionKeyProviderProbe` against a real Azure Key Vault. It is the one `01.Core` package with a genuine third-party vendor SDK dependency — `Azure.Security.KeyVault.Keys` + `Azure.Security.KeyVault.Secrets` (added P-496/WO-081, for the durable version registry below) + `Azure.Identity` — kept out of this package so `SharedKernel.Cryptography` itself stays dependency-free.

```csharp
// appsettings.json
// {
//   "SharedKernel": { "Cryptography": { "KeyVault": { "Azure": {
//     "VaultUri": "https://my-vault.vault.azure.net/",
//     "CurrentKeyId": "primary",
//     "KeyNames": { "primary": "tenant-data-key" }
//   } } } }
// }

builder.Services.AddSharedKernelAzureKeyVaultCryptography(builder.Configuration);
// Credential defaults to DefaultAzureCredential; supply your own via:
// builder.Services.PostConfigure<AzureKeyVaultCryptographyOptions>(o => o.Credential = myCredential);

// Optional: bounded-TTL caching, composed explicitly — this package ships none of its own.
builder.Services.AddSingleton<IEncryptionKeyProvider>(sp =>
    new CachedEncryptionKeyProvider(
        sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>(),
        TimeProvider.System,
        TimeSpan.FromMinutes(5)));
```

**Design decision — direct retrieval is built on envelope wrapping, not a second code path.** Azure Key Vault Keys does not export raw HSM-protected key material by default — the vendor-idiomatic operation is `CryptographyClient.WrapKeyAsync`/`UnwrapKeyAsync`, exactly `IEnvelopeEncryptionProvider`'s shape. `GetCurrentKeyAsync`/`GetKeyAsync` are both built atop `GenerateDataKeyAsync`/`UnwrapDataKeyAsync` — the vault's own master key material never crosses the process boundary either way, whether reached through `IEncryptionKeyProvider` or `IEnvelopeEncryptionProvider`. This is a deliberate, permanent design choice — see `AzureKeyVaultEncryptionKeyProvider`'s XML docs for the full reasoning; it must never be "fixed" into two divergent code paths.

**Fail-closed**, matching every other provider in this seam: any genuine Azure SDK exception (unreachable vault, `RequestFailedException` for permission/auth failure, or the vault itself rejecting a wrapped key as tampered) propagates directly — never a silent fallback. The one narrow exception is `UnwrapDataKeyAsync`'s `Result<byte[]>` failure path, returned only when the supplied `masterKeyId` fails *local* well-formedness validation before any call ever reaches Azure, and `GetKeyAsync` translating a Key Vault "not found" (HTTP 404) into `null` per its "retired or unknown" contract.

**Connection reuse.** A `CryptographyClient` is resolved at most once per distinct (Azure key name, key version) pair — never constructed inside a per-call code path — cached in a `SingleFlightCache` (P-511/WO-083; the same cross-caller-cancellation-safe single-flight cache this package's two other sites and `CachedEncryptionKeyProvider` itself use — see that type's section above), mirroring `AzureKeyVaultAsymmetricKeyProvider`'s own cache. Unlike that sibling class, this provider keys by (name, version) rather than name alone, because `UnwrapDataKeyAsync` must be able to pin to a specific historical Azure key version, not only "whichever version is current."

**A durable version registry replaced process-lifetime caching (P-496/WO-081).** Before this phase, `GetCurrentKeyAsync` cached a single locally-generated data key for the lifetime of the process — every process/pod/replica silently minted its **own** unique data key on first use, with no sharing across replicas and no genuine rotation intent. `AzureKeyVaultEncryptionKeyProvider` now stores every version it mints as its own Key Vault Secret (a short opaque tag, `"v1"`, `"v2"`, …) plus a single shared `"current version"` pointer secret every replica reads live — see "Key rotation" below. `CryptographicKey.Id` is now that short tag rather than the previous self-decodable ~470-byte envelope; `GetKeyAsync` still transparently decodes the old envelope shape for any already-persisted row (backward-read compatible, no forced migration).

**A `SingleFlightCache<string, byte[]>`-backed memoization** (P-511/WO-083) caches a version tag's already-unwrapped plaintext data key for the remainder of the process's lifetime once resolved — a second `GetKeyAsync`/`GetCurrentKeyAsync` call citing an already-seen tag costs zero further Key Vault calls, and, like every other single-flight site in this seam, one caller cancelling its own request against a not-yet-resolved tag never disturbs another caller's concurrent request for that same tag. This is also what actually bounds `CachedEncryptionKeyProvider`'s working set now: every replica converges on the same small, deliberately-minted set of live version tags instead of accumulating one entry per pod restart.

**Readiness probe** — `AzureKeyVaultEncryptionKeyProvider` also implements `IEncryptionKeyProviderProbe`. Unlike every other member of this class, `ProbeAsync` never lets an Azure SDK exception propagate: it performs one read-only key-metadata call (never a wrap/unwrap/sign/verify) and reports `EncryptionKeyProviderHealth.IsHealthy = false` with a `Description` instead of throwing. This mirrors `07.Messaging`'s `IMessageBusProbe`/`MessageBusHealth` shape. `01.Core` ships this probe primitive only, never an `IHealthCheck` — wiring it into `AddHealthChecks()` is `13.ServiceDefaults`'s concern.

```csharp
IEncryptionKeyProviderProbe probe = provider.GetRequiredService<IEncryptionKeyProviderProbe>();
EncryptionKeyProviderHealth health = await probe.ProbeAsync();
// health.IsHealthy / health.Description
```

**Key rotation (P-496/WO-081)** — `MintNewVersionAsync` mints a fresh data-key version, wraps it under the current Azure master key, stores it as a new Key Vault Secret, and atomically repoints the shared `"current version"` pointer secret to it. It is deliberately **not** part of `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider` — a provider-specific operational method, resolved against the concrete `AzureKeyVaultEncryptionKeyProvider` type:

```csharp
AzureKeyVaultEncryptionKeyProvider provider = serviceProvider.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>();

// Mint the FIRST version once, during initial provisioning, before any traffic reaches this
// service — GetCurrentKeyAsync deliberately never auto-mints one (throws InvalidOperationException
// instead, so "current" stays a genuinely deliberate act, never a per-process accident).
string firstVersionTag = await provider.MintNewVersionAsync();

// ... later, from an ops script / hosted job / future 19.Scheduling job — NEVER automatic or
// scheduled by this package itself:
string newVersionTag = await provider.MintNewVersionAsync();
// Every previously-minted version, including the one just superseded, remains resolvable via
// GetKeyAsync(oldTag) indefinitely — MintNewVersionAsync never deletes or overwrites anything.
```

Rotation is deliberately manual — this method makes rotation possible and cheap to call, never automatic/crypto-period-enforced; that policy layer is out of scope here (closer to `19.Scheduling` territory).

**Remote signing (P-494/WO-081)** — the same `AddSharedKernelAzureKeyVaultCryptography(configuration)` call also registers `AzureKeyVaultAsymmetricKeyProvider` as `IAsymmetricKeyProvider`, a **distinct** singleton from `AzureKeyVaultEncryptionKeyProvider` (signing keys and wrap/unwrap keys are a different Key Vault key usage pattern even in the same vault), reusing the identical `AzureKeyVaultCryptographyOptions.KeyNames` map:

```csharp
builder.Services.AddSharedKernelAzureKeyVaultCryptography(builder.Configuration);
builder.Services.AddSharedKernelCryptography(builder.Configuration); // RsaSignatureService/EcdsaSignatureService

IAsymmetricSignatureService rsa = provider.GetRequiredKeyedService<IAsymmetricSignatureService>(
    CryptographyServiceCollectionExtensions.RsaSignatureServiceKey);
byte[] signature = await rsa.SignAsync(data, "primary", ct); // any KeyNames entry works as a signing keyId
bool isValid = await rsa.VerifyAsync(data, signature, "primary", ct);
```

`GetRsaKeyAsync`/`GetEcdsaKeyAsync` return a thin `RSA`/`ECDsa` subclass whose `SignHash`/`VerifyHash` overrides — the real overridable BCL extension points, since `RSA.SignData`/`ECDsa.SignData` are non-virtual convenience methods that hash locally and call `SignHash`/`VerifyHash` internally — delegate to Azure's genuine synchronous `CryptographyClient.Sign`/`Verify`. `RsaSignatureService`/`EcdsaSignatureService` need **zero code changes** beyond what P-493 already introduced to consume this provider. `ExportParameters`/`ImportParameters` always throw `NotSupportedException` — private key material never crosses the process boundary. The provider **never** implements `ISynchronousAsymmetricKeyProvider` (every call is a real network round trip), so always use `SignAsync`/`VerifyAsync`; only key *resolution* is genuinely asynchronous, not the cryptographic call itself (see "Gating the synchronous members" below). One `CryptographyClient` is cached per distinct Azure key name from the first call — never constructed per call. See [`SharedKernel.Cryptography.KeyVault.Azure/README.md`](SharedKernel.Cryptography.KeyVault.Azure/README.md#remote-signing-p-494wo-081) for the full worked example.

### Asymmetric Signing (RSA / ECDSA)

`IAsymmetricSignatureService` has two implementations sharing one interface — `RsaSignatureService` and `EcdsaSignatureService`. Both are registered as **keyed singletons**; `RsaSignatureService` is additionally registered as the unkeyed default.

```csharp
// Consuming service supplies key material. A genuinely synchronous provider (in-memory,
// resolved once from IConfiguration at startup, etc.) should additionally implement
// ISynchronousAsymmetricKeyProvider (P-493/WO-081) instead of the plain IAsymmetricKeyProvider —
// see "Gating the synchronous members" below. A KMS/HSM/certificate-store-backed provider that
// can genuinely block on I/O must never implement that marker.
public sealed class MyCertificateStoreKeyProvider : ISynchronousAsymmetricKeyProvider
{
    public ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default) =>
        new(LoadRsaFromCertificateStore(keyId));

    public ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default) =>
        new(LoadEcdsaFromCertificateStore(keyId));
}

public sealed class TokenSigningExample(
    [FromKeyedServices(CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey)]
        IAsymmetricSignatureService ecdsaSigner)
{
    // Prefer the *Async members on hot paths, and always when the registered
    // IAsymmetricKeyProvider is not confirmed genuinely synchronous — see below.
    public ValueTask<byte[]> SignPayloadAsync(byte[] data, CancellationToken ct) =>
        ecdsaSigner.SignAsync(data, keyId: "signing-key-2026", ct);

    public ValueTask<bool> VerifyPayloadAsync(byte[] data, byte[] signature, CancellationToken ct) =>
        ecdsaSigner.VerifyAsync(data, signature, keyId: "signing-key-2026", ct);
}

// Resolving explicitly from IServiceProvider:
IAsymmetricSignatureService rsa = provider.GetRequiredService<IAsymmetricSignatureService>(); // unkeyed default = RSA
IAsymmetricSignatureService ecdsa = provider.GetRequiredKeyedService<IAsymmetricSignatureService>(
    CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey);
```

RSA uses 2048-bit minimum keys with PSS padding and SHA-256. ECDSA uses the P-256 curve (256-bit minimum) with SHA-256. Both minimums are now enforced on **both** `Sign`/`SignAsync` and `Verify`/`VerifyAsync` — previously RSA enforced it on `Sign` only, and ECDSA enforced no minimum at all (P-493/WO-081).

**A genuine BCL limitation, not a gap in this package:** `RSA`/`ECDsa`'s `SignData`/`VerifyData` have no async overload anywhere in the BCL. `SignAsync`/`VerifyAsync` make **key resolution** asynchronous (the `IAsymmetricKeyProvider` call); the cryptographic sign/verify call itself remains inherently synchronous once the key is in hand. For a remote-KMS-backed key, that final call can still perform a real blocking network round trip on the calling thread — this phase narrows the blocking surface, it does not eliminate it.

**The RSA/ECDSA instance returned by `IAsymmetricKeyProvider` is not caller-owned.** `RsaSignatureService`/`EcdsaSignatureService` never dispose it — a provider may return the same cached instance across many calls. Lifecycle ownership (including whether and when to dispose) belongs entirely to the provider implementation.

#### Migrating `IAsymmetricKeyProvider`/`IAsymmetricSignatureService` to async (P-493/WO-081, breaking)

| Member | Before | After |
| --- | --- | --- |
| `IAsymmetricKeyProvider.GetRsaKey` | `RSA GetRsaKey(string keyId)` | *(removed — replaced by `GetRsaKeyAsync` below)* |
| `IAsymmetricKeyProvider.GetRsaKeyAsync` | *(did not exist)* | `ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default)` |
| `IAsymmetricKeyProvider.GetEcdsaKey` | `ECDsa GetEcdsaKey(string keyId)` | *(removed — replaced by `GetEcdsaKeyAsync` below)* |
| `IAsymmetricKeyProvider.GetEcdsaKeyAsync` | *(did not exist)* | `ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default)` |
| `IAsymmetricSignatureService.Sign` | `byte[] Sign(byte[] data, string keyId)` | unchanged signature — now gated, see below |
| `IAsymmetricSignatureService.SignAsync` | *(did not exist)* | `ValueTask<byte[]> SignAsync(byte[] data, string keyId, CancellationToken ct = default)` |
| `IAsymmetricSignatureService.Verify` | `bool Verify(byte[] data, byte[] signature, string keyId)` | unchanged signature — now gated, see below |
| `IAsymmetricSignatureService.VerifyAsync` | *(did not exist)* | `ValueTask<bool> VerifyAsync(byte[] data, byte[] signature, string keyId, CancellationToken ct = default)` |

The two `IAsymmetricKeyProvider` synchronous members are **removed outright**, not retained as a parallel overload — mirroring P-446/WO-068's `IEncryptionKeyProvider` precedent exactly. A synchronous, config- or certificate-store-backed implementer migrates mechanically by wrapping its existing return value: `RSA GetRsaKey(string keyId) => Load(keyId);` becomes `ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default) => new(Load(keyId));` — no behavioral change for that class of implementer. A `KeyNotFoundException` for an unknown `keyId` propagates through the returned `ValueTask` exactly as it did from the prior synchronous member.

#### Gating the synchronous members (P-493/WO-081, breaking behavior change)

This mirrors `ISymmetricEncryptionService`'s P-492 gating exactly, with one deliberate difference (below). `ISynchronousAsymmetricKeyProvider` is a zero-member marker interface extending `IAsymmetricKeyProvider`:

```csharp
public interface ISynchronousAsymmetricKeyProvider : IAsymmetricKeyProvider;
```

`AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(IAsymmetricKeyProvider)` is a **direct marker check only**:

```csharp
public static bool IsGenuinelySynchronous(IAsymmetricKeyProvider provider) =>
    provider is ISynchronousAsymmetricKeyProvider;
```

**Deliberately no decorator-unwrapping logic** — unlike `EncryptionKeyProviderCapabilities`, which recurses through `CachedEncryptionKeyProvider.Inner`. No caching decorator exists for `IAsymmetricKeyProvider` as of this phase, so there is nothing to unwrap.

This check is evaluated once, at `RsaSignatureService`/`EcdsaSignatureService` construction time, and cached for the instance's lifetime — never re-evaluated per call. When the registered provider is not marked, `Sign`/`Verify` throw `NotSupportedException` immediately instead of silently blocking a real thread:

```csharp
// throws:
// "The registered IAsymmetricKeyProvider does not implement
//  ISynchronousAsymmetricKeyProvider, so it cannot be trusted never to
//  block the calling thread on a network/IPC round trip. Call SignAsync instead."
```

This is a real, narrow breaking *behavior* change — distinct from a compile-time API break, since neither `Sign` nor `Verify`'s signature changed. Any existing custom `IAsymmetricKeyProvider` implementer that relied on the sync members silently blocking a thread against a network-bound provider (e.g. a certificate store requiring a remote lookup, or a future Key Vault-backed provider) now gets an immediate, structural `NotSupportedException` instead. There is zero behavior change for a provider that is genuinely synchronous and marks itself accordingly — those call sites are byte-for-byte unaffected. `SharedKernel.Cryptography.KeyVault.Azure`'s planned remote-signing `IAsymmetricKeyProvider` implementation (P-494/WO-081) deliberately will not implement `ISynchronousAsymmetricKeyProvider` — every call is a real Azure SDK round trip — so registering it means the sync members are structurally unusable against it; use `SignAsync`/`VerifyAsync`.

### HMAC Signing

`IHmacSigner` signs and verifies data with a shared secret using HMACSHA256. `Verify` always uses a constant-time comparison — never `==` or `SequenceEqual` on secret-derived bytes.

```csharp
public sealed class WebhookSignatureExample(IHmacSigner hmacSigner)
{
    public string SignPayload(byte[] payload, byte[] sharedSecret) =>
        Convert.ToHexString(hmacSigner.Sign(payload, sharedSecret));

    public bool VerifyIncomingWebhook(byte[] payload, byte[] receivedSignature, byte[] sharedSecret) =>
        hmacSigner.Verify(payload, receivedSignature, sharedSecret); // timing-attack resistant
}
```

### Secure Random / Token Generation

`ISecureRandomGenerator` is the only permitted source of randomness for tokens, keys, nonces, and salts anywhere in the platform. `System.Random` and `Guid.NewGuid()` are never acceptable substitutes for security-sensitive values.

```csharp
public sealed class TokenIssuanceExample(ISecureRandomGenerator randomGenerator)
{
    public string IssuePasswordResetToken() =>
        randomGenerator.NextToken(); // 32 random bytes, URL-safe Base64, no padding

    public string IssueApiKey() =>
        randomGenerator.NextToken(length: 48); // longer token for higher-entropy use cases

    public byte[] GenerateNewEncryptionKeyMaterial() =>
        randomGenerator.NextBytes(32); // raw bytes — e.g., seeding a new AES-256 key for rotation
}
```

### TOTP/HOTP — Second-Factor Codes

RFC 6238 TOTP (and the RFC 4226 HOTP core it is built on) generates and verifies time-based
one-time passcodes — the same kind of second-factor code produced by Google Authenticator,
Microsoft Authenticator, and similar apps. `AddSharedKernelCryptography` registers
`IHotpGenerator`, `ITotpGenerator`, and `TotpVerifier` as singletons. Unlike the services above,
`ITotpGenerator` additionally requires an `IClock` registration (`services.AddClock()`, from
`SharedKernel.Primitives`) and `TotpVerifier` additionally requires a consumer-supplied
`ITotpReplayGuard` — this package ships no default replay-guard implementation, since a real one
inherently needs a backing store this dependency-free package cannot own. A standalone
`ITotpAttemptThrottle` seam is also available for RFC 4226 §7.3 attempt rate-limiting — also
consumer-implemented, also never registered by this package, and deliberately never wired into
`TotpVerifier` itself (see "Attempt throttling" below).

**Enrollment — provisioning URI:**

```csharp
public sealed class TotpEnrollmentService(ISecureRandomGenerator randomGenerator)
{
    public (byte[] Secret, Uri ProvisioningUri) BeginEnrollment(string accountEmail)
    {
        byte[] secret = randomGenerator.NextBytes(20); // 160 bits — the RFC 4226/6238 default SHA-1 secret length

        Uri provisioningUri = TotpProvisioningUri.Build(
            issuer: "Contoso",
            accountName: accountEmail,
            secret: secret);

        // Persist `secret` (encrypted at rest, e.g. via ISymmetricEncryptionService) against the
        // user's account, then render `provisioningUri` as a QR code for the user to scan.
        return (secret, provisioningUri);
    }
}
```

**Challenge — generating a code:**

```csharp
public sealed class TotpChallengeService(ITotpGenerator totpGenerator)
{
    public string CurrentCode(byte[] secret) => totpGenerator.GenerateCode(secret);
}
```

**Verification — end to end, with a consumer-supplied `ITotpReplayGuard`:**

> **BREAKING (P-514/WO-083):** `ITotpReplayGuard`'s prior two-step `HasBeenUsedAsync` (check) +
> `MarkUsedAsync` (mark) shape has been REMOVED. Two concurrent verification calls presenting the
> same valid code could both observe "not yet used" before either one marked it used, so both
> would pass — a textbook TOCTOU. It is replaced by one atomic member, `TryMarkUsedAsync`,
> returning `true` only when THIS call is the first to mark `(identityKey, code)` used and `false`
> when it was already marked (a replay). **Migration:** collapse any existing
> `HasBeenUsedAsync`/`MarkUsedAsync` pair into one atomic `TryMarkUsedAsync` — e.g. a single
> `ConcurrentDictionary<string, byte>.TryAdd` call (shown below), or a store's native atomic
> reservation primitive (Redis `SET NX PX`, SQL `INSERT ... ON CONFLICT DO NOTHING`). Never
> reimplement it as a separate check followed by a separate set — that reopens the same TOCTOU.
> `TotpVerifier.VerifyAsync` also gained new optional `digits`/`stepSeconds`/`driftWindow`/
> `algorithm` parameters, inserted before the existing trailing `ct` — a caller that passes `ct`
> *positionally* (not by name) will now bind it to `digits` instead and fail to compile; pass
> `ct` by name (`ct: ct`), as shown below.

```csharp
public sealed class InMemoryTotpReplayGuard : ITotpReplayGuard
{
    private readonly ConcurrentDictionary<string, byte> _used = new();

    public ValueTask<bool> TryMarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default)
    {
        // A real implementation persists to a store (e.g. distributed cache) with an expiry of
        // `validityWindow`, so the entry never grows unbounded, and relies on the store's own
        // atomic conditional-write primitive instead of `ConcurrentDictionary.TryAdd` — omitted
        // here for brevity.
        return ValueTask.FromResult(_used.TryAdd($"{identityKey}:{code}", 0));
    }
}

public sealed class TotpLoginStepUpHandler(TotpVerifier totpVerifier)
{
    public async Task<bool> VerifySecondFactorAsync(string userId, byte[] secret, string submittedCode, CancellationToken ct) =>
        await totpVerifier.VerifyAsync(userId, secret, submittedCode, ct: ct);
}
```

**Attempt throttling — RFC 4226 §7.3 rate limiting:**

A 6-digit code with a ±1-step drift window is otherwise brute-forceable, so a real deployment
should rate-limit verification attempts. `ITotpAttemptThrottle` is a standalone seam for this —
implemented by the consuming service (this package ships no default), and deliberately never
wired into `TotpVerifier`'s constructor. The caller composes it around `VerifyAsync`:

```csharp
public sealed class ThrottledTotpLoginHandler(TotpVerifier totpVerifier, ITotpAttemptThrottle attemptThrottle)
{
    public async Task<bool> VerifySecondFactorAsync(string userId, byte[] secret, string submittedCode, CancellationToken ct)
    {
        if (await attemptThrottle.IsThrottledAsync(userId, ct))
        {
            return false; // caller decides lockout response shaping (e.g. HTTP 429)
        }

        await attemptThrottle.RecordAttemptAsync(userId, ct);

        return await totpVerifier.VerifyAsync(userId, secret, submittedCode, ct: ct);
    }
}
```

**Recovery codes:**

```csharp
public sealed class RecoveryCodeIssuanceService(RecoveryCodeGenerator recoveryCodeGenerator, IOneWayHasher hasher)
{
    public (IReadOnlyList<string> PlaintextCodesToShowOnce, IReadOnlyList<string> HashesToPersist) IssueRecoveryCodes()
    {
        IReadOnlyList<string> codes = recoveryCodeGenerator.GenerateCodes();
        List<string> hashes = codes.Select(hasher.Hash).ToList();

        // Show `PlaintextCodesToShowOnce` to the user now — this is the only time the plaintext
        // is ever available. Persist only `HashesToPersist`.
        return (codes, hashes);
    }
}
```

## SharedKernel.Compression — Generic Payload Compression

`SharedKernel.Compression` provides generic compress/decompress of an arbitrary byte payload or stream via `IPayloadCompressor`. It is the direct sibling of `SharedKernel.Cryptography`'s `ISymmetricEncryptionService` — same shape, same zero-third-party-NuGet-dependency constraint (pure BCL `System.IO.Compression`), orthogonal concern. There is no `.Abstractions`/`.{Provider}` package split — a single package with a keyed-DI algorithm choice, mirroring `SharedKernel.Cryptography`'s RSA/ECDSA keyed-singleton precedent.

**Ordering rule: always compress, then encrypt — never the reverse.** Compressing already-encrypted/high-entropy ciphertext wastes CPU for no size benefit, since ciphertext has no redundancy left to compress. Never call `Compress` on a payload that has already passed through `ISymmetricEncryptionService.Encrypt`, and never call `Compress` a second time on an already-compressed payload — double-compression wastes CPU and typically *increases* output size.

### Registration

```csharp
// Program.cs
builder.Services.AddSharedKernelCompression(builder.Configuration);
```

Optional configuration (`SharedKernel:Compression` section, defaults to `CompressionLevel.Optimal`):

```json
{
  "SharedKernel": {
    "Compression": {
      "Level": "Optimal"
    }
  }
}
```

`AddSharedKernelCompression` registers `BrotliPayloadCompressor` as both the unkeyed `IPayloadCompressor` default and the `"Brotli"`-keyed singleton, and `GZipPayloadCompressor` only as the `"GZip"`-keyed singleton (mirroring `EcdsaSignatureService`'s keyed-only registration in `SharedKernel.Cryptography` — there is no unkeyed GZip registration).

### Brotli (default) — byte[] and stream usage

```csharp
public sealed class QueuePublishExample(IPayloadCompressor compressor)
{
    // byte[] overload — small in-memory payloads.
    public byte[] PrepareForQueue(byte[] jsonPayload) =>
        compressor.Compress(jsonPayload); // Brotli — best ratio for JSON/text-shaped payloads

    public Result<byte[]> RestoreFromQueue(byte[] received) =>
        compressor.Decompress(received); // Result<byte[]> — never throws on corrupt/truncated input

    // Stream overload — large payloads, never materializes the full content in memory.
    public async Task CompressUploadAsync(Stream sourceFile, Stream destination, CancellationToken ct) =>
        await compressor.CompressAsync(sourceFile, destination, ct);
}
```

Handling corrupt/truncated input via the railway pattern:

```csharp
Result<byte[]> decompressed = compressor.Decompress(received);

decompressed.Match(
    onSuccess: bytes => ProcessPayload(bytes),
    onFailure: error => logger.LogWarning(
        "Decompression failed: {Code} — {Message}", error.Code, error.Message));
// error.Code == CompressionErrorCodes.DecompressionFailed
```

### GZip — explicit keyed resolution

Use `GZipPayloadCompressor` only when interoperating with a system that specifically requires the gzip format. It is never the unkeyed default — resolve it explicitly by key:

```csharp
public sealed class LegacyInteropExample(
    [FromKeyedServices(CompressionServiceCollectionExtensions.GZipPayloadCompressorKey)]
        IPayloadCompressor gzipCompressor)
{
    public byte[] CompressForLegacySystem(byte[] payload) => gzipCompressor.Compress(payload);
}

// Resolving explicitly from IServiceProvider:
IPayloadCompressor brotli = provider.GetRequiredService<IPayloadCompressor>(); // unkeyed default = Brotli
IPayloadCompressor gzip = provider.GetRequiredKeyedService<IPayloadCompressor>(
    CompressionServiceCollectionExtensions.GZipPayloadCompressorKey);
```

### A note on truncation detection

`Decompress` never lets an unhandled exception escape — bit-level corruption and unrecognized/garbage input are always caught and mapped to a failed `Result`/`Result<byte[]>` for both algorithms. However, a compressed stream that is missing only its *trailing* bytes (a genuinely truncated upload or transfer that stopped early) is not always detected as an error by the underlying BCL implementations: neither `BrotliStream` nor `GZipStream` validates that the full originally-compressed length was reproduced, and `GZipStream` additionally does not validate its own trailing CRC32/ISIZE footer on read. `BrotliStream` in particular has no fixed magic-number header the way gzip does, so it can decode a truncated stream's remaining bytes without raising any error at all. This is a genuine, confirmed platform (BCL) characteristic, not a defect in this package. Services that must guarantee detection of a truncated transfer end-to-end should pair compression with a separate integrity check — e.g., `SharedKernel.Cryptography`'s `IContentHasher` over the original payload, or a known expected length — rather than relying solely on the compression format's own error signaling.

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

## SharedKernel.Localization — Culture-Keyed Message Catalog

`SharedKernel.Localization` ships `ILocalizationCatalog`, keyed on the same `code` string every `Error` factory in `SharedKernel.Primitives` already requires. It depends on `SharedKernel.Primitives` and the first-party `Microsoft.Extensions.Localization.Abstractions` NuGet package only — never a bespoke `.resx` pipeline.

### The fallback contract

**AN UNTRANSLATED ERROR MESSAGE FALLS BACK TO THE ORIGINAL THROW-SITE STRING, IT IS NEVER BLANK.** `ILocalizationCatalog.TryGetString` only ever returns `false`/`null` for an unregistered or untranslated `(code, culture)` pair — never throws for that outcome, never returns an empty string. Applying the throw-site-message fallback when a lookup misses is entirely the caller's responsibility — in practice `14.Presentation`'s `Error.ToProblemDetails()` (P-484, out of this package's jurisdiction). `01.Core.Primitives.Error` itself is completely unchanged by this package's existence — no new property, no breaking change to the platform's single most-depended-upon type.

```csharp
using SharedKernel.Localization;
using System.Globalization;

ILocalizationCatalog catalog = new InMemoryLocalizationCatalog()
    .AddTranslation("user.not_found", CultureInfo.GetCultureInfo("tr"), "Kullanıcı bulunamadı.");

string throwSiteMessage = "User not found.";
string resolvedMessage = catalog.TryGetString("user.not_found", CultureInfo.GetCultureInfo("tr-TR"), out string? translated)
    ? translated!
    : throwSiteMessage; // never blank — this is the fallback 14.Presentation applies
```

### InMemoryLocalizationCatalog — dictionary-backed default, with parent-culture fallback

`InMemoryLocalizationCatalog` is keyed by `(code, CultureInfo.Name)`, seeded via a chained `AddTranslation(...)` builder. A lookup for a specific culture (e.g. `tr-TR`) that has no exact entry falls back through each parent culture (`tr`) and finally `CultureInfo.InvariantCulture`, mirroring standard `ResourceManager`/`IStringLocalizer` resource-fallback behavior — the single most likely real-world case a bare `(code, CultureInfo.Name)` key alone leaves unspecified. Seed a translation under `CultureInfo.InvariantCulture` for a universal default reached by every culture with no more specific entry of its own. Code lookup is case-sensitive (ordinal), consistent with how `Error.Code` is compared everywhere else on the platform.

```csharp
var catalog = new InMemoryLocalizationCatalog()
    .AddTranslation("generic.error", CultureInfo.InvariantCulture, "Something went wrong.")
    .AddTranslation("generic.error", CultureInfo.GetCultureInfo("tr"), "Bir şeyler yanlış gitti.");

catalog.TryGetString("generic.error", CultureInfo.GetCultureInfo("tr-TR"), out string? v1); // "Bir şeyler yanlış gitti." — tr, not invariant
catalog.TryGetString("generic.error", CultureInfo.GetCultureInfo("fr-FR"), out string? v2); // "Something went wrong." — falls to invariant
```

### StringLocalizerLocalizationCatalog — composing with `.resx` tooling

Wraps a caller-supplied `IStringLocalizerFactory` so a service with full `.resx` tooling composes behind the same seam. It never blindly forwards `LocalizedString.Value` — a missing resource key returns a `LocalizedString` whose `Value` falls back to the key itself with `ResourceNotFound = true`; `TryGetString` checks `ResourceNotFound` first and returns `false`/`null` whenever it is `true`, rather than surfacing the raw error code as if it were a translation.

```csharp
using Microsoft.Extensions.Localization;
using SharedKernel.Localization;

public sealed class ErrorMessages; // marker type — matches ErrorMessages.resx / ErrorMessages.tr.resx

services.AddLocalization(options => options.ResourcesPath = "Resources");
services.AddStringLocalizerCatalog<ErrorMessages>();
```

### Naming — deliberately not `AddSharedKernelLocalization`

Registration is `AddInMemoryLocalizationCatalog(...)` / `AddStringLocalizerCatalog<TResource>()` — never `AddSharedKernelLocalization()`, which name is reserved for `13.ServiceDefaults`'s culture-*resolution* middleware entry point (P-483, out of this package's jurisdiction): resolving which culture a request is in, not looking up a translated message for an already-known `(code, culture)` pair.

See [`SharedKernel.Localization`'s own README](SharedKernel.Localization/README.md) for the full usage guide.

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
       +──► SharedKernel.Localization  (ILocalizationCatalog; also pulls in the first-party Microsoft.Extensions.Localization.Abstractions package)
```

All twelve packages can be referenced independently. Downstream packages in the SharedKernel ecosystem reference `SharedKernel.Primitives` as the minimum baseline and add the others as needed. `SharedKernel.Validation.FluentValidation`, `SharedKernel.Cryptography.KeyVault.Azure`, `SharedKernel.Cryptography.Argon2`, and `SharedKernel.Localization` are the four exceptions to "zero third-party NuGet dependencies" in this domain: `.FluentValidation` depends on `SharedKernel.Validation` plus the third-party `FluentValidation` package, deliberately kept out of `SharedKernel.Validation` itself so a FluentValidation-free consumer never pulls it in transitively; `.KeyVault.Azure` depends on `SharedKernel.Cryptography` plus the third-party `Azure.Security.KeyVault.Keys`/`Azure.Identity` packages, deliberately kept out of `SharedKernel.Cryptography` itself for the identical reason; `.Argon2` depends on `SharedKernel.Cryptography` plus the third-party `Konscious.Security.Cryptography.Argon2` package (a pure-managed implementation, no native/P-Invoke binding), deliberately kept out of `SharedKernel.Cryptography` itself for the identical reason again; `SharedKernel.Localization` depends on the first-party (not third-party) `Microsoft.Extensions.Localization.Abstractions` package — a deliberate exception to the zero-dependency default because it is the platform's own vendor's abstraction, not an external one, and the alternative (a bespoke resx pipeline) was explicitly rejected.
