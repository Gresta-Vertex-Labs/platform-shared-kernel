---
name: project-wo025-communication-packages
description: WO-025 task set for all four Communication packages — phase counts, key decisions, and sequencing
metadata:
  type: project
---

> WO-086 (2026-09): `CorrelationIdDelegatingHandler` + `TenantIdDelegatingHandler` are now one `RequestContextDelegatingHandler` reading `IRequestContextAccessor` (no `IHttpContextAccessor`/`IUserContext`); `SharedKernel.Communication.GraphQL` moved to `14.Presentation` as `SharedKernel.Presentation.GraphQL` — the GraphQL decisions below are history for this domain.

WO-025 (P-154 through P-157) added 97 total tasks across 9 phases for the four Communication packages.

Phase counts: Design 22, Scaffold 13, Rest 10, Grpc 9, GraphQL 8, Internal 6, Tests 18, Docs 5, Published 6.

**Key decisions recorded:**
- `StandardResilienceHandler` is the mandatory Polly v8 wiring — no raw `Polly.Core` pipeline construction
- Handler pipeline order is fixed: `CorrelationIdDelegatingHandler` → `TenantIdDelegatingHandler` → `StandardResilienceHandler` → transport
- `TimeoutSeconds` is a per-request timeout configured via `StandardResilienceHandler`, NOT `HttpClient.Timeout`
- `TenantIdDelegatingHandler` resolves `IUserContext` from `IHttpContextAccessor.HttpContext.RequestServices` — never constructor-injected (request-scope rule)
- STJ `ProblemDetailsJsonContext` is the primary deserialization path; reflection-STJ is fallback only
- `CorrelationTracingInterceptor` reads `Activity.Current` at call time, not DI registration time
- gRPC interceptors catch all exceptions, log `Error`, continue — never propagate
- `StaticServiceEndpointResolver.ResolveAsync` returns K8s convention URI for unknown names (never throws) — parity with `KubernetesServiceEndpointResolver`
- `AddSharedKernelGraphQL` is idempotent (second call is no-op); must be called before service-specific `AddGraphQL()`/`AddTypes()`
- HotChocolate v14 is NOT AOT-safe — `<IsAotCompatible>true</IsAotCompatible>` must never appear on `.GraphQL` project
- `MaxPageSize` default 100, hard cap 500 enforced by `IValidateOptions<GraphQLOptions>`

**Why:** WO-025 is the initial implementation wave for 11.Communication. All four packages were empty scaffolds. No tasks existed before this session.

**How to apply:** When implementation tasks are dispatched, reference S-13 for NuGet version pinning before any R/G/GQ/I tasks begin.
