# 03.Domain — DDD Building Blocks

## What This Domain Is

The DDD primitives layer. Every aggregate, entity, value object, and domain event in a downstream microservice derives from the abstractions defined here. This domain may only reference `01.Core` — it must never reference infrastructure, persistence, or messaging layers.

Philosophy: **Pure domain model. No side effects. No I/O. AOT-preferred. Railway-friendly.**

---

## Packages

| Package                 | Role                                                                                                                                                                                             | References                                      |
|-------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------|
| `SharedKernel.Domain`   | All DDD building blocks: identity contracts, aggregate/entity bases, value object base, domain event contract, business rules, policies, specifications, auditable bases, strongly-typed ID base | `SharedKernel.Primitives`, `SharedKernel.Core` |

Both references are project references within the `01.Core` capability domain. `SharedKernel.Domain` has **zero external NuGet dependencies**. WO-082/P-509 (2026-09-10) re-pointed the former `SharedKernel.Guards` `ProjectReference` onto `SharedKernel.Core` after `SharedKernel.Guards` was merged into it (P-505) — the `Guard`/`Guard.Against`/`Guard.Throw` C# namespace was deliberately preserved on the merge, so every `Guard.Throw.Null(...)` call site below required zero source change, only the project reference moved.

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

IDomainEventDispatcher
    .DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct) → Task
    NOTE: Opt-in DI contract. Consumed by EfUnitOfWork in 06.Persistence.EfCore as an optional
          dependency — not auto-registered by any infrastructure builder. Consuming services
          register an implementation alongside their persistence builder.
          CONTRACT (documented in XML doc on the interface):
            — An empty events list must be treated as a no-op by any conforming implementation.
            — Handler exceptions must propagate unchanged — implementations must not swallow them.
          IDomainEventHandler<TEvent> is NOT added here — handler registration remains 05.Application.
          The MediatR-based implementation (MediatRDomainEventDispatcher) is a future 05.Application phase.
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
        WO-051/P-311 — result is cached per Type in a private ConcurrentDictionary<Type, int>; the
        reflection call (GetCustomAttribute) happens at most once per distinct Type for the process
        lifetime. Unlike StronglyTypedIdJsonConverterFactory (already cached by JsonSerializerOptions),
        this helper is called directly by infrastructure (messaging/outbox) on a potential per-message
        hot path with no caller-side cache of its own.
    NOTE: Versioning workflow — declare [DomainEventVersion(1)] on every concrete event from its first
          schema (enforced by 00.Governance analyzer SK0009), and increment it when the schema changes
          in a backward-incompatible way. Consumers read the version via GetVersion() to route to the
          correct deserializer. GetVersion() returning 1 for an undeclared type is a runtime fallback
          only, not permission to omit the attribute.
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

IHasAggregateId<TId>  where TId : notnull   (WO-051/P-309)
    .AggregateId                                           → TId
    NOTE: Opt-in marker — zero-ceremony shape deliberately mirroring IHasTenant exactly. IDomainEvent,
          DomainEvent, and DomainEvent<TPayload> are completely unchanged; a concrete event may
          additionally implement this interface to declare which aggregate raised it. Gives
          infrastructure code (audit trails, outbox/messaging translation, projections, logging) a
          single `is IHasAggregateId<TId>` type check instead of per-event ad hoc property-name
          conventions (OrderId, SourceId, etc.). Closes a gap 04.Contracts's EventEnvelope<TEvent>
          XML doc already assumed was filled.
    Example:
        public sealed record OrderPlacedEvent(OrderId AggregateId)
            : DomainEvent<OrderPlacedPayload>, IHasAggregateId<OrderId>;
```

#### Entity base (`Entities/`)

```text
Entity<TId>  (abstract class, implements IEntity<TId>, IEquatable<Entity<TId>>)
    .Id                                                     → TId  (private init)
    .IsTransient()                                          → bool  (true when Id == default(TId))
    .Equals(object? obj)                                    → bool  (identity: GetType() + EqualityComparer<TId>.Default; sealed override)
    .Equals(Entity<TId>? other)                             → bool  (WO-051/P-311 — IEquatable<T>; delegates to Equals(object?); avoids boxing in generic collections)
    .GetHashCode()                                          → int   (RuntimeHelpers.GetHashCode for transient instances; sealed override)
    operator == / operator !=
```

#### AggregateRoot base (`Aggregates/`)

```text
AggregateRoot<TId>  (abstract class, extends Entity<TId>, implements IAggregateRoot<TId>, IHasVersion)
    constructor: protected AggregateRoot(TId id, IClock clock)
        WO-051/P-311 — clock is guarded via Guard.Throw.Null(clock, nameof(clock)) (SharedKernel.Guards namespace,
        physically hosted in SharedKernel.Core since WO-082/P-505/P-509);
        passing null throws DomainException at the constructor boundary instead of a downstream
        NullReferenceException from Now/RaiseDomainEvent. TenantedAggregateRoot<TId>,
        TenantedAuditableAggregateRoot<TId>, and TenantedFullAuditableAggregateRoot<TId> all chain
        through base(id, clock) to this same constructor, so this one guard site protects all four
        aggregate bases — no per-subclass duplication.
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
    .Value                                                  → TValue  (required positional; WO-051/P-311 — guarded against null when TValue is a reference type, see below)
    .ToString()                                             → string  (= Value.ToString()!)
    implicit operator TValue
    NOTE: STJ serialization is supported via StronglyTypedIdJsonConverterFactory (see StronglyTypedIds/Serialization/
          below) — opt-in, not auto-registered. Concrete IDs must follow the documented shape:
              public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
          i.e. a public primary constructor (TValue Value) on a non-abstract closed type.
    NOTE: WO-051/P-311 — Value is null-guarded via a TValue-safe check (Guard.Throw.Null<T>'s
          `where T : class` constraint cannot apply to unconstrained TValue): the redeclared Value
          property's initializer calls a private static helper that throws DomainException when
          value is null and TValue is a reference type, and is a no-op for value-type instantiations
          (e.g. Guid, int). Guarded via the same positional-record validation technique already
          proven safe by SingleValueObject<TValue>'s construction-order fix.
```

#### Strongly-typed ID STJ serialization (`StronglyTypedIds/Serialization/`)

```text
StronglyTypedIdJsonConverterFactory  (sealed, extends JsonConverterFactory)
    .CanConvert(Type typeToConvert)                        → bool
        True when typeToConvert is non-abstract and its base type closes StronglyTypedId<TValue>
        for TValue in { Guid, int, long, string }.
    .CreateConverter(Type typeToConvert, JsonSerializerOptions options) → JsonConverter
        Returns a closed StronglyTypedIdJsonConverter<TStronglyTypedId, TValue> via
        Activator.CreateInstance(typeof(StronglyTypedIdJsonConverter<,>).MakeGenericType(idType, valueType)).
        Called once per closed type by STJ; the result is cached by JsonSerializerOptions.

StronglyTypedIdJsonConverter<TStronglyTypedId, TValue>  (sealed, extends JsonConverter<TStronglyTypedId>)
    where TStronglyTypedId : StronglyTypedId<TValue>  where TValue : notnull
    .Read(...)                                             → TStronglyTypedId
        Deserializes the raw TValue token via JsonSerializer.Deserialize<TValue>, then constructs
        TStronglyTypedId via a cached Func<TValue, TStronglyTypedId> activator.
    .Write(...)                                            → void
        Writes value.Value via JsonSerializer.Serialize<TValue> — the wire format is the bare
        primitive (e.g. a JSON string for Guid/string, a JSON number for int/long), never an
        object wrapper such as { "value": ... }.

USAGE (opt-in — consuming services register explicitly):
    var options = new JsonSerializerOptions();
    options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
    // OrderId, CustomerId, etc. now (de)serialize as their bare TValue.
```

#### ValueObject base (`ValueObjects/`)

```text
ValueObject  (abstract class, implements IValueObject, IEquatable<ValueObject>)
    protected abstract .GetEqualityComponents()             → IEnumerable<object?>
    protected abstract .Validate()                          → IEnumerable<Error>?  (null = valid; non-null throws ValidationException)
    .Equals(object? obj)                                    → bool  (all components equal)
    .Equals(ValueObject? other)                             → bool  (WO-051/P-311 — IEquatable<T>; delegates to Equals(object?); avoids boxing in generic collections)
    .GetHashCode()                                          → int
    protected static .CheckRule(IBusinessRule rule)         → void  (WO-051/P-310 — identical semantics to AggregateRoot<TId>.CheckRule; throws BusinessRuleViolationException if broken)
    protected static .TryCreate<T>(Func<T> factory)        → Result<T>  (WO-051/P-310 — identical semantics to AggregateRoot<TId>.TryCreate<T>)
        Catches BusinessRuleViolationException → Result.Failure(ex.Error)
        Catches ValidationException → Result.Failure(ex.Errors[0])
        On success → Result.Success(factory())
        Lives on ValueObject itself, so SingleValueObject<TValue> inherits both helpers with zero
        additional code. Use in static Create(...) factory methods, mirroring AggregateRoot<TId>'s
        own recommended pattern — achieves full parity between the two base types.
    HAZARD: The base constructor calls Validate() immediately. If Validate() reads members assigned in the
            subclass constructor body (not as field initializers), those members will have default values
            (null / 0 / false) when Validate() runs. See Implementation Rules for safe patterns.

