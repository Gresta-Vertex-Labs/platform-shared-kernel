# 01.Core

Foundational building blocks for the Platform.SharedKernel ecosystem. Twelve independently publishable NuGet packages — every one but `SharedKernel.Validation.FluentValidation`, `SharedKernel.Cryptography.KeyVault.Azure`, and `SharedKernel.Localization` (a first-party Microsoft dependency, not a third-party one) has zero third-party NuGet dependencies.

| Package | Purpose |
|---------|---------|
| `SharedKernel.Primitives` | `Result<T>`, `Error`, `IClock`, `IIdGenerator`, `SmartEnum`, `ValidationResult` |
| `SharedKernel.Core` | Base exceptions, railway extensions, BCL helpers |
| `SharedKernel.Configuration` | `AddValidatedOptions` startup-validation pattern |
| `SharedKernel.FeatureManagement` | `IFeatureManager` abstraction over Microsoft.FeatureManagement |
| `SharedKernel.Guards` | Two-path guard system: `Guard.Against.*` (functional) + `Guard.Throw.*` (imperative) |
| `SharedKernel.Cryptography` | Password hashing, AES-256-GCM symmetric encryption, RSA/ECDSA + HMAC signing, secure random/token generation |
| `SharedKernel.Compression` | Generic payload compression (`IPayloadCompressor`): Brotli default, GZip keyed alternate |
| `SharedKernel.Validation` | Culture-independent IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT validators + pluggable national-ID registry |
| `SharedKernel.Validation.FluentValidation` | `IRuleBuilder<T,string>` adapter over `SharedKernel.Validation` (a third-party dependency — `FluentValidation`) |
| `SharedKernel.Cryptography.KeyVault.Azure` | Azure Key Vault Keys implementation of `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider` (a third-party dependency — `Azure.Security.KeyVault.Keys` + `Azure.Identity`) |
| `SharedKernel.DataPrivacy` | `DataClassificationAttribute`/`SensitiveDataCategoryAttribute` pure-metadata markers, `PiiMasking.*` deterministic masking helpers, `IDataSubjectRequestHandler` export/erasure contract |
| `SharedKernel.Localization` | `ILocalizationCatalog`, keyed on the same `code` string every `Error` factory requires (a first-party dependency — `Microsoft.Extensions.Localization.Abstractions`) |

All packages target `net10.0` and are AOT-compatible.

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

```csharp
// Default mapping: Error.Unexpected(ErrorCodes.Unexpected.Default, "{ExceptionType}: {ExceptionMessage}")
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

An `AggregateException` (e.g., caught from a `Task.Wait()`/`.Result`-style call) is flattened via `AggregateException.Flatten()` before the default message is built, so every inner exception's type and message is represented — not just the generic outer aggregate message. `ResultTry` never rethrows.

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

## SharedKernel.Guards — Guard Clauses

`SharedKernel.Guards` provides a two-path guard system for validating inputs and enforcing invariants. Every guard is available via both paths:

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

`IEncryptionKeyProvider` resolves the key material. Both of its members are asynchronous and `CancellationToken`-aware, so a genuine network-bound KMS/HSM implementation (Azure Key Vault, AWS KMS, HashiCorp Vault) never needs a blocking-on-async anti-pattern:

```csharp
// Consuming service supplies key material — SharedKernel.Cryptography holds none of its own.
// A synchronous/config-backed provider can still complete synchronously by returning an
// already-completed ValueTask, exactly like this one does:
public sealed class MyConfigBackedKeyProvider : IEncryptionKeyProvider
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
    public ValueTask<EncryptedPayload> EncryptForQueueAsync(byte[] plaintext, CancellationToken ct) =>
        encryption.EncryptAsync(plaintext, ct); // fresh random nonce every call — never reused

    public ValueTask<Result<byte[]>> DecryptFromQueueAsync(EncryptedPayload payload, CancellationToken ct) =>
        encryption.DecryptAsync(payload, ct); // Result<byte[]> — never throws CryptographicException directly

    // The synchronous members are retained for call sites that cannot easily become async
    // (e.g. a synchronous EF Core ValueConverter). They bridge onto the async key provider via
    // .GetAwaiter().GetResult() — GENUINELY NON-BLOCKING when the provider resolves
    // synchronously (as above, or a CachedEncryptionKeyProvider cache hit), but BLOCKS A REAL
    // THREAD when the provider is genuinely network-bound on a cache miss.
    public string EncryptSecret(string plaintext) => encryption.EncryptToString(plaintext);

    public Result<string> DecryptSecret(string encoded) => encryption.DecryptToString(encoded);
}
```

Handling tamper/wrong-key failures:

```csharp
Result<byte[]> decrypted = await encryption.DecryptAsync(payload, ct);

