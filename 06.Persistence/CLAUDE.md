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
| `SharedKernel.Persistence.EfCore` | EF Core implementation: `EfRepository<T,TId>`, `EfReadRepository<T,TId>`, `EfUnitOfWork` (implements both `SharedKernel.Persistence.Abstractions.IUnitOfWork` and, opt-in, `SharedKernel.Application.Behaviors.IUnitOfWork`), `SharedKernelDbContext`, `SpecificationEvaluator<T>`, interceptors (Audit, SoftDelete, Concurrency — no OutboxInterceptor), `TenantedDbContext`, `EfCorePersistenceBuilder` | `SharedKernel.Persistence.Abstractions`, `SharedKernel.Domain`, `SharedKernel.Cryptography` (01.Core, added P-227 — `EncryptedValueConverter` delegates AES-256-GCM to `ISymmetricEncryptionService`), `SharedKernel.Application.Behaviors` (05.Application, added P-228 — `EfUnitOfWork` dual-interface bridge), `Microsoft.EntityFrameworkCore` 10.0.5, `Microsoft.EntityFrameworkCore.Relational` 10.0.5, `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.5 (must match EFCore transitive — NU1605 fires if pinned lower) |
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
    .StreamAsync(ISpecification<TAggregate> spec, CancellationToken ct)
        → IAsyncEnumerable<TAggregate>
    .StreamProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct)
        → IAsyncEnumerable<TResult>
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
          StreamAsync / StreamProjectedAsync<TResult> (P-149): BCL IAsyncEnumerable<T> — no new
          .Abstractions dependency. Intended for large result sets (exports, batch processing) where
          materializing an IReadOnlyList<T> would be memory-prohibitive. Spec's Skip/Take are honored
          as a row-window applied before streaming begins (not special-cased). AsNoTracking is forced
          unconditionally by the EfCore implementation regardless of the spec's AsNoTracking flag —
          see EfReadRepository for rationale.
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

#### Diagnostics (`Diagnostics/`) — P-150

```text
DatabaseReadinessResult  (sealed record)
    (bool IsHealthy, TimeSpan Latency, string Provider, string? ErrorMessage)
    NOTE: BCL-only — no ORM types. IsHealthy is true only if the probe completed without error.
          ErrorMessage is null when IsHealthy is true.

IDbConnectionFactory.CheckReadinessAsync(CancellationToken ct)  (extension method) → Task<DatabaseReadinessResult>
    NOTE: Opens a connection via CreateConnectionAsync, runs "SELECT 1" via IDbCommand.ExecuteScalar,
          times the round-trip with System.Diagnostics.Stopwatch. Provider is taken from the
          IDbConnection.GetType().Namespace-derived label (e.g., "Npgsql"). Never throws — any
          exception is caught and reported as IsHealthy = false with ErrorMessage populated.
          Only System.Data and System.Diagnostics types are used — zero new dependencies.
          This domain does NOT implement IHealthCheck — see Hard Violations. 13.ServiceDefaults
          wraps this extension (or SharedKernelDbContext.CheckReadinessAsync) inside an
          IHealthCheck adapter for ASP.NET Core health check middleware.
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

SharedKernelDbContext.CheckReadinessAsync(CancellationToken ct)  (extension method) → Task<DatabaseReadinessResult>
    NOTE: P-150. EfCore-side readiness probe — uses Database.CanConnectAsync wrapped in a
          System.Diagnostics.Stopwatch, with Provider taken from Database.ProviderName (e.g.
          "Npgsql.EntityFrameworkCore.PostgreSQL"). Never throws — exceptions are caught and
          reported as IsHealthy = false with ErrorMessage populated. Returns the same
          DatabaseReadinessResult record defined in SharedKernel.Persistence.Abstractions/Diagnostics/.
          Prefer this overload when a DbContext is already in scope; prefer
          IDbConnectionFactory.CheckReadinessAsync for Dapper-only read services that have no DbContext.
          This domain does NOT implement IHealthCheck — see Hard Violations.
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

IBulkMutationRepository<TAggregate, TId>  (interface, SharedKernel.Persistence.EfCore — NOT Abstractions)
    .ExecuteUpdateAsync(ISpecification<TAggregate> spec,
        Action<UpdateSettersBuilder<TAggregate>> setPropertyCalls,
        CancellationToken ct)                                                          → Task<int>
    .ExecuteDeleteAsync(ISpecification<TAggregate> spec, CancellationToken ct)         → Task<int>
    NOTE: CORRECTED (P-148, supersedes original WO-024 spec text): the original spec referenced
          `Expression<Func<SetPropertyCalls<TAggregate>, SetPropertyCalls<TAggregate>>>` — this is the
          EF Core 5–8 era API. In EF Core 10.0.5, `SetPropertyCalls<T>` no longer exists; both
          `IQueryable<T>.ExecuteUpdate` / `ExecuteUpdateAsync` take
          `Action<UpdateSettersBuilder<TEntity>>` (a delegate, not an expression tree). The interface
          signature above reflects the actual EF Core 10 API. Lives in SharedKernel.Persistence.EfCore
          because UpdateSettersBuilder<TAggregate> is an EF Core type (Microsoft.EntityFrameworkCore.Query)
          — placing this interface in .Abstractions would introduce an ORM dependency there.
          Implemented directly by EfRepository<TAggregate, TId> (no separate base class).
          Both methods translate to a single server-side ExecuteUpdate / ExecuteDelete SQL statement —
          they bypass the ChangeTracker entirely, which means:
            - IUnitOfWork.SaveChangesAsync is NOT invoked and has no effect on these rows.
            - The three platform interceptors (Audit, SoftDelete, Concurrency) do NOT run.
            - Domain events are NOT collected or dispatched for affected aggregates.
          ExecuteDeleteAsync ALWAYS issues a hard physical DELETE, even when TAggregate implements
          ISoftDeletable — there is no server-side translation for "set IsDeleted = true" semantics
          via ExecuteDelete. Callers needing soft-delete semantics in bulk must use ExecuteUpdateAsync
          with an explicit setPropertyCalls delegate that sets the IsDeleted / DeletedOn columns, e.g.
          `setters => setters.SetProperty(x => ((ISoftDeletable)x).IsDeleted, true)`.

BulkSpecificationGuard  (internal static class)
    .Validate<T>(ISpecification<T> spec)                                              → void
    NOTE: Called at the start of both ExecuteUpdateAsync and ExecuteDeleteAsync. Throws
          UnsupportedSpecificationException if the spec has any of: non-default Includes,
          StringIncludes, OrderBy/OrderByDescending, ThenBys, Skip, or Take — these shapes have
          no meaning for a single server-side ExecuteUpdate/ExecuteDelete statement.
          IncludeDeleted, IsDistinct, and AsNoTracking are tolerated (the latter two are no-ops
          in this path). Only Criteria and IncludeDeleted are applied by the evaluator.

UnsupportedSpecificationException  (sealed class, extends SharedKernelException)
    .ctor(string reason)
    NOTE: Message format: "The specification cannot be used with bulk mutation operations: {reason}".

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
    .StreamAsync(ISpecification<TAggregate> spec, CancellationToken ct)
        → IAsyncEnumerable<TAggregate>
    .StreamProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct)
        → IAsyncEnumerable<TResult>
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
          StreamAsync / StreamProjectedAsync<TResult> (P-149): built on GetQuery / GetProjectedQuery via
          ISpecificationEvaluator<T>, then .AsNoTracking().AsAsyncEnumerable() with [EnumeratorCancellation]
          on the CancellationToken parameter. AsNoTracking() is applied UNCONDITIONALLY here — the ONE
          documented exception to "spec's AsNoTracking flag is honored" — because a long-lived streaming
          enumeration under change tracking would grow the ChangeTracker unbounded for the lifetime of
          the enumeration. If spec.Skip / spec.Take are set, they are applied as a normal row-window by
          the evaluator before the query is converted to IAsyncEnumerable<T> — no special-casing.
