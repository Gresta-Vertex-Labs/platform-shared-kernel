# SharedKernel.MultiTenancy

Per-request tenant resolution for Platform.SharedKernel microservices. It decides which tenant a request belongs to
and puts it on the request context, so every layer that reads `IRequestContext.TenantId` (`TenantId?`,
`SharedKernel.Execution`) — persistence filters and row-level security, cache keys, idempotency keys, outbound
propagation — sees the resolved tenant. **Tier: Host.**

Without this package, a request's tenant is the one the caller's credential asserts (through
`SharedKernel.ServiceDefaults.Security`). Add it when a tenant can also come from a header or a tenant directory, or
when a suspended tenant must be rejected.

## Included

**`TenantResolutionMiddleware`** — runs the configured strategies in order; the first one returning a tenant wins.
It then opens an inner `RequestContextScope` that replaces **only** the tenant: the caller and the correlation id stay
those of `UseSharedKernelRequestContext()`'s scope. When nothing resolves (or the tenant is inactive), the tenant is
`null` — fail closed, even if the credential asserted one. A resolved tenant is also set as `Activity` baggage
(`WellKnownBaggageKeys.TenantId`) for log enrichment.

| Strategy | Resolves from |
|---|---|
| `ClaimTenantResolutionStrategy` | The authenticated credential's tenant, through the authentication package's `IUserContextMapper` (the claim type is that package's setting) |
| `HeaderTenantResolutionStrategy` | The `X-Tenant-Id` request header (`WellKnownHeaders.TenantId`), parsed with `TenantId.TryParse`; malformed → no tenant, never an exception |
| `DatabaseTenantResolutionStrategy` | A tenant-directory lookup (host/domain → tenant) through `IDbConnectionFactory`, parameterized |

**`ITenantResolutionStrategy`** — implement `Task<TenantId?> TryResolveAsync(HttpContext, CancellationToken)` and a
`StrategyName` to add your own.

**`ITenantStatusValidator`** — optional. When registered, it is consulted after resolution so a suspended or
offboarded tenant is rejected even with an otherwise valid claim or header.

**`TenantResolutionOptions`** — `StrategyOrder`, bindable from `SharedKernel:MultiTenancy`
(`TenantResolutionOptions.SectionName`), validated at startup by `TenantResolutionOptionsValidator` (a typo or a
duplicate entry fails the host rather than silently resolving nothing). Leave it empty to use
`TenantResolutionOptions.DefaultStrategyOrder`; a configured list replaces the default.

## Quick start

```csharp
builder.Services.AddSharedKernelRequestContext();        // SharedKernel.ServiceDefaults.Security
builder.Services.AddSharedKernelMultiTenancy();

// Optional: read StrategyOrder from configuration instead of using the default
builder.Services.Configure<TenantResolutionOptions>(
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName));

var app = builder.Build();

// The canonical pipeline, with 14.Presentation's SharedKernel.Presentation.WebApi:
app.UseSharedKernelRequestContext();                        // first: the request's scope and correlation id
app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a =>
    a.UseMiddleware<TenantResolutionMiddleware>()));        // after authentication, before authorization
app.MapEndpoints();

// Without SharedKernel.Presentation.WebApi, the same order by hand:
// app.UseSharedKernelRequestContext();
// app.UseExceptionHandler();
// app.UseAuthentication();
// app.UseMiddleware<TenantResolutionMiddleware>();
// app.UseAuthorization();
```

Read the tenant through the request context:

```csharp
public sealed class OrderService(IRequestContext caller)
{
    public TenantId? CurrentTenant => caller.TenantId;
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

**`ITenantCatalog`** — `GetByIdAsync(TenantId, ct)` and `GetByResolutionKeyAsync(string, ct)` (host, claim value,
header value) returning a `TenantDescriptor` (status, isolation mode, default culture, settings). Lookup only —
tenant provisioning/onboarding is a consuming service's own concern.

**`DatabaseTenantCatalog`** — queries a consumer-owned tenant directory table via `IDbConnectionFactory`, with the same
parameterized-query pattern as `DatabaseTenantResolutionStrategy`.

**`CachedTenantCatalog`** — a short (30 s default), bounded-TTL decorator over any `ITenantCatalog`, stored in the
service's `ICacheService`. Call `InvalidateTenantAsync(tenantId, ct)` immediately after changing a tenant's status —
do not rely on the TTL alone. With a distributed cache and backplane (`AddRedisL2`) the invalidation reaches every
instance; without one, other instances fall back to the TTL. Fail-safe is off, so an unreachable catalog database
never serves a stale `Active` descriptor.

**`CatalogTenantStatusValidator`** — an `ITenantStatusValidator` backed by `ITenantCatalog`. Fails closed: a tenant
absent from the catalog is treated like `Suspended`/`Offboarded`.

```csharp
builder.Services.AddScoped<ITenantCatalog>(sp =>
{
    var database = new DatabaseTenantCatalog(sp.GetRequiredService<IDbConnectionFactory>());
    return new CachedTenantCatalog(
        database,
        sp.GetRequiredService<ICacheService>(),
        sp.GetRequiredService<ICacheKeyProvider>()); // 30 s default TTL
});

builder.Services.AddScoped<ITenantStatusValidator, CatalogTenantStatusValidator>();
```

## Security note — strategy order matters

The default order is **`Claim` → `Header` → `Database`**, and it is deliberate.

A JWT tenant claim is signature-verified; the `X-Tenant-Id` header is caller-supplied and trivially forged. Placing
`Header` ahead of `Claim` lets any caller override a verified identity with an arbitrary one — a cross-tenant
impersonation vector. This ordering is locked by an architecture test in `00.Governance` so it cannot silently
regress.

A service with no tenant directory simply omits `"Database"` from the order — no code change required.

`StrategyOrder` is empty by default and the documented order lives in `TenantResolutionOptions.DefaultStrategyOrder`.
Configuration binding appends to a list that already has items, so a non-empty default would turn a configured
`["Header"]` into `[Claim, Header, Database, Header]`; with an empty default, a configured order replaces the default
exactly.

## Logging

| EventId | Event |
| --- | --- |
| `13100` | Tenant resolved — tenant id and strategy name |
| `13101` | No tenant resolved (or the resolved tenant is inactive) |

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the
[13.ServiceDefaults README](../README.md) for the full host composition.
