# SharedKernel.Persistence.EfCore

EF Core 10 implementation of `SharedKernel.Persistence.Abstractions` for Platform.SharedKernel microservices: `EfRepository<TAggregate,TId>` / `EfReadRepository<TAggregate,TId>`, `EfUnitOfWork` / `EfTransactionalUnitOfWork`, the platform's three save-changes interceptors (Audit, SoftDelete, Concurrency — deliberately **no** outbox interceptor, see below), `SpecificationEvaluator<T>`, `SharedKernelDbContext` / `TenantedDbContext`, and the `EfCorePersistenceBuilder` fluent DI entry point (`AddSharedKernelEfCore<TContext>`). Field-level AES-256-GCM column encryption and the append-only audit trail are opt-in sibling packages — see below.

> **Outbox scope note:** the outbox pattern is owned entirely by `07.Messaging` via MassTransit's `UseEntityFrameworkOutbox`. No `OutboxMessage`/`IOutboxWriter`/`OutboxInterceptor` types exist in this package.

## Included types

- `SharedKernelDbContext` — abstract base; registers the three platform interceptors; `CurrentActor`/`RefreshActor` support DbContext pooling
- `TenantedDbContext` — multi-tenant base; installs an expression-tree global tenant query filter; `CurrentTenant`/`RefreshTenant` for pooling
- `EfRepository<TAggregate,TId>` / `EfReadRepository<TAggregate,TId>` — abstract bases consuming services extend per aggregate
- `EfUnitOfWork` / `EfTransactionalUnitOfWork` — the `IUnitOfWork.SaveChangesAsync` save boundary and explicit-transaction support. `EfUnitOfWork` satisfies both this domain's `IUnitOfWork` and `05.Application.Behaviors`' same-named seam, so `TransactionBehavior` commits through it — once per request, for the outermost command only, and only when the handler returns a successful `Result`
- `AuditInterceptor` / `SoftDeleteInterceptor` / `ConcurrencyInterceptor` — the platform three, always composed first
- `DomainClockMaterializationInterceptor` — registered automatically; gives every aggregate loaded from the database the application `IClock`, so a loaded aggregate can raise timestamped events and soft-delete. `EntityTypeConfigurationBase` also maps each aggregate's `Version` event sequence number as a column (existing databases need a migration adding it)
- `SpecificationEvaluator<T>` — criteria → keyset seek → includes → split-query → ordering → distinct → tracking → paging → projection, in that fixed order
- `EntityTypeConfigurationBase<TEntity,TId>`, `StronglyTypedIdValueConverter<TId,TValue>` — EF Core configuration building blocks
- `IRestorableRepository<TAggregate,TId>` / `EfRepository.RestoreAsync` — single-entity soft-delete restore (stages only, same save boundary as every other write)
- `IReadReplicaContextAccessor<TContext>` / `.WithReadReplica(...)` — opt-in read-replica routing for `IReadRepository`
- `CurrencyValueConverter` / `MoneyEntityTypeBuilderExtensions.Money(...)` / `ConfigureMoney()` — `03.Domain`'s `Money`/`Currency` value objects mapped as an EF Core 10 complex type with two independently queryable columns
- `[LoggerMessage]`-based structured logging (EventId sub-block `6000-6099`) across `ConcurrencyInterceptor`, `MigrationAndSeedHostedService`, and transient-retry diagnostics — never a key byte or plaintext/ciphertext value
- `EfCorePersistenceBuilder<TContext>` — fluent DI builder (`AddSharedKernelEfCore<TContext>(...)`)

Field-level column encryption (`.WithEncryption()`) and the append-only audit trail (`.WithAuditTrail()`) are extension methods on `EfCorePersistenceBuilder<TContext>` shipped by two sibling packages — `SharedKernel.Persistence.EfCore.Encryption` and `SharedKernel.Persistence.EfCore.Auditing` — not by this package. See their own `README.md` files; both require an explicit `ProjectReference`/`PackageReference` of their own before their `.With…()` method is even callable.

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.EfCore\SharedKernel.Persistence.EfCore.csproj" />
```

## Quick start — single-tenant service

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .Build();

services.AddOidcAuthentication(configuration);  // real IUserContext; replaces the AnonymousUserContext placeholder in any order
services.AddScoped<IRepository<Order, OrderId>, OrderEfRepository>();
services.AddScoped<IReadRepository<Order, OrderId>, OrderEfReadRepository>();
```

## Quick start — multi-tenant service

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithMultiTenancy()          // TContext must extend TenantedDbContext, or Build() throws
    .Build();

// WithMultiTenancy() TryAdds UserContextTenantProvider: the tenant from IUserContext.TenantId, or
// Guid.Empty (zero rows) when the caller has none. Register your own ITenantProvider only for another source.
services.AddOidcAuthentication(configuration);
```

Audit columns (`CreatedBy`/`ModifiedBy`/`DeletedBy`) store `IUserContext.SubjectId` as issued when the caller is authenticated and has a subject, otherwise `PersistenceServiceOptions.ServiceName` (default `"system"`, set with `.WithServiceName(...)`).

## DbContext pooling (opt-in, high-throughput services)

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithDbContextPooling(poolSize: 1024)
    .Build();
```