SingleValueObject<TValue>  (abstract class, extends ValueObject)
    .Value                                                  → TValue  (get-only; assigned before Validate() runs via field-initializer design; WO-051/P-311 — null-guarded for reference-type TValue via the same field-initializer guard technique)
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

CurrencyMismatchRule  (sealed, implements IBusinessRule)   (WO-066/P-439)
    constructor: CurrencyMismatchRule(Currency expected, Currency actual)
    .IsBroken()                                            → bool  (= !expected.Equals(actual))
    .Message                                                → string  ($"Currency mismatch: expected '{expected.Code}' but was '{actual.Code}'.")
    NOTE: Public, top-level, reusable rule — sibling to AndBusinessRule/OrBusinessRule/NotBusinessRule,
          not nested inside Money — so a consuming service can reuse it independently of Money
          construction (e.g. validating an incoming payment DTO's currency before ever constructing
          a Money instance). Money's cross-currency arithmetic (see Money system below) calls
          ValueObject.CheckRule(new CurrencyMismatchRule(...)) — reusing the already-shipped
          BusinessRuleViolationException / ErrorType.BusinessRule → HTTP 422 pipeline. No new
          exception type was introduced for Money's cross-currency rejection.
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
    .Explain(T subject)                                    → string   (default interface member, WO-051/P-312)
        Default body: IsCompliant(subject) ? string.Empty : $"Policy '{GetType().Name}' is not satisfied.".
        A C# DIM — added with zero breaking change: any pre-existing IPolicy<T> implementor that only
        ever declared IsCompliant continues to compile unmodified and gets the default explanation.
        Overridable when a specific message is wanted. Mirrors IBusinessRule's Message/IsBroken pair.

AndPolicy<T>  (sealed)   — compliant only when both sub-policies are compliant
                          Explain: aggregates every non-compliant sub-policy's Explain(subject) with "; "
                          (mirrors AndBusinessRule.Message's string.Join("; ", ...) pattern exactly)
OrPolicy<T>   (sealed)   — compliant when at least one sub-policy is compliant
                          Explain: non-empty only when BOTH sub-policies are non-compliant (OR requires
                          just one to pass) — aggregates both explanations with "; "
NotPolicy<T>  (sealed)   — inverts compliance
                          Explain: when non-compliant (i.e. the inner policy unexpectedly WAS compliant),
                          returns a fixed generic message — there is no sub-policy failure to delegate to

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
    .StringIncludes                                        → IReadOnlyList<string>
        String-based navigation-include paths for deep eager loading (e.g. "Orders.Items.Product").
        Applied after expression-based Includes (evaluator step 2b) and before ordering. Corrects a
        prior gap in this brain — StringIncludes/AddStringInclude shipped in an earlier phase but was
        never documented here until WO-051/P-307.
    .AsSplitQuery                                          → bool
        When true, the consuming repository must call EF Core's AsSplitQuery() instead of a single
        Cartesian-joined query. Default false. Set true when a specification declares two or more
        collection Includes to avoid duplicate-row Cartesian-product results. Composite specifications
        propagate true when either operand has it set (more-permissive wins — mirrors AsNoTracking).
    .AsNoTracking                                          → bool
        When true, the consuming repository must apply AsNoTracking() to the underlying query.
        Default is false — safe for specifications used before write operations.
        Set to true for read-only query specifications to avoid unnecessary change-tracking overhead.
    .IncludeDeleted                                        → bool
        When true, the consuming repository must bypass the global soft-delete query filter so that
        soft-deleted records are included in results. Intended for admin panels, audit trails, data
        export, and recovery operations only. Default is false — soft-deleted records are hidden.
        WARNING: Setting IncludeDeleted = true calls IgnoreQueryFilters() internally (in EF Core),
        which also bypasses ANY tenant isolation filter on the entity type because EF Core's
        IgnoreQueryFilters() cannot selectively bypass a single filter.
        For tenant-scoped soft-delete queries, re-apply the tenant criterion manually:
            AddCriteria(e => e.TenantId == tenantId)

Specification<T>  (abstract class, implements ISpecification<T>)
    protected .AddCriteria(Expression<Func<T, bool>>)      → void
    protected .AddInclude(Expression<Func<T, object>>)     → void
    protected .ApplyOrderBy(Expression<Func<T, object>>)   → void
    protected .ApplyOrderByDescending(Expression<Func<T, object>>) → void
    protected .ApplyThenBy(Expression<Func<T, object>>, bool descending) → void
    protected .ApplyPaging(int skip, int take)             → void
    protected .ApplyDistinct()                             → void
    protected .AddStringInclude(string path)               → void  (throws ArgumentException on null/whitespace; deep navigation paths, e.g. "Orders.Items.Product")
    protected .ApplyNoTracking()                           → void  (sets AsNoTracking = true)
    protected .ApplySplitQuery()                           → void  (sets AsSplitQuery = true — call when a spec declares 2+ collection Includes)
    protected .IncludeSoftDeleted()                        → void  (sets IncludeDeleted = true)
        Call this in a concrete specification's constructor when the spec is an admin/audit/export/recovery
        specification that must see soft-deleted records. Never call it from non-admin specifications.
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

Specification<T>.Create(Expression<Func<T, bool>> criteria)  → Specification<T>   (WO-051/P-313)
    Ad hoc, criteria-only factory for genuinely one-off/throwaway filters — a third sealed-wrapper
    sentinel alongside AllSpecification<T>/EmptySpecification<T>, backed internally by
    `internal sealed class CriteriaSpecification<T> : Specification<T>` (constructor calls
    AddCriteria(criteria) only — no includes, no ordering, no paging; AsNoTracking/IncludeDeleted/
    AsSplitQuery all default false). Composes normally via .And()/.Or()/.Not() against both other
    ad hoc specs and named Specification<T> subclasses.
    NOTE: Does NOT relax the constructor-only/no-fluent-chaining builder rule for named, reusable
    specifications — a reusable business concept must still be its own dedicated Specification<T>
    subclass. Use Create(...) only for a genuinely single-use filter.

KeysetSpecification<T, TKey>  (abstract class, extends ReadOnlySpecification<T>)  where TKey : struct, IComparable<TKey>   (WO-051/P-308)
    constructor: protected KeysetSpecification(
        Expression<Func<T, TKey>> keySelector, Expression<Func<T, object>> idSelector,
        TKey? afterKey, object? afterId, bool descending, int take)
    .AfterKey                                              → TKey?    (null = first page, no seek predicate)
    .AfterId                                               → object?  (the Id tiebreaker cursor value; null on first page)
    .Descending                                            → bool
    Cursor/seek-pagination sibling to PagedSpecification<T>. Applies OrderBy/OrderByDescending on
    keySelector per `descending`, then unconditionally ApplyThenBy(idSelector, descending: false) as
    a MANDATORY deterministic tiebreaker — a keyset page boundary is unsound without a unique
    tiebreaker when many rows share the same TKey value. Calls ApplyPaging(0, take) — Skip is fixed
    at 0; the persistence evaluator must ignore Skip and instead translate AfterKey/AfterId into a
    `WHERE (SortKey, Id) > (@cursor, @cursorId)`-shaped seek predicate, composed with any Criteria
    via AND. Concrete subclasses still call AddCriteria(...) in their own constructor for filtering —
    this shape governs ordering/paging only, composing cleanly with existing Criteria/Includes.
    Guards: take < 1 → ArgumentOutOfRangeException; exactly one of afterKey/afterId supplied (a
    partial cursor) → ArgumentException — a cursor is atomic, both-or-neither.
    CORRECTED post-implementation (WO-051/C-39): the constraint is `struct, IComparable<TKey>`, not
    `IComparable<TKey>` alone as originally designed. Empirically confirmed against the real compiler:
    C#'s nullable annotation on a type parameter constrained only by an interface (no `struct`/`class`
    split) erases `TKey?` to plain non-nullable `TKey` for value-type closures — this would have
    silently broken the "null = first page" cursor contract for exactly the sort-key shapes this type
    is built around (`DateTimeOffset`, `int`, `long`, `Guid`). Consequence: `KeysetSpecification<T, TKey>`
    supports only value-typed sort keys — a reference-typed sort key (e.g. `string`) must sort by a
    value-typed proxy column instead.

