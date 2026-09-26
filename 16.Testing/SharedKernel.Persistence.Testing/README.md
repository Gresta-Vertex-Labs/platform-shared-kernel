# SharedKernel.Persistence.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Testcontainers](https://img.shields.io/badge/Testcontainers-PostgreSQL-2496ED?logo=docker&logoColor=white)](https://dotnet.testcontainers.org/)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)

> **Test services built on the SharedKernel persistence packages at both speeds: in-memory fakes that follow the
> production rules for unit tests, and a real PostgreSQL with the production role split for integration tests.**

A fake that behaves differently from production passes tests that production then fails: a repository that ignores
soft delete, a unit of work that never replays, a PostgreSQL connected as a superuser that silently bypasses every
row-level-security policy. The fakes here evaluate specifications, paging and cursors with the production rules and
roll back like the real unit of work; the PostgreSQL fixture creates the same four roles production uses, so
isolation is actually exercised.

| ⚡ Unit tests | 🐘 Integration tests | 🔁 Retry-proofing | 🧰 Framework-free |
| --- | --- | --- | --- |
| `FakeRepository`, `FakeUnitOfWork`, `FakeAuditTrailWriter`, `FakeCrossTenantScope` | `PostgresTestServer` + `PostgresTestDatabase` | `TransientFailures = n` replays a handler like the retrying strategy | No xUnit/NUnit/MSTest dependency |
| Production specification, paging and cursor rules | Roles `app_migrator`, `app_runtime`, `app_cross_tenant`, `app_audit_sealer` | Rollbacks restore the fake repositories | `Add*` helpers replace real registrations |
| `TestRequestContext` for any caller | Schema, RLS, ledger and key-table helpers | Proves a handler is re-runnable | Works on top of `WebApplicationFactory` |

## Contents

