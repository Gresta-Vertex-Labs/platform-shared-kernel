# 11.Communication — Domain Brain

## What This Domain Is

The outbound communication capability domain. Provides platform-standard wrappers for a microservice to communicate with **other services or clients** using the four primary channels in a K8s-native microservice ecosystem: REST, gRPC, GraphQL, and in-cluster service discovery.

Every package in this domain eliminates repeated boilerplate — resilience policies, OTel propagation, correlation/tenant header injection, and service address resolution are wired once at the composition root and invisible to application code.

Philosophy: **Protocol-Agnostic Resilience. Propagate Context Always. Fail Informatively.**

> **Scope clarification:** This domain covers **outgoing** communication adapters only. Inbound concerns (ASP.NET routing, middleware, endpoint mapping) belong in `14.Presentation`. Message-bus event publishing belongs in `07.Messaging`. Cache invalidation signaling belongs in `02.Caching`.

---

## Current Phase

**Design complete (SK.11.Design `●`). Scaffold phase next (SK.11.Scaffold `○`).** All 22 Design tasks validated and complete. Four packages ready for project scaffolding. No implementation code exists yet.

---

## Packages

| Package | Role | NuGet / Project References |
| ------- | ---- | -------------------------- |
| `SharedKernel.Communication.Rest` | Typed `HttpClient` factory with Polly v8 resilience (`StandardResilienceHandler`: retry, circuit breaker, timeout), `CorrelationIdDelegatingHandler`, `TenantIdDelegatingHandler`, `ProblemDetailsDeserializer` → `Error` mapping, `HttpResponseMessageExtensions.EnsureSuccessOrErrorAsync`, and fluent `IRestCommunicationBuilder` DI entry point | `01.Core`, `04.Contracts`, `12.Security.Abstractions`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience` |
| `SharedKernel.Communication.Grpc` | gRPC channel factory via `Grpc.Net.ClientFactory`, `CorrelationTracingInterceptor` (W3C OTel + `x-correlation-id`), `TenantIdInterceptor` (`x-tenant-id`), `MoneyProtoExtensions` (`Money ↔ decimal`), `TimestampProtoExtensions` (`Timestamp ↔ DateTimeOffset`), and fluent `IGrpcCommunicationBuilder` DI entry point | `01.Core`, `04.Contracts`, `12.Security.Abstractions`, `Grpc.Net.Client`, `Grpc.Net.ClientFactory`, `OpenTelemetry.Instrumentation.GrpcNetClient` |
| `SharedKernel.Communication.GraphQL` | HotChocolate v14 server-side conventions: `SharedKernelFilterConvention` (snake_case filter operations), `FilterBase<T>` + `SortBase<T>` abstract base types, `PagedResponseType<T>` (`TotalCount + Items`), `SharedKernelErrorFilter` (`IError` → `ProblemDetails` shape), and `AddSharedKernelGraphQL` DI entry point | `01.Core`, `04.Contracts`, `HotChocolate.Data`, `HotChocolate.AspNetCore` |
| `SharedKernel.Communication.Internal` | `IServiceEndpointResolver` interface, `KubernetesServiceEndpointResolver` (DNS SRV + A-record, via `Microsoft.Extensions.ServiceDiscovery`), `StaticServiceEndpointResolver` (dev/test only), `K8sServiceDiscoveryOptions`, `AddK8sServiceDiscovery` and `AddStaticServiceDiscovery` DI extensions | `01.Core`, `Microsoft.Extensions.ServiceDiscovery` |

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

**Layering rule (from root CLAUDE.md):** `11.Communication` may reference `01.Core`, `04.Contracts`, and `12.Security` abstractions only. It must never reference `02.Caching`, `05.Application`, `06.Persistence`, or `07.Messaging`.

---

## Technology Stack

| Concern | Technology | Confirmed Version | Owning Package |
| ------- | ---------- | ----------------- | -------------- |
| Typed HTTP client DI | `Microsoft.Extensions.Http` | 10.0.0 | `.Rest` |
| HTTP resilience (Polly v8) | `Microsoft.Extensions.Http.Resilience` | 9.x / 10.x | `.Rest` |
| gRPC client | `Grpc.Net.Client` | 2.x | `.Grpc` |
| gRPC DI factory | `Grpc.Net.ClientFactory` | 2.x | `.Grpc` |
| gRPC OTel instrumentation | `OpenTelemetry.Instrumentation.GrpcNetClient` | 0.9.x | `.Grpc` |
| GraphQL server | `HotChocolate.AspNetCore` | 14.x | `.GraphQL` |
| GraphQL filtering/sorting | `HotChocolate.Data` | 14.x | `.GraphQL` |
| K8s service discovery | `Microsoft.Extensions.ServiceDiscovery` | 9.x / 10.x | `.Internal` |
| DI abstractions | `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.0 | all packages |

