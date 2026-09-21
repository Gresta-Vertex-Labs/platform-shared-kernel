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
| Conventions | Every `StronglyTypedId<T>` reachable from the context's `DbSet`s is mapped; every `Money` property is a two-column complex type (`numeric(19,4)` + `char(3)`, required unless declared `Money?`) with no configuration — `builder.Money(e => e.Price, ...)` only changes the defaults; audit, soft-delete, tenant and version columns are configured from the interfaces an entity implements (no base configuration class). `CreatedBy`/`CreatedOn` are written once and never updated. Every aggregate root gets PostgreSQL's `xmin` as its concurrency token. |
| Save pipeline | One interceptor, one change-detection pass: soft delete (children kept), aggregate-root touch (a change to an owned entity, a child or a grandchild updates the root row and checks its version), audit stamps, tenant stamping and write guard. `ExecuteUpdate` can never set `TenantId`, the creation audit columns or a concurrency token. |
| Domain events | Dispatched before every asynchronous save, whichever code calls it (`IUnitOfWork`, a seeder, a factory user). A synchronous `SaveChanges` with pending events and a dispatcher throws. Without an `IDomainEventDispatcher` the events are discarded with a warning (startup also warns). A handler that throws abandons the save and clears the change tracker. |
| Concurrency | Every `DbUpdateConcurrencyException` becomes a `ConflictException` (`persistence.concurrency_conflict`) — also for a row of another tenant, so the answer never reveals that it exists. |
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

The version is an opaque `EntityVersion`: send `ToString()` as the ETag and parse the `If-Match` header back.

```csharp
var order = await db.Orders.SingleAsync(o => o.Id == id, ct);
response.Headers.ETag = $"\"{ConcurrencyVersion.Get(db, order)}\"";

// later, for PUT with If-Match
if (!EntityVersion.TryParse(request.Headers.IfMatch, out var ifMatch)) return Results.StatusCode(428);
await orders.UpdateAsync(order, ifMatch, ct);          // or ConcurrencyVersion.SetExpected(db, order, ifMatch)
await unitOfWork.SaveChangesAsync(ct);                 // stale → ConflictException

catch (ConflictException ex) when (ConcurrencyVersion.TryGetCurrentVersion(ex, out var current)) { /* 412 + current */ }
```

A **detached** aggregate (deserialized, or loaded in another scope) carries no version: `xmin` lives only in the
database. `UpdateAsync(detached)` and `DeleteAsync(detached)` therefore throw and point at
`UpdateAsync(aggregate, expectedVersion)` / `DeleteAsync(aggregate, expectedVersion)`; pass the version the client
read. (An aggregate with a `RowVersion` property, such as `FullAuditableAggregateRoot`, carries it and can be attached as is.)

## Transactions

`IUnitOfWork.ExecuteInTransactionAsync` runs the whole delegate in the retrying execution strategy.

- **One transaction per scope.** Every context resolved in the request scope that reaches the same database (host,
  port, database, user) shares the transaction's connection and is saved before the commit — also a change staged
  through the repository of a context other than the one whose unit of work started it. A context on another
  database cannot join: if it holds changes at commit, the commit is refused (rolled back) with an
  `InvalidOperationException`. Outside a transaction, `SaveChangesAsync` saves every joinable context in one transaction.
- **Nested calls join.** A nested `ExecuteInTransactionAsync` (on any context's unit of work) joins the running
  transaction. If it fails — a failed `Result` or an exception — the transaction becomes rollback-only: nothing
  commits, and an outer operation that still returns success gets `TransactionRolledBackException`.
- **Ambiguous commits are never replayed.** A `COMMIT` that fails without a response from the server throws
  `CommitOutcomeUnknownException` and is not retried; re-read the data (or the idempotency key) before repeating. A
  commit the server rejected (serialization failure, deferred constraint) rolled back and is retried or reported as usual.

## Several contexts

Call `AddSharedKernelPostgres` once per context. Contexts with the same connection name share one data source.
Inject `IUnitOfWork<TContext>` (or `[FromKeyedServices(typeof(TContext))] IUnitOfWork`) to start the transaction on
a specific context; any unit of work of the scope commits every joinable context (see above). When the contexts
live in one assembly, override `ShouldApplyConfiguration(Type)` so each applies only its own
`IEntityTypeConfiguration<T>` classes (the default applies every configuration in the assembly).

## Background work

`IDbContextFactory<TContext>` is scoped: it attaches the caller of the scope it is resolved from. A singleton
(hosted service, scheduled job) either creates a scope, or injects `ICallerDbContextFactory<TContext>` and names
the caller:

```csharp
await using var db = await factory.CreateDbContextAsync(new SystemRequestContext([], "nightly-billing"), ct);
// with a domain-event dispatcher: factory.CreateDbContextAsync(caller, dispatcher, ct)
```

## Multi-tenancy

A `TenantedDbContext` treats **every** entity type as tenant data: each non-owned type implements `IHasTenant` —
children of aggregates included — or is declared global reference data with `[TenantShared]` (on the class) or
`builder.IsTenantShared()`. Anything else fails the model build. Every `IHasTenant` type gets the tenant query filter,
the write guard and `TenantId` as a concurrency token; an added child whose `TenantId` is unset takes its aggregate's
(else the caller's). With `UseMultiTenancy(rowLevelSecurity: true)`, create the policies for the whole model in a
migration and let the startup check verify them (Fail by default, Warn in Development):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // ... CreateTable calls ...
    migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!);
}
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

`IPersistenceStartup` completes when every startup migration and seeder has finished (at once when none is
configured). `SharedKernel.ServiceDefaults.Persistence`'s `AddDatabaseReadinessCheck<TContext>()` and
`AddPersistenceStartupReadinessCheck()` report not-ready until then, and the audit ledger's self-check and sealer wait
for it, so a fresh deployment that creates its tables at startup is never checked against a missing schema.

**Adding migrations with split roles.** `dotnet ef` builds the context through a design-time factory; derive it from
`PostgresDesignTimeDbContextFactory<TContext>` so it connects as the migration (owner) role:

```csharp
public sealed class OrderDbContextFactory() : PostgresDesignTimeDbContextFactory<OrderDbContext>("orders")
{
    protected override OrderDbContext Create(DbContextOptions<OrderDbContext> options, PersistenceContextDependencies dependencies)
        => new(options, dependencies);
}
```

It reads `--connection "…"` (`dotnet ef database update -- --connection "…"`), then
`SharedKernel:Persistence:orders:MigrationConnectionString`, then `ConnectionStrings:orders`, from `appsettings*.json`
and environment variables. Reference `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets="all"`) in the
migrations project. In CI, publish `dotnet ef migrations script --idempotent -o migrate.sql` and apply it as the
owner role, or keep `MigrateOnStartup()` with a `MigrationConnectionString`.

## Read replicas

Not routed by this package. Use Npgsql multi-host (`Host=primary,replica;Target Session Attributes=prefer-standby`)
for a separate read context with its own connection name.

## Hand-built contexts (design-time factories, tools, tests)

```csharp
var options = new DbContextOptionsBuilder<OrderDbContext>();
options.UsePostgres(dataSource);
await using var db = new OrderDbContext(options.Options, PersistenceContextDependencies.Create());
```

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see
[06.Persistence/CLAUDE.md](../CLAUDE.md) for the contracts and implementation rules.
