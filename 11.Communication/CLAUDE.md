# 11.Communication — Domain Brain

## What This Domain Is

The outbound communication capability domain. Provides platform-standard wrappers for a microservice to communicate with **other services or clients** using the four primary channels in a K8s-native microservice ecosystem: REST, gRPC, GraphQL, and in-cluster service discovery.

Every package in this domain eliminates repeated boilerplate — resilience policies, OTel propagation, correlation/tenant header injection, and service address resolution are wired once at the composition root and invisible to application code.

Philosophy: **Protocol-Agnostic Resilience. Propagate Context Always. Fail Informatively.**

> **Scope clarification:** This domain covers **outgoing** communication adapters only. Inbound concerns (ASP.NET routing, middleware, endpoint mapping) belong in `14.Presentation`. Message-bus event publishing belongs in `07.Messaging`. Cache invalidation signaling belongs in `02.Caching`.

---

## Current Phase

**All four initial packages complete. WO-026 correctness and quality fixes in progress (○).** Rest (10/18 ◐), Grpc (9/13 ◐), GraphQL (8/9 ◐), Internal (6/8 ◐) — 23 new tasks added by P-160–P-165 (WO-026): 4 REST correctness fixes, `ReadEnvelopeAsync<T>` boundary bridge, `ServiceDiscoveryResolvingHandler` per-client bug fix, 3 gRPC improvements (`GrpcMetadataHelper` extraction, optional address parameter, dead-reference removal), TTL endpoint cache in `KubernetesServiceEndpointResolver`, and `PagedResponseType<T>.FromPagedList` factory.

---

## Packages