> Exact versions must be pinned when Scaffold task S-13 completes. Verify `net10.0` compatibility at that time and record pinned versions in a changelog entry.

---

## Interface Contracts

### `SharedKernel.Communication.Rest` — public surface

```text
IRestCommunicationBuilder
    Services  IServiceCollection { get; }
    AddRestClient<TClient>(string name, Action<RestClientOptions>? configure = null)
        → IRestCommunicationBuilder          // returns this — fluent chaining

RestClientOptions  (sealed class)
    BaseAddress          string?             // [Required] when IServiceEndpointResolver not registered
    TimeoutSeconds       int                 // default 30 — per-request timeout
    Resilience           RestResilienceOptions

RestResilienceOptions  (sealed class)
    RetryCount           int                 // default 3
    RetryBaseDelayMs     int                 // default 500 — exponential backoff base
    CircuitBreakerEnabled bool               // default true
    FailureThreshold     int                 // default 5 failures before CB opens
    SamplingDurationSec  int                 // default 30 — failure counting window
    BreakDurationSec     int                 // default 30 — CB open duration

CorrelationIdDelegatingHandler  [internal sealed — transient]
    // Reads Activity.Current?.Id; falls back to Guid.NewGuid().ToString("N").
    // Injects x-correlation-id header. Never overwrites a caller-supplied header.

TenantIdDelegatingHandler  [internal sealed — transient]
    // Resolves IUserContext from IHttpContextAccessor.HttpContext.RequestServices.
    // Injects x-tenant-id header when TenantId non-null.
    // Silent no-op when HttpContext null, IUserContext not registered, or TenantId null.
    // Never throws.

ProblemDetailsDeserializer  [internal static]
    // Deserializes application/problem+json response bodies on non-2xx responses.
    // Uses STJ source-generated ProblemDetailsJsonContext (AOT path).
    // Falls back to reflection-based STJ when source-generated context unavailable.
    // Maps: type → Error.Code, detail ?? title → Error.Message.

HttpResponseMessageExtensions  [public static]
    EnsureSuccessOrErrorAsync<T>(this HttpResponseMessage, CancellationToken)
        → Task<Result<T>>
    // 2xx → Result.Ok; non-2xx → Result.Fail(Error) via ProblemDetailsDeserializer.

AddSharedKernelRestCommunication(this IServiceCollection) → IRestCommunicationBuilder
    // Entry point. Registers IRestCommunicationBuilder, both delegation handlers (transient),
    // and STJ ProblemDetailsJsonContext.
```

### `SharedKernel.Communication.Grpc` — public surface

