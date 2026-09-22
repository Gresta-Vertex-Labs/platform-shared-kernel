# SharedKernel.Persistence.Testing

Test helpers for services built on the SharedKernel persistence packages (`SharedKernel.Persistence.EfCore`,
`.Npgsql`, `.Dapper`, `.EfCore.Auditing`, `.EfCore.Encryption`). **Reference it from test projects only** — the
platform's architecture tests fail the build of any production project that references it.

```shell
dotnet add MyService.Tests package SharedKernel.Persistence.Testing
```

Everything lives in one namespace: `using SharedKernel.Persistence.Testing;`.

## Unit tests: in-memory fakes

| Type | Stands in for | Notes |
|---|---|---|
| `FakeRepository<TAggregate, TId>` | `IRepository<,>`, `IReadRepository<,>` | Evaluates specifications in memory with the production rules (soft-deleted rows hidden, ordering before paging, call-site paging, keyset cursors encoded exactly as production does). Writes apply immediately; `Seed`, `Items`, `SimulateFailure`. |
| `FakeUnitOfWork` | `IUnitOfWork` | `ExecuteInTransactionAsync` runs the operation, saves, runs every `OnBeforeCommit` callback, commits; an exception or a failed `Result` rolls back; a joined call that fails marks the transaction rollback-only. A rollback puts every `FakeRepository` registered with `AddFakeRepository` back as it was when the transaction started (which aggregates are stored, not in-place changes to an aggregate object). `TransientFailures = n` replays the operation n times from that state, like the retrying execution strategy — proof that a handler is re-runnable. Counters: `TransactionCount`, `CommitCount`, `RollbackCount`, `SaveChangesCallCount`. |
| `FakeAuditTrailWriter` | `IAuditTrailWriter` | Records every `AuditEntry`; `ShouldHaveAudited(action, resourceType, resourceId)`. |
| `FakeCrossTenantScope` | `ICrossTenantScope` | Records `Enter` reasons, nests, `DenyWith` makes `Enter` throw, `ShouldHaveEntered(reason)`. |
| `TestRequestContext` | `IRequestContext` | `ForUser`, `ForTenant`, `Service`, `System`, `Anonymous`, then `WithTenant`, `WithPermissions`, `WithSession`, `WithImpersonator`. Mutable: change `TenantId` to act as another caller. Grants no permission unless told. |
| `FakeDbConnectionFactory` | `IDbConnectionFactory` | Hands out the `DbConnection` your delegate builds (for example an NSubstitute substitute). |

```csharp
var services = new ServiceCollection();
var orders = services.AddFakeRepository<Order, OrderId>([Order.Create(...)]);
var unitOfWork = services.AddFakeUnitOfWork();
var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(tenantId).WithPermissions("orders.write"));
var audit = services.AddFakeAuditTrailWriter();
services.AddFakeCrossTenantScope();
// ... register the handler under test, resolve it, run it ...

unitOfWork.CommitCount.Should().Be(1);
orders.Items.Should().ContainKey(orderId);
audit.ShouldHaveAudited("order.place", "Order", orderId.ToString());
```

Each `Add*` call **replaces** any registration of the same contract, so the fakes also work on top of the service's
real composition root (a `WebApplicationFactory`). Every call returns the instance for seeding and assertions.

Dapper sessions (`IDbSessionFactory`) have no in-memory fake: a session is a real `DbConnection` plus transaction.
Test Dapper code against PostgreSQL, below.

`FakeCrossTenantScope` is for unit tests over the fakes. The real EF Core contexts, Dapper sessions and encryption
maintenance consult the platform's own scope; against a real database keep the registration `AddSharedKernelPostgres`
makes and enter it as production code does.

## Integration tests: PostgreSQL with the production role split

`PostgresTestServer` starts a Testcontainers PostgreSQL (or uses a running server:
`PostgresTestServer.FromExistingServer(adminConnectionString)`) and provisions the canonical roles of the
`SharedKernel.Persistence.Npgsql` README once:

| Role (`PostgresTestRoles`) | Used for |
|---|---|
| `app_migrator` | owns every table, runs migrations (`MigrationConnectionString`) |
| `app_runtime` | the application (`ConnectionStrings:{name}`): no superuser, no BYPASSRLS, owns nothing |
| `app_cross_tenant` | cross-tenant work (`RowLevelSecurity:CrossTenantConnectionString`), BYPASSRLS |
| `app_audit_sealer` | the audit sealer's own data source |

`CreateDatabaseAsync()` creates a database owned by `app_migrator` with the role script's grants and default
privileges; disposing it drops it. `ConfigurationFor(name)` / `BuildConfiguration(name)` return exactly what
`AddSharedKernelPostgres` reads.

```csharp
public sealed class PostgresFixture : IAsyncLifetime            // xUnit; one container per test class/collection
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

Schema and migration helpers, all run as `app_migrator`:

| Call | Does |
|---|---|
| `CreateSchemaAsync(context)` | the model's create script — `EnsureCreated` with production ownership (the runtime role cannot create tables) |
| `EnableRowLevelSecurityAsync(context, crossTenantRole?)` | `EnableTenantRowLevelSecurityForModel` over the design-time model |
| `CreateAuditLedgerAsync(separateAuditSealer)` | `CreateAuditLedgerTable(runtimeRole:, sealerRole:)` plus the cross-tenant REVOKE of the role script |
| `CreateTenantEncryptionKeyTableAsync()` | `CreateTenantEncryptionKeyTable()` plus the REVOKE that keeps tombstones undeletable |
| `ApplyMigrationAsync(m => ...)` | any `MigrationBuilder` operations, through EF Core's SQL generator |
| `ExecuteAsMigratorAsync(sql)` / `ExecuteAsAdminAsync(sql)` | raw SQL (`CREATE EXTENSION vector` needs the admin) |

With real migrations, call `.MigrateOnStartup()` instead: the generated configuration already points
`MigrationConnectionString` at `app_migrator`. For a separate audit sealer pass `separateAuditSealer: true` to
`BuildConfiguration` and register its data source as in production:
`services.AddSharedKernelNpgsql(configuration.GetSection("SharedKernel:Persistence:audit-sealer"), "audit-sealer")`.

Docker must be running for `PostgresTestServer.StartAsync()`.
