# 01.Core

Foundational building blocks for the Platform.SharedKernel ecosystem. Six independently publishable NuGet packages with zero infrastructure dependencies.

| Package | Purpose |
|---------|---------|
| `SharedKernel.Primitives` | `Result<T>`, `Error`, `IClock`, `SmartEnum`, `ValidationResult` |
| `SharedKernel.Core` | Base exceptions, railway extensions, BCL helpers |
| `SharedKernel.Configuration` | `AddValidatedOptions` startup-validation pattern |
| `SharedKernel.FeatureManagement` | `IFeatureManager` abstraction over Microsoft.FeatureManagement |
| `SharedKernel.Guards` | Two-path guard system: `Guard.Against.*` (functional) + `Guard.Throw.*` (imperative) |
| `SharedKernel.Cryptography` | Password hashing, AES-256-GCM symmetric encryption, RSA/ECDSA + HMAC signing, secure random/token generation |

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

// Sentinel — no error
Error none = Error.None;

// Discriminating by type
string response = error.Type switch
{
    ErrorType.NotFound     => "404 Not Found",
    ErrorType.Validation   => "400 Bad Request",
    ErrorType.Unauthorized => "401 Unauthorized",
    ErrorType.Conflict     => "409 Conflict",
    _                      => "500 Internal Server Error",
};
```

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

### Symmetric Encryption (AES-256-GCM)

`ISymmetricEncryptionService` is for general-purpose encryption of arbitrary payloads outside an EF Core column — before publishing to a queue, writing to blob storage, or returning from an API. It is distinct from `06.Persistence`'s `EncryptedValueConverter`, which remains the dedicated path for transparent EF Core column-level encryption.

```csharp
// Consuming service supplies key material — SharedKernel.Cryptography holds none of its own.
public sealed class MyKeyVaultBackedKeyProvider : IEncryptionKeyProvider
{
    public CryptographicKey GetCurrentKey() =>
        new("key-v2", LoadKeyMaterialFromVault("key-v2")); // 32 bytes for AES-256

    public CryptographicKey? GetKey(string keyId) =>
        TryLoadKeyMaterialFromVault(keyId, out byte[] material) ? new(keyId, material) : null;
}

public sealed class PayloadEncryptionExample(ISymmetricEncryptionService encryption)
{
    public EncryptedPayload EncryptForQueue(byte[] plaintext) =>
        encryption.Encrypt(plaintext); // fresh random nonce every call — never reused

    public Result<byte[]> DecryptFromQueue(EncryptedPayload payload) =>
        encryption.Decrypt(payload); // Result<byte[]> — never throws CryptographicException directly

    // Convenience string overloads for simple cases (e.g., a connection string or API token):
    public string EncryptSecret(string plaintext) => encryption.EncryptToString(plaintext);

    public Result<string> DecryptSecret(string encoded) => encryption.DecryptToString(encoded);
}
```

Handling tamper/wrong-key failures via the railway pattern:

```csharp
Result<byte[]> decrypted = encryption.Decrypt(payload);

decrypted.Match(
    onSuccess: plaintext => ProcessPlaintext(plaintext),
    onFailure: error => logger.LogWarning(
        "Decryption failed: {Code} — {Message}", error.Code, error.Message));
// error.Code is one of CryptographyErrorCodes.DecryptionFailed, .UnknownKeyId, or .MalformedPayload
```

`ISymmetricEncryptionService` always uses an AEAD cipher (AES-GCM) — never an unauthenticated mode such as CBC/ECB.

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

---

## Dependency Graph

```
SharedKernel.Primitives              (no dependencies)
       |
       +──► SharedKernel.Core           (BCL extensions, railway extensions, exceptions)
       |       |
       |       +──► SharedKernel.Guards  (Guard.Against / Guard.Throw two-path guard system)
       |
       +──► SharedKernel.Configuration  (Options pattern + startup validation)
       |       |
       |       +──► SharedKernel.Cryptography  (one-way hashing, AES-GCM, RSA/ECDSA, HMAC, secure random)
       |
       +──► SharedKernel.FeatureManagement  (IFeatureManager + Microsoft.FeatureManagement adapter)
```

All six packages can be referenced independently. Downstream packages in the SharedKernel ecosystem reference `SharedKernel.Primitives` as the minimum baseline and add the others as needed.