```

#### EF Core unit of work (`UnitOfWork/`)

```text
EfUnitOfWork  (sealed class, implements SharedKernel.Persistence.Abstractions.IUnitOfWork,
                                          SharedKernel.Application.Behaviors.IUnitOfWork)
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
          IDomainEventDispatcher is from SharedKernel.Domain (03.Domain) — no 05.Application reference for
          this dependency (the event dispatcher is unrelated to the dual-IUnitOfWork bridge below).
          Dispatcher is optional: consuming services opt in by registering IDomainEventDispatcher in DI.
          Dispatch failure does not roll back the already-committed transaction (document as known trade-off).
          DUAL-INTERFACE BRIDGE (P-228): EfUnitOfWork additionally implements
          SharedKernel.Application.Behaviors.IUnitOfWork — the minimal local seam TransactionBehavior
          depends on, declared in 05.Application because 05.Application cannot reference 06.Persistence.
          Both interfaces declare a structurally compatible SaveChangesAsync(CancellationToken) →
          Task<int> member, so the single existing method body satisfies both contracts — no branching,
          no second method. Requires a direct ProjectReference from SharedKernel.Persistence.EfCore to
          SharedKernel.Application.Behaviors (legal: 06 may reference 01–05). Registration against the
          second interface is OPT-IN via EfCorePersistenceBuilder.WithApplicationTransactionBehavior()
          (see below) — omitting it leaves EfUnitOfWork registered only against
          SharedKernel.Persistence.Abstractions.IUnitOfWork, exactly as before P-228.

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

**Audit string format rule (P-091, updated WO-019):**

Audit columns (`CreatedBy`, `ModifiedBy`, `DeletedBy`) are `string` with `HasMaxLength(256)`. The string value is produced as:

- When `userContext.IsAuthenticated == true && userContext.UserId != Guid.Empty`: write `userContext.UserId.ToString("D")` — lowercase hyphenated GUID, 36 characters, e.g. `"a1b2c3d4-e5f6-7890-abcd-ef1234567890"`.
- Otherwise: write `PersistenceServiceOptions.ServiceName` — defaults to `"system"` but is configurable per-service via `EfCorePersistenceBuilder.WithServiceName(string)`.

No other format is permitted. The `"D"` format specifier is mandatory for authenticated users — `"N"`, `"B"`, `"P"`, and `"X"` formats are all violations. The unauthenticated fallback must always come from `PersistenceServiceOptions.ServiceName`; the hardcoded literal `"system"` is no longer permitted in `AuditInterceptor.ResolveUserId()` — it must be the options default value only.

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

#### Encryption (`Encryption/`) — WO-019

Field-level transparent encryption for `string` EF Core properties. Zero domain-layer leakage — entities carry no encryption attributes. All configuration lives in `IEntityTypeConfiguration<T>` implementations.

