# 11.Communication — State Map

> **What this file is:** Phase and task tracker for all work within `11.Communication`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.11.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition | Root Backlog ID |
| --- | --- | --- | --- |
| `SK.11.Design` | Design | All tasks in Phase: Design are `●` | — |
| `SK.11.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` | — |
| `SK.11.Rest` | Rest | All tasks in Phase: Rest are `●` | P-154 |
| `SK.11.Grpc` | Grpc | All tasks in Phase: Grpc are `●` | P-156 |
| `SK.11.GraphQL` | GraphQL | All tasks in Phase: GraphQL are `●` | P-157 |
| `SK.11.Internal` | Internal | All tasks in Phase: Internal are `●` | P-155 |
| `SK.11.Tests` | Tests | All tasks in Phase: Tests are `●` | — |
| `SK.11.Docs` | Docs | All tasks in Phase: Docs are `●` | — |
| `SK.11.Published` | Published | All tasks in Phase: Published are `●` | — |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement CorrelationIdDelegatingHandler | SK.11.Rest | SharedKernel.Communication.Rest | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.11.Rest | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Communication.Rest` | Rest | `●` | P-154: Typed HttpClient factory, Polly v8, correlation + tenant handlers, ProblemDetails deserialization |
| `SharedKernel.Communication.Grpc` | Grpc | `●` | P-156: gRPC channel factory, OTel tracing + tenant interceptors, Protobuf helpers |
| `SharedKernel.Communication.GraphQL` | GraphQL | `●` | P-157: HotChocolate v14 conventions, FilterBase/SortBase, pagination, error mapping |
| `SharedKernel.Communication.Internal` | Internal | `●` | P-155: IServiceEndpointResolver, K8s DNS resolver, static dev resolver — 27/27 tests passing |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.11.Rest` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Error`, `IClock`) | Available |
| `SK.11.Rest` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference (`Envelope`, `PagedList`) | Available |
| `SK.11.Rest` | `12.Security` | `SharedKernel.Security.Abstractions` ProjectReference (`IUserContext` — for tenant ID header injection) | Available |
| `SK.11.Grpc` | `01.Core` | `SharedKernel.Primitives` ProjectReference | Available |
| `SK.11.Grpc` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference | Available |
| `SK.11.Grpc` | `12.Security` | `SharedKernel.Security.Abstractions` ProjectReference (`IUserContext` — for tenant ID metadata injection) | Available |
| `SK.11.GraphQL` | `01.Core` | `SharedKernel.Primitives` ProjectReference | Available |
| `SK.11.GraphQL` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference (`PagedList` — for pagination shape parity) | Available |
| `SK.11.Internal` | `01.Core` | `SharedKernel.Primitives` ProjectReference | Available |

---

## Phase: Design <!-- phase-key: SK.11.Design -->

> Finalize all interface shapes, builder API contracts, resilience option models, interceptor behaviour rules, GraphQL convention configuration, and service discovery contracts before implementation begins.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| D-01 | Finalize `IRestCommunicationBuilder` contract: `AddRestClient<TClient>` signature, chaining return type, and builder lifetime | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| D-02 | Finalize `RestClientOptions` and `RestResilienceOptions` field list, defaults, and validation rules (retry 3 / 500 ms / CB 5 in 30 s / break 30 s) | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| D-03 | Define `CorrelationIdDelegatingHandler` behaviour contract: `Activity.Current?.Id` source, GUID fallback, caller-header-wins no-overwrite rule | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| D-04 | Define `TenantIdDelegatingHandler` behaviour contract: request-scope `IUserContext` resolution via `IHttpContextAccessor`, silent no-op on absent context or null `TenantId`, transient lifetime enforcement | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| D-05 | Define ProblemDetails deserialization contract: STJ source-generated context preferred, reflection-STJ fallback, `Error` mapping shape, non-2xx trigger condition | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| D-06 | Define `IServiceEndpointResolver` interface contract: `ResolveAsync(string serviceName, CancellationToken) → ValueTask<Uri>`, never-throw production rule, K8s convention URI fallback format | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| D-07 | Finalize `K8sServiceDiscoveryOptions` field list, defaults (`Namespace = "default"`, `ClusterDomain = "cluster.local"`, `SchemeOverride = null`), and Options-pattern validator rules | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| D-08 | Define `KubernetesServiceEndpointResolver` resolution algorithm: DNS SRV first (`_http._tcp.<service>.<namespace>.svc.<clusterDomain>`), A-record fallback for headless services, return-on-fail-without-throw contract | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| D-09 | Define `StaticServiceEndpointResolver` registration guard: `InvalidOperationException` on double-registration, `Warning`-level startup log requirement, known/unknown service lookup behaviour | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| D-10 | Define service-discovery integration point for `AddRestClient<TClient>`: optional `BaseAddress` omission when `IServiceEndpointResolver` is in DI, at-request-time resolution contract | WO-025 | `SharedKernel.Communication.Rest`, `SharedKernel.Communication.Internal` | `●` |
| D-11 | Finalize `IGrpcCommunicationBuilder` contract: `AddGrpcClient<TClient>` signature, global interceptor registration pattern via `Grpc.Net.ClientFactory`, channel singleton lifecycle | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| D-12 | Finalize `GrpcClientOptions` field list, defaults (`DeadlineSeconds = 30`, `EnableRetry = true`), and per-call deadline application contract | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| D-13 | Define `CorrelationTracingInterceptor` behaviour contract: W3C `traceparent` + `tracestate` + `x-correlation-id` metadata injection, `Activity.Current` read-at-call-time rule, no-overwrite rule, exception-swallow + `Error`-log contract | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| D-14 | Define `TenantIdInterceptor` behaviour contract: request-scope `IUserContext` resolution, `x-tenant-id` metadata injection, silent no-op, exception-swallow + `Error`-log contract | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| D-15 | Define Protobuf helper extension API: `MoneyProtoExtensions` (`Money ↔ decimal`), `TimestampProtoExtensions` (`Timestamp ↔ DateTimeOffset`), zero-allocation contract, pure-static requirement | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| D-16 | Define service-discovery integration point for `AddGrpcClient<TClient>`: optional `Address` omission when `IServiceEndpointResolver` is in DI, at-channel-creation-time resolution contract | WO-025 | `SharedKernel.Communication.Grpc`, `SharedKernel.Communication.Internal` | `●` |
| D-17 | Finalize `AddSharedKernelGraphQL` entry-point contract: `IRequestExecutorBuilder` return type, call-before-service-AddGraphQL ordering rule, idempotency requirement | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| D-18 | Finalize `GraphQLOptions` field list and defaults (`EnableFiltering = true`, `EnableSorting = true`, `EnablePaging = true`, `MaxPageSize = 100`, `AllowIntrospection = true`); document 500 hard cap on `MaxPageSize` | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| D-19 | Define `SharedKernelFilterConvention` configuration: snake_case binding names, string / numeric / date filter operations, `FilterConventionDescriptor` extension points | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| D-20 | Define `FilterBase<T>` and `SortBase<T>` abstract base contracts: `Descriptor()` override model, snake_case enforcement, field-visibility restriction rationale, direct-registration prohibition rule | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| D-21 | Define `PagedResponseType<T>` shape: `TotalCount + Items` surface for both offset (`CollectionSegment`) and cursor (`Connection`) paging, parity with `PagedList<T>` from `04.Contracts` | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| D-22 | Define GraphQL error mapping contract: `IErrorFilter` translates `IError` → `ProblemDetails`-compatible JSON shape; document field mapping (status, title, detail, extensions) | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.11.Scaffold -->

> Create project files, solution folder registrations, directory structure, and empty stub files.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| S-01 | Create `SharedKernel.Communication.Rest.csproj` with NuGet references: `01.Core`, `04.Contracts`, `12.Security.Abstractions`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience`; add to solution folder `11.Communication` | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| S-02 | Create folder structure under `SharedKernel.Communication.Rest/`: `Builders/`, `Options/`, `Handlers/`, `ProblemDetails/`, `Extensions/`; add empty stub files for all planned public types | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| S-03 | Create `SharedKernel.Communication.Rest.Tests.csproj` nested inside `SharedKernel.Communication.Rest/`; reference `SharedKernel.Communication.Rest` + `16.Testing/SharedKernel.Testing`; add xUnit | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| S-04 | Create `SharedKernel.Communication.Internal.csproj` with NuGet references: `01.Core`, `Microsoft.Extensions.ServiceDiscovery`; add to solution folder `11.Communication` | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| S-05 | Create folder structure under `SharedKernel.Communication.Internal/`: `Resolvers/`, `Options/`, `Extensions/`; add empty stub files for `IServiceEndpointResolver`, `KubernetesServiceEndpointResolver`, `StaticServiceEndpointResolver`, `K8sServiceDiscoveryOptions` | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| S-06 | Create `SharedKernel.Communication.Internal.Tests.csproj` nested inside `SharedKernel.Communication.Internal/`; reference `SharedKernel.Communication.Internal` + `16.Testing/SharedKernel.Testing`; add xUnit | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| S-07 | Create `SharedKernel.Communication.Grpc.csproj` with NuGet references: `01.Core`, `04.Contracts`, `12.Security.Abstractions`, `Grpc.Net.Client`, `Grpc.Net.ClientFactory`, `OpenTelemetry.Instrumentation.GrpcNetClient`; add to solution folder `11.Communication` | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| S-08 | Create folder structure under `SharedKernel.Communication.Grpc/`: `Builders/`, `Options/`, `Interceptors/`, `Protobuf/`, `Extensions/`; add empty stub files for all planned public types | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| S-09 | Create `SharedKernel.Communication.Grpc.Tests.csproj` nested inside `SharedKernel.Communication.Grpc/`; reference `SharedKernel.Communication.Grpc` + `16.Testing/SharedKernel.Testing`; add xUnit | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| S-10 | Create `SharedKernel.Communication.GraphQL.csproj` with NuGet references: `01.Core`, `04.Contracts`, `HotChocolate.Data`, `HotChocolate.AspNetCore`; add to solution folder `11.Communication`; confirm `<IsAotCompatible>` is NOT set | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| S-11 | Create folder structure under `SharedKernel.Communication.GraphQL/`: `Conventions/`, `Options/`, `Types/`, `Pagination/`, `Errors/`, `Extensions/`; add empty stub files for all planned public types | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| S-12 | Create `SharedKernel.Communication.GraphQL.Tests.csproj` nested inside `SharedKernel.Communication.GraphQL/`; reference `SharedKernel.Communication.GraphQL` + `16.Testing/SharedKernel.Testing`; add xUnit and `HotChocolate.AspNetCore.Tests.Utilities` | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| S-13 | Pin NuGet package versions for all four packages: verify `net10.0` compatibility for `Microsoft.Extensions.Http.Resilience`, `Grpc.Net.Client` 2.x, `OpenTelemetry.Instrumentation.GrpcNetClient`, `HotChocolate.Data` 14.x, `Microsoft.Extensions.ServiceDiscovery`; record chosen versions in CLAUDE.md | WO-025 | All | `●` |