| Package | Role | NuGet / Project References |
| ------- | ---- | -------------------------- |
| `SharedKernel.Communication.Rest` | Typed `HttpClient` factory with Polly v8 resilience (`StandardResilienceHandler`: retry, circuit breaker, timeout), `CorrelationIdDelegatingHandler`, `TenantIdDelegatingHandler`, `ProblemDetailsDeserializer` → `Error` mapping, `HttpResponseMessageExtensions.EnsureSuccessOrErrorAsync`, and fluent `IRestCommunicationBuilder` DI entry point | `01.Core`, `04.Contracts`, `12.Security.Abstractions`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience` |
| `SharedKernel.Communication.Grpc` | gRPC channel factory via `Grpc.Net.ClientFactory`, `CorrelationTracingInterceptor` (W3C OTel + `x-correlation-id`), `TenantIdInterceptor` (`x-tenant-id`), `GrpcMetadataHelper` (internal shared helper for metadata ops), `MoneyProtoExtensions` (`Money ↔ decimal`), `TimestampProtoExtensions` (`Timestamp ↔ DateTimeOffset`), and fluent `IGrpcCommunicationBuilder` DI entry point | `01.Core`, `12.Security.Abstractions`, `Grpc.Net.Client`, `Grpc.Net.ClientFactory`, `OpenTelemetry.Instrumentation.GrpcNetClient` — note: `04.Contracts` reference removed (P-163); gRPC uses Protobuf-generated types directly |
| `SharedKernel.Communication.GraphQL` | HotChocolate v16 server-side conventions: `SharedKernelFilterConvention` (snake_case filter operations), `FilterBase<T>` + `SortBase<T>` abstract base types, `PagedResponseType<T>` (`TotalCount + Items`), `SharedKernelErrorFilter` (`IError` → `ProblemDetails` shape), and `AddSharedKernelGraphQL` DI entry point | `01.Core`, `04.Contracts`, `HotChocolate.Data`, `HotChocolate.AspNetCore` |
| `SharedKernel.Communication.Internal` | `IServiceEndpointResolver` interface, `KubernetesServiceEndpointResolver` (DNS SRV + A-record, via `Microsoft.Extensions.ServiceDiscovery`), `StaticServiceEndpointResolver` (dev/test only), `K8sServiceDiscoveryOptions`, `AddK8sServiceDiscovery` and `AddStaticServiceDiscovery` DI extensions | `01.Core`, `Microsoft.Extensions.ServiceDiscovery` |

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

**Layering rule (from root CLAUDE.md):** `11.Communication` may reference `01.Core`, `04.Contracts`, and `12.Security` abstractions only. It must never reference `02.Caching`, `05.Application`, `06.Persistence`, or `07.Messaging`.

---

## Technology Stack

| Concern | Technology | Confirmed Version | Owning Package |
| ------- | ---------- | ----------------- | -------------- |
| Typed HTTP client DI | `Microsoft.Extensions.Http` | 10.0.0 | `.Rest` |
| HTTP resilience (Polly v8) | `Microsoft.Extensions.Http.Resilience` | 9.x / 10.x | `.Rest` |
| gRPC client | `Grpc.Net.Client` | 2.80.0 | `.Grpc` |
| gRPC DI factory | `Grpc.Net.ClientFactory` | 2.80.0 | `.Grpc` |
| gRPC Protobuf | `Google.Protobuf` | 3.35.1 | `.Grpc` |
| gRPC common protos (Money, Timestamp) | `Google.Api.CommonProtos` | 2.17.0 | `.Grpc` |
| gRPC OTel instrumentation | `OpenTelemetry.Instrumentation.GrpcNetClient` | 1.15.1-beta.1 | `.Grpc` |
| GraphQL server | `HotChocolate.AspNetCore` | 16.1.4 | `.GraphQL` |
| GraphQL filtering/sorting | `HotChocolate.Data` | 16.1.4 | `.GraphQL` |
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
    ServiceName          string?             // default null — overrides name param for DNS lookup when set
    TimeoutSeconds       int                 // default 30 — per-request timeout
    Resilience           RestResilienceOptions

RestResilienceOptions  (sealed class)
    RetryCount           int                 // default 3
    RetryBaseDelayMs     int                 // default 500 — exponential backoff base
    CircuitBreakerEnabled bool               // default true
    FailureThreshold     int                 // default 5 failures before CB opens
    SamplingDurationSec  int                 // default 30 — failure counting window
    BreakDurationSec     int                 // default 30 — CB open duration
    TotalTimeoutBufferSec int                // default 10, minimum 0 — added to TimeoutSeconds × (RetryCount + 1)
                                             // to compute TotalRequestTimeout; provides headroom for jitter
                                             // and circuit-breaker probe time; set to 0 for tight latency budgets

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
    // Falls back to static readonly reflection-based JsonSerializerOptions (initialized once at class load).
    // Maps: type → Error.Code, detail ?? title → Error.Message.

HttpResponseMessageExtensions  [public static]
    EnsureSuccessOrErrorAsync<T>(this HttpResponseMessage, CancellationToken)
        → Task<Result<T>>
    // 2xx → Result.Ok; non-2xx → Result.Fail(Error) via ProblemDetailsDeserializer.

    ReadEnvelopeAsync<T>(this HttpResponseMessage, JsonTypeInfo<T> typeInfo, CancellationToken)
        → Task<Envelope<T>>
    // AOT-safe primary path. 2xx → deserialize body using typeInfo → Envelope<T>.Ok(value).
    // 2xx with null/empty body → Envelope<T>.Fail(Error.Unexpected("http.empty-body", ...)).
    // non-2xx → ProblemDetailsDeserializer.DeserializeAsync → Envelope<T>.Fail(error).

    ReadEnvelopeAsync<T>(this HttpResponseMessage, JsonSerializerOptions? options, CancellationToken)
        → Task<Envelope<T>>
    // Reflection-based fallback overload. Same success/empty/failure logic as above.
    // Uses static readonly JsonSerializerOptions from ProblemDetailsDeserializer when options is null.

AddSharedKernelRestCommunication(this IServiceCollection) → IRestCommunicationBuilder
    // Entry point. Registers IRestCommunicationBuilder, both delegation handlers (transient),
    // STJ ProblemDetailsJsonContext, and RestClientOptionsValidator
    // (services.AddSingleton<IValidateOptions<RestClientOptions>, RestClientOptionsValidator>()).
```

### `SharedKernel.Communication.Grpc` — public surface

