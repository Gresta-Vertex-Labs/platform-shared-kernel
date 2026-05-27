# 03.Domain — DDD Building Blocks

## What This Domain Is

The DDD primitives layer. Every aggregate, entity, value object, and domain event in a downstream microservice derives from the abstractions defined here. This domain may only reference `01.Core` — it must never reference infrastructure, persistence, or messaging layers.

Philosophy: **Pure domain model. No side effects. No I/O. AOT-preferred. Railway-friendly.**

---

## Packages

| Package                 | Role                                                                                                                                                                                             | References                                      |
|-------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------|
| `SharedKernel.Domain`   | All DDD building blocks: identity contracts, aggregate/entity bases, value object base, domain event contract, business rules, policies, specifications, auditable bases, strongly-typed ID base | `SharedKernel.Primitives`, `SharedKernel.Core`  |

Both references are project references within the `01.Core` capability domain. `SharedKernel.Domain` has **zero external NuGet dependencies**.

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern            | Technology                                                                                           |
|--------------------|------------------------------------------------------------------------------------------------------|
| Domain primitives  | Pure C# 13 — zero external NuGet dependencies                                                        |
| Domain events      | Pure C# 13 — zero external NuGet dependencies                                                        |
| Time sourcing      | `IClock` from `SharedKernel.Primitives` only                                                         |
| Validation errors  | `Error` from `SharedKernel.Primitives`; `ValidationException` from `SharedKernel.Core.Exceptions`    |
| Base exception     | `SharedKernelException` from `SharedKernel.Core.Exceptions`                                          |
| Domain exceptions  | `DomainException` from `SharedKernel.Core.Exceptions` (intermediate between `SharedKernelException` and domain-specific exceptions); `BusinessRuleViolationException` and `DomainNotFoundException` both extend `DomainException` |

---

## Interface Contracts

### `SharedKernel.Domain` — public surface

#### Core identity interfaces (`Abstractions/`)

```text
IEntity<TId>  where TId : notnull
    — marker: type has a typed, non-null identity key

IHasDomainEvents
    .DomainEvents                                           → IReadOnlyCollection<IDomainEvent>
    .ClearDomainEvents()                                    → void
    NOTE: Infrastructure dispatch code must depend on IHasDomainEvents, not IAggregateRoot<TId>.
          Dependency on aggregate identity is not required for event dispatch.

IAggregateRoot<TId>  extends IEntity<TId>, IHasDomainEvents
    — all domain events members are inherited from IHasDomainEvents

IValueObject
    — zero-member marker: structural equality semantics

IDomainService
    — zero-member marker: domain logic not belonging to a single aggregate

IStronglyTypedId<TValue>  where TValue : notnull
    .Value                                                  → TValue

IHasVersion
    .Version                                                → int
    NOTE: Domain-native integer version counter. Increments on every raised domain event.
          Starts at 0. ClearDomainEvents() does not decrement it.
          Distinct from IHasConcurrency.RowVersion which is an infrastructure-specific SQL Server binary token.

IAggregateFactory<TAggregateRoot, TId>
    where TAggregateRoot : IAggregateRoot<TId>
    where TId : notnull
    — zero-member marker: tags a class as a factory for a specific aggregate root type
    NOTE: Implement this on static factory classes or dedicated factory services.
          Convention: expose a static Result<TAggregateRoot> Create(...) factory method.
```

#### Domain event contracts (`Events/`)