```text
EncryptionOptions  (options POCO, section "SharedKernel:Encryption")
    .Enabled          (bool, default false)   — master on/off switch; false = plaintext pass-through
    .CurrentVersion   (string)                — version tag for new encryptions, e.g. "v1"; must exist in Keys
    .Keys             (Dictionary<string,string>) — version → Base64-encoded 32-byte AES key
    NOTE: Startup validation fires when Enabled == true:
          (a) CurrentVersion non-null/non-empty; (b) CurrentVersion key exists in Keys;
          (c) every key value decodes to exactly 32 bytes.
          Register via EfCorePersistenceBuilder.WithEncryption(action).

PersistenceServiceOptions  (options POCO, section "SharedKernel:Persistence")
    .ServiceName      (string, default "system") — unauthenticated audit fallback written to CreatedBy/ModifiedBy/DeletedBy
    NOTE: Replaces the hardcoded "system" literal in AuditInterceptor.ResolveUserId().
          Register via EfCorePersistenceBuilder.WithServiceName(string).
          Startup validation: ServiceName must be non-null, non-empty, ≤ 256 characters.

EncryptedValueConverter  (sealed class, extends ValueConverter<string, string>)
    constructor: EncryptedValueConverter(IOptionsMonitor<EncryptionOptions> optionsMonitor,
                                          ISymmetricEncryptionService symmetricEncryptionService,
                                          IEncryptionVersionOverride? versionOverride = null)
    NOTE: Non-generic — operates on string columns only.
          CRYPTOGRAPHY DELEGATION (P-227, supersedes the original WO-019 hand-rolled AesGcm design):
          all AES-256-GCM cryptographic operations (nonce generation, encrypt, tag-append, decrypt,
          tag verification) are delegated to SharedKernel.Cryptography.ISymmetricEncryptionService —
          zero direct AesGcm/RandomNumberGenerator calls remain in this converter or anywhere in
          SharedKernel.Persistence.EfCore. SharedKernel.Persistence.EfCore takes a direct
          ProjectReference to 01.Core/SharedKernel.Cryptography (legal: 06 may reference 01–05, no
          layering exception needed). This converter retains exclusive ownership of the EF Core
          column WIRE FORMAT — ISymmetricEncryptionService knows nothing about column storage strings.
          Ciphertext format (UNCHANGED, byte-for-byte, since WO-019): "v{version}:{Base64(nonce ||
          ciphertext || 16-byte auth-tag)}". The version prefix is mandatory — it identifies the
          decryption key.
          Encrypt path: calls symmetricEncryptionService.Encrypt(plaintextBytes) → receives an
          EncryptedPayload { KeyId, Nonce, Ciphertext, Tag } → this converter packs
          "v{KeyId}:{Base64(Nonce||Ciphertext||Tag)}" itself. KeyId is the EncryptionOptions version
          string (e.g. "v1") — no translation layer, because EncryptionOptionsKeyProvider (below)
          uses EncryptionOptions.Keys' dictionary keys directly as CryptographicKey.Id/KeyId.
          Decrypt path: parses the version prefix and the packed nonce||ciphertext||tag blob from the
          stored string; FIRST calls EncryptionOptionsKeyProvider.GetKey(parsedVersion) directly (a
          cheap synchronous dictionary lookup) — if null (version not in EncryptionOptions.Keys),
          throws EncryptionKeyNotFoundException(parsedVersion) immediately without calling Decrypt at
          all. Otherwise reconstructs an EncryptedPayload { KeyId = parsedVersion, Nonce, Ciphertext,
          Tag } and calls symmetricEncryptionService.Decrypt(payload) → Result<byte[]>; a
          Result.Failure here (tamper / auth-tag mismatch) propagates as the documented
          ISymmetricEncryptionService failure mode (Error.Unexpected) — this converter does not invent
          a new tamper-specific exception type.
          Legacy plaintext (no "v" prefix): returned as-is — safe migration path from unencrypted data.
          Enabled == false: pass-through in both directions, no ISymmetricEncryptionService calls made.
          Holds IOptionsMonitor<EncryptionOptions> — hot-reload of CurrentVersion and key changes
          takes effect on the next read/write without a service restart (delegated to
          EncryptionOptionsKeyProvider, which reads optionsMonitor.CurrentValue on every call).
          Target encrypt version resolution (P-147, relocated by P-227): now resolved inside
          EncryptionOptionsKeyProvider.GetCurrentKey() as versionOverride.OverrideVersion ??
          options.CurrentVersion — see IEncryptionVersionOverride below for the rotation-scoped
          override seam; the precedence rule itself is unchanged from P-147.
          Do NOT instantiate directly in IEntityTypeConfiguration — use .Encrypt() extension (SK0304).
          ISymmetricEncryptionService is resolved from DI — registered by the CONSUMING SERVICE's own
          call to SharedKernel.Cryptography's AddSharedKernelCryptography(), not by this domain.
          EfCorePersistenceBuilder.WithEncryption() performs an eager startup check that
          ISymmetricEncryptionService is resolvable, throwing an actionable InvalidOperationException
          naming AddSharedKernelCryptography() if it is missing.

EncryptionOptionsKeyProvider  (internal sealed class, implements SharedKernel.Cryptography.IEncryptionKeyProvider)
    constructor: EncryptionOptionsKeyProvider(IOptionsMonitor<EncryptionOptions> optionsMonitor,
                                               IEncryptionVersionOverride versionOverride)
    .GetCurrentKey()                                            → CryptographicKey
    .GetKey(string keyId)                                       → CryptographicKey?
    NOTE: P-227. Bridges EncryptionOptions (this domain's existing options POCO) to
          SharedKernel.Cryptography.IEncryptionKeyProvider (the seam ISymmetricEncryptionService
          requires for key resolution). This is the ONLY IEncryptionKeyProvider implementation this
          domain ships — it is registered scoped, specifically as the key provider backing
          EncryptedValueConverter's injected ISymmetricEncryptionService for the persistence layer.
          GetCurrentKey(): resolves the target version as
          versionOverride.OverrideVersion ?? optionsMonitor.CurrentValue.CurrentVersion (identical
          precedence to the pre-P-227 design), Base64-decodes optionsMonitor.CurrentValue.Keys[version],
          returns new CryptographicKey(version, decodedBytes). This is how the existing rotation-scoped
          IEncryptionVersionOverride seam (P-147) continues to direct which key a rotation batch
          encrypts with.
          GetKey(keyId): looks up optionsMonitor.CurrentValue.Keys[keyId] directly — deliberately
          IGNORES versionOverride, because decryption always targets the exact KeyId recorded in the
          stored ciphertext's version prefix, never the current/override version. Returns null (never
          throws) when keyId is absent from Keys, per IEncryptionKeyProvider's documented contract.
          Registered scoped by EfCorePersistenceBuilder.WithEncryption() (matching
          IEncryptionVersionOverride's existing scoped lifetime) as
          SharedKernel.Cryptography.IEncryptionKeyProvider. A consuming service that separately uses
          SharedKernel.Cryptography for its own general-purpose (non-column) encryption needs and
          registers its own IEncryptionKeyProvider implementation must be aware both registrations
          target the same interface type — scope the registrations accordingly (e.g. keyed services,
          or accept that the persistence layer's converter resolves whichever IEncryptionKeyProvider
          was registered last/used by DI's normal single-registration-per-scope resolution rules).
          Reads optionsMonitor.CurrentValue fresh on every call — never a captured snapshot — so
          hot-reload of EncryptionOptions.Keys/CurrentVersion is preserved exactly as before P-227.

IEncryptionVersionOverride  (interface) / EncryptionVersionOverride  (sealed class — default impl)
    .OverrideVersion  (string?, mutable)
    NOTE: Scoped accessor (P-147) allowing EncryptionRotationService to direct a single batch's
          EncryptedValueConverter instances to encrypt with toVersion, without mutating
          EncryptionOptions.CurrentVersion. Registered as scoped by .WithEncryption(); a shared
          no-op instance (OverrideVersion always null) is used when not registered.
          EncryptionModelConvention resolves this from DI and passes it to every converter it constructs.
          Concurrent unrelated scoped DbContext instances are unaffected — each resolves its own
          scoped IEncryptionVersionOverride, defaulting to null (i.e., CurrentVersion).

.Encrypt(bool? enabled = true)  (extension method on PropertyBuilder<T>)
    NOTE: Writes annotation "SharedKernel:Encrypt" = true/false on the property.
          This is the ONLY permitted way to mark a property for encryption.
          Called inside IEntityTypeConfiguration<TEntity>.Configure(builder):
              builder.Property(x => x.Email).HasMaxLength(255).Encrypt().IsRequired();
          Passing false explicitly opts the property out even if future bulk-annotation approaches are added.
          The method returns the builder for fluent chaining.

EncryptionModelConvention  (sealed class, implements IModelFinalizingConvention)
    NOTE: Runs at model-finalization time (after all IEntityTypeConfiguration implementations).
          Scans all entity type properties for the "SharedKernel:Encrypt" annotation.
          Applies EncryptedValueConverter to each annotated property where annotation == true,
          passing the resolved IEncryptionVersionOverride (or shared no-op instance) to each converter.
          Registered automatically in SharedKernelDbContext.OnModelCreating — no manual call needed.
          Behavior is gated by EncryptionOptions.Enabled inside the converter, not the convention —
          the convention always wires the converter; Enabled == false makes the converter a pass-through.

EncryptionKeyNotFoundException  (sealed class, extends SharedKernelException)
    NOTE: Thrown by EncryptedValueConverter when the version prefix in stored ciphertext
          is not found in EncryptionOptions.Keys. Carries the unknown version string.
          Indicates a key was removed from options before all rows using it were rotated.

IEncryptionRotationJob  (interface)
    .RotateAsync(string fromVersion, string toVersion, CancellationToken ct) → Task<EncryptionRotationResult>
    NOTE: Abstraction for triggering key rotation. Registered only when .WithEncryption() is called.
          Trigger via Hangfire job, Temporal activity, hosted service, or management endpoint.
          Must NOT be injected in MediatR handlers, domain services, or any 03.Domain/05.Application type (SK0303).

EncryptionRotationResult  (record)
    .RowsProcessed   (int)
    .RowsRotated     (int)
    .RowsFailed      (int)
    .Errors          (IReadOnlyList<string>)

IEncryptedEntityBatchProcessor  (interface, internal — Encryption/Rotation/)
    .LoadBatchAsync(DbContext context, int skip, int take, CancellationToken ct) → Task<List<object>>
    NOTE: P-147 corrected design (see "EF Core 10 API correction" below). One closed generic
          implementation per encrypted entity type, registered at startup — NOT resolved via
          runtime MakeGenericMethod/Invoke.

EncryptedEntityBatchProcessor<TEntity>  (sealed class, implements IEncryptedEntityBatchProcessor)
    where TEntity : class
    .LoadBatchAsync(DbContext context, int skip, int take, CancellationToken ct) → Task<List<object>>
    NOTE: Body: `(await context.Set<TEntity>().Skip(skip).Take(take).ToListAsync(ct)).Cast<object>().ToList()`.
          context.Set<TEntity>() is the ordinary generic DbSet accessor — a direct generic method call,
          not a reflection invocation. Returns List<object> so the caller (EncryptionRotationService)
          can iterate with context.Entry(entity) regardless of CLR type.

EncryptionRotationService  (abstract class, implements IEncryptionRotationJob)
    NOTE: Uses IDbContextFactory<TContext> to open fresh contexts per batch (default batch size 500).
          Enumerates all entity types in the EF model that have encrypted properties (via
          context.Model.GetEntityTypes() and the "SharedKernel:Encrypt" annotation — model metadata
          access, not reflection over CLR members).
          LoadBatchAsync (P-147, CORRECTED): for each discovered entity type's ClrType, looks up the
          matching IEncryptedEntityBatchProcessor from a registry populated at startup (see
          EncryptedEntityBatchProcessorRegistry below) and calls its LoadBatchAsync. No
          GetMethod/MakeGenericMethod/Invoke anywhere in the rotation hot path or in registry lookup —
          the registry is a Dictionary<Type, IEncryptedEntityBatchProcessor> keyed by ClrType, populated
          via closed-generic constructor calls made at registration time (compile-time known types).
          For each batch: loads rows, checks whether stored value starts with "v{fromVersion}:";
          resolves the batch context's scoped IEncryptionVersionOverride and sets OverrideVersion = toVersion
          for the duration of SaveChangesAsync (resetting to null afterward) so EncryptedValueConverter
          re-encrypts with toVersion's key without mutating EncryptionOptions.CurrentVersion.
          Rotation is idempotent — rows already at toVersion are skipped.
          Keys read from IOptionsMonitor<EncryptionOptions> at RotateAsync call time.
          Subclasses supply TContext; may override batch size or pre/post-batch hooks.

EncryptedEntityBatchProcessorRegistry  (sealed class — Encryption/Rotation/)
    .TryGet(Type clrType, out IEncryptedEntityBatchProcessor? processor) → bool
    NOTE: P-147. Backs LoadBatchAsync's per-entity-type dispatch. Populated by
          EfCorePersistenceBuilder.WithEncryption() at DI-registration time: the builder iterates
          context.Model.GetEntityTypes() for entity types carrying the "SharedKernel:Encrypt"
          annotation on at least one property (the SAME discovery query EncryptionRotationService
          uses), and for each such ClrType constructs
          `new EncryptedEntityBatchProcessor<TEntity>()` via a small closed-generic helper method
          `CreateProcessor<TEntity>() => new EncryptedEntityBatchProcessor<TEntity>()` invoked through
          a switch/dispatch built from the model's entity CLR types at startup — a one-time,
          model-build-time operation, not a runtime hot-path reflection call.
          Because the set of entity types is enumerable from context.Model (not arbitrary user input),
          and EncryptedEntityBatchProcessor<TEntity> has a parameterless constructor, the registry can
          alternatively be populated via `Activator.CreateInstance(typeof(EncryptedEntityBatchProcessor<>)
          .MakeGenericType(clrType))` ONCE per entity type at startup — this is the documented,
          justified, model-build-time exception to the SK0xxx MakeGenericMethod/Invoke rule (same class
          of exception as ValueObjectOwnershipBuilder's startup-time model scan). Registered as a
          singleton; immutable after construction.
```