- [Install](#install)
- [Unit tests: in-memory fakes](#unit-tests-in-memory-fakes)
- [Integration tests: PostgreSQL with the production role split](#integration-tests-postgresql-with-the-production-role-split)
- [End-to-end tests over HTTP](#end-to-end-tests-over-http)
- [AI quick reference](#ai-quick-reference)

## Install

```shell
dotnet add MyService.Tests package SharedKernel.Persistence.Testing
```

> **Reference it from test projects only.** The platform's architecture tests fail the build of any production
> project that references it.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Docker | Only for `PostgresTestServer.StartAsync()` (or use `FromExistingServer`) |
| Namespace | `SharedKernel.Persistence.Testing` — everything except `TestRequestContext` (`SharedKernel.Testing.Execution`) |
| Tier | Testing (packable; references `SharedKernel.Testing`, the persistence packages and `Testcontainers.PostgreSql`) |
| Version | From the consumer's single `SharedKernelVersion` |

## Unit tests: in-memory fakes

| Type | Stands in for | Notes |
| --- | --- | --- |
| `FakeRepository<TAggregate, TId>` | `IRepository<,>`, `IReadRepository<,>` | Specifications evaluated in memory with the production rules (soft-deleted rows hidden, ordering before paging, call-site paging, keyset cursors encoded exactly as production does). Writes apply immediately; `Seed`, `Items`, `SimulateFailure`, `Reset` |
| `FakeUnitOfWork` | `IUnitOfWork` | Runs the operation, saves, runs every `OnBeforeCommit` callback, commits. An exception or a failed `Result` rolls back; a failing joined call makes the transaction rollback-only. A rollback puts every `FakeRepository` registered next to it back as it was when the transaction started. `TransientFailures = n` replays the operation n times from that state. Counters: `TransactionCount`, `CommitCount`, `RollbackCount`, `SaveChangesCallCount` |
| `FakeAuditTrailWriter` | `IAuditTrailWriter` | Records every `AuditEntry`; `ShouldHaveAudited(action, resourceType, resourceId)` |
| `FakeCrossTenantScope` | `ICrossTenantScope` | Records `Enter` reasons, nests; `DenyWith` makes `Enter` throw; `ShouldHaveEntered(reason)` |
| `TestRequestContext` (namespace `SharedKernel.Testing.Execution`, from the `SharedKernel.Testing` package this one depends on) | `IRequestContext` | `ForUser`, `ForTenant`, `Service`, `System`, `Anonymous`, then `WithTenant`, `WithPermissions`, `WithSession`, `WithImpersonator`. Mutable: change `TenantId` to act as another caller. Grants no permission unless told |
| `FakeDbConnectionFactory` | `IDbConnectionFactory` | Hands out the `DbConnection` your delegate builds (for example an NSubstitute substitute) |

```csharp
var services = new ServiceCollection();
var orders = services.AddFakeRepository<Order, OrderId>([Order.Create(...)]);
var unitOfWork = services.AddFakeUnitOfWork();
var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(tenantId).WithPermissions("orders.write"));
var audit = services.AddFakeAuditTrailWriter();
services.AddFakeCrossTenantScope();
services.AddScoped<PlaceOrderHandler>();
await using var provider = services.BuildServiceProvider();

unitOfWork.TransientFailures = 2;          // the handler runs three times, as under the retrying strategy
var result = await provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
    ct => provider.GetRequiredService<PlaceOrderHandler>().Handle(command, ct));

result.IsSuccess.Should().BeTrue();
unitOfWork.CommitCount.Should().Be(1);
orders.Items.Should().ContainKey(orderId);                      // once — the failed attempts were rolled back
audit.ShouldHaveAudited("order.place", "Order", orderId.ToString());
```

Each `Add*` call **replaces** any registration of the same contract, so the fakes also work on top of the service's
real composition root. Every call returns the instance for seeding and assertions. A rollback restores **which**
aggregates a fake repository holds, not changes made to an aggregate object in place — load aggregates inside the
operation, as production code must anyway.

Dapper sessions (`IDbSessionFactory`) have no in-memory fake: a session is a real connection plus transaction. Test
Dapper code against PostgreSQL, below. `FakeCrossTenantScope` is for unit tests over the fakes; against a real
database keep the registration `AddSharedKernelPostgres` makes and enter it as production code does.

## Integration tests: PostgreSQL with the production role split

`PostgresTestServer` starts a Testcontainers PostgreSQL (or uses a running server:
`PostgresTestServer.FromExistingServer(adminConnectionString)`) and provisions the canonical roles of the
[`SharedKernel.Persistence.Npgsql`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence/SharedKernel.Persistence.Npgsql#roles-the-one-canonical-script)
role script once:

| Role (`PostgresTestRoles`) | Used for |
| --- | --- |
| `app_migrator` | owns every table, runs migrations (`MigrationConnectionString`) |
| `app_runtime` | the application (`ConnectionStrings:{name}`): no superuser, no `BYPASSRLS`, owns nothing |
| `app_cross_tenant` | cross-tenant work (`RowLevelSecurity:CrossTenantConnectionString`), `BYPASSRLS` |
| `app_audit_sealer` | the audit sealer's own data source |

`CreateDatabaseAsync()` creates a database owned by `app_migrator` with the role script's grants and default
privileges; disposing it drops it. `ConfigurationFor(name)` / `BuildConfiguration(name)` return exactly what
`AddSharedKernelPostgres` reads.

```csharp
public sealed class PostgresFixture : IAsyncLifetime            // xUnit; one container per test class or collection
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
        // ... write as tenant A, switch caller.TenantId, assert ...
    }
}
```

| Helper (all run as `app_migrator`) | Does |
| --- | --- |
| `CreateSchemaAsync(context)` | The model's create script — `EnsureCreated` with production ownership (the runtime role cannot create tables) |
| `EnableRowLevelSecurityAsync(context, crossTenantRole?)` | `EnableTenantRowLevelSecurityForModel` over the model |
| `CreateAuditLedgerAsync(separateAuditSealer)` | `CreateAuditLedgerTable(runtimeRole:, sealerRole:)` plus the cross-tenant REVOKE of the role script |
| `CreateTenantEncryptionKeyTableAsync()` | `CreateTenantEncryptionKeyTable()` plus the REVOKE that keeps tombstones undeletable |
| `ApplyMigrationAsync(m => ...)` | Any `MigrationBuilder` operations, through EF Core's SQL generator |
| `ExecuteAsMigratorAsync(sql)` / `ExecuteAsAdminAsync(sql)` | Raw SQL (`CREATE EXTENSION vector` needs the admin) |

With real migrations, call `.MigrateOnStartup()` instead: the generated configuration already points
`MigrationConnectionString` at `app_migrator`. For a separate audit sealer pass `separateAuditSealer: true` to
`BuildConfiguration` and register its data source as in production:
`services.AddSharedKernelNpgsql(configuration.GetSection("SharedKernel:Persistence:audit-sealer"), "audit-sealer")`.

## End-to-end tests over HTTP

The same fixture drives a whole service through `WebApplicationFactory`, in the Production environment so every
startup check fails closed:

```csharp
var server = await PostgresTestServer.StartAsync("postgres:17-alpine");
var database = await server.CreateDatabaseAsync();
var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
{
    host.UseEnvironment("Production");
    host.ConfigureAppConfiguration((_, config) => config
        .AddInMemoryCollection(database.ConfigurationFor("billing", separateAuditSealer: true))
        .AddInMemoryCollection(testKeys));
});
// real migrations run at startup as app_migrator; poll /health/ready, then call the API
```

The [BillingApi sample](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/samples/BillingApi)'s
`BillingApi.Tests` is a complete example: tenant isolation through EF Core, blind indexes and hand-written SQL, ETags,
Dapper and EF Core in one transaction, the sealed audit chain and crypto-shredding — all over HTTP.

## AI quick reference

```text
SCOPE        Test projects only (architecture tests fail production references). using SharedKernel.Persistence.Testing;
             using SharedKernel.Testing.Execution; (TestRequestContext)
UNIT         var repo = services.AddFakeRepository<T, TId>(seed); var uow = services.AddFakeUnitOfWork();
             services.AddTestRequestContext(TestRequestContext.ForTenant(id).WithPermissions("p"));
             services.AddFakeAuditTrailWriter(); services.AddFakeCrossTenantScope(). Resolve IUnitOfWork from DI.
RETRY        uow.TransientFailures = 2 -> the operation runs 3 times; fake repositories are restored between runs.
ASSERT       uow.CommitCount / RollbackCount; repo.Items; audit.ShouldHaveAudited(action, type, id);
             scope.ShouldHaveEntered(reason).
POSTGRES     await PostgresTestServer.StartAsync([image]) once per class/collection; per test
             await server.CreateDatabaseAsync(); database.BuildConfiguration("name"[, extra, separateAuditSealer]).
SCHEMA       database.CreateSchemaAsync(ctx); EnableRowLevelSecurityAsync(ctx); CreateAuditLedgerAsync();
             CreateTenantEncryptionKeyTableAsync(); or MigrateOnStartup() with real migrations.
FORBIDDEN    Testing row-level security as a superuser (it bypasses RLS); in-memory fakes for Dapper sessions;
             referencing this package from production code.
```

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · start at the
[persistence overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence).