```text
IGrpcCommunicationBuilder
    Services  IServiceCollection { get; }
    AddGrpcClient<TClient>(string address, Action<GrpcClientOptions>? configure = null)
        → IGrpcCommunicationBuilder          // returns this — fluent chaining

GrpcClientOptions  (sealed class)
    Address              string              // [Required] when IServiceEndpointResolver not registered
    DeadlineSeconds      int                 // default 30 — per-call deadline
    EnableRetry          bool                // default true

CorrelationTracingInterceptor  [internal sealed — Interceptor]
    // Overrides AsyncUnaryCall, AsyncServerStreamingCall, AsyncClientStreamingCall,
    // AsyncDuplexStreamingCall.
    // Reads Activity.Current at call time (not DI registration time).
    // Injects traceparent (W3C format), tracestate, and x-correlation-id into metadata.
    // Does not overwrite existing x-correlation-id entry.
    // Wraps body in try/catch — logs Error and continues on exception (never propagates).

TenantIdInterceptor  [internal sealed — Interceptor]
    // Same four call-type overrides.
    // Resolves IUserContext from request scope via IHttpContextAccessor.
    // Injects x-tenant-id metadata when TenantId non-null.
    // Silent no-op otherwise. Same exception-swallow + Error-log contract.

MoneyProtoExtensions  [public static class]
    ToDecimal(this Money money) → decimal
    ToMoneyProto(this decimal value, string currencyCode) → Money
    // Pure arithmetic, no intermediate object allocations.

TimestampProtoExtensions  [public static class]
    ToDateTimeOffset(this Timestamp ts) → DateTimeOffset
    ToTimestampProto(this DateTimeOffset dto) → Timestamp
    // Allocation-minimal; pure static.

AddSharedKernelGrpcCommunication(this IServiceCollection) → IGrpcCommunicationBuilder
    // Entry point. Registers IGrpcCommunicationBuilder and both interceptors globally.
```

### `SharedKernel.Communication.GraphQL` — public surface

```text
AddSharedKernelGraphQL(this IServiceCollection, Action<GraphQLOptions>? configure = null)
    → IRequestExecutorBuilder
    // Must be called BEFORE any service-specific AddGraphQL()/AddTypes() calls.
    // Wires: snake_case naming, SharedKernelFilterConvention, offset + cursor pagination,
    //        SharedKernelErrorFilter, MaxPageSize cap, AllowIntrospection gate.
    // Idempotent — safe to call twice (second call is a no-op).

GraphQLOptions  (sealed class)
    EnableFiltering      bool                // default true
    EnableSorting        bool                // default true
    EnablePaging         bool                // default true (offset paging)
    MaxPageSize          int                 // default 100; validated ≤ 500
    AllowIntrospection   bool                // default true — set false in production

SharedKernelFilterConvention  [extends FilterConvention — internal]
    // AddDefaults() + snake_case binding names for string/numeric/date operations.
    // Registered as IFilterConvention by AddSharedKernelGraphQL.

FilterBase<T>  [public abstract — extends FilterInputType<T>]
    // Consuming services override Descriptor() to configure visible fields and operations.
    // Enforces snake_case field binding. Direct FilterInputType<T> registration is forbidden.

SortBase<T>  [public abstract — extends SortInputType<T>]
    // Same override model as FilterBase<T>. Direct SortInputType<T> registration is forbidden.

PagedResponseType<T>  [public sealed]
    TotalCount  int
    Items       IReadOnlyList<T>
    // Wraps CollectionSegment<T> (offset) and Connection<T> (cursor) paged results.
    // Field names match PagedList<T> from 04.Contracts for API shape consistency.

SharedKernelErrorFilter  [implements IErrorFilter — internal]
    // IError → ProblemDetails-compatible shape:
    //   status  ← HTTP status code extension
    //   title   ← IError.Message
    //   detail  ← IError.Exception?.Message
    //   extensions ← IError.Extensions
```

### `SharedKernel.Communication.Internal` — public surface

```text
IServiceEndpointResolver  [public interface]
    ResolveAsync(string serviceName, CancellationToken ct) → ValueTask<Uri>
    // Never throws in production. Returns DNS-convention URI on resolution failure.
    // Format on fallback: {scheme}://{serviceName}.{namespace}.svc.{clusterDomain}

K8sServiceDiscoveryOptions  (sealed class)
    Namespace            string              // default "default"
    ClusterDomain        string              // default "cluster.local"
    SchemeOverride       string?             // default null → resolved as "http"

AddK8sServiceDiscovery(this IServiceCollection, Action<K8sServiceDiscoveryOptions>? configure = null)
    → IServiceCollection
    // Registers KubernetesServiceEndpointResolver as IServiceEndpointResolver (singleton).
    // Wires Microsoft.Extensions.ServiceDiscovery DNS resolver.

AddStaticServiceDiscovery(this IServiceCollection, Dictionary<string, Uri> endpoints)
    → IServiceCollection
    // Registers StaticServiceEndpointResolver (singleton). Dev/test only.
    // Throws InvalidOperationException if IServiceEndpointResolver already registered.
    // Logs LogLevel.Warning at startup.
```

