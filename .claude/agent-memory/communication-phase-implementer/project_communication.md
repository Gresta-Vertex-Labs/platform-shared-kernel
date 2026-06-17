---
name: project-communication
description: 11.Communication domain status, NuGet version pins, and key cross-phase implementation patterns discovered during SK.11.Rest and SK.11.Grpc
metadata:
  type: project
---

## Phase completion status (as of 2026-06-17)

- SK.11.Design: ● (22 tasks)
- SK.11.Scaffold: ● (13 tasks)
- SK.11.Rest: ● (10 tasks, 35/35 tests)
- SK.11.Grpc: ● (9 tasks, 46/46 tests)
- SK.11.Internal: pending
- SK.11.GraphQL: pending

**Why:** WO-025 (P-154 through P-159) implementing the full 11.Communication domain.

**How to apply:** When resuming, start from the first non-● phase in sub state-map at `11.Communication/state-map.md`.

## NuGet version pins (confirmed working)

### SharedKernel.Communication.Rest
- `Microsoft.Extensions.Http` 10.0.0
- `Microsoft.Extensions.Http.Resilience` 9.8.0
- `Microsoft.AspNetCore.Http` 2.3.0 (for HttpContextAccessor concrete class)

### SharedKernel.Communication.Grpc
- `Grpc.Net.Client` 2.80.0
- `Grpc.Net.ClientFactory` 2.80.0
- `Google.Protobuf` 3.35.1
- `Google.Api.CommonProtos` 2.17.0
- `OpenTelemetry.Instrumentation.GrpcNetClient` 1.15.1-beta.1

### SharedKernel.Communication.Internal
- `Microsoft.Extensions.ServiceDiscovery` (pin TBD when SK.11.Internal implemented)

## Handler pipeline order (REST)

CorrelationIdDelegatingHandler → TenantIdDelegatingHandler → StandardResilienceHandler → transport

Registration order in `AddRestClient<TClient>()` determines pipeline: add innermost (StandardResilienceHandler) last.

## gRPC interceptor singleton registration pattern

Both `CorrelationTracingInterceptor` and `TenantIdInterceptor` are registered as **singletons** via `TryAddSingleton`. Despite resolving request-scoped `ITenantProvider`, they are safe as singletons because they access the scope dynamically via `IHttpContextAccessor` at call time (not via constructor injection).

## Address resolution at gRPC channel creation

When `IServiceEndpointResolver` is registered and `Address` is omitted, the address is resolved via `.GetAwaiter().GetResult()` inside the `AddGrpcClient<T>((sp, o) => ...)` factory action. Safe because:
1. `KubernetesServiceEndpointResolver.ResolveAsync` never throws per its contract
2. Channel is a singleton — factory fires once, not per-call

## Money Protobuf conversion formula

`decimal → Money`: `Units = (long)Truncate(value)`, `Nanos = (int)Round((value - units) * 1_000_000_000, 0)`
`Money → decimal`: `Units + (decimal)Nanos / 1_000_000_000`
