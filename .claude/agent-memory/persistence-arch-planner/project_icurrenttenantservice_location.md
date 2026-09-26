---
name: project_icurrenttenantservice_location
description: Tenant source for TenantedDbContext — IRequestContext.TenantId (SharedKernel.Execution, TenantId?); null fails closed (zero rows, writes rejected)
metadata:
  type: project
---

> WO-086 (2026-09): `ITenantProvider` (Security.Abstractions) and its `Guid.Empty` sentinel were deleted; the earlier `ICurrentTenantService` (P-078) and the `NoOpTenantProvider` are long gone.

**Current rule:** `TenantedDbContext.CurrentTenantId` is `RequestContext.TenantId` — `IRequestContext` from `SharedKernel.Execution.Context` (Foundation tier), typed `SharedKernel.Execution.Tenancy.TenantId?` (a `readonly record struct` that rejects `Guid.Empty`). The same `IRequestContext` feeds audit attribution. With no `IRequestContext` registered the builder defaults to `AnonymousRequestContext` (no tenant).

**Fail-closed:** `null` tenant → the named tenant filter matches zero rows and the tenant write guard rejects every tenant-scoped write. There is no "empty GUID" tenant any more.

**Why:** one caller contract shared with `05.Application`'s pipeline; `06.Persistence` never references `12.Security` or `SharedKernel.Application` — the host (`SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()`) supplies the implementation.

**How to apply:** plan multi-tenancy tasks against `IRequestContext.TenantId`; never reintroduce a local tenant seam or a sentinel value. Cross-tenant work goes through `ICrossTenantScope`.

**Filter construction:** expression trees applied via `HasQueryFilter` — no `GetMethod`/`MakeGenericMethod`/`Invoke`.

See also: [[project_iusercontext_pattern]]