---

## Implementation Rules

### REST client rules

- Every outgoing `HttpClient` registered via `AddRestClient<TClient>` **must** have `StandardResilienceHandler` attached — never register a raw `HttpClient` without the resilience handler.
- `StandardResilienceHandler` is the required Polly wiring — do not build retry/circuit-breaker pipelines from scratch with raw `Polly.Core`. Configure it from `RestResilienceOptions` values.
- `CorrelationIdDelegatingHandler` and `TenantIdDelegatingHandler` are registered as **transient** — they must never hold cross-request state.
- `TenantIdDelegatingHandler` must resolve `IUserContext` from the **request scope** via `IHttpContextAccessor.HttpContext.RequestServices` — injecting `IUserContext` directly into the handler constructor would capture the wrong scope.
- `BaseAddress` on `RestClientOptions` is the only allowed way to set the base URI — callers must never hardcode URIs inside typed client methods.
- ProblemDetails deserialization uses STJ source-generated `ProblemDetailsJsonContext` in the primary path; reflection-based STJ is the fallback only.
- The handler pipeline order is fixed: `CorrelationIdDelegatingHandler` → `TenantIdDelegatingHandler` → `StandardResilienceHandler` → transport.
- `TimeoutSeconds` is applied as a per-request timeout via `StandardResilienceHandler`, not as a global `HttpClient.Timeout`.

### gRPC rules

- All interceptors are registered globally via `AddGrpcClient<T>().AddInterceptor<T>()` — no per-call interceptor injection.
- `CorrelationTracingInterceptor` must read `Activity.Current` at the **moment of the call**, not at DI registration time.
- Interceptors must catch **all** exceptions, log at `Error` level, and continue — they must never propagate exceptions into the gRPC call pipeline.
- `MoneyProtoExtensions` and `TimestampProtoExtensions` are pure, static, and allocation-minimal — no `new()` allocations for conversion; no `ToString()`-based intermediate representations.
- `GrpcChannel` instances are expensive — rely on `Grpc.Net.ClientFactory` channel caching (singleton pattern); never create a new `GrpcChannel.ForAddress()` per call.
- TLS is configured at the channel level only (`ChannelCredentials.Insecure` for HTTP, `SslCredentials` for HTTPS) — no per-call TLS configuration.
- Caller-supplied `x-correlation-id` or `x-tenant-id` metadata entries must never be overwritten by the interceptors.

### GraphQL rules

- `AddSharedKernelGraphQL` must be called **before** any service-specific `AddGraphQL()` / `AddTypes()` calls — it establishes the base convention all types inherit.
- `AllowIntrospection` must be `false` in non-development environments — consuming services are responsible for environment-gating this flag in their `Program.cs`.
- `FilterBase<T>` and `SortBase<T>` are mandatory base classes. Direct registration of `FilterInputType<T>` or `SortInputType<T>` without the base wrapper is a platform violation.
- GraphQL error responses must map to the same `ProblemDetails` shape as REST responses — `SharedKernelErrorFilter` handles this automatically when registered via `AddSharedKernelGraphQL`.
- `MaxPageSize` default is 100. Hard cap is 500 — `GraphQLOptions` validator rejects values above 500. Any override beyond 500 requires documented justification in the consuming service.
- `AddSharedKernelGraphQL` is idempotent — calling it twice does not double-register conventions, error filters, or pagination settings.
- HotChocolate v14 is **not AOT-safe** — do not add `<IsAotCompatible>true</IsAotCompatible>` to `SharedKernel.Communication.GraphQL.csproj` or any consuming project that references it.

### Service discovery rules

