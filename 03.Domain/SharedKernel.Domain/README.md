# SharedKernel.Domain

DDD building blocks for .NET 10 microservices. Provides abstract base classes and interfaces for entities, aggregate roots, value objects, domain events, business rules, policies, and specifications. References only `SharedKernel.Primitives` — zero infrastructure dependencies.

---

## Quick Start: Aggregate Root

```csharp
// 1. Define a strongly-typed ID
public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);

// 2. Define a domain event (sealed record, OccurredOn is required init)
public sealed record OrderCreatedEvent(Guid OrderId) : DomainEvent;

// 3. Implement the aggregate — inject IClock, raise events via the factory overload
public sealed class Order : AggregateRoot<OrderId>
{
    public string CustomerName { get; private set; }

    public Order(OrderId id, string customerName, IClock clock) : base(id, clock)
    {
        CustomerName = customerName;
        // Use the Func<DateTimeOffset, IDomainEvent> overload so the clock sources the timestamp
        RaiseDomainEvent(ts => new OrderCreatedEvent(id.Value) { OccurredOn = ts });
    }

    protected Order() { } // Required for ORM materialisation (EF Core)
}
```

After persisting, the infrastructure dispatch layer calls `order.ClearDomainEvents()`. The aggregate must never call this itself.

---

## Value Object with Validate() Hook

```csharp
public sealed class Coordinates : ValueObject
{
    public double Latitude { get; }
    public double Longitude { get; }

    public Coordinates(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
        // Base constructor calls Validate() automatically — do NOT call it here
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }

    // Return null (or empty) for valid; yield Error instances to fail construction
    protected override IEnumerable<Error>? Validate()
    {
        if (Latitude is < -90 or > 90)
            yield return Error.Validation("Coordinates.InvalidLatitude", "Latitude must be between -90 and 90.");
        if (Longitude is < -180 or > 180)
            yield return Error.Validation("Coordinates.InvalidLongitude", "Longitude must be between -180 and 180.");
    }
}

// Usage — throws ValidationException if invariants are violated
var origin       = new Coordinates(0, 0);
var sanFrancisco = new Coordinates(37.7749, -122.4194);
Console.WriteLine(origin == sanFrancisco); // false — structural equality on Latitude + Longitude
```

---

## Money — Currency-Aware Monetary Value Object

`Money` (`ValueObjects/Money/`) is a shipped, production-ready value object — not a pattern to
reimplement per service. It pairs a `decimal` amount with a validated ISO 4217 `Currency`, rounds
unconditionally to the currency's minor-unit precision (`RoundingPolicy.BankersRounding` by
default), and rejects cross-currency arithmetic via the existing business-rule pipeline.

```csharp
// Create — validates the currency and unconditionally rounds the amount to its minor-unit
// precision (2 places for USD). Returns Result<Money>, never throws on excess precision.
Result<Money> priceResult = Money.Create(19.995m, Currency.Usd);
if (priceResult.IsFailure)
    throw new InvalidOperationException(priceResult.Error.Message);

Money price    = priceResult.Value;                    // 20.00 USD (rounded via BankersRounding)
Money shipping = Money.Create(4.99m, Currency.Usd).Value;

// Arithmetic — same-currency only. A mismatched currency throws BusinessRuleViolationException
// (ErrorType.BusinessRule -> HTTP 422), carrying a CurrencyMismatchRule.
Money total      = price + shipping;                   // 24.99 USD
Money discounted = total * 0.9m;                        // 22.49 USD — Multiply re-rounds the product

// Comparison
if (total > Money.Zero(Currency.Usd))
    Console.WriteLine("Order total is positive.");

// Allocate — largest-remainder (Hare-Niemeyer) split that conserves the total exactly.
IReadOnlyList<Money> splitThreeWays = Money.Create(10.00m, Currency.Usd).Value.Allocate(3);
// [3.33, 3.33, 3.34] USD — never [3.33, 3.33, 3.33] (loses a cent) or [3.34, 3.34, 3.34] (invents two)

IReadOnlyList<Money> weightedSplit = total.Allocate(ratios: [2, 1, 1]); // 50% / 25% / 25%

// Cross-currency conversion — SharedKernel.Domain ships only the IExchangeRateProvider port and
// the ConvertAsync composition helper; the consuming service supplies the real rate lookup
// (typically bridged via 11.Communication) at its own composition root.
Result<Money> converted = await price.ConvertAsync(
    Currency.Eur, rateProvider, cancellationToken: cancellationToken);
```