```text
IGrpcCommunicationBuilder
    Services  IServiceCollection { get; }
    AddGrpcClient<TClient>(string? address = null, Action<GrpcClientOptions>? configure = null)
        → IGrpcCommunicationBuilder          // returns this — fluent chaining
        // address is optional when IServiceEndpointResolver is registered; throws
        // InvalidOperationException when neither address nor resolver is present.

GrpcClientOptions  (sealed class)
    Address              string?             // optional when IServiceEndpointResolver registered
    DeadlineSeconds      int                 // default 30 — per-call deadline
    EnableRetry          bool                // default true

GrpcMetadataHelper  [internal static]
    // Shared helper used by CorrelationTracingInterceptor and TenantIdInterceptor.
    HasMetadataEntry(Metadata metadata, string key) → bool   // case-insensitive key match
    CloneAndAdd(Metadata metadata, string key, string value) → Metadata
    // Returns new Metadata instance; all existing entries copied; new entry appended;
    // original Metadata not mutated.

CorrelationTracingInterceptor  [internal sealed — Interceptor]
    // Overrides AsyncUnaryCall, AsyncServerStreamingCall, AsyncClientStreamingCall,
    // AsyncDuplexStreamingCall.
    // Reads Activity.Current at call time (not DI registration time).
    // Injects traceparent (W3C format), tracestate, and x-correlation-id into metadata.
    // Does not overwrite existing x-correlation-id entry.
    // Wraps body in try/catch — logs Error and continues on exception (never propagates).

TenantIdInterceptor  [internal sealed — Interceptor]
    // Same four call-type overrides.
    // Resolves ITenantProvider from request scope via IHttpContextAccessor.HttpContext.RequestServices.
    // Injects x-tenant-id metadata when TenantId != Guid.Empty.
    // Silent no-op when HttpContext null, ITenantProvider absent, or TenantId == Guid.Empty.
    // Same exception-swallow + Error-log contract.

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
    // HC v16: offset paging source is IPage (not CollectionSegment<T>); cursor source is Connection<T>.
    // FromPage(IPage) — uses IPageTotalCountProvider.TotalCount + IPage.Items.OfType<T>().
    // FromConnection(Connection<T>) — uses connection.Edges.Select(e => e.Node).
    // From(IReadOnlyList<T>, int) — convenience factory for manual assembly.
    // FromPagedList(PagedList<T> pagedList) — bridge factory from 04.Contracts PagedList<T>;
    //   guards ArgumentNullException.ThrowIfNull; maps Items and TotalCount directly.
    //   Use when resolver receives PagedList<T> from application layer.
    //   Use FromPage/FromConnection for HotChocolate-paged sources.
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
    EndpointCacheTtlSeconds int              // default 30; 0 = no caching; negative = validator rejects
    // TTL-based in-memory endpoint cache. KubernetesServiceEndpointResolver uses a ConcurrentDictionary
    // keyed by serviceName (OrdinalIgnoreCase) with CachedEntry { Uri, DateTimeOffset ExpiresAt }.
    // Stale-while-revalidate: on DNS failure with stale entry, logs Warning and returns stale Uri.

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
- `TenantIdDelegatingHandler` must resolve `ITenantProvider` from the **request scope** via `IHttpContextAccessor.HttpContext.RequestServices` — injecting `ITenantProvider` directly into the handler constructor would capture the wrong scope. Note: `IUserContext` (which carries user identity) does not expose `TenantId`; use `ITenantProvider` for tenant resolution in both REST and gRPC.
- `BaseAddress` on `RestClientOptions` is the only allowed way to set the base URI — callers must never hardcode URIs inside typed client methods.
- ProblemDetails deserialization uses STJ source-generated `ProblemDetailsJsonContext` in the primary path; reflection-based STJ is the fallback only.
- The reflection-based STJ fallback in `ProblemDetailsDeserializer` uses a **`static readonly JsonSerializerOptions`** field initialized once at class load — never allocate `new JsonSerializerOptions()` per call (hot-path GC violation).
- The handler pipeline order is fixed: `CorrelationIdDelegatingHandler` → `TenantIdDelegatingHandler` → `StandardResilienceHandler` → transport.
- `TimeoutSeconds` is applied as a per-request timeout via `StandardResilienceHandler`, not as a global `HttpClient.Timeout`.
- `TotalRequestTimeout` formula: `TimeoutSeconds × (RetryCount + 1) + TotalTimeoutBufferSec`. The buffer (default 10 s) accounts for jitter headroom and circuit-breaker probe time. Consumers can set `TotalTimeoutBufferSec = 0` for tight latency budgets.
- `RestClientOptionsValidator` **must** be registered in `AddSharedKernelRestCommunication` — it must never be left unregistered silently.
- `RestCommunicationBuilder` captures resolver presence as `bool _resolverRegistered` at construction time (`Services.Any(...)` must not be called per `AddRestClient<TClient>` invocation).
- `ServiceDiscoveryResolvingHandler` must **never** be registered as a shared DI type when service discovery is used. Each typed client gets its own instance via an inline `AddHttpMessageHandler(sp => new ServiceDiscoveryResolvingHandler(..., capturedName))` factory closure.
- `RestClientOptions.ServiceName` (nullable `string?`) overrides the `name` parameter for DNS lookup when set. When `null`, the `name` parameter is used. Document clearly in all new client registration examples.

### gRPC rules

- All interceptors are registered globally via `AddGrpcClient<T>().AddInterceptor<T>(InterceptorScope.Channel)` — no per-call interceptor injection. `InterceptorScope` is in the `Grpc.Net.ClientFactory` namespace.
- `CorrelationTracingInterceptor` must read `Activity.Current` at the **moment of the call**, not at DI registration time.
- Interceptors must catch **all** exceptions, log at `Error` level, and continue — they must never propagate exceptions into the gRPC call pipeline.
- `MoneyProtoExtensions` and `TimestampProtoExtensions` are pure, static, and allocation-minimal — no `new()` allocations for conversion; no `ToString()`-based intermediate representations. Money conversion: `Units` (int64) + `Nanos` (int32, billionths) where `NanosPerUnit = 1_000_000_000`.
- `GrpcChannel` instances are expensive — rely on `Grpc.Net.ClientFactory` channel caching (singleton pattern); never create a new `GrpcChannel.ForAddress()` per call.
- TLS is configured at the channel level only (`ChannelCredentials.Insecure` for HTTP, `SslCredentials` for HTTPS) — no per-call TLS configuration.
- Caller-supplied `x-correlation-id` or `x-tenant-id` metadata entries must never be overwritten by the interceptors.
- gRPC retry policy is configured via `ServiceConfig` / `MethodConfig` / `RetryPolicy` in the `Grpc.Net.Client.Configuration` namespace — not via Polly. Wired into `GrpcChannelOptions.ServiceConfig` on the channel options.
- **Namespace alias required:** The project namespace `SharedKernel.Communication.Grpc` collides with the `Grpc.Core` NuGet namespace. Always add `using GrpcCore = Grpc.Core;` in files that reference both.
- When `IServiceEndpointResolver` is present and `Address` is omitted: resolve address synchronously at channel creation using `.GetAwaiter().GetResult()` inside the `AddGrpcClient<T>((sp, o) => ...)` factory action. This is safe because (a) `KubernetesServiceEndpointResolver.ResolveAsync` never throws, and (b) the channel is a singleton created once.
- gRPC `Metadata` entries must be cloned before adding new entries to avoid mutating the original `CallOptions.Headers`. Use `GrpcMetadataHelper.CloneAndAdd` — the single authoritative implementation.
- `GrpcMetadataHelper` is the sole location for `HasMetadataEntry` and `CloneAndAdd` logic. Neither interceptor may define its own local copy of these helpers — all metadata manipulation delegates to `GrpcMetadataHelper`.
- The `SharedKernel.Contracts` (`04.Contracts`) project reference must **not** appear in `SharedKernel.Communication.Grpc.csproj`. The gRPC package uses Protobuf-generated types directly; `Envelope<T>`, `PagedList<T>`, and `EventEnvelope<T>` have no place in gRPC package code.

### GraphQL rules

- `AddSharedKernelGraphQL` must be called **before** any service-specific `AddGraphQL()` / `AddTypes()` calls — it establishes the base convention all types inherit.
- `AllowIntrospection` must be `false` in non-development environments — consuming services are responsible for environment-gating this flag in their `Program.cs`.
- `FilterBase<T>` and `SortBase<T>` are mandatory base classes. Direct registration of `FilterInputType<T>` or `SortInputType<T>` without the base wrapper is a platform violation.
- GraphQL error responses must map to the same `ProblemDetails` shape as REST responses — `SharedKernelErrorFilter` handles this automatically when registered via `AddSharedKernelGraphQL`.
- `MaxPageSize` default is 100. Hard cap is 500 — `GraphQLOptions` validator rejects values above 500. Any override beyond 500 requires documented justification in the consuming service.
- `AddSharedKernelGraphQL` is idempotent — calling it twice does not double-register conventions, error filters, or pagination settings.
- HotChocolate v16 is **not AOT-safe** — do not add `<IsAotCompatible>true</IsAotCompatible>` to `SharedKernel.Communication.GraphQL.csproj` or any consuming project that references it.
- `DefaultFilterOperations` constants use `LowerThan` (= 20) and `LowerThanOrEquals` (= 22) — **not** `LessThan`/`LessThanOrEquals` (those names do not exist in HC v16).
- HC v16 offset paging returns `IPage` (not `CollectionSegment<T>`); use `IPageTotalCountProvider.TotalCount` for count and `IPage.Items.OfType<T>()` for typed items.
- `InputField.Name` is `string` in HC v16 (not `NameString`) — use `f.Name` directly, never `.Value`.
- `IExecutionResult` does not expose `.Errors` directly; cast via `result.ExpectOperationResult()` to get `OperationResult` with `.Errors`.
- Schema types used in HC v16 test schema builders must be **public** — private or private-nested classes are not discoverable by HC reflection and cause `SchemaException: Unable to infer or resolve a schema type`.
- Register custom filter convention via `.AddFiltering<SharedKernelFilterConvention>()` (from `HotChocolateDataRequestBuilderExtensions`); add cursor paging support via `.AddQueryableCursorPagingProvider()`.
- Idempotency is guarded via a private sentinel `SharedKernelGraphQLRegistrationMarker` class registered as a singleton; second call to `AddSharedKernelGraphQL` returns early.

### Service discovery rules

- `IServiceEndpointResolver` must always be injected into typed clients that need service address resolution — never construct `Uri` from environment variables or `IConfiguration` directly inside typed client methods.
- `KubernetesServiceEndpointResolver` attempts DNS SRV lookup (`_http._tcp.<service>.<namespace>.svc.<clusterDomain>`) first; falls back to A-record for headless services; returns K8s convention URI on full resolution failure.
- `ResolveAsync` must **never throw** for an unresolvable service name in production — it returns a non-null `Uri` using the K8s DNS convention and lets the caller's transport surface the connection error.
- `StaticServiceEndpointResolver` must only be registered in non-production environments — `AddStaticServiceDiscovery` logs `LogLevel.Warning` at startup.
- `AddStaticServiceDiscovery` throws `InvalidOperationException` if `IServiceEndpointResolver` is already registered — prevents silent resolver replacement in misconfigured environments.
- `StaticServiceEndpointResolver.ResolveAsync` returns the K8s convention URI for unknown service names (never throws) to maintain behavioural parity with `KubernetesServiceEndpointResolver`.
- `KubernetesServiceEndpointResolver` implements a TTL-based in-memory endpoint cache controlled by `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds` (default 30, 0 = disabled). The cache uses `ConcurrentDictionary<string, CachedEntry>` keyed case-insensitively. Stale-while-revalidate: DNS failure with stale entry logs `Warning` and returns stale `Uri` (never throws). DNS failure with no prior entry falls through to K8s convention URI.
- `K8sServiceDiscoveryOptionsValidator` rejects negative `EndpointCacheTtlSeconds` values. Zero is valid (disables caching).
- Both `RestCommunicationBuilder` and `GrpcCommunicationBuilder` capture resolver presence as a `bool` field at construction time — `Services.Any(d => d.ServiceType == typeof(IServiceEndpointResolver))` must only ever be called once, at builder construction, not per client registration.

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
| `TenantIdDelegatingHandler` or `TenantIdInterceptor` receives `ITenantProvider` in its constructor directly | Hard violation — must resolve from request scope via `IHttpContextAccessor.HttpContext.RequestServices` |
| `AddRestClient<TClient>` without `StandardResilienceHandler` in the pipeline | Hard violation |
| Typed client method contains a hardcoded `Uri` or `BaseAddress` string | Hard violation |
| gRPC interceptor propagates an exception into the call stack | Hard violation — must catch, log `Error`, continue |
| `AddStaticServiceDiscovery` used in a production environment | Hard violation |
| `ResolveAsync` throws for an unresolvable name | Hard violation |
| `FilterInputType<T>` or `SortInputType<T>` registered without `FilterBase<T>` / `SortBase<T>` wrapper | Platform violation |
| `<IsAotCompatible>true</IsAotCompatible>` on `.GraphQL` project | Hard violation — HotChocolate v16 not AOT-safe |
| `MaxPageSize` set above 500 without documented justification | Violation |
| Reflection-based STJ used as primary ProblemDetails deserialization path (not as fallback) | Violation |
| `new JsonSerializerOptions()` allocated per call inside `ProblemDetailsDeserializer` | Violation — must be `static readonly` |
| `RestClientOptionsValidator` not registered in `AddSharedKernelRestCommunication` | Hard violation — options validation silently absent |
| `Services.Any(d => ...)` called inside `AddRestClient` or `AddGrpcClient` per-registration (O(n) probe) | Violation — resolver presence captured once at builder construction |
| `ServiceDiscoveryResolvingHandler` registered as a shared DI type when multiple clients need distinct service names | Hard violation — per-client closure factory required |
| `SharedKernel.Contracts` project reference in `SharedKernel.Communication.Grpc.csproj` | Violation — gRPC package must not reference 04.Contracts |
| `CorrelationTracingInterceptor` or `TenantIdInterceptor` defining local `HasMetadataEntry` or `CloneAndAdd` | Violation — must delegate to `GrpcMetadataHelper` |
| Negative `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds` | Violation — validator must reject |

### AOT compatibility

- Prefer STJ source-generated `ProblemDetailsJsonContext` for ProblemDetails deserialization in `.Rest`. Reflection-STJ is the fallback, not the default.
- Avoid `dynamic` or reflection-based Protobuf serialization in `.Grpc` — use generated code only.
- HotChocolate v16 in `.GraphQL` is **not AOT-safe** — `<IsAotCompatible>true</IsAotCompatible>` must never be added.
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

// REST + service discovery with ServiceName override (DNS name differs from logical registration name)
services.AddSharedKernelRestCommunication()
        .AddRestClient<IInventoryServiceClient>("inventory-client", options => {
            // ServiceName drives DNS lookup; "inventory-client" is just the DI registration name
            options.ServiceName = "inventory-service";
            options.Resilience.RetryCount = 2;
        });

// ReadEnvelopeAsync<T> single-call boundary mapping (inside a typed client method)
// var response = await _httpClient.GetAsync("/api/orders/123", ct);
// return await response.ReadEnvelopeAsync<OrderDto>(OrderJsonContext.Default.OrderDto, ct);
// On 2xx: Envelope<OrderDto>.Ok(dto); on non-2xx: Envelope<OrderDto>.Fail(error)

// K8s discovery with TTL cache tuned for tight latency (disable buffer)
services.AddK8sServiceDiscovery(options => {
    options.Namespace = "production";
    options.EndpointCacheTtlSeconds = 60;  // refresh endpoints every 60 s
});
services.AddSharedKernelRestCommunication()
        .AddRestClient<IOrderServiceClient>(options => {
            options.TimeoutSeconds = 5;
            options.Resilience.RetryCount = 1;
            options.Resilience.TotalTimeoutBufferSec = 0;  // tight budget: no jitter buffer
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
- [2026-06-17] SK.11.Grpc complete (G-01–G-09, 46/46 tests): ITenantProvider used for tenant resolution in both REST and gRPC (IUserContext has no TenantId); gRPC package versions pinned (Grpc.Net.Client 2.80.0, Grpc.Net.ClientFactory 2.80.0, Google.Protobuf 3.35.1, Google.Api.CommonProtos 2.17.0); InterceptorScope in Grpc.Net.ClientFactory namespace; namespace alias pattern for Grpc.Core collision; ServiceConfig/RetryPolicy in Grpc.Net.Client.Configuration; GetAwaiter().GetResult() pattern for address resolution at channel creation; Metadata clone-and-add pattern documented
- [2026-06-17] SK.11.GraphQL complete (GQ-01–GQ-08, 40/40 tests): HotChocolate upgraded to v16.1.4 (v14 incompatible with net10.0); DefaultFilterOperations uses LowerThan/LowerThanOrEquals; IPage replaces CollectionSegment for offset paging; test schema types must be public; ExpectOperationResult() required to access Errors; sentinel-marker idempotency pattern documented
- [2026-06-18] SK.11.Internal complete (I-01–I-06, 27/27 tests): IServiceEndpointResolver, KubernetesServiceEndpointResolver (DNS SRV + A-record fallback), StaticServiceEndpointResolver (dev/test), AddK8sServiceDiscovery, AddStaticServiceDiscovery — all implemented and tested
- [2026-06-18] WO-026 P-160–P-165: 23 new tasks added across Rest (R-11–R-18), Grpc (G-10–G-13), GraphQL (GQ-09), Internal (I-07–I-08), Tests (T-19–T-26); key design decisions recorded — static readonly JsonSerializerOptions for fallback path, RestClientOptionsValidator registration required, resolver-presence sentinel-bool pattern, ServiceDiscoveryResolvingHandler inline-factory fix (per-client closure), RestClientOptions.ServiceName override, `ReadEnvelopeAsync<T>` boundary bridge (JsonTypeInfo + JsonSerializerOptions overloads), GrpcMetadataHelper extraction, `AddGrpcClient<TClient>` address param changed to `string? = null`, 04.Contracts reference removed from .Grpc csproj, TTL endpoint cache in KubernetesServiceEndpointResolver (ConcurrentDictionary + stale-while-revalidate), `PagedResponseType<T>.FromPagedList` factory; layering violation guard table expanded with 8 new entries
