---
name: feedback-tenant-provider
description: Use ITenantProvider (not IUserContext) for tenant resolution in REST and gRPC handlers/interceptors
metadata:
  type: feedback
---

The 11.Communication REST and gRPC packages use `ITenantProvider` (from `12.Security.Abstractions`) for tenant ID extraction — not `IUserContext`.

**Why:** `IUserContext` carries user identity (claims, roles) but does not expose `TenantId`. `ITenantProvider` is the correct abstraction for reading the current tenant. The CLAUDE.md spec initially said "IUserContext" but the actual `IUserContext` interface has no `TenantId` property. The Rest phase implementation (done before Grpc) already used `ITenantProvider`, so the Grpc phase followed suit.

**How to apply:** In `TenantIdDelegatingHandler` and `TenantIdInterceptor`, always resolve `ITenantProvider` from `IHttpContextAccessor.HttpContext.RequestServices`. The null-guard chain is: HttpContext null → no-op; `GetService<ITenantProvider>()` null → no-op; `TenantId == Guid.Empty` → no-op.