```csharp
// A minimal test/demo IExchangeRateProvider — production code bridges to a real rate source.
public sealed class FixedRateProvider(decimal rate) : IExchangeRateProvider
{
    public Task<Result<decimal>> GetExchangeRateAsync(
        Currency source, Currency target, CancellationToken cancellationToken) =>
        Task.FromResult(Result<decimal>.Success(rate));
}
```

`Currency.Create("try")` normalizes casing/whitespace and validates against a fixed, compile-time
ISO 4217 catalog (`CurrencyCatalog`) — correctly distinguishing zero-decimal currencies (e.g. JPY)
and three-decimal currencies (e.g. BHD) from the 2-digit default. `Currency.Usd`/`.Eur`/`.Gbp`/`.Jpy`
are DX-convenience statics only, not an exhaustive currency list — use `Create` for any other code.

---

## Specification Pattern

```csharp
// Define a specification in its constructor using the protected builder methods
public sealed class ActiveOrdersSpec : Specification<Order>
{
    public ActiveOrdersSpec()
    {
        AddCriteria(o => !o.IsDeleted && o.Status == OrderStatus.Active);
        ApplyOrderByDescending(o => o.CreatedOn);
        ApplyPaging(skip: 0, take: 20);
    }
}

public sealed class LargeOrdersSpec : Specification<Order>
{
    public LargeOrdersSpec(decimal threshold)
    {
        AddCriteria(o => o.Total > threshold);
    }
}

// Compose specifications with And / Or / Not extension methods
var spec = new ActiveOrdersSpec().And(new LargeOrdersSpec(1000m));

// Pass to a repository (06.Persistence) — the spec encodes what; persistence handles how
var orders = await repository.ListAsync(spec, cancellationToken);
```

---

## Business Rule Composition

```csharp
// Implement a rule as a small, focused class
public sealed class OrderMustNotBeEmpty : IBusinessRule
{
    private readonly int _lineCount;
    public OrderMustNotBeEmpty(int lineCount) => _lineCount = lineCount;
    public string Message => "An order must contain at least one line item.";
    public bool IsBroken() => _lineCount == 0;
}

public sealed class OrderTotalMustBePositive : IBusinessRule
{
    private readonly decimal _total;
    public OrderTotalMustBePositive(decimal total) => _total = total;
    public string Message => "Order total must be greater than zero.";
    public bool IsBroken() => _total <= 0;
}

// Inside an aggregate method, enforce rules with CheckRule (throws BusinessRuleViolationException)
public void Submit()
{
    // Compose rules with .And() — both must pass
    CheckRule(new OrderMustNotBeEmpty(Lines.Count)
        .And(new OrderTotalMustBePositive(Total)));

    RaiseDomainEvent(ts => new OrderSubmittedEvent(Id.Value) { OccurredOn = ts });
}
```

Use `IBusinessRule` for primitive invariants (counts, amounts, strings) inside aggregates and value objects.
Use `IPolicy<T>` when the compliance check operates on a full domain object and may involve multiple collaborators.

---

## Policy vs Business Rule

| Concern | Use |
|---------|-----|
| Primitive invariant within an aggregate constructor or method | `IBusinessRule` + `CheckRule` |
| Compliance evaluation on a rich domain object (e.g., "is this order eligible for express shipping?") | `IPolicy<T>` |

```csharp
// Policy example — evaluates a full domain object
public sealed class EligibleForExpressShippingPolicy : IPolicy<Order>
{
    public bool IsCompliant(Order subject) =>
        subject.Total > 50m && subject.ShippingAddress.Country == "US";
}

// Compose policies
var policy = new EligibleForExpressShippingPolicy()
    .And(new OrderNotFlaggedForReviewPolicy());

if (policy.IsCompliant(order))
    order.ApplyExpressShipping();
```

---

## Domain Event Handler Location

`IDomainEventHandler<TEvent>` is **not in this package**. Domain event dispatch and handler registration live in `05.Application`. This package defines only the event contract (`IDomainEvent`, `DomainEvent`) and the raising mechanism on `AggregateRoot<TId>`.

---

## Package Rules

- Zero NuGet dependencies beyond `SharedKernel.Primitives`.
- `IClock` is the only permitted time source — `DateTimeOffset.UtcNow` is a hard violation.
- Audit properties (`CreatedBy`, `CreatedOn`, etc.) are populated by EF Core interceptors in `06.Persistence`, never by domain code.
- `ClearDomainEvents()` is called only by infrastructure after successful dispatch — never by an aggregate.
- STJ serialization of strongly-typed IDs requires a custom `JsonConverter` in the consuming service — this package ships none.
- `Money`/`Currency` perform no I/O — `IExchangeRateProvider` is a pure port; the consuming service supplies the real rate-lookup implementation, never this package.
