# 06.Persistence — Data Access Layer

## What This Domain Is

The persistence primitives layer. Every repository, unit of work, and data-access abstraction in a downstream microservice derives from the interfaces and base types defined here. This domain may reference `01.Core`, `03.Domain`, and `05.Application` — it must never be referenced by those layers in return.

Philosophy: **Abstraction-first. Provider-swappable. Specification-driven. Interceptor-composed.**

> **Outbox scope:** The outbox pattern is owned entirely by `07.Messaging` via MassTransit's `UseEntityFrameworkOutbox`. No outbox types (`OutboxMessage`, `IOutboxWriter`, `OutboxInterceptor`) exist in this domain. Introducing any such type here is a hard violation — it creates competing infrastructure with no clear owner.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | `IRepository<T,TId>`, `IReadRepository<T,TId>`, `IUnitOfWork`, `IDbConnectionFactory`, `ISpecificationEvaluator<T>`, `ByIdSpecification<T,TId>` — pure interface library; no outbox types | `SharedKernel.Primitives`, `SharedKernel.Domain`, `SharedKernel.Contracts` (added P-080 — required for `PagedList<T>` in `IReadRepository.ListPagedAsync`) |
| `SharedKernel.Persistence.EfCore` | EF Core implementation: `EfRepository<T,TId>`, `EfReadRepository<T,TId>`, `EfUnitOfWork`, `SharedKernelDbContext`, `SpecificationEvaluator<T>`, interceptors (Audit, SoftDelete, Concurrency — no OutboxInterceptor), `TenantedDbContext`, `EfCorePersistenceBuilder` | `SharedKernel.Persistence.Abstractions`, `SharedKernel.Domain`, `Microsoft.EntityFrameworkCore` 10.0.5, `Microsoft.EntityFrameworkCore.Relational` 10.0.5, `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.5 (must match EFCore transitive — NU1605 fires if pinned lower) |
| `SharedKernel.Persistence.PostgreSQL` | PostgreSQL-specific conventions: `SnakeCaseNamingConvention`, `UsePostgreSQL()` DI extension, JSONB column support (`HasJsonbColumn`, `JsonbColumnAttribute`), pgvector support (`HasVectorColumn`, `VectorColumnAttribute`), `NpgsqlConnectionFactory`, `AddSharedKernelPostgreSQL()` DI extension | `SharedKernel.Persistence.EfCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x, `Pgvector.EntityFrameworkCore` |
| `SharedKernel.Persistence.Dapper` | Dapper micro-ORM read-side: `StronglyTypedIdTypeHandler<TStronglyTypedId,TValue>`, `SmartEnumTypeHandler<TEnum,TValue>`, `DapperTypeHandlers` (idempotent `Register()`), `DapperReadService` base, `AddSharedKernelDapper()` DI extension | `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.PostgreSQL` (for `NpgsqlConnectionFactory`), `Dapper` |

All packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
| --- | --- |
| Repository pattern | Pure C# 13 interfaces in `.Abstractions` — no ORM dependency |
| Unit of work | Pure C# 13 interface (`IUnitOfWork`) — provider-agnostic |
| EF Core ORM | `Microsoft.EntityFrameworkCore` 10.x |
| EF Core interceptors | `ISaveChangesInterceptor` — Audit, SoftDelete, Concurrency (three total; no OutboxInterceptor) |
| Specification evaluation | Custom `SpecificationEvaluator<T>` translating `ISpecification<T>` to `IQueryable<T>` |
| PostgreSQL provider | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x |
| JSONB | Npgsql built-in JSON column support with STJ serialization |
| Vector search | `Pgvector.EntityFrameworkCore` for pgvector typed columns |
| Micro-ORM (read side) | `Dapper` |
| Connection factory | `Npgsql` (PostgreSQL) via `NpgsqlConnectionFactory` in the Dapper package |
| Multi-tenancy | `ITenantProvider` (from `SharedKernel.Security.Abstractions`) + `TenantedDbContext` + `TenantedRepository<T,TId>`; `NoOpTenantProvider` (returning `Guid.Empty`) registered by `.WithMultiTenancy()` as fallback |
| DI composition | `EfCorePersistenceBuilder` fluent builder via `AddSharedKernelEfCore<TContext>` |

---

## Interface Contracts

### `SharedKernel.Persistence.Abstractions` — public surface

> Zero ORM NuGet dependencies. References only `SharedKernel.Primitives` and `SharedKernel.Domain`.
> No outbox types exist here — outbox is entirely within `07.Messaging`.

#### Repository interfaces (`Repositories/`)

```text
IRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
    .GetByIdAsync(TId id, CancellationToken ct)                                            → Task<TAggregate?>
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<TAggregate?>
    .AddAsync(TAggregate aggregate, CancellationToken ct)                                  → Task
    .UpdateAsync(TAggregate aggregate, CancellationToken ct)                               → Task
    .DeleteAsync(TAggregate aggregate, CancellationToken ct)                               → Task
    .ExistsAsync(TId id, CancellationToken ct)                                             → Task<bool>
    .AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)               → Task
    .UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)            → Task
    .DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)            → Task
    NOTE: Write-side only. Does not expose IQueryable or raw SQL.
          GetBySpecAsync returns a TRACKED entity by default (spec's AsNoTracking flag honored).
          Write-side callers should use specs with AsNoTracking == false so subsequent mutations
          are detected by EF change detection without requiring an explicit .Update() call.
          ExistsAsync issues an EXISTS/ANY check — never materializes the aggregate.
          Bulk methods stage mutations without committing — same semantics as single-entity counterparts.

IReadRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<TAggregate?>
    .ListAsync(ISpecification<TAggregate> spec, CancellationToken ct)                      → Task<IReadOnlyList<TAggregate>>
    .CountAsync(ISpecification<TAggregate> spec, CancellationToken ct)                     → Task<int>
    .AnyAsync(ISpecification<TAggregate> spec, CancellationToken ct)                       → Task<bool>
    .GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct)                             → Task<IReadOnlyList<TAggregate>>
    .ListPagedAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<PagedList<TAggregate>>
    .ListProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct)    → Task<IReadOnlyList<TResult>>
    .GetBySpecProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> s, ct)  → Task<TResult?>
    .ListPagedProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct) → Task<PagedList<TResult>>
    NOTE: Read-side only. Specifications control tracking, filtering, ordering, paging, and includes.
          Callers use ReadOnlySpecification<T> or PagedSpecification<T> for read-heavy paths.
          GetByIdsAsync translates to IN (...) SQL; result order is not guaranteed; missing IDs produce
          no entry; performance degrades above 1000 IDs (chunk at the application layer).
          ListPagedAsync issues two DB round-trips (count + data) under the same DbContext scope.
          ListPagedProjectedAsync<TResult> (P-101): same two-round-trip pattern; count query uses GetQuery
          (no projection), data query uses GetProjectedQuery (with selector); returns PagedList<TResult>.
          Use this instead of ListPagedAsync when the caller needs DTOs, not aggregate roots.
          PagedList<T> is defined in SharedKernel.Contracts (04.Contracts).
          BREAKING CHANGE (P-080): GetByIdAsync has been removed from IReadRepository.
          Migration: replace readRepo.GetByIdAsync(id, ct) with
                     readRepo.GetBySpecAsync(new ByIdSpecification<TAggregate, TId>(id), ct).
          Note: IRepository (write side) retains its own GetByIdAsync — only the read side is affected.
```

#### Unit of Work (`UnitOfWork/`)