```text
IDomainEvent
    .Id                                                     → Guid  (event identity)
    .OccurredOn                                             → DateTimeOffset

DomainEvent  (abstract record, implements IDomainEvent)
    .Id                                                     → Guid  (= Guid.NewGuid() at construction)
    .OccurredOn                                             → DateTimeOffset  (init-only — must be supplied by caller)

DomainEvent<TPayload>  (abstract record, extends DomainEvent)  where TPayload : notnull
    .Payload                                                → TPayload  (required init — the domain-specific event data)
    NOTE: Non-generic DomainEvent is unchanged and not deprecated.
          Use DomainEvent<TPayload> when the event payload is also published as an integration event payload
          or when handlers need type-safe structured access to the event data.
    Example:
        public sealed record OrderPlacedEvent : DomainEvent<OrderPlacedPayload>;
        public sealed record OrderPlacedPayload(Guid OrderId, decimal Total);

DomainEventVersionAttribute  [AttributeUsage(Class, Inherited = false, AllowMultiple = false)]
    constructor: DomainEventVersionAttribute(int version)  — version >= 1; throws ArgumentOutOfRangeException for version < 1
    .Version                                               → int  (public get)
    NOTE: IDomainEvent interface is unchanged — no runtime Version property is added to it.
          Apply this attribute to concrete event classes to signal the schema version.
          Used by infrastructure (messaging, outbox) for backward-compatibility routing.

DomainEventVersionHelper  (static class)
    .GetVersion(Type domainEventType)                      → int
        Returns the Version from DomainEventVersionAttribute if present on domainEventType.
        Returns 1 as default when the attribute is absent.
    NOTE: Versioning workflow — declare [DomainEventVersion(2)] when a domain event schema changes
          in a backward-incompatible way. Consumers read the version via GetVersion() to route to the
          correct deserializer. Version 1 is implicit and requires no attribute.
```

#### Audit and cross-cutting marker interfaces (`Abstractions/`)

```text
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

```text
Entity<TId>  (abstract class, implements IEntity<TId>)
    .Id                                                     → TId  (private init)
    .IsTransient()                                          → bool  (true when Id == default(TId))
    .Equals(object? obj)                                    → bool  (identity: GetType() + EqualityComparer<TId>.Default)
    .GetHashCode()                                          → int   (RuntimeHelpers.GetHashCode for transient instances)
    operator == / operator !=
```

#### AggregateRoot base (`Aggregates/`)

```text
AggregateRoot<TId>  (abstract class, extends Entity<TId>, implements IAggregateRoot<TId>, IHasVersion)
    .DomainEvents                                           → IReadOnlyCollection<IDomainEvent>  (via IHasDomainEvents)
    .ClearDomainEvents()                                    → void  (via IHasDomainEvents — infrastructure dispatch only)
    .Version                                               → int  (private set; starts at 0; increments on every RaiseDomainEvent call)
    protected .Now                                          → DateTimeOffset  (IClock.UtcNow accessor — see usage constraint below)
    protected .RaiseDomainEvent(IDomainEvent domainEvent)   → void
    protected .RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent> factory) → void  (IClock-sourced — preferred for event timestamping)
    protected static .CheckRule(IBusinessRule rule)         → void  (throws BusinessRuleViolationException if broken)
    protected static .TryCreate<T>(Func<T> factory)        → Result<T>
        Catches BusinessRuleViolationException → Result.Failure(ex.Error)
        Catches ValidationException → Result.Failure(ex.Errors.First())
        On success → Result.Success(factory())
        Use this in static Create(...) factory methods to produce railway-friendly aggregate construction.
```

#### Auditable aggregate bases (`Aggregates/`)

The auditable aggregate hierarchy is a strict IS-A chain. Each level adds exactly one cross-cutting concern:

```text
AggregateRoot<TId>
  ├── AuditableAggregateRoot<TId>                          implements IHasAudit
  ├── SoftDeletableAggregateRoot<TId>                      implements ISoftDeletable
  │       protected abstract .OnDelete()                   → void  (concrete aggregate raises domain event here)
  │       protected .MarkAsDeleted(string deletedBy)       → void  (sets soft-delete fields)
  └── AuditableSoftDeletableAggregateRoot<TId>             implements IHasAudit + ISoftDeletable
        — all audit and soft-delete fields: private set
        — inherits MarkAsDeleted and OnDelete from SoftDeletableAggregateRoot via composition
          └── FullAuditableAggregateRoot<TId>              implements IHasAudit + ISoftDeletable + IHasConcurrency
                — extends AuditableSoftDeletableAggregateRoot<TId> (NOT AggregateRoot<TId> directly)
                — inherits all audit fields, soft-delete fields, MarkAsDeleted, and OnDelete from the hierarchy
                — adds only: IHasConcurrency explicit interface declaration + RowVersion { get; protected set; }