AndSpecification<T>  — combines two specs via ExpressionVisitor ParameterReplacer (logical AND)
                      Includes = union of left.Includes + right.Includes (WO-051/P-307)
                      StringIncludes = union of left.StringIncludes + right.StringIncludes
                      AsNoTracking = true if either operand has AsNoTracking = true
                      AsSplitQuery = true if either operand has AsSplitQuery = true (WO-051/P-308)
                      IncludeDeleted = true if either operand has IncludeDeleted = true (more-permissive wins)
OrSpecification<T>   — combines via logical OR
                      Includes = union of left.Includes + right.Includes (WO-051/P-307)
                      StringIncludes = union of left.StringIncludes + right.StringIncludes
                      AsNoTracking = true if either operand has AsNoTracking = true
                      AsSplitQuery = true if either operand has AsSplitQuery = true (WO-051/P-308)
                      IncludeDeleted = true if either operand has IncludeDeleted = true (more-permissive wins)
                      Null-criteria handling: if either operand has null criteria, combined Criteria is null
NotSpecification<T>  — negates via Expression.Not
                      Includes = union from the operand (WO-051/P-307)
                      StringIncludes = union from the operand
                      AsNoTracking = true if the operand has AsNoTracking = true
                      AsSplitQuery = true if the operand has AsSplitQuery = true (WO-051/P-308)
                      IncludeDeleted = true if the operand has IncludeDeleted = true

SpecificationExtensions
    .And<T>(this Specification<T>, Specification<T>)       → AndSpecification<T>
    .Or<T>(this Specification<T>, Specification<T>)        → OrSpecification<T>
    .Not<T>(this Specification<T>)                         → NotSpecification<T>
```

#### Money system (`ValueObjects/Money/`)  (WO-066/P-439)

```text
RoundingPolicy  (enum)
    BankersRounding   — default; maps to MidpointRounding.ToEven. Platform default because it
                         introduces no systematic rounding bias across a large volume of
                         transactions — the standard choice for financial ledgers.
    AwayFromZero      — maps to MidpointRounding.AwayFromZero. Explicit opt-in for
                         jurisdictions/contracts that require it.

CurrencyCatalog  (static class)
    .TryGetMinorUnitDigits(string code, out int digits)     → bool
    .IsKnownCode(string code)                               → bool
    NOTE: Backed by a fixed static Dictionary<string, int> covering every active ISO 4217
          alpha-3 code. Default is 2 digits; documented exceptions: zero-decimal currencies
          (JPY, KRW, VND, ISK, CLP, PYG, UGX, RWF, XOF, XAF, XPF, KMF, GNF, DJF, VUV) at 0
          digits, three-decimal currencies (BHD, KWD, OMR, JOD, TND, LYD, IQD) at 3 digits.
          This is a fixed dataset — not I/O-backed, not refreshed at runtime. A currency
          addition/deprecation requires a new package version, not a runtime config change.

Currency  (sealed class, extends SingleValueObject<string>)
    .Code                                                   → string  (= Value; ISO 4217 alpha-3, always uppercase)
    .MinorUnitDigits                                        → int  (derived, non-equality-participating —
                                                                     computed via CurrencyCatalog.TryGetMinorUnitDigits(Code, ...))
    protected override .Validate()                          → checks Code is exactly 3 uppercase ASCII
                                                                letters AND a known CurrencyCatalog code
    public static .Create(string code)                      → Result<Currency>
        Via ValueObject.TryCreate(() => new Currency(code.Trim().ToUpperInvariant())) — the same
        SingleValueObject<TValue>/ValueObject.TryCreate<T> pattern already documented above.
    Well-known static convenience instances: Currency.Usd, .Eur, .Gbp, .Jpy (at minimum) — a DX
    convenience only, NOT an exhaustive currency list. Create(...) remains the general-purpose
    path covering the full ISO 4217 catalog via CurrencyCatalog.

Money  (sealed class, extends ValueObject — NOT SingleValueObject<TValue>, since it has two
        independent components rather than one)
    .Amount                                                 → decimal  (get-only; ALWAYS already rounded
                                                                          to Currency.MinorUnitDigits — see below)
    .Currency                                                → Currency  (get-only)
    NOTE: Amount and Currency are assigned in a traditional (non-primary) PRIVATE constructor —
          NOT via the SingleValueObject<TValue>-style field-initializer-before-base() technique.
          CORRECTED post-implementation (WO-066/C-51, same class of design/compiler-mismatch
          correction as WO-051/C-39's KeysetSpecification<T,TKey> constraint fix): the
          field-initializer trick requires a PRIMARY constructor (only a primary constructor's
          parameter is in scope for a field initializer that must run before base()), and a
          primary constructor's accessibility on a SEALED, non-abstract class always matches the
          class's own (public) — there is no C# language mechanism to restrict it further. This
          works for SingleValueObject<TValue> only because that type is ABSTRACT, where an
          unmodified-accessibility primary constructor is implicitly protected. Money cannot be
          both sealed and rely on that trick while staying genuinely private. Amount's rounding
          (via the RoundingPolicy helper, against Currency.MinorUnitDigits) still happens
          unconditionally in the constructor — there is no separate "reject excess precision"
          validation path — it is simply computed in the constructor body rather than a field
          initializer, which is safe here (see Validate(), below).
    .GetEqualityComponents()                                → IEnumerable<object?>  (returns [Amount, Currency])
    protected override .Validate()                          → a no-op, always returns null.
        CORRECTED post-implementation (WO-066/C-51): the currency-null-guard is NOT performed in
        Validate() — it is Guard.Throw.Null(currency, nameof(currency)), called directly in the
        constructor body BEFORE Amount/Currency are assigned (mirrors the AggregateRoot<TId>
        clock-parameter precedent, WO-051/P-311, exactly: same guard call shape, same immediate
        DomainException-on-violation behavior). Validate() has nothing left to check by the time
        it could run, since the constructor's own guard already ran first.
    public static .Create(decimal amount, Currency currency, RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding)
                                                              → Result<Money>
        Via ValueObject.TryCreate(() => new Money(amount, currency, roundingPolicy)).
    public static .Zero(Currency currency)                   → Money  (Amount = 0m; cannot fail, no Result wrapper)

    Arithmetic / comparison — every cross-currency path below calls
    CheckRule(new CurrencyMismatchRule(this.Currency, other.Currency)) first, throwing
    BusinessRuleViolationException (ErrorType.BusinessRule → HTTP 422) on mismatch:
        .Add(Money other)                                    → Money
        .Subtract(Money other)                                → Money
        .Negate()                                             → Money  (no currency to mismatch)
        .Multiply(decimal factor, RoundingPolicy roundingPolicy = RoundingPolicy.BankersRounding)
                                                                → Money  (scalar — no currency to mismatch;
                                                                          re-rounds the product to Currency.MinorUnitDigits)
        .CompareTo(Money other)                               → int  (implements IComparable<Money>;
                                                                        cross-currency comparison is exactly
                                                                        as invalid as cross-currency addition)
    operator +(Money, Money)              → left.Add(right)
    operator -(Money, Money)              → left.Subtract(right)
    operator -(Money)                     → money.Negate()
    operator *(Money, decimal)            → money.Multiply(factor)
    operator <, <=, >, >=                 → delegate to CompareTo
    NOTE: Add/Subtract/Multiply/Negate construct the result Money directly (not re-validated
          through Create/TryCreate) — both operands are already-valid, same-currency instances,
          so no new failure mode is introduced by combining them. Equals/GetHashCode/
          IEquatable<ValueObject> are inherited unchanged from ValueObject.

    Allocation:
        .Allocate(int numberOfParts)                          → IReadOnlyList<Money>
        .Allocate(IReadOnlyList<int> ratios)                  → IReadOnlyList<Money>  (weighted split)
        Algorithm: largest-remainder / Hare–Niemeyer method — converts Amount to an exact integer
        count of minor units, integer-divides proportionally to each ratio's share (floor
        division), then distributes the leftover minor units one-by-one, largest-remainder-first,
        with ties broken by DESCENDING original index (CORRECTED/clarified post-implementation,
        WO-066/C-53 — the worked example below requires the LAST equally-tied share to win first;
        ascending-index tie-breaking would instead give the extra minor unit to the FIRST share).
        Guarantees Sum(result) == this exactly, and no part differs from another by more than one
        minor unit. Worked example: splitting $10.00 three ways produces [3.33, 3.33, 3.34] —
        never [3.33, 3.33, 3.33] (loses a cent) or [3.34, 3.34, 3.34] (invents two cents).
        Guards: numberOfParts < 1 → ArgumentOutOfRangeException; empty/negative/all-zero ratios
        → ArgumentException.