```text
IUnitOfWork
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>
    NOTE: Wraps the provider's commit boundary. EF Core implementation fires all three interceptors
          (Audit, SoftDelete, Concurrency) inside the same SaveChanges call.
          This is the ONLY permitted save boundary — calling DbContext.SaveChangesAsync directly
          outside EfUnitOfWork is a hard violation.

IPersistenceTransaction  (IAsyncDisposable)
    .CommitAsync(CancellationToken ct)                         → Task
    .RollbackAsync(CancellationToken ct)                       → Task
    NOTE: Provider-agnostic transaction handle. EF Core implementation wraps IDbContextTransaction.
          Callers must await DisposeAsync() (or use await using) — the handle disposes the underlying
          transaction resource. Zero ORM dependencies — lives in Abstractions (BCL types only).

ITransactionalUnitOfWork  (extends IUnitOfWork)
    .BeginTransactionAsync(CancellationToken ct)               → Task<IPersistenceTransaction>
    NOTE: Opens an explicit database transaction. Multiple repository operations within the
          returned IPersistenceTransaction scope are committed atomically via CommitAsync or
          rolled back via RollbackAsync. Domain event dispatch fires after CommitAsync, consistent
          with EfUnitOfWork semantics. Register via EfCorePersistenceBuilder.WithTransactionalUnitOfWork().
          Application layer injects ITransactionalUnitOfWork — never IDbContextTransaction directly.
```

#### Connection factory (`Connections/`)

```text
IDbConnectionFactory
    .CreateConnectionAsync(CancellationToken ct)               → Task<IDbConnection>
    NOTE: Returns an open connection. Caller is responsible for disposal.
          Connection pooling is provider-managed. This factory is not restricted to Dapper —
          any component needing a raw IDbConnection may inject it.
```

#### Specification evaluator contract (`Specifications/`)

```text
ISpecificationEvaluator<T>
    .GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec) → IQueryable<T>
    .GetProjectedQuery<TResult>(IQueryable<T> inputQuery, IProjectionSpecification<T,TResult> spec) → IQueryable<TResult>
    NOTE: Applies criteria, includes (expression + string), ordering, paging, distinct, and AsNoTracking.
          GetProjectedQuery applies the full aggregate pipeline (steps 0–7 + 2b) then step 8 (Select).
          Both methods live in Abstractions so alternative evaluators (e.g., for Cosmos) implement the same
          interface without coupling to EF Core.
          BREAKING from original design: GetProjectedQuery was previously only on the concrete
          SpecificationEvaluator<T>; it is now on the interface (P-097). EfReadRepository no longer
          downcasts to the concrete type — any ISpecificationEvaluator<T> implementation must implement both.
          All ISpecificationEvaluator<T> implementations must read both spec.Includes (expression-based)
          and spec.StringIncludes (string-based) to be complete — omitting StringIncludes is a silent bug.

IProjectionSpecification<TAggregate, TResult>  (extends ISpecification<TAggregate>)
    .Selector                                                   → Expression<Func<TAggregate, TResult>>
    NOTE: Prerequisite for projection-read methods (ListProjectedAsync, GetBySpecProjectedAsync).
          Selector is an expression tree — AOT-safe on IQueryable.
          Implementations extend a concrete Specification<TAggregate> base and supply the Selector expression.
          Zero ORM dependencies — lives in Abstractions.

ByIdSpecification<TAggregate, TId>  (sealed class, implements ISpecification<TAggregate>)
    constructor: ByIdSpecification(TId id)
    .Criteria                                                   → Expression<Func<TAggregate, bool>>
    NOTE: Sets Criteria = e => e.Id.Equals(id). Expression tree — AOT-safe.
          Canonical replacement for the removed IReadRepository.GetByIdAsync.
          Usage: readRepo.GetBySpecAsync(new ByIdSpecification<TAggregate, TId>(id), ct).
```

---

### `SharedKernel.Persistence.EfCore` — public surface

#### DbContext base (`Context/`)

```text
SharedKernelDbContext  (abstract class, extends DbContext)
    protected SharedKernelDbContext(DbContextOptions options,
                                    IEnumerable<ISaveChangesInterceptor> additionalInterceptors = default)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>  (override — interceptors fire here)
    NOTE: Concrete downstream DbContexts must extend this base.
          Constructor registers platform interceptors (AuditInterceptor, SoftDeleteInterceptor,
          ConcurrencyInterceptor) first, then any additional interceptors supplied via the second parameter.
          Platform interceptors always fire before consumer-supplied interceptors — this ordering is
          intentional and cannot be overridden.
          No OutboxInterceptor — outbox is MassTransit's concern at 07.Messaging.
          OnModelCreating calls modelBuilder.ApplyConfigurationsFromAssembly for the calling assembly.
          Does not declare any entity DbSets — those belong to the consuming service's DbContext subclass.
```

#### EF Core repositories (`Repositories/`)

```text
EfRepository<TAggregate, TId>  (abstract class, implements IRepository<TAggregate, TId>)
    .GetByIdAsync(TId id, CancellationToken ct)                                            → Task<TAggregate?>
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<TAggregate?>
    .AddAsync(TAggregate aggregate, CancellationToken ct)                                  → Task
    .UpdateAsync(TAggregate aggregate, CancellationToken ct)                               → Task   ← tracking-aware: skips .Update() for tracked entities
    .DeleteAsync(TAggregate aggregate, CancellationToken ct)                               → Task
    .ExistsAsync(TId id, CancellationToken ct)                                             → Task<bool>
    .AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)               → Task
    .UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)            → Task
    .DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct)            → Task
    protected virtual .MarkAsModifiedIfDetached(TAggregate aggregate)                      → void
    NOTE: Backed by DbContext.Set<TAggregate>(). Concrete repositories extend this — do not register
          EfRepository<T,TId> directly in DI without a concrete subclass. Never exposes IQueryable.
          GetBySpecAsync calls ISpecificationEvaluator<T>.GetQuery then FirstOrDefaultAsync; returns a
          tracked entity by default (spec's AsNoTracking flag honored — write-side callers should not set it).
          UpdateAsync checks DbContext.Entry(aggregate).State: if Detached calls .Update(); otherwise
          EF change detection handles dirty tracking automatically (avoids full-column UPDATE statements).
          UpdateRangeAsync applies the same detached-state check per entity. UpdateRange itself is synchronous.

EfReadRepository<TAggregate, TId>  (abstract class, implements IReadRepository<TAggregate, TId>)
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<TAggregate?>
    .ListAsync(ISpecification<TAggregate> spec, CancellationToken ct)                      → Task<IReadOnlyList<TAggregate>>
    .CountAsync(ISpecification<TAggregate> spec, CancellationToken ct)                     → Task<int>
    .AnyAsync(ISpecification<TAggregate> spec, CancellationToken ct)                       → Task<bool>
    .GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct)                             → Task<IReadOnlyList<TAggregate>>
    .ListPagedAsync(ISpecification<TAggregate> spec, CancellationToken ct)                 → Task<PagedList<TAggregate>>
    .ListProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct)    → Task<IReadOnlyList<TResult>>
    .GetBySpecProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> s, ct)  → Task<TResult?>
    .ListPagedProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct) → Task<PagedList<TResult>>
    NOTE: Uses ISpecificationEvaluator<T> internally. Pass ReadOnlySpecification<T> subclasses to avoid
          unnecessary change-tracking. AsNoTracking() applied when spec.AsNoTracking == true.
          GetByIdsAsync uses an expression-tree Contains predicate (e => ids.Contains(e.Id)) so that
          registered ValueConverters (e.g., StronglyTypedIdValueConverter) are applied at the property
          level by the LINQ provider, generating a server-side IN (...) SQL clause. The former EF.Property
          approach has been removed — it caused silent client-side evaluation with strongly-typed IDs.
          ListPagedAsync: count query strips Skip/Take; data query applies full spec; both under same DbContext.
          Projection methods delegate to ISpecificationEvaluator<T>.GetProjectedQuery (interface method since P-097);
          no downcast to concrete SpecificationEvaluator<T> — any ISpecificationEvaluator<T> implementation works.
          ListPagedProjectedAsync: count query uses GetQuery (no projection), data query uses GetProjectedQuery;
          returns PagedList<TResult>. Use instead of ListPagedAsync when caller needs DTOs.
          Select applied after paging in all projection paths.
          GetByIdAsync has been removed (P-080 breaking change) — use GetBySpecAsync(new ByIdSpecification<>(...)).
```

#### EF Core unit of work (`UnitOfWork/`)

