# 11.Communication — Domain Brain

## What This Domain Is

The outbound communication capability domain. Provides platform-standard wrappers for a microservice to communicate with **other services or clients** using the four primary channels in a K8s-native microservice ecosystem: REST, gRPC, GraphQL, and in-cluster service discovery.

Every package in this domain eliminates repeated boilerplate — resilience policies, OTel propagation, correlation/tenant header injection, and service address resolution are wired once at the composition root and invisible to application code.

Philosophy: **Protocol-Agnostic Resilience. Propagate Context Always. Fail Informatively.**

> **Scope clarification:** This domain covers **outgoing** communication adapters only. Inbound concerns (ASP.NET routing, middleware, endpoint mapping) belong in `14.Presentation`. Message-bus event publishing belongs in `07.Messaging`. Cache invalidation signaling belongs in `02.Caching`.

---

## Current Phase

**All four packages fully implemented and tested (Core + Tests phases ● across Rest/Grpc/GraphQL/Internal — 315/315 tests passing).** Docs and Published phases remain `○`/`◐` Pending from original scaffolding (WO-025) — not yet fully dispatched. **P-255 (WO-041) logging retrofit — Design/Grpc/Internal/Tests all ● complete, only Docs (DO-06) remains**: `SharedKernel.Communication.Grpc` (G-14/G-15) and `SharedKernel.Communication.Internal` (I-09/I-10/I-11) — `CorrelationTracingInterceptor`/`TenantIdInterceptor` retrofitted to `[LoggerMessage]`-attributed static partial methods (EventId 11100/11101, 60/60 tests passing); `KubernetesServiceEndpointResolver`'s six delegate fields renumbered and two ad-hoc `LogDebug` calls converted, plus `StaticServiceDiscoveryStartupWarning`'s delegate converted, all to `[LoggerMessage]`-attributed static partial methods (EventId 11300-11308, 52/52 tests passing) — the platform's last mixed-authoring-style file is now clean. `SharedKernel.Communication.Rest` and `SharedKernel.Communication.GraphQL` carry zero logging today — no retrofit needed there. T-27/T-28 (regression tests + real-assembly `LoggingEventIdIntegrityAssertion`/SK0020-SK0021 verification) are now complete — see Test Rules below for the verification techniques. See the **Logging** section below for the full EventId sub-block allocation and per-method table.

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
    // Wraps body in try/catch — logs Error via [LoggerMessage] (EventId 11100,
    // LogCorrelationEnrichmentFailed) and continues on exception (never propagates).

TenantIdInterceptor  [internal sealed — Interceptor]
    // Same four call-type overrides.
    // Resolves ITenantProvider from request scope via IHttpContextAccessor.HttpContext.RequestServices.
    // Injects x-tenant-id metadata when TenantId != Guid.Empty.
    // Silent no-op when HttpContext null, ITenantProvider absent, or TenantId == Guid.Empty.
    // Same exception-swallow contract — logs Error via [LoggerMessage] (EventId 11101,
    // LogTenantIdEnrichmentFailed).

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
    // Stale-while-revalidate: on DNS failure with stale entry, logs Warning via [LoggerMessage]
    // (EventId 11304, LogStaleCacheUsed) and returns stale Uri.

AddK8sServiceDiscovery(this IServiceCollection, Action<K8sServiceDiscoveryOptions>? configure = null)
    → IServiceCollection
    // Registers KubernetesServiceEndpointResolver as IServiceEndpointResolver (singleton).
    // Wires Microsoft.Extensions.ServiceDiscovery DNS resolver.

