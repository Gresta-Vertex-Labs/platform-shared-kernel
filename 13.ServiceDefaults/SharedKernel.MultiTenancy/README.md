# SharedKernel.MultiTenancy

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)

> **Per-request tenant resolution: decides which tenant an HTTP request belongs to — from the verified credential, a
> header or a tenant directory — and puts it on the request context, so persistence, caching, idempotency and outbound
> calls all see the same tenant. A suspended tenant fails closed.**

| You get | So that |
| --- | --- |
| `TenantResolutionMiddleware` | The resolved tenant replaces **only** the tenant on `IRequestContext`; caller and correlation id are kept |
| Claim → Header → Database strategies | A signed claim outranks a caller-supplied header by default |
| `ITenantResolutionStrategy` | You can add a strategy of your own (subdomain, path, API-key owner…) |
| `ITenantStatusValidator` + `CatalogTenantStatusValidator` | A suspended or offboarded tenant is rejected even with a valid claim |
| `ITenantCatalog`, `DatabaseTenantCatalog`, `CachedTenantCatalog` | Read-only tenant metadata (status, isolation mode, default culture) with a short, invalidatable cache |
| Startup validation of `StrategyOrder` | A typo or duplicate strategy name fails the host instead of silently resolving nothing |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.MultiTenancy" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** project |
| Depends on | `SharedKernel.Execution`, `SharedKernel.Security.Abstractions`, `SharedKernel.Persistence.Abstractions`, `SharedKernel.Caching.Abstractions` |
| Namespaces | `SharedKernel.MultiTenancy.Extensions`, `.Middleware`, `.Resolution`, `.Catalog` |

Without this package a request's tenant is the one the caller's credential asserts (through
`SharedKernel.ServiceDefaults.Security`). Add it when a tenant can also come from a header or a directory, or when a
suspended tenant must be rejected.

## Quick start

```csharp
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.ServiceDefaults.Security;

builder.Services.AddSharedKernelRequestContext();
builder.Services.AddSharedKernelMultiTenancy(o =>
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName).Bind(o));   // optional

var app = builder.Build();

app.UseSharedKernelRequestContext();                        // first: the request's scope and correlation id
app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a =>
    a.UseMiddleware<TenantResolutionMiddleware>()));        // after authentication, before authorization
app.MapEndpoints();
```

```json
{
  "SharedKernel": {
    "MultiTenancy": { "StrategyOrder": [ "Claim", "Header" ] }
  }
}
```

Read the tenant through the request context, never from `HttpContext`:

```csharp
public sealed class OrderService(IRequestContext caller)
{
    public TenantId? CurrentTenant => caller.TenantId;   // null → tenant-scoped code fails closed
}
```

Without `SharedKernel.Presentation.WebApi`: `UseSharedKernelRequestContext()`, `UseExceptionHandler()`,
`UseAuthentication()`, `UseMiddleware<TenantResolutionMiddleware>()`, `UseAuthorization()`.

## How it works

- The middleware runs the strategies in `StrategyOrder`; **the first one returning a tenant wins**.
- With an `ITenantStatusValidator` registered, a resolved tenant that is not active is dropped. Nothing resolved, or
  an inactive tenant, means the tenant is `null` — fail closed, even if the credential asserted one. The request still
  runs; tenant-scoped code (persistence filters, row-level security, tenant caches) refuses to work without a tenant.
- It then opens an inner `RequestContextScope` via `IRequestContext.WithTenant(...)`: only the tenant changes.
- The `TenantId` baggage item on the `Activity` is always replaced (removed when no tenant resolved), so a caller's
  own `TenantId` baggage never reaches log records.

| Strategy | Name | Resolves from |
| --- | --- | --- |
| `ClaimTenantResolutionStrategy` | `Claim` | The authenticated credential's tenant, through the scheme's `IUserContextMapper` |
| `HeaderTenantResolutionStrategy` | `Header` | `X-Tenant-Id` (`WellKnownHeaders.TenantId`) via `TenantId.TryParse`; malformed → no tenant, never an exception |
| `DatabaseTenantResolutionStrategy` | `Database` | `SELECT tenant_id FROM tenant_directory WHERE host = @host` through `IDbConnectionFactory` |

**Why Claim → Header → Database?** A JWT tenant claim is signature-verified; the header is caller-supplied and trivially
forged. Putting `Header` first would let any caller override a verified identity — a cross-tenant impersonation
vector. The default order is locked by an architecture test in `00.Governance`.

## Recipes

### 1. Reject suspended tenants

