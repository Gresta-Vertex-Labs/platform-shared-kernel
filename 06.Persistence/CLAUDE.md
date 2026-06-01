# 06.Persistence — Data Access Layer

## What This Domain Is

The persistence primitives layer. Every repository, unit of work, and data-access abstraction in a downstream microservice derives from the interfaces and base types defined here. This domain may reference `01.Core`, `03.Domain`, and `05.Application` — it must never be referenced by those layers in return.

Philosophy: **Abstraction-first. Provider-swappable. Specification-driven. Interceptor-composed.**

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | `IRepository<T,TId>`, `IReadRepository<T,TId>`, `IUnitOfWork`, `IDbConnectionFactory`, outbox contracts | `SharedKernel.Primitives`, `SharedKernel.Domain` |
| `SharedKernel.Persistence.EfCore` | EF Core implementation: `EfRepository<T,TId>`, `EfUnitOfWork`, `SharedKernelDbContext`, `SpecificationEvaluator<T>`, all interceptors (Audit, SoftDelete, Outbox, Concurrency) | `SharedKernel.Persistence.Abstractions`, `SharedKernel.Domain`, `Microsoft.EntityFrameworkCore` |
| `SharedKernel.Persistence.PostgreSQL` | PostgreSQL-specific conventions: Npgsql wiring, JSONB column support, pgvector support, Snake_case naming convention, sequence-based ID generators | `SharedKernel.Persistence.EfCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `SharedKernel.Persistence.Dapper` | Dapper micro-ORM read-side: `IDbConnectionFactory` implementation, strongly-typed ID type handlers, SmartEnum type handlers, base read-model query service | `SharedKernel.Persistence.Abstractions`, `Dapper` |

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
| --- | --- |
| Repository pattern | Pure C# 13 interfaces in `.Abstractions` — no ORM dependency |
| Unit of work | Pure C# 13 interface (`IUnitOfWork`) — provider-agnostic |
| Outbox contract | Pure C# 13 record (`OutboxMessage`) in `.Abstractions` |
| EF Core ORM | `Microsoft.EntityFrameworkCore` 10.x |
| EF Core interceptors | `ISaveChangesInterceptor` — audit, soft-delete, outbox, concurrency |
| Specification evaluation | Custom `SpecificationEvaluator<T>` translating `ISpecification<T>` to `IQueryable<T>` |
| PostgreSQL provider | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x |
| JSONB | Npgsql built-in JSON column support with STJ serialization |
| Vector search | `Pgvector.EntityFrameworkCore` for pgvector typed columns |
| Micro-ORM (read side) | `Dapper` |
| Connection factory | `Microsoft.Data.SqlClient` (SQL Server) or `Npgsql` (PostgreSQL) via `IDbConnectionFactory` |

---

## Interface Contracts

### `SharedKernel.Persistence.Abstractions` — public surface

#### Repository interfaces (`Repositories/`)

```text
IRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
    .GetByIdAsync(TId id, CancellationToken ct)                → Task<TAggregate?>
    .AddAsync(TAggregate aggregate, CancellationToken ct)      → Task
    .UpdateAsync(TAggregate aggregate, CancellationToken ct)   → Task
    .DeleteAsync(TAggregate aggregate, CancellationToken ct)   → Task
    NOTE: Write-side only. Does not expose IQueryable or raw SQL.

IReadRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
    .GetByIdAsync(TId id, CancellationToken ct)                                   → Task<TAggregate?>
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct)        → Task<TAggregate?>
    .ListAsync(ISpecification<TAggregate> spec, CancellationToken ct)             → Task<IReadOnlyList<TAggregate>>
    .CountAsync(ISpecification<TAggregate> spec, CancellationToken ct)            → Task<int>
    .AnyAsync(ISpecification<TAggregate> spec, CancellationToken ct)              → Task<bool>
    NOTE: Read-side only. Specifications control tracking, filtering, ordering, paging, and includes.
          Callers use ReadOnlySpecification<T> or PagedSpecification<T> for read-heavy paths.