AddStaticServiceDiscovery(this IServiceCollection, Dictionary<string, Uri> endpoints)
    → IServiceCollection
    // Registers StaticServiceEndpointResolver (singleton). Dev/test only.
    // Throws InvalidOperationException if IServiceEndpointResolver already registered.
    // Logs LogLevel.Warning at startup via StaticServiceDiscoveryStartupWarning (IHostedService),
    // via [LoggerMessage] (EventId 11308, LogStaticServiceDiscoveryActive).
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
- **`StandardResilienceHandler` lesson (DO-05):** Polly v8's circuit-breaker strategy enforces a hard validation constraint — `CircuitBreaker.SamplingDuration` must be at least `2 × AttemptTimeout.Timeout`, or `AddStandardResilienceHandler` throws at configuration time. `RestCommunicationBuilder.AddRestClient<TClient>` guards this automatically: it computes `minimumSamplingDuration = attemptTimeout × 2 + 1 tick` and silently raises `SamplingDurationSec` to that floor whenever a caller's configured value would violate the constraint (e.g. a short `TimeoutSeconds` combined with the default 30 s `SamplingDurationSec` is safe, but a long `TimeoutSeconds` paired with a short `SamplingDurationSec` is not). This auto-adjustment is silent by design — it never throws back to the caller — so consumers tuning `TimeoutSeconds` and `RestResilienceOptions.SamplingDurationSec` together should be aware the effective sampling window may be larger than the value they set.
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
| Direct `ILogger.LogXxx(...)` extension-method call or hand-written `LoggerMessage.Define<>()` delegate anywhere in `.Grpc` or `.Internal` production code | Hard violation — always author via `[LoggerMessage]` with an explicit `EventId` inside this domain's reserved sub-block; enforced by `00.Governance` SK0020/SK0021 (P-250) |
| `EventId` used outside this domain's reserved range (11000-11999) or outside its package's own 100-wide sub-block | Hard violation — verified by `00.Governance`'s `LoggingEventIdIntegrityAssertion` (P-250) once real-assembly wiring lands |

### Logging

All production log statements in this domain follow the root `CLAUDE.md` Logging Conventions: `[LoggerMessage]` source-generated partial methods only, explicit `EventId` per call site, PascalCase named template placeholders, and ambient (never explicit-placeholder) correlation/trace/tenant context. Direct `ILogger.LogXxx(...)` extension-method calls and hand-written `LoggerMessage.Define<>()` static delegates are prohibited (root CLAUDE.md, WO-041; mechanically enforced by `00.Governance` SK0020/SK0021 once P-250 ships).

`11.Communication`'s reserved block is `LoggingEventIdRanges.Communication = 11000` through `11999` (`01.Core`, P-249), subdivided into four 100-wide sub-blocks in package-declaration order:

| Sub-block | Package | Status |
| --- | --- | --- |
| 11000-11099 | `SharedKernel.Communication.Rest` | Reserved — no logging exists in this package today |
| 11100-11199 | `SharedKernel.Communication.Grpc` | In use — see table below |
| 11200-11299 | `SharedKernel.Communication.GraphQL` | Reserved — no logging exists in this package today |
| 11300-11399 | `SharedKernel.Communication.Internal` | In use — see table below |

**`SharedKernel.Communication.Grpc` EventId table:**

| EventId | Method | Level | Type | Trigger |
| --- | --- | --- | --- | --- |
| 11100 | `LogCorrelationEnrichmentFailed` | Error | `CorrelationTracingInterceptor` | `EnrichContext` catch-block — any exception during metadata enrichment; interceptor still returns the original `context` unmodified and never propagates |
| 11101 | `LogTenantIdEnrichmentFailed` | Error | `TenantIdInterceptor` | `EnrichContext` catch-block — any exception during `x-tenant-id` metadata injection; same never-propagate contract |

**`SharedKernel.Communication.Internal` EventId table:**

