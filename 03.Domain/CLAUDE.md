# 03.Domain — DDD Building Blocks

## What This Domain Is

The DDD primitives layer. Every aggregate, entity, value object, and domain event in a downstream microservice derives from the abstractions defined here. This domain may only reference `01.Core` — it must never reference infrastructure, persistence, or messaging layers.

Philosophy: **Pure domain model. No side effects. No I/O. AOT-preferred. Railway-friendly.**

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Domain` | All DDD building blocks: identity contracts, aggregate/entity bases, value object base, domain event contract, business rules, policies, specifications, auditable bases, strongly-typed ID base | `SharedKernel.Primitives` |

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Domain primitives | Pure C# 13 — zero NuGet dependencies |
| Domain events | Pure C# 13 — zero NuGet dependencies |
| Time sourcing | `IClock` from `SharedKernel.Primitives` only |
| Validation errors | `Error` and `ValidationException` from `SharedKernel.Primitives` |
| Base exception | `SharedKernelException` from `SharedKernel.Primitives` |

---

## Interface Contracts

### `SharedKernel.Domain` — public surface

#### Core identity interfaces (`Abstractions/`)

```
IEntity<TId>  where TId : notnull
    — marker: type has a typed, non-null identity key

IAggregateRoot<TId>  extends IEntity<TId>
    .DomainEvents                                           → IReadOnlyCollection<IDomainEvent>
    .ClearDomainEvents()                                    → void

IValueObject
    — zero-member marker: structural equality semantics

IDomainService
    — zero-member marker: domain logic not belonging to a single aggregate

IStronglyTypedId<TValue>  where TValue : notnull
    .Value                                                  → TValue
```

#### Domain event contracts (`Events/`)

```
IDomainEvent
    .Id                                                     → Guid  (event identity)
    .OccurredOn                                             → DateTimeOffset

DomainEvent  (abstract record, implements IDomainEvent)
    .Id                                                     → Guid  (= Guid.NewGuid() at construction)
    .OccurredOn                                             → DateTimeOffset  (init-only — must be supplied by caller)
```

#### Audit and cross-cutting marker interfaces (`Abstractions/`)

```
IHasCreatedAudit
    .CreatedBy                                              → string
    .CreatedOn                                              → DateTimeOffset

IHasAudit  extends IHasCreatedAudit
    .ModifiedBy                                             → string?
    .ModifiedOn                                             → DateTimeOffset?

ISoftDeletable
    .IsDeleted                                              → bool
    .DeletedOn                                              → DateTimeOffset?
    .DeletedBy                                              → string?

IHasConcurrency
    .RowVersion                                             → byte[]

IHasTenant
    .TenantId                                               → Guid
```

#### Entity base (`Entities/`)

```
Entity<TId>  (abstract class, implements IEntity<TId>)
    .Id                                                     → TId  (private init)
    .IsTransient()                                          → bool  (true when Id == default(TId))
    .Equals(object? obj)                                    → bool  (identity: GetType() + EqualityComparer<TId>.Default)
    .GetHashCode()                                          → int   (RuntimeHelpers.GetHashCode for transient instances)
    operator == / operator !=
```

#### AggregateRoot base (`Aggregates/`)

```
AggregateRoot<TId>  (abstract class, extends Entity<TId>, implements IAggregateRoot<TId>)
    .DomainEvents                                           → IReadOnlyCollection<IDomainEvent>
    .ClearDomainEvents()                                    → void
    protected .RaiseDomainEvent(IDomainEvent domainEvent)   → void
    protected .RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent> factory) → void  (IClock-sourced)
    protected static .CheckRule(IBusinessRule rule)         → void  (throws BusinessRuleViolationException if broken)
```

#### Auditable aggregate bases (`Aggregates/`)

```
AuditableAggregateRoot<TId>  extends AggregateRoot<TId>, implements IHasAudit
    — audit fields: private set (populated by EF Core interceptors)

SoftDeletableAggregateRoot<TId>  extends AggregateRoot<TId>, implements ISoftDeletable
    protected abstract .OnDelete()                          → void  (concrete aggregate raises domain event here)
    protected .MarkAsDeleted(string deletedBy)              → void  (sets soft-delete fields)

AuditableSoftDeletableAggregateRoot<TId>  extends AggregateRoot<TId>, implements IHasAudit + ISoftDeletable
    — all audit and soft-delete fields: private set

FullAuditableAggregateRoot<TId>  extends AggregateRoot<TId>, implements IHasAudit + ISoftDeletable + IHasConcurrency
    — audit and soft-delete fields: private set; RowVersion: protected set
```

#### Auditable entity bases (`Entities/`)

```
AuditableEntity<TId>  extends Entity<TId>, implements IHasAudit
    — no event machinery; audit fields: private set

FullAuditableEntity<TId>  extends Entity<TId>, implements IHasAudit + ISoftDeletable + IHasConcurrency
    — no event machinery; all fields: private set; RowVersion: protected set
