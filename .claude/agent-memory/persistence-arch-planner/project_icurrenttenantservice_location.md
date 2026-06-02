---
name: project_icurrenttenantservice_location
description: ICurrentTenantService removed — TenantedDbContext now uses ITenantProvider from Security.Abstractions; Guid.Empty is the no-tenant sentinel
metadata:
  type: project
---

**P-078 decision (WO-014, 2026-06-02):** `ICurrentTenantService` (local interface with `TenantId` as `Guid?`) has been removed from `SharedKernel.Persistence.EfCore`. `TenantedDbContext` now injects `ITenantProvider` from `SharedKernel.Security.Abstractions` (`TenantId` is `Guid`, non-nullable).

**Why the switch was made:** The local interface copy created divergence risk. `Security.Abstractions` is zero-dependency, so referencing it from `EfCore` is architecturally correct and eliminates the maintenance burden of keeping two interfaces in sync.

**Guid.Empty no-tenant sentinel (P-092, WO-016, 2026-06-02):**

`ITenantProvider.TenantId` is non-nullable. When no real provider is registered, `NoOpTenantProvider` returns `Guid.Empty` explicitly (not `default(Guid)` — same value but intent is explicit). The global filter becomes `e.TenantId == Guid.Empty`, returning zero rows. This is intentional and safe — no production entity should have `TenantId == Guid.Empty`.

**Why:** `Guid?` vs `Guid` is a correctness boundary. Returning zero rows on misconfiguration is safer than a cross-tenant leak. Teams see an empty result set immediately.

**How to apply:** When planning any multi-tenancy task, use `ITenantProvider` (not `ICurrentTenantService`). The no-op placeholder is `NoOpTenantProvider` returning `Guid.Empty`. Never plan a task that writes `Guid.Empty` as a real tenant ID in production rows.

**TenantedDbContext filter:** Built via expression trees — `Expression.Parameter`, `Expression.Property`, `Expression.Equal`, `Expression.Lambda` — applied via `modelBuilder.Entity(clrType).HasQueryFilter(lambda)`. No `GetMethod`/`MakeGenericMethod`/`Invoke`.

See also: [[project_iusercontext_pattern]]