if (decrypted.IsSuccess)
{
    ProcessPlaintext(decrypted.Value);
}
else
{
    logger.LogWarning("Decryption failed: {Code} — {Message}", decrypted.Error.Code, decrypted.Error.Message);
}
// error.Code is one of CryptographyErrorCodes.DecryptionFailed, .UnknownKeyId, or .MalformedPayload
```

`ISymmetricEncryptionService` always uses an AEAD cipher (AES-GCM) — never an unauthenticated mode such as CBC/ECB.

#### Migrating a custom `IEncryptionKeyProvider` implementer (P-446/WO-068, breaking)

`IEncryptionKeyProvider`'s synchronous `GetCurrentKey()`/`GetKey(string)` members were **removed outright** — not kept as a parallel overload. Every implementer must migrate to the asynchronous shape:

| Before (removed) | After |
|---|---|
| `CryptographicKey GetCurrentKey()` | `ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)` |
| `CryptographicKey? GetKey(string keyId)` | `ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)` |

A synchronous/config-backed implementer migrates mechanically — wrap the existing return value in `new ValueTask<CryptographicKey>(...)` (or `new ValueTask<CryptographicKey?>(...)`), exactly as shown in `MyConfigBackedKeyProvider` above. No behavioral change is required for that class of implementer, and **config-supplied keys remain the fully-supported default requiring no consumer-side opt-in.** A genuinely network-bound implementer (a real KMS/HSM call) can now `await` its SDK call directly instead of blocking a thread.

`ISymmetricEncryptionService`'s own public surface did **not** break — `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString` are unchanged in signature and behavior; only their *internal* key resolution now goes through the async provider via a bridge. The four new `*Async` overloads are purely additive.

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

### Azure Key Vault Key Provider — `SharedKernel.Cryptography.KeyVault.Azure`

`AzureKeyVaultEncryptionKeyProvider` (from the sibling `SharedKernel.Cryptography.KeyVault.Azure` package) implements `IEncryptionKeyProvider`, `IEnvelopeEncryptionProvider`, and `IEncryptionKeyProviderProbe` against a real Azure Key Vault. It is the one `01.Core` package with a genuine third-party vendor SDK dependency (`Azure.Security.KeyVault.Keys` + `Azure.Identity`) — kept out of this package so `SharedKernel.Cryptography` itself stays dependency-free.

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

**Design decision — direct retrieval is built on envelope wrapping, not a second code path.** Azure Key Vault Keys does not export raw HSM-protected key material by default — the vendor-idiomatic operation is `CryptographyClient.WrapKeyAsync`/`UnwrapKeyAsync`, exactly `IEnvelopeEncryptionProvider`'s shape. `GetCurrentKeyAsync` therefore generates (or returns a process-lifetime-cached) local AES-256 data key via `GenerateDataKeyAsync`, exposing only the already-in-memory plaintext data key as `CryptographicKey.Material` — the vault's own master key material never crosses the process boundary either way, whether reached through `IEncryptionKeyProvider` or `IEnvelopeEncryptionProvider`. `GetKeyAsync` mirrors this by decoding the wrapped data key packed into the requested `keyId` string and unwrapping it via the same internal path. This is a deliberate, permanent design choice — see `AzureKeyVaultEncryptionKeyProvider`'s XML docs for the full reasoning; it must never be "fixed" into two divergent code paths.

**Fail-closed**, matching every other provider in this seam: any genuine Azure SDK exception (unreachable vault, `RequestFailedException` for permission/auth failure, or the vault itself rejecting a wrapped key as tampered) propagates directly — never a silent fallback. The one narrow exception is `UnwrapDataKeyAsync`'s `Result<byte[]>` failure path, returned only when the supplied `masterKeyId` fails *local* well-formedness validation (it is not a recognized Azure Key Vault key identifier URI) before any call ever reaches Azure.

**Ships zero caching of its own** beyond the single process-lifetime "current data key" slot needed to keep `CryptographicKey.Id` stable across repeated `GetCurrentKeyAsync` calls — it never applies a bounded TTL or re-resolves an already-unwrapped historical key. Compose `CachedEncryptionKeyProvider` externally, as shown above, if bounded-TTL caching is desired; two independent caches with different TTL semantics must never both wrap the same provider instance.

**Readiness probe** — `AzureKeyVaultEncryptionKeyProvider` also implements `IEncryptionKeyProviderProbe`. Unlike every other member of this class, `ProbeAsync` never lets an Azure SDK exception propagate: it performs one read-only key-metadata call (never a wrap/unwrap/sign/verify) and reports `EncryptionKeyProviderHealth.IsHealthy = false` with a `Description` instead of throwing. This mirrors `07.Messaging`'s `IMessageBusProbe`/`MessageBusHealth` shape. `01.Core` ships this probe primitive only, never an `IHealthCheck` — wiring it into `AddHealthChecks()` is `13.ServiceDefaults`'s concern.

```csharp
IEncryptionKeyProviderProbe probe = provider.GetRequiredService<IEncryptionKeyProviderProbe>();
EncryptionKeyProviderHealth health = await probe.ProbeAsync();
// health.IsHealthy / health.Description
```

### Asymmetric Signing (RSA / ECDSA)

`IAsymmetricSignatureService` has two implementations sharing one interface — `RsaSignatureService` and `EcdsaSignatureService`. Both are registered as **keyed singletons**; `RsaSignatureService` is additionally registered as the unkeyed default.

```csharp
// Consuming service supplies key material.
public sealed class MyCertificateStoreKeyProvider : IAsymmetricKeyProvider
{
    public RSA GetRsaKey(string keyId) => LoadRsaFromCertificateStore(keyId);
    public ECDsa GetEcdsaKey(string keyId) => LoadEcdsaFromCertificateStore(keyId);
}

