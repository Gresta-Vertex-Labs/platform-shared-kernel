---
name: feedback-request-context-accessor
description: Outbound REST/gRPC propagation reads the caller from IRequestContextAccessor (SharedKernel.Execution) — never IUserContext or IHttpContextAccessor
metadata:
  type: feedback
---

`RequestContextDelegatingHandler` (Rest) and `CorrelationTracingInterceptor`/`TenantIdInterceptor` (Grpc) take `IRequestContextAccessor` by constructor and write `accessor.Current` through `RequestContextPropagation.WriteHeaders` under `WellKnownHeaders` names (correlation, tenant, actor, client). The correlation id is `CorrelationIds.Current(context) ?? CorrelationIds.New()`, never `Activity.Id`.

**Why:** WO-086 (2026-09) deleted the old tenant-provider seam and moved the caller contract into `SharedKernel.Execution`. The old path (`IHttpContextAccessor.HttpContext.RequestServices` → a scoped tenant provider) only worked for HTTP-originated calls; a call from a message consumer, workflow activity or scheduled job carried no tenant. The accessor is AsyncLocal and is set by every inbound adapter, and it keeps these packages free of ASP.NET Core (SKTIER006 for the Adapter tier).

**How to apply:** Never inject `IHttpContextAccessor`, `IUserContext` or a scoped service into a handler/interceptor. Handlers/interceptors can be singletons because the accessor is read at call time. In tests, set the caller with `RequestContextScope.Begin(ctx)` (e.g. core `SharedKernel.Testing`'s `TestRequestContext`); "no tenant" is `TenantId == null` (`TenantId?`), not `Guid.Empty`.