| EventId | Method | Level | Type | Trigger |
| --- | --- | --- | --- | --- |
| 11300 | `LogSrvLookupAttempt` | Debug | `KubernetesServiceEndpointResolver` | Before issuing the DNS SRV lookup (`_http._tcp.{service}.{namespace}.svc.{clusterDomain}`) |
| 11301 | `LogARecordLookupAttempt` | Debug | `KubernetesServiceEndpointResolver` | Before issuing the A-record lookup (`{service}.{namespace}.svc.{clusterDomain}`) fallback |
| 11302 | `LogDnsFallback` | Warning | `KubernetesServiceEndpointResolver` | Both SRV and A-record lookups failed and no stale cache entry exists — returning the K8s convention URI |
| 11303 | `LogServiceResolved` | Debug | `KubernetesServiceEndpointResolver` | DNS resolution succeeded (SRV or A-record) |
| 11304 | `LogStaleCacheUsed` | Warning | `KubernetesServiceEndpointResolver` | DNS resolution failed but a stale (expired) cache entry exists — stale-while-revalidate path |
| 11305 | `LogCacheHit` | Debug | `KubernetesServiceEndpointResolver` | Cache hit (not expired) — returned without DNS I/O |
| 11306 | `LogSrvLookupFailed` | Debug | `KubernetesServiceEndpointResolver` | SRV lookup threw (caught, non-`OperationCanceledException`) — carries the caught `Exception` |
| 11307 | `LogARecordLookupFailed` | Debug | `KubernetesServiceEndpointResolver` | A-record lookup threw (caught, non-`OperationCanceledException`) — carries the caught `Exception` |
| 11308 | `LogStaticServiceDiscoveryActive` | Warning | `StaticServiceDiscoveryStartupWarning` | Once at host startup (`IHostedService.StartAsync`) whenever `AddStaticServiceDiscovery` is registered |