```

All audit fields (`CreatedBy`, `CreatedOn`, `ModifiedBy`, `ModifiedOn`, `IsDeleted`, `DeletedOn`, `DeletedBy`) have `private set` — populated exclusively by EF Core interceptors or persistence-layer conventions. `RowVersion` has `protected set` so the persistence layer can update it post-fetch.

The `IHasAudit`, `ISoftDeletable`, and `IHasConcurrency` interface declarations are retained explicitly on `FullAuditableAggregateRoot<TId>` for discoverability, even though they are transitively satisfied through the hierarchy.

#### Tenanted aggregate bases (`Aggregates/`)

Three tenanted bases parallel the core hierarchy. Each adds `IHasTenant` to the corresponding non-tenanted base:

```text
TenantedAggregateRoot<TId>              extends AggregateRoot<TId>, implements IHasTenant
    .TenantId                           → Guid  (private set — construction-time only)
    constructor: (TId id, Guid tenantId, IClock clock)
    ORM-path constructor: parameterless — TenantId = Guid.Empty

TenantedAuditableAggregateRoot<TId>     extends AuditableAggregateRoot<TId>, implements IHasTenant
    .TenantId                           → Guid  (private set — construction-time only)
    constructor: (TId id, Guid tenantId, IClock clock)
    ORM-path constructor: parameterless — TenantId = Guid.Empty

TenantedFullAuditableAggregateRoot<TId> extends FullAuditableAggregateRoot<TId>, implements IHasTenant
    .TenantId                           → Guid  (private set — construction-time only)
    constructor: (TId id, Guid tenantId, IClock clock)
    ORM-path constructor: parameterless — TenantId = Guid.Empty
    MarkAsDeleted and OnDelete are inherited from the FullAuditableAggregateRoot hierarchy
```

`TenantId` has `private set` on all three classes — tenant reassignment is a domain violation. The `tenantId` value is supplied by the application layer (typically resolved from `ITenantProvider` in `12.Security`) and passed as a `Guid` primitive. The domain layer must never reference `ITenantProvider` or `12.Security` directly.

`TenantedSoftDeletableAggregateRoot<TId>` is intentionally absent — it would explode the hierarchy without proportionate benefit.

#### Auditable entity bases (`Entities/`)

```text
AuditableEntity<TId>  extends Entity<TId>, implements IHasAudit
    — no event machinery; audit fields: private set

FullAuditableEntity<TId>  extends Entity<TId>, implements IHasAudit + ISoftDeletable + IHasConcurrency
    — no event machinery; all fields: private set; RowVersion: protected set
```

#### Strongly-typed ID base (`StronglyTypedIds/`)

```text
StronglyTypedId<TValue>  (abstract record, implements IStronglyTypedId<TValue>)  where TValue : notnull
    .Value                                                  → TValue  (required positional)
    .ToString()                                             → string  (= Value.ToString()!)
    implicit operator TValue
    NOTE: STJ serialization requires a custom JsonConverter in the consuming service — this package ships none.
```

#### ValueObject base (`ValueObjects/`)

```text
ValueObject  (abstract class, implements IValueObject)
    protected abstract .GetEqualityComponents()             → IEnumerable<object?>
    protected abstract .Validate()                          → IEnumerable<Error>?  (null = valid; non-null throws ValidationException)
    .Equals(object? obj)                                    → bool  (all components equal)
    .GetHashCode()                                          → int
    HAZARD: The base constructor calls Validate() immediately. If Validate() reads members assigned in the
            subclass constructor body (not as field initializers), those members will have default values
            (null / 0 / false) when Validate() runs. See Implementation Rules for safe patterns.

SingleValueObject<TValue>  (abstract class, extends ValueObject)
    .Value                                                  → TValue  (get-only; assigned before Validate() runs via field-initializer design)
    .GetEqualityComponents()                                → IEnumerable<object?>  (sealed; returns [Value])
    .ToString()                                             → string  (sealed; returns Value?.ToString() ?? string.Empty)
    implicit operator TValue
    protected abstract .Validate()                          → IEnumerable<Error>?  (subclasses must still declare rules)
    NOTE: Distinct from StronglyTypedId<TValue>. Use SingleValueObject<TValue> for domain concepts with
          validation (EmailAddress, Money, Percentage). Use StronglyTypedId<TValue> for entity/aggregate
          identity keys that are persisted to the database without domain validation.
