# SharedKernel.Domain

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Third-party dependencies: 0](https://img.shields.io/badge/third--party%20dependencies-0-brightgreen)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**Domain-driven design building blocks for .NET services.**

- **Entities and aggregate roots:** identity equality, domain events stamped from an injected clock, and business-rule enforcement.
- **Value objects:** structural equality with explicit validation that reports every error, plus single-value wrappers.
- **Strongly-typed identifiers:** `OrderId` instead of `Guid`, with one-line JSON support.
- **Audit, soft-delete and tenant bases:** the properties your persistence layer fills in.
- **Specifications:** query filters with composition, offset paging and keyset paging.
- **Policies:** reusable domain decisions that explain themselves.
- **Money:** a currency-aware amount with ISO 4217 minor units, rounding policies and loss-free allocation.

## Contents

- [Install](#install)
- [At a glance](#at-a-glance)
- [Namespaces](#namespaces)
- [Aggregates and entities](#aggregates-and-entities)
  - [Time and domain events](#time-and-domain-events)
  - [Loading an aggregate: attaching the clock](#loading-an-aggregate-attaching-the-clock)
  - [Base classes](#base-classes)
  - [Soft delete](#soft-delete)
- [Business rules](#business-rules)
- [Creating objects without exceptions: TryCreate](#creating-objects-without-exceptions-trycreate)
- [Value objects](#value-objects)
- [Strongly-typed identifiers](#strongly-typed-identifiers)
- [Specifications](#specifications)
- [Policies](#policies)
- [Money](#money)
- [Error codes](#error-codes)
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
| Registration | None. Everything is a base class, an interface or a static method. |

## At a glance

```csharp
public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);

[DomainEventVersion(1)]
public sealed record OrderPlaced(Guid OrderId, Money Total) : DomainEvent;

public sealed class OrderMustHaveLines(int lineCount) : IBusinessRule
{
    public string Code => "order.no_lines";
    public string Message => "An order needs at least one line.";
    public bool IsBroken() => lineCount == 0;
}

public sealed class Order : TenantedAggregateRoot<OrderId>
{
    private Order(OrderId id, Guid tenantId, Money total, int lineCount, IClock clock)
        : base(id, tenantId, clock)
    {
        CheckRule(new OrderMustHaveLines(lineCount));
        Total = total;
        RaiseDomainEvent(at => new OrderPlaced(id.Value, total) { OccurredOn = at });
    }

    private Order() { } // ORM

    public Money Total { get; private set; } = null!;

    public static ValidationResult<Order> Place(OrderId id, Guid tenantId, Money total, int lineCount, IClock clock) =>
        TryCreate(() => new Order(id, tenantId, total, lineCount, clock));
}
```

`Order.Place` never throws for invalid input. A broken rule, a guard violation such as an empty tenant, or a
failed value-object validation becomes a failed `ValidationResult<Order>`. Construction stops at the first
failure, so a result carries one error unless a value object reports several from `EnsureValid`.

## Namespaces

| Namespace | Contains |
| --- | --- |
| `SharedKernel.Domain.Abstractions` | `IEntity<TId>`, `IAggregateRoot<TId>`, `IHasDomainEvents`, `IHasClock`, `IHasVersion`, `IHasAudit`, `ISoftDeletable`, `IHasTenant`, `IHasConcurrency`, `IDomainEventDispatcher`, markers |
| `SharedKernel.Domain.Aggregates` | `AggregateRoot<TId>` and the audit, soft-delete and tenant bases |
| `SharedKernel.Domain.Entities` | `Entity<TId>` and the entity bases |
| `SharedKernel.Domain.Events` | `IDomainEvent`, `DomainEvent`, `DomainEvent<TPayload>`, versioning |
| `SharedKernel.Domain.BusinessRules` | `IBusinessRule` and its `And`/`Or`/`Not` composition |
| `SharedKernel.Domain.ValueObjects` | `ValueObject`, `SingleValueObject<TValue>` |
| `SharedKernel.Domain.StronglyTypedIds` | `StronglyTypedId<TValue>` and its JSON converter |
| `SharedKernel.Domain.Specifications` | `Specification<T>`, paged and keyset specifications |
| `SharedKernel.Domain.Policies` | `IPolicy<T>` and its composition |
| `SharedKernel.Domain.Monetary` | `Money`, `Currency`, `CurrencyCatalog`, `RoundingPolicy`, `IExchangeRateProvider` |
| `SharedKernel.Domain.Exceptions` | `BusinessRuleViolationException`, `DomainNotFoundException` |

## Aggregates and entities

### Time and domain events

An aggregate reads time only from the `IClock` it is constructed with, never from `DateTimeOffset.UtcNow`
(analyzer `SK0001` enforces it). Raise events through the factory overload, which passes the clock's time at
the moment the event is recorded:

```csharp
RaiseDomainEvent(at => new OrderShipped(Id.Value) { OccurredOn = at });
```

Each raised event advances `Version` by one, so `Version` is the aggregate's event sequence number. Map it as a
column so a loaded aggregate continues its numbering; consumers can then use it to detect a missing or
out-of-order event. Infrastructure
dispatches the pending events after the unit of work commits, then calls `ClearDomainEvents()`; an aggregate
never clears its own events.

Every `DomainEvent` gets a version 7 UUID as its `Id`, which is time-ordered and survives serialization, so
deduplication keyed on `Id` keeps working after an event passes through an outbox.

### Loading an aggregate: attaching the clock

An ORM creates an aggregate through its parameterless constructor, which cannot receive a clock. Until
infrastructure attaches one through `IHasClock.AttachClock`, anything that needs the time throws
`InvalidOperationException`:

```csharp
var order = await db.Orders.SingleAsync(o => o.Id == id);
order.Ship();   // throws: "Order has no clock ... AttachClock"
```

`SharedKernel.Persistence.EfCore` attaches the clock automatically as each aggregate is materialized. If you load
aggregates another way, call `AttachClock` yourself as each one is created. This exists so that a missing
clock fails loudly instead of recording `0001-01-01` as the time an event happened.

### Base classes

| Base class | Adds |
| --- | --- |
| `AggregateRoot<TId>` | Events, rules, clock |
| `AuditableAggregateRoot<TId>` | `CreatedBy`, `CreatedOn`, `ModifiedBy`, `ModifiedOn` |
| `SoftDeletableAggregateRoot<TId>` | `IsDeleted`, `DeletedOn`, `DeletedBy`, `MarkAsDeleted` |
| `AuditableSoftDeletableAggregateRoot<TId>` | Audit and soft delete |
| `FullAuditableAggregateRoot<TId>` | Audit, soft delete and a `RowVersion` concurrency token |

Each has a `Tenanted…` counterpart that adds a `TenantId`, which is fixed at construction and must not be
`Guid.Empty`. Child entities have `Entity<TId>`, `AuditableEntity<TId>`, `SoftDeletableEntity<TId>`,
`AuditableSoftDeletableEntity<TId>` and `FullAuditableEntity<TId>`.

The persistence layer fills audit and concurrency properties and reads only the interfaces, never the base
classes. For a combination not listed, extend `AggregateRoot<TId>` and implement the interfaces yourself.

**Entity equality** is by concrete type and `Id`. An entity whose `Id` is still the default is *transient*: it
is equal only to itself, so two unsaved entities stay distinct while each can still be removed from a
collection. Its hash code changes once the database assigns its key, so do not keep a transient entity in a
`HashSet` across that save.

### Soft delete

```csharp
public sealed class Customer : SoftDeletableAggregateRoot<CustomerId>
{
    public void Close(string closedBy) => MarkAsDeleted(closedBy);

    protected override void OnDelete() =>
        RaiseDomainEvent(at => new CustomerClosed(Id.Value) { OccurredOn = at });
}
```

`MarkAsDeleted` records the actor and the clock's time, then calls `OnDelete` once. Deleting an already
deleted aggregate changes nothing and raises no second event; an empty actor throws. Entities have no clock,
so their `MarkAsDeleted(deletedBy, deletedOn)` takes the owning aggregate's time, which must be UTC.

Removing an aggregate through a repository also soft-deletes it, but raises no domain event. Prefer a domain
method whenever other parts of the system must react.

## Business rules

A rule has a stable `Code`, a `Message` and `IsBroken()`. `CheckRule(rule)` throws
`BusinessRuleViolationException` when the rule is broken; its error has type `BusinessRule` (HTTP 422) and the
rule's own code, which clients branch on and localization looks messages up by.

| Composition | Broken when | Reports |
| --- | --- | --- |
| `a.And(b)` | Either is broken | The code of the first broken operand; the messages of all broken operands, joined with `; ` |
| `a.Or(b)` | Both are broken | `a`'s code; both messages |
| `a.Not(code, message)` | `a` holds | The code and message you supply |

## Creating objects without exceptions: TryCreate

Aggregates and value objects expose a protected `TryCreate(() => new …)`. It returns a `ValidationResult<T>`:

| What the constructor throws | Result |
| --- | --- |
| Nothing | Valid, holding the object |
| `ValidationException` (from `EnsureValid`) | Invalid, holding **every** error |
| `BusinessRuleViolationException` | Invalid, holding the rule's error |
| Any other `DomainException`, including every `Guard.Throw` violation | Invalid, holding its error |
| Anything else | Rethrown: it is a defect, not invalid input |

## Value objects

Assign every member in the constructor, then call `EnsureValid()` as the last statement. It runs `Validate()`
against the fully built object and throws a `ValidationException` with all errors:

```csharp
public sealed class DateRange : ValueObject
{
    private DateRange(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
        EnsureValid();
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }

    public static ValidationResult<DateRange> Create(DateOnly start, DateOnly end) =>
        TryCreate(() => new DateRange(start, end));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Start;
        yield return End;
    }

    protected override IEnumerable<Error> Validate()
    {
        if (End < Start)
            yield return Error.Validation("date_range.end_before_start", "The end must not be before the start.");
    }
}
```

The base class never validates by itself: a virtual call from its constructor would run before your constructor
assigned anything. A value object that never calls `EnsureValid()` is never validated; analyzer `SK0037` in
`SharedKernel.Analyzers` reports that at build time.

`SingleValueObject<TValue>` wraps one value and calls `EnsureValid()` for you, so a subclass only implements
`Validate()`. A sequence used as an equality component, other than a `string`, is compared element by element.

## Strongly-typed identifiers

```csharp
public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);

var id = new OrderId(Guid.CreateVersion7());
Guid raw = id.Value;       // or (Guid)id
```

There is no implicit conversion, so an `OrderId` can never flow silently into a parameter that expects another
concept's `Guid`. Register the converter factory once and every identifier serializes as its bare value,
including as a dictionary key:

```csharp
var options = new JsonSerializerOptions();
options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
// {"3f2b…": 5} for a Dictionary<OrderId, int>
```

## Specifications

```csharp
public sealed class ActiveOrders : Specification<Order>
{
    public ActiveOrders(Guid tenantId)
    {
        AddCriteria(o => !o.IsDeleted);
        AddCriteria(o => o.TenantId == tenantId);   // combined with AND
        ApplyOrderByDescending(o => o.CreatedOn);
    }
}

var spec = new ActiveOrders(tenantId).And(Specification<Order>.Create(o => o.Total.Amount > 100));
```

| Rule | Behaviour |
| --- | --- |
| `AddCriteria` twice | Conditions are combined with AND |
| A second primary sort | Throws; add further keys with `ApplyThenBy` |
| `And`, `Or`, `Not` | Carry over criteria, includes and the tracking, split-query and include-deleted flags; **not** ordering, paging or `Distinct` |
| `Not` of a specification without criteria | Matches nothing |
| `PagedSpecification<T>` | 1-based pages, page size up to 1000, rejects an offset beyond `int.MaxValue` |
| `KeysetSpecification<T, TKey>` | Seek paging; the `Id` tiebreak always sorts in the same direction as the key |
| `IncludeSoftDeleted()` | Bypasses **every** global query filter, including tenant isolation; re-add the tenant criterion |

## Policies

```csharp
public sealed class DiscountEligibility : IPolicy<Customer>
{
    public bool IsCompliant(Customer customer) => customer.OrderCount >= 5;

    public string Explain(Customer customer) =>
        IsCompliant(customer) ? string.Empty : "A discount needs at least five previous orders.";
}

if (!policy.IsCompliant(customer))
    return Error.BusinessRule("discount.not_eligible", policy.Explain(customer));

// Or enforce it inside an aggregate:
CheckRule(policy.ToRule(customer, "discount.not_eligible"));
```

Use a policy for a decision about any subject passed in, a rule for an invariant bound to its data, and a
specification for a query.

## Money

```csharp
var price = Money.Create(19.99m, Currency.Usd).Value;

var total = price * 3;                              // 59.97 USD
var perPerson = total.Allocate(2);                  // 29.98 USD, 29.99 USD — adds up exactly
var net = total.Divide(1.2m, RoundingPolicy.Floor); // 49.97 USD
var sum = new[] { price, total }.Sum();             // 79.96 USD

Console.WriteLine(total.ToString());                // "59.97 USD", invariant culture
Console.WriteLine(total.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))); // "59,97 USD"
```

| Behaviour | Detail |
| --- | --- |
| Rounding | Always to the currency's minor unit when created: `BankersRounding` (default), `AwayFromZero`, `ToZero`, `Ceiling`, `Floor` |
| Currency mismatch | `+`, `-`, comparison, `Min`, `Max` and `Sum` throw `BusinessRuleViolationException` with code `money.currency_mismatch` |
| Allocation | Largest-remainder: parts differ by at most one minor unit and always add up to the original |
| Conversion | `money.ConvertAsync(target, rateProvider)` through your `IExchangeRateProvider` |
| Currencies | `CurrencyCatalog` holds every active ISO 4217 transactional currency as of `CurrencyCatalog.RegistryAsOf`, with its minor units |

`Currency.Create("try")` accepts any casing and returns a `ValidationResult<Currency>`; `Currency.Usd`, `.Eur`,
`.Gbp`, `.Jpy` and `.Try` are ready-made instances.

## Error codes

| Code | Raised by |
| --- | --- |
| The rule's own `Code` | `BusinessRuleViolationException` |
| `money.currency_mismatch` | Money operations across currencies |
| `currency.code.invalid_format` | `Currency.Create` with input that is not three letters |
| `currency.code.unknown` | `Currency.Create` with a code outside the catalog |
| `validation.required` | A null strongly-typed identifier or single value; guard violations |
| `not_found.default` | `DomainNotFoundException` |

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`; any change fails the build until
  it is recorded.
- **Every public member is documented**, including the exceptions it throws; the XML documentation ships in the
  package.
- **No infrastructure dependency.** The package references only `SharedKernel.Primitives` and
  `SharedKernel.Core`, performs no I/O, and never reads the system clock.

## Deliberately not included

- **No event handlers or dispatcher implementation.** Handlers and the MediatR dispatcher live in the
  application layer; this package defines `IDomainEventDispatcher` only.
- **No persistence.** Repositories, EF Core mappings and the clock-attaching interceptor live in the persistence
  layer; the domain never references it.
- **No implicit conversions** from identifiers or single-value objects to their primitive, by design.
- **No exchange rates.** `IExchangeRateProvider` is a port; a service supplies the rate source.