IExchangeRateProvider  (pure interface — zero implementation in this package)
    .GetExchangeRateAsync(Currency source, Currency target, CancellationToken cancellationToken)
                                                              → Task<Result<decimal>>
    NOTE: An async, Task-returning domain port — the same shape precedent as the already-shipped
          IDomainEventDispatcher.DispatchAsync (WO-014/P-081), which proves an async member on a
          pure-contract interface is accepted in this zero-I/O package: the INTERFACE performs no
          I/O itself — only a consuming service's real implementation does. 03.Domain ships no
          implementation, no hardcoded rate table, no embedded HttpClient/SDK call.

MoneyExtensions  (static class)
    .ConvertAsync(this Money money, Currency targetCurrency, IExchangeRateProvider rateProvider,
                  RoundingPolicy roundingPolicy, CancellationToken cancellationToken)
                                                              → Task<Result<Money>>
    NOTE: An extension method, not an instance member on Money — keeps the synchronous, pure
          ValueObject free of async members. Composes the rate lookup with Money.Create as the
          ergonomic conversion entry point consuming services use.
```

**Explicitly out of scope for this phase:** percentage/interest-calculation helpers. Flagged as a
documented future extension only if a real consumer need materializes — not speculatively added now.

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
- `IDomainEventDispatcher` is the **only** dispatch-related type permitted in this package. Its scope is strictly the interface contract — no implementation, no registration. Any conforming implementation must (a) treat an empty `events` list as a no-op and (b) propagate handler exceptions unchanged without swallowing. The `06.Persistence.EfCore` layer consumes this interface as an optional dependency; consuming services opt in by registering an implementation via DI. The MediatR-based implementation belongs in `05.Application`.
- **`TenantId` is construction-time only** — the `TenantId` property on all tenanted aggregate bases has `private set` and must never change after construction. Tenant reassignment is a domain violation. The application layer (typically `ITenantProvider` from `12.Security`) resolves the tenant and passes it as a `Guid` primitive to the aggregate constructor. `ITenantProvider` must never be referenced from `03.Domain` — the domain receives `tenantId` as a primitive, not a resolved service.
- `IHasTenant` is a marker only — the domain layer has no tenant resolution logic.
- **All domain service implementations must extend `DomainService` abstract class** — do not implement `IDomainService` directly. Extending `DomainService` provides `CheckRule` access without any infrastructure coupling.
- `StronglyTypedIdJsonConverterFactory` supports exactly four `TValue` shapes — `Guid`, `int`, `long`, `string`. A closed `StronglyTypedId<TValue>` for any other `TValue` is not converted by the factory (`CanConvert` returns `false`); such types fall back to default STJ record serialization (an object wrapper `{ "value": ... }`) unless the consuming service provides its own converter.
- Concrete strongly-typed ID types must follow the documented shape — `public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);` — i.e., a public primary constructor `(TValue Value)` on a non-abstract closed type. `StronglyTypedIdJsonConverter<TStronglyTypedId, TValue>` compiles its activator delegate against this constructor; a type that hides or omits this constructor will fail at first use with an `InvalidOperationException` from the converter, not at `CreateConverter` time.
- The wire format produced by `StronglyTypedIdJsonConverter<TStronglyTypedId, TValue>` is the bare `TValue` (a JSON string for `Guid`/`string`, a JSON number for `int`/`long`) — never an object wrapper. This is a deliberate compatibility choice so strongly-typed IDs serialize identically to their underlying primitive across service boundaries.
- `StronglyTypedIdJsonConverterFactory` is **opt-in** — `SharedKernel.Domain` does not call `JsonSerializerOptions.Converters.Add` anywhere itself and ships no global STJ configuration. Consuming services register it explicitly: `options.Converters.Add(new StronglyTypedIdJsonConverterFactory())`.
- No static mutable state anywhere in this domain.
- No persistence concerns (`DbContext`, repository interfaces, EF annotations) — those live in `06.Persistence`.
- No messaging concerns (`IMessageBus`, `IEventPublisher`) — those live in `07.Messaging`.
- Specifications built in concrete subclass constructors only — no fluent builder calls outside the constructor.
- `Specification<T>.IsSatisfiedBy(T entity)` is for **in-domain and in-test use only** — repository implementations in `06.Persistence` must not call it. The compiled delegate cache is instance-scoped; reusing a specification instance across requests is safe.
- `AsNoTracking = false` is the safe default — it does not cause data loss. Set it `true` only for read-only query specifications. Composite specifications inherit `true` if either operand is `true` (more restrictive wins).
- **`BusinessRuleViolationException` carries `Error.Type == ErrorType.BusinessRule`** — this maps to HTTP 422 in the presentation layer. It must never use `Error.Unexpected` (which maps to HTTP 500). The error code is `ErrorCodes.Domain.RuleViolated` from `SharedKernel.Primitives`.
- **`AggregateRoot<TId>.Version` is a domain-native optimistic concurrency helper** — starts at 0, increments by 1 on every `RaiseDomainEvent` call. This is distinct from `IHasConcurrency.RowVersion` which is an infrastructure-specific SQL Server byte array. `ClearDomainEvents()` does not decrement `Version`.
- **`TryCreate<T>` is the recommended aggregate factory helper** — use `protected static Result<T> TryCreate<T>(Func<T> factory)` in static `Create(...)` factory methods on aggregate roots to produce railway-friendly construction that converts exceptions to `Result.Failure` values.
- **`ISpecification<T>.IncludeDeleted` bypasses ALL EF Core global query filters** — the repository implementation calls `IgnoreQueryFilters()` when `IncludeDeleted = true`. Because EF Core's `IgnoreQueryFilters()` cannot target a single filter, it bypasses every global query filter on the entity type, including any tenant isolation filter registered in a `TenantedDbContext`. For tenant-scoped soft-delete queries, always pair `IncludeSoftDeleted()` with an explicit `AddCriteria(e => e.TenantId == tenantId)` call so the tenant boundary is re-enforced at the query level.
- `IncludeDeleted = false` is the safe default — it does not change existing query behaviour. Call `IncludeSoftDeleted()` only in constructors of admin/audit/export/recovery specifications. Never set it in read-model or user-facing query specifications.
- Composite specifications (`AndSpecification<T>`, `OrSpecification<T>`, `NotSpecification<T>`) propagate `IncludeDeleted = true` when any operand has it set (more-permissive wins). This mirrors the `AsNoTracking` propagation rule.
- **Composite specifications must union `Includes` and `StringIncludes`** (WO-051/P-307) — `AndSpecification<T>`, `OrSpecification<T>`, and `NotSpecification<T>` all append every operand's expression-based `Includes` and string-based `StringIncludes` into the composite. Before this fix, `AndSpecification<T>` unioned only `StringIncludes` and dropped `Includes`; `OrSpecification<T>`/`NotSpecification<T>` dropped both — a silent eager-loading data-loss defect in the platform's most fundamental composition mechanism. Duplicate entries are tolerated (the persistence-layer evaluator treats duplicate `.Include()` calls as idempotent) — no dedupe logic is required.
- **`KeysetSpecification<T, TKey>`'s Id tiebreaker is mandatory, never optional** (WO-051/P-308) — a keyset/cursor page boundary is unsound without a unique deterministic sort order. The constructor always calls `ApplyThenBy(idSelector, descending: false)` regardless of caller input; there is no way to construct a `KeysetSpecification<T, TKey>` without it.
- **`KeysetSpecification<T, TKey>`'s `TKey` carries a `struct` constraint in addition to `IComparable<TKey>`** (WO-051/C-39, corrected post-implementation) — required for `AfterKey`'s `TKey?` to compile to a genuine `Nullable<TKey>` rather than erasing to plain `TKey` for value-type closures, which C# does for a type parameter constrained only by an interface. This narrows the type to value-typed sort keys only (`DateTimeOffset`, `int`, `long`, `Guid`, etc.) — a reference-typed sort key is not supported by this base.
- **`AsSplitQuery` composite propagation mirrors `AsNoTracking`/`IncludeDeleted` exactly** (WO-051/P-308) — more-permissive wins; default `false` is always safe (a single-query plan is never wrong, only potentially less efficient with multiple collection includes).
- **`IHasAggregateId<TId>` is purely opt-in** (WO-051/P-309) — `IDomainEvent`/`DomainEvent`/`DomainEvent<TPayload>` carry no new required member. A concrete event either implements it or doesn't; infrastructure code detects it via `is IHasAggregateId<TId>` pattern matching, never a reflection-based property-name convention.
- **`ValueObject.TryCreate<T>`/`CheckRule` achieve full parity with `AggregateRoot<TId>`'s helpers of the same name and shape** (WO-051/P-310) — both base types now offer the identical railway-friendly construction helper. `SingleValueObject<TValue>` inherits both for free since it extends `ValueObject`.
- **Guard-clause adoption via the `SharedKernel.Guards` namespace** (WO-051/P-311; physically hosted inside `SharedKernel.Core` since WO-082/P-505/P-509 — the namespace was deliberately preserved on that merge, so this remains a `using SharedKernel.Guards;` call site unchanged) — `AggregateRoot<TId>`'s `clock` constructor parameter is guarded via `Guard.Throw.Null(clock, nameof(clock))`; this single guard site protects all three tenanted aggregate bases too, since each chains through `base(id, clock)` to this same constructor. `StronglyTypedId<TValue>.Value` and `SingleValueObject<TValue>.Value` are guarded against null for reference-type `TValue` instantiations only (a `TValue`-safe null check, since `Guard.Throw.Null<T>`'s `where T : class` constraint cannot apply to an unconstrained `TValue`) — a no-op for value-type instantiations.
- **`Entity<TId>.id` remains deliberately unguarded** (WO-051/P-311) — `default(TId)` is the intentional "transient entity" sentinel (see `IsTransient()`), not an error condition. Do not add a null/default guard to `Entity<TId>`'s constructor; doing so would break every legitimate transient-entity construction path.
- **`DomainEventVersionHelper.GetVersion(Type)` caches its reflection lookup** (WO-051/P-311) — a `ConcurrentDictionary<Type, int>` ensures the `GetCustomAttribute` call happens at most once per distinct `Type`, since this helper (unlike `StronglyTypedIdJsonConverterFactory`, which STJ itself caches) is called directly by infrastructure on a potential per-message hot path.
- **`Entity<TId>` and `ValueObject` implement `IEquatable<T>`** (WO-051/P-311) — `Equals(T? other)` delegates to the existing `Equals(object?)` override; this is a boxing/virtual-dispatch-avoidance addition for generic-collection consumers (`List<T>.Contains`, `Dictionary` keys, LINQ `Distinct`/`Except`), not a behavior change. `Entity<TId>`'s `Equals(object?)`/`GetHashCode()` remain `sealed override`.
- **`IPolicy<T>.Explain` is a default interface member, not a required override** (WO-051/P-312) — this is the mechanism that makes the addition zero-breaking-change: any pre-existing `IPolicy<T>` implementor that only ever declared `IsCompliant` continues to compile unmodified. Composite policies (`AndPolicy<T>`, `OrPolicy<T>`, `NotPolicy<T>`) override `Explain` to aggregate/synthesize a meaningful message rather than relying on the generic DIM default.
- **`Specification<T>.Create(criteria)` is for one-off filters only** (WO-051/P-313) — it does not relax the existing constructor-only/no-fluent-chaining builder-method rule for named, reusable specifications. A domain concept that will be referenced from more than one call site must still be its own dedicated `Specification<T>` subclass.
- **`Money.Amount` is unconditionally rounded at construction** (WO-066/P-439) — there is no "reject excess precision" validation path. `Money.Create(10.005m, Usd)` does not throw; it silently rounds to the currency's minor-unit precision per the supplied `RoundingPolicy`. Callers that need to detect precision loss must compare the input against the constructed `Money.Amount` themselves before calling `Create`.
- **`Money`'s private constructor does NOT use the `SingleValueObject<TValue>` field-initializer-before-`base()` technique** (WO-066/C-51, corrected post-implementation — same class of design/compiler-mismatch fix as WO-051/C-39's `KeysetSpecification<T,TKey>` constraint correction) — that technique requires a primary constructor, and a primary constructor's accessibility on a `sealed`, non-abstract class always matches the class's own (public), with no C# mechanism to restrict it further; it only stays effectively `protected` for `SingleValueObject<TValue>` because that base is `abstract`. `Money` instead uses a traditional private constructor that calls `Guard.Throw.Null(currency, nameof(currency))` directly in the constructor body — before assigning `Amount`/`Currency` — mirroring `AggregateRoot<TId>`'s clock-parameter guard exactly (WO-051/P-311: throws `DomainException` immediately, same call shape). `Money.Validate()` is consequently a no-op (`return null;`) — there is nothing left for it to check once the constructor's own guard has already run. Any future `ValueObject` subclass that is both `sealed` and needs more than one constructor-parameter-derived field pre-validated must use this same constructor-body-guard pattern, not the field-initializer trick — the field-initializer trick is only viable when the class owning the trick is itself `abstract`.
- **Cross-currency `Money` operations are a `BusinessRuleViolationException`, not a silent coercion or a separate exception type** (WO-066/P-439) — `Add`, `Subtract`, and `CompareTo` (and by extension the `+`/`-`/`<`/`<=`/`>`/`>=` operators) all call the existing `ValueObject.CheckRule` (WO-051/P-310) with a `CurrencyMismatchRule`. This reuses the already-shipped `BusinessRuleViolationException`/`ErrorType.BusinessRule` → HTTP 422 pipeline exactly as every other domain rule violation does — no new exception hierarchy was introduced for `Money`.
- **`CurrencyCatalog` is a fixed, compile-time dataset — never I/O-backed** (WO-066/P-439) — a currency's minor-unit exponent is looked up from a static in-memory table, never fetched from an external service or configuration at runtime. Adding, removing, or correcting a currency's exponent requires a new `SharedKernel.Domain` package version.
- **`Money.Allocate` must never lose or invent a minor unit** (WO-066/P-439) — the largest-remainder (Hare–Niemeyer) algorithm is a hard correctness requirement, not an implementation detail: `Sum(money.Allocate(n)) == money` must hold for every valid `n`/ratio set. A naive equal-division-with-truncation implementation is a defect, not an acceptable approximation. **Leftover-minor-unit tie-breaking is by descending original index** (WO-066/C-53, clarified post-implementation — not specified in the original design) — when two or more shares have an equal remainder, the LAST (highest-index) tied share receives the leftover minor unit first, matching the platform's own worked example (`$10.00` split three ways → `[3.33, 3.33, 3.34]`, never `[3.34, 3.33, 3.33]`).
- **`IExchangeRateProvider` is a pure, zero-I/O domain port** (WO-066/P-439) — `03.Domain` ships the interface and the `MoneyExtensions.ConvertAsync` composition helper only. No implementation, no hardcoded rate table, and no embedded `HttpClient`/SDK call may ever be added to this package — the consuming service supplies the real adapter (typically bridged via `11.Communication`) at its own composition root, mirroring the `IDomainEventDispatcher` opt-in-implementation pattern.
- **Percentage/interest-calculation helpers are explicitly out of scope** (WO-066/P-439) — do not add them speculatively. They are a documented future extension, to be added only if a real consumer need materializes.

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
- `IDomainEventDispatcher` is a plain interface with no generic type parameters, no reflection, and no attribute usage — AOT-safe. Implementations live outside this package; their AOT compatibility is a consumer concern.
- Specification expression trees (`Expression<Func<T, bool>>`) are AOT-safe when the expressions do not involve runtime-only reflection APIs.
- `ExpressionVisitor` / `ParameterReplacer` in specification composites: AOT-safe as these operate on already-compiled expression trees with no runtime type discovery.
- `Specification<T>.IsSatisfiedBy` calls `Criteria.Compile()` — expression compilation is AOT-safe for expressions that do not use late-bound reflection inside the lambda body.
- `ISpecification<T>.IncludeDeleted` is a `bool` property — BCL primitive, no reflection, AOT-safe. `Specification<T>.IncludeSoftDeleted()` is a simple field write — AOT-safe.
- No `Activator.CreateInstance`, no `Assembly.Load`, no reflection in hot paths.
- `StronglyTypedIdJsonConverterFactory.CreateConverter` uses `Activator.CreateInstance` on a closed generic converter type, and `StronglyTypedIdJsonConverter<TStronglyTypedId, TValue>` compiles a `Func<TValue, TStronglyTypedId>` activator once per closed type via `Expression.New` over the concrete `(TValue Value)` primary constructor. Both occur once per closed type — `CreateConverter` is called once per type by STJ and the result is cached by `JsonSerializerOptions`; the compiled `Expression` delegate is cached for the converter's lifetime. This is the same class of startup-time, type-inspection-only reflection as `DomainEventVersionHelper.GetVersion(Type)` — acceptable per the repo's pragmatic AOT guidance, not used in per-element hot paths. Native AOT trimming of `Expression.Compile()` requires the `System.Linq.Expressions` interpreter fallback or the `RequiresDynamicCode`/`RequiresUnreferencedCode` annotations on the converter's `CreateConverter`/factory methods if a consuming service publishes with `PublishAot=true`; this is documented as a consumer-facing caveat, not a hard blocker.
- `KeysetSpecification<T, TKey>` (WO-051/P-308) is an abstract class built entirely on `Expression<Func<T,...>>` trees composed through the same `ApplyOrderBy`/`ApplyThenBy`/`ApplyPaging` builder methods every other specification already uses — no reflection, AOT-safe by the same reasoning as `PagedSpecification<T>`.
- `ISpecification<T>.AsSplitQuery` (WO-051/P-308) is a `bool` property — BCL primitive, no reflection, AOT-safe. `Specification<T>.ApplySplitQuery()` is a simple field write — AOT-safe.
- `IHasAggregateId<TId>` (WO-051/P-309) is a marker interface with a single generic property — no reflection, AOT-safe.
- `ValueObject.TryCreate<T>`/`CheckRule` (WO-051/P-310) use try/catch on known exception types with no reflection — AOT-safe, identical reasoning to `AggregateRoot<TId>.TryCreate<T>`.
- The `SharedKernel.Guards` namespace's `Guard.Throw.Null<T>` (WO-051/P-311; hosted inside `SharedKernel.Core` since WO-082/P-505/P-509) is already a proven AOT-safe pattern in `01.Core` (static dispatch, `EqualityComparer<T>.Default`-based checks, no reflection) — its adoption here introduces no new AOT concern. `Entity<TId>.Equals(Entity<TId>? other)` and `ValueObject.Equals(ValueObject? other)` (`IEquatable<T>` additions) are ordinary instance methods delegating to the existing `Equals(object?)` override — no reflection, AOT-safe.
- `DomainEventVersionHelper.GetVersion(Type)`'s `ConcurrentDictionary<Type, int>` cache (WO-051/P-311) adds no new AOT concern — `ConcurrentDictionary<TKey,TValue>` is a standard BCL generic collection; the underlying `GetCustomAttribute<T>()` reflection call remains the same class of startup/first-use-time, type-inspection-only reflection already accepted for this helper.
- `IPolicy<T>.Explain` (WO-051/P-312) is a C# default interface member — default interface methods are resolved at compile/JIT time via the interface's vtable slot, not via reflection; fully AOT-safe, no `[RequiresUnreferencedCode]` needed.
- `Specification<T>.Create(criteria)` and the internal `CriteriaSpecification<T>` (WO-051/P-313) are a plain static factory method and a sealed class wrapping an `Expression<Func<T,bool>>` via the existing `AddCriteria` builder — no reflection, AOT-safe, identical reasoning to `AllSpecification<T>`/`EmptySpecification<T>`.
- `RoundingPolicy` (WO-066/P-439) is a plain `enum`; the rounding helper is a static method calling `Math.Round` — no reflection, AOT-safe.
- `CurrencyCatalog` (WO-066/P-439) is a static `Dictionary<string, int>` lookup — a standard BCL generic collection, no reflection, AOT-safe.
- `Currency` (WO-066/P-439) extends `SingleValueObject<string>` — already-proven AOT-safe base; `MinorUnitDigits`'s derived-property lookup is a plain dictionary read, no reflection.
- `Money` (WO-066/P-439) extends `ValueObject` — already-proven AOT-safe base. `GetEqualityComponents()`, arithmetic/comparison members, and `Allocate` are all plain arithmetic and BCL collection operations — no reflection, no `Activator.CreateInstance`, no `Expression.Compile()`.
- `CurrencyMismatchRule` (WO-066/P-439) is a plain sealed class implementing `IBusinessRule` — identical reasoning to `AndBusinessRule`/`OrBusinessRule`/`NotBusinessRule` — no reflection, AOT-safe.
- `IExchangeRateProvider`/`MoneyExtensions.ConvertAsync` (WO-066/P-439) is a plain interface plus a `Task`-returning extension method — no reflection, no generic type discovery, AOT-safe; identical reasoning to `IDomainEventDispatcher`. Implementations live outside this package; their AOT compatibility is a consumer concern.

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
- `ISpecification<T>.IncludeDeleted`: default `false`; `IncludeSoftDeleted()` sets `true`; `AndSpecification<T>` / `OrSpecification<T>` propagate `true` when either operand is `true`; remain `false` when both operands are `false`; `NotSpecification<T>` propagates `true` when operand is `true`; all existing specification tests continue to pass (additive change only).
- `IDomainEventDispatcher` `ContractShapeTests`: (a) interface exists in assembly `SharedKernel.Domain` under namespace `SharedKernel.Domain`; (b) has exactly one method `DispatchAsync`; (c) method signature is `Task DispatchAsync(IReadOnlyList<IDomainEvent>, CancellationToken)` verified via reflection; (d) interface is `public`; (e) `IDomainEvent` parameter type resolves to the existing interface from the same package (no external type references introduced).
- `StronglyTypedIdJsonConverterFactory` / `StronglyTypedIdJsonConverter<TStronglyTypedId, TValue>`: round-trip serialize/deserialize for concrete test ID records over each supported `TValue` (`Guid`, `int`, `long`, `string`) preserves equality; serialized JSON for each is the bare primitive token, not `{"value": ...}`; `CanConvert` returns `false` for unrelated types (plain `string`, a non-`StronglyTypedId` `ValueObject` subclass); a strongly-typed ID property inside a containing DTO record round-trips correctly when the factory is registered; without the factory registered, default STJ record serialization produces the object-wrapper shape (documents the opt-in contract).
- `AndSpecification<T>`/`OrSpecification<T>`/`NotSpecification<T>` `Includes`/`StringIncludes` propagation (WO-051/P-307): compose two specs where only one operand declares an `Include`/`AddStringInclude`, for each of `And`/`Or`/`Not`; composed spec's `Includes`/`StringIncludes` contains the expected entries; existing `AsNoTracking`/`IncludeDeleted` propagation tests continue to pass unmodified.
- `KeysetSpecification<T, TKey>` (WO-051/P-308): first-page cursor (`afterKey`/`afterId` both null) produces no seek-predicate contribution; `OrderBy`/`ThenBys` reflect the key selector + mandatory Id tiebreaker in the correct order; `take < 1` throws `ArgumentOutOfRangeException`; a partial cursor (only one of `afterKey`/`afterId` supplied) throws `ArgumentException`; `AfterKey`/`AfterId`/`Descending`/`Take` readable and correct post-construction — all evaluated in isolation against expression trees, no database required.
- `ISpecification<T>.AsSplitQuery` (WO-051/P-308): default `false`; `ApplySplitQuery()` sets `true`; `AndSpecification<T>`/`OrSpecification<T>`/`NotSpecification<T>` propagate `true` when either/the operand has it set; both-`false` operands propagate `false` — mirrors the existing `AsNoTracking` test suite exactly.
- `IHasAggregateId<TId>` (WO-051/P-309): a concrete event implementing both `DomainEvent<TPayload>` and `IHasAggregateId<TId>` is detected via `is IHasAggregateId<TId>` pattern matching and exposes the expected `AggregateId`; an event not implementing the marker is unaffected (pattern-match returns `false`, no exception).
- `ValueObject.TryCreate<T>`/`CheckRule` (WO-051/P-310): mirrors the existing aggregate-side `TryCreateTests.cs` coverage — success path; `BusinessRuleViolationException` → `Result.Failure` with `ErrorType.BusinessRule`; `ValidationException` → `Result.Failure`; a `SingleValueObject<TValue>` subclass exercises both inherited helpers directly.
- Guard-clause adoption (WO-051/P-311): `new SomeAggregate(id, clock: null!)` (and the three tenanted variants, via the shared base-constructor chain) throws the Guards system's `DomainException`, never a downstream `NullReferenceException`; a reference-type `TValue` passed as `null!` to `StronglyTypedId<TValue>`/`SingleValueObject<TValue>` throws the same guard exception; a value-type `TValue` construction path is unaffected.
- `DomainEventVersionHelper` caching (WO-051/P-311): reflection (`GetCustomAttribute`) invoked at most once per distinct `Type` across repeated `GetVersion(Type)` calls. `Entity<TId>`/`ValueObject` `IEquatable<T>` (WO-051/P-311): `entity is IEquatable<Entity<TId>>` / `valueObject is IEquatable<ValueObject>` both `true`; typed `Equals(T? other)` matches `Equals(object?)` for equal/unequal/`null` inputs; all existing equality tests continue to pass unmodified.
- `IPolicy<T>.Explain` (WO-051/P-312): a concrete test policy demonstrates both `IsCompliant` and `Explain` returning consistent results; a policy implementing only `IsCompliant` still compiles and returns the default DIM explanation (zero-breaking-change proof); `AndPolicy<T>.Explain` aggregates non-compliant sub-policy explanations with `"; "`; `OrPolicy<T>.Explain` (both non-compliant) aggregates both; `NotPolicy<T>.Explain` (inner unexpectedly compliant) returns the fixed generic message.
- `Specification<T>.Create(criteria)` (WO-051/P-313): `IsSatisfiedBy` and expression-tree behavior identical to an equivalent named `Specification<T>` subclass; composes correctly via `.And()`/`.Or()`/`.Not()` against both another ad hoc spec and a named subclass; every non-criteria member defaults to its empty/false baseline.
- `RoundingPolicy` (WO-066/P-439): `Math.Round` delegation verified for both policies at 0/2/3-digit precisions; a midpoint value rounds differently under each policy.
- `CurrencyCatalog` (WO-066/P-439): `TryGetMinorUnitDigits` returns the correct digit count for a default-precision, zero-decimal, and three-decimal code each; returns `false` for an unknown code; `IsKnownCode` mirrors the same set.
- `Currency` (WO-066/P-439): `Create` normalizes casing and succeeds for a well-formed known code; fails for a wrong-length or well-formed-but-unknown code; equality by `Code`; `MinorUnitDigits` reflects the catalog value; well-known statics construct successfully.
- `CurrencyMismatchRule` (WO-066/P-439): `IsBroken()`/`Message` correctness, exercised standalone (independent of `Money`), proving the reuse claim.
- `Money` construction/rounding/equality (WO-066/P-439): rounding is unconditional and visible in the constructed `Amount` for both `RoundingPolicy` values; structural equality by `Amount`+`Currency`; `Zero(currency).Amount == 0m`.
- `Money` arithmetic/comparison (WO-066/P-439): same-currency `Add`/`Subtract`/`Multiply`/`Negate`/`CompareTo` produce correct results; every cross-currency path (`Add`, `Subtract`, `CompareTo`, and their operator equivalents) throws `BusinessRuleViolationException` carrying a `CurrencyMismatchRule` with `ErrorType.BusinessRule`.
- `Money.Allocate` (WO-066/P-439): `Sum(result) == original` for both the equal-split and weighted-ratio overloads, across odd/even amounts; guard exceptions for `numberOfParts < 1` and invalid `ratios`.
- `IExchangeRateProvider`/`MoneyExtensions.ConvertAsync` (WO-066/P-439): a hand-rolled test-project double proves `ConvertAsync` composes the returned rate with `Money.Create` correctly, and that a provider `Result<decimal>.Failure` short-circuits to `Result<Money>.Failure` without constructing a `Money`.

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
- [2026-06-02] P-095/WO-016 — ISpecification(T).IncludeDeleted flag added to public surface; Specification(T).IncludeSoftDeleted() builder documented; composite spec propagation rule added (IncludeDeleted = true if any operand true, mirrors AsNoTracking); implementation rules section updated with IgnoreQueryFilters() bypass warning and tenant isolation caveat; AOT note added; test rule added; version bump to 1.4.0 planned (domain-arch-planner)
- [2026-06-02] P-081/WO-014 — IDomainEventDispatcher interface added to public surface (Abstractions/ section); implementation rule added (only dispatch interface permitted; empty-list no-op and exception propagation contract; opt-in DI; MediatR impl deferred to 05.Application); AOT note added; ContractShapeTests rule added; version bump to 1.5.0 planned (domain-arch-planner)
- [2026-06-12] P-152/WO-024 — `StronglyTypedIdJsonConverterFactory` + `StronglyTypedIdJsonConverter<TStronglyTypedId, TValue>` added to public surface (new StronglyTypedIds/Serialization/ section); supports Guid/int/long/string, bare-primitive wire format, opt-in registration via `options.Converters.Add(...)`; `StronglyTypedId<TValue>` "ships none" note replaced; implementation rules added (supported TValue shapes, concrete-type constructor shape requirement, opt-in-only); AOT note added (cached `Expression.New` activator, same class as `DomainEventVersionHelper` precedent, PublishAot caveat documented); test rule added (round-trip, wire-format, CanConvert negative cases); version bump to 1.6.0 planned (domain-arch-planner)
- [2026-06-15] SK.03.Docs complete (DO-29) — `StronglyTypedId<TValue>` source XML `<remarks>` updated to describe `StronglyTypedIdJsonConverterFactory` opt-in; `SharedKernel.Domain.csproj` bumped to 1.6.0 with WO-024 release notes; CLAUDE.md content already current from prior pass; 246 tests green (domain-phase-implementer)
- [2026-06-15] SK.03.Published complete (P-10) — SharedKernel.Domain 1.6.0 packed and verified; manifest deps: Primitives + Core only; consumer-verify confirms StronglyTypedIdJsonConverterFactory/Converter exported (19/19); all 6 phases of 03.Domain now ● complete; no new architectural signals (domain-phase-implementer)
- [2026-07-29] WO-051 (P-307..P-313) — CLAUDE.md refreshed for a seven-phase gap-fill batch targeting `SharedKernel.Domain` v1.7.0: (1) corrected a pre-existing documentation gap discovered during grounding research against shipped source — `StringIncludes`/`ISpecification<T>` and `AddStringInclude`/`Specification<T>` were already shipped but never documented in this brain; both now appear in the Specification system section; (2) `AndSpecification<T>`/`OrSpecification<T>`/`NotSpecification<T>` now documented to union `Includes`/`StringIncludes`, fixing a confirmed silent eager-loading data-loss defect (P-307); (3) added `KeysetSpecification<T, TKey>` cursor/seek-pagination base (mandatory Id tiebreaker) and `ISpecification<T>.AsSplitQuery` flag with `AsNoTracking`-identical propagation (P-308); (4) added `IHasAggregateId<TId>` opt-in marker mirroring `IHasTenant` (P-309); (5) added `ValueObject.TryCreate<T>`/`CheckRule`, achieving full parity with `AggregateRoot<TId>`'s existing helpers (P-310); (6) added `SharedKernel.Guards` as a third `01.Core` `ProjectReference` (Packages table updated), guarded `AggregateRoot<TId>`'s `clock` parameter (one guard site protects all four aggregate bases via constructor chaining), guarded `StronglyTypedId<TValue>`/`SingleValueObject<TValue>`'s `Value` for reference-type `TValue`, documented `Entity<TId>.id`'s deliberate unguarded transient-sentinel exception, cached `DomainEventVersionHelper.GetVersion`'s reflection lookup via `ConcurrentDictionary<Type,int>`, and added `IEquatable<Entity<TId>>`/`IEquatable<ValueObject>` (P-311); (7) added `IPolicy<T>.Explain` as a zero-breaking-change C# default interface member with composite aggregation mirroring `AndBusinessRule.Message` (P-312); (8) added `Specification<T>.Create(criteria)` ad hoc factory extending the `AllSpecification<T>`/`EmptySpecification<T>` sealed-wrapper precedent (P-313). Implementation Rules, AOT Compatibility, and Test Rules sections all extended accordingly; 41 new tasks recorded in state-map.md across all 6 phases (domain-arch-planner, WO-051)
- [2026-07-30] SK.03.Design closed — D-34..D-42 (WO-051/P-307..P-313) independently re-verified against shipped `.cs` files (not re-trusted from the arch-planner's changelog claim alone): composite spec Include/StringInclude propagation gap, tenanted-base `base(id, clock)` constructor chaining, `AndBusinessRule.Message`'s `string.Join("; ", ...)`, `ValueObject.cs`'s current XML `<example>`, and `SharedKernel.Guards`' `Guard.Throw.Null<T>` shape all confirmed accurate; no discrepancies found; no content edits needed (designs already present) (domain-phase-implementer)
- [2026-07-30] SK.03.Core closed — C-38..C-46 (WO-051/P-307..P-313) implemented: Include/StringInclude union-propagation fix in And/Or/NotSpecification; `IHasAggregateId<TId>`; `ValueObject.TryCreate<T>`/`CheckRule`; Guards adoption; `DomainEventVersionHelper` caching + `IEquatable<T>` on `Entity<TId>`/`ValueObject`; `IPolicy<T>.Explain` DIM; `Specification<T>.Create(criteria)`. **Corrected `KeysetSpecification<T, TKey>`'s constraint during implementation** — D-35's `where TKey : IComparable<TKey>` does not compile as designed; empirically confirmed the compiler erases `TKey?` to plain `TKey` for value-type closures absent a `struct` constraint, which would have silently broken the "null = first page" cursor contract for the design's own `DateTimeOffset` example. Shipped constraint is `struct, IComparable<TKey>`, narrowing `KeysetSpecification<T, TKey>` to value-typed sort keys only (documented in the Specification system section and Implementation Rules). All 46 Core tasks now `●`; 316/316 tests green (33 new), 0 build warnings (domain-phase-implementer)
- [2026-07-30] SK.03.Docs closed — DO-30..DO-36 (WO-051/P-307..P-313) independently re-verified against shipped `.cs` files (not re-trusted from the WO-051 arch-planner changelog claim alone): `AndSpecification<T>`/`OrSpecification<T>`/`NotSpecification<T>`'s `Includes`/`StringIncludes` union propagation, `KeysetSpecification<T, TKey>`'s constructor/guards/`struct` constraint, `ISpecification<T>.AsSplitQuery` + composite propagation, `IHasAggregateId<TId>`, `ValueObject.TryCreate<T>`/`CheckRule` (including the rewritten `Money.Create` `<example>`), the `SharedKernel.Guards` `ProjectReference` plus `AggregateRoot<TId>`/`StronglyTypedId<TValue>`/`SingleValueObject<TValue>` guard sites and `Entity<TId>.id`'s deliberate unguarded exception, `DomainEventVersionHelper`'s `ConcurrentDictionary` caching, `Entity<TId>`/`ValueObject`'s `IEquatable<T>` additions, `IPolicy<T>.Explain` DIM with `AndPolicy<T>`/`OrPolicy<T>`/`NotPolicy<T>` aggregation, and `Specification<T>.Create(criteria)`/`CriteriaSpecification<T>` — all confirmed to match this brain's existing content exactly, with zero discrepancies. No content edits required; only this changelog line added. 317/317 tests green (Release). All 36 Docs tasks now `●`; SK.03.Docs phase → `●` (domain-phase-implementer)
- [2026-07-30] SK.03.Published complete (P-11) — SharedKernel.Domain 1.7.0 packed and verified; manifest deps: Primitives + Core + Guards only (zero external NuGet); consumer-verify extended with 10 new tests confirming `KeysetSpecification<T,TKey>`, `AsSplitQuery`, `IHasAggregateId<TId>`, `ValueObject.TryCreate<T>`, `IPolicy<T>.Explain`, `Specification<T>.Create(criteria)`, and the P-307 composite Include/StringInclude union-propagation fix are all exported and functioning through a real PackageReference resolution (28/28 consumer tests green, 317/317 domain tests green); all 6 phases of 03.Domain (Design, Scaffold, Core, Tests, Docs, Published) now ● complete — WO-051 v1.7.0 cycle closed end to end; no new architectural signals beyond what Core/Docs already documented (domain-phase-implementer)

- [2026-08-26] WO-066/P-439 — CLAUDE.md refreshed for `Money`, this platform's most conspicuous pre-WO-066 gap given how fintech-grade the recent work orders (WO-058 step-up auth, WO-060 FAPI 2.0 hardening) had already become. Added a new "Money system" Interface Contracts subsection: `RoundingPolicy` (`BankersRounding` default / `AwayFromZero`); `CurrencyCatalog` (fixed static ISO 4217 minor-unit-exponent table, correctly distinguishing the zero-decimal and three-decimal exceptions from the 2-digit default); `Currency` (`SingleValueObject<string>` with a derived `MinorUnitDigits`, well-known static convenience instances); `Money` (`ValueObject`, not `SingleValueObject<TValue>` — two independent components — with unconditional construction-order-safe rounding, no separate excess-precision rejection path); the full arithmetic/comparison surface plus a largest-remainder-method `Allocate` proven to conserve minor units exactly; and a zero-I/O `IExchangeRateProvider` port plus `MoneyExtensions.ConvertAsync`, mirroring `IDomainEventDispatcher`'s already-shipped async-on-a-pure-interface precedent. Added `CurrencyMismatchRule` to the Business rule system section — a public, top-level, reusable `IBusinessRule` sibling to `AndBusinessRule`/`OrBusinessRule`/`NotBusinessRule`, deliberately reusing the already-shipped `ValueObject.CheckRule`/`BusinessRuleViolationException`/`ErrorType.BusinessRule` → HTTP 422 pipeline rather than inventing a new exception type for cross-currency rejection. Implementation Rules, AOT Compatibility, and Test Rules sections all extended accordingly. Purely additive — zero new NuGet dependency, zero new `ProjectReference` (still exactly `SharedKernel.Primitives` + `SharedKernel.Core` + `SharedKernel.Guards`), zero breaking change to existing public surface. Percentage/interest-calculation helpers explicitly declared out of scope (future extension only, on real consumer need). `samples/OrderApi/Domain/Money.cs`'s evaluation-for-replacement is explicitly flagged as outside this domain's jurisdiction — a cross-cutting follow-up for `arch-lead`/root `state-map.md`, never an action `03.Domain` takes itself. Also noted: per the root `CLAUDE.md`'s 2026-08-25 versioning switch, the Published phase for this work order no longer bumps a per-package `PackageVersion`/`PackageReleaseNotes` — MinVer now derives the shipped version repo-wide from the next `git tag`. 30 new tasks recorded in state-map.md across all 6 phases (domain-arch-planner, WO-066)
- [2026-09-02] SK.03.Core closed (WO-066/C-47..C-53) — `RoundingPolicy`/`CurrencyCatalog`/`Currency`/`CurrencyMismatchRule`/`Money` fully implemented; the "Money system" subsection and Implementation Rules corrected post-implementation for one genuine design/compiler mismatch (`Money`'s private-constructor field-initializer technique cannot compile with `private` accessibility on a `sealed` non-abstract class — the currency-null-guard moved into the constructor body, `Validate()` is now documented as a no-op) plus one previously-unspecified detail (`Allocate`'s leftover-minor-unit tie-break is by descending original index, matching the `$10.00`→`[3.33, 3.33, 3.34]` worked example). 417/417 tests green, 0 build warnings (domain-phase-implementer)
- [2026-09-10] WO-082/P-509 — `SharedKernel.Guards` was merged into `SharedKernel.Core` upstream (P-505, 01.Core), retiring `01.Core/SharedKernel.Guards/` as an independent package; the `SharedKernel.Guards`/`SharedKernel.Guards.Clauses`/`SharedKernel.Guards.Descriptions` C# namespaces were deliberately preserved on the merge (now physically hosted inside `SharedKernel.Core`). `SharedKernel.Domain.csproj`'s `ProjectReference` to the now-deleted `SharedKernel.Guards.csproj` was re-pointed onto `SharedKernel.Core.csproj` — already permitted by the root layering table ("03.Domain may reference 01.Core"), so no new layering exception was needed. Verified by direct source inspection that `AggregateRoot.cs`, `Money.cs` (the only two production `Guard.Against`/`Guard.Throw` call sites in this domain), `StronglyTypedId.cs`, and `SingleValueObject.cs` (comment-only `Guard.Throw.Null<T>` mentions, no live call) all needed zero source change — their existing `using SharedKernel.Guards;` directives resolve unchanged against the new assembly. Packages table, and the Implementation Rules/AOT Compatibility current-state prose that named `SharedKernel.Guards` as a live `ProjectReference`, updated to describe it as a namespace now hosted inside `SharedKernel.Core`; all prior WO-051/WO-066 changelog entries above are left untouched as historical record of what was true when written, per the root `CLAUDE.md`'s "historical record, not current state" convention for versioned/dependency claims. `dotnet build` clean (0 warnings/0 errors); `dotnet test` 423/423 green — no test file changed. This domain's own `00.Governance` layering re-verification (`SharedKernel.ArchitectureTests`' 03.Domain rules) is deferred to P-508, which cannot build until this phase lands (`SharedKernel.ArchitectureTests` itself still references the now-deleted `SharedKernel.Guards.csproj`). No new phase/task IDs were opened in `03.Domain/state-map.md` for this change — `03.Domain` is fully `●` Published and this is a mechanical upstream-merge follow-up, not a new architectural phase; the Package Board's `SharedKernel.Guards` dependency mentions describe the last real `dotnet pack` run (P-12, WO-066) and are intentionally left as-is until the next real re-pack (domain-phase-implementer)