```

#### Business rule system (`BusinessRules/`, `Exceptions/`)

```text
IBusinessRule
    .Message                                               → string
    .IsBroken()                                            → bool

BusinessRuleViolationException  extends DomainException  (from SharedKernel.Core.Exceptions)
    .Rule                                                  → IBusinessRule
    .Error                                                 → Error  (Error.BusinessRule(ErrorCodes.Domain.RuleViolated, rule.Message))
    NOTE: Hierarchy: Exception → SharedKernelException → DomainException → BusinessRuleViolationException.
          Catching SharedKernelException still catches this. Catching DomainException catches domain exceptions only.
          Error.Type == ErrorType.BusinessRule — maps to HTTP 422 in the presentation layer, NOT HTTP 500.

DomainNotFoundException  extends DomainException  (from SharedKernel.Core.Exceptions)
    .AggregateType                                         → Type
    .AggregateId                                           → object
    .Error                                                 → Error  (Error.NotFound(...))
    Message format: "Entity of type '{aggregateType.Name}' with id '{aggregateId}' was not found."

AndBusinessRule  (sealed)   — broken if either sub-rule is broken; Message aggregates with "; "
OrBusinessRule   (sealed)   — broken only if both sub-rules are broken
NotBusinessRule  (sealed)   — inverts broken state

BusinessRuleExtensions
    .And(this IBusinessRule, IBusinessRule)                → AndBusinessRule
    .Or(this IBusinessRule, IBusinessRule)                 → OrBusinessRule
    .Not(this IBusinessRule)                               → NotBusinessRule
```

#### Domain service base (`DomainServices/`)

```text
DomainService  (abstract class, implements IDomainService)
    protected static .CheckRule(IBusinessRule rule)        → void  (throws BusinessRuleViolationException if broken)
    — no constructor parameters
    — no IClock injection
    — no DomainEvents accumulation
    — no repository access
    NOTE: All domain service implementations must extend DomainService, not implement IDomainService directly.
          Extending DomainService gives access to CheckRule without any infrastructure coupling.
```

#### Policy system (`Policies/`)

```text
IPolicy<T>
    .IsCompliant(T subject)                                → bool

AndPolicy<T>  (sealed)   — compliant only when both sub-policies are compliant
OrPolicy<T>   (sealed)   — compliant when at least one sub-policy is compliant
NotPolicy<T>  (sealed)   — inverts compliance

PolicyExtensions
    .And<T>(this IPolicy<T>, IPolicy<T>)                  → AndPolicy<T>
    .Or<T>(this IPolicy<T>, IPolicy<T>)                   → OrPolicy<T>
    .Not<T>(this IPolicy<T>)                              → NotPolicy<T>
```

#### Specification system (`Specifications/`)

```text
ISpecification<T>
    .Criteria                                              → Expression<Func<T, bool>>?
    .Includes                                              → IReadOnlyList<Expression<Func<T, object>>>
    .OrderBy                                               → Expression<Func<T, object>>?
    .OrderByDescending                                     → Expression<Func<T, object>>?
    .ThenBys                                               → IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)>
    .Skip                                                  → int?
    .Take                                                  → int?
    .IsDistinct                                            → bool
    .AsNoTracking                                          → bool
        When true, the consuming repository must apply AsNoTracking() to the underlying query.
        Default is false — safe for specifications used before write operations.
        Set to true for read-only query specifications to avoid unnecessary change-tracking overhead.

Specification<T>  (abstract class, implements ISpecification<T>)
    protected .AddCriteria(Expression<Func<T, bool>>)      → void
    protected .AddInclude(Expression<Func<T, object>>)     → void
    protected .ApplyOrderBy(Expression<Func<T, object>>)   → void
    protected .ApplyOrderByDescending(Expression<Func<T, object>>) → void
    protected .ApplyThenBy(Expression<Func<T, object>>, bool descending) → void
    protected .ApplyPaging(int skip, int take)             → void
    protected .ApplyDistinct()                             → void
    protected .ApplyNoTracking()                           → void  (sets AsNoTracking = true)
    .IsSatisfiedBy(T entity)                               → bool  (non-virtual concrete method — in-domain/in-test use only)
        Compiles Criteria to Func<T, bool> exactly once and caches the delegate in a private field.
        Returns true when Criteria is null (criteria-less specification matches all entities).
        NOT present on ISpecification<T> — repository implementations do not use this method.

