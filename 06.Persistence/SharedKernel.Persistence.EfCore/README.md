# SharedKernel.Persistence.EfCore

EF Core 10 on PostgreSQL for SharedKernel services — one registration call, platform behavior by convention.

## Install

```shell
dotnet add package SharedKernel.Persistence.EfCore
```

## Quick start

```csharp
// Program.cs — reads ConnectionStrings:orders (the Aspire / Testcontainers convention)
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p
    .UseMultiTenancy(rowLevelSecurity: true)   // TenantedDbContext only
    .MigrateOnStartup());

// The context: one constructor, both parameters forwarded.
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> o, PersistenceContextDependencies d)
    : TenantedDbContext(o, d)
{
    public DbSet<Order> Orders => Set<Order>();
}
```

`services.AddSharedKernelPostgres<TContext>(configuration, "orders", ...)` is the same call on an
`IServiceCollection`. There is no terminal `.Build()`.

What the one call gives you:

| Area | Behavior |
|---|---|
| Connection | The shared `NpgsqlDataSource` for `ConnectionStrings:{name}` (further settings in `SharedKernel:Persistence:{name}`), one per connection name, also used by Dapper. TLS `VerifyFull`; an `SSL Mode` written in the connection string is honored; a local host (or Development) may run without TLS. |
| Provider | snake_case names, retry on transient failures (on by default — `p.ConfigureProvider(o => o.MaxRetryCount = 0)` turns it off), SQLSTATE classification (unique → `ConflictException`, foreign key → `ValidationException`/`ConflictException`, ...). |
| Conventions | Every `StronglyTypedId<T>` reachable from the context's `DbSet`s is mapped; `Money` is a two-column complex type; audit, soft-delete, tenant and version columns are configured from the interfaces an entity implements (no base configuration class). `CreatedBy`/`CreatedOn` are written once and never updated. Every aggregate root gets PostgreSQL's `xmin` as its concurrency token. |
| Save pipeline | One interceptor, one change-detection pass: soft delete (children kept), aggregate-root touch (an owned or required-FK child change updates the root row and checks its version), audit stamps, tenant write guard. |
| Domain events | Dispatched before every asynchronous save, whichever code calls it (`IUnitOfWork`, a seeder, a factory user). A synchronous `SaveChanges` with pending events and a dispatcher throws. Without an `IDomainEventDispatcher` the events are discarded with a warning (startup also warns). |
| Concurrency | Every `DbUpdateConcurrencyException` becomes a `ConflictException` (`persistence.concurrency_conflict`); a `ForbiddenException` only when the row provably belongs to another tenant. |
| Services | `TContext` (scoped), `IDbContextFactory<TContext>` (scoped, attaches the scope's caller), `ICallerDbContextFactory<TContext>` (singleton, explicit caller), `IUnitOfWork` (the first registered context), `IUnitOfWork<TContext>` and a keyed `IUnitOfWork` per context type, the repositories, `ICrossTenantScope`, a fail-closed anonymous `IRequestContext` and `IClock` when none is registered. |
| Startup | The model is built and validated when the host starts (`ValidateOnStart`), and a missing connection string fails the start naming `ConnectionStrings:{name}`. |

## Options

```csharp
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p
    .ConfigureProvider(o => o.UseVector = true)                    // retry, pgvector
    .ConfigureDataSource((sp, ds) => ds.MapEnum<OrderStatus>())    // Npgsql data-source builder
    .ConfigureDbContext((sp, o) => o.UseModel(OrderDbContextModel.Instance)) // extra EF Core options (compiled model, ...)
    .UseDbContextPooling()
    .UseServiceName("orders-api")      // actor for writes without a user (default: SharedKernel:Persistence:ServiceName, else "system")
    .UseUuidV7Keys()                   // generate unset Guid / StronglyTypedId<Guid> keys with IIdGenerator (UUID v7)
    .MigrateOnStartup(lockTimeout: TimeSpan.FromMinutes(5))   // cross-replica lock wait, default 2 minutes
    .AddSeeder<ReferenceDataSeeder>());
```

The command timeout is the connection string's `Command Timeout`. Capability packages add their own options to the same builder: `.UseAuditTrail()`
(`SharedKernel.Persistence.EfCore.Auditing`) and `.UseFieldEncryption(...)` (`SharedKernel.Persistence.EfCore.Encryption`).

## Optimistic concurrency with ETag / If-Match

```csharp
var order = await db.Orders.SingleAsync(o => o.Id == id, ct);
var etag = ConcurrencyVersion.Get(db, order);          // send as ETag

// later, for PUT with If-Match: etag
ConcurrencyVersion.SetExpected(db, order, ifMatch);    // repositories: UpdateAsync(order, ifMatch)
await unitOfWork.SaveChangesAsync(ct);                 // stale → ConflictException

catch (ConflictException ex) when (ConcurrencyVersion.TryGetCurrentVersion(ex, out var current)) { /* 412 + current */ }
```

## Several contexts

Call `AddSharedKernelPostgres` once per context. Contexts with the same connection name share one data source.
Inject `IUnitOfWork<TContext>` (or `[FromKeyedServices(typeof(TContext))] IUnitOfWork`) for any context but the
first. When the contexts live in one assembly, override `ShouldApplyConfiguration(Type)` so each applies only
its own `IEntityTypeConfiguration<T>` classes (the default applies every configuration in the assembly).

## Background work

`IDbContextFactory<TContext>` is scoped: it attaches the caller of the scope it is resolved from. A singleton
(hosted service, scheduled job) either creates a scope, or injects `ICallerDbContextFactory<TContext>` and names
the caller:

```csharp
await using var db = await factory.CreateDbContextAsync(new SystemRequestContext([], "nightly-billing"), cancellationToken: ct);
```

## Cross-tenant access

```csharp
using (crossTenantScope.Enter("monthly revenue report"))   // reason required; actor, tenant and reason are logged
{
    var all = await db.Invoices.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync(ct);
}
```

The bypass belongs to the dependency-injection scope (the request or job), not to the calling method: entered
anywhere — including inside an awaited helper — it is honoured by every repository, context and Dapper session of
that scope until the handle is disposed, and never by another scope. A context from `ICallerDbContextFactory` carries
its own bypass, attributed to the explicit caller: `using (db.CrossTenantScope.Enter("reason")) { ... }`.

Seeders on a `TenantedDbContext` run inside a cross-tenant scope as the caller `seeder:{Type}`; they still
see the tenant query filter, so existence checks use `IgnoreQueryFilters([PersistenceFilterNames.Tenant])`.

## Migrations and seeding

`MigrateOnStartup()` and seeders run once per replica set under the Npgsql advisory migration lock (default wait
2 minutes, `MigrateOnStartup(lockTimeout)`). EF Core 9+ `Migrate()` also takes its own database lock, so a migration is
never applied twice; the platform lock additionally keeps seeders off a half-migrated schema.

## Read replicas

Not routed by this package. Use Npgsql multi-host (`Host=primary,replica;Target Session Attributes=prefer-standby`)
for a separate read context with its own connection name.

## Hand-built contexts (design-time factories, tools, tests)

```csharp
var options = new DbContextOptionsBuilder<OrderDbContext>().UsePostgres(dataSource).Options;
await using var db = new OrderDbContext(options, PersistenceContextDependencies.Create());
```

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see
[06.Persistence/CLAUDE.md](../CLAUDE.md) for the contracts and implementation rules.