---

## Phase: Rest <!-- phase-key: SK.11.Rest -->

> Implement typed HttpClient factory, Polly v8 resilience, CorrelationIdDelegatingHandler, TenantIdDelegatingHandler, and ProblemDetails error handling.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| R-01 | Implement `RestClientOptions` and `RestResilienceOptions` with all fields, production-safe defaults, and `IValidateOptions<RestClientOptions>` validator (require `BaseAddress` when no `IServiceEndpointResolver`) | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-02 | Implement `IRestCommunicationBuilder` interface and `RestCommunicationBuilder` concrete class; expose `Services` property; `AddRestClient<TClient>` must return `this` for chaining | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-03 | Implement `AddSharedKernelRestCommunication(this IServiceCollection) → IRestCommunicationBuilder` DI extension; registers builder, both delegation handlers as transient, and STJ `ProblemDetailsJsonContext` | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-04 | Implement `AddRestClient<TClient>` on the builder: call `services.AddHttpClient<TClient>()`, configure `BaseAddress` from `RestClientOptions`, attach `StandardResilienceHandler` configured from `RestResilienceOptions`, chain `CorrelationIdDelegatingHandler` and `TenantIdDelegatingHandler` in that order | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-05 | Implement `CorrelationIdDelegatingHandler` (internal sealed): read `Activity.Current?.Id`; fall back to `Guid.NewGuid().ToString("N")`; inject `x-correlation-id` header only if not already present; no cross-request state | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-06 | Implement `TenantIdDelegatingHandler` (internal sealed, transient): inject `IHttpContextAccessor` in constructor; resolve `IUserContext` from `IHttpContextAccessor.HttpContext.RequestServices`; inject `x-tenant-id` header when `TenantId` is non-null; silently no-op when `IHttpContextAccessor.HttpContext` is null, `IUserContext` not registered, or `TenantId` is null; never throw | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-07 | Implement `ProblemDetailsDeserializer` internal helper: STJ source-generated `ProblemDetailsJsonContext` for AOT path; reflection-STJ fallback when content type is `application/problem+json`; map deserialized fields to `SharedKernel.Primitives.Error` (code from `type`, message from `detail` or `title`) | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-08 | Implement `HttpResponseMessageExtensions.EnsureSuccessOrErrorAsync(this HttpResponseMessage, CancellationToken) → Task<Result<T>>` extension method available to typed-client base classes: returns `Result.Ok` on 2xx; deserializes `ProblemDetails` and returns `Result.Fail<Error>` on non-2xx | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-09 | Implement optional service-discovery integration in `AddRestClient<TClient>`: when `BaseAddress` is null/empty and `IServiceEndpointResolver` is registered in DI, configure a custom `HttpMessageHandler` that resolves the base URI via `IServiceEndpointResolver.ResolveAsync(clientName, ct)` at request time | WO-025 | `SharedKernel.Communication.Rest` | `●` |
| R-10 | Validate that `StandardResilienceHandler` `RetryCount`, `RetryBaseDelayMs` (exponential backoff), `CircuitBreakerEnabled`, `FailureThreshold`, `SamplingDurationSec`, and `BreakDurationSec` from `RestResilienceOptions` are applied correctly to the built pipeline; write smoke test to confirm policy fires | WO-025 | `SharedKernel.Communication.Rest` | `●` |

