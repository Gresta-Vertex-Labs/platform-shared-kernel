---
proven-by:
  - samples/Shop/Ordering/Shop.Ordering.Domain/Order.cs
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderingPorts.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Persistence/OrderingDbContext.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Fulfilment/OrderFulfilment.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/appsettings.json
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Program.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Security/InventorySecurity.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Reconciliation/ReconcileStock.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Stock/StockStore.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Images/ProductImages.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/SearchQueries.cs
unproven:
  - ITenantCacheService: the reference platform keeps its only tenant-keyed cache in a Redis hash whose key it builds from the TenantId
---
# Add multi-tenancy

**Use when:** a service holds data for several tenants and must resolve the tenant of each request and keep
every tenant's rows, cache entries, files and messages apart.
**Not when:** you only need to store aggregates (add-persistence.md) or authenticate the caller
(add-authentication.md); the tenant claim itself comes from the authentication scheme.

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Domain | `SharedKernel.Domain` | `TenantedAggregateRoot<TId>`, `TenantedAuditableAggregateRoot<TId>`, `IHasTenant`; brings `TenantId` |
| Application | `SharedKernel.Execution` (through `SharedKernel.Application`) | `IRequestContext.TenantId`, `TenantScope` |
| Application | `SharedKernel.Persistence.Abstractions` | `ICrossTenantScope` for deliberate cross-tenant work |
| Infrastructure | `SharedKernel.Persistence.EfCore` | `TenantedDbContext`, `UseMultiTenancy(rowLevelSecurity: true)`, `IsTenantShared()` |
| Infrastructure | `SharedKernel.Persistence.Dapper` | Sessions bound to the caller's tenant |
| Api/Worker | `SharedKernel.ServiceDefaults.Security` | `AddSharedKernelRequestContext()`, `UseSharedKernelRequestContext()` |
| Api/Worker | `SharedKernel.MultiTenancy` | `AddSharedKernelMultiTenancy()`, `TenantResolutionMiddleware`, `ITenantResolutionStrategy` |

## Steps

1. **Domain.** A tenant-owned aggregate derives a `Tenanted…` base and receives its `TenantId` at creation.
   A child entity implements `IHasTenant`; the kernel stamps its tenant from the root.

```csharp
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Entities;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;

public sealed class OrderLine : Entity<Guid>, IHasTenant
{
    public TenantId TenantId { get; private set; }
}

public sealed class Order : TenantedAuditableAggregateRoot<OrderId>
{
    private Order(OrderId id, TenantId tenantId, IClock clock)
        : base(id, tenantId, clock) { }
}
```

2. **Application.** The tenant is `IRequestContext.TenantId`, a `TenantId?`. A use case that needs one
   fails closed when it is missing; it never takes a tenant from the request body.

```csharp
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

public static class CallerTenant
{
    public static Result<TenantId> Of(IRequestContext caller) =>
        caller.TenantId is { } tenant
            ? Result<TenantId>.Success(tenant)
            : Result<TenantId>.Failure(
                Error.Forbidden("ordering.tenant_required", "Ordering needs a caller with a tenant."));
}
```

   APIs that act for a tenant or globally take a `TenantScope`: `TenantScope.For(tenant)`, or
   `TenantScope.FromNullable(caller.TenantId)` when a missing tenant means global.

   Deliberate work across tenants (a reconciliation job, a report) enters a named scope; the reason is
   recorded with the caller.

```csharp
using SharedKernel.Persistence.Abstractions.Context;

using (crossTenant.Enter("inventory reconciliation"))
{
    await using var session = await sessions.OpenAsync(ct);
    // … read every tenant's rows, then commit
}
```

3. **Infrastructure.** Derive `TenantedDbContext` and turn on row-level security; the tenant filter, the
   tenant stamp and the database policy all follow the caller. Tables that span tenants are marked shared.

```csharp
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Context;

public sealed class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    PersistenceContextDependencies dependencies
) : TenantedDbContext(options, dependencies)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TotpEnrollment>().IsTenantShared();
    }
}

builder.AddSharedKernelPostgres<OrderingDbContext>(
    "ordering",
    p => p.UseMultiTenancy(rowLevelSecurity: true).MigrateOnStartup()
);
```

   Dapper sessions bind the caller's tenant for row-level security too; `session.RequireTenantId()` gives it to
   an `INSERT` that must write the column.

4. **Api/Worker.** The request context comes first, then tenant resolution. The middleware runs after
   authentication, so the signed claim is available, and before authorization.

```csharp
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Security;

builder.Services.AddSharedKernelRequestContext();
builder.Services.AddSharedKernelMultiTenancy(o =>
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName).Bind(o)
);

var app = builder.Build();
app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi(pipeline =>
    pipeline.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>())
);
```

   A service with its own rule adds an `ITenantResolutionStrategy` (a `StrategyName` and
   `TryResolveAsync(HttpContext, CancellationToken)` returning `TenantId?`), registered with
   `builder.Services.AddScoped<ITenantResolutionStrategy, ServiceHeaderTenantResolutionStrategy>()` and named in
   the strategy order. The reference platform uses one to trust the tenant header only from a caller
   authenticated by client certificate.

5. **Configuration.** `SharedKernel:MultiTenancy` (the strategy order). Keep the signed claim first; the keys
   are in the `SharedKernel.MultiTenancy` README.

## Tenant scope in the other capabilities

- Caching: `ITenantCacheService` keeps entries per tenant (add-caching.md).
- Files: `ITenantFileStorage.ForTenant(tenant)` (add-file-storage.md).
- Search and vectors: every read takes a `TenantScope` (add-search.md, add-ai.md).
- Idempotency keys are reserved per tenant and caller (add-idempotency.md).
- Messages carry the publisher's tenant with `WithInboundRequestContext()` (add-messaging.md).
- Workflows start under a `TenantScope` (add-long-running-workflow.md).

## Test it

- Unit: `TestRequestContext.ForTenant(tenant).WithPermissions(…)` (`SharedKernel.Testing`) as the
  `IRequestContext`; assert that a context without a tenant fails with `Forbidden`.
- Resolution: `SharedKernel.ServiceDefaults.Testing` has an in-memory `ITenantCatalog` and a scriptable
  `ITenantResolutionStrategy`.
- Row-level security: integration tests on `PostgresTestServer` (`SharedKernel.Persistence.Testing`), which
  runs the production role split. See write-tests.md.

## Check

- `SK0201`: a `TenantedDbContext.OnModelCreating` override without the base call drops the tenant filter.
- `SK0202`: a parameterless `IgnoreQueryFilters()` in service code; use `ICrossTenantScope` instead.
- `SK0031`: `IHttpContextAccessor`, `HttpContext` or `ClaimsPrincipal` injected to read the tenant; read
  `IRequestContext`.
- With row-level security on, the database privilege check fails the host at startup when the application
  role can bypass the policy.

## Read next

`SharedKernel.MultiTenancy`, `SharedKernel.ServiceDefaults.Security`, `SharedKernel.Execution`,
`SharedKernel.Persistence.EfCore`, `SharedKernel.Persistence.Npgsql`, `SharedKernel.Domain`