- `IServiceEndpointResolver` must always be injected into typed clients that need service address resolution — never construct `Uri` from environment variables or `IConfiguration` directly inside typed client methods.
- `KubernetesServiceEndpointResolver` attempts DNS SRV lookup (`_http._tcp.<service>.<namespace>.svc.<clusterDomain>`) first; falls back to A-record for headless services; returns K8s convention URI on full resolution failure.
- `ResolveAsync` must **never throw** for an unresolvable service name in production — it returns a non-null `Uri` using the K8s DNS convention and lets the caller's transport surface the connection error.
- `StaticServiceEndpointResolver` must only be registered in non-production environments — `AddStaticServiceDiscovery` logs `LogLevel.Warning` at startup.
- `AddStaticServiceDiscovery` throws `InvalidOperationException` if `IServiceEndpointResolver` is already registered — prevents silent resolver replacement in misconfigured environments.
- `StaticServiceEndpointResolver.ResolveAsync` returns the K8s convention URI for unknown service names (never throws) to maintain behavioural parity with `KubernetesServiceEndpointResolver`.

### Cross-cutting propagation rules

- **Correlation ID** must be propagated in all outbound channels: REST header `x-correlation-id`, gRPC metadata `x-correlation-id`.
- **Tenant ID** is propagated as `x-tenant-id` in REST and gRPC. GraphQL services resolve tenant from the JWT claim server-side — no header propagation.
- All propagation handlers and interceptors are **best-effort** — they silently skip propagation when ambient context is unavailable. They never throw.
- Caller-supplied `x-correlation-id` or `x-tenant-id` values always win — handlers and interceptors must not overwrite headers or metadata entries already set by the caller.

### Layering violation guard (hard rules)

The following are unconditional violations that must be caught at design review:

| Violation | Rule |
| --------- | ---- |
| Any `.csproj` in this domain references `02.Caching`, `05.Application`, `06.Persistence`, or `07.Messaging` | Hard layering violation |
| `TenantIdDelegatingHandler` or `TenantIdInterceptor` registered as singleton | Hard violation — must be transient |
| `TenantIdDelegatingHandler` receives `IUserContext` in its constructor directly | Hard violation — must resolve from request scope via `IHttpContextAccessor` |
| `AddRestClient<TClient>` without `StandardResilienceHandler` in the pipeline | Hard violation |
| Typed client method contains a hardcoded `Uri` or `BaseAddress` string | Hard violation |
| gRPC interceptor propagates an exception into the call stack | Hard violation — must catch, log `Error`, continue |
| `AddStaticServiceDiscovery` used in a production environment | Hard violation |
| `ResolveAsync` throws for an unresolvable name | Hard violation |
| `FilterInputType<T>` or `SortInputType<T>` registered without `FilterBase<T>` / `SortBase<T>` wrapper | Platform violation |
| `<IsAotCompatible>true</IsAotCompatible>` on `.GraphQL` project | Hard violation — HotChocolate v14 not AOT-safe |
| `MaxPageSize` set above 500 without documented justification | Violation |
| Reflection-based STJ used as primary ProblemDetails deserialization path (not as fallback) | Violation |

### AOT compatibility

- Prefer STJ source-generated `ProblemDetailsJsonContext` for ProblemDetails deserialization in `.Rest`. Reflection-STJ is the fallback, not the default.
- Avoid `dynamic` or reflection-based Protobuf serialization in `.Grpc` — use generated code only.
- HotChocolate v14 in `.GraphQL` is **not AOT-safe** — `<IsAotCompatible>true</IsAotCompatible>` must never be added.
- `Microsoft.Extensions.ServiceDiscovery` in `.Internal` — verify AOT compatibility status on each major upgrade.

---

## DI Registration (reference shape)