- `SrvLookupFailed`/`ARecordLookupFailed` (11306/11307) are net-new `[LoggerMessage]` methods — prior to P-255 these two sites were ad-hoc `logger.LogDebug(ex, "...", serviceName)` calls that bypassed the same file's own `LoggerMessage.Define<>` delegate pattern; this was the platform's last mixed-authoring-style file (root CLAUDE.md WO-041 changelog).
- EventIds 11300-11305 and 11308 are **renumbered**, not newly introduced — they existed pre-P-255 as hand-written `LoggerMessage.Define<>` delegate fields with small, file-local numbers (1-6 and 100 respectively) that collided in spirit (not in fact, since no other package used them) with the platform's numbering discipline. Renumbering carries no behavioral change: identical `LogLevel`, identical message templates, identical call sites.
- `Rest` (11000-11099) and `GraphQL` (11200-11299) are reserved but currently unused — confirmed via source audit (P-255) that neither package logs anything today. The first log statement added to either package must draw its `EventId` from that package's own reserved sub-block, starting at the sub-block's base value.

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
- **`[LoggerMessage]` `EventId`/`Level` regression coverage (WO-041, P-255, T-27):** verify via reflection over the compiled type (`GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).GetCustomAttribute<LoggerMessageAttribute>()`, asserting `.EventId`/`.Level`) rather than only exercising the runtime call path — several call sites (DNS-failure fallback, stale-cache-used, SRV/A-record-lookup-failed) are not reachable through the pass-through `IServiceEndpointResolver` test provider (`AddPassThroughServiceEndpointProvider`, `Microsoft.Extensions.ServiceDiscovery`'s own always-succeeds test provider) without a fault-injectable DNS provider, matching the same documented limitation `TtlCacheTests` already records for its `[Trait("Category", "Integration")]`-tagged, skipped stale-cache tests. Combine with a runtime capturing-`ILogger<T>`/`TestLogSink` assertion for every call site that IS reachable (e.g. `TenantIdInterceptor`'s injected-exception path; `KubernetesServiceEndpointResolver`'s SRV-lookup-attempt/service-resolved/cache-hit paths on a successful pass-through resolution) so the retrofit is verified both statically (attribute-correct) and dynamically (actually fires) wherever feasible.
- **Real-assembly `LoggingEventIdIntegrityAssertion` invocation (WO-041, P-255, T-28):** each package's own `.Tests` project carries a **test-project-only** `ProjectReference` to `00.Governance/SharedKernel.ArchitectureTests` (never from the production `.csproj`) — mirrors the cross-domain precedent already established by `05.Application.Behaviors.Tests` and `SharedKernel.Messaging.MassTransit.Tests`. Requires bumping `FluentAssertions` to `8.10.0` in the referencing test project (`SharedKernel.ArchitectureTests` itself pins 8.10.0; a lower pin triggers `NU1605`). A cross-assembly "no EventId collision between `.Grpc` and `.Internal`" test belongs in **`SharedKernel.Communication.Grpc.Tests` only** — never `.Internal.Tests` — because `.Grpc`'s production `.csproj` already legitimately references `.Internal` (G-09 service-discovery integration); the reverse direction from `.Internal.Tests` would invert `CommunicationLayeringRules.CommunicationInternalNeverReferencesOtherCommunicationPackages`, even as a test-only reference. When a test needs `typeof(SomeInternalType).Assembly` from outside `.Internal`'s own `InternalsVisibleTo` grant, anchor on a **public** type (`IServiceEndpointResolver`, `K8sServiceDiscoveryOptions`) — not an `internal sealed` implementation type (`KubernetesServiceEndpointResolver`), which only `SharedKernel.Communication.Internal.Tests` can see.
- **SK0020/SK0021 zero-diagnostics verification (WO-041, P-255, T-28):** verified via a **temporary** analyzer `ProjectReference` on the production `.csproj` (`<ProjectReference ... OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` pointing at `00.Governance/SharedKernel.Analyzers`), built with `-p:TreatWarningsAsErrors=false` to surface every analyzer diagnostic as a warning instead of a build-breaking error, grepped for `SK0020`/`SK0021`, then **reverted** — never a permanent wiring change (same technique as `07.Messaging`'s LR-16/LR-17). This surfaced two pre-existing, unrelated findings out of scope for the logging retrofit: `KubernetesServiceEndpointResolver`'s `DateTimeOffset.UtcNow` calls (SK0001 — should inject `IClock`) and `CorrelationTracingInterceptor`'s `Guid.ToString("N")` correlation-ID fallback (SK0011 — non-canonical GUID format) — both flagged here as a candidate follow-up work order, not fixed under this phase's scope.

---

## Changelog

> Maintained by the communication domain agent. One line per significant change.

- [2026-06-16] Domain brain initialized — packages, interfaces, technology stack, implementation rules, DI shape, test rules
- [2026-06-16] P-154/P-155/P-156/P-157 (WO-025): CLAUDE.md refreshed to reflect full post-design-task state — public surface expanded with all concrete types (`ProblemDetailsDeserializer`, `HttpResponseMessageExtensions`, `SharedKernelErrorFilter`, `PagedResponseType<T>`, `SharedKernelFilterConvention`); implementation rules expanded with handler pipeline order, per-request timeout note, interceptor exception-swallow contract, idempotency requirement for GraphQL, and complete layering-violation guard table; DI shape updated with service-discovery + REST combined example; test rules updated with `GC.GetAllocatedBytesForCurrentThread` guidance for Protobuf allocation tests
- [2026-06-17] SK.11.Grpc complete (G-01–G-09, 46/46 tests): ITenantProvider used for tenant resolution in both REST and gRPC (IUserContext has no TenantId); gRPC package versions pinned (Grpc.Net.Client 2.80.0, Grpc.Net.ClientFactory 2.80.0, Google.Protobuf 3.35.1, Google.Api.CommonProtos 2.17.0); InterceptorScope in Grpc.Net.ClientFactory namespace; namespace alias pattern for Grpc.Core collision; ServiceConfig/RetryPolicy in Grpc.Net.Client.Configuration; GetAwaiter().GetResult() pattern for address resolution at channel creation; Metadata clone-and-add pattern documented
- [2026-06-17] SK.11.GraphQL complete (GQ-01–GQ-08, 40/40 tests): HotChocolate upgraded to v16.1.4 (v14 incompatible with net10.0); DefaultFilterOperations uses LowerThan/LowerThanOrEquals; IPage replaces CollectionSegment for offset paging; test schema types must be public; ExpectOperationResult() required to access Errors; sentinel-marker idempotency pattern documented
- [2026-06-18] SK.11.Internal complete (I-01–I-06, 27/27 tests): IServiceEndpointResolver, KubernetesServiceEndpointResolver (DNS SRV + A-record fallback), StaticServiceEndpointResolver (dev/test), AddK8sServiceDiscovery, AddStaticServiceDiscovery — all implemented and tested
- [2026-06-18] WO-026 P-160–P-165: 23 new tasks added across Rest (R-11–R-18), Grpc (G-10–G-13), GraphQL (GQ-09), Internal (I-07–I-08), Tests (T-19–T-26); key design decisions recorded — static readonly JsonSerializerOptions for fallback path, RestClientOptionsValidator registration required, resolver-presence sentinel-bool pattern, ServiceDiscoveryResolvingHandler inline-factory fix (per-client closure), RestClientOptions.ServiceName override, `ReadEnvelopeAsync<T>` boundary bridge (JsonTypeInfo + JsonSerializerOptions overloads), GrpcMetadataHelper extraction, `AddGrpcClient<TClient>` address param changed to `string? = null`, 04.Contracts reference removed from .Grpc csproj, TTL endpoint cache in KubernetesServiceEndpointResolver (ConcurrentDictionary + stale-while-revalidate), `PagedResponseType<T>.FromPagedList` factory; layering violation guard table expanded with 8 new entries
- [2026-07-09] P-255 (WO-041) applied — logging retrofit to the platform `[LoggerMessage]` standard for `.Grpc` and `.Internal`. Source audit confirmed the violation inventory: `CorrelationTracingInterceptor`/`TenantIdInterceptor` call `_logger.LogError(ex, "...")` directly; `KubernetesServiceEndpointResolver` mixes six hand-written `LoggerMessage.Define<>` delegates (local EventId 1-6) with two ad-hoc `logger.LogDebug(ex, "...", serviceName)` calls in the same file — the platform's last mixed-authoring-style file; `StaticServiceDiscoveryStartupWarning` has one hand-written `LoggerMessage.Define<int>` delegate (local EventId 100). `.Rest` and `.GraphQL` confirmed to carry zero logging today — their 100-wide sub-blocks (11000-11099, 11200-11299) are reserved but unused. New "Logging" section added documenting the full EventId sub-block allocation (`LoggingEventIdRanges.Communication` = 11000, subdivided in package-declaration order: Rest/Grpc/GraphQL/Internal) and the explicit per-method EventId table for `.Grpc` (11100-11101) and `.Internal` (11300-11308); Interface Contracts updated to reference the new EventIds; layering-violation guard table gained two new hard-violation rows (direct ILogger/LoggerMessage.Define usage; EventId outside reserved range/sub-block); 9 new tasks added to state-map.md (D-23, G-14/G-15, I-09/I-10/I-11, T-27/T-28, DO-06); depends on `01.Core` P-249 (`LoggingEventIdRanges`) and `00.Governance` P-250 (SK0020/SK0021, `LoggingEventIdIntegrityAssertion`), both `○` Pending as of this phase — design proceeds unblocked, only T-28's real-assembly verification is gated on those two phases shipping (communication-arch-planner, WO-041)
- [2026-07-13] SK.11.Grpc (G-14, G-15, P-255) complete — `CorrelationTracingInterceptor`/`TenantIdInterceptor` retrofitted to `[LoggerMessage]`-attributed static partial methods (EventId 11100/11101), preserving exact catch/log/continue semantics; 55/55 `SharedKernel.Communication.Grpc.Tests` passing; `.Internal` retrofit (I-09–I-11) remains pending (communication-phase-implementer)
- [2026-07-13] SK.11.Internal (I-09, I-10, I-11, P-255) complete — `KubernetesServiceEndpointResolver`'s six hand-written `LoggerMessage.Define<>` delegates renumbered to `[LoggerMessage]`-attributed static partial methods (11300-11305), two ad-hoc `LogDebug` calls converted to new `LogSrvLookupFailed`/`LogARecordLookupFailed` (11306/11307), `StaticServiceDiscoveryStartupWarning`'s delegate converted to `LogStaticServiceDiscoveryActive` (11308); identical `LogLevel`/message templates/call sites — no behavioral change; 39/39 `SharedKernel.Communication.Internal.Tests` passing; P-255's remaining scope is Tests (T-27/T-28) and Docs (DO-06) (communication-phase-implementer)
- [2026-07-13] SK.11.Tests (T-27, T-28, P-255) complete — confirmed both cross-domain blockers (`01.Core` P-249, `00.Governance` P-250/`SK.00.LoggingStandardEnforcement`) shipped before proceeding. T-27: new `LoggingEventIdRegressionTests.cs` in both `.Grpc.Tests`/`.Internal.Tests` — reflection-based `[LoggerMessage]` attribute EventId/Level checks for all 9 call sites, plus runtime `TestLogSink`/capturing-`ILogger<T>` assertions for the call sites reachable via the pass-through `IServiceEndpointResolver` test provider (SRV-lookup-attempt, service-resolved, cache-hit; `TenantIdInterceptor`'s injected-exception path) — the DNS-failure/stale-cache paths remain untestable in unit scope without a fault-injectable DNS provider, matching `TtlCacheTests`' existing documented limitation; existing `AddStaticServiceDiscovery_StartupWarning_LogsAtWarningLevel` extended with an `EventId.Id == 11308` check (no behavioral change). T-28: new `Governance/LoggingEventIdIntegrityRealAssemblyTests.cs` in both test projects (test-project-only `ProjectReference` to `00.Governance/SharedKernel.ArchitectureTests`; `FluentAssertions` bumped 8.4.0→8.10.0) invoking `LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange` against the real compiled assemblies — passes; the Grpc↔Internal cross-assembly no-collision check lives in `.Grpc.Tests` only, never `.Internal.Tests`, to avoid inverting `CommunicationLayeringRules.CommunicationInternalNeverReferencesOtherCommunicationPackages` (Grpc's production `.csproj` already legitimately references `.Internal`); anchored on the public `IServiceEndpointResolver` type rather than the `internal` `KubernetesServiceEndpointResolver` (no `InternalsVisibleTo` grant to `.Grpc.Tests`). SK0020/SK0021 zero-diagnostics verified via a temporary analyzer `ProjectReference` on both production `.csproj` files (built with `-p:TreatWarningsAsErrors=false`, then reverted) — zero SK0020/SK0021 findings; surfaced two pre-existing, unrelated findings out of this phase's scope (SK0001 on `KubernetesServiceEndpointResolver`'s `DateTimeOffset.UtcNow`; SK0011 on `CorrelationTracingInterceptor`'s `Guid.ToString("N")`), flagged as a candidate follow-up, not fixed. `.Grpc.Tests` now 60/60 passing (up from 55); `.Internal.Tests` now 52/52 passing (up from 39). `SK.11.Tests` now 28/28 ● — only DO-06 (Docs) remains for P-255 (communication-phase-implementer)
- [2026-07-14] SK.11.Docs (DO-01–DO-06) complete — audited all four packages' public surface against this file's Interface Contracts and confirmed complete XML doc coverage already existed from prior implementation phases; mechanically verified via `dotnet build -c Release` on all four production `.csproj` (each carries `GenerateDocumentationFile=true` + `TreatWarningsAsErrors=true`, so a missing public-member doc comment fails the build with CS1591) — all four built 0 warnings/0 errors. DO-05: added a new REST-rules bullet documenting the Polly v8 `StandardResilienceHandler` constraint that `CircuitBreaker.SamplingDuration` must be ≥ 2× `AttemptTimeout.Timeout`, and that `RestCommunicationBuilder.AddRestClient<TClient>` silently auto-raises `SamplingDurationSec` to satisfy it — previously undocumented despite being implemented since the original Rest phase. DO-06: verified (no changes needed) — the existing Logging section (added 2026-07-09) was re-checked line-by-line against the shipped `[LoggerMessage]` source in `.Grpc`/`.Internal`; all 9 EventIds/levels/triggers match exactly, no stale hand-written-delegate or ad-hoc-`LogDebug` references remain. Only the Published phase (NuGet packaging metadata, pack, publish) remains for this domain (communication-phase-implementer)