---

## Phase: Grpc <!-- phase-key: SK.11.Grpc -->

> Implement gRPC channel factory, OTel tracing interceptor, tenant/correlation metadata interceptors, and Protobuf helper extensions.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| G-01 | Implement `GrpcClientOptions` with all fields and defaults (`DeadlineSeconds = 30`, `EnableRetry = true`); add `IValidateOptions<GrpcClientOptions>` validator requiring `Address` when `IServiceEndpointResolver` not registered | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| G-02 | Implement `IGrpcCommunicationBuilder` interface and `GrpcCommunicationBuilder` concrete class; expose `Services` property; `AddGrpcClient<TClient>` must return `this` for chaining | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| G-03 | Implement `AddSharedKernelGrpcCommunication(this IServiceCollection) → IGrpcCommunicationBuilder` DI extension; registers builder and both interceptors via `services.AddGrpcClient` global interceptor pattern | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| G-04 | Implement `AddGrpcClient<TClient>` on the builder: call `services.AddGrpcClient<TClient>()`, configure channel `Address` from `GrpcClientOptions`, register `CorrelationTracingInterceptor` and `TenantIdInterceptor` as global interceptors via `.AddInterceptor<T>()`; apply `DeadlineSeconds` as per-call `CallOptions.Deadline`; channels registered as singletons via `Grpc.Net.ClientFactory` caching | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| G-05 | Implement `CorrelationTracingInterceptor` (internal sealed): override `AsyncUnaryCall`, `AsyncServerStreamingCall`, `AsyncClientStreamingCall`, `AsyncDuplexStreamingCall`; read `Activity.Current` at call time; inject `traceparent` (W3C format), `tracestate`, and `x-correlation-id` into `CallOptions.Headers` metadata; do not overwrite existing `x-correlation-id` entry; wrap interceptor body in try/catch — log `Error` on exception and continue without propagating | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| G-06 | Implement `TenantIdInterceptor` (internal sealed): same four call-type overrides as G-05; resolve `IUserContext` from request scope via `IHttpContextAccessor`; inject `x-tenant-id` metadata when `TenantId` non-null; silent no-op otherwise; wrap in try/catch — log `Error` on exception and continue | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| G-07 | Implement `MoneyProtoExtensions` (public static class): `ToDecimal(this Money money) → decimal` and `ToMoneyProto(this decimal value, string currencyCode) → Money`; no intermediate object allocations; pure arithmetic only | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| G-08 | Implement `TimestampProtoExtensions` (public static class): `ToDateTimeOffset(this Timestamp ts) → DateTimeOffset` and `ToTimestampProto(this DateTimeOffset dto) → Timestamp`; use `Timestamp.FromDateTimeOffset`/`ToDateTimeOffset` under the hood or direct ticks arithmetic — whichever avoids extra allocations | WO-025 | `SharedKernel.Communication.Grpc` | `●` |
| G-09 | Implement optional service-discovery integration in `AddGrpcClient<TClient>`: when `Address` is null/empty and `IServiceEndpointResolver` is registered in DI, resolve address via `IServiceEndpointResolver.ResolveAsync(clientName, ct)` at channel creation time and configure as channel `Address` | WO-025 | `SharedKernel.Communication.Grpc` | `●` |