```text
EfUnitOfWork  (sealed class, implements IUnitOfWork)
    constructor: EfUnitOfWork(SharedKernelDbContext dbContext, IDomainEventDispatcher? dispatcher = null)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>
    NOTE: Exactly one public constructor. IDomainEventDispatcher? is a nullable optional parameter resolved
          by the DI container — no second constructor. Adding a second constructor creates DI ambiguity
          (container may silently pick the shorter one and skip the dispatcher). This is a hard violation.
          Delegates to SharedKernelDbContext.SaveChangesAsync.
          All three interceptors (Audit, SoftDelete, Concurrency) fire automatically before the commit.
          No OutboxInterceptor — MassTransit's UseEntityFrameworkOutbox handles outbox in 07.Messaging.
          After SaveChangesAsync succeeds: collects all domain events from ChangeTracker.Entries<IHasDomainEvents>();
          calls IDomainEventDispatcher.DispatchAsync(events, ct) if a dispatcher is registered;
          clears domain events on each aggregate regardless of dispatcher registration (prevents double-dispatch).
          IDomainEventDispatcher is from SharedKernel.Domain (03.Domain) — no 05.Application reference.
          Dispatcher is optional: consuming services opt in by registering IDomainEventDispatcher in DI.
          Dispatch failure does not roll back the already-committed transaction (document as known trade-off).

EfTransactionalUnitOfWork  (sealed class, implements ITransactionalUnitOfWork)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>      (inherited)
    .BeginTransactionAsync(CancellationToken ct)               → Task<IPersistenceTransaction>
    NOTE: Registered only when EfCorePersistenceBuilder.WithTransactionalUnitOfWork() is called.
          Wraps DbContext.Database.BeginTransactionAsync; returns an EfPersistenceTransaction adapter
          that implements IPersistenceTransaction.
          DISPATCH DEFERRAL RULE (P-105): SaveChangesAsync checks DbContext.Database.CurrentTransaction.
          When a transaction IS active (non-null): calls only DbContext.SaveChangesAsync — domain event
          dispatch is deferred to EfPersistenceTransaction.CommitAsync (fires after DB commit succeeds).
          When NO transaction is active (null): calls SaveChangesAsync then dispatches immediately,
          matching EfUnitOfWork semantics exactly.
          EfPersistenceTransaction.RollbackAsync does NOT dispatch domain events.
          Application layer injects ITransactionalUnitOfWork — never IDbContextTransaction directly
          (hard violation, enforced by governance Rule P-103).
```

#### Specification evaluator (`Specifications/`)

```text
SpecificationEvaluator<T>  (sealed class, implements ISpecificationEvaluator<T>)
    .GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec) → IQueryable<T>
    .GetProjectedQuery<TResult>(IQueryable<T> inputQuery, IProjectionSpecification<T,TResult> spec) → IQueryable<TResult>
    Applies in strict order:
        0.  IgnoreQueryFilters() — only when spec.IncludeDeleted == true; applied before all other steps
        1.  Criteria (Where clause) — if non-null; null Criteria matches all entities
        2.  Includes (expression-based eager loading — ThenInclude supported via include expressions)
        2b. StringIncludes — applied after expression includes and before ordering; each entry in
            spec.StringIncludes is applied via IQueryable<T>.Include(string); empty list is a no-op
        3.  OrderBy / OrderByDescending — primary sort; last-call wins
        4.  ThenBys — secondary sorts; only applied if a primary sort is already set
        5.  Distinct (Distinct())
        6.  AsNoTracking (AsNoTracking())
        7.  Skip / Take — paging; ALWAYS applied last for the aggregate pipeline
        8.  Select(spec.Selector) — projection overload only; applied after Skip/Take
    NOTE: Paging is always the final operation before projection so ordering is stable before any Skip/Take.
          ThenBys are silently ignored when no primary sort is configured.
          StringIncludes are intended for deep navigation paths (e.g., "Orders.Items.Product") where
          expression-based ThenInclude chains become cumbersome. Existing specs not calling
          AddStringInclude are unaffected — empty StringIncludes is a no-op.
          QueryableExtensions.IgnoreSoftDeleteFilter() has been removed (P-080 breaking change).
          Use spec.IncludeDeleted = true instead — the evaluator calls .IgnoreQueryFilters() automatically.
```

#### Interceptors (`Interceptors/`)

```text
AuditInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SavingChanges/SavingChangesAsync: populates IHasCreatedAudit.CreatedBy / CreatedOn for Added entries;
      populates IHasAudit.ModifiedBy / ModifiedOn for Modified entries.
    — Uses EF ChangeTracker entry CurrentValues[propertyName] — NEVER direct property setters on aggregates.
    — Constructor injection: IUserContext (scoped DI — see IUserContext pattern below), IClock (from 01.Core).

SoftDeleteInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SavingChanges/SavingChangesAsync: converts Deleted state to Modified for ISoftDeletable entities;
      sets IsDeleted = true, DeletedOn = clock.UtcNow, DeletedBy = current user via EF ChangeTracker.
    — Non-ISoftDeletable entities pass through without modification.
    — Constructor injection: IUserContext (scoped DI), IClock.

ConcurrencyInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SaveChangesFailed/SaveChangesFailedAsync: catches DbUpdateConcurrencyException for IHasConcurrency entries;
      rethrows as typed ConcurrencyException carrying Error.Conflict(...) from SharedKernel.Primitives.
    — Does NOT retry — conflict resolution is the application layer's responsibility.
    — Non-concurrency exceptions propagate unchanged.

NOTE: Exactly three interceptors exist in this package. No OutboxInterceptor — MassTransit's
      UseEntityFrameworkOutbox is the outbox infrastructure owner at 07.Messaging.
```

#### IUserContext injection pattern and audit string format

`AuditInterceptor` and `SoftDeleteInterceptor` require `IUserContext` from `SharedKernel.Security.Abstractions` to resolve the current user. `SharedKernel.Persistence.EfCore` holds a deliberate project reference to `SharedKernel.Security.Abstractions` — this is an approved layering exception: `12.Security.Abstractions` is a zero-dependency interface library, and the alternative (maintaining local interface copies) creates divergence risk. The pattern:

1. `IUserContext` is sourced from `SharedKernel.Security.Abstractions`. `UserId` is `Guid`; `IsAuthenticated` is `bool`.
2. `EfCorePersistenceBuilder.Build()` registers a scoped no-op `IUserContext` placeholder (`UserId = Guid.Empty`, `IsAuthenticated = false`) if no `IUserContext` is already registered in the DI container.
3. Consuming services register their own `IUserContext` implementation (from `12.Security.Oidc` or similar) before or after `.Build()` — the last registration wins.
4. Interceptors are registered as **scoped** services so they receive a per-request `IUserContext` from DI.

**Audit string format rule (P-091):**

Audit columns (`CreatedBy`, `ModifiedBy`, `DeletedBy`) are `string` with `HasMaxLength(256)`. The string value is produced as:

- When `userContext.IsAuthenticated == true && userContext.UserId != Guid.Empty`: write `userContext.UserId.ToString("D")` — lowercase hyphenated GUID, 36 characters, e.g. `"a1b2c3d4-e5f6-7890-abcd-ef1234567890"`.
- Otherwise: write `"system"`.

No other format is permitted. The `"D"` format specifier is mandatory — `"N"`, `"B"`, `"P"`, and `"X"` formats are all violations.

#### Type configurations (`Configurations/`)

```text
EntityTypeConfigurationBase<TEntity, TId>  (abstract class, implements IEntityTypeConfiguration<TEntity>)
    — Applies when base.Configure(builder) is called:
        • Primary key on TId
        • Concurrency token (.IsRowVersion()) for IHasConcurrency entities
        • Global query filter e => !e.IsDeleted for ISoftDeletable entities
        • Owned audit columns CreatedBy (max-length string, not null) and CreatedOn (DateTimeOffset, not null)
          for IHasCreatedAudit
        • Additionally ModifiedBy (nullable string) and ModifiedOn (nullable DateTimeOffset) for IHasAudit
        • TenantId column (Guid, not null) + tenant index for IHasTenant entities
    NOTE: Concrete configurations must call base.Configure(builder) first, then add entity-specific mappings.

StronglyTypedIdValueConverter<TStronglyTypedId, TValue>  (sealed class, extends ValueConverter<TStronglyTypedId, TValue>)
    — Converts StronglyTypedId<TValue> to/from its primitive TValue for EF Core column mapping.
    — Uses implicit operator TValue for to-provider direction — no Activator.CreateInstance, no reflection.
    — Companion ModelConfigurationBuilder extension auto-registers the converter for all IStronglyTypedId<TValue>
      types, eliminating per-aggregate manual converter registration.
```

