# SharedKernel.Domain

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Third-party dependencies: 0](https://img.shields.io/badge/third--party%20dependencies-0-brightgreen)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**Domain-driven design building blocks for .NET services: aggregates, value objects, strongly-typed
identifiers, business rules, specifications, policies and money.**

The package is pure domain code. It performs no I/O, never reads the system clock, and has no
dependency on persistence, messaging or dependency injection.

| You get | So that |
| --- | --- |
| Aggregates that read time from an injected `IClock` | Events and timestamps are testable and never silently wrong |
| Value objects that report **every** validation error | A form shows all its problems at once |
| `TryCreate` returning `ValidationResult<T>` | Invalid input is a result, not an exception |
| Business rules with a stable error `Code` | Clients branch on codes; localization looks messages up by them |
| Strongly-typed identifiers | An `OrderId` can never be passed where a `CustomerId` is expected |
| Specifications with an inline builder and typed `ThenInclude` | Queries are named, reusable and composable; paging stays at the call site |
| `Money` with ISO 4217 minor units | Rounding, allocation and currency mismatches are handled once, correctly |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Which type do I need?](#which-type-do-i-need)
- [Walkthrough: an order domain](#walkthrough-an-order-domain)
- [Reference](#reference)
  - [Namespaces](#namespaces)
  - [Aggregates and entities](#aggregates-and-entities)
  - [Business rules](#business-rules)
  - [TryCreate](#trycreate)
  - [Value objects](#value-objects)
  - [Strongly-typed identifiers](#strongly-typed-identifiers)
  - [Specifications](#specifications)
  - [Policies](#policies)
  - [Money](#money)
  - [Error codes](#error-codes)
- [Pitfalls](#pitfalls)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)
- [Deliberately not included](#deliberately-not-included)

## Install

```shell
dotnet add package SharedKernel.Domain
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | `SharedKernel.Primitives` and `SharedKernel.Core` only |
| Registration | None: everything is a base class, an interface or a static method |

## Quick start

```csharp
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

public sealed record InvoiceId(Guid Value) : StronglyTypedId<Guid>(Value);

[DomainEventVersion(1)]
public sealed record InvoiceIssued(InvoiceId InvoiceId, Money Amount) : DomainEvent;

public sealed class AmountMustBePositive(Money amount) : IBusinessRule
{
    public string Code => "invoice.amount_not_positive";
    public string Message => "An invoice amount must be greater than zero.";
    public bool IsBroken() => !amount.IsPositive;
}

public sealed class Invoice : AggregateRoot<InvoiceId>
{
    private Invoice(InvoiceId id, Money amount, IClock clock) : base(id, clock)
    {
        CheckRule(new AmountMustBePositive(amount));
        Amount = amount;
        RaiseDomainEvent(at => new InvoiceIssued(id, amount) { OccurredOn = at });
    }

    private Invoice() { } // ORM

    public Money Amount { get; private set; } = null!;

    public static ValidationResult<Invoice> Issue(InvoiceId id, Money amount, IClock clock) =>
        TryCreate(() => new Invoice(id, amount, clock));
}
```

```csharp
var result = Invoice.Issue(new InvoiceId(Guid.CreateVersion7()), Money.Zero(Currency.Usd), clock);

result.IsValid;          // false
result.Errors[0].Code;   // "invoice.amount_not_positive"
```

## Which type do I need?

| I need to model… | Use | Namespace |
| --- | --- | --- |
| A consistency boundary that is loaded and saved as a whole | `AggregateRoot<TId>` or one of its [audit, soft-delete and tenant bases](#base-classes) | `Aggregates` |
| A thing with identity that lives inside an aggregate | `Entity<TId>` or one of its bases | `Entities` |
| A concept defined only by its values (address, date range) | `ValueObject` | `ValueObjects` |
| A concept that wraps exactly one value (quantity, email) | `SingleValueObject<TValue>` | `ValueObjects` |
| An identifier that cannot be confused with another | `StronglyTypedId<TValue>` | `StronglyTypedIds` |
| Something that happened, for other parts of the system to react to | `DomainEvent` with `[DomainEventVersion(n)]` | `Events` |
| An invariant the aggregate must never break | `IBusinessRule` + `CheckRule` | `BusinessRules` |
| A decision about any subject, with an explanation ("is this customer eligible?") | `IPolicy<T>` | `Policies` |
| A reusable, named query | `Specification<T>` | `Specifications` |
| A one-off query built inline | `Spec.For<T>()` | `Specifications` |
| A query that projects to a DTO | `ProjectionSpecification<T, TResult>` or `Spec.For<T>()...Select(...)` | `Specifications` |
| Logic that spans several aggregates and belongs to none | `DomainService` | `DomainServices` |
| Creation that needs collaborators (uniqueness check, ID generator) | A class implementing `IAggregateFactory<TAggregate, TId>` | `Abstractions` |
| A monetary amount | `Money` and `Currency` | `Monetary` |

The difference between the three decision types is the question they answer:

| Type | Answers | Bound to |
| --- | --- | --- |
| `IBusinessRule` | "Is this invariant broken?" | The data it was constructed with |
| `IPolicy<T>` | "Does this subject comply, and if not, why?" | Any subject passed in |
| `Specification<T>` | "Which rows match?" | A query, translated by the persistence layer |

## Walkthrough: an order domain

Eight steps build a small, complete ordering model. Every snippet compiles against the package.

### 1. Identity

```csharp
public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
public sealed record CustomerId(Guid Value) : StronglyTypedId<Guid>(Value);
```

### 2. Value objects

A single-value wrapper only implements `Validate()`; the base class calls `EnsureValid()` for you.

```csharp
public sealed class Quantity : SingleValueObject<int>
{
    public Quantity(int value) : base(value) { }

    protected override IEnumerable<Error> Validate()
    {
        if (Value is < 1 or > 999)
            yield return Error.Validation("quantity.out_of_range", "Quantity must be between 1 and 999.");
    }
}
```

A multi-value object assigns every member, then calls `EnsureValid()` as the constructor's last statement.

```csharp
public sealed class ShippingAddress : ValueObject
{
    private ShippingAddress(string countryCode, string city, string street)
    {
        CountryCode = countryCode;
        City = city;
        Street = street;
        EnsureValid(); // always the last statement
    }

    public string CountryCode { get; }
    public string City { get; }
    public string Street { get; }

    public static ValidationResult<ShippingAddress> Create(string countryCode, string city, string street) =>
        TryCreate(() => new ShippingAddress(countryCode, city, street));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CountryCode;
        yield return City;
        yield return Street;
    }

    protected override IEnumerable<Error> Validate()
    {
        if (CountryCode is not { Length: 2 })
            yield return Error.Validation("address.country_code", "Use a two-letter ISO 3166 country code.");
        if (string.IsNullOrWhiteSpace(City))
            yield return Error.Validation("address.city_required", "City is required.");
        if (string.IsNullOrWhiteSpace(Street))
            yield return Error.Validation("address.street_required", "Street is required.");
    }
}
```

### 3. A child entity

```csharp
public sealed class OrderLine : Entity<Guid>
{
    public OrderLine(Guid id, string sku, Quantity quantity, Money unitPrice) : base(id)
    {
        Sku = sku;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    private OrderLine() { } // ORM

    public string Sku { get; private set; } = string.Empty;
    public Quantity Quantity { get; private set; } = null!;
    public Money UnitPrice { get; private set; } = null!;
    public Money Total => UnitPrice * Quantity.Value;
}
```

### 4. Rules and events

```csharp
public sealed class OrderMustHaveLines(int lineCount) : IBusinessRule
{
    public string Code => "order.no_lines";
    public string Message => "An order needs at least one line.";
    public bool IsBroken() => lineCount == 0;
}

public sealed class OrderMustNotBeShipped(DateTimeOffset? shippedOn) : IBusinessRule
{
    public string Code => "order.already_shipped";
    public string Message => "The order has already been shipped.";
    public bool IsBroken() => shippedOn is not null;
}

[DomainEventVersion(1)]
public sealed record OrderPlaced(OrderId OrderId, Money Total) : DomainEvent;

[DomainEventVersion(1)]
public sealed record OrderShipped(OrderId OrderId) : DomainEvent;
```

### 5. The aggregate

```csharp
public sealed class Order : TenantedAuditableAggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    private Order(
        OrderId id, Guid tenantId, CustomerId customerId, ShippingAddress shipTo,
        IReadOnlyList<OrderLine> lines, IClock clock)
        : base(id, tenantId, clock)
    {
        CheckRule(new OrderMustHaveLines(lines.Count));

        CustomerId = customerId;
        ShipTo = shipTo;
        _lines.AddRange(lines);
        Total = lines.Select(line => line.Total).Sum();

        RaiseDomainEvent(at => new OrderPlaced(id, Total) { OccurredOn = at });
    }

    private Order() { } // ORM

    public CustomerId CustomerId { get; private set; } = null!;
    public ShippingAddress ShipTo { get; private set; } = null!;
    public Money Total { get; private set; } = null!;
    public DateTimeOffset? ShippedOn { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;

    public static ValidationResult<Order> Place(
        OrderId id, Guid tenantId, CustomerId customerId, ShippingAddress shipTo,
        IReadOnlyList<OrderLine> lines, IClock clock) =>
        TryCreate(() => new Order(id, tenantId, customerId, shipTo, lines, clock));

    public void Ship()
    {
        CheckRule(new OrderMustNotBeShipped(ShippedOn));
        ShippedOn = Now;
        RaiseDomainEvent(at => new OrderShipped(Id) { OccurredOn = at });
    }
}
```

What the base class gives this aggregate: `TenantId` (never `Guid.Empty`), `CreatedBy`/`CreatedOn`/`ModifiedBy`/`ModifiedOn`
filled by persistence, `DomainEvents`, and `Version`, the event sequence number.

### 6. Queries

```csharp
public sealed class OrdersOfCustomer : Specification<Order>
{
    public OrdersOfCustomer(Guid tenantId, CustomerId customerId)
    {
        AddCriteria(order => order.TenantId == tenantId);
        AddCriteria(order => order.CustomerId == customerId); // combined with AND
        AddInclude(order => order.Lines); // continue a path with .ThenInclude(line => line.Nav)
        ApplyOrderByDescending(order => order.CreatedOn);
        ApplyThenBy(order => order.Id, descending: true);
    }
}

// The same thing inline, for a one-off query:
var spec = Spec.For<Order>()
    .Where(order => order.CustomerId == customerId)
    .Include(order => order.Lines)
    .OrderByDescending(order => order.CreatedOn)
    .ThenByDescending(order => order.Id);
```

Paging is decided at the call site, not in the specification: the persistence repositories take a
`PageRequest` (offset pages) or a `CursorPageRequest` plus a key selector (keyset pages) from
`SharedKernel.Contracts`.

### 7. A policy

```csharp
public sealed class FreeShippingPolicy : IPolicy<Order>
{
    private static readonly Money Threshold = Money.Create(50m, Currency.Usd).Value;

    public bool IsCompliant(Order order) =>
        order.Total.Currency == Threshold.Currency && order.Total >= Threshold;

    public string Explain(Order order) =>
        IsCompliant(order) ? string.Empty : "Free shipping starts at 50.00 USD.";
}
```

### 8. Use it

```csharp
var address = ShippingAddress.Create("Turkey", "", "Bagdat Cd. 1");
// address.IsValid == false, with two errors:
//   address.country_code: Use a two-letter ISO 3166 country code.
//   address.city_required: City is required.

var price = Money.Create(19.99m, Currency.Usd).Value;
var lines = new[] { new OrderLine(Guid.CreateVersion7(), "SKU-1", new Quantity(3), price) };

var placed = Order.Place(orderId, tenantId, customerId, shipTo, lines, clock);
var order = placed.Value;           // Total 59.97 USD, 1 event, Version 1

order.Ship();                       // 2 events, Version 2
order.Ship();                       // throws BusinessRuleViolationException, Rule.Code "order.already_shipped"

var empty = Order.Place(orderId, tenantId, customerId, shipTo, [], clock);
// empty.IsValid == false, Errors[0].Code "order.no_lines", Errors[0].Type ErrorType.BusinessRule
```

### Persisting and loading

With [`SharedKernel.Persistence.EfCore`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/SharedKernel.Persistence.EfCore/README.md)
the infrastructure concerns of this model are handled for you:

| Concern | Handled by |
| --- | --- |
| Filling `CreatedBy`, `CreatedOn`, `ModifiedBy`, `ModifiedOn` | The audit interceptor, on save |
| Attaching the clock to an aggregate the database loads | The clock materialization interceptor, on load |
| Mapping `Version` so event numbering continues after a reload | The entity type configuration base |
| Dispatching `DomainEvents` after the save succeeds, then clearing them | The unit of work, through `IDomainEventDispatcher` |
| Tenant isolation | `TenantedDbContext`'s global query filter |

```csharp
var order = await orders.GetByIdAsync(orderId, ct); // clock attached as the row is materialized
order!.Ship();
await unitOfWork.SaveChangesAsync(ct);              // OrderShipped is dispatched after the commit
```

## Reference

### Namespaces

All namespaces start with `SharedKernel.Domain.`

| Namespace | Contains |
| --- | --- |
| `Abstractions` | `IEntity<TId>`, `IAggregateRoot<TId>`, `IHasDomainEvents`, `IHasClock`, `IHasVersion`, `IHasAudit`, `IHasCreatedAudit`, `ISoftDeletable`, `IHasTenant`, `IHasConcurrency`, `IHasAggregateId<TId>`, `IStronglyTypedId<TValue>`, `IDomainEventDispatcher`, markers `IAggregateFactory<,>`, `IDomainService`, `IValueObject` |
| `Aggregates` | `AggregateRoot<TId>` and its audit, soft-delete and tenant bases |
| `Entities` | `Entity<TId>` and its audit and soft-delete bases |
| `Events` | `IDomainEvent`, `DomainEvent`, `DomainEvent<TPayload>`, `DomainEventVersionAttribute`, `DomainEventVersionHelper` |
| `BusinessRules` | `IBusinessRule`, `AndBusinessRule`, `OrBusinessRule`, `NotBusinessRule`, `BusinessRuleExtensions` |
| `ValueObjects` | `ValueObject`, `SingleValueObject<TValue>` |
| `StronglyTypedIds` | `StronglyTypedId<TValue>`; `Serialization.StronglyTypedIdJsonConverterFactory` |
| `Specifications` | `Specification<T>`, `Spec.For<T>()`/`SpecificationBuilder<T>`, `ProjectionSpecification<T, TResult>`, `AllSpecification<T>`, `EmptySpecification<T>`, composites and `SpecificationExtensions` |
| `Policies` | `IPolicy<T>`, `AndPolicy<T>`, `OrPolicy<T>`, `NotPolicy<T>`, `PolicyExtensions` |
| `DomainServices` | `DomainService` |
| `Monetary` | `Money`, `Currency`, `CurrencyCatalog`, `RoundingPolicy`, `CurrencyMismatchRule`, `IExchangeRateProvider`, `MoneyExtensions` |
| `Exceptions` | `BusinessRuleViolationException`, `DomainNotFoundException` |

### Aggregates and entities

#### Time and events

- An aggregate reads time only from its `IClock`, through the protected `Now` property. Analyzer `SK0001`
  reports any direct `DateTime.UtcNow` or `DateTimeOffset.UtcNow`.
- Raise events with the factory overload so the timestamp is the clock's time when the event is recorded:
  `RaiseDomainEvent(at => new OrderShipped(Id) { OccurredOn = at })`.
- Each raised event advances `Version` by one. `Version` is the aggregate's **event sequence number**, not a
  concurrency token; consumers can use it to detect a missing or out-of-order event.
- Every `DomainEvent` gets a version 7 UUID as its `Id`. It is time-ordered and survives serialization, so
  deduplication by `Id` still works after an outbox round trip.
- Every concrete event declares `[DomainEventVersion(n)]`; analyzer `SK0009` reports a missing one.
- An aggregate never clears its own events. Infrastructure dispatches them after the save, then calls
  `ClearDomainEvents()`.

#### Loading: attaching the clock

An ORM creates an aggregate through its parameterless constructor, which cannot receive a clock. Until
infrastructure calls `IHasClock.AttachClock`, anything that reads the time throws:

```text
InvalidOperationException: Order has no clock. It was created through its parameterless constructor,
and infrastructure must call IHasClock.AttachClock before the aggregate reads the time.
```

`SharedKernel.Persistence.EfCore` attaches the clock as each aggregate is materialized. If you load aggregates
another way, call `((IHasClock)aggregate).AttachClock(clock)` yourself. Failing loudly is deliberate: the
alternative is an event stamped `0001-01-01`.

#### Base classes

| Base class | Adds |
| --- | --- |
| `AggregateRoot<TId>` | Events, rules, `TryCreate`, clock, `Version` |
| `AuditableAggregateRoot<TId>` | `CreatedBy`, `CreatedOn`, `ModifiedBy`, `ModifiedOn` |
| `SoftDeletableAggregateRoot<TId>` | `IsDeleted`, `DeletedOn`, `DeletedBy`, `MarkAsDeleted`, `OnDelete` |
| `AuditableSoftDeletableAggregateRoot<TId>` | Audit and soft delete |
| `FullAuditableAggregateRoot<TId>` | Audit, soft delete and a `RowVersion` concurrency token |

- Each aggregate base has a `Tenanted…` counterpart that adds `TenantId`: fixed at construction, never
  `Guid.Empty`.
- Entities mirror the non-tenanted set: `Entity<TId>`, `AuditableEntity<TId>`, `SoftDeletableEntity<TId>`,
  `AuditableSoftDeletableEntity<TId>`, `FullAuditableEntity<TId>`.
- Persistence reads only the interfaces (`IHasAudit`, `ISoftDeletable`, `IHasTenant`, `IHasConcurrency`). For a
  combination not listed, extend `AggregateRoot<TId>` and implement the interfaces.

#### Entity equality

Two entities are equal when they have the same concrete type and the same non-default `Id`. An entity whose
`Id` is still the default is **transient**: it equals only itself, and its hash code changes once the database
assigns its key.

#### Soft delete

```csharp
public sealed class Customer : SoftDeletableAggregateRoot<CustomerId>
{
    public void Close(string closedBy) => MarkAsDeleted(closedBy);

    protected override void OnDelete() =>
        RaiseDomainEvent(at => new CustomerClosed(Id) { OccurredOn = at });
}
```

| Case | Behaviour |
| --- | --- |
| First delete | Records the actor and the clock's time, then calls `OnDelete` once |
| Already deleted | Changes nothing and raises no second event |
| Blank actor | Throws `DomainException` |
| Entity (no clock) | `MarkAsDeleted(deletedBy, deletedOn)` takes the owning aggregate's time, which must be UTC |

### Business rules

A rule has a stable `Code`, a human-readable `Message` and `IsBroken()`. `CheckRule(rule)` throws
`BusinessRuleViolationException` when the rule is broken. Its error has type `ErrorType.BusinessRule`
(HTTP 422 at the API boundary) and the rule's own code.

| Composition | Broken when | Reports |
| --- | --- | --- |
| `a.And(b)` | Either operand is broken | The first broken operand's code; the broken operands' messages, separated by semicolons |
| `a.Or(b)` | Both operands are broken | `a`'s code; both messages |
| `a.Not(code, message)` | `a` holds | The code and message you supply |

`CheckRule` is available on `AggregateRoot<TId>`, `ValueObject` and `DomainService`.

### TryCreate

Aggregates and value objects expose a protected `TryCreate(() => new …)` that returns `ValidationResult<T>`.

| The constructor throws | Result |
| --- | --- |
| Nothing | Valid, holding the object |
| `ValidationException` (from `EnsureValid`) | Invalid, holding **every** error |
| `BusinessRuleViolationException` | Invalid, holding the rule's error |
| Any other `DomainException`, including every `Guard.Throw` violation | Invalid, holding its error |
| Anything else | Rethrown: it is a defect, not invalid input |

Construction stops at the first throw, so a result holds one error unless a value object reports several.
Reading `Value` on an invalid result throws `InvalidOperationException`; check `IsValid` first.

### Value objects

| Rule | Detail |
| --- | --- |
| Validation | Assign every member, then call `EnsureValid()` last. It runs `Validate()` and throws `ValidationException` with all errors. |
| The base constructor | Never validates: a virtual call from it would run before your members are assigned |
| Forgetting `EnsureValid()` | The object is never validated. Analyzer `SK0037` reports it at build time. |
| `SingleValueObject<TValue>` | Calls `EnsureValid()` itself; a subclass only implements `Validate()`. A null value throws `DomainException` (`validation.required`). |
| Equality | Same runtime type and equal components. A sequence component (other than `string`) compares element by element. |
| Conversion | Explicit only: `(int)quantity` or `quantity.Value` |

### Strongly-typed identifiers

```csharp
public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);

Guid raw = orderId.Value;   // or (Guid)orderId
```

There is no implicit conversion, so an identifier never flows silently into a parameter that expects another
concept's `Guid`. Register the converter factory once and every identifier serializes as its bare value,
including as a dictionary key:

```csharp
var options = new JsonSerializerOptions();
options.Converters.Add(new StronglyTypedIdJsonConverterFactory());

JsonSerializer.Serialize(new { order.Id }, options); // {"Id":"01a0a4d5-b229-7b13-a618-37a291ff0bcf"}
```

The factory supports any `TValue` that System.Text.Json can serialize.

### Specifications

| Member | Behaviour |
| --- | --- |
| `AddCriteria` | Each call is combined with AND |
| `ApplyOrderBy` / `ApplyOrderByDescending` | One primary sort; a second call throws. Add keys with `ApplyThenBy`, which throws without a primary sort. |
| `ApplyPaging(skip, take)`, `ApplyTake(take)` | Fixed windows only ("the ten most recent"); page-by-page access takes a page request at the repository |
| `AddInclude(...).ThenInclude(...)`, `AddStringInclude` | Eager loading; `ThenInclude` is type-checked through collections |
| `ApplySplitQuery`, `ApplyDistinct` | Query-shape flags for the evaluator; tracking is decided by the repository |
| `IncludeSoftDeleted()` | Returns soft-deleted rows too; tenant isolation stays |
| `IsSatisfiedBy(entity)` | Evaluates the criteria in memory |
| `Specification<T>.Create(criteria)` | An ad hoc specification without a subclass |
| `a.And(b)`, `a.Or(b)`, `a.Not()` | Combine criteria, includes and flags; carry the ordering of the one operand that has it; **throw** when both operands are ordered or either pages. `Not` of a specification without criteria matches nothing. |
| `AllSpecification<T>`, `EmptySpecification<T>` | Match everything or nothing |

### Policies

```csharp
if (!policy.IsCompliant(order))
    return Error.BusinessRule("shipping.not_free", policy.Explain(order));

// Or enforce it as an invariant inside an aggregate:
CheckRule(policy.ToRule(order, "shipping.not_free"));
```

- `Explain` returns an empty string when the subject complies.
- `a.And(b)` and `a.Or(b)` combine the explanations, separated by semicolons; `a.Not(explanation)` requires
  its own explanation.
- `IPolicy<in T>` is contravariant: a policy written for a base type can be used where a policy for a derived
  type is expected.

### Money

```csharp
var price = Money.Create(19.99m, Currency.Usd).Value;

var total = price * 3;                              // 59.97 USD
var split = total.Allocate(2);                      // 29.98 USD, 29.99 USD — adds up exactly
var net = total.Divide(1.2m, RoundingPolicy.Floor); // 49.97 USD
var sum = new[] { price, total }.Sum();             // 79.96 USD

total.ToString();                                           // "59.97 USD", invariant culture
total.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));  // "59,97 USD"
```

| Topic | Behaviour |
| --- | --- |
| Rounding | Always to the currency's minor unit. `RoundingPolicy`: `BankersRounding` (default), `AwayFromZero`, `ToZero`, `Ceiling`, `Floor`. |
| Arithmetic | `+`, `-`, `*`, `/`, `Negate`, `Abs`, `Multiply` and `Divide` with a rounding policy |
| Predicates | `IsZero`, `IsPositive`, `IsNegative`; comparison operators; `Min`, `Max` |
| Currency mismatch | `+`, `-`, comparisons, `Min`, `Max` and `Sum` throw `BusinessRuleViolationException` with code `money.currency_mismatch` |
| Allocation | `Allocate(parts)` or `Allocate(ratios)`, largest remainder: parts differ by at most one minor unit and always add up to the original |
| Sum | `amounts.Sum()` throws on an empty sequence; `amounts.Sum(currency)` returns zero in that currency |
| Conversion | `await money.ConvertAsync(target, rateProvider)` through your `IExchangeRateProvider`; returns `Result<Money>` |
| Currencies | `Currency.Create("try")` accepts any casing and returns `ValidationResult<Currency>`. `Currency.Usd`, `.Eur`, `.Gbp`, `.Jpy`, `.Try` are ready-made. `CurrencyCatalog` holds every active ISO 4217 currency as of `CurrencyCatalog.RegistryAsOf`. |

### Error codes

| Code | Raised by |
| --- | --- |
| The rule's own `Code` | `BusinessRuleViolationException` from `CheckRule` |
| `money.currency_mismatch` | Money operations across currencies |
| `currency.code.invalid_format` | `Currency.Create` with input that is not three letters |
| `currency.code.unknown` | `Currency.Create` with a code outside the catalog |
| `validation.required` | A null strongly-typed identifier or single value, and guard violations |
| `not_found.default` | `DomainNotFoundException` |

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Load aggregates with your own mapper and call a method straight away | Call `IHasClock.AttachClock` as each one is created | Without a clock, reading the time throws `InvalidOperationException` |
| Write a `ValueObject` constructor without `EnsureValid()` | End every constructor with `EnsureValid()` | Nothing else validates it; `SK0037` warns |
| Keep an unsaved entity in a `HashSet` or dictionary across the save | Add it after the save, or key by something else | Its hash code changes when the database assigns its key |
| Call `IncludeSoftDeleted()` and trust tenant isolation | Add `AddCriteria(x => x.TenantId == tenantId)` yourself | It bypasses every global query filter, tenancy included |
| Put ordering or paging on the operands of `And`/`Or`/`Not` | Build one specification subclass that calls `AddCriteria` repeatedly | Composites drop ordering, paging and `Distinct` |
| Remove an aggregate through the repository when others must react | Call a domain method such as `Close()` that raises an event | Repository deletion soft-deletes without a domain event |
| Rely on events being published without a dispatcher | Register an `IDomainEventDispatcher` (for example `AddSharedKernelApplication()`) | The unit of work clears events after the save even when nothing dispatched them |
| Compare or add `Money` of possibly different currencies | Check `Currency` first, or convert with `ConvertAsync` | Mismatches throw `BusinessRuleViolationException` |
| Call `.Sum()` on a possibly empty list of `Money` | Call `.Sum(currency)` | An empty sequence has no currency to return |
| Read `result.Value` without checking | Check `result.IsValid` first | `Value` on an invalid result throws |
| Read `DateTime.UtcNow` in domain code | Use `Now` or the event factory's timestamp | Time must come from the clock; `SK0001` reports it |
| Clear events inside the aggregate | Leave it to infrastructure | Clearing before dispatch loses them |

## AI quick reference

Conventions for generating code with this package. Each line is a rule.

```text
IDENTIFIER   public sealed record {Name}Id(Guid Value) : StronglyTypedId<Guid>(Value);
             Unwrap with .Value or an explicit cast. No implicit conversions exist.
AGGREGATE    Extend AggregateRoot<TId> or a base: Auditable-, SoftDeletable-, AuditableSoftDeletable-,
             FullAuditable-; prefix Tenanted- for multi-tenant (extra Guid tenantId constructor argument).
             Private constructor (id, [tenantId,] ..., IClock clock) : base(id, [tenantId,] clock).
             Private parameterless constructor for the ORM. Properties with private setters.
             Public static factory returning ValidationResult<TAggregate> => TryCreate(() => new ...).
             Enforce invariants with CheckRule(new SomeRule(...)) before assigning state.
             Read time only via Now. Never DateTime.UtcNow.
EVENT        [DomainEventVersion(1)] public sealed record {Past}(...) : DomainEvent;
             Raise inside the aggregate: RaiseDomainEvent(at => new {Past}(...) { OccurredOn = at });
RULE         public sealed class {Rule}(...) : IBusinessRule { Code "{context}.{snake_case}"; Message; IsBroken() }
             Code is required and stable. Compose with .And(), .Or(), .Not(code, message).
VALUE OBJECT Extend ValueObject: assign members, call EnsureValid() LAST in every constructor,
             implement GetEqualityComponents() and Validate() (yield Error.Validation(code, message)).
             Expose static Create(...) => TryCreate(() => new ...). Single value: extend SingleValueObject<T>
             with a public constructor : base(value) and Validate() only.
ENTITY       Extend Entity<TId>; constructor (TId id, ...) : base(id); private parameterless constructor.
SPECIFICATION Extend Specification<T>; call builders only in the constructor: AddCriteria (AND), one
             ApplyOrderBy/ApplyOrderByDescending, ApplyThenBy, AddInclude(...).ThenInclude(...). Inline: Spec.For<T>().
             Paging at the call site: ListPagedAsync(spec, PageRequest) / ListKeysetAsync(spec, CursorPageRequest, key).
POLICY       class : IPolicy<T> { bool IsCompliant(T); string Explain(T) => "" when compliant }.
             Inside an aggregate: CheckRule(policy.ToRule(subject, "code")).
MONEY        Money.Create(amount, Currency.Usd) returns ValidationResult<Money>. Never mix currencies.
             Split with Allocate, never by dividing and rounding each part. Sum empty lists with Sum(currency).
RESULTS      ValidationResult<T>: IsValid, Value (throws if invalid), Errors (IReadOnlyList<Error>).
JSON         options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
FORBIDDEN    I/O, DbContext, ILogger, DI, HttpClient or messaging types in domain code.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`; any change fails the build until
  it is recorded.
- **Every public member is documented**, including the exceptions it throws; the XML documentation ships in the
  package.
- **No infrastructure dependency.** The package references only `SharedKernel.Primitives` and
  `SharedKernel.Core`, performs no I/O, and never reads the system clock.
- **Build-time guardrails** in [`SharedKernel.Analyzers`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/00.Governance/SharedKernel.Analyzers/README.md):
  `SK0001` (direct clock access), `SK0009` (event without a version), `SK0010` (two primary sorts),
  `SK0037` (value object without `EnsureValid()`).

## Deliberately not included

- **No event handlers or dispatcher implementation.** Handlers and the MediatR dispatcher live in the
  application layer; this package defines `IDomainEventDispatcher` only.
- **No persistence.** Repositories, EF Core mappings and the clock-attaching interceptor live in the persistence
  layer; the domain never references it.
- **No implicit conversions** from identifiers or single-value objects to their underlying value.
- **No exchange rates.** `IExchangeRateProvider` is a port; your service supplies the rate source.