### EF Core 10 API correction (P-147, superseding original WO-024 spec text)

The original WO-024 spec for D-54 Issue 1 referenced a non-generic `DbContext.Set(Type entityType)`
overload. **This overload does not exist in EF Core 10.0.5** — `DbContext.Set` only has
`Set<TEntity>()` and `Set<TEntity>(string name)` generic overloads. Likewise,
`EntityFrameworkQueryableExtensions.ToListAsync` has no non-generic `IQueryable` overload — only
`ToListAsync<TSource>(IQueryable<TSource>, CancellationToken)`. The corrected design above
(`IEncryptedEntityBatchProcessor` / `EncryptedEntityBatchProcessor<TEntity>` /
`EncryptedEntityBatchProcessorRegistry`) achieves the same outcome — a reflection-free hot path —
using closed-generic types resolved once at startup instead of per-call `MakeGenericMethod`/`Invoke`.
This correction must be reflected in C-82/C-83 (Core phase) and T-47 (Tests phase) when implemented.

**EfCorePersistenceBuilder encryption wiring (WO-019):**

```text
.WithEncryption(Action<EncryptionOptions>? configure = null)
    — Registers EncryptionOptions via the Options system with the supplied configuration action.
    — Registers eager startup validation for EncryptionOptions.
    — Registers EncryptionOptionsKeyProvider as scoped SharedKernel.Cryptography.IEncryptionKeyProvider (P-227).
    — Registers an eager startup check (P-227) that SharedKernel.Cryptography.ISymmetricEncryptionService
      is resolvable from DI; throws an actionable InvalidOperationException naming
      AddSharedKernelCryptography() if it is not — the consuming service must call
      AddSharedKernelCryptography() itself (this domain does not call it).
    — Sets flag: .Build() registers IEncryptionRotationJob → EncryptionRotationService (scoped).
    — Optional. Omitting leaves all existing behavior unchanged (Enabled == false by default).

.WithApplicationTransactionBehavior()
    — Opt-in (P-228). Registers the same scoped EfUnitOfWork instance against
      SharedKernel.Application.Behaviors.IUnitOfWork in addition to its existing registration against
      SharedKernel.Persistence.Abstractions.IUnitOfWork — both registrations resolve the SAME scoped
      EfUnitOfWork instance per DI scope (not two independent instances).
      Enables 05.Application.Behaviors' TransactionBehavior to resolve a working unit-of-work without
      a hand-written composition-root adapter.
    — Optional. Omitting leaves SharedKernel.Application.Behaviors.IUnitOfWork unregistered — services
      not using TransactionBehavior, or bridging a non-EF-Core IUnitOfWork implementation, are unaffected.

.WithServiceName(string serviceName)
    — Registers PersistenceServiceOptions with the given ServiceName.
    — AuditInterceptor reads ServiceName as the unauthenticated audit fallback.
    — Optional. Omitting keeps the default "system" fallback.
```

**SharedKernelDbContext constructor update (WO-019):**
`SharedKernelDbContext` gains an additional optional constructor parameter `IOptionsMonitor<EncryptionOptions>? encryptionOptions`. When provided, `EncryptionModelConvention` receives the monitor. When not provided (or when `.WithEncryption()` is not called), the convention defaults to `Enabled = false` and all annotated properties are pass-through converters. This parameter is nullable/optional so all existing `SharedKernelDbContext` subclasses remain compatible without modification.

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
    .WithMigrationsOnStartup()
        — Opt-in. Marks that Database.MigrateAsync should run during host startup.
        — Does not by itself register a hosted service — see .Build() below.
        — Compatible with .WithCompiledModel(): MigrateAsync still applies pending SQL migrations;
          the compiled model only affects runtime query/model-building, not migration application.
    .AddSeeder<TSeeder>()
        — TSeeder : class, IDataSeeder<TContext> for the same TContext as the builder.
        — Registers TSeeder as scoped. Multiple calls accumulate into an ordered list, executed in
          call order during startup.
        — Opt-in. Seeders are idempotent by contract (not enforced by the framework) — see
          IDataSeeder<TContext> below.
    .Build()
        — Registers TContext as DbContext (scoped)
        — Registers SharedKernel.Persistence.Abstractions.IUnitOfWork → EfUnitOfWork (scoped)
        — Registers SharedKernel.Application.Behaviors.IUnitOfWork → the SAME scoped EfUnitOfWork
          instance ONLY when .WithApplicationTransactionBehavior() was called (P-228)
        — Registers ISpecificationEvaluator<T> → SpecificationEvaluator<T> (singleton — stateless)
        — Registers AuditInterceptor, SoftDeleteInterceptor, ConcurrencyInterceptor (scoped)
        — Registers any additional interceptors supplied via .AddInterceptor<T>() (scoped)
        — Registers no-op IUserContext placeholder (UserId = Guid.Empty, IsAuthenticated = false)
          if no IUserContext already registered; uses SharedKernel.Security.Abstractions.IUserContext
        — Calls AddDbContextFactory<TContext> when .WithDbContextFactory() was invoked
        — Applies UseModel(compiledModel) when .WithCompiledModel() was invoked
        — Registers MigrationAndSeedHostedService<TContext> as IHostedService ONLY when
          .WithMigrationsOnStartup() was called, or at least one .AddSeeder<TSeeder>() was called.
          If neither was called, no hosted service is registered — fully opt-in, zero overhead.
        — Registers EncryptionOptionsKeyProvider and performs the ISymmetricEncryptionService
          eager startup check ONLY when .WithEncryption() was called (P-227).
    NOTE: No outbox, Dapper, or PostgreSQL wiring in this builder. Those are separate concerns.
          ITransactionalUnitOfWork is only registered when .WithTransactionalUnitOfWork() is called.
          IUserContext and ITenantProvider are both sourced from SharedKernel.Security.Abstractions.
          All fluent methods return EfCorePersistenceBuilder for chaining.
```

#### Migrations and data seeding (`Seeding/`) — P-151

```text
IDataSeeder<TContext>  (interface)
    where TContext : DbContext
    .SeedAsync(TContext context, CancellationToken ct)          → Task
    NOTE: Idempotency is a CONTRACT, not enforced by the framework — implementations must check
          for existing data (or use upsert semantics) before inserting, since SeedAsync may run
          on every application startup. Each registered seeder receives its own DI scope and its
          own TContext instance, resolved via IDbContextFactory<TContext> (so .WithDbContextFactory()
          is implicitly required when any seeder is registered — EfCorePersistenceBuilder.Build()
          enables the factory automatically in this case if not already requested).