#### Conventions (`Conventions/`)

```text
ValueObjectOwnershipBuilder  (static class)
    .Apply(ModelBuilder modelBuilder) → void
    NOTE: Static utility method — NOT an IModelFinalizingConvention and does NOT auto-apply.
          Must be called manually from OnModelCreating after all entity configurations are applied.
          Scans all non-owned entity types; calls OwnsOne for any property whose CLR type implements
          IValueObject that is not already explicitly configured.
          OwnsMany collections are excluded — register those explicitly in entity configuration classes.
          Startup cost: O(n×m); early-exit when no IValueObject properties present.
          Do NOT register via ConfigureConventions — it will have no effect there.
```

#### Multi-tenancy (`MultiTenancy/`)

```text
TenantedDbContext  (abstract class, extends SharedKernelDbContext)
    — Overrides OnModelCreating to install a global query filter on all IHasTenant entities:
      e => e.TenantId == tenantProvider.TenantId
    — Filter is built using expression trees (Expression.Parameter / Expression.Property /
      Expression.Equal / Expression.Lambda) — no GetMethod, MakeGenericMethod, or Invoke calls.
    — The filter captures the ITenantProvider reference, not a startup-time snapshot —
      tenant ID is resolved at query execution time.
    — Constructor accepts ITenantProvider (from SharedKernel.Security.Abstractions) and DbContextOptions.
    NOTE: Multi-tenant services extend TenantedDbContext; single-tenant services extend SharedKernelDbContext.

TenantedRepository<TAggregate, TId>  (abstract class, extends EfRepository<TAggregate, TId>)
    .GetByIdForTenantAsync(TId id, Guid tenantId, CancellationToken ct) → Task<TAggregate?>
    .GetByIdForTenantIncludingDeletedAsync(TId id, Guid tenantId, CancellationToken ct) → Task<TAggregate?>
    NOTE: GetByIdForTenantAsync — bypasses the tenant filter only; preserves the soft-delete filter
          for ISoftDeletable entities (soft-deleted records are excluded). Use for admin cross-tenant lookups
          where only live records are expected.
          GetByIdForTenantIncludingDeletedAsync — bypasses BOTH the tenant filter AND the soft-delete filter
          via IgnoreQueryFilters(). Use only for audit, recovery, or data-export operations. XML doc must warn
          both filters are bypassed.
          Standard GetByIdAsync flows through the global tenant filter automatically.
```

**Guid.Empty no-tenant sentinel rule (P-092):**

`ITenantProvider.TenantId` is `Guid` (non-nullable). `NoOpTenantProvider` returns `Guid.Empty` explicitly. When no real provider is registered, the global filter becomes `e.TenantId == Guid.Empty`, which returns **zero rows** — no production entity should ever have `TenantId == Guid.Empty`. This is intentional and safe: teams that forget to register a real provider see an empty result set immediately rather than a cross-tenant data leak.

`EntityTypeConfigurationBase.ConfigureTenantColumn` carries an XML doc stating `TenantId == Guid.Empty` is forbidden in production rows.

#### DI extensions (`Extensions/`)

```text
AddSharedKernelEfCore<TContext>(IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    → returns EfCorePersistenceBuilder

EfCorePersistenceBuilder
    .WithMultiTenancy()
        — Registers NoOpTenantProvider (ITenantProvider, returns Guid.Empty) as scoped placeholder.
        — Asserts at .Build() time that TContext extends TenantedDbContext; throws InvalidOperationException
          with actionable message if the assertion fails.
    .WithTransactionalUnitOfWork()
        — Registers ITransactionalUnitOfWork → EfTransactionalUnitOfWork (scoped) alongside IUnitOfWork.
        — Optional. Services that never need explicit transactions can omit this call.
        — Application layer injects ITransactionalUnitOfWork; never IDbContextTransaction directly.
    .WithDbContextFactory()
        — Calls services.AddDbContextFactory<TContext>(configureDb) in addition to AddDbContext.
        — Required for background services, hosted workers, Hangfire jobs, and Temporal activities.
        — Factory-created contexts receive NoOpUserContext (UserId = Guid.Empty) for audit fields,
          producing "system" audit values, unless a singleton IUserContext is registered.
        — Optional. Omit for services that have no background DbContext consumers.
    .AddInterceptor<TInterceptor>()
        — Registers one additional ISaveChangesInterceptor beyond the platform three.
        — Multiple calls accumulate; all fire after the platform interceptors in registration order.
        — TInterceptor is registered as scoped.
        — Platform three (Audit, SoftDelete, Concurrency) always fire first — this is non-negotiable.
    .WithCompiledModel(IModel compiledModel)
        — Wraps the caller-supplied configureDb action to also call optionsBuilder.UseModel(compiledModel).
        — Compiled models are produced via dotnet ef dbcontext optimize.
        — When used, ValueObjectOwnershipBuilder.Apply and runtime model-building scans do not run —
          all mappings must be present in the compiled model.
        — Pure pass-through: builder does not validate the model.
    .Build()
        — Registers TContext as DbContext (scoped)
        — Registers IUnitOfWork → EfUnitOfWork (scoped)
        — Registers ISpecificationEvaluator<T> → SpecificationEvaluator<T> (singleton — stateless)
        — Registers AuditInterceptor, SoftDeleteInterceptor, ConcurrencyInterceptor (scoped)
        — Registers any additional interceptors supplied via .AddInterceptor<T>() (scoped)
        — Registers no-op IUserContext placeholder (UserId = Guid.Empty, IsAuthenticated = false)
          if no IUserContext already registered; uses SharedKernel.Security.Abstractions.IUserContext
        — Calls AddDbContextFactory<TContext> when .WithDbContextFactory() was invoked
        — Applies UseModel(compiledModel) when .WithCompiledModel() was invoked
    NOTE: No outbox, Dapper, or PostgreSQL wiring in this builder. Those are separate concerns.
          ITransactionalUnitOfWork is only registered when .WithTransactionalUnitOfWork() is called.
          IUserContext and ITenantProvider are both sourced from SharedKernel.Security.Abstractions.
          All fluent methods return EfCorePersistenceBuilder for chaining.
```

---

### `SharedKernel.Persistence.PostgreSQL` — public surface

#### Conventions and extensions (`Conventions/`, `Extensions/`)

```text
SnakeCaseNamingConvention  (implements IModelFinalizingConvention)
    — Converts all table names, column names, index names, and constraint names to snake_case.
    — Applied automatically when UsePostgreSQL() DI extension is called.

UsePostgreSQL(DbContextOptionsBuilder optionsBuilder, string connectionString)
    → configures Npgsql provider + SnakeCaseNamingConvention + vector support + JSONB defaults
    NOTE: Single call in DI composition replaces manual provider + naming convention wiring.
```

#### JSONB support (`Jsonb/`)

```text
JsonbColumnAttribute  [AttributeUsage(Property)]
    — Marks an EF Core property for JSONB column storage using Npgsql JSON column type.
    — Consumed by JsonbEntityTypeBuilderExtension to apply .HasColumnType("jsonb").

JsonbEntityTypeBuilderExtension  (static extension on EntityTypeBuilder<T>)
    .HasJsonbColumn<TProperty>(propertyExpression)             → EntityTypeBuilder<T>
    NOTE: STJ serialization is configured globally; individual JSONB columns do not need per-column converters.
```

#### pgvector support (`Vector/`)