ReadOnlySpecification<T>  (sealed abstract class, extends Specification<T>)
    — calls ApplyNoTracking() in constructor; always returns true for AsNoTracking
    — use as base class for query-only specifications instead of calling ApplyNoTracking() manually

Specification<T>  (continued — additional builder method)
    protected .ApplyThenByDescending(Expression<Func<T, object>> keySelector) → void
        Alias for ApplyThenBy(keySelector, descending: true). Use instead of ApplyThenBy for clarity.
    ORDERING PRECEDENCE (documented in ISpecification<T> remarks):
        1. Primary sort: either ApplyOrderBy or ApplyOrderByDescending (mutually exclusive — last call wins)
        2. Secondary sorts: ThenBys list, applied in order of ApplyThenBy / ApplyThenByDescending calls
        3. If neither primary sort is set, ThenBys entries are ignored by well-behaved repositories

ReadOnlySpecification<T>  (sealed abstract class, extends Specification<T>)
    — calls ApplyNoTracking() in constructor; always returns true for AsNoTracking
    — use as base class for query-only specifications instead of calling ApplyNoTracking() manually

PagedSpecification<T>  (abstract class, extends ReadOnlySpecification<T>)
    constructor: protected PagedSpecification(int page, int pageSize)
        page is 1-based (page 1 = first page); calls ApplyPaging((page-1)*pageSize, pageSize) automatically
    .Page                                                  → int  (public get)
    .PageSize                                              → int  (public get)
    protected const MaxPageSize = 1000  (subclasses may shadow)
    Guards: page < 1 → ArgumentOutOfRangeException; pageSize < 1 → ArgumentOutOfRangeException;
            pageSize > MaxPageSize → ArgumentOutOfRangeException
    AsNoTracking is always true (inherited from ReadOnlySpecification<T>)

AllSpecification<T>  (sealed concrete class)  — identity element for AND composition
    Criteria = null  (matches all entities)
    IsSatisfiedBy always returns true
    NOTE: And(all, spec) effectively returns spec — AllSpecification is the AND identity element.

EmptySpecification<T>  (sealed concrete class)  — identity element for OR composition
    Criteria = _ => false  (matches no entity)
    IsSatisfiedBy always returns false
    NOTE: Or(empty, spec) effectively returns spec — EmptySpecification is the OR identity element.
    NOTE: Or(all, spec) has null criteria (matches everything) — null short-circuits OR composition.

AndSpecification<T>  — combines two specs via ExpressionVisitor ParameterReplacer (logical AND)
                      AsNoTracking = true if either operand has AsNoTracking = true
OrSpecification<T>   — combines via logical OR
                      AsNoTracking = true if either operand has AsNoTracking = true
                      Null-criteria handling: if either operand has null criteria, combined Criteria is null
NotSpecification<T>  — negates via Expression.Not
                      AsNoTracking = true if the operand has AsNoTracking = true

SpecificationExtensions
    .And<T>(this Specification<T>, Specification<T>)       → AndSpecification<T>
    .Or<T>(this Specification<T>, Specification<T>)        → OrSpecification<T>
    .Not<T>(this Specification<T>)                         → NotSpecification<T>