MigrationAndSeedHostedService<TContext>  (internal sealed class, implements IHostedService)
    where TContext : DbContext
    .StartAsync(CancellationToken ct)
    NOTE: Registered by EfCorePersistenceBuilder.Build() only when .WithMigrationsOnStartup() was
          called, or at least one seeder was registered via .AddSeeder<TSeeder>().
          Sequence on StartAsync:
            1. Acquire a PostgreSQL advisory lock via pg_advisory_lock(hashtext(lockKey)), using the
               existing IDbConnectionFactory — no new dependency on 02.Caching is introduced. The
               lock key is derived from the context type's full name so multiple replicas racing on
               startup serialize migration/seeding to a single instance.
            2. If migrations-on-startup was requested, call context.Database.MigrateAsync(ct). The
               compiled model (if supplied via .WithCompiledModel()) does not change this step —
               MigrateAsync applies SQL DDL independently of the runtime model.
            3. Run each registered seeder in registration order, each resolved in its own DI scope
               with its own TContext instance via IDbContextFactory<TContext>.
            4. Release the advisory lock via pg_advisory_unlock in a finally block, regardless of
               success or failure of steps 2-3.
          .StopAsync is a no-op.
          Alternative for non-PostgreSQL providers or stronger guarantees: consumers may instead
          wrap their own startup logic with SharedKernel.Caching.Redis.DistributedLocking — this is
          documented as an option, not implemented by this hosted service.
          Non-goals: this is not a migration-authoring tool (use `dotnet ef migrations add` as
          normal) and does not replace .WithCompiledModel() (P-106) — compiled models and
          migrations operate independently.
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
- Writing audit strings in any format other than `userId.ToString("D")` or `PersistenceServiceOptions.ServiceName` — the `"D"` lowercase hyphenated GUID format is the only permitted authenticated value; the unauthenticated fallback must come from options (never a hardcoded string literal other than the options default). `"N"`, `"B"`, `"P"`, `"X"` GUID formats are violations.
- Placing encryption attributes (any attribute whose name contains `Encrypt` or `Encrypted`) on domain entity classes — encryption is configured exclusively via `PropertyBuilder<T>.Encrypt()` inside `IEntityTypeConfiguration<TEntity>` implementations. Attributes on domain types create an infrastructure concern in the domain layer, violating DDD purity (SK0302).
- Instantiating `EncryptedValueConverter` directly inside `IEntityTypeConfiguration<TEntity>.Configure(builder)` and passing it to `.HasConversion(converter)` — `EncryptionModelConvention` applies the converter automatically after model finalization; manual instantiation produces duplicate or inconsistent converter registration (SK0304).
- Injecting `IEncryptionRotationJob` in any MediatR handler, domain service, application command/query handler, or any type in `03.Domain` or `05.Application` — key rotation is an infrastructure operation triggered via hosted service, Hangfire job, Temporal activity, or management endpoint only (SK0303).
- Using `AesGcm`, `Aes`, `SymmetricAlgorithm`, or any BCL symmetric cipher directly in `03.Domain` or `05.Application` layer types — all field-level encryption goes through `EncryptedValueConverter<T>` registered by the model convention (SK0301).
- Removing a key version from `EncryptionOptions.Keys` before completing the rotation of all rows that were encrypted with that version — doing so causes `EncryptionKeyNotFoundException` at query time for any row still carrying a ciphertext prefixed with the removed version.
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
- Expecting `IUnitOfWork.SaveChangesAsync`, the three platform interceptors, or domain event dispatch to run for rows affected by `IBulkMutationRepository.ExecuteUpdateAsync` or `ExecuteDeleteAsync` — both compile to a single server-side `ExecuteUpdate`/`ExecuteDelete` SQL statement that bypasses the `ChangeTracker` entirely. If audit fields, soft-delete flags, or domain events must be applied, do not use bulk mutation — load and save the aggregates normally.
- Calling `IBulkMutationRepository.ExecuteDeleteAsync` on an `ISoftDeletable` aggregate and expecting a soft delete — `ExecuteDeleteAsync` always issues a hard physical `DELETE`. Use `ExecuteUpdateAsync` with an explicit `setPropertyCalls` expression that sets the `IsDeleted`/`DeletedOn` columns if soft-delete semantics are required in bulk.
- Passing a specification with `Includes`, `StringIncludes`, `OrderBy`/`OrderByDescending`, `ThenBys`, `Skip`, or `Take` to `IBulkMutationRepository.ExecuteUpdateAsync`/`ExecuteDeleteAsync` — `BulkSpecificationGuard.Validate` throws `UnsupportedSpecificationException` for these shapes; only `Criteria` and `IncludeDeleted` are meaningful for a single bulk statement.
- Expecting `StreamAsync`/`StreamProjectedAsync<TResult>` to honor `spec.AsNoTracking == false` — both methods force `AsNoTracking()` unconditionally regardless of the specification's flag, to prevent unbounded `ChangeTracker` growth during long-lived enumeration. If tracked entities are required, use `ListAsync`/`ListPagedAsync` instead.
- Implementing `IHealthCheck` (Microsoft.Extensions.Diagnostics.HealthChecks) anywhere in `06.Persistence` — readiness probing is exposed as `DatabaseReadinessResult` plus `IDbConnectionFactory.CheckReadinessAsync`/`SharedKernelDbContext.CheckReadinessAsync` extension methods only; wrapping these in an `IHealthCheck` adapter is `13.ServiceDefaults`'s responsibility.
- Adding a project reference from any `06.Persistence` package to `02.Caching` (any `SharedKernel.Caching.*` package) to implement the `MigrationAndSeedHostedService` advisory lock — the lock uses the existing `IDbConnectionFactory` and PostgreSQL `pg_advisory_lock`/`pg_advisory_unlock`. Consumers wanting a stronger or cross-database lock may wrap their own startup logic with `SharedKernel.Caching.Redis.DistributedLocking` themselves; this domain does not take that dependency.
- Implementing `IDataSeeder<TContext>.SeedAsync` without an idempotency check (existence check or upsert) — `MigrationAndSeedHostedService` may invoke seeders on every application startup; non-idempotent seeders will duplicate data on redeploys.
- Calling `AesGcm`, `RandomNumberGenerator`, or any other `System.Security.Cryptography` symmetric-cipher type directly inside `EncryptedValueConverter` or anywhere else in `06.Persistence.EfCore` (P-227) — all field-level encryption cryptographic operations are delegated to the injected `SharedKernel.Cryptography.ISymmetricEncryptionService`; hand-rolling AES-GCM here duplicates `01.Core`'s own hard rule against hand-rolled symmetric encryption and was the exact defect P-227 corrected.
- Changing the on-disk ciphertext wire format (`"v{version}:{Base64(nonce||ciphertext||tag)}"`) for any reason, including the P-227 cryptography-delegation refactor — existing encrypted columns must continue to decrypt without a data migration; the wire format is owned exclusively by `EncryptedValueConverter`, never by `ISymmetricEncryptionService` or `EncryptedPayload`.
- Calling `06.Persistence`'s own `AddSharedKernelCryptography()` from inside this domain — `ISymmetricEncryptionService` registration is the CONSUMING SERVICE's responsibility (via `01.Core/SharedKernel.Cryptography`'s own DI extension); `EfCorePersistenceBuilder.WithEncryption()` only verifies the registration exists and fails fast with an actionable message if it does not.
- Constructing a second, independent `EfUnitOfWork`-equivalent instance to bridge `SharedKernel.Application.Behaviors.IUnitOfWork` (P-228) — `EfCorePersistenceBuilder.WithApplicationTransactionBehavior()` must register the SAME scoped `EfUnitOfWork` instance against both `IUnitOfWork` interfaces; two independent instances per scope would double-fire interceptors and domain event dispatch if both were ever resolved and called within the same logical operation.
- Registering `SharedKernel.Application.Behaviors.IUnitOfWork` unconditionally in `EfCorePersistenceBuilder.Build()` — the registration is opt-in via `.WithApplicationTransactionBehavior()` only; every `06.Persistence` consumer does not use `05.Application.Behaviors`' `TransactionBehavior`, and an unconditional registration would force an implicit, undocumented dependency surface on every consumer.

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

// With field-level encryption (optional — WO-019, cryptography delegated to 01.Core since P-227)
// Keys sourced from appsettings, environment variables, or Azure Key Vault mappings
// IMPORTANT (P-227): AddSharedKernelCryptography() MUST be called — WithEncryption() only verifies
// ISymmetricEncryptionService is resolvable and throws an actionable error if it is missing.
services.AddSharedKernelCryptography(configuration);   // 01.Core/SharedKernel.Cryptography
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithEncryption(enc =>
    {
        enc.Enabled = true;
        enc.CurrentVersion = "v1";
        enc.Keys["v1"] = "<Base64-encoded 32-byte key>";
        enc.Keys["v2"] = "<Base64-encoded 32-byte key>";   // add before rotating
    })
    .WithServiceName("order-service")   // replaces "system" in audit fallback
    .Build();

// Consuming service entity configuration (inside IEntityTypeConfiguration<Customer>.Configure):
//   builder.Property(x => x.Email).HasMaxLength(255).Encrypt().IsRequired();
//   builder.Property(x => x.PhoneNumber).HasMaxLength(20).Encrypt().IsRequired();

// Bridging 05.Application.Behaviors' TransactionBehavior to a real EfUnitOfWork (optional — P-228)
// Replaces the hand-written composition-root adapter previously documented in 05.Application/CLAUDE.md.
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithApplicationTransactionBehavior()   // registers EfUnitOfWork against SharedKernel.Application.Behaviors.IUnitOfWork too
    .Build();
// Application-layer registration (05.Application.Behaviors), unchanged:
//   services.AddSharedKernelApplicationBehaviors(cfg => cfg.AddTransactionBehavior());
// No manual adapter needed — TransactionBehavior resolves the same scoped EfUnitOfWork instance.

// Triggering key rotation — inject IEncryptionRotationJob in a Hangfire job / hosted service
//   await rotationJob.RotateAsync(fromVersion: "v1", toVersion: "v2", ct);

// With migrations-on-startup and data seeders (optional — P-151)
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UseNpgsql(connectionString))
    .WithMigrationsOnStartup()           // runs Database.MigrateAsync via MigrationAndSeedHostedService
    .AddSeeder<ReferenceDataSeeder>()    // runs SeedAsync once per registered seeder, in order
    .AddSeeder<DefaultTenantSeeder>()
    .Build();