```text
VectorColumnAttribute  [AttributeUsage(Property)]
    — Marks a float[] or Vector property for pgvector column storage.

VectorEntityTypeBuilderExtension  (static extension on EntityTypeBuilder<T>)
    .HasVectorColumn<TProperty>(propertyExpression, int dimensions) → EntityTypeBuilder<T>
    NOTE: Requires pgvector extension enabled in PostgreSQL. Call EnsureVectorExtension()
          in the migration or startup to create it if absent.
```

#### PostgreSQL DI registration (`Extensions/`)

```text
AddSharedKernelPostgreSQL(IServiceCollection services, string connectionString)
    → registers Npgsql IDbConnectionFactory (for Dapper in the same service)
    → configures NpgsqlDataSource with STJ options
    NOTE: Downstream services call AddDbContext<TContext>(...).UsePostgreSQL(conn) separately —
          this extension only wires the shared NpgsqlDataSource and IDbConnectionFactory.
```

---

### `SharedKernel.Persistence.Dapper` — public surface

#### Dapper connection factory (`Connections/`)

```text
NpgsqlConnectionFactory  (sealed class, implements IDbConnectionFactory)
    .CreateConnectionAsync(CancellationToken ct)               → Task<IDbConnection>
    NOTE: Returns an open NpgsqlConnection. Caller disposes.
          Backed by injected NpgsqlDataSource — connection pooling is managed by Npgsql.
```

#### Type handlers (`TypeHandlers/`)

```text
StronglyTypedIdTypeHandler<TStronglyTypedId, TValue>  (abstract class, extends SqlMapper.TypeHandler<TStronglyTypedId>)
    .SetValue(IDbDataParameter parameter, TStronglyTypedId value) → void
    .Parse(object value)                                          → TStronglyTypedId
    NOTE: Consuming services implement a concrete handler per strongly-typed ID type (one line).
          Registered via DapperTypeHandlers.Register() at startup.

SmartEnumTypeHandler<TEnum, TValue>  (abstract class, extends SqlMapper.TypeHandler<TEnum>)
    .SetValue(IDbDataParameter parameter, TEnum value)            → void
    .Parse(object value)                                          → TEnum
    NOTE: Uses SmartEnum<TEnum,TValue>.TryFromValue — no reflection.

DapperTypeHandlers  (static class)
    .Register()                                                   → void
    NOTE: Call once at startup to register all type handlers. Idempotent.
```

#### Read-model base (`ReadModels/`)

```text
DapperReadService  (abstract class)
    protected DapperReadService(IDbConnectionFactory connectionFactory)
    protected .QueryAsync<TResult>(string sql, object? parameters, CancellationToken ct) → Task<IEnumerable<TResult>>
    protected .QuerySingleOrDefaultAsync<TResult>(string sql, object? parameters, CancellationToken ct) → Task<TResult?>
    protected .ExecuteAsync(string sql, object? parameters, CancellationToken ct) → Task<int>
    NOTE: All methods open and dispose the connection per call via IDbConnectionFactory.
          SQL is caller-supplied — no query builder abstraction is provided at this layer.
          Parameterized queries only — no string interpolation in SQL.
```

---

## Implementation Rules

### Hard violations (never do these)

- `SharedKernel.Persistence.Abstractions` introducing **any ORM NuGet dependency** — it may only reference `SharedKernel.Primitives` and `SharedKernel.Domain`.
- Any interface or type in `.Abstractions` exposing `IQueryable<T>` to callers — all queries are expressed via `ISpecification<T>`.
- Calling `DbContext.SaveChanges[Async]` directly anywhere outside `EfUnitOfWork` — `IUnitOfWork.SaveChangesAsync` is the only permitted save boundary.
- Adding `OutboxMessage`, `IOutboxWriter`, or any outbox type to this domain — outbox belongs to `07.Messaging`.
- Adding an `OutboxInterceptor` to `SharedKernelDbContext` — MassTransit's `UseEntityFrameworkOutbox` registers its own interceptors at the `07.Messaging` composition layer.
- `AuditInterceptor` or `SoftDeleteInterceptor` calling property setters directly on aggregate instances — all field writes must go through `ChangeTracker.Entry(entity).CurrentValues[propertyName]`.
- `ConcurrencyInterceptor` swallowing non-concurrency exceptions — only `DbUpdateConcurrencyException` for `IHasConcurrency` entities is caught and rethrown.
- `SpecificationEvaluator<T>` applying paging (`Skip`/`Take`) before ordering — paging is always the final operation.
- String interpolation in any SQL inside `DapperReadService` subclasses — parameterized queries only (SQL injection risk).
- Using `Activator.CreateInstance` or reflection in `StronglyTypedIdValueConverter` — use the `implicit operator TValue` (a static method call).
- Using reflection in `SmartEnumTypeHandler` — use `SmartEnum<TEnum,TValue>.TryFromValue`.
- Adding messaging concerns (`IMessageBus`, `IEventPublisher`) to this domain — outbox message writes are the persistence boundary; dispatching belongs in `07.Messaging`.
- Adding domain logic to any type in this domain — this layer is pure data-access plumbing.
- Adding a second public constructor to `EfUnitOfWork` — exactly one constructor with `IDomainEventDispatcher?` as a nullable optional parameter is required; a second constructor creates DI ambiguity where the container silently picks the shorter one and skips the dispatcher.
- Injecting `Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction` directly in application layer handlers — explicit transaction control must go through `ITransactionalUnitOfWork.BeginTransactionAsync` which returns an `IPersistenceTransaction` handle; direct `IDbContextTransaction` injection bypasses the abstraction and couples application code to EF Core.
- Downcasting `ISpecificationEvaluator<T>` to `SpecificationEvaluator<T>` (or any concrete type) — `GetProjectedQuery` is now on the interface; any cast to the concrete type breaks alternative evaluator implementations at runtime.
- Inline `new NpgsqlConnection(connStr)` in Dapper services — `IDbConnectionFactory` is the only permitted connection source.
- Any static mutable state.
- Adding a project reference from `SharedKernel.Persistence.EfCore` to any `12.Security` package other than `SharedKernel.Security.Abstractions` — the single approved exception is `SharedKernel.Security.Abstractions` (zero-dependency interface library). All other `12.Security.*` packages are forbidden.
- Using `nameof(IEntity<TId>.Id)` in EF Core configurations — `IEntity<TId>` is a **marker interface** with no `Id` property; `Id` is declared on `Entity<TId>`. Use the string literal `"Id"` or `nameof(Entity<TId>.Id)` (requires a concrete TEntity constraint).
- Omitting the nested test-project exclusion items from production `.csproj` files — every production csproj that has a nested `*.Tests` subfolder must include: `<Compile Remove="*.Tests\**" />`, `<EmbeddedResource Remove="*.Tests\**" />`, `<None Remove="*.Tests\**" />`. Without these the SDK globs pick up test `.cs` files and the production build fails.
- Writing audit strings in any format other than `userId.ToString("D")` or `"system"` — the `"D"` lowercase hyphenated GUID format is the only permitted non-system value. `"N"`, `"B"`, `"P"`, `"X"` formats are violations.
- Using `GetMethod`, `MakeGenericMethod`, or `Invoke` in `TenantedDbContext.OnModelCreating` — the global tenant filter must be built using expression trees (`Expression.Parameter`, `Expression.Property`, `Expression.Equal`, `Expression.Lambda`) and applied via the non-generic `modelBuilder.Entity(clrType).HasQueryFilter(lambdaExpr)` overload.
- Storing `Guid.Empty` as a `TenantId` in production rows — `Guid.Empty` is the reserved no-tenant sentinel used by `NoOpTenantProvider`; any row with `TenantId == Guid.Empty` will be invisible to all tenanted queries.
- Calling `QueryableExtensions.IgnoreSoftDeleteFilter()` — this class has been deleted (P-080). Use `spec.IncludeDeleted = true` on the specification; `SpecificationEvaluator<T>` calls `.IgnoreQueryFilters()` automatically at step 0.
- Calling `IReadRepository.GetByIdAsync(id, ct)` — this method has been removed (P-080 breaking change). Use `readRepo.GetBySpecAsync(new ByIdSpecification<TAggregate, TId>(id), ct)` instead. `IRepository` (write side) still has `GetByIdAsync`.
- Calling `IDomainEventDispatcher.DispatchAsync` from anywhere other than `EfUnitOfWork.SaveChangesAsync` — domain event dispatch is triggered exclusively post-commit by `EfUnitOfWork`; dispatching from application layer is a responsibility violation (07.Messaging publishes integration events from dispatched domain events).
- Applying `.Select(spec.Selector)` before `Skip/Take` in the `SpecificationEvaluator` projection path — projection must always be the final step (step 8) after paging (step 7) to preserve the paging-last invariant.
- Calling `DispatchAndClearEventsAsync` inside `EfTransactionalUnitOfWork.SaveChangesAsync` when `DbContext.Database.CurrentTransaction` is non-null — dispatch must be deferred to `EfPersistenceTransaction.CommitAsync`; premature dispatch causes duplicate events when the flow is `BeginTransactionAsync → SaveChangesAsync → CommitAsync`.
- Using `EF.Property<TId>(e, "Id")` inside a `Contains` predicate for `GetByIdsAsync` — this approach may silently fall back to client-side evaluation when `TId` is a strongly-typed ID with a registered `ValueConverter`; use an expression-tree `Contains` lambda instead so the converter is applied at the property level by the LINQ provider.
- Registering consumer interceptors that fire before the platform three (Audit, SoftDelete, Concurrency) — platform interceptors are always composed first in `SharedKernelDbContext`; consumer interceptors added via `.AddInterceptor<T>()` are always appended after.
- Setting `AsNoTracking = true` on a specification passed to `IRepository.GetBySpecAsync` (write-side) — the entity returned will be detached and subsequent mutations will not be detected by change tracking, forcing a full-column UPDATE via `.Update()`; write-side specs must leave `AsNoTracking` unset.