```csharp
using SharedKernel.Caching.Abstractions;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence.Abstractions.Connections;

builder.Services.AddScoped<ITenantCatalog>(sp => new CachedTenantCatalog(
    new DatabaseTenantCatalog(sp.GetRequiredService<IDbConnectionFactory>()),
    sp.GetRequiredService<ICacheService>(),
    sp.GetRequiredService<ICacheKeyProvider>()));           // ttl defaults to 30 s

builder.Services.AddScoped<ITenantStatusValidator, CatalogTenantStatusValidator>();
```

`CatalogTenantStatusValidator` fails closed: a tenant absent from the catalog is treated like `Suspended`. After
changing a tenant's status, call `CachedTenantCatalog.InvalidateTenantAsync(tenantId, ct)` — with a Redis L2 and
backplane the eviction reaches every instance; otherwise other instances fall back to the TTL. Fail-safe is off, so an
unreachable catalog database never serves a stale `Active` descriptor.

### 2. Add a strategy of your own

Implement `ITenantResolutionStrategy` — `string StrategyName` and
`Task<TenantId?> TryResolveAsync(HttpContext, CancellationToken)` — register it scoped, and add its name to
`StrategyOrder`.

### 3. A service with no tenant directory

Leave `"Database"` out of `StrategyOrder`. No code change.

## Configuration

Section `SharedKernel:MultiTenancy` (`TenantResolutionOptions.SectionName`), bound when you bind it (see Quick start)
and validated at host start.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:MultiTenancy:StrategyOrder` | `string[]` | empty → `DefaultStrategyOrder` (`Claim`, `Header`, `Database`) | Strategies to try, in order; each must match a registered strategy's `StrategyName`, no duplicates |

`StrategyOrder` is empty by default on purpose: configuration binding appends to a non-empty list, so a configured
`["Header"]` would otherwise become `[Claim, Header, Database, Header]`. A configured order replaces the default exactly.

The `tenant_directory` table (`tenant_id`, `host`, `resolution_key`, `display_name`, `status`, `isolation_mode`,
`default_culture`) is consumer-owned; provisioning is out of scope.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddSharedKernelMultiTenancy(Action<TenantResolutionOptions>?)` | `TenantResolutionOptions` (validated on start), `IRequestContextAccessor` (if absent), the Header, Claim and Database strategies (scoped). The middleware itself is added by you |

### Types

| Type | Purpose |
| --- | --- |
| `TenantResolutionMiddleware` | Resolves the tenant and opens the inner scope |
| `ITenantResolutionStrategy`, `TenantResolutionStrategyNames` | Strategy contract; `Claim`, `Header`, `Database` |
| `ITenantStatusValidator` | `IsActiveAsync(TenantId, ct)`; optional gate |
| `ITenantCatalog` | `GetByIdAsync(TenantId, ct)`, `GetByResolutionKeyAsync(string, ct)` → `TenantDescriptor?` |
| `TenantDescriptor` | `TenantId`, `DisplayName`, `Status` (`Active`/`Suspended`/`Offboarded`), `IsolationMode` (`Shared`/`Dedicated`), `DefaultCulture`, `Settings` |
| `DatabaseTenantCatalog`, `CachedTenantCatalog` (`DefaultTtl` = 30 s, `InvalidateTenantAsync`), `CatalogTenantStatusValidator` | Catalog implementations |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 13100 | Debug | Tenant resolved — tenant id and strategy name |
| 13101 | Trace | No tenant resolved, or the resolved tenant is inactive |

## Testing

Reference [`SharedKernel.ServiceDefaults.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.ServiceDefaults.Testing/README.md)
(namespace `SharedKernel.Testing.ServiceDefaults`):

- `new FakeTenantResolutionStrategy(tenantId)` (or a resolver delegate), `StrategyName` settable — drive the middleware
  without headers or a database.
- `InMemoryTenantCatalog` — `SeedTenant(descriptor, resolutionKey)`, `MutateStatus(tenantId, status)`, `Reset()`; pair
  it with `CatalogTenantStatusValidator` to test suspension.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Put `Header` before `Claim` | Keep the default order, or omit `Header` | A forged header would override a verified tenant |
| Add the middleware before `UseAuthentication()` | Place it after authentication, before authorization | The `Claim` strategy needs the authenticated user |
| Call `AddSharedKernelMultiTenancy()` and forget `UseMiddleware<TenantResolutionMiddleware>()` | Wire the middleware explicitly | Registration alone resolves nothing beyond the credential's tenant |
| Rely on the catalog TTL after suspending a tenant | Call `InvalidateTenantAsync` | A suspended tenant would keep access for up to the TTL |
| Treat a `null` tenant as "all tenants" | Fail closed; use `ICrossTenantScope` for deliberate cross-tenant work | `null` means no tenant was resolved |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[ServiceDefaults domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/13.ServiceDefaults/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
