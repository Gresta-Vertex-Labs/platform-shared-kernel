# SharedKernel.Persistence.EfCore

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![EF Core 10](https://img.shields.io/badge/EF%20Core-10-512BD4)](https://learn.microsoft.com/ef/core/)
[![PostgreSQL 15+](https://img.shields.io/badge/PostgreSQL-15%2B-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **EF Core on PostgreSQL, the way a multi-tenant production service needs it — registered with one call, correct
> by convention, and safe under retries, concurrency and row-level security.**

Getting EF Core right in a real service takes dozens of decisions, each easy to get subtly wrong: retry that breaks
explicit transactions, a tenant filter a child entity escapes, a lost update because nobody checks `If-Match`, a
domain event that fires for one save path but not another, a migration that runs twice on two replicas. This package
makes those decisions once. You write a `DbContext` with `DbSet`s and aggregates; the platform supplies the
conventions, the repositories, the unit of work, tenant isolation down to the database, and the startup checks that
refuse a misconfigured deployment.

| ⚡ One call | 🧱 Conventions | 🔁 Transactions | 🏢 Tenants |
| --- | --- | --- | --- |
| `AddSharedKernelPostgres<TContext>("orders")` | snake_case, strongly-typed ids, `Money`, audit and soft-delete columns | One retry-safe transaction per DI scope, every context included | Filter + write guard + transaction-local row-level security |
| Configuration from `ConnectionStrings:orders` | `xmin` optimistic concurrency on every aggregate root | Ambiguous commits never replayed | Cross-tenant work on a separate database role |
| No `.Build()`, validated at startup | No configuration base class | Domain events dispatched on every save | Startup checks for privileges and policy coverage |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration reference](#configuration-reference)
- [Security model](#security-model)
- [Pitfalls](#pitfalls)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)

## Install

```shell
dotnet add package SharedKernel.Persistence.EfCore
dotnet add package Microsoft.EntityFrameworkCore.Design   # for dotnet ef, with PrivateAssets="all"
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Database | PostgreSQL 15 or later (the only supported database) |
| Brings | `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.Npgsql`, Npgsql's EF Core provider, `EFCore.NamingConventions`, `Pgvector.EntityFrameworkCore` |
| Namespaces | `SharedKernel.Persistence` (registration), `SharedKernel.Persistence.EfCore` (model and migration helpers), `SharedKernel.Persistence.EfCore.Context` (context bases) |

| Companion package | Adds |
| --- | --- |
| [`SharedKernel.Persistence.EfCore.Encryption`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence/SharedKernel.Persistence.EfCore.Encryption) | `.UseFieldEncryption()`: encrypted columns, blind indexes, key rotation, per-tenant crypto-shredding |
| [`SharedKernel.Persistence.EfCore.Auditing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence/SharedKernel.Persistence.EfCore.Auditing) | `.UseAuditTrail()`: a tamper-evident audit ledger |
| [`SharedKernel.Persistence.Dapper`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence/SharedKernel.Persistence.Dapper) | Hand-written SQL that joins the same transaction |
| [`SharedKernel.Application.Behaviors`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/05.Application/SharedKernel.Application.Behaviors) | `TransactionBehavior`/`AuditingBehavior`: one transaction per MediatR command |
| [`SharedKernel.ServiceDefaults.Persistence`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence) | Readiness checks |
| [`SharedKernel.Persistence.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/16.Testing/SharedKernel.Persistence.Testing) | Fakes and a PostgreSQL fixture with the production role split, for test projects |

## Quick start

**1. The context** — one constructor, both parameters forwarded; no configuration class needed:

```csharp
using SharedKernel.Persistence.EfCore.Context;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)          // SharedKernelDbContext when there are no tenants
{
    public DbSet<Order> Orders => Set<Order>();
}
```

**2. The registration** — reads `ConnectionStrings:orders` and `SharedKernel:Persistence:orders`:

```csharp
using SharedKernel.Persistence;

builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p
    .UseMultiTenancy(rowLevelSecurity: true)    // tenant filter + write guard + row-level security
    .MigrateOnStartup());                        // migrations and seeders, one replica at a time
```

**3. Use it** — repositories for every aggregate are already registered; the unit of work commits:

```csharp
public sealed class RenameOrderHandler(IRepository<Order, OrderId> orders) : ICommandHandler<RenameOrder>
{
    public async Task<Result> Handle(RenameOrder command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.Id, ct);            // tracked, whole aggregate, caller's tenant only
        if (order is null) return Result.Failure(OrderErrors.NotFound(command.Id));

        order.Rename(command.Name);
        return Result.Success();                                          // TransactionBehavior saves and commits
    }
}
```

That is the whole setup. Everything below is detail.

## How it works

```text
 HTTP request / job ─► DI scope ─► IRequestContext (user, tenant)
                                        │
   IRepository<T,TId> ─┐                ▼
   IReadRepository ────┼─► TContext ─► one save interceptor ─► PostgreSQL (as app_runtime)
   IBulkMutation... ───┘      │         • audit stamps            • transaction-local app.tenant_id
                              │         • soft delete             • RLS policy: tenant_id = app.tenant_id
   IUnitOfWork ───────────────┘         • tenant stamp + guard    • xmin checked on every aggregate update
     ExecuteInTransactionAsync          • aggregate-root touch
     (retrying strategy, one            • domain events
      transaction per scope)
```

| What the one call registers | Lifetime |
| --- | --- |
| The `NpgsqlDataSource` for the connection name (shared with Dapper) | singleton |
| `TContext`, `IDbContextFactory<TContext>` (attaches the scope's caller) | scoped |
| `ICallerDbContextFactory<TContext>` (explicit caller, for background work) | singleton |
| `IRepository<,>`, `IReadRepository<,>`, `IBulkMutationRepository<,>` for every aggregate of the model | scoped, open-generic |
| `IUnitOfWork` (the first context), `IUnitOfWork<TContext>`, keyed `IUnitOfWork` per context type | scoped |
| `ICrossTenantScope`, `IPersistenceStartup` | scoped / singleton |
| A fail-closed anonymous `IRequestContext` and `IClock`, when none is registered | — |
| Startup validation of the options and the model (`ValidateOnStart`) | — |
| A hosted service that loads the ETag key from an asynchronous key provider (a KMS) before the host takes traffic | singleton |

**Conventions**, applied to every context, overridable by explicit configuration:

| You write | You get |
| --- | --- |
| `record OrderId(Guid Value) : StronglyTypedId<Guid>` | a `uuid` column, no converter to register |
| `Money Total` | `total_amount numeric(19,4)` + `total_currency char(3)`; required unless `Money?` |
| an aggregate root | PostgreSQL `xmin` as its concurrency token, exposed as an opaque `EntityVersion` token |
| `IHasAudit` / `ISoftDeletable` / `IHasTenant` | the columns, lengths and indexes; `CreatedBy`/`CreatedOn` written once and never updated |
| `OrderLine` in `Order.Lines` | changing a line touches the root row and checks its version |
| any name | `snake_case`, identifiers above 63 bytes truncated deterministically |

**Transactions.** `IUnitOfWork.ExecuteInTransactionAsync` runs the whole delegate inside the retrying execution
strategy, so a transient failure replays it from scratch. One transaction per DI scope: every context of the scope on
the same database joins it, nested calls join it, a failed nested call makes it rollback-only, and a `COMMIT` whose
outcome is unknown throws `CommitOutcomeUnknownException` instead of being replayed.

## Recipes

### 1. Optimistic concurrency with ETag / If-Match

The version is an opaque `EntityVersion`: PostgreSQL's `xmin` — a transaction counter the whole database shares —
sealed together with the aggregate's identity, so an ETag never reveals it. Register the service's root key provider
once; versions are sealed with a subkey derived from it (HKDF, purpose `SharedKernel.Persistence.EntityVersion`), so
it can be the same one field encryption uses:

```csharp
builder.AddSharedKernelKeyVaultKeyProvider();                       // production: a KMS (13.ServiceDefaults)
// or, keys already in memory (development, a secret store read at startup):
builder.Services.AddSingleton<ISynchronousEncryptionKeyProvider>(new StaticEncryptionKeyProvider("k1", [new("k1", key32)]));
```

A KMS is asked for the key at startup, before the host takes traffic (at most 10 seconds; a slower answer is still
used when it arrives), and again in the background every 5 minutes, so no request waits for the key service. If the
key cannot be loaded at startup, a warning is logged (6025, 6026), the service starts anyway, and the next request
that needs a version loads the key itself, blocking while it does. A failed load is not kept: while the key service
stays down, every such request retries the load and fails with a server error. Readiness does not wait for it:
persistence works without the key, only ETags need it.

Read the version from a **tracked** instance, and let `SharedKernel.Presentation.WebApi` carry it over HTTP (as
`samples/BillingApi` does):

```csharp
// Query handler: IRepository tracks; IReadRepository never does, and the version lives in the change tracker.
var order = await orders.GetByIdAsync(id, ct);
return new VersionedOrder(order!.ToView(), ConcurrencyVersion.Get(db, order));   // "AdU2…": 28 characters, never a number

// Endpoints
app.MapGet("/orders/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
    sender.Send(new GetOrder(new OrderId(id)), ct)
        .ToOkWithETag(found => found.Version.ToString(), found => found.Order));   // ETag; 304 on a matching If-None-Match

// IfMatch<EntityVersion> requires If-Match before the handler runs: missing or * → 428, malformed or several
// tags → 400, weak or not a version at all (a plain number) → 412.
app.MapPut("/orders/{id:guid}", (Guid id, RenameOrderRequest body, IfMatch<EntityVersion> ifMatch, ISender sender,
    CancellationToken ct) =>
    sender.Send(new RenameOrder(new OrderId(id), body.Name, ifMatch.Version), ct).ToNoContent());

// Command handler: a stale version fails the save with ConflictException (persistence.concurrency_conflict).
await orders.UpdateAsync(order, command.ExpectedVersion, ct);
```

The same version of the same aggregate always has the same ETag, so `If-None-Match` works. A token of another
aggregate (even one with the same `xmin`), an altered token, or one sealed with a key the service does not know — for
example issued before a restart that rotated the key — is a stale version: `ConflictException`
(`persistence.concurrency_conflict`), never a 500. `SharedKernel.Presentation.WebApi` answers it 412 when the request
carried `If-Match` or `If-None-Match`, and 409 otherwise; the 412 carries no ETag, so the client re-reads. A hand-written
response can read the current version with `ConcurrencyVersion.TryGetCurrentVersion(exception, out var current)`. A
running process keeps opening the tokens of the keys it used before a rotation.

`ConcurrencyVersion.Get` throws for an entity the context does not track rather than inventing a version, and throws
when no key provider is registered. A detached aggregate (deserialized, or loaded in another scope) must use
`UpdateAsync(aggregate, expectedVersion)` / `DeleteAsync(aggregate, expectedVersion)`. A context built by hand passes
its keys to `PersistenceContextDependencies.Create(..., entityVersionKeys: provider)`; it has no host, so with a KMS its
first version loads the key.

### 2. Queries, paging and projections

Queries are `03.Domain` specifications; paging happens at the call site:

```csharp
SpecificationBuilder<Order> Open() => Spec.For<Order>().Where(o => o.Status == OrderStatus.Open);   // builders are mutable

PagedList<Order> page = await orders.ListPagedAsync(Open().OrderByDescending(o => o.CreatedOn), PageRequest.Create(2, 50).Value, ct);
CursorPagedList<Order> slice = await orders.ListKeysetAsync(Open(), CursorPageRequest.Create(cursor, 50).Value, o => o.CreatedOn, descending: true, ct);
IReadOnlyList<OrderRow> rows = await orders.ListProjectedAsync(Open().Select(o => new OrderRow(o.Id, o.Total)), ct);
await foreach (var order in orders.StreamAsync(Open(), ct)) { /* constant memory */ }
```

An offset page needs an ordering; a keyset spec must not order itself (the key selector does, with the id as the
tiebreaker); a malformed cursor is a `ValidationException` (`pagination.cursor.invalid`).

Every query is tagged with the specification's type name and every repository call is traced, so a slow query leads
back to the code that issued it. Put `Include`s of the whole aggregate in a repository subclass's `AggregateQuery()`,
or in the entity configuration (`Navigation(...).AutoInclude()`).

### 3. Transactions around several repositories

```csharp
await unitOfWork.ExecuteInTransactionAsync(async ct =>
{
    var order = await orders.GetByIdAsync(id, ct);         // load INSIDE: the delegate may run again
    order!.Ship();
    await shipments.AddAsync(Shipment.For(order), ct);
    unitOfWork.OnBeforeCommit(token => outboxWriter.WriteAsync(..., token));
}, ct);
```

Keep HTTP calls and message publishing out of the delegate; queue them with `ICommandScope.OnCompleted`, which runs
after the commit. With MediatR, `TransactionBehavior` does exactly this for every command.

### 4. Multi-tenancy and row-level security

In a `TenantedDbContext` **every** entity type is tenant data: it implements `IHasTenant` — children of aggregates
included — or is global reference data marked `[TenantShared]` / `builder.IsTenantShared()`. Anything else fails the
model build. Each tenant type gets a named query filter, a write guard (writing another tenant's row is refused) and
`TenantId` as a concurrency token; an added child without a `TenantId` takes its aggregate's.

With `UseMultiTenancy(rowLevelSecurity: true)` every transaction binds `app.tenant_id` **transaction-locally**
(safe behind PgBouncer), and the policies come from one migration call:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // ... generated CreateTable calls ...
    migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!);   // FORCE RLS + policy on every tenant table
    migrationBuilder.EnableTenantRowLevelSecurity("payments");            // a table outside the EF model
}
```

At startup the runtime role's privileges and the policy coverage of every tenant table are checked (`Fail`;
coverage only warns in Development). No tenant bound means no rows.

### 5. Cross-tenant work

```csharp
using (crossTenantScope.Enter("monthly revenue report"))    // reason required; logged with the caller
{
    db.Database.UseCrossTenantConnection();                  // under RLS: the cross-tenant database role
    var all = await db.Invoices.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync(ct);
}
```

The scope belongs to the DI scope — entered anywhere in the request or job, it holds for every repository, context
and Dapper session of that scope until disposed, and never leaks to another. There is no escape token: under
row-level security the work runs as a separate role (`RowLevelSecurity:CrossTenantConnectionString`).

### 6. Migrations, seeding and `dotnet ef`

```csharp
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => OrderPersistence.Configure(p)
    .MigrateOnStartup(lockTimeout: TimeSpan.FromMinutes(5))
    .AddSeeder<ReferenceDataSeeder>());

// dotnet ef uses this factory: it connects as the migration role and sees the same model as the service.
public sealed class OrderDbContextFactory() : PostgresDesignTimeDbContextFactory<OrderDbContext>("orders")
{
    protected override OrderDbContext Create(DbContextOptions<OrderDbContext> options, PersistenceContextDependencies dependencies)
        => new(options, dependencies);

    protected override void ConfigurePersistence(EfCorePersistenceBuilder<OrderDbContext> persistence)
        => OrderPersistence.Configure(persistence);   // UseMultiTenancy(...).UseAuditTrail().UseFieldEncryption(...)
}
```

- `MigrateOnStartup()` applies migrations and runs seeders under a cross-replica advisory lock, over
  `MigrationConnectionString`. `IPersistenceStartup` completes when they are done; readiness checks wait for it.
- `ConfigurePersistence` gives the design-time model what the capability packages add (encrypted column widths,
  blind-index columns); share one method with `Program.cs`. A model with `.Encrypt()` refuses to build without it.
- The factory reads `--connection "…"`, then `SharedKernel:Persistence:{name}:MigrationConnectionString`, then
  `ConnectionStrings:{name}` (appsettings*.json and environment variables).
- In CI you can instead publish `dotnet ef migrations script --idempotent` and apply it as the owner role.

### 7. Several contexts

Call `AddSharedKernelPostgres` once per context. Inject `IUnitOfWork<TContext>` (or
`[FromKeyedServices(typeof(TContext))] IUnitOfWork`) to start the transaction on a specific context; contexts on the
same database share it. A context on another database cannot join: if it holds changes at commit, the commit is
refused. When the contexts share an assembly, override `ShouldApplyConfiguration(Type)` so each applies only its own
`IEntityTypeConfiguration<T>` classes.

### 8. Background work

`IDbContextFactory<TContext>` is scoped and attaches the scope's caller. A hosted service names its caller:

```csharp
await using var db = await callerFactory.CreateDbContextAsync(new SystemRequestContext([], "nightly-billing"), ct);
using (db.CrossTenantScope.Enter("nightly billing run")) { /* ... */ }
```

### 9. Bulk updates, JSONB and vectors

```csharp
await bulk.ExecuteUpdateAsync(Spec.For<Order>().Where(o => o.Status == OrderStatus.Draft && o.CreatedOn < cutoff),
    s => s.SetProperty(o => o.Status, OrderStatus.Expired), ct);      // one UPDATE, ModifiedBy/On stamped

builder.HasJsonbColumn(e => e.Settings, AppJsonContext.Default.OrderSettings);   // structural comparer
builder.HasVectorColumn(e => e.Embedding, dimensions: 1536)
       .HasVectorIndex(e => e.Embedding, VectorIndexMethod.Hnsw, VectorDistanceMetric.Cosine);
var nearest = Spec.For<Doc>().OrderBy(VectorOrderingExpressions.ByDistance<Doc>(d => d.Embedding, query, VectorDistanceMetric.Cosine)).Take(10);
```

Bulk statements skip the save pipeline and domain events; they refuse to set keys, concurrency tokens, `TenantId`,
creation audit columns or encrypted columns. Vectors need `ConfigureProvider(o => o.UseVector = true)`.

## Configuration reference

```csharp
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p
    .ConfigureProvider(o => { o.MaxRetryCount = 6; o.UseVector = true; })      // retry (0 = off), pgvector
    .ConfigureDataSource((sp, ds) => ds.MapEnum<OrderStatus>())                  // Npgsql data-source builder
    .ConfigureDbContext((sp, o) => o.UseModel(OrderDbContextModel.Instance))     // compiled model, other options
    .UseDbContextPooling()                                                       // safe with multi-tenancy
    .UseServiceName("orders-api")                                                // actor for writes without a user
    .UseUuidV7Keys()                                                             // generate unset Guid keys as UUID v7
    .AddInterceptor<MyInterceptor>()
    .MigrateOnStartup()
    .AddSeeder<ReferenceDataSeeder>());
```

| Setting | Where |
| --- | --- |
| Connection string (runtime role) | `ConnectionStrings:{name}` |
| Migration, read-only and cross-tenant connection strings, timeouts, TLS, `UseVector`, RLS checks | `SharedKernel:Persistence:{name}` — see [the Npgsql package](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence/SharedKernel.Persistence.Npgsql) |
| Service name (actor for system writes) | `SharedKernel:Persistence:ServiceName` or `UseServiceName` (default `system`) |
| Command timeout | the connection string's `Command Timeout` |
| Row-level security coverage check | `UseMultiTenancy(rowLevelSecurity: true, rowLevelSecurityCheck: RowLevelSecurityCheckMode.Warn)` (default: `Fail`, `Warn` in Development) |
| Row-level security privilege check | `SharedKernel:Persistence:{name}:RowLevelSecurity:PrivilegeCheck` (`Fail`, `Warn`, `Disabled`) |

**Errors.** Every PostgreSQL error from a save is classified: unique violation → `ConflictException`, foreign key →
`ValidationException`/`ConflictException`, row-level security or privilege → `Forbidden`, serialization failures and
timeouts → transient `Conflict`. Every concurrency conflict is `ConflictException` with code
`persistence.concurrency_conflict` — also for another tenant's row, so the answer never reveals that it exists.

**Telemetry.** `ActivitySource` and `Meter` `SharedKernel.Persistence`; logs in EventId range 6000–6099.
`SharedKernel.ServiceDefaults`' `WithPersistenceTelemetry()` wires them with Npgsql's own source.

## Security model

**Guarantees.**

- A tenant's rows are filtered by EF Core **and** by a PostgreSQL policy bound per transaction; a missing filter, an
  `IgnoreQueryFilters()` or hand-written SQL still sees only the caller's tenant.
- A caller without a tenant sees no tenant rows and cannot write any (fail closed).
- Writes to another tenant's row are refused and answered like a missing row.
- `CreatedBy`/`CreatedOn`, `TenantId`, keys and concurrency tokens cannot be rewritten through a save or a bulk update.
- A misconfigured deployment fails at startup: a runtime role that can bypass RLS or owns a protected table, a tenant
  table without its policy, an invalid option, a missing connection string.

**Not covered.** SQL injection (injected SQL runs as the application role — keep SQL parameterized), a superuser or
table-owner connection at runtime (they bypass RLS; the role split prevents it), and two-phase commit across
databases.

## Pitfalls

| Symptom | Cause and fix |
| --- | --- |
| `InvalidOperationException` "… is not tracked by this context" from `ConcurrencyVersion.Get` | The entity was read untracked. Read the version from `IRepository`, not `IReadRepository` |
| `InvalidOperationException` "no key provider is registered" from `ConcurrencyVersion.Get` | Versions are sealed with a subkey of the service's key provider. Register an `ISynchronousEncryptionKeyProvider` or `IEncryptionKeyProvider` (for example `AddSharedKernelKeyVaultKeyProvider()`) |
| Every `If-Match` is 412 right after a deploy | The version key rotated: ETags issued before the restart are stale. Clients re-read and retry once |
| Warning 6025 or 6026 at startup: the key that seals entity versions was not loaded | The KMS failed or was slow at startup. The service still runs; the next request that issues or checks an ETag loads the key, blocking while it does, and while the key service stays down every such request retries and fails with a server error. Check the key service and its credentials |
| `dotnet ef migrations add` refuses an encrypted model | The design-time factory lacks `ConfigurePersistence` with `UseFieldEncryption()` |
| Startup fails: "tenant tables are not protected" | A migration lacks `EnableTenantRowLevelSecurityForModel(TargetModel!)`, or a table was added later without `EnableTenantRowLevelSecurity("table")` |
| Startup fails: runtime role can bypass row-level security | Connecting as a superuser or table owner. Use the role script; in local development set `RowLevelSecurity:PrivilegeCheck` to `Warn` |
| Model build fails: "not tenant-scoped" | A child entity lacks `IHasTenant`, or reference data lacks `[TenantShared]` |
| A handler's side effect happens twice | The delegate replayed after a transient failure. Move side effects to `OnBeforeCommit` or `ICommandScope.OnCompleted` |
| `TransactionRolledBackException` though the outer code succeeded | A nested call failed and made the transaction rollback-only |
| `InvalidOperationException` on `UpdateAsync(detached)` | Pass the client's version: `UpdateAsync(aggregate, expectedVersion)` |

## AI quick reference

Rules for generating code with this package. Each line is a rule.

```text
REGISTER     builder.AddSharedKernelPostgres<TContext>("name", p => p.UseMultiTenancy(rowLevelSecurity: true)
             .MigrateOnStartup()); using SharedKernel.Persistence; no .Build(). One call per context.
CONFIG       ConnectionStrings:{name} = runtime role. SharedKernel:Persistence:{name}:MigrationConnectionString,
             :RowLevelSecurity:CrossTenantConnectionString. Never put a connection string in code.
CONTEXT      sealed class X(DbContextOptions<X> o, PersistenceContextDependencies d) : TenantedDbContext(o, d)
             (or SharedKernelDbContext). Only DbSet properties; no base configuration class.
ENTITIES     Every entity in a TenantedDbContext implements IHasTenant (children too) or is [TenantShared].
             Ids: record XId(Guid Value) : StronglyTypedId<Guid>(Value). Money maps itself. No [Column]/HasConversion
             for these. IEntityTypeConfiguration<T> only for lengths, indexes, .Encrypt(), jsonb, vectors.
READ         IReadRepository<T,TId> (never tracks) for queries; Spec.For<T>().Where(..).OrderBy(..);
             ListPagedAsync(spec, PageRequest) | ListKeysetAsync(spec, CursorPageRequest, key, descending) |
             *ProjectedAsync | StreamAsync. A keyset spec must not order itself.
WRITE        IRepository<T,TId> (always tracks): GetByIdAsync, AddAsync, UpdateAsync(agg[, version]),
             DeleteAsync(agg[, version]). Never call SaveChanges in a MediatR handler: TransactionBehavior commits.
TRANSACTION  unitOfWork.ExecuteInTransactionAsync(async ct => { load + change inside }, ct). The delegate may run
             again: no HTTP calls or publishing inside; use OnBeforeCommit / ICommandScope.OnCompleted.
ETAG         ConcurrencyVersion.Get(db, trackedEntity) -> EntityVersion; ToString() is the ETag value (an opaque
             token, never xmin); with 14.Presentation .ToOkWithETag(x => x.Version.ToString(), map). Write: an
             IfMatch<EntityVersion> parameter (or RequireIfMatch()) -> UpdateAsync/DeleteAsync(agg, ifMatch.Version);
             stale/foreign/altered -> ConflictException -> 412 when the request carries If-Match/If-None-Match (409
             otherwise); no catch needed. Never Get on an IReadRepository result. Needs a registered
             ISynchronousEncryptionKeyProvider/IEncryptionKeyProvider. Never build a version from a number.
TENANT       Tenant comes from IRequestContext.TenantId; never from a header, route or body.
CROSSTENANT  using (crossTenantScope.Enter("reason")) { db.Database.UseCrossTenantConnection(); ...
             IgnoreQueryFilters([PersistenceFilterNames.Tenant]) }. Never the parameterless IgnoreQueryFilters().
MIGRATIONS   Factory : PostgresDesignTimeDbContextFactory<T>("name") { Create => new(o, d);
             ConfigurePersistence(p) => SameMethodAsProgram(p); }. First migration adds
             migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!); new tenant tables later:
             EnableTenantRowLevelSecurity("table").
BACKGROUND   ICallerDbContextFactory<T>.CreateDbContextAsync(new SystemRequestContext([], "job"), ct), or a DI scope.
FORBIDDEN    DbContext.Database.BeginTransaction in application code; SaveChanges() (sync) with domain events;
             string-interpolated SQL; superuser/owner connection at runtime; IgnoreQueryFilters() without a name;
             a Guid.Empty tenant.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`; every change is deliberate and reviewed.
- **Every public member is documented**, including the exceptions it throws.
- **Tested against real PostgreSQL** (Testcontainers) with the production role split, including a
  [complete sample service](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/samples/BillingApi)
  exercised end to end over HTTP.
- **PgBouncer (transaction mode) safe:** no session state outlives a transaction.
- **Fail-closed defaults:** no tenant, no rows; an unknown configuration fails the start, not the first request.

**Deliberately not included:** other databases, read-replica routing inside one context, two-phase commit, a
generic repository base class to inherit, lazy loading.

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · start at the
[persistence overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence) ·
maintainer rules in [CLAUDE.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/CLAUDE.md).