### Specification evaluator ordering (canonical)

```text
0.  IgnoreQueryFilters()  ← only when spec.IncludeDeleted == true; before all other steps
1.  Criteria  (Where clause — null = no filter = all entities)
2.  Includes  (expression-based Include / ThenInclude)
2b. StringIncludes  (string-based Include paths — applied after expression includes, before ordering)
3.  OrderBy / OrderByDescending  (primary sort)
4.  ThenBys  (secondary sorts — only when primary sort is set)
5.  Distinct
6.  AsNoTracking
7.  Skip / Take  ← ALWAYS LAST for the aggregate pipeline
8.  Select(spec.Selector)  ← projection overload only; applied after Skip/Take
```

Step 2b note: `spec.StringIncludes` contains paths like `"Orders.Items.Product"`. Each is applied via `IQueryable<T>.Include(string)`. Empty list is a no-op — existing specs are unaffected. Null/whitespace paths are rejected by `AddStringInclude` with `ArgumentException`.

### Interceptor-only save mutation rule

`AuditInterceptor` and `SoftDeleteInterceptor` must set field values exclusively via:

```csharp
context.Entry(entity).CurrentValues[nameof(IHasCreatedAudit.CreatedBy)] = userContext.UserId;
```

Never:

```csharp
((IHasCreatedAudit)entity).CreatedBy = userContext.UserId;  // ← VIOLATION
```

---

## DI Registration (expected shape)

```csharp
// Minimal EF Core wiring (single-tenant service)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .Build();

// Multi-tenant EF Core wiring
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithMultiTenancy()   // TContext must extend TenantedDbContext or Build() throws
    .Build();

// With explicit transaction support (optional — only for services that need ITransactionalUnitOfWork)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithTransactionalUnitOfWork()   // registers ITransactionalUnitOfWork → EfTransactionalUnitOfWork
    .Build();

// Consuming service overrides the IUserContext placeholder with its real implementation
// IUserContext is from SharedKernel.Security.Abstractions; UserId is Guid
services.AddScoped<IUserContext, OidcUserContext>();

// Multi-tenant service overrides the NoOpTenantProvider with its real implementation
// ITenantProvider is from SharedKernel.Security.Abstractions; TenantId is Guid (Guid.Empty = no tenant)
services.AddScoped<ITenantProvider, ClaimsTenantProvider>();

// Per-aggregate repository pair — one registration per aggregate in the consuming service
services.AddScoped<IRepository<Order, OrderId>, OrderEfRepository>();
services.AddScoped<IReadRepository<Order, OrderId>, OrderEfReadRepository>();

// With IDbContextFactory for background services (optional)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithDbContextFactory()   // registers IDbContextFactory<OrderDbContext>
    .Build();

// With custom service-specific interceptor (optional — fires after platform three)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .AddInterceptor<OrderAuditBridgeInterceptor>()
    .Build();

// With EF Core compiled model (optional — for AOT / cold-start performance)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithCompiledModel(OrderDbContextModel.Instance)  // produced by dotnet ef dbcontext optimize
    .Build();

// PostgreSQL — shared NpgsqlDataSource and IDbConnectionFactory for Dapper
services.AddSharedKernelPostgreSQL(connectionString);

// Dapper — type handlers registered at startup; IDbConnectionFactory already registered above
services.AddSharedKernelDapper();
```

`SharedKernel.Persistence.Abstractions` ships **no DI extensions** — it is a pure interface library.

---

## AOT Compatibility