```

#### Strongly-typed ID base (`StronglyTypedIds/`)

```
StronglyTypedId<TValue>  (abstract record, implements IStronglyTypedId<TValue>)  where TValue : notnull
    .Value                                                  → TValue  (required positional)
    .ToString()                                             → string  (= Value.ToString()!)
    implicit operator TValue
    NOTE: STJ serialization requires a custom JsonConverter in the consuming service — this package ships none.
```

#### ValueObject base (`ValueObjects/`)

```
ValueObject  (abstract class, implements IValueObject)
    protected abstract .GetEqualityComponents()             → IEnumerable<object?>
    protected abstract .Validate()                          → IEnumerable<Error>?  (null = valid; non-null throws ValidationException)
    .Equals(object? obj)                                    → bool  (all components equal)
    .GetHashCode()                                          → int
```

#### Business rule system (`BusinessRules/`, `Exceptions/`)

```
IBusinessRule
    .Message                                                → string
    .IsBroken()                                             → bool

BusinessRuleViolationException  extends SharedKernelException
    .Rule                                                   → IBusinessRule

AndBusinessRule  (sealed)   — broken if either sub-rule is broken; Message aggregates with "; "
OrBusinessRule   (sealed)   — broken only if both sub-rules are broken
NotBusinessRule  (sealed)   — inverts broken state

BusinessRuleExtensions
    .And(this IBusinessRule, IBusinessRule)                 → AndBusinessRule
    .Or(this IBusinessRule, IBusinessRule)                  → OrBusinessRule
    .Not(this IBusinessRule)                                → NotBusinessRule
```

#### Policy system (`Policies/`)

```
IPolicy<T>
    .IsCompliant(T subject)                                 → bool

AndPolicy<T>  (sealed)   — compliant only when both sub-policies are compliant
OrPolicy<T>   (sealed)   — compliant when at least one sub-policy is compliant
NotPolicy<T>  (sealed)   — inverts compliance

PolicyExtensions
    .And<T>(this IPolicy<T>, IPolicy<T>)                   → AndPolicy<T>
    .Or<T>(this IPolicy<T>, IPolicy<T>)                    → OrPolicy<T>
    .Not<T>(this IPolicy<T>)                               → NotPolicy<T>
```

#### Specification system (`Specifications/`)

```
ISpecification<T>
    .Criteria                                               → Expression<Func<T, bool>>?
    .Includes                                               → IReadOnlyList<Expression<Func<T, object>>>
    .OrderBy                                                → Expression<Func<T, object>>?
    .OrderByDescending                                      → Expression<Func<T, object>>?
    .ThenBys                                                → IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)>
    .Skip                                                   → int?
    .Take                                                   → int?
    .IsDistinct                                             → bool

Specification<T>  (abstract class, implements ISpecification<T>)
    protected .AddCriteria(Expression<Func<T, bool>>)       → void
    protected .AddInclude(Expression<Func<T, object>>)      → void
    protected .ApplyOrderBy(Expression<Func<T, object>>)    → void
    protected .ApplyOrderByDescending(Expression<Func<T, object>>) → void
    protected .ApplyThenBy(Expression<Func<T, object>>, bool descending) → void
    protected .ApplyPaging(int skip, int take)              → void
    protected .ApplyDistinct()                              → void

AndSpecification<T>  — combines two specs via ExpressionVisitor ParameterReplacer (logical AND)
OrSpecification<T>   — combines via logical OR
NotSpecification<T>  — negates via Expression.Not

SpecificationExtensions
    .And<T>(this Specification<T>, Specification<T>)        → AndSpecification<T>
    .Or<T>(this Specification<T>, Specification<T>)         → OrSpecification<T>
    .Not<T>(this Specification<T>)                          → NotSpecification<T>
