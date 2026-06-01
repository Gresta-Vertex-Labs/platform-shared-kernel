---
name: project_iusercontext_pattern
description: IUserContext injection pattern for AuditInterceptor/SoftDeleteInterceptor — no direct 12.Security reference
metadata:
  type: project
---

`AuditInterceptor` and `SoftDeleteInterceptor` need `IUserContext` but `06.Persistence` cannot reference `12.Security` by layering rules (lower number = more foundational; 12 > 6 so the reference direction is wrong).

Resolution pattern (confirmed in WO-013 design, 2026-06-01):

1. Interceptors declare `IUserContext` as a constructor parameter typed to the interface from `12.Security.Abstractions`. DI wires this at runtime — no compile-time project reference needed in `06.Persistence`.
2. `EfCorePersistenceBuilder.Build()` registers a scoped no-op `IUserContext` placeholder (returns `"system"`) **only when no `IUserContext` is already registered** in the DI container.
3. Consuming services override the placeholder by registering their own implementation (e.g., `OidcUserContext` from `12.Security.Oidc`) before or after `.Build()` — last registration wins.
4. All three interceptors are registered as **scoped** services so they receive a per-request `IUserContext`.

**Why:** Standard EF Core interceptor DI pattern. The interceptors need per-request user context; scoped lifetime provides it. The no-op placeholder prevents startup failures in test or background-service contexts where no real user is present.

**How to apply:** Never add `SharedKernel.Security.Abstractions` as a project reference in any `.Persistence.*` csproj. The interface type resolves purely through DI at runtime.