Consumer code injects `OrderDbContext` exactly as before — pooling and the per-lease user/tenant-context refresh are transparent. Cannot be combined with `.WithEncryption()` (throws an actionable `InvalidOperationException` at `Build()` time, enforced by the `SharedKernel.Persistence.EfCore.Encryption` package itself — this core builder has no compile-time knowledge that encryption exists).

## Field-level encryption and the audit trail live in sibling packages

Field-level column encryption (`.WithEncryption()`, `PropertyBuilder<T>.Encrypt()`) and the append-only, hash-chained audit trail (`.WithAuditTrail()`) are **not part of this package**. Both are extension methods on `EfCorePersistenceBuilder<TContext>` contributed by two opt-in sibling packages that each need their own `ProjectReference`/`PackageReference` before their `.With…()` method is even callable:

- **`SharedKernel.Persistence.EfCore.Encryption`** — see its own `README.md` for `.WithEncryption()`, `.Encrypt()`, blind-index equality search, and key rotation.
- **`SharedKernel.Persistence.EfCore.Auditing`** — see its own `README.md` for `.WithAuditTrail()`, `IAuditTrailWriter`, and chain verification.

## Configuration-section binding

`.WithServiceName(...)` also accepts an `IConfiguration` overload — binds `PersistenceServiceOptions` from `PersistenceServiceOptions.SectionName` (`"SharedKernel:Persistence"`) instead of a code-only `Action<T>`:

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithServiceName(configuration)    // binds configuration.GetSection(PersistenceServiceOptions.SectionName)
    .Build();
```

```json
{
  "SharedKernel": {
    "Persistence": { "ServiceName": "order-service" }
  }
}
```

## Soft-delete restore and command timeout (opt-in)

```csharp
// Single-entity restore — stages only; caller still calls SaveChangesAsync.
var order = await orderRepository.GetBySpecAsync(new ByIdSpecification<Order, OrderId>(orderId), ct);
if (order is not null)
{
    await orderRepository.RestoreAsync(order, ct);
    await unitOfWork.SaveChangesAsync(ct);
}

// Command timeout — provider-neutral (Microsoft.EntityFrameworkCore.Relational), not Npgsql-specific.
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithCommandTimeout(commandTimeoutSeconds: 30)
    .Build();
```

## Read-replica routing (opt-in)

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(primaryConnectionString))
    .WithReadReplica(options => options.UseNpgsql(replicaConnectionString))
    .Build();
```

`IReadRepository` reads are routed to the replica connection; `IRepository` writes always target the primary. **READ-AFTER-WRITE CONSISTENCY BECOMES THE CALLER'S RESPONSIBILITY ONCE ENABLED** — a handler that writes then immediately reads via `IReadRepository` in the same logical operation MAY OBSERVE STALE DATA under replication lag. A read issued inside an active transaction is NEVER routed to the replica, even when this is configured.

## Mapping `Money` (opt-in, requires `ConfigureMoney()`)

`03.Domain`'s `Money` value object maps as an **EF Core 10 complex type with two independently queryable columns** — `{property}_amount numeric(precision,scale)` and `{property}_currency char(3)` (exact column names follow whatever naming convention, e.g. snake_case, the consuming `DbContext` applies). `Amount` and `Currency` are filterable and aggregatable in SQL through this mapping (`WHERE`, `ORDER BY`, `SUM`, …) — `Money` now has a private, persistence-only two-parameter constructor EF Core's complex-type materialization binds directly. A stored amount with more decimal places than its currency's minor unit allows fails loudly on read instead of being silently re-rounded.

`ConfigureMoney()` **must** be called from `ConfigureConventions()` before `.Money(...)` is used anywhere in the model — omitting it fails model building, because EF Core's automatic navigation discovery walks `Money` (and transitively `Currency`) as a candidate entity type before `OnModelCreating` ever runs:

```csharp
using SharedKernel.Persistence.EfCore.Conversions;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    // Required once per DbContext — registers Money as a complex type before OnModelCreating's
    // automatic navigation discovery ever walks a Money-typed property, and registers the
    // Currency conversion globally for any standalone (not Money-nested) Currency property.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureMoney();
        base.ConfigureConventions(configurationBuilder);
    }
}

// Applied automatically by SharedKernelDbContext.OnModelCreating via
// ModelBuilder.ApplyConfigurationsFromAssembly — no manual registration needed.
internal sealed class OrderEntityConfiguration : EntityTypeConfigurationBase<Order, OrderId>
{
    public override void Configure(EntityTypeBuilder<Order> builder)
    {
        base.Configure(builder);

        builder.Money(x => x.Total);                                                   // total_amount / total_currency
        builder.Money(x => x.Discount, required: false, precision: 19, scale: 2, amountColumnName: "discount_amount");
    }
}
```

A `Money` property is deliberately excluded from `ValueObjectOwnershipBuilder`'s generic auto-owned scan — it must always be configured explicitly via `.Money(...)`.

## Explicit transactions, bulk mutation, streaming, keyset pagination

See [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full `ITransactionalUnitOfWork`/`IBulkMutationRepository`/`IReadRepository.StreamAsync`/`ListKeysetAsync<TKey>` examples, the canonical `SpecificationEvaluator<T>` pipeline order, and every hard violation this package enforces.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts, implementation rules, and AOT posture.
