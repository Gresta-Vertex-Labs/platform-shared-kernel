---
name: project_iusercontext_pattern
description: Audit actor for CreatedBy/ModifiedBy — IRequestContext.UserId, else the configured ServiceName ("system"); no Security.Abstractions reference
metadata:
  type: project
---

> WO-086 (2026-09): the P-078 `SharedKernel.Security.Abstractions` reference (a numbered-layer exception) and the `IUserContext`/`NoOpUserContext`-based audit path are gone; the actor comes from `SharedKernel.Execution`'s `IRequestContext`.

**Current rule:** `SharedKernelDbContext` records the actor as `RequestContext.UserId` when it is non-empty, otherwise the service name (`UseServiceName(...)` or `SharedKernel:Persistence:ServiceName`, default `"system"` — `PersistenceDefaults.ServiceName`). `IRequestContext` is `SharedKernel.Execution.Context` (Foundation tier), so `06.Persistence` references no `12.Security` package.

**Why:** a background job or unauthenticated path still produces a non-null, attributable audit value; one caller contract shared with the application pipeline.

**How to apply:** plan audit-column tasks against `IRequestContext` only; for background work use `SystemRequestContext` (explicit identity) through `ICallerDbContextFactory<TContext>` or a DI scope. Never add a `12.Security` reference.

See also: [[project_icurrenttenantservice_location]]
