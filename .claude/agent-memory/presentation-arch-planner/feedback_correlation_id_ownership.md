---
name: feedback_correlation_id_ownership
description: Inbound correlation id is owned by 13.ServiceDefaults.Security (UseSharedKernelRequestContext) since WO-086 — 14.Presentation consumes it, never re-reads the header
metadata:
  type: feedback
---

Since WO-086 (2026-09) the inbound correlation id, tenant and actor are established by `app.UseSharedKernelRequestContext()` (`SharedKernel.ServiceDefaults.Security`, the first middleware, before `UseExceptionHandler()`), which owns `X-Correlation-Id` (`WellKnownHeaders.CorrelationId`) and opens a `RequestContextScope`. `14.Presentation` reads the result through `IRequestContextAccessor` (`SharedKernel.Execution`); its own `CorrelationIdMiddleware`/`CorrelationIdOptions`/`AddSharedKernelCorrelationId` were deleted. (Before WO-086 the key and middleware were this domain's own contract — WO-031 P-192.)

**Why:** two readers of the same inbound header drifted before (the `"correlation.id"` vs `"CorrelationId"` baggage mismatch, WO-042 — see [[project_correlationid_baggage_key_mismatch]]). One owner, one scope, and everyone else reading the accessor makes that drift impossible.

**How to apply:** never plan a correlation-id or tenant header reader, baggage writer or `HttpContext.Items` key in `14.Presentation`; a hub filter, gRPC interceptor or ProblemDetails `traceId`/correlation extension reads `IRequestContextAccessor`/`Activity`. The general lesson still holds when evaluating a cross-domain dependency row: separate "another domain consumes our published contract" (one-way, not a blocking dependency) from a genuine implementation need.
