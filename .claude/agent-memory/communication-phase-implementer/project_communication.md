---
name: project-communication
description: 11.Communication domain status, NuGet version pins, and key cross-phase implementation patterns discovered during SK.11 implementation
metadata:
  type: project
---

## Phase completion status (as of 2026-07-13)

- SK.11.Design: ● (23 tasks, incl. D-23 EventId allocation)
- SK.11.Scaffold: ● (13 tasks)
- SK.11.Rest: ● (18 tasks, 66/66 tests)
- SK.11.Grpc: ● (15 tasks, 60/60 tests) — includes G-14/G-15 [LoggerMessage] retrofit (EventId 11100/11101)
- SK.11.GraphQL: ● (9 tasks, 43/43 tests)
- SK.11.Internal: ● (11 tasks, 52/52 tests) — includes I-09/I-10/I-11 [LoggerMessage] retrofit (EventId 11300-11308)
- SK.11.Tests: ● (28 tasks) — T-27/T-28 (WO-041 P-255 regression + real-assembly verification) complete
- SK.11.Docs: ○ (6 tasks pending) — only DO-06 is P-255-specific; DO-01..DO-05 are original WO-025 backlog
- SK.11.Published: ○ (6 tasks pending)

**Why:** WO-025/WO-026 built the domain; WO-041 (P-255) retrofitted `.Grpc`/`.Internal` onto the platform `[LoggerMessage]` logging standard (root CLAUDE.md).

**How to apply:** When resuming, start from the first non-● phase in sub state-map at `11.Communication/state-map.md`. Next pending: DO-06 (Docs — update CLAUDE.md's already-present Logging section is done; DO-06 itself may already be satisfied by prior sessions' edits — verify before redoing).

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

### SharedKernel.Communication.GraphQL

- `HotChocolate.Data` 16.1.4
- `HotChocolate.AspNetCore` 16.1.4
- Note: architecture originally specified v14 but v14 is not available for net10.0; v16.1.4 used

### SharedKernel.Communication.Internal

- `Microsoft.Extensions.ServiceDiscovery` (pin TBD when SK.11.Internal I-07/I-08 complete)

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

## PagedResponseType factory method summary

- `FromPage(IPage)` — offset paging source (HC v16 IPage, not `CollectionSegment<T>`)
- `FromConnection(Connection<T>)` — cursor paging source
- `From(IReadOnlyList<T>, int)` — manual assembly
- `FromPagedList(PagedList<T>)` — bridge from 04.Contracts application layer result (GQ-09/P-165)

`FromPagedList` requires the `SharedKernel.Contracts` (04.Contracts) project reference in the GraphQL csproj — already present in the original scaffold.