- `IRepository<TAggregate, TId>`, `IReadRepository<TAggregate, TId>`, `IUnitOfWork`, `IDbConnectionFactory`, `ISpecificationEvaluator<T>` are interfaces — AOT-safe by definition.
- `SpecificationEvaluator<T>` applies `Expression<Func<T, bool>>` expression trees to `IQueryable<T>` — expression trees on `IQueryable` are AOT-safe when lambda bodies do not reference runtime-only reflection APIs.
- `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` uses the `implicit operator` (a static method) — no reflection, AOT-safe.
- `SmartEnumTypeHandler<TEnum,TValue>` uses `SmartEnum<TEnum,TValue>.TryFromValue` — no reflection in the hot path, AOT-safe.
- `AuditInterceptor` and `SoftDeleteInterceptor` access EF Core shadow properties by string key — shadow property access via `CurrentValues[name]` is AOT-safe (no reflection on CLR types).
- `TenantedDbContext.OnModelCreating` global filter is built with expression trees (`Expression.Parameter`, `Expression.Property`, `Expression.Equal`, `Expression.Lambda`) — no `GetMethod`/`MakeGenericMethod`/`Invoke` calls; fully AOT-safe.
- `ValueObjectOwnershipBuilder` scans entity types at model-build time — O(n×m) startup cost (n entity types, m properties per type); early-exit when no `IValueObject` properties found prevents unnecessary allocation; model-build time only, not a hot path. The `GetProperties(BindingFlags.Public | BindingFlags.Instance)` call at the `entityType.ClrType` usage site carries `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]` to suppress IL2026/IL2075 trim warnings; this annotation is acceptable because the call is model-build time only.
- `SnakeCaseNamingConvention` operates on EF Core model metadata at model-building time — not in hot paths, AOT-safe.
- `JsonbEntityTypeBuilderExtension` and `VectorEntityTypeBuilderExtension` configure the EF model at startup — AOT-safe.
- `Microsoft.EntityFrameworkCore` — fully AOT-compatible as of .NET 8+ with compiled models; verify on each major upgrade.
- `Npgsql.EntityFrameworkCore.PostgreSQL` — verify AOT status on each major upgrade; abstraction boundary allows a provider swap.
- `Dapper` — uses reflection for parameter binding and result mapping. Known AOT limitation. All Dapper code is behind `DapperReadService` so the AOT boundary is contained to that class.
- `EfCorePersistenceBuilder` uses generic type constraints to validate `TContext` at compile time where possible; startup-time `InvalidOperationException` for the multi-tenancy mismatch guard is acceptable (DI composition is not AOT-critical path).
- `IPersistenceTransaction` and `ITransactionalUnitOfWork` are pure interfaces — AOT-safe by definition. Zero ORM dependencies in Abstractions.
- `EfTransactionalUnitOfWork` and `EfPersistenceTransaction` delegate to EF Core's `IDbContextTransaction` which is AOT-compatible as of EF Core 8+; verify on each major upgrade.
- `ByIdSpecification<TAggregate, TId>` uses `e => e.Id.Equals(id)` as an expression tree — AOT-safe on `IQueryable`.
- `SpecificationEvaluator<T>.GetProjectedQuery<TResult>` applies `.Select(spec.Selector)` where `Selector` is `Expression<Func<TAggregate, TResult>>` — expression trees on `IQueryable` are AOT-safe when the lambda body contains no runtime reflection APIs.
- `EfUnitOfWork` domain event dispatch: `ChangeTracker.Entries<IHasDomainEvents>()` is a generic EF Core API — AOT-safe. `IDomainEventDispatcher` is a pure interface; no reflection in the dispatch path.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Persistence.Abstractions.Tests/` — interface contract shape; assertion that no outbox types exist in this package.
- `SharedKernel.Persistence.EfCore.Tests/` — interceptor integration tests with SQLite provider; `SpecificationEvaluator<T>` expression evaluation tests; `EfRepository` / `EfReadRepository` round-trips with real SQLite; `TenantedDbContext` isolation; `EfCorePersistenceBuilder` smoke tests.
- `SharedKernel.Persistence.PostgreSQL.Tests/` — integration tests with a real PostgreSQL Testcontainer; JSONB round-trip; vector column read/write; snake_case naming verification via `DbContext.Model`.
- `SharedKernel.Persistence.Dapper.Tests/` — type handler round-trip tests; `DapperReadService` query tests with a real PostgreSQL Testcontainer.
- `AuditInterceptor` tests: verify `CreatedBy`/`CreatedOn` set on Added entities; `ModifiedBy`/`ModifiedOn` set on Modified; no audit mutation on Deleted entities (SoftDeleteInterceptor handles those); `IsAuthenticated == false` → `CreatedBy` receives `"system"`; `IsAuthenticated == true` with valid `UserId` → `CreatedBy` receives the lowercase hyphenated GUID string (`ToString("D")` format).
- `SoftDeleteInterceptor` tests: verify Deleted state converted to Modified; `IsDeleted = true`; `DeletedOn` and `DeletedBy` set (same adapter rules as audit: `"system"` or GUID `"D"` format); soft-deleted records excluded by global query filter; non-soft-deletable entity passes through.
- `ConcurrencyInterceptor` tests: `DbUpdateConcurrencyException` is caught and rethrown as a typed `ConcurrencyException` carrying `Error.Conflict(...)`; non-concurrency exceptions are not swallowed.
- `SpecificationEvaluator<T>` tests: criteria, ordering (asc/desc), ThenBys, paging, distinct, AsNoTracking each verified independently; paging applied after ordering (determinism test); null Criteria matches all entities; ThenBys ignored without primary sort.
- `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` tests: round-trip (entity to DB value, DB value to entity) with a concrete strongly-typed ID.
- `SmartEnumTypeHandler<TEnum,TValue>` tests: SetValue writes the underlying `TValue`; Parse reads back the correct enum member; unknown value handled without reflection.
- `DapperReadService` tests: parameterized query returns correct result; `IDbConnectionFactory` called once per operation; connection disposed after each call.
- `EfRepository.ExistsAsync` tests: returns `true` for existing ID, `false` for missing ID; does not materialize the entity.
- `EfReadRepository.GetByIdsAsync` tests: partial match (some IDs missing — only matching returned); empty input → empty list; all IDs present → full list; result order is not asserted.
- `EfRepository.UpdateAsync` tests: tracked entity with one changed property — only that column is Modified; tracked entity with no mutations — no UPDATE statement issued; detached entity — full-column UPDATE issued (unchanged behavior).
- `TenantedDbContext` isolation tests: no-op `ITenantProvider` (returns `Guid.Empty`) → all queries return zero rows; real provider returning `tenantId = X` → only rows with `TenantId == X` returned; filter is applied at query execution time not startup.
- `ContractShapeTests` in Abstractions: verify `IRepository` has `ExistsAsync`; `IReadRepository` has `GetByIdsAsync` and `ListPagedProjectedAsync`; `IProjectionSpecification` exists in `Specifications/`, extends `ISpecification`, has `Selector` of correct expression type; `ISpecificationEvaluator<T>` declares `GetProjectedQuery`; `ITransactionalUnitOfWork` extends `IUnitOfWork` and declares `BeginTransactionAsync`; `IPersistenceTransaction` declares `CommitAsync` and `RollbackAsync`; no ORM assembly in package.
- **SQLite for EfCore tests** — no Testcontainers needed; SQLite covers all EF Core LINQ and interceptor behavior.
- **Testcontainers PostgreSQL** for PostgreSQL and Dapper tests — no mocked database connections. Import helpers from `16.Testing/SharedKernel.Testing`.
- **Standard test package set** (all test `.csproj` files): `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0. EfCore tests also add `Microsoft.EntityFrameworkCore.Sqlite` 10.0.5 and `NSubstitute` 5.3.0.
- **GlobalUsings.cs required** — every test project must include a `GlobalUsings.cs` containing `global using Xunit;`. `ImplicitUsings` does not auto-import xUnit attributes.
- `EfRepository` bulk write tests (P-080): `AddRangeAsync` — N rows written after `SaveChangesAsync`, zero rows before; `UpdateRangeAsync` — tracked entities: only changed columns Modified; detached entities: all columns Modified; `DeleteRangeAsync` — soft-deletable entities: all `IsDeleted = true`; non-soft-deletable: rows removed; SQLite provider.
- Projection read tests (P-080): `ListProjectedAsync<TResult>` — N aggregates project to N DTOs; paging applied before Select (verify via expression tree order); `GetBySpecProjectedAsync<TResult>` — match returns projected result, no-match returns null; SQLite.
- `ListPagedAsync` tests (P-080): count query strips Skip/Take; data query applies full spec; `PagedList` `TotalCount` is correct; empty result: `TotalCount == 0`; page beyond data: empty `Items`, correct `TotalCount`; `ContractShapeTests` verify method declared on `IReadRepository`.
- `EfUnitOfWork` domain event dispatch tests (P-080): dispatcher registered → events dispatched and cleared post-commit; no dispatcher → events cleared, no dispatch; double-dispatch prevention (events cleared before second `SaveChangesAsync`); dispatch failure does not roll back committed data (documented known trade-off).
- `IncludeDeleted` evaluator tests (P-080): `spec.IncludeDeleted == false` (default) → soft-deleted rows excluded; `spec.IncludeDeleted == true` → soft-deleted rows returned; `QueryableExtensions` class absent from assembly (reflection assertion).
- `ByIdSpecification` and `IReadRepository.GetByIdAsync` removal tests (P-080): `ByIdSpecification` `Criteria` matches only the given ID; `GetBySpecAsync(new ByIdSpecification<>(...))` returns correct aggregate; `ContractShapeTests` assert `IReadRepository` has no `GetByIdAsync` method; `IRepository` still has `GetByIdAsync`.
- `ListPagedProjectedAsync` tests (P-101): 10 aggregates → page 2 size 3 returns 3 DTOs, `TotalCount == 10`; empty set: `TotalCount == 0`, `Items == []`; page beyond data: empty items, correct `TotalCount`; SQLite provider.
- `ITransactionalUnitOfWork` tests (P-099): begin → add entity → commit → entity persisted; begin → add entity → rollback → entity NOT persisted; SQLite provider.
- `TenantedRepository.GetByIdForTenantIncludingDeletedAsync` tests (P-100): soft-delete entity; `GetByIdForTenantAsync` returns null; `GetByIdForTenantIncludingDeletedAsync` returns entity; SQLite provider.
- `AsNoTracking` behavioral tests (P-104): `ListAsync` with `AsNoTracking = true` → entities have `EntityState.Detached`; `ListAsync` without → entities tracked; SQLite provider.
- `EfTransactionalUnitOfWork` double-dispatch fix tests (P-105): (1) begin → `SaveChangesAsync` → `CommitAsync` → domain events dispatched exactly once (after commit); (2) `SaveChangesAsync` via `EfTransactionalUnitOfWork` without open transaction → dispatched once immediately; (3) begin → `SaveChangesAsync` → `RollbackAsync` → events NOT dispatched; domain events cleared in all paths; SQLite provider.
- `EfReadRepository.GetByIdsAsync` strongly-typed ID test (P-105): entity with `StronglyTypedId<Guid>` PK and registered `StronglyTypedIdValueConverter`; `GetByIdsAsync` returns correct entities; SQL log shows server-side `IN (...)` clause (no client-side evaluation log entry); all existing `GetByIdsAsync` behavioral tests continue to pass.
- `IRepository.GetBySpecAsync` write-side tests (P-106): spec matching by non-PK field returns tracked entity (mutation detected without explicit `.Update()`); spec matching nothing returns null; spec with `AsNoTracking = true` returns detached entity; `ContractShapeTests` verify `IRepository<T,TId>` declares `GetBySpecAsync`.
- `EfCorePersistenceBuilder.WithDbContextFactory()` smoke test (P-106): `IDbContextFactory<TContext>` resolvable when `.WithDbContextFactory()` called; not resolvable when omitted; factory-created context is operable (non-null, can execute a query); SQLite provider.
- Custom interceptor firing order test (P-106): platform three fire before consumer-supplied interceptors; two consumer interceptors fire in registration order after the platform three; SQLite provider.
- Compiled model passthrough smoke test (P-106): `.WithCompiledModel(model)` → resolved `DbContext` options contain the compiled model (reference equality or extensions check); no behavioral assertion beyond successful context creation.
- `SpecificationEvaluator<T>` string include tests (P-107): string include path applied → navigation property populated; expression include + string include both applied → both navigation properties populated; string includes applied after expression includes and before primary sort (ordering verification via query log or expression tree); `ContractShapeTests` verify `ISpecification<T>` declares `StringIncludes` property.
- PostgreSQL package integration tests (P-108) — all require real PostgreSQL Testcontainer: (1) `SnakeCaseNamingConvention` produces snake_case names in `DbContext.Model`; (2) JSONB round-trip: insert + fetch + verify deserialization; (3) pgvector round-trip: insert + fetch + verify dimensions; (4) `NpgsqlConnectionFactory.CreateConnectionAsync` returns open `NpgsqlConnection`; (5) `AddSharedKernelPostgreSQL` smoke: `IDbConnectionFactory` resolves as `NpgsqlConnectionFactory`.
- Dapper package integration tests (P-109) — all require real PostgreSQL Testcontainer: (1) `QueryAsync` returns correct rows from parameterized query; (2) `QuerySingleOrDefaultAsync` returns entity / null; (3) `ExecuteAsync` returns affected row count; (4) connection opened once and disposed per operation (no leaks); (5) `StronglyTypedIdTypeHandler` round-trip; (6) `SmartEnumTypeHandler` round-trip.