```

#### Unit of Work (`UnitOfWork/`)

```text
IUnitOfWork
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>
    NOTE: Wraps the provider's commit boundary. EF Core implementation dispatches
          outbox writes and fires interceptors inside a single transaction.
```

#### Connection factory (`Connections/`)

```text
IDbConnectionFactory
    .CreateConnectionAsync(CancellationToken ct)               → Task<IDbConnection>
    NOTE: Returns an open connection. Caller is responsible for disposal.
          Used exclusively by Dapper read-side services — never by EF Core repositories.
```

#### Outbox contracts (`Outbox/`)

```text
OutboxMessage  (sealed record)
    .Id                                                        → Guid  (= Guid.NewGuid() at construction)
    .OccurredOn                                                → DateTimeOffset
    .Type                                                      → string  (fully-qualified event type name)
    .Payload                                                   → string  (JSON-serialized event)
    .ProcessedOn                                               → DateTimeOffset?  (null = unprocessed)

IOutboxWriter
    .WriteAsync(IEnumerable<OutboxMessage> messages, CancellationToken ct) → Task
    NOTE: Called by EfOutboxInterceptor inside the same DbTransaction as SaveChanges.
          Consuming services must not call this directly — it is driven by the interceptor.
```

#### Specification evaluator contract (`Specifications/`)

```text
ISpecificationEvaluator<T>
    .GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec) → IQueryable<T>
    NOTE: Applies criteria, includes, ordering, paging, distinct, and AsNoTracking to the input queryable.
          Lives in Abstractions so alternative evaluators (e.g., for Cosmos) can implement the same interface.
```

---

### `SharedKernel.Persistence.EfCore` — public surface

#### DbContext base (`Context/`)

```text
SharedKernelDbContext  (abstract class, extends DbContext)
    protected SharedKernelDbContext(DbContextOptions options)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>  (override — interceptors fire here)
    NOTE: Concrete downstream DbContexts must extend this base.
          Registers all interceptors and applies base model configurations on OnModelCreating.
          Does not contain any entity DbSets — those are declared by the consuming service's DbContext subclass.
```

#### EF Core repositories (`Repositories/`)

```text
EfRepository<TAggregate, TId>  (abstract class, implements IRepository<TAggregate, TId>)
    .GetByIdAsync(TId id, CancellationToken ct)                → Task<TAggregate?>
    .AddAsync(TAggregate aggregate, CancellationToken ct)      → Task
    .UpdateAsync(TAggregate aggregate, CancellationToken ct)   → Task
    .DeleteAsync(TAggregate aggregate, CancellationToken ct)   → Task
    NOTE: Backed by DbContext.Set<TAggregate>(). Concrete repositories extend this — do not register
          EfRepository<T,TId> directly in DI without a concrete subclass.

EfReadRepository<TAggregate, TId>  (abstract class, implements IReadRepository<TAggregate, TId>)
    .GetByIdAsync(TId id, CancellationToken ct)                → Task<TAggregate?>
    .GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct) → Task<TAggregate?>
    .ListAsync(ISpecification<TAggregate> spec, CancellationToken ct)      → Task<IReadOnlyList<TAggregate>>
    .CountAsync(ISpecification<TAggregate> spec, CancellationToken ct)     → Task<int>
    .AnyAsync(ISpecification<TAggregate> spec, CancellationToken ct)       → Task<bool>
    NOTE: Uses ISpecificationEvaluator<T> internally. Pass ReadOnlySpecification<T> subclasses to avoid
          unnecessary change-tracking.
```

#### EF Core unit of work (`UnitOfWork/`)

```text
EfUnitOfWork  (sealed class, implements IUnitOfWork)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>
    NOTE: Delegates to SharedKernelDbContext.SaveChangesAsync.
          Interceptors (Audit, SoftDelete, Outbox, Concurrency) fire automatically before the commit.