```

---

## Implementation Rules

- `SharedKernel.Domain` has **zero NuGet dependencies** — references only `SharedKernel.Primitives`.
- `Entity<TId>` equality is **identity-based** — two entities are equal if and only if `GetType()` matches and `Id` values are equal via `EqualityComparer<TId>.Default`.
- **Transient entities** (`IsTransient() == true`) are never equal to any other instance including themselves — use `RuntimeHelpers.GetHashCode` for their hash.
- `ValueObject` equality is **structural** — the `abstract class + GetEqualityComponents()` pattern is the chosen strategy (not abstract record positional equality) because: (a) multi-component equality does not always map to positional records, (b) the `Validate()` constructor hook requires explicit constructor control.
- `AggregateRoot` is the **only** type permitted to call `RaiseDomainEvent` — plain entities must not accumulate events.
- `DomainEvents` on `AggregateRoot` is **cleared by infrastructure** after successful dispatch — aggregates must never call `ClearDomainEvents()` on themselves.
- `IDomainEvent.OccurredOn` must be supplied by the aggregate's `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` overload using `_clock.UtcNow`. Direct `DateTime.UtcNow` / `DateTimeOffset.UtcNow` usage anywhere in this package is a **hard violation**.
- `NullClock` internal sentinel (returns `DateTimeOffset.MinValue`) is assigned to `_clock` in the protected parameterless ORM-path constructor of `AggregateRoot<TId>` — prevents NPE during ORM hydration.
- `ClearDomainEvents()` is called **only by infrastructure dispatch code** — never by an aggregate or application service.
- Audit properties on all auditable bases (`CreatedBy`, `CreatedOn`, `ModifiedBy`, `ModifiedOn`, `IsDeleted`, `DeletedOn`, `DeletedBy`) have `private set` — populated exclusively by EF Core interceptors or persistence-layer conventions, never by the aggregate.
- `RowVersion` on `FullAuditableAggregateRoot<TId>` and `FullAuditableEntity<TId>` has `protected set` to allow persistence layer population after fetch.
- `IDomainEventHandler<TEvent>` is **explicitly excluded** from this package — it belongs in `05.Application`. The domain layer has no knowledge of handler dispatch.
- `IHasTenant` is a marker only — the domain layer has no tenant resolution logic.
- `StronglyTypedId<TValue>` does not ship a STJ `JsonConverter` — consuming services must provide their own in their serialization context (e.g., `04.Contracts` or `06.Persistence`).
- No static mutable state anywhere in this domain.
- No persistence concerns (`DbContext`, repository interfaces, EF annotations) — those live in `06.Persistence`.
- No messaging concerns (`IMessageBus`, `IEventPublisher`) — those live in `07.Messaging`.
- Specifications built in concrete subclass constructors only — no fluent builder calls outside the constructor.

---

## Equality Strategy Decision Record

**ValueObject:** Abstract class with `GetEqualityComponents() → IEnumerable<object?>`.

Rejected alternative: Abstract record relying on positional equality.

Reasons for rejection:
1. Value objects often have components that do not map cleanly to record positional properties (e.g., computed or derived components).
2. The `Validate()` constructor hook requires an explicit constructor body, which abstract records handle less cleanly (primary constructor does not support `this()` chaining in the same way).
3. Abstract class gives complete control over `Equals`/`GetHashCode` without relying on record-generated implementations that may include unwanted members.

---

## DI Registration (expected shape)

`SharedKernel.Domain` ships **no DI extensions** — it is a pure library with no runtime services to register.

---

## AOT Compatibility

- `Entity<TId>`, `AggregateRoot<TId>`, and all auditable bases are abstract classes — no reflection, AOT-safe.
- `ValueObject` uses `IEnumerable<object?>` from `GetEqualityComponents()` — implementors returning simple values or primitives are AOT-safe.
- `IDomainEvent` is a marker interface — AOT-safe.
- `DomainEvent` is an abstract record — sealed concrete records in consuming services are AOT-safe by default.
- `StronglyTypedId<TValue>` is an abstract record — `implicit operator TValue` is a static method, AOT-safe.
- Specification expression trees (`Expression<Func<T, bool>>`) are AOT-safe when the expressions do not involve runtime-only reflection APIs.
- `ExpressionVisitor` / `ParameterReplacer` in specification composites: AOT-safe as these operate on already-compiled expression trees with no runtime type discovery.
- No `Activator.CreateInstance`, no `Assembly.Load`, no reflection in hot paths.

---

## Test Rules

- Unit tests for this package live in `03.Domain/SharedKernel.Domain/SharedKernel.Domain.Tests/`.
- Entity equality: same Id = equal; different Id = not equal; different concrete type same Id = not equal; transient = not equal to anything including itself.
- Aggregate tests: event raised appears in `DomainEvents`; `ClearDomainEvents` empties the list; events do not leak across instances; `RaiseDomainEvent(Func<...>)` uses clock from injected `IClock`.
- ValueObject tests: structural equality across all components; changed component breaks equality; `Validate()` returning errors throws `ValidationException`.
- BusinessRule composites: `And`, `Or`, `Not` — verify correct broken/non-broken logic and message aggregation.
- Policy composites: `And`, `Or`, `Not` — verify correct compliance logic.
- Specification composition: compiled expression trees evaluate correctly against in-memory collections.
- `StronglyTypedId`: implicit operator unwraps; `ToString()` delegates to inner value.
- `CheckRule`: broken rule throws `BusinessRuleViolationException` carrying the rule; non-broken rule does not throw.

---

## Changelog

> Maintained by the domain agent. One line per significant change.

- [2026-05-22] Domain brain initialized — packages, interfaces, rules, AOT notes
- [2026-05-22] P-032 (WO-008) — full public surface documented: all interfaces, abstract bases, auditable hierarchy, ValueObject strategy decision, business rules, policies, specifications, strongly-typed IDs, NullClock sentinel, IDomainEventHandler exclusion boundary