---

## Changelog

> Maintained by the persistence domain agent. One line per significant change.

- [2026-06-01] Domain brain initialized — packages, interfaces, rules, AOT notes, test rules
- [2026-06-01] Major refresh: outbox types removed from Abstractions and EfCore scope (MassTransit EF outbox owns outbox at 07.Messaging); SharedKernelDbContext now registers exactly three interceptors (no OutboxInterceptor); ICurrentTenantService added to EfCore package; EfCorePersistenceBuilder fluent DI builder documented; IUserContext placeholder injection pattern documented; DI registration shape updated; test rules split by SQLite (EfCore) vs Testcontainers (PostgreSQL/Dapper); implementation rules reorganized with hard-violation list; WO-008 (P-033) and WO-013 (P-065 through P-074) phases reflected
- [2026-06-01] SK.06.Scaffold complete — EfCore package versions pinned (DI.Abstractions 10.0.5); nested test exclusion pattern and IEntity marker-only rule added to Implementation Rules; standard test package set and GlobalUsings.cs requirement added to Test Rules (sync-brain)
- [2026-06-02] WO-016 (P-091, P-092, P-093, P-094) and WO-014 (P-078, P-079, P-082) planned — audit string adapter rule added (userId.ToString("D") or "system"); Guid.Empty no-tenant sentinel documented; IRepository/IReadRepository extended with ExistsAsync/GetByIdsAsync; IProjectionSpecification added to Abstractions; IDbConnectionFactory doc restriction removed; EfRepository.UpdateAsync tracking optimization documented; TenantedDbContext reflection-elimination and ValueObjectOwnershipBuilder early-exit documented; deliberate layering exception for Security.Abstractions reference recorded; ICurrentTenantService removed in favour of ITenantProvider from Security.Abstractions (persistence-arch-planner)
- [2026-06-02] WO-014 (P-080) planned — bulk write methods (AddRangeAsync/UpdateRangeAsync/DeleteRangeAsync) added to IRepository and EfRepository; projection reads (ListProjectedAsync/GetBySpecProjectedAsync) added to IReadRepository and EfReadRepository; ListPagedAsync added returning `PagedList<T>` (Abstractions gains SharedKernel.Contracts reference); IDomainEventDispatcher optional hook added to EfUnitOfWork (post-commit dispatch + event clear); QueryableExtensions.IgnoreSoftDeleteFilter deleted — replaced by spec.IncludeDeleted flag handled by SpecificationEvaluator (step 0 IgnoreQueryFilters); IReadRepository.GetByIdAsync removed (breaking change) — `ByIdSpecification<TAggregate,TId>` added as canonical replacement; SpecificationEvaluator pipeline updated with step 0 and step 8; all hard violations, AOT notes, test rules, and interface contracts updated (persistence-arch-planner)
- [2026-06-02] SK.06.Scaffold S-06 and S-07 complete — EfCore→Security.Abstractions and Abstractions→Contracts project references added to csproj files; both already documented; no CLAUDE.md content changes needed (sync-brain)
- [2026-06-03] WO-017: GetProjectedQuery promoted to `ISpecificationEvaluator<T>`; ITransactionalUnitOfWork/IPersistenceTransaction added to Abstractions; EfTransactionalUnitOfWork/EfCorePersistenceBuilder.WithTransactionalUnitOfWork added to EfCore; ListPagedProjectedAsync added to IReadRepository/EfReadRepository; ValueObjectOwnershipConvention renamed to ValueObjectOwnershipBuilder with static-utility note; TenantedRepository.GetByIdForTenantAsync soft-delete-preserving semantics documented + GetByIdForTenantIncludingDeletedAsync added; EfUnitOfWork single-constructor hard rule added; three new hard violations (downcast, second constructor, direct IDbContextTransaction injection) (sync-brain)
- [2026-06-03] WO-018 (P-105..P-109) planned: EfTransactionalUnitOfWork double-dispatch fix documented (dispatch deferred to EfPersistenceTransaction.CommitAsync when CurrentTransaction active); EfReadRepository.GetByIdsAsync expression-tree Contains fix documented (eliminates silent client-side evaluation with strongly-typed ID converters); ValueObjectOwnershipBuilder DynamicallyAccessedMembers annotation documented in AOT notes; IRepository.GetBySpecAsync added (write-side tracked fetch by spec); EfCorePersistenceBuilder gains `WithDbContextFactory()`, `AddInterceptor<T>()`, `WithCompiledModel(IModel)`; SharedKernelDbContext constructor extended for additional interceptors; SpecificationEvaluator canonical pipeline updated with step 2b (StringIncludes — between expression includes and OrderBy); PostgreSQL package fully documented (SnakeCaseNamingConvention, JSONB, pgvector, NpgsqlConnectionFactory, AddSharedKernelPostgreSQL); Dapper package fully documented (StronglyTypedIdTypeHandler, SmartEnumTypeHandler, DapperTypeHandlers, DapperReadService, AddSharedKernelDapper); five new hard violations; DI registration examples expanded; test rules updated with nine new test scenarios (persistence-arch-planner)