public sealed class TokenSigningExample(
    [FromKeyedServices(CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey)]
        IAsymmetricSignatureService ecdsaSigner)
{
    public byte[] SignPayload(byte[] data) => ecdsaSigner.Sign(data, keyId: "signing-key-2026");

    public bool VerifyPayload(byte[] data, byte[] signature) =>
        ecdsaSigner.Verify(data, signature, keyId: "signing-key-2026");
}

// Resolving explicitly from IServiceProvider:
IAsymmetricSignatureService rsa = provider.GetRequiredService<IAsymmetricSignatureService>(); // unkeyed default = RSA
IAsymmetricSignatureService ecdsa = provider.GetRequiredKeyedService<IAsymmetricSignatureService>(
    CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey);
```

RSA uses 2048-bit minimum keys with PSS padding and SHA-256. ECDSA uses the P-256 curve with SHA-256.

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
inherently needs a backing store this dependency-free package cannot own.

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

```csharp
public sealed class InMemoryTotpReplayGuard : ITotpReplayGuard
{
    private readonly ConcurrentDictionary<string, byte> _used = new();

    public ValueTask<bool> HasBeenUsedAsync(string identityKey, string code, CancellationToken ct = default) =>
        ValueTask.FromResult(_used.ContainsKey($"{identityKey}:{code}"));

    public ValueTask MarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default)
    {
        // A real implementation persists to a store (e.g. distributed cache) with an expiry of
        // `validityWindow`, so the entry never grows unbounded — omitted here for brevity.
        _used[$"{identityKey}:{code}"] = 0;
        return ValueTask.CompletedTask;
    }
}