---

## Phase: GraphQL <!-- phase-key: SK.11.GraphQL -->

> Implement HotChocolate convention wiring, FilterBase/SortBase, PagedResponseType, error mapping, and AddSharedKernelGraphQL entry point.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| GQ-01 | Implement `GraphQLOptions` sealed class with all fields and defaults; add `IValidateOptions<GraphQLOptions>` validator rejecting `MaxPageSize > 500` | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| GQ-02 | Implement `SharedKernelFilterConvention` (extends `FilterConvention`): call `AddDefaults()`, configure snake_case binding names for all registered filter operations; register standard string (`eq`, `neq`, `contains`, `startsWith`, `endsWith`), numeric (`eq`, `neq`, `gt`, `gte`, `lt`, `lte`), and date (`eq`, `neq`, `gt`, `gte`, `lt`, `lte`) operations | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| GQ-03 | Implement `FilterBase<T>` (public abstract, extends `FilterInputType<T>`): seal default `Configure()` to enforce snake_case binding; require consuming services to override `Descriptor()` for field configuration; document that direct `FilterInputType<T>` registration without this base is a platform violation | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| GQ-04 | Implement `SortBase<T>` (public abstract, extends `SortInputType<T>`): same snake_case enforcement and `Descriptor()` override model as `FilterBase<T>` | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| GQ-05 | Implement `PagedResponseType<T>` (public sealed class): presents `TotalCount` (int) and `Items` (`IReadOnlyList<T>`) shape; supports both `CollectionSegment<T>` (offset) and `Connection<T>` (cursor) HotChocolate paged result types as source; aligns field naming with `PagedList<T>` from `04.Contracts` | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| GQ-06 | Implement `SharedKernelErrorFilter` (implements `IErrorFilter`): map `IError` → `ProblemDetails`-compatible shape: `status` from HTTP status code extension, `title` from `IError.Message`, `detail` from `IError.Exception?.Message`, `extensions` from `IError.Extensions`; produce consistent JSON shape matching REST `ProblemDetails` responses | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| GQ-07 | Implement `AddSharedKernelGraphQL(this IServiceCollection, Action<GraphQLOptions>? configure) → IRequestExecutorBuilder`: register `GraphQLOptions`, call `AddGraphQL()`, chain `.AddConvention<IFilterConvention, SharedKernelFilterConvention>()`, apply snake_case naming convention, register `SharedKernelErrorFilter`, configure `MaxPageSize` globally, set `AllowIntrospection` from `GraphQLOptions`; guard idempotency (check if already registered via service descriptor lookup) | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |
| GQ-08 | Apply offset and cursor pagination support in `AddSharedKernelGraphQL`: call `.AddOffsetPagination()` and `.AddPagination()` when `EnablePaging = true`; configure `SetPagingOptions` with `MaxPageSize` from `GraphQLOptions`; enforce global page-size cap | WO-025 | `SharedKernel.Communication.GraphQL` | `●` |