```csharp
// REST typed client with full resilience and header propagation
services.AddSharedKernelRestCommunication()
        .AddRestClient<IOrderServiceClient>(options => {
            options.BaseAddress = "http://order-service";
            options.TimeoutSeconds = 15;
            options.Resilience.RetryCount = 3;
            options.Resilience.CircuitBreakerEnabled = true;
        });
// Inject: IOrderServiceClient (typed HttpClient with StandardResilienceHandler +
//         CorrelationIdDelegatingHandler + TenantIdDelegatingHandler)

// gRPC typed client with tracing interceptors
services.AddSharedKernelGrpcCommunication()
        .AddGrpcClient<OrderGrpc.OrderGrpcClient>(options => {
            options.Address = "http://order-service:5001";
            options.DeadlineSeconds = 10;
        });
// Inject: OrderGrpc.OrderGrpcClient (pre-configured with CorrelationTracingInterceptor
//         and TenantIdInterceptor globally)

// K8s in-cluster service discovery
services.AddK8sServiceDiscovery(options => {
    options.Namespace = "production";
    options.ClusterDomain = "cluster.local";
});
// Inject: IServiceEndpointResolver
// ResolveAsync("order-service") → http://order-service.production.svc.cluster.local

// Static service discovery (local dev / tests only)
services.AddStaticServiceDiscovery(new Dictionary<string, Uri> {
    ["order-service"]  = new Uri("http://localhost:5001"),
    ["payment-service"] = new Uri("http://localhost:5002"),
});
// Logs Warning at startup. Throws if IServiceEndpointResolver already registered.

// GraphQL server-side (service that exposes a GraphQL API)
services.AddSharedKernelGraphQL(options => {
    options.AllowIntrospection = builder.Environment.IsDevelopment();
    options.MaxPageSize = 50;
})
.AddQueryType<QueryType>()
.AddType<OrderFilterType>();   // OrderFilterType : FilterBase<Order>
                                // OrderSortType   : SortBase<Order>

// REST + service discovery combined (BaseAddress omitted — resolved at request time)
services.AddK8sServiceDiscovery(options => options.Namespace = "production");
services.AddSharedKernelRestCommunication()
        .AddRestClient<IInventoryServiceClient>(options => {
            // BaseAddress omitted — resolved via IServiceEndpointResolver at request time
            options.Resilience.RetryCount = 2;
        });
```

---

## Test Rules

- Unit tests are nested inside each project folder (e.g., `SharedKernel.Communication.Rest/SharedKernel.Communication.Rest.Tests/`).
- HTTP delegation handler tests (`CorrelationIdDelegatingHandler`, `TenantIdDelegatingHandler`) must use `HttpMessageHandler` test doubles — never make real HTTP calls.
- gRPC interceptor tests must use `Grpc.Core.Testing.TestServerCallContext` or an equivalent in-memory channel stub.
- `KubernetesServiceEndpointResolver` tests must mock the DNS resolver — no live K8s cluster dependency; DNS-dependent tests tagged `[Trait("Category", "Integration")]`.
- `StaticServiceEndpointResolver` unit tests: registered service → correct `Uri`; unregistered service → K8s convention URL; `ResolveAsync` never throws.
- REST resilience tests: use `HttpMessageHandler` test doubles to simulate transient failures; assert retry fires `RetryCount` times; assert circuit breaker opens after `FailureThreshold`.
- GraphQL tests: use HotChocolate's `IRequestExecutor` test builder; assert filter + sort + paging work with configured convention; assert `MaxPageSize` enforcement; assert error mapping to `ProblemDetails` shape.
- All test projects reference `16.Testing/SharedKernel.Testing` for shared helpers.
- Integration tests making real network calls must carry `[Trait("Category", "Integration")]` and must be skipped in unit-only CI runs.

---

## Changelog

> Maintained by the communication domain agent. One line per significant change.

- [2026-06-16] Domain brain initialized — packages, interfaces, technology stack, implementation rules, DI shape, test rules
- [2026-06-16] P-154/P-155/P-156/P-157 (WO-025): CLAUDE.md refreshed to reflect full post-design-task state — public surface expanded with all concrete types (`ProblemDetailsDeserializer`, `HttpResponseMessageExtensions`, `SharedKernelErrorFilter`, `PagedResponseType<T>`, `SharedKernelFilterConvention`); implementation rules expanded with handler pipeline order, per-request timeout note, interceptor exception-swallow contract, idempotency requirement for GraphQL, and complete layering-violation guard table; DI shape updated with service-discovery + REST combined example; test rules updated with `GC.GetAllocatedBytesForCurrentThread` guidance for Protobuf allocation tests