```

#### Specification evaluator (`Specifications/`)

```text
SpecificationEvaluator<T>  (sealed class, implements ISpecificationEvaluator<T>)
    .GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec) → IQueryable<T>
    Applies in order:
        1. Criteria (Where clause)
        2. Includes (eager loading — ThenInclude supported via include expressions)
        3. OrderBy / OrderByDescending / ThenBys
        4. Distinct (Distinct())
        5. AsNoTracking (AsNoTracking())
        6. Paging (Skip / Take) — applied last
    NOTE: Paging is always the final operation so ordering is stable before any Skip/Take.
```

#### Interceptors (`Interceptors/`)

```text
AuditInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SavingChanges: populates IHasCreatedAudit.CreatedBy / CreatedOn for Added entries;
      populates IHasAudit.ModifiedBy / ModifiedOn for Modified entries.
    — Requires IUserContext (from 12.Security) to resolve the current user string.
    — Does NOT modify entities directly; sets shadow or private-set properties via EF ChangeTracker.

SoftDeleteInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SavingChanges: converts Deleted state to Modified for ISoftDeletable entities;
      sets IsDeleted = true, DeletedOn = clock.UtcNow, DeletedBy = current user.
    — Requires IUserContext and IClock.

OutboxInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SavingChangesAsync: collects IDomainEvent list from all IAggregateRoot<TId> entries
      in the ChangeTracker; serializes events to OutboxMessage records via IOutboxWriter;
      clears DomainEvents via IHasDomainEvents.ClearDomainEvents() after successful write.
    — Uses IHasDomainEvents (not IAggregateRoot<TId>) for event collection.
    — Serialization uses STJ with DomainEventsJsonContext for type-safe, AOT-compatible serialization.
    — Runs inside the same SaveChanges transaction — outbox and aggregate state are always consistent.

ConcurrencyInterceptor  (sealed class, implements ISaveChangesInterceptor)
    — On SavingChangesAsync: when a DbUpdateConcurrencyException is caught for IHasConcurrency entries,
      wraps it in a typed ConcurrencyException carrying Error.Conflict(...) payload.
    NOTE: This interceptor does NOT silently retry — it translates the provider exception into a
          SharedKernel exception type for railway-friendly handling at the application layer.
```

#### Type configurations (`Configurations/`)

```text
EntityTypeConfigurationBase<TEntity, TId>  (abstract class, implements IEntityTypeConfiguration<TEntity>)
    — Provides base EF model configuration: TId primary key, concurrency token for IHasConcurrency,
      soft-delete global query filter for ISoftDeletable, owned audit properties for IHasAudit.
    NOTE: Concrete entity configurations must extend this base and call base.Configure(builder) first.

StronglyTypedIdValueConverter<TStronglyTypedId, TValue>  (sealed class, extends ValueConverter<TStronglyTypedId, TValue>)
    — Converts StronglyTypedId<TValue> to/from its primitive TValue for EF Core column mapping.
    — No reflection — uses the implicit operator TValue from StronglyTypedId<TValue>.
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

