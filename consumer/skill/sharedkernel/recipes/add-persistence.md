---
proven-by:
  - samples/Shop/Ordering/Shop.Ordering.Domain/Order.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Persistence/OrderingDbContext.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderCommands.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Program.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Stock/StockStore.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Stock/StockCommands.cs
  - samples/Shop/Billing/Shop.Billing.Api/Program.cs
  - samples/Shop/Billing/Shop.Billing.Api/Security/BillingKeyVault.cs
unproven:
  - Calling IUnitOfWork.ExecuteInTransactionAsync directly: every command of the reference platform runs in the transaction app.WithTransactions() opens, so no service code calls it
  - Field-encryption keys held in Azure Key Vault: the reference platform reads its field-encryption keys from configuration and uses Key Vault keys only for signing and envelope encryption
---
# Add persistence

**Use when:** a service stores its aggregates in PostgreSQL with EF Core, runs hand-written SQL with Dapper,
needs one transaction per command, a tamper-evident audit trail, or encrypted columns.
**Not when:** the question is how tenants are isolated (row-level security, cross-tenant work): open
add-multi-tenancy.md. Deduplicating commands in PostgreSQL is add-idempotency.md.

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Domain | `SharedKernel.Domain` | Aggregate bases and strongly-typed ids |
| Application | `SharedKernel.Persistence.Abstractions` | `IRepository<T, TId>`, `IReadRepository<T, TId>` |
| Application | `SharedKernel.Application` | `IAuditableRequest<TResponse>` on audited commands |
| Infrastructure | `SharedKernel.Persistence.EfCore` | `TenantedDbContext`/`SharedKernelDbContext`, `AddSharedKernelPostgres<TContext>` |
| Infrastructure | `SharedKernel.Persistence.Npgsql` | Connections, TLS, database roles (brought by EF Core and Dapper; reference it to add a data source) |
| Infrastructure | `SharedKernel.Persistence.Dapper` | `IDbSessionFactory` for hand-written SQL |
| Infrastructure | `SharedKernel.Persistence.EfCore.Auditing` | `UseAuditTrail()` and the audit ledger |
| Infrastructure | `SharedKernel.Persistence.EfCore.Encryption` | `UseFieldEncryption()` and `.Encrypt("purpose")` |
| Infrastructure | `SharedKernel.Cryptography` | The HMAC signer the audit ledger needs, the keys of field encryption |
| Infrastructure | `SharedKernel.Cryptography.KeyVault.Azure` | Optional: signing and envelope-encryption keys in Azure Key Vault |
| Api/Worker | `SharedKernel.Application.Pipeline` | `app.WithTransactions()`, `app.WithAuditing()` |
| Api/Worker | `SharedKernel.ServiceDefaults.Persistence` | `AddDatabaseReadinessCheck<TContext>()` |

## Steps

1. **Domain.** An aggregate derives a kernel base; a tenant-scoped one derives a `Tenanted…` base
   (add-multi-tenancy.md has the tenant rules).

```csharp
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
public sealed class Order : TenantedAuditableAggregateRoot<OrderId> { /* … */ }
```

2. **Application.** Handlers inject the repository contract, never the `DbContext`. `IRepository<,>` tracks
   changes for commands; `IReadRepository<,>` is untracked for queries. An audited command implements
   `IAuditableRequest<TResponse>`.

```csharp
using System.Globalization;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

public sealed record CancelOrderCommand(Guid OrderId) : ICommand, IAuditableRequest<Result>
{
    public string Action => "order.cancelled";
    public string ResourceType => nameof(Order);
    public string ResourceId => OrderId.ToString("D", CultureInfo.InvariantCulture);
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result response) => null;
}

public sealed class GetOrderHandler(IReadRepository<Order, OrderId> orders)
    : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(new OrderId(query.OrderId), ct);
        return order is null
            ? Result<OrderDto>.Failure(
                Error.NotFound("ordering.order.not_found", $"Order {query.OrderId} was not found."))
            : Result<OrderDto>.Success(new OrderDto(order.Id.Value, order.Status.ToString() /* , … */));
    }
}
```

   A handler does not save or commit: `app.WithTransactions()` commits the command when it succeeds.

3. **Infrastructure: the context.** Derive `TenantedDbContext` (tenant data) or `SharedKernelDbContext`, keep
   the base call in `OnModelCreating`, and mark encrypted columns in the entity configuration. Put the
   persistence capabilities in one method so the design-time factory (used by `dotnet ef`) applies the same.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;

public sealed class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    PersistenceContextDependencies dependencies
) : TenantedDbContext(options, dependencies)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        base.OnModelCreating(modelBuilder);
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder) =>
        builder
            .Property(o => o.CustomerEmail)
            .HasMaxLength(320)
            .Encrypt("ordering.order.customer_email")
            .WithBlindIndex(BlindIndexNormalization.Trim | BlindIndexNormalization.CaseFold);
}