```

---

## Implementation Rules

- `SharedKernel.Domain` has **zero external NuGet dependencies** — references only `SharedKernel.Primitives` and `SharedKernel.Core` (both from `01.Core`).
- `Entity<TId>` equality is **identity-based** — two entities are equal if and only if `GetType()` matches and `Id` values are equal via `EqualityComparer<TId>.Default`.
- **Transient entities** (`IsTransient() == true`) are never equal to any other instance including themselves — use `RuntimeHelpers.GetHashCode` for their hash.
- `ValueObject` equality is **structural** — the `abstract class + GetEqualityComponents()` pattern is the chosen strategy (not abstract record positional equality) because: (a) multi-component equality does not always map to positional records, (b) the `Validate()` constructor hook requires explicit constructor control.
- **`ValueObject` constructor-call-order hazard:** The base constructor calls `Validate()` immediately. If your `Validate()` implementation reads properties assigned in the subclass constructor body (not as field initializers), they will have their default values (`null` / `0` / `false`) when validation runs. Two safe patterns: (1) **Field-initializer / primary-constructor assignment** — use `public decimal Amount { get; } = amount;` or a C# 12+ primary constructor parameter assignment, which executes before the base constructor body. (2) **Factory method pattern** — keep the constructor `private` or `protected`, expose a `static Result<TValueObject> Create(...)` factory that constructs the object (triggering validation) and returns `Result<TValueObject>` for railway-friendly error handling.
- `AggregateRoot` is the **only** type permitted to call `RaiseDomainEvent` — plain entities must not accumulate events.
- `DomainEvents` on `AggregateRoot` is **cleared by infrastructure** after successful dispatch — aggregates must never call `ClearDomainEvents()` on themselves.
- `IDomainEvent.OccurredOn` must be supplied by the aggregate's `RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` overload using `_clock.UtcNow`. Direct `DateTime.UtcNow` / `DateTimeOffset.UtcNow` usage anywhere in this package is a **hard violation**.
- Aggregate subclasses must use the **`RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent>)` factory overload** to timestamp events. Passing `Now` as `OccurredOn` in a pre-built event (e.g., `new MyEvent { OccurredOn = Now }`) is a **soft violation** documented in the `Now` property XML remarks — it compiles but bypasses the deterministic timestamp contract and makes event timestamps non-deterministic in unit tests.
- `NullClock` internal sentinel (returns `DateTimeOffset.MinValue`) is assigned to `_clock` in the protected parameterless ORM-path constructor of `AggregateRoot<TId>` — prevents NPE during ORM hydration.
- `ClearDomainEvents()` is called **only by infrastructure dispatch code** — never by an aggregate or application service.
- **Infrastructure event dispatch code must depend on `IHasDomainEvents`**, not `IAggregateRoot<TId>`. Aggregate identity is not required for event dispatch.
- Audit properties on all auditable bases (`CreatedBy`, `CreatedOn`, `ModifiedBy`, `ModifiedOn`, `IsDeleted`, `DeletedOn`, `DeletedBy`) have `private set` — populated exclusively by EF Core interceptors or persistence-layer conventions, never by the aggregate.
- `RowVersion` on `FullAuditableAggregateRoot<TId>` and `FullAuditableEntity<TId>` has `protected set` to allow persistence layer population after fetch.
- `FullAuditableAggregateRoot<TId>` extends `AuditableSoftDeletableAggregateRoot<TId>` — **not** `AggregateRoot<TId>` directly. It inherits all soft-delete and audit machinery; it contributes only `IHasConcurrency` and `RowVersion`.
- `IDomainEventHandler<TEvent>` is **explicitly excluded** from this package — it belongs in `05.Application`. The domain layer has no knowledge of handler dispatch.
- **`TenantId` is construction-time only** — the `TenantId` property on all tenanted aggregate bases has `private set` and must never change after construction. Tenant reassignment is a domain violation. The application layer (typically `ITenantProvider` from `12.Security`) resolves the tenant and passes it as a `Guid` primitive to the aggregate constructor. `ITenantProvider` must never be referenced from `03.Domain` — the domain receives `tenantId` as a primitive, not a resolved service.
- `IHasTenant` is a marker only — the domain layer has no tenant resolution logic.
- **All domain service implementations must extend `DomainService` abstract class** — do not implement `IDomainService` directly. Extending `DomainService` provides `CheckRule` access without any infrastructure coupling.
- `StronglyTypedId<TValue>` does not ship a STJ `JsonConverter` — consuming services must provide their own in their serialization context (e.g., `04.Contracts` or `06.Persistence`).
- No static mutable state anywhere in this domain.
- No persistence concerns (`DbContext`, repository interfaces, EF annotations) — those live in `06.Persistence`.
- No messaging concerns (`IMessageBus`, `IEventPublisher`) — those live in `07.Messaging`.
- Specifications built in concrete subclass constructors only — no fluent builder calls outside the constructor.
- `Specification<T>.IsSatisfiedBy(T entity)` is for **in-domain and in-test use only** — repository implementations in `06.Persistence` must not call it. The compiled delegate cache is instance-scoped; reusing a specification instance across requests is safe.
- `AsNoTracking = false` is the safe default — it does not cause data loss. Set it `true` only for read-only query specifications. Composite specifications inherit `true` if either operand is `true` (more restrictive wins).
- **`BusinessRuleViolationException` carries `Error.Type == ErrorType.BusinessRule`** — this maps to HTTP 422 in the presentation layer. It must never use `Error.Unexpected` (which maps to HTTP 500). The error code is `ErrorCodes.Domain.RuleViolated` from `SharedKernel.Primitives`.
- **`AggregateRoot<TId>.Version` is a domain-native optimistic concurrency helper** — starts at 0, increments by 1 on every `RaiseDomainEvent` call. This is distinct from `IHasConcurrency.RowVersion` which is an infrastructure-specific SQL Server byte array. `ClearDomainEvents()` does not decrement `Version`.
- **`TryCreate<T>` is the recommended aggregate factory helper** — use `protected static Result<T> TryCreate<T>(Func<T> factory)` in static `Create(...)` factory methods on aggregate roots to produce railway-friendly construction that converts exceptions to `Result.Failure` values.

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
- `SingleValueObject<TValue>` — `implicit operator TValue` is a static method, AOT-safe.
- `IDomainEvent` is a marker interface — AOT-safe.
- `DomainEvent` is an abstract record — sealed concrete records in consuming services are AOT-safe by default.
- `DomainEvent<TPayload>` is an abstract record with a generic type parameter constrained to `notnull` — AOT-safe when `TPayload` is a sealed record or value type.
- `DomainEventVersionAttribute` is a standard `System.Attribute` subclass — AOT-safe.
- `DomainEventVersionHelper.GetVersion(Type)` uses `Type.GetCustomAttribute<T>()` — this is reflection, but operates on attribute metadata only. This is a well-supported AOT pattern; attribute reading is preserved by the trimmer via `[DynamicallyAccessedMembers]` when needed. Keep usage at startup/registration time, not in hot paths.
- `StronglyTypedId<TValue>` is an abstract record — `implicit operator TValue` is a static method, AOT-safe.
- `DomainService` abstract class — static `CheckRule` method, AOT-safe.
- `AggregateRoot<TId>.TryCreate<T>` — uses try/catch on known exception types, no reflection, AOT-safe.
- `AllSpecification<T>` and `EmptySpecification<T>` are sealed concrete classes — AOT-safe.
- `PagedSpecification<T>` is an abstract class — AOT-safe.
- Tenanted aggregate bases are abstract classes extending the existing hierarchy — AOT-safe.
- Specification expression trees (`Expression<Func<T, bool>>`) are AOT-safe when the expressions do not involve runtime-only reflection APIs.
- `ExpressionVisitor` / `ParameterReplacer` in specification composites: AOT-safe as these operate on already-compiled expression trees with no runtime type discovery.
- `Specification<T>.IsSatisfiedBy` calls `Criteria.Compile()` — expression compilation is AOT-safe for expressions that do not use late-bound reflection inside the lambda body.
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
- `Specification<T>.IsSatisfiedBy`: criteria-less spec returns `true`; matching entity returns `true`; non-matching returns `false`; compiled delegate is reused across calls (same `Func<T,bool>` reference on repeated calls to same instance).
- `ISpecification<T>.AsNoTracking`: default `false`; `ApplyNoTracking()` sets `true`; `ReadOnlySpecification<T>` always `true`; composites propagate `true` if either operand is `true`.
- `DomainEvent<TPayload>`: payload exposed correctly; implements `IDomainEvent`; `Id` auto-generated; `OccurredOn` init-only.
- `FullAuditableAggregateRoot<TId>` hierarchy: IS-A `AuditableSoftDeletableAggregateRoot<TId>`; `MarkAsDeleted` and `OnDelete` are inherited, not re-declared.
- `StronglyTypedId`: implicit operator unwraps; `ToString()` delegates to inner value.
- `CheckRule`: broken rule throws `BusinessRuleViolationException` carrying the rule; non-broken rule does not throw.
- `BusinessRuleViolationException` error type: caught exception carries `Error.Type == ErrorType.BusinessRule`; never `ErrorType.Unexpected`.
- `DomainService.CheckRule`: concrete domain service subclass can call it; broken rule throws; non-broken does not.
- `TenantedAggregateRoot<TId>`: `TenantId` set correctly at construction; ORM-path constructor leaves `TenantId == Guid.Empty`; `TenantId` setter is `private` (verified via reflection).
- `IHasDomainEvents`: `AggregateRoot<TId>` implements it; all existing event tests pass without modification.
- `SingleValueObject<TValue>`: `Value` accessible inside `Validate()` at construction time (construction-order fix); equality by value; implicit unwrap; failed `Validate()` throws `ValidationException`.
- `IHasVersion`: initial `Version` is 0; raises 3 events → `Version == 3`; `ClearDomainEvents()` does not decrement.
- `DomainNotFoundException`: IS-A `DomainException`; message format verified; `Error.Type == ErrorType.NotFound`.
- `BusinessRuleViolationException` IS-A `DomainException`; catching `DomainException` catches both domain exceptions.
- `AllSpecification<T>` and `EmptySpecification<T>`: sentinel behavior verified; composition identity elements verified.
- `PagedSpecification<T>`: Skip/Take computed correctly; guards throw `ArgumentOutOfRangeException`; `AsNoTracking = true`.
- `ApplyThenByDescending`: produces `ThenBys` entry with `Descending = true`.
- `DomainEventVersionAttribute` / `DomainEventVersionHelper`: version readable; default 1 when absent; version < 1 throws.
- `TryCreate<T>`: success path; `BusinessRuleViolationException` → `Result.Failure` with `ErrorType.BusinessRule`; `ValidationException` → `Result.Failure`.

---

## Changelog

> Maintained by the domain agent. One line per significant change.

- [2026-05-22] Domain brain initialized — packages, interfaces, rules, AOT notes
- [2026-05-22] P-032 (WO-008) — full public surface documented: all interfaces, abstract bases, auditable hierarchy, ValueObject strategy decision, business rules, policies, specifications, strongly-typed IDs, NullClock sentinel, IDomainEventHandler exclusion boundary
- [2026-05-22] WO-009 (P-036..P-041) — refreshed: corrected FullAuditableAggregateRoot hierarchy (extends AuditableSoftDeletableAggregateRoot); corrected SharedKernel.Core dependency declaration; added IsSatisfiedBy and AsNoTracking to specification surface; added `DomainEvent<TPayload>`; added Now property usage constraint; updated all affected sections (domain-arch-planner)
- [2026-05-26] SK.03.Design closed — D-15..D-18 verified against CLAUDE.md; all 18 design tasks ●; no content edits needed (designs already present) (domain-phase-implementer)
- [2026-05-26] SK.03.Published complete — SharedKernel.Domain 1.1.0 packed; manifest deps: Primitives + Core only; no new architectural signals (domain-phase-implementer)
- [2026-05-27] WO-010 (P-043, P-044) — refreshed: BusinessRuleViolationException now extends DomainException and uses Error.BusinessRule; ValueObject constructor hazard documented; tenanted aggregate family (3 bases) added to hierarchy; IHasTenant implementation rules added; version bump to 1.2.0 planned (domain-arch-planner)
- [2026-05-27] WO-011 (P-045..P-054) — refreshed: IHasDomainEvents extracted; SingleValueObject<TValue> added; DomainService abstract base added; IHasVersion added; DomainNotFoundException added; AllSpecification/EmptySpecification sentinels added; PagedSpecification<T> added; ApplyThenByDescending alias added; DomainEventVersionAttribute/Helper added; IAggregateFactory + TryCreate<T> added; version bump to 1.3.0 planned (domain-arch-planner)
- [2026-05-27] SK.03.Published complete — SharedKernel.Domain 1.2.0 and 1.3.0 packed; manifests: Primitives + Core only; no new architectural signals (domain-phase-implementer)