---

## Phase: Internal <!-- phase-key: SK.11.Internal -->

> Implement IServiceEndpointResolver, KubernetesServiceEndpointResolver (DNS-based), StaticServiceEndpointResolver (dev/test), and AddK8sServiceDiscovery DI extension.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| I-01 | Implement `IServiceEndpointResolver` interface: single method `ResolveAsync(string serviceName, CancellationToken ct) → ValueTask<Uri>`; mark interface as public; XML doc comment mandates never-throw contract in production | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| I-02 | Implement `K8sServiceDiscoveryOptions` sealed class: `Namespace` (default `"default"`), `ClusterDomain` (default `"cluster.local"`), `SchemeOverride` (default `null` → `"http"`); add `IValidateOptions<K8sServiceDiscoveryOptions>` validator rejecting empty `Namespace` or `ClusterDomain` | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| I-03 | Implement `KubernetesServiceEndpointResolver` (implements `IServiceEndpointResolver`, registered as singleton): use `Microsoft.Extensions.ServiceDiscovery` to attempt DNS SRV lookup (`_http._tcp.<service>.<namespace>.svc.<clusterDomain>`) first; fall back to A-record (`<service>.<namespace>.svc.<clusterDomain>`); on any resolution failure, return constructed K8s convention URI `{scheme}://{serviceName}.{namespace}.svc.{clusterDomain}` without throwing | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| I-04 | Implement `StaticServiceEndpointResolver` (implements `IServiceEndpointResolver`, registered as singleton): constructor accepts `IReadOnlyDictionary<string, Uri>`; `ResolveAsync` returns registered `Uri` for known service names; for unknown names returns K8s convention URI `http://{serviceName}.default.svc.cluster.local` (never throws) | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| I-05 | Implement `AddK8sServiceDiscovery(this IServiceCollection, Action<K8sServiceDiscoveryOptions>? configure = null) → IServiceCollection`: bind `K8sServiceDiscoveryOptions` via `Configure<>()`; register `KubernetesServiceEndpointResolver` as `IServiceEndpointResolver` singleton; wire `Microsoft.Extensions.ServiceDiscovery` DNS resolver | WO-025 | `SharedKernel.Communication.Internal` | `●` |
| I-06 | Implement `AddStaticServiceDiscovery(this IServiceCollection, Dictionary<string, Uri> endpoints) → IServiceCollection`: check if `IServiceEndpointResolver` is already registered — throw `InvalidOperationException` with clear message if so; register `StaticServiceEndpointResolver` as `IServiceEndpointResolver` singleton; log `LogLevel.Warning` at startup via `IStartupFilter` or `IHostedService`-based early log indicating this is a non-production resolver | WO-025 | `SharedKernel.Communication.Internal` | `●` |