public sealed class OrderingDbContextFactory()
    : PostgresDesignTimeDbContextFactory<OrderingDbContext>(OrderingDatabase.ConnectionName)
{
    protected override OrderingDbContext Create(
        DbContextOptions<OrderingDbContext> options,
        PersistenceContextDependencies dependencies
    ) => new(options, dependencies);

    protected override void ConfigurePersistence(
        EfCorePersistenceBuilder<OrderingDbContext> persistence
    ) => OrderingDatabase.Configure(persistence);
}

public static class OrderingDatabase
{
    public const string ConnectionName = "ordering";

    public static EfCorePersistenceBuilder<OrderingDbContext> Configure(
        EfCorePersistenceBuilder<OrderingDbContext> persistence
    ) =>
        persistence
            .UseMultiTenancy(rowLevelSecurity: true)
            .UseAuditTrail()
            .UseFieldEncryption(keys => keys.FromConfiguration());
}
```

   The tenant filter and row-level security read the caller's tenant claim: wire authentication (add-multi-tenancy.md).
4. **Infrastructure: registration.** One call registers the context, the open-generic repositories and
   `IUnitOfWork`. The audit ledger signs with the HMAC signer of `SharedKernel.Cryptography`, so register
   cryptography too.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Persistence;

builder.AddSharedKernelPostgres<OrderingDbContext>(
    OrderingDatabase.ConnectionName,
    p => OrderingDatabase.Configure(p).UseServiceName("ordering-api").MigrateOnStartup()
);
builder.Services.AddSharedKernelCryptography(builder.Configuration);
```

   **Hand-written SQL.** Register a data source and Dapper; a handler opens an `IDbSession`, which joins the
   caller's tenant and the right database role. SQL is a constant string with parameters.

```csharp
using Dapper;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Dapper.Sessions;

builder.Services.AddSharedKernelNpgsql(builder.Configuration, "inventory");
builder.Services.AddSharedKernelDapper(builder.Configuration);

await using var session = await sessions.OpenAsync(ct);   // OpenReadOnlyAsync for reads
await session.Connection.ExecuteAsync(
    session.Command(
        "UPDATE stock_items SET reserved = reserved + @delta WHERE sku = @sku",
        new { sku, delta },
        ct
    )
);
await session.CommitAsync(ct);
```

   **Keys in Azure Key Vault (optional).** The cryptography builder takes the Key Vault providers; the
   service registers the `TokenCredential` and the Key Vault client options in the container first.

```csharp
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.KeyVault.Azure;

builder
    .Services.AddSharedKernelCryptography(builder.Configuration)
    .AddAzureKeyVaultSigning(builder.Configuration)
    .AddAzureKeyVaultEncryption(builder.Configuration)
    .AddAsymmetricSigning()
    .AddEnvelopeEncryption();
```

5. **Api/Worker.** Opt into the transaction and audit behaviors, and add the database readiness check.
   The behaviors' seams are checked when the host starts.

```csharp
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.Services.AddSharedKernelApplication(
    typeof(PlaceOrderCommand).Assembly,
    app => app.UseMediatR().WithTransactions().WithAuditing()
);
builder
    .Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<OrderingDbContext>()
    .AddSharedKernelReadiness();
```

6. **Configuration.** `ConnectionStrings:{name}`, `SharedKernel:Persistence` (with `:Npgsql`, `:Dapper`,
   `:Auditing`, `:Encryption`), `SharedKernel:Cryptography` (with `:KeyVault:Azure:Signing` and
   `:KeyVault:Azure:Encryption`). The keys are in each package README.

## Test it

- Unit: `services.AddFakeRepository<Order, OrderId>()` and `services.AddFakeAuditTrailWriter()`
  (`SharedKernel.Persistence.Testing`) under `ApplicationPipelineTestHarness` (`SharedKernel.Application.Testing`)
  with `app.WithTransactions().WithAuditing()`; assert on `Items` and `Recorded`. The fakes never roll back.
- Integration: `PostgresTestServer` (`SharedKernel.Persistence.Testing`), real PostgreSQL with RLS on: write-tests.md.

## Check

- `SK0201`: a `TenantedDbContext.OnModelCreating` override that skips the base call.
- `SK0202`: a parameterless `IgnoreQueryFilters()` in service code.
- `SK0042`: interpolated or non-constant SQL passed to an `IDbSession` or Dapper method.
- `SK0030`: a `Result` from a repository or publish call whose outcome nobody reads.
- The host fails at startup when `WithTransactions()` finds no `IUnitOfWork` or `WithAuditing()` no
  `IAuditTrailWriter`; `AddDatabaseReadinessCheck<TContext>()` keeps readiness unhealthy until migrations end.

## Read next

`SharedKernel.Persistence.EfCore`, `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.Npgsql`,
`SharedKernel.Persistence.Dapper`, `SharedKernel.Persistence.EfCore.Auditing`,
`SharedKernel.Persistence.EfCore.Encryption`, `SharedKernel.Cryptography`,
`SharedKernel.Cryptography.KeyVault.Azure`, `SharedKernel.ServiceDefaults.Persistence`