public sealed class TotpLoginStepUpHandler(TotpVerifier totpVerifier)
{
    public async Task<bool> VerifySecondFactorAsync(string userId, byte[] secret, string submittedCode, CancellationToken ct) =>
        await totpVerifier.VerifyAsync(userId, secret, submittedCode, ct);
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

`SharedKernel.Validation` provides culture-independent financial and identity format validators: IBAN (per-country length table + ISO 13616 mod-97 check digit), BIC/SWIFT, payment-card PAN (Luhn + card-network detection), ISO 4217 currency codes, ISO 3166-1 country codes, E.164 phone numbers, a baseline VAT/tax-identifier format check, and a pluggable per-country national-identity-number registry (`TckNationalIdValidator` — Turkey's TCKN — ships as the built-in default). Zero third-party NuGet dependencies. References `SharedKernel.Primitives` (for `Result`/`Error`) and `SharedKernel.Guards` (extending `Guard.Against` with new members via extension methods — `SharedKernel.Guards` itself is never modified).

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

**`Guard.Throw.*` parity is intentionally out of scope for this package.** `SharedKernel.Guards`' `Guard.Throw` nested class is a hand-enumerated static class hardcoded inside `SharedKernel.Guards` itself — a package outside `SharedKernel.Guards` cannot add a member to it without modifying that package, which is out of `SharedKernel.Validation`'s jurisdiction. Only the functional `Guard.Against.*` path is provided here.

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

`SharedKernel.DataPrivacy` ships three independent, composable pieces: a pure-metadata classification taxonomy, deterministic PII masking helpers, and the `IDataSubjectRequestHandler` GDPR/KVKK export/erasure contract. It depends on `SharedKernel.Primitives` only (for `Result<T>`/`Error` on the request-handler contract) — the same single-package, zero-third-party-dependency reasoning as `SharedKernel.Cryptography`/`.Compression`/`.Guards`.

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
       +──► SharedKernel.Core           (BCL extensions, railway extensions, exceptions)
       |       |
       |       +──► SharedKernel.Guards  (Guard.Against / Guard.Throw two-path guard system)
       |               |
       |               +──► SharedKernel.Validation  (IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT + national-ID registry)
       |                       |
       |                       +──► SharedKernel.Validation.FluentValidation  (IRuleBuilder<T,string> adapter; also pulls in the third-party FluentValidation package)
       |
       +──► SharedKernel.Configuration  (Options pattern + startup validation)
       |       |
       |       +──► SharedKernel.Cryptography  (one-way hashing, AES-GCM, RSA/ECDSA, HMAC, secure random)
       |       |       |
       |       |       +──► SharedKernel.Cryptography.KeyVault.Azure  (Azure Key Vault Keys provider; also pulls in the third-party Azure.Security.KeyVault.Keys + Azure.Identity packages)
       |       |
       |       +──► SharedKernel.Compression   (IPayloadCompressor: Brotli default, GZip keyed alternate)
       |
       +──► SharedKernel.FeatureManagement  (IFeatureManager + Microsoft.FeatureManagement adapter)
       |
       +──► SharedKernel.DataPrivacy  (DataClassification/SensitiveDataCategory attributes, PiiMasking, IDataSubjectRequestHandler)
       |
       +──► SharedKernel.Localization  (ILocalizationCatalog; also pulls in the first-party Microsoft.Extensions.Localization.Abstractions package)
```

All twelve packages can be referenced independently. Downstream packages in the SharedKernel ecosystem reference `SharedKernel.Primitives` as the minimum baseline and add the others as needed. `SharedKernel.Validation.FluentValidation`, `SharedKernel.Cryptography.KeyVault.Azure`, and `SharedKernel.Localization` are the three exceptions to "zero third-party NuGet dependencies" in this domain: `.FluentValidation` depends on `SharedKernel.Validation` plus the third-party `FluentValidation` package, deliberately kept out of `SharedKernel.Validation` itself so a FluentValidation-free consumer never pulls it in transitively; `.KeyVault.Azure` depends on `SharedKernel.Cryptography` plus the third-party `Azure.Security.KeyVault.Keys`/`Azure.Identity` packages, deliberately kept out of `SharedKernel.Cryptography` itself for the identical reason; `SharedKernel.Localization` depends on the first-party (not third-party) `Microsoft.Extensions.Localization.Abstractions` package — a deliberate exception to the zero-dependency default because it is the platform's own vendor's abstraction, not an external one, and the alternative (a bespoke resx pipeline) was explicitly rejected.