---

## Phase: Tests <!-- phase-key: SK.11.Tests -->

> Unit and integration tests for all packages. Delegation handler tests, interceptor tests, resilience policy tests, GraphQL filter tests, and service discovery resolver tests.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| T-01 | `CorrelationIdDelegatingHandler` tests: assert `x-correlation-id` injected from `Activity.Current.Id` when trace active; assert GUID fallback when no trace; assert caller-set header not overwritten; use `HttpMessageHandler` test double (no real HTTP) | WO-025 | `SharedKernel.Communication.Rest` | `○` |
| T-02 | `TenantIdDelegatingHandler` tests: assert `x-tenant-id` injected from request-scoped `IUserContext.TenantId`; assert silent no-op when `HttpContext` is null; assert silent no-op when `IUserContext` not registered; assert silent no-op when `TenantId` is null; use `HttpMessageHandler` test double | WO-025 | `SharedKernel.Communication.Rest` | `○` |
| T-03 | REST resilience policy tests: simulate transient 500 responses; assert retry fires `RetryCount` times with exponential backoff; assert circuit breaker opens after `FailureThreshold` failures within `SamplingDurationSec`; assert client receives `Result.Fail` after CB opens; use `HttpMessageHandler` test double | WO-025 | `SharedKernel.Communication.Rest` | `○` |
| T-04 | ProblemDetails deserialization tests: assert `application/problem+json` response body maps to `SharedKernel.Primitives.Error` correctly (`type` → code, `detail` → message); assert 2xx response returns `Result.Ok`; assert non-`application/problem+json` non-2xx response returns generic `Error` | WO-025 | `SharedKernel.Communication.Rest` | `○` |
| T-05 | `AddRestClient` DI builder smoke tests: assert typed client registered in DI container; assert `StandardResilienceHandler` present in handler pipeline; assert both delegation handlers present and in correct order (Correlation before Tenant) | WO-025 | `SharedKernel.Communication.Rest` | `○` |
| T-06 | `StaticServiceEndpointResolver` unit tests: assert registered service name returns correct `Uri`; assert unregistered service name returns K8s convention URI (`http://{name}.default.svc.cluster.local`); assert `ResolveAsync` never throws | WO-025 | `SharedKernel.Communication.Internal` | `○` |
| T-07 | `AddStaticServiceDiscovery` guard tests: assert `InvalidOperationException` thrown when `IServiceEndpointResolver` already registered; assert `Warning`-level log emitted at startup on successful registration; use `ILoggerFactory` test double to capture log output | WO-025 | `SharedKernel.Communication.Internal` | `○` |
| T-08 | `K8sServiceDiscoveryOptions` validation tests: assert validator rejects empty `Namespace`; assert validator rejects empty `ClusterDomain`; assert default values are applied when options not configured | WO-025 | `SharedKernel.Communication.Internal` | `○` |
| T-09 | `KubernetesServiceEndpointResolver` tests (mocked DNS): assert SRV record resolution attempted first; assert A-record fallback on SRV failure; assert K8s convention URI returned on full DNS failure without throwing; mark DNS-dependent tests `[Trait("Category", "Integration")]` | WO-025 | `SharedKernel.Communication.Internal` | `○` |
| T-10 | `CorrelationTracingInterceptor` tests: assert `traceparent`, `tracestate`, and `x-correlation-id` injected into metadata; assert `Activity.Current` read at call time not at DI time; assert caller-set `x-correlation-id` not overwritten; assert exception inside interceptor is swallowed and logged at `Error`, not propagated; use `Grpc.Core.Testing.TestServerCallContext` stub | WO-025 | `SharedKernel.Communication.Grpc` | `○` |
| T-11 | `TenantIdInterceptor` tests: assert `x-tenant-id` injected from request-scoped `IUserContext`; assert silent no-op when `HttpContext` null or `TenantId` null; assert interceptor exception swallowed and logged at `Error`; use `Grpc.Core.Testing.TestServerCallContext` stub | WO-025 | `SharedKernel.Communication.Grpc` | `○` |
| T-12 | Protobuf helper round-trip tests: `MoneyProtoExtensions` — assert `decimal → Money → decimal` round-trips without precision loss; assert no intermediate allocations (use `GC.GetAllocatedBytesForCurrentThread`); `TimestampProtoExtensions` — assert `DateTimeOffset → Timestamp → DateTimeOffset` round-trips; assert UTC edge cases (min/max `DateTimeOffset`) | WO-025 | `SharedKernel.Communication.Grpc` | `○` |
| T-13 | `AddGrpcClient` DI builder smoke tests: assert typed client registered in DI; assert both interceptors registered globally; assert channel reuse (singleton pattern) | WO-025 | `SharedKernel.Communication.Grpc` | `○` |
| T-14 | GraphQL filter + sort + paging tests: use HotChocolate `IRequestExecutor` test builder; assert snake_case field names applied; assert filter operations (`eq`, `contains`, `gt`) produce expected schema fields; assert sort fields available; assert offset paging returns `CollectionSegment` with `TotalCount` | WO-025 | `SharedKernel.Communication.GraphQL` | `○` |
| T-15 | `MaxPageSize` enforcement tests: assert request for items > `MaxPageSize` is rejected at the HotChocolate layer; assert `MaxPageSize = 50` configuration applied; assert `MaxPageSize > 500` rejected by `GraphQLOptions` validator | WO-025 | `SharedKernel.Communication.GraphQL` | `○` |
| T-16 | GraphQL error mapping tests: assert `IError` with status code extension maps to `ProblemDetails`-compatible JSON (`status`, `title`, `detail`, `extensions` fields); assert `SharedKernelErrorFilter` registered and active | WO-025 | `SharedKernel.Communication.GraphQL` | `○` |
| T-17 | `AllowIntrospection` flag tests: assert schema introspection query succeeds when `AllowIntrospection = true`; assert introspection query rejected when `AllowIntrospection = false` | WO-025 | `SharedKernel.Communication.GraphQL` | `○` |
| T-18 | `AddSharedKernelGraphQL` idempotency tests: assert calling `AddSharedKernelGraphQL` twice does not double-register conventions or error filters | WO-025 | `SharedKernel.Communication.GraphQL` | `○` |

