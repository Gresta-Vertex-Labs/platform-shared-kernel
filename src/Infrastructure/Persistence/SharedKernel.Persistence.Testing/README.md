# SharedKernel.Persistence.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
[![Testcontainers](https://img.shields.io/badge/Testcontainers-PostgreSQL-2496ED?logo=docker&logoColor=white)](https://dotnet.testcontainers.org/)

> **Test services built on the SharedKernel persistence packages at both speeds: in-memory fakes that follow the
> production rules for unit tests, and a real PostgreSQL with the production role split for integration tests.**

A fake that behaves differently from production passes tests that production then fails: a repository that ignores
soft delete, a unit of work that never replays, a PostgreSQL connected as a superuser that silently bypasses every
row-level-security policy. The fakes here evaluate specifications, paging and cursors with the production rules and
roll back like the real unit of work; the PostgreSQL fixture creates the same four roles production uses, so
isolation is actually exercised.

| You get | So that |
| --- | --- |
| `FakeRepository<TAggregate, TId>` with the production specification, paging and cursor rules | Handlers are unit-tested against the real query semantics, soft delete included |
| `FakeUnitOfWork` with `TransientFailures` | A test proves a handler is re-runnable, as the retrying execution strategy requires |
| Rollback that restores every fake repository | A failed or replayed command leaves nothing behind, as in the database |
| `FakeAuditTrailWriter`, `FakeCrossTenantScope`, `FakeDbConnectionFactory`, `AddTestRequestContext()` | Every persistence seam has a recording double with `Should*` assertions |
| `PostgresTestServer` + `PostgresTestDatabase` | Integration tests run as `app_runtime`, so row-level security and grants are really enforced |
| Schema, RLS, audit-ledger and key-table helpers run as `app_migrator` | The database is shaped as production migrations shape it |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Persistence.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only**. `TestingNeverReferencedByProduction` fails any production project that
references a testing package.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Testing`, `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.EfCore`, `SharedKernel.Persistence.EfCore.Auditing`, `SharedKernel.Persistence.EfCore.Encryption`, `Testcontainers.PostgreSql`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Namespaces | `SharedKernel.Persistence.Testing`; `TestRequestContext` is in `SharedKernel.Testing.Execution` |
| Docker | Only for `PostgresTestServer.StartAsync()`; `FromExistingServer` uses a running server |

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Transactions;
using SharedKernel.Persistence.Testing;
using SharedKernel.Testing.Execution;
using Xunit;

public sealed class PlaceOrderHandlerTests
{
    [Fact]
    public async Task The_handler_is_rerunnable_and_commits_once()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var services = new ServiceCollection();
        var orders = services.AddFakeRepository<Order, OrderId>();
        var unitOfWork = services.AddFakeUnitOfWork();
        services.AddTestRequestContext(TestRequestContext.ForTenant(tenantId).WithPermissions("orders.write"));
        var audit = services.AddFakeAuditTrailWriter();
        services.AddScoped<PlaceOrderHandler>();
        await using var provider = services.BuildServiceProvider();

        unitOfWork.TransientFailures = 2;          // the operation runs three times, as under the retrying strategy
        var result = await provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            ct => provider.GetRequiredService<PlaceOrderHandler>().Handle(command, ct));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Single(orders.Items);                // the two failed attempts were rolled back
        audit.ShouldHaveAudited("order.place", "Order", result.Value.ToString());
    }
}
```

`Order`, `OrderId`, `PlaceOrderHandler` and `command` stand for your own types.

## How it works

**`FakeRepository<TAggregate, TId>`** — faithful where it matters:

- Soft-deleted aggregates (`ISoftDeletable { IsDeleted: true }`) are hidden from every read unless the specification
  includes them; `GetByIdAsync`, `GetByIdsAsync` and `ExistsAsync` hide them too.
- Distinct before ordering; Skip/Take last and only with a primary sort, otherwise `InvalidOperationException`.
- `ListPagedAsync` requires a primary sort and rejects a specification that pages itself; `ListKeysetAsync` rejects one
  that pages or orders itself, orders by key then identity (strongly-typed ids compare by value) and encodes cursors
  exactly as production does, so a cursor from the fake decodes against the real repository. A malformed cursor throws
  `ValidationException` (`SharedKernel.Core.Exceptions`).
- `CountAsync` ignores Skip/Take.

Simplified, by design:

- Writes apply immediately (no change tracker); tracked and untracked reads return the same instances.
- `AddAsync` throws on a duplicate key and `UpdateAsync` (`KeyNotFoundException`) on a missing one, to surface
  test-authoring bugs; `DeleteAsync` always removes, soft-deletable or not, and ignores a missing key.
- The `expectedVersion` overloads ignore the version: a real `EntityVersion` is an opaque token only the real
  repository issues. Pass `EntityVersion.None` and test concurrency and ETags against PostgreSQL.
- Includes and split queries are no-ops. `SimulateFailure` makes the writes throw; reads, `Seed` and `Reset` are
  unaffected.

**`FakeUnitOfWork`** runs the operation, calls `SaveChangesAsync`, runs every `OnBeforeCommit` callback, commits. An
exception or a failed `Result` rolls back; a nested call joins, and a failure there marks the transaction
rollback-only, so the outer call then throws `TransactionRolledBackException`. `TransientFailures = n` rolls back and
replays the operation n times after the save. `IsolationLevel` is ignored. `SaveChangesAsync` is a counter; with
`SimulateFailure` it throws.

**Rollback** puts back *which* aggregates each linked `FakeRepository` holds as of the transaction start. It does not
undo changes made to an aggregate object in place — load aggregates inside the operation, as production code must.
Only fakes registered through `AddFakeRepository` and `AddFakeUnitOfWork` are linked, in either order.

**Lifetimes and threads.** Every `Add*` helper registers one singleton instance, as the contract and as its concrete
type, and first **removes** any registration of the same contract — so the fakes also work on top of a service's real
composition root. The repository, audit writer and cross-tenant scope are thread-safe; a `FakeUnitOfWork` models one
logical transaction at a time.

**PostgreSQL.** `PostgresTestServer` starts a Testcontainers PostgreSQL (`postgres:16.4` by default) or wraps a
running server, and provisions the canonical roles of the
[`SharedKernel.Persistence.Npgsql`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Persistence/SharedKernel.Persistence.Npgsql/README.md)
role script once (each role's password is its name — test servers only):

| Role (`PostgresTestRoles`) | Used for |
| --- | --- |
| `app_migrator` (`Migrator`) | Owns the database and every table, runs migrations (`MigrationConnectionString`) |
| `app_runtime` (`Runtime`) | The application (`ConnectionStrings:{name}`): no superuser, no `BYPASSRLS`, owns nothing |
| `app_cross_tenant` (`CrossTenant`) | Cross-tenant work (`RowLevelSecurity:CrossTenantConnectionString`), `BYPASSRLS` |
| `app_audit_sealer` (`AuditSealer`) | The audit sealer's own data source |

`CreateDatabaseAsync()` creates a database owned by `app_migrator` with the role script's grants and default
privileges; disposing it drops it (`WITH (FORCE)`).

## Recipes

### 1. Integration test with tenant isolation

```csharp
using SharedKernel.Persistence;          // AddSharedKernelPostgres

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgresTestServer Server { get; private set; } = null!;
    public async Task InitializeAsync() => Server = await PostgresTestServer.StartAsync();   // "pgvector/pgvector:pg16" for vectors
    public async Task DisposeAsync() => await Server.DisposeAsync();
}

public sealed class OrderTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Orders_are_tenant_isolated()
    {
        await using var database = await fixture.Server.CreateDatabaseAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelPostgres<OrderDbContext>(database.BuildConfiguration("orders"), "orders",
            p => p.UseMultiTenancy(rowLevelSecurity: true));
        var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(tenantA));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            await database.CreateSchemaAsync(context);              // as app_migrator, like a migration
            await database.EnableRowLevelSecurityAsync(context);    // EnableTenantRowLevelSecurityForModel
        }
        // write as tenant A, set caller.TenantId = tenantB, assert the rows are invisible
    }
}
```

With real migrations call `.MigrateOnStartup()` instead: the generated configuration already points
`MigrationConnectionString` at `app_migrator`.

### 2. Audit ledger with a separate sealer role

```csharp
await database.CreateAuditLedgerAsync(separateAuditSealer: true);
var configuration = database.BuildConfiguration("billing", separateAuditSealer: true);
services.AddSharedKernelNpgsql(configuration.GetSection("SharedKernel:Persistence:audit-sealer"), "audit-sealer");
```

### 3. End-to-end over HTTP

```csharp
var server = await PostgresTestServer.StartAsync("postgres:17-alpine");
await using var database = await server.CreateDatabaseAsync();
var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
{
    host.UseEnvironment("Production");                 // every startup check fails closed
    host.ConfigureAppConfiguration((_, config) => config
        .AddInMemoryCollection(database.ConfigurationFor("billing", separateAuditSealer: true)));
});
// migrations run at startup as app_migrator; poll /health/ready, then call the API
```

The [Shop sample](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/samples/Shop)'s
`Shop.Ordering.Tests` runs Ordering's use cases through the real pipeline over these fakes, and its `Shop.E2E` flows
prove the rest against PostgreSQL over HTTP: tenant isolation, ciphertext at rest and the audit ledger.

### 4. Prove a cross-tenant path was entered with a reason

```csharp
var scope = services.AddFakeCrossTenantScope();
// … run the report job …
scope.ShouldHaveEntered("monthly-revenue-report");
Assert.Equal(0, scope.Depth);                          // every scope was disposed
```

Set `scope.DenyWith = new UnauthorizedAccessException()` to test the refusal path.

## Reference

### Registration (`PersistenceTestingServiceCollectionExtensions`)

Each replaces any registration of the contract, registers one singleton and returns it.

| Method | Registers |
| --- | --- |
| `AddFakeRepository<TAggregate, TId>(IEnumerable<TAggregate>? seed = null)` | `IRepository<,>`, `IReadRepository<,>`, `FakeRepository<,>` (`TAggregate : IAggregateRoot<TId>`) |
| `AddFakeUnitOfWork()` | `IUnitOfWork`, `FakeUnitOfWork` |
| `AddTestRequestContext(TestRequestContext? context = null)` | `IRequestContext`, `TestRequestContext` (default `TestRequestContext.ForUser()`) |
| `AddFakeCrossTenantScope()` | `ICrossTenantScope`, `FakeCrossTenantScope` |
| `AddFakeAuditTrailWriter()` | `IAuditTrailWriter`, `FakeAuditTrailWriter` |

### Fakes

| Type | Implements | Test surface |
| --- | --- | --- |
| `FakeRepository<TAggregate, TId>(Func<TAggregate, TId> idSelector, IEnumerable<TAggregate>? seed = null)` | `IRepository<,>` | `Items` (live, soft-deleted included), `Seed(…)`, `Reset()`, `SimulateFailure` |
| `FakeUnitOfWork` | `IUnitOfWork` | `TransientFailures`, `TransactionCount`, `CommitCount`, `RollbackCount`, `SaveChangesCallCount`, `SaveChangesResult` (default 1), `SimulateFailure`, `IsTransactionActive`, `Reset()` |
| `FakeAuditTrailWriter` | `IAuditTrailWriter` | `Recorded`, `ShouldHaveAudited(action, resourceType, resourceId)`, `SimulateFailure`, `Reset()` (entries only) |
| `FakeCrossTenantScope` | `ICrossTenantScope` | `EnteredReasons`, `Depth`, `IsActive`, `ForceActive`, `DenyWith`, `ShouldHaveEntered(reason)`, `Reset()` |
| `FakeDbConnectionFactory(Func<DbConnection>)` | `IDbConnectionFactory` | Calls your delegate on every `CreateConnectionAsync` |

Assertions throw `InvalidOperationException` with a readable message; no test framework is referenced.

### PostgreSQL

| Member | Does |
| --- | --- |
| `PostgresTestServer.StartAsync(string image = "postgres:16.4", CancellationToken)` | Starts a container; `DisposeAsync` removes it |
| `PostgresTestServer.FromExistingServer(string adminConnectionString)` | Uses a running server (superuser login); `DisposeAsync` leaves it running |
| `CreateDatabaseAsync(string? name = null, CancellationToken)` | A new database (lowercase identifier, unique by default) |
| `ConfigurationFor(name, separateAuditSealer = false)` / `BuildConfiguration(name, additional, separateAuditSealer)` | `ConnectionStrings:{name}`, `SharedKernel:Persistence:{name}:MigrationConnectionString`, `…:RowLevelSecurity:CrossTenantConnectionString` (+ sealer keys) |
| `AdminConnectionString`, `MigratorConnectionString`, `RuntimeConnectionString`, `CrossTenantConnectionString`, `AuditSealerConnectionString` | One connection string per role |
| `CreateSchemaAsync(DbContext)` | The model's create script, as `app_migrator` (`EnsureCreated` as the runtime role fails by design) |
| `EnableRowLevelSecurityAsync(DbContext, string? crossTenantRole = null)` | `EnableTenantRowLevelSecurityForModel` over the model |
| `CreateAuditLedgerAsync(bool separateAuditSealer = false)` | `CreateAuditLedgerTable` + the cross-tenant `REVOKE` of the role script |
| `CreateTenantEncryptionKeyTableAsync()` | `CreateTenantEncryptionKeyTable` + the `REVOKE` that keeps tombstones undeletable |
| `ApplyMigrationAsync(Action<MigrationBuilder>)` | Any `MigrationBuilder` operations through EF Core's SQL generator, as `app_migrator` |
| `ExecuteAsMigratorAsync(sql)` / `ExecuteAsAdminAsync(sql)` | Raw SQL (`CREATE EXTENSION vector` needs the admin) |

## Testing

This package is the test double; its self-tests live in
[`SharedKernel.Persistence.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/src/Infrastructure/Persistence/SharedKernel.Persistence.Testing/SharedKernel.Persistence.Testing.Tests)
(Integration lane, Docker) and prove the fakes against the production rules and the fixture against real role
attributes and tenant isolation. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `TestRequestContext`, `FakeClock` and the aggregate fakers, and with
[`SharedKernel.Application.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Application/SharedKernel.Application.Testing/README.md)
to run the handler through `TransactionBehavior`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference it from production code | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build |
| Test row-level security as a superuser | Connect with `RuntimeConnectionString` / the generated configuration | A superuser bypasses every RLS policy |
| Test concurrency or ETags on `FakeRepository` | Use `PostgresTestDatabase` | The fake ignores `EntityVersion` |
| Mutate an aggregate loaded outside the transaction and expect a rollback to undo it | Load inside the operation | Rollback restores membership, not object state |
| `new FakeUnitOfWork()` next to `new FakeRepository<,>(…)` and expect linked rollback | Use `AddFakeUnitOfWork()` and `AddFakeRepository<,>()` | Only the helpers link them |
| Fake Dapper sessions (`IDbSessionFactory`) | Test Dapper code against PostgreSQL | A session is a real connection plus transaction |
| Register `FakeCrossTenantScope` against a real database | Keep the scope `AddSharedKernelPostgres` registers | EF Core, Dapper and encryption consult the platform's own scope |
| Start a container per test | One `PostgresTestServer` per class or collection, one database per test | Container start dominates test time |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
