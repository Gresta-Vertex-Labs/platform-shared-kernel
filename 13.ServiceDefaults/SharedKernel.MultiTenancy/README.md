# SharedKernel.MultiTenancy

Concrete multi-tenant resolution strategies for Platform.SharedKernel microservices. Resolves the ambient tenant identity for each request and exposes it through `ITenantProvider`.

## Included

**`TenantResolutionMiddleware`** — runs the configured strategies in order; the first one returning a non-null tenant wins.

| Strategy | Resolves from |
|---|---|
| `ClaimTenantResolutionStrategy` | The authenticated credential's tenant, through the authentication package's `IUserContextMapper` (the claim type is that package's setting) |
| `HeaderTenantResolutionStrategy` | The `X-Tenant-Id` request header |
| `DatabaseTenantResolutionStrategy` | A tenant-directory lookup (host/domain → tenant) |

**`AmbientTenantProvider`** — the resolved identity, injected as `ITenantProvider`.

**`ITenantStatusValidator`** — optional seam. When registered, it is consulted after resolution so a suspended or offboarded tenant is rejected even if it presents an otherwise valid claim or header. Bridge it to your own tenant directory at the composition root.

**`ITenantResolutionStrategy`** — implement this to add your own strategy.

**`TenantResolutionOptions`** — `StrategyOrder`, bindable from configuration section `SharedKernel:MultiTenancy` (`TenantResolutionOptions.SectionName`), validated at startup by `TenantResolutionOptionsValidator` (a typo or a duplicate entry in `StrategyOrder` fails the host rather than silently resolving nothing). Leave `StrategyOrder` empty to use `TenantResolutionOptions.DefaultStrategyOrder`; a configured list replaces the default.

## Quick Start

```csharp
// Register (Program.cs)
builder.Services.AddSharedKernelMultiTenancy();

// Optional: read StrategyOrder from configuration instead of using the default
builder.Services.Configure<TenantResolutionOptions>(
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName));

app.UseMiddleware<TenantResolutionMiddleware>();           // after UseAuthentication()
// With 14.Presentation's UseSharedKernelWebApi(), in its hook instead:
// app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));

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

**`CachedTenantCatalog`** — a short (30s default), bounded-TTL decorator over any `ITenantCatalog`, stored in the service's `ICacheService`. Call `InvalidateTenantAsync` immediately after changing a tenant's status — do not rely on the TTL alone for a suspended/offboarded tenant. With a distributed cache and backplane (`AddRedisL2`) the invalidation reaches every instance; without one, other instances fall back to the TTL. Fail-safe is off, so an unreachable catalog database never serves a stale `Active` descriptor.

**`CatalogTenantStatusValidator`** — the first real default implementation of `ITenantStatusValidator` above, backed by `ITenantCatalog`. Fails closed: a tenant absent from the catalog is treated identically to `Suspended`/`Offboarded`.

Worked composition example:

```csharp
// Program.cs
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

builder.Services.AddScoped<ITenantCatalog>(sp =>
{
    var database = new DatabaseTenantCatalog(sp.GetRequiredService<IDbConnectionFactory>());
    return new CachedTenantCatalog(
        database,
        sp.GetRequiredService<ICacheService>(),
        sp.GetRequiredService<ICacheKeyProvider>()); // 30s default TTL
});

builder.Services.AddScoped<ITenantStatusValidator, CatalogTenantStatusValidator>();
```

## Security note — strategy order matters

The default order is **`Claim` → `Header` → `Database`**, and it is deliberate.

A JWT tenant claim is signature-verified; the `X-Tenant-Id` header is caller-supplied and trivially forged. Placing `Header` ahead of `Claim` lets any caller override a verified identity with an arbitrary one — a cross-tenant impersonation vector. This ordering is locked by an architecture test in `00.Governance` so it cannot silently regress.

A service with no tenant directory simply omits `"Database"` from the order — no code change required.

`StrategyOrder` is empty by default and the documented order lives in `TenantResolutionOptions.DefaultStrategyOrder`. Configuration binding appends to a list that already has items, so a non-empty default would turn a configured `["Header"]` into `[Claim, Header, Database, Header]`; with an empty default, a configured order replaces the default exactly.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [13.ServiceDefaults README](../README.md) for the full host-composition layer.
