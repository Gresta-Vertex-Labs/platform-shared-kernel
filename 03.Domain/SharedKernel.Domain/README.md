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
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
        // Base constructor calls Validate() automatically — do NOT call it here
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    // Return null (or empty) for valid; yield Error instances to fail construction
    protected override IEnumerable<Error>? Validate()
    {
        if (Amount < 0)
            yield return Error.Validation("Money.NegativeAmount", "Amount must be non-negative.");
        if (string.IsNullOrWhiteSpace(Currency))
            yield return Error.Validation("Money.InvalidCurrency", "Currency code is required.");
    }
}

// Usage — throws ValidationException if invariants are violated
var price = new Money(9.99m, "USD");
var free  = new Money(0m, "USD");
Console.WriteLine(price == free); // false — structural equality on Amount + Currency
```

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