---

## Phase: Docs <!-- phase-key: SK.11.Docs -->

> Ensure all public types carry XML doc comments. Update CLAUDE.md with implementation-phase discoveries.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| DO-01 | Add XML doc comments to all public types in `SharedKernel.Communication.Rest`: `IRestCommunicationBuilder`, `RestClientOptions`, `RestResilienceOptions`, DI extension method, `HttpResponseMessageExtensions`, internal handler summaries in assembly-doc | WO-025 | `SharedKernel.Communication.Rest` | `○` |
| DO-02 | Add XML doc comments to all public types in `SharedKernel.Communication.Internal`: `IServiceEndpointResolver`, `K8sServiceDiscoveryOptions`, both DI extension methods; emphasise never-throw production contract in `ResolveAsync` doc | WO-025 | `SharedKernel.Communication.Internal` | `○` |
| DO-03 | Add XML doc comments to all public types in `SharedKernel.Communication.Grpc`: `IGrpcCommunicationBuilder`, `GrpcClientOptions`, `MoneyProtoExtensions`, `TimestampProtoExtensions`, DI extension method; note exception-swallow contract on interceptors in assembly-doc | WO-025 | `SharedKernel.Communication.Grpc` | `○` |
| DO-04 | Add XML doc comments to all public types in `SharedKernel.Communication.GraphQL`: `AddSharedKernelGraphQL`, `GraphQLOptions`, `FilterBase<T>`, `SortBase<T>`, `PagedResponseType<T>`, `SharedKernelFilterConvention`; note AOT incompatibility of HotChocolate v14 in package-level doc | WO-025 | `SharedKernel.Communication.GraphQL` | `○` |
| DO-05 | Update `11.Communication/CLAUDE.md` with implementation-phase discoveries: confirmed NuGet version pins, any deviations from planned interface shapes, and lessons from `StandardResilienceHandler` configuration | WO-025 | All | `○` |