// ReferenceDataSeeder : IDataSeeder<OrderDbContext> — SeedAsync must check for existing rows
// before inserting (idempotency is a contract, not enforced by the framework).

// Bulk mutation — server-side ExecuteUpdate / ExecuteDelete (P-148)
// Bypasses SaveChangesAsync, the three platform interceptors, and domain event dispatch.
var updatedCount = await orderRepository.ExecuteUpdateAsync(
    new OrdersOlderThanSpecification(cutoffDate),
    setters => setters.SetProperty(o => o.Status, OrderStatus.Archived),
    ct);

var deletedCount = await draftOrderRepository.ExecuteDeleteAsync(
    new DraftOrdersSpecification(),
    ct);
// ExecuteDeleteAsync always issues a hard physical DELETE, even for ISoftDeletable aggregates.

// Streaming large result sets (P-149) — AsNoTracking forced unconditionally
await foreach (var order in orderReadRepository.StreamAsync(new AllOrdersSpecification(), ct))
{
    // process one Order at a time without materializing the full list
}

await foreach (var dto in orderReadRepository.StreamProjectedAsync(new OrderSummaryProjection(), ct))
{
    // process one OrderSummaryDto at a time
}

// Readiness probes (P-150) — consumed by 13.ServiceDefaults health check adapters
var dbContextReadiness = await dbContext.CheckReadinessAsync(ct);
var connectionFactoryReadiness = await connectionFactory.CheckReadinessAsync(ct);
// Both return DatabaseReadinessResult { IsHealthy, Latency, Provider, ErrorMessage } and never throw.
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
- `EncryptedValueConverter` (P-227, delegation refactor) no longer calls `AesGcm`/`RandomNumberGenerator` directly — it delegates to the injected `ISymmetricEncryptionService` (`01.Core/SharedKernel.Cryptography`, itself BCL-only and AOT-compatible per `01.Core/CLAUDE.md`). The converter's own remaining responsibility (wire-format string parsing/packing, Base64 encode/decode, byte-slice concatenation) uses only BCL `string`/`Convert`/`Span<byte>` operations — no reflection. `IOptionsMonitor<EncryptionOptions>` and `IEncryptionVersionOverride` access are property reads on DI-managed instances. AOT-safe end-to-end.
- `EncryptionOptionsKeyProvider` (P-227) reads `IOptionsMonitor<EncryptionOptions>.CurrentValue` (property read) and performs a `Dictionary<string,string>` lookup plus `Convert.FromBase64String` — no reflection. Implements `SharedKernel.Cryptography.IEncryptionKeyProvider`, a pure interface. AOT-safe.
- `IEncryptionVersionOverride`/`EncryptionVersionOverride` (P-147) are a pure interface plus a tiny mutable-property class — BCL-only, zero reflection, AOT-safe. `EncryptionModelConvention` resolves the scoped instance via normal DI and passes it to `EncryptedValueConverter`'s constructor — a constructor call, not `Activator.CreateInstance`.
- `EncryptionModelConvention` scans `modelBuilder.Model.GetEntityTypes()` and `entityType.GetProperties()` at model-finalization time — model-build time only, not a hot path. The same `[DynamicallyAccessedMembers]` pattern applies if CLR property access is needed for annotation scanning; annotation access via EF Core's `IAnnotation` API is AOT-safe by design.
- `EncryptionRotationService` uses `IDbContextFactory<TContext>` which is AOT-compatible as of EF Core 8+. Batch processing is ordinary LINQ against the already-materialized EF model — no runtime-model scanning in the hot path.
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
- `EfUnitOfWork`'s dual-interface implementation (P-228) — declaring two interfaces on one class and satisfying both from a single method body is resolved entirely at compile time by the C# compiler; zero runtime type inspection, zero reflection. `EfCorePersistenceBuilder.WithApplicationTransactionBehavior()`'s registration (`services.AddScoped<SharedKernel.Application.Behaviors.IUnitOfWork>(sp => ...)`) is an ordinary DI factory delegate — AOT-safe.
- `EncryptionRotationService.LoadBatchAsync` (P-147, corrected for EF Core 10 — see "EF Core 10 API correction" above) is reflection-free in its hot path: each encrypted entity type has a closed-generic `EncryptedEntityBatchProcessor<TEntity>` (using ordinary `context.Set<TEntity>().Skip(skip).Take(take).ToListAsync(ct)`), looked up by `ClrType` from `EncryptedEntityBatchProcessorRegistry` — a plain `Dictionary<Type, IEncryptedEntityBatchProcessor>` lookup, no `GetMethod`/`MakeGenericMethod`/`Invoke` per call. The registry itself is populated once at startup (model-build time) — see the registry's NOTE for the documented, justified, startup-only `Activator.CreateInstance`/`MakeGenericType` exception (same class of exception as `ValueObjectOwnershipBuilder`). Hot-path AOT-safe; startup-time exception documented and isolated.
- `IBulkMutationRepository.ExecuteUpdateAsync` (P-148, corrected for EF Core 10) takes `Action<UpdateSettersBuilder<TAggregate>>` — a delegate consumed by EF Core's `ExecuteUpdateAsync` LINQ provider; the lambda body (`setters => setters.SetProperty(x => x.Prop, value)`) is translated by the provider via expression-tree analysis of the `SetProperty` calls performed against `UpdateSettersBuilder<T>`, not via runtime reflection. AOT-safe.
- `BulkSpecificationGuard.Validate<T>` (P-148) inspects `ISpecification<T>` properties (`Includes`, `StringIncludes`, `OrderBy`, etc.) via the interface's typed members — no reflection.
- `StreamAsync`/`StreamProjectedAsync<TResult>` (P-149) return BCL `IAsyncEnumerable<T>` via EF Core's `AsAsyncEnumerable()` — AOT-safe as of EF Core 8+. `[EnumeratorCancellation]` is a BCL attribute consumed by the compiler-generated async iterator, no reflection at runtime.
- `DatabaseReadinessResult` (P-150) is a BCL-only `sealed record` (`bool`, `TimeSpan`, `string`, `string?`) — AOT-safe by definition. `IDbConnectionFactory.CheckReadinessAsync` uses only `System.Data.IDbCommand.ExecuteScalar` and `System.Diagnostics.Stopwatch` — no reflection. `SharedKernelDbContext.CheckReadinessAsync` uses `Database.CanConnectAsync` and `Database.ProviderName` — both AOT-compatible EF Core APIs as of EF Core 8+.
- `IDataSeeder<TContext>` (P-151) is a pure generic interface — AOT-safe. `MigrationAndSeedHostedService<TContext>` uses `Database.MigrateAsync` (AOT-compatible EF Core 8+) and `IDbContextFactory<TContext>.CreateDbContextAsync` (AOT-compatible) — no reflection. The PostgreSQL advisory lock uses parameterized `pg_advisory_lock(hashtext(@lockKey))`/`pg_advisory_unlock` via `IDbConnectionFactory` and `IDbCommand` — no reflection.

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
- `EncryptionRotationService.LoadBatchAsync` reflection-free regression tests (P-147, corrected for EF Core 10): rotation over a multi-entity-type model rotates rows for all encrypted entity types via `EncryptedEntityBatchProcessorRegistry` + `EncryptedEntityBatchProcessor<TEntity>.LoadBatchAsync` (`context.Set<TEntity>().Skip().Take().ToListAsync(ct)`); batch-boundary tests at exactly, one less than, and one more than `BatchSize`; existing rotation idempotency tests continue to pass unmodified; registry lookup is a `Dictionary<Type, IEncryptedEntityBatchProcessor>` hit for every registered encrypted entity type (no missing-type fallback needed since the registry is built from the same model enumeration as `DiscoverEncryptedEntityTypes`).
- `IEncryptionVersionOverride` rotation-scoped override tests (P-147): `RotateAsync("v1","v2")` while `CurrentVersion == "v1"` re-encrypts rotated rows with `v2`'s key; `EncryptionOptions.CurrentVersion` remains `"v1"` after rotation; a concurrent unrelated scoped `EncryptedValueConverter` continues encrypting with `CurrentVersion` (no cross-scope leakage of the override); a fresh scoped converter after rotation resolves `OverrideVersion == null`.
- Doc-drift correction verification (P-147): no `EncryptedValueConverter<T>`/`EncryptedValueConverter<string>` generic type exists in the `Encryption/` namespace; `EncryptionModelConvention` references the non-generic `EncryptedValueConverter`; existing converter round-trip tests continue to pass unmodified.
- `IBulkMutationRepository.ExecuteUpdateAsync`/`ExecuteDeleteAsync` integration tests — SQLite (P-148): `ExecuteUpdateAsync` with a criteria-only spec returns the matched row count and updates only matched rows; `ExecuteDeleteAsync` physically removes matched rows even when the aggregate implements `ISoftDeletable`; both bypass `AuditInterceptor` (no `ModifiedBy`/`ModifiedOn` updates unless explicitly set via the `Action<UpdateSettersBuilder<TAggregate>>` delegate) and domain event dispatch; `spec.IncludeDeleted == true` applies `IgnoreQueryFilters` so previously soft-deleted rows are also matched.
- `BulkSpecificationGuard`/`UnsupportedSpecificationException` unit tests (P-148): each of `Includes`, `StringIncludes`, `OrderBy`/`OrderByDescending`, `ThenBys`, `Skip`, `Take` set to a non-default value throws `UnsupportedSpecificationException` naming the offending property; a spec with only `Criteria` (plus `IncludeDeleted`/`IsDistinct`/`AsNoTracking`) passes; `ExecuteUpdateAsync`/`ExecuteDeleteAsync` surface the exception directly with no SQL issued.
- `IReadRepository.StreamAsync`/`StreamProjectedAsync<TResult>` integration tests — SQLite (P-149): streaming a spec matching N rows yields exactly N items via `IAsyncEnumerable<T>` regardless of `spec.AsNoTracking`, with `ChangeTracker.Entries().Count() == 0` after full enumeration; `StreamProjectedAsync<TResult>` matches `ListProjectedAsync` for the same spec; `Skip`/`Take` bounds the streamed count to the expected window; cancellation mid-enumeration stops further yields; `ContractShapeTests` confirm both methods are declared on `IReadRepository<TAggregate, TId>`.
- `DatabaseReadinessResult`/`CheckReadinessAsync` tests (P-150): `SharedKernelDbContext.CheckReadinessAsync` against a healthy in-memory SQLite context returns `IsHealthy == true` with non-negative `Latency` and non-empty `Provider`; against an invalid connection string returns `IsHealthy == false` with `ErrorMessage` populated, without throwing; `IDbConnectionFactory.CheckReadinessAsync` against a healthy PostgreSQL Testcontainer returns `IsHealthy == true`; against a factory that throws on `CreateConnectionAsync` returns `IsHealthy == false` without throwing; `ContractShapeTests` confirm `DatabaseReadinessResult` is a `sealed record` with exactly the four documented properties in `SharedKernel.Persistence.Abstractions`.
- `IDataSeeder<TContext>`/`MigrationAndSeedHostedService` integration tests — SQLite/Testcontainers (P-151): `.AddSeeder<TSeeder>()` registers the seeder as scoped and the hosted service; starting the host invokes `SeedAsync` exactly once per registered seeder in registration order; running the host twice does not duplicate rows when the seeder checks for existing data; `.WithMigrationsOnStartup()` against PostgreSQL applies all pending migrations before any seeder runs; omitting both options registers no hosted service; two concurrent host instances racing against the same PostgreSQL Testcontainer serialize via the advisory lock with no duplicate-key or migration-conflict errors.
- **`ManyServiceProvidersCreatedWarning` suppression in EfCore tests** — any test class that creates more than ~20 distinct `DbContextOptions` hashes across the entire test process triggers EF Core's internal `ServiceProviderCache` limit and throws `ManyServiceProvidersCreatedWarning` as an error. Suppress via `.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))` in EVERY `DbContextOptionsBuilder` setup across ALL test classes that use the same `DbContext` type. One unsuppressed call in a sibling test class is enough to exceed the limit.
- **Model cache isolation for encryption tests** — `EncryptionAwareModelCacheKeyFactory` keys the EF Core model cache on `(contextType, designTime, IEncryptionVersionOverride reference)` — the override is compared by reference. Tests that construct `MultiPropDbContext` (or any `SharedKernelDbContext` subclass using encryption) without an explicit `IEncryptionVersionOverride` all share the `EncryptionVersionOverride.NoOp` singleton, producing the same cache key. The first test builds the model with its `IOptionsMonitor<EncryptionOptions>` captured in the converter; subsequent tests with different monitors get the CACHED model. Fix: always pass `new EncryptionVersionOverride()` (a fresh instance per call) in each test's `DbContextOptionsBuilder` setup so every test gets a unique cache entry and its own `IOptionsMonitor` captured.
- **`EncryptionRotationService.RotateAsync` `fromVersion` semantics** — `fromVersion` does NOT filter which rows are processed. All rows with any encrypted property are unconditionally re-encrypted using `toVersion`'s key. `fromVersion` is retained for API stability and audit/logging purposes only. Tests must not assert that rows prefixed with a different version are skipped — the correct assertion is `RowsRotated == total row count` regardless of what `fromVersion` value is passed.
- `EncryptedValueConverter` wire-format regression tests (P-227): a ciphertext string produced by the OLD hand-rolled `AesGcm` implementation (fixture captured before the delegation refactor, same key material) decrypts correctly under the NEW `ISymmetricEncryptionService`-delegating converter — proves the wire format is byte-for-byte unchanged and no data migration is required for existing encrypted columns. Round-trip (encrypt then decrypt with the new converter), legacy-plaintext pass-through, and `Enabled == false` pass-through tests are re-run unmodified against the refactored converter.
- `EncryptionOptionsKeyProvider` tests (P-227): `GetCurrentKey()` precedence (`OverrideVersion ?? CurrentVersion`) matches the pre-P-227 `EncryptedValueConverter.Encrypt` precedence exactly; `GetKey(keyId)` returns `null` (not a throw) for an unknown version; hot-reload via `IOptionsMonitor.CurrentValue` is observed on the next call without re-resolving the provider from DI.
- `EfUnitOfWork` dual-interface tests (P-228): resolving `SharedKernel.Persistence.Abstractions.IUnitOfWork` and `SharedKernel.Application.Behaviors.IUnitOfWork` from the same DI scope (after `.WithApplicationTransactionBehavior()`) returns the SAME instance by reference equality; `SaveChangesAsync` via either interface reference fires the identical interceptor chain and post-commit dispatch exactly once; omitting `.WithApplicationTransactionBehavior()` leaves `SharedKernel.Application.Behaviors.IUnitOfWork` unresolvable.
- `TransactionBehavior` end-to-end consumer-verify test (P-228): a full MediatR pipeline test host wiring `AddSharedKernelEfCore<TestDbContext>(...).WithApplicationTransactionBehavior().Build()` alongside `05.Application.Behaviors`' `AddTransactionBehavior()` — dispatching a test command persists the staged mutation exactly once after the handler returns; a thrown handler exception prevents any persistence (no partial commit). This test supersedes the documentation-only adapter example previously carried only in `05.Application/CLAUDE.md`.

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
- [2026-06-04] WO-019 (P-111..P-113) planned: field-level AES-256-GCM encryption subsystem documented — EncryptedValueConverter<string>, EncryptionOptions, PersistenceServiceOptions, EncryptionModelConvention (IModelFinalizingConvention), PropertyBuilder.Encrypt() annotation extension, IEncryptionRotationJob/EncryptionRotationService, EncryptionKeyNotFoundException; audit fallback hardcoded "system" replaced by configurable PersistenceServiceOptions.ServiceName; EfCorePersistenceBuilder gains .WithEncryption() and .WithServiceName(); SharedKernelDbContext gains optional IOptionsMonitor<EncryptionOptions> constructor parameter; six new hard violations; AOT notes for AesGcm/EncryptionModelConvention/EncryptionRotationService; DI registration example updated; test rules added for converter round-trip, tamper detection, hot-reload, legacy plaintext path, convention integration, rotation idempotency (arch-lead)
- [2026-06-12] WO-024 (P-147..P-151) planned: doc-drift corrected — `EncryptedValueConverter<string>`/`EncryptedValueConverter<T>` (nonexistent generic) replaced everywhere with the actual non-generic `EncryptedValueConverter : ValueConverter<string, string>`; new `IEncryptionVersionOverride`/`EncryptionVersionOverride` scoped seam documented (mutable `OverrideVersion`, resolved by `EncryptionModelConvention`, set/reset by `EncryptionRotationService.RotateAsync` around `SaveChangesAsync` without mutating `EncryptionOptions.CurrentVersion`); `EncryptionRotationService.LoadBatchAsync` documented as reflection-free via non-generic `Set(Type)`/`Skip`/`Take`/`ToListAsync(IQueryable, ct)`; new `IBulkMutationRepository<TAggregate, TId>` (EfCore-only — `SetPropertyCalls<TAggregate>` is an EF Core type) with `ExecuteUpdateAsync`/`ExecuteDeleteAsync`, implemented directly by `EfRepository<TAggregate, TId>`, both bypassing `SaveChangesAsync`/platform interceptors/domain event dispatch, `ExecuteDeleteAsync` always a hard physical DELETE even for `ISoftDeletable`; new `BulkSpecificationGuard`/`UnsupportedSpecificationException` restricting bulk specs to `Criteria`/`IncludeDeleted` only; `IReadRepository`/`EfReadRepository` gain `StreamAsync`/`StreamProjectedAsync<TResult>` returning `IAsyncEnumerable<T>` with AsNoTracking forced unconditionally (the one documented exception to the spec's AsNoTracking flag) and Skip/Take honored as a row-window; new `DatabaseReadinessResult` sealed record plus `IDbConnectionFactory.CheckReadinessAsync` (Abstractions) and `SharedKernelDbContext.CheckReadinessAsync` (EfCore) readiness probes, both BCL-only and never-throwing, with an explicit no-`IHealthCheck`-in-this-domain rule (that's `13.ServiceDefaults`'s job); new `IDataSeeder<TContext>` plus opt-in `EfCorePersistenceBuilder.WithMigrationsOnStartup()`/`.AddSeeder<TSeeder>()` registering `MigrationAndSeedHostedService<TContext>` which runs a PostgreSQL advisory lock (via existing `IDbConnectionFactory`, no new `02.Caching` reference), `Database.MigrateAsync`, and ordered seeders; ten new hard violations; AOT notes for the reflection-free rotation path, `IEncryptionVersionOverride`, `SetPropertyCalls<T>` expression trees, `IAsyncEnumerable<T>` streaming, BCL-only readiness probes, and `IDataSeeder`/`MigrationAndSeedHostedService`; eight new/expanded test-rule scenarios; four new DI registration examples (seeders/migrations, bulk mutation, streaming, readiness probes) (persistence-arch-planner)
- [2026-06-16] SK.06.Tests T-40..T-54 complete — three new test patterns added to Test Rules: (1) `ManyServiceProvidersCreatedWarning` suppression via `ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))` on every `DbContextOptionsBuilder` in every test class sharing the same DbContext type; (2) model cache isolation for encryption tests — `EncryptionAwareModelCacheKeyFactory` keys on the `IEncryptionVersionOverride` reference, so tests using different `IOptionsMonitor` instances but the same singleton override share a cached model and converter — fix by passing `new EncryptionVersionOverride()` per test; (3) `EncryptionRotationService.RotateAsync` `fromVersion` does NOT filter rows — all rows unconditionally re-encrypted with `toVersion` (sync-brain)
- [2026-06-15] SK.06.Design D-54..D-58 closeout — two EF Core 10.0.5 API-compatibility corrections verified by compiling sample code against the real package: (1) D-54/P-147 — `DbContext.Set(Type entityType)` non-generic overload does NOT exist in EF Core 10 (only `Set<TEntity>()`/`Set<TEntity>(string)`), and `EntityFrameworkQueryableExtensions.ToListAsync` has no non-generic `IQueryable` overload; `EncryptionRotationService.LoadBatchAsync`'s reflection-free design is corrected to a closed-generic `IEncryptedEntityBatchProcessor`/`EncryptedEntityBatchProcessor<TEntity>` per encrypted entity type, dispatched via a `EncryptedEntityBatchProcessorRegistry` (`Dictionary<Type, IEncryptedEntityBatchProcessor>`) populated once at startup from `context.Model.GetEntityTypes()` — hot path is a dictionary lookup + ordinary `context.Set<TEntity>().Skip().Take().ToListAsync(ct)`, with one documented startup-time `MakeGenericType`/`Activator.CreateInstance` exception (same class as `ValueObjectOwnershipBuilder`); (2) D-55/P-148 — `SetPropertyCalls<T>` no longer exists in EF Core 10; `IBulkMutationRepository<TAggregate,TId>.ExecuteUpdateAsync` signature corrected from `Expression<Func<SetPropertyCalls<TAggregate>, SetPropertyCalls<TAggregate>>>` to `Action<UpdateSettersBuilder<TAggregate>>` (the actual EF Core 10 `ExecuteUpdateAsync` delegate parameter type); D-56 (`IAsyncEnumerable`/`AsAsyncEnumerable`), D-57 (`CanConnectAsync`/`ProviderName`/`IDbCommand.ExecuteScalar`), and D-58 (`IHostedService`/`IDbContextFactory`/`Database.MigrateAsync`/`pg_advisory_lock` via `IDbConnectionFactory`) all confirmed implementable as originally specified — no changes. All five corrections/confirmations apply to C-82..C-92 (Core) and T-47..T-54 (Tests) when implemented (persistence-phase-implementer)
- [2026-06-30] WO-037 (P-227, P-228) planned. **P-227** — `EncryptedValueConverter`'s hand-rolled `AesGcm`/`RandomNumberGenerator` mechanism is replaced by delegation to `01.Core/SharedKernel.Cryptography`'s `ISymmetricEncryptionService`; new direct `ProjectReference` from `SharedKernel.Persistence.EfCore` to `SharedKernel.Cryptography` (legal, no layering exception); the converter's constructor gains an `ISymmetricEncryptionService` parameter and now packs/parses the unchanged `"v{version}:{Base64(nonce||ciphertext||tag)}"` wire format around calls to `Encrypt(byte[])`/`Decrypt(EncryptedPayload)` instead of calling `AesGcm` directly; new internal `EncryptionOptionsKeyProvider` implements `SharedKernel.Cryptography.IEncryptionKeyProvider`, bridging `EncryptionOptions`/`IOptionsMonitor` (hot-reload preserved) and relocating the existing P-147 `IEncryptionVersionOverride` precedence rule (`OverrideVersion ?? CurrentVersion`) into `GetCurrentKey()`; `EncryptionKeyNotFoundException` is preserved as the public exception type, now triggered by a `GetKey()` pre-check before delegating to `Decrypt`; `EfCorePersistenceBuilder.WithEncryption()` gains an eager startup check that `ISymmetricEncryptionService` is resolvable (the consuming service must call `AddSharedKernelCryptography()` itself — this domain does not call it); five new hard violations (no hand-rolled AES in this domain, wire format must never change, no self-registering cryptography, no duplicate `EfUnitOfWork`-equivalent instances, no unconditional `Behaviors.IUnitOfWork` registration); root `CLAUDE.md`'s encryption "What Goes Where" row to be corrected (by root-level sync) from "two independent implementations" to "delegation". **P-228** — `EfUnitOfWork` gains a second interface declaration, `SharedKernel.Application.Behaviors.IUnitOfWork`, satisfied by its existing `SaveChangesAsync` method body; new direct `ProjectReference` from `SharedKernel.Persistence.EfCore` to `05.Application/SharedKernel.Application.Behaviors` (legal: 06 may reference 01–05); new opt-in `EfCorePersistenceBuilder.WithApplicationTransactionBehavior()` registers the same scoped `EfUnitOfWork` instance against both `IUnitOfWork` interfaces, shipping as code the bridge `05.Application/CLAUDE.md` had only documented as a future option since WO-035; omitting the call leaves `Build()` unchanged. DI registration examples added for both phases; AOT notes added for the delegation refactor and the dual-interface declaration; Cross-Domain Dependencies and Packages table updated with the two new project references; 21 new state-map tasks (D-59..D-64, S-13..S-14, C-93..C-97, T-55..T-60, DO-37..DO-38) (persistence-arch-planner)