- `SharedKernel.Persistence.Abstractions` has **zero ORM dependencies** — references only `SharedKernel.Primitives` and `SharedKernel.Domain` from lower layers.
- `IRepository<TAggregate, TId>` and `IReadRepository<TAggregate, TId>` are **split by intention** — write repositories do not expose query methods; read repositories do not expose mutation methods. Consuming services should inject the narrowest interface needed.
- `IUnitOfWork.SaveChangesAsync` is the **only permitted save boundary** — calling `DbContext.SaveChanges[Async]` directly anywhere outside `EfUnitOfWork` is a hard violation.
- `OutboxInterceptor` collects domain events via `IHasDomainEvents` — **not** via `IAggregateRoot<TId>`. This satisfies the infrastructure dispatch rule from `03.Domain`.
- `OutboxInterceptor` calls `ClearDomainEvents()` on each aggregate root **after** the outbox write succeeds and **within** the same `SaveChanges` transaction. Events are never cleared on failure.
- `AuditInterceptor` populates audit fields exclusively via EF Core's `ChangeTracker` — it must never call setters directly on the aggregate or entity. Properties with `private set` are set via `CurrentValues[propertyName]` or shadow properties.
- `SoftDeleteInterceptor` converts the EF entity state from `Deleted` to `Modified` for `ISoftDeletable` entities. It must install a **global query filter** (`HasQueryFilter`) on all soft-deletable entities so deleted records are excluded from all queries by default. The filter is applied in `EntityTypeConfigurationBase`.
- `ConcurrencyInterceptor` must **not** swallow `DbUpdateConcurrencyException` — it wraps and rethrows as a typed exception carrying `Error.Conflict(...)`. The application layer handles the conflict.
- `SpecificationEvaluator<T>` must apply **paging last** — Skip/Take always follows ordering to produce stable, deterministic pages.
- `SpecificationEvaluator<T>` must apply `AsNoTracking()` when `ISpecification<T>.AsNoTracking == true` — read-only specifications must never incur change-tracking overhead.
- `SharedKernelDbContext` registers all interceptors in its constructor — downstream `DbContext` subclasses must not bypass interceptor registration by calling `optionsBuilder.AddInterceptors` with an incomplete set.
- `EfRepository<TAggregate, TId>` must never expose `IQueryable<TAggregate>` to callers — all queries are expressed via `ISpecification<T>` passed to `EfReadRepository`.
- `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` must use the `implicit operator TValue` from `StronglyTypedId<TValue>` — no reflection, no `Activator.CreateInstance`.
- `NpgsqlConnectionFactory` backs all Dapper read services — `IDbConnection` instances must be opened per operation and disposed promptly. Connection pooling is delegated to `NpgsqlDataSource`.
- `DapperReadService` subclasses must use **parameterized queries only** — string interpolation in SQL is a hard violation (SQL injection risk).
- `SmartEnumTypeHandler<TEnum,TValue>` must use `SmartEnum<TEnum,TValue>.TryFromValue` — no reflection, no `Enum.Parse`.
- `SnakeCaseNamingConvention` is applied at the EF model level — do not rely on `[Column("snake_name")]` data annotations; naming is applied globally by convention.
- `IDbConnectionFactory` is the **only permitted connection source** for Dapper services — `new NpgsqlConnection(connStr)` inline is a hard violation.
- No static mutable state anywhere in this domain.
- No messaging concerns (`IMessageBus`, `IEventPublisher`) — outbox messages are written; dispatching is the responsibility of `07.Messaging`.
- No domain logic anywhere in this domain — repositories and services are pure data-access plumbing.

---

## DI Registration (expected shape)

```csharp
// Abstractions — no direct DI registration; implemented by provider packages

// EF Core (consumer adds their own DbContext)
services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(connectionString)           // from PostgreSQL package
           .AddInterceptors(                       // interceptors registered by SharedKernelDbContext base
               new AuditInterceptor(userContext, clock),
               new SoftDeleteInterceptor(userContext, clock),
               new OutboxInterceptor(outboxWriter, jsonOptions),
               new ConcurrencyInterceptor()));

// EF repository pair — one registration per aggregate
services.AddScoped<IRepository<Order, OrderId>, OrderEfRepository>();
services.AddScoped<IReadRepository<Order, OrderId>, OrderEfReadRepository>();
services.AddScoped<IUnitOfWork, EfUnitOfWork>();

// PostgreSQL — shared NpgsqlDataSource and IDbConnectionFactory for Dapper
services.AddSharedKernelPostgreSQL(connectionString);

// Dapper — type handlers registered at startup
DapperTypeHandlers.Register();
```

`SharedKernel.Persistence.Abstractions` ships **no DI extensions** — it is a pure interface library.

---

## AOT Compatibility