---

## Phase: Published <!-- phase-key: SK.11.Published -->

> Set NuGet metadata on all four packages, pack, verify manifests, and publish to internal feed.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| PB-01 | Set NuGet packaging metadata on `SharedKernel.Communication.Rest.csproj`: `PackageId`, `Version`, `Description`, `Authors`, `PackageTags`; confirm no unintended transitive dependencies in `.nupkg` manifest | WO-025 | `SharedKernel.Communication.Rest` | `○` |
| PB-02 | Set NuGet packaging metadata on `SharedKernel.Communication.Internal.csproj`: same fields; confirm `Microsoft.Extensions.ServiceDiscovery` is a direct dependency in the manifest | WO-025 | `SharedKernel.Communication.Internal` | `○` |
| PB-03 | Set NuGet packaging metadata on `SharedKernel.Communication.Grpc.csproj`: same fields; confirm `Grpc.Net.Client`, `Grpc.Net.ClientFactory`, and `OpenTelemetry.Instrumentation.GrpcNetClient` appear in manifest | WO-025 | `SharedKernel.Communication.Grpc` | `○` |
| PB-04 | Set NuGet packaging metadata on `SharedKernel.Communication.GraphQL.csproj`: same fields; note in package description that HotChocolate v14 is not AOT-safe; confirm `HotChocolate.Data` and `HotChocolate.AspNetCore` are direct dependencies | WO-025 | `SharedKernel.Communication.GraphQL` | `○` |
| PB-05 | Pack all four packages; verify `.nupkg` manifests (dependency graphs, target frameworks, included files); run `dotnet list package --vulnerable` across all four packages | WO-025 | All | `○` |
| PB-06 | Publish all four packages to internal NuGet feed; verify installation in a blank consumer project; confirm `AddSharedKernelRestCommunication`, `AddSharedKernelGrpcCommunication`, `AddSharedKernelGraphQL`, `AddK8sServiceDiscovery`, and `AddStaticServiceDiscovery` DI extensions are available and compile | WO-025 | All | `○` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | :---: | :---: | :---: | :---: |
| `SK.11.Design` | Design | 22 | 22 | 0 | `●` |
| `SK.11.Scaffold` | Scaffold | 13 | 13 | 0 | `●` |
| `SK.11.Rest` | Rest | 10 | 10 | 0 | `●` |
| `SK.11.Grpc` | Grpc | 9 | 9 | 0 | `●` |
| `SK.11.GraphQL` | GraphQL | 8 | 8 | 0 | `●` |
| `SK.11.Internal` | Internal | 6 | 6 | 0 | `●` |
| `SK.11.Tests` | Tests | 18 | 0 | 18 | `○` |
| `SK.11.Docs` | Docs | 5 | 0 | 5 | `○` |
| `SK.11.Published` | Published | 6 | 0 | 6 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-16] Sub state-map initialized — phase key registry, 9 phases scaffolded at ○, package board with 4 packages at not-started; no tasks yet
- [2026-06-16] P-154/P-155/P-156/P-157 (WO-025): full task set added — 22 Design, 13 Scaffold, 10 Rest, 9 Grpc, 8 GraphQL, 6 Internal, 18 Tests, 5 Docs, 6 Published tasks (total 97 tasks across all phases); package board updated to reflect active design phase for all four packages
- [2026-06-16] SK.11.Design complete — all 22 design tasks validated against CLAUDE.md (WO-025); all contracts, builder APIs, resilience options, interceptor rules, GraphQL conventions, and service discovery contracts confirmed documented; package board advanced to Scaffold
- [2026-06-16] SK.11.Design → ● — all 22 Design tasks complete; promoting to root state-map (state-map-phase)
- [2026-06-16] SK.11.Scaffold → ● — all 13 Scaffold tasks complete; 4 csproj + 4 test csproj + all stub files created, all build clean (state-map-phase)
- [2026-06-17] R-01–R-10 → ● in SK.11.Rest — all 10 Rest tasks complete; 35/35 tests passing (state-map-phase)
- [2026-06-17] G-01–G-09 → ● in SK.11.Grpc — all 9 Grpc tasks complete; 46/46 tests passing (state-map-phase)
- [2026-06-17] GQ-01–GQ-08 → ● in SK.11.GraphQL — all 8 GraphQL tasks complete; 40/40 tests passing (state-map-phase)
- [2026-06-18] I-01–I-06 → ● in SK.11.Internal — all 6 Internal tasks complete; 27/27 tests passing (state-map-phase)
