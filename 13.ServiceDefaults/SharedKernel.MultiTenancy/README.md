# SharedKernel.MultiTenancy

Concrete multi-tenant resolution strategies for Platform.SharedKernel microservices. Resolves the ambient tenant identity for each request and exposes it through `ITenantProvider`.

## Included

**`TenantResolutionMiddleware`** — runs the configured strategies in order; the first one returning a non-null tenant wins.

| Strategy | Resolves from |
|---|---|
| `ClaimTenantResolutionStrategy` | A signature-verified JWT tenant claim |
| `HeaderTenantResolutionStrategy` | The `X-Tenant-Id` request header |
| `DatabaseTenantResolutionStrategy` | A tenant-directory lookup (host/domain → tenant) |

**`AmbientTenantProvider`** — the resolved identity, injected as `ITenantProvider`.

**`ITenantStatusValidator`** — optional seam. When registered, it is consulted after resolution so a suspended or offboarded tenant is rejected even if it presents an otherwise valid claim or header. Bridge it to your own tenant directory at the composition root.

**`ITenantResolutionStrategy`** — implement this to add your own strategy.

**`TenantResolutionOptions`** — bound from configuration section `SharedKernel:MultiTenancy`, validated at startup by `TenantResolutionOptionsValidator` (a typo in `StrategyOrder` fails the host rather than silently resolving nothing).

## Quick Start

```csharp
// Register (Program.cs)
builder.Services.AddSharedKernelMultiTenancy(builder.Configuration);

app.UseMiddleware<TenantResolutionMiddleware>();

// Consume
public class OrderService(ITenantProvider tenants)
{
    public Guid CurrentTenant => tenants.TenantId;
}
```

```json
{
  "SharedKernel": {
    "MultiTenancy": {
      "StrategyOrder": [ "Claim", "Header", "Database" ]
    }
  }
}
```

## Tenant catalog (read-only metadata lookup)

**`ITenantCatalog`** — read-only tenant metadata lookup by id or by a resolution-strategy-supplied raw value (host, claim value, header value). Ships lookup only — tenant provisioning/onboarding is a consuming service's own concern.

**`DatabaseTenantCatalog`** — queries a consumer-owned tenant directory table via `IDbConnectionFactory`, reusing `DatabaseTenantResolutionStrategy`'s exact parameterized-query pattern.

**`CachedTenantCatalog`** — a short (30s default), bounded-TTL in-memory decorator over any `ITenantCatalog`. Call `InvalidateTenantAsync` immediately after changing a tenant's status — do not rely on the TTL alone for a suspended/offboarded tenant.

**`CatalogTenantStatusValidator`** — the first real default implementation of `ITenantStatusValidator` above, backed by `ITenantCatalog`. Fails closed: a tenant absent from the catalog is treated identically to `Suspended`/`Offboarded`.

Worked composition example:

```csharp
// Program.cs
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

builder.Services.AddScoped<ITenantCatalog>(sp =>
{
    var database = new DatabaseTenantCatalog(sp.GetRequiredService<IDbConnectionFactory>());
    return new CachedTenantCatalog(database); // 30s default TTL
});

builder.Services.AddScoped<ITenantStatusValidator, CatalogTenantStatusValidator>();
```

Opt-in cross-instance cache invalidation (so a status change on one replica evicts every replica's cached copy, not just the one that made the change):

```csharp
builder.Services.AddScoped<ITenantCatalog>(sp =>
{
    var database = new DatabaseTenantCatalog(sp.GetRequiredService<IDbConnectionFactory>());
    var cached = new CachedTenantCatalog(database)
        .WithCrossInstanceInvalidation(sp.GetRequiredService<ICacheInvalidationBus>());
    return cached;
});

// Elsewhere, wherever you already wire your own Redis Pub/Sub subscription
// (ICacheInvalidationBus itself is publish-only — this is the consumer-side receive half):
await channelService.SubscribeAsync("your-tenant-invalidation-channel", async message =>
{
    var tenantId = ParseTenantId(message);
    cachedTenantCatalog.HandleCrossInstanceInvalidationSignal(tenantId);
});
```

## Security note — strategy order matters

The default order is **`Claim` → `Header` → `Database`**, and it is deliberate.

A JWT tenant claim is signature-verified; the `X-Tenant-Id` header is caller-supplied and trivially forged. Placing `Header` ahead of `Claim` lets any caller override a verified identity with an arbitrary one — a cross-tenant impersonation vector. This ordering is locked by an architecture test in `00.Governance` so it cannot silently regress.

A service with no tenant directory simply omits `"Database"` from the order — no code change required.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [13.ServiceDefaults README](../README.md) for the full host-composition layer.