- `IRepository<TAggregate, TId>`, `IReadRepository<TAggregate, TId>`, `IUnitOfWork`, `IDbConnectionFactory` are interfaces — AOT-safe by definition.
- `OutboxMessage` is a sealed record — AOT-safe.
- `IOutboxWriter` is an interface — AOT-safe.
- `SpecificationEvaluator<T>` applies `Expression<Func<T, bool>>` expression trees to `IQueryable<T>` — expression trees are AOT-safe when they do not reference runtime-only reflection APIs inside the lambda body.
- `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` uses the `implicit operator` (a static method) — no reflection, AOT-safe.
- `SmartEnumTypeHandler<TEnum,TValue>` uses `SmartEnum<TEnum,TValue>.TryFromValue` — no reflection in the hot path, AOT-safe.
- `OutboxInterceptor` serializes domain events using STJ. A `JsonSerializerContext`-based approach (`DomainEventsJsonContext`) is preferred for AOT safety — if the consuming service registers its event types in an STJ source-generated context, serialization is fully AOT-compatible.
- `AuditInterceptor` and `SoftDeleteInterceptor` access EF Core shadow properties by string key — shadow property access via `CurrentValues[name]` is AOT-safe (no reflection on CLR types).
- `SnakeCaseNamingConvention` operates on EF Core model metadata at model-building time — not in hot paths. AOT-safe.
- `JsonbEntityTypeBuilderExtension` and `VectorEntityTypeBuilderExtension` configure the EF model at startup — AOT-safe.
- `Microsoft.EntityFrameworkCore` — fully AOT-compatible as of .NET 8+ with compiled models; verify on each major upgrade.
- `Npgsql.EntityFrameworkCore.PostgreSQL` — verify AOT status on each major upgrade; the abstraction boundary allows a provider swap.
- `Dapper` — uses reflection for parameter binding and result mapping. This is a known AOT limitation. Place all Dapper code behind `DapperReadService` so the AOT boundary is contained to that class.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Persistence.Abstractions.Tests/` — interface contract shape, `OutboxMessage` record semantics.
- `SharedKernel.Persistence.EfCore.Tests/` — interceptor unit tests with an in-memory or SQLite provider; `SpecificationEvaluator<T>` expression evaluation tests; `EfRepository` / `EfReadRepository` with real SQLite.
- `SharedKernel.Persistence.PostgreSQL.Tests/` — integration tests with a real PostgreSQL Testcontainer; JSONB round-trip; vector column read/write; snake_case naming verification via `DbContext.Model`.
- `SharedKernel.Persistence.Dapper.Tests/` — type handler round-trip tests; `DapperReadService` query tests with a real PostgreSQL Testcontainer.
- `AuditInterceptor` tests: verify `CreatedBy`/`CreatedOn` set on Added entities; `ModifiedBy`/`ModifiedOn` set on Modified; no audit mutation on Deleted entities (SoftDeleteInterceptor handles those).
- `SoftDeleteInterceptor` tests: verify Deleted state converted to Modified; `IsDeleted = true`; `DeletedOn` and `DeletedBy` set; soft-deleted records excluded by global query filter.
- `OutboxInterceptor` tests: after `SaveChanges`, `OutboxMessage` records are written for every raised domain event; `DomainEvents` list is empty post-save; no outbox write on failed save; outbox and aggregate state are always consistent.
- `ConcurrencyInterceptor` tests: `DbUpdateConcurrencyException` is caught and rethrown as a typed `ConcurrencyException` carrying `Error.Conflict(...)`; non-concurrency exceptions are not swallowed.
- `SpecificationEvaluator<T>` tests: criteria, ordering, paging, distinct, AsNoTracking, and includes each verified independently; paging applied after ordering (determinism test).
- `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` tests: round-trip (entity to DB value, DB value to entity) with a concrete strongly-typed ID.
- `SmartEnumTypeHandler<TEnum,TValue>` tests: SetValue writes the underlying `TValue`; Parse reads back the correct enum member; unknown value falls through without reflection.
- `DapperReadService` tests: parameterized query returns correct result; `IDbConnectionFactory` is called once per operation; connection is disposed after each call.
- Integration tests (PostgreSQL, Dapper) must use Testcontainers — no mocked database connections. Import helpers from `16.Testing/SharedKernel.Testing`.

---

## Changelog

> Maintained by the persistence domain agent. One line per significant change.

- [2026-06-01] Domain brain initialized — packages, interfaces, rules, AOT notes, test rules
