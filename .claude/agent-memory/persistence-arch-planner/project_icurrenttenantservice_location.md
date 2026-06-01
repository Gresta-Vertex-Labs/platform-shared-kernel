---
name: project_icurrenttenantservice_location
description: ICurrentTenantService lives in SharedKernel.Persistence.EfCore, not in Abstractions
metadata:
  type: project
---

`ICurrentTenantService` (exposes `TenantId` as `Guid?`) is defined in `SharedKernel.Persistence.EfCore`, not in `SharedKernel.Persistence.Abstractions`.

**Why:** It is an EfCore DbContext concern — `TenantedDbContext` needs it to install a global query filter. Placing it in `.Abstractions` would make the ORM-agnostic interface layer aware of a concept only relevant to EF Core's model-building pipeline.

**Concrete implementation:** Lives in `13.ServiceDefaults.MultiTenancy`. `EfCorePersistenceBuilder.WithMultiTenancy()` registers a no-op placeholder that is overridden by the ServiceDefaults package.

**How to apply:** When a phase request mentions `ICurrentTenantService`, always place it in `.EfCore`. If it appears in a design for `.Abstractions`, reject and correct.
