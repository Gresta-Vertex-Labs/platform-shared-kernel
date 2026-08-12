# 11.Communication — Domain Brain

## What This Domain Is

The outbound communication capability domain. Provides platform-standard wrappers for a microservice to communicate with **other services or clients** using the four primary channels in a K8s-native microservice ecosystem: REST, gRPC, GraphQL, and in-cluster service discovery.

Every package in this domain eliminates repeated boilerplate — resilience policies, OTel propagation, correlation/tenant header injection, and service address resolution are wired once at the composition root and invisible to application code.

Philosophy: **Protocol-Agnostic Resilience. Propagate Context Always. Fail Informatively.**

> **Scope clarification:** This domain covers **outgoing** communication adapters only. Inbound concerns (ASP.NET routing, middleware, endpoint mapping) belong in `14.Presentation`. Message-bus event publishing belongs in `07.Messaging`. Cache invalidation signaling belongs in `02.Caching`.

---

## Current Phase

**All four packages fully implemented and tested (Core + Tests phases ● across Rest/Grpc/GraphQL/Internal — 315/315 tests passing).** Published phase remains `○` Pending from original scaffolding (WO-025) — not yet fully dispatched. **P-255 (WO-041) logging retrofit — fully complete** (Design/Grpc/Internal/Tests/Docs all ●): `SharedKernel.Communication.Grpc` (G-14/G-15) and `SharedKernel.Communication.Internal` (I-09/I-10/I-11) — `CorrelationTracingInterceptor`/`TenantIdInterceptor` retrofitted to `[LoggerMessage]`-attributed static partial methods (EventId 11100/11101, 60/60 tests passing); `KubernetesServiceEndpointResolver`'s six delegate fields renumbered and two ad-hoc `LogDebug` calls converted, plus `StaticServiceDiscoveryStartupWarning`'s delegate converted, all to `[LoggerMessage]`-attributed static partial methods (EventId 11300-11308, 52/52 tests passing) — the platform's last mixed-authoring-style file is now clean. `SharedKernel.Communication.Rest` and `SharedKernel.Communication.GraphQL` carry zero logging today — no retrofit needed there. See the **Logging** section below for the full EventId sub-block allocation and per-method table.

**P-260 (WO-042) header-name constant consolidation — fully closed.** `CorrelationIdDelegatingHandler`/`TenantIdDelegatingHandler` (`.Rest`, R-19/R-20 ●) and `TenantIdInterceptor`/`CorrelationTracingInterceptor` (`.Grpc`, G-16/G-17 ●) source their `x-correlation-id`/`x-tenant-id` header/metadata names from `01.Core`'s `WellKnownHeaders` (P-259) instead of independently-declared literals; the gRPC package's locally-named `const`s survive only as thin value-forwarding aliases (carved out for gRPC's lowercase-metadata-key convention — verified safe via `Grpc.Core.Metadata`'s internal key-casing normalization, see gRPC rules below). T-29 replaced the remaining test-local literal duplicates in `.Rest.Tests`/`.Grpc.Tests` with `WellKnownHeaders` references; DO-07 confirmed the propagation-rules documentation was already accurate. No new project reference was required.

**WO-056 (P-356–P-364) — nine gap-fill phases design-locked; Rest, Grpc, GraphQL, Internal, and Tests shipped, three more phase keys queued.** A fresh review against real shipped source (not domain-brain prose) found nine defects: a duplicated SK0011 violation (non-canonical correlation-ID fallback), the SK0001 finding P-255/T-28 flagged as an unfixed "candidate follow-up" and never turned into a phase, a structurally-dead options-validator wiring defect in both `.Rest` and `.GraphQL`, an unenforced `GrpcClientOptions.DeadlineSeconds`, an asymmetric service-discovery registration guard, a misleading `EnsureSuccessOrErrorAsync<T>`, a missing `consumer-verify` harness, this domain's still-never-been-published status, and a gap in the platform's outbound idempotency story. `SK.11.Design`/`SK.11.Scaffold`/`SK.11.Rest`/`SK.11.Grpc`/`SK.11.GraphQL`/`SK.11.Internal`/`SK.11.Tests` are now `●` (R-22–R-26, G-18–G-20, GQ-10, I-12/I-13 all implemented and tested — the SK0001 `KubernetesServiceEndpointResolver` fix and the symmetric `AddK8sServiceDiscovery` guard both shipped; T-31–T-38's deferred regression/DI-resolution test coverage for every one of the above all landed, 243/243 tests passing across all four packages); `SK.11.Docs` and `SK.11.Published` remain `○`/`◐` in `state-map.md` — the source of truth for what remains. The sections below (Interface Contracts, Implementation Rules, DI Registration, Test Rules) already describe the shipped `.Rest`/`.Grpc`/`.GraphQL`/`.Internal` behavior in full. See the Changelog for the full per-defect breakdown.

**P-329 (WO-052) namespace adoption — complete.** `04.Contracts`' P-328 renamed `SharedKernel.Contracts.Envelope` → `SharedKernel.Contracts.Envelopes` (eliminating the namespace/type-name collision that previously forced consuming code onto a `using EnvelopeNs = ...` alias workaround). Source audit confirmed — and was independently re-confirmed directly against shipped `04.Contracts` source — that `HttpResponseMessageExtensions.cs` (`.Rest`) was the **sole** production reference to the retired namespace anywhere in this domain. The one-line swap has landed: `HttpResponseMessageExtensions.cs:4` now reads `using SharedKernel.Contracts.Envelopes;` (D-25, R-21 both `●`). The regression pass (T-30, `●`) re-ran independently rather than being reused: `dotnet build` on `SharedKernel.Communication.Rest.csproj` is clean (0 warnings/0 errors) and `SharedKernel.Communication.Rest.Tests` is 66/66 passing. That build proof is validated only at the **`ProjectReference` level** — `04.Contracts` and `11.Communication` are wired by `ProjectReference` in this repo, not by a consumed `.nupkg`, so no packed-package consumption test was run. `ReadEnvelopeAsyncTests.cs` needed no change — it imports neither namespace variant (relies on `var` type inference); its real `[Fact]` count is **10** (5 in the `JsonTypeInfo<T>` section, 5 in the `JsonSerializerOptions?` section), correcting an earlier "12-test suite" miscount that had propagated through WO-052 phase prose. `.Grpc`'s doc-comment mention of `Envelope<T>` (Interface Contracts, gRPC rules below) remains a type-name mention only, never a namespace import, and required no change — confirmed by direct re-check of every `Envelope`/`namespace` occurrence in this file. All four P-329 tasks (D-25, R-21, T-30, DO-08) are now `●`. Every phase key in `11.Communication/state-map.md` is now `●` except Published (not yet dispatched).

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
    TimeoutSeconds       int                 // default 30 — per-request timeout; must be > 0
    Resilience           RestResilienceOptions
    EnableIdempotencyKeyPropagation bool     // default false (P-364/WO-056) — opt-in IdempotencyKeyDelegatingHandler

RestResilienceOptions  (sealed class)
    RetryCount           int                 // default 3; must be > 0
    RetryBaseDelayMs     int                 // default 500 — exponential backoff base; must be >= 0
    CircuitBreakerEnabled bool               // default true
    FailureThreshold     int                 // default 5 failures before CB opens; must be > 0
    SamplingDurationSec  int                 // default 30 — failure counting window; must be > 0
    BreakDurationSec     int                 // default 30 — CB open duration; must be > 0
    TotalTimeoutBufferSec int                // default 10, minimum 0 — added to TimeoutSeconds × (RetryCount + 1)
                                             // to compute TotalRequestTimeout; provides headroom for jitter
                                             // and circuit-breaker probe time; set to 0 for tight latency budgets
                                             // All range constraints above enforced by RestClientOptionsValidator
                                             // (P-358/WO-056) — see REST client rules below for how/when it fires.

CorrelationIdDelegatingHandler  [internal sealed — transient]
    // Reads Activity.Current?.Id; falls back to Guid.NewGuid().ToString() — the default "D"
    // (hyphenated) format, the platform's canonical GUID string shape (P-356/WO-056).
    // Never Guid.NewGuid().ToString("N") — that non-hyphenated 32-char-hex form was a
    // confirmed SK0011 violation, fixed under P-356.
    // Header name sourced from 01.Core's WellKnownHeaders.CorrelationId (P-259/P-260) —
    // never an independently-declared literal.
    // Injects x-correlation-id header. Never overwrites a caller-supplied header.

TenantIdDelegatingHandler  [internal sealed — transient]
    // Resolves IUserContext from IHttpContextAccessor.HttpContext.RequestServices.
    // Header name sourced from 01.Core's WellKnownHeaders.TenantId (P-259/P-260) —
    // never an independently-declared literal.
    // Injects x-tenant-id header when TenantId non-null.
    // Silent no-op when HttpContext null, IUserContext not registered, or TenantId null.
    // Never throws.

IdempotencyKeyDelegatingHandler  [internal sealed — transient]  (P-364/WO-056)
    // Opt-in only — added to the handler pipeline solely when
    // RestClientOptions.EnableIdempotencyKeyPropagation is true (default false).
    // Generates a hyphenated Guid.NewGuid().ToString() idempotency-key value and injects it
    // under IdempotencyHeaders.IdempotencyKey ("x-idempotency-key") only when the header is
    // not already present on the outgoing HttpRequestMessage. Never overwrites a
    // caller-supplied key.
    // StandardResilienceHandler retries re-send the SAME HttpRequestMessage instance, so the
    // header-already-present check is sufficient by construction to keep the key identical
    // across every retry attempt of one logical call — no extra per-call state needed.

ProblemDetailsDeserializer  [internal static]
    // Deserializes application/problem+json response bodies on non-2xx responses.
    // Uses STJ source-generated ProblemDetailsJsonContext (AOT path).
    // Falls back to static readonly reflection-based JsonSerializerOptions (initialized once at class load).
    // Maps: type → Error.Code, detail ?? title → Error.Message.

HttpResponseMessageExtensions  [public static]
    EnsureSuccessOrErrorAsync(this HttpResponseMessage, CancellationToken = default)
        → Task<Result>
    // CORRECTED (P-361/WO-056): the prior generic EnsureSuccessOrErrorAsync<T> is retired — it
    // returned Result<T>.Success(default!) unconditionally on 2xx, never actually reading or
    // deserializing the response body despite the generic parameter's promise. This package was
    // never packed or published, so the fix is retirement rather than a breaking-change patch.
    // Status-check-only: 2xx → Result.Success(); non-2xx → Result.Failure(Error) via
    // ProblemDetailsDeserializer. Callers wanting the deserialized payload use ReadEnvelopeAsync<T>
    // below — never re-add a generic overload that promises a payload it does not deliver.

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
    // Entry point. Registers IRestCommunicationBuilder, all three delegation handlers (transient —
    // CorrelationIdDelegatingHandler, TenantIdDelegatingHandler, IdempotencyKeyDelegatingHandler,
    // the last only actually added to a given client's pipeline when opted in), STJ
    // ProblemDetailsJsonContext, and RestClientOptionsValidator
    // (services.AddSingleton<IValidateOptions<RestClientOptions>, RestClientOptionsValidator>()).
    // CORRECTED (P-358/WO-056): that IValidateOptions<RestClientOptions> registration is retained
    // but is NOT the mechanism that actually enforces validation — RestClientOptions is never
    // resolved via IOptions<T>.Value by application code, so the registered validator can never
    // structurally fire through it. Real enforcement happens inside AddRestClient<TClient> itself —
    // see REST client rules below.
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
    DeadlineSeconds      int                 // default 30 — REAL, ENFORCED per-call deadline (P-359/WO-056);
                                             // must be > 0 — GrpcClientOptionsValidator rejects <= 0
    EnableRetry          bool                // default true

GrpcClientOptionsValidator  [internal sealed, implements IValidateOptions<GrpcClientOptions>]  (P-359/WO-056)
    // New — no validator existed for this options type before P-359. Rejects DeadlineSeconds <= 0.
    // Invoked directly inside AddGrpcClient<TClient> immediately after configure?.Invoke(options),
    // mirroring RestClientOptionsValidator's validate-at-point-of-consumption pattern (see REST client
    // rules' P-358 note) — never relies on the IOptions<T> pipeline, for the same structural reason.

GrpcMetadataHelper  [internal static]
    // Shared helper used by CorrelationTracingInterceptor and TenantIdInterceptor.
    HasMetadataEntry(Metadata metadata, string key) → bool   // case-insensitive key match
    CloneAndAdd(Metadata metadata, string key, string value) → Metadata
    // Returns new Metadata instance; all existing entries copied; new entry appended;
    // original Metadata not mutated.

CorrelationTracingInterceptor  [internal sealed — Interceptor]
    // Overrides AsyncUnaryCall, AsyncServerStreamingCall, AsyncClientStreamingCall,
    // AsyncDuplexStreamingCall.
    // Reads Activity.Current at call time (not DI registration time); falls back to
    // Guid.NewGuid().ToString() (hyphenated "D" format, P-356/WO-056 — never .ToString("N"),
    // a confirmed SK0011 violation fixed under P-356) when no ambient trace exists.
    // Correlation metadata key sourced from 01.Core's WellKnownHeaders.CorrelationId
    // (P-259/P-260) — never an independently-typed literal.
    // Injects traceparent (W3C format), tracestate, and x-correlation-id into metadata.
    // Does not overwrite existing x-correlation-id entry.
    // Wraps body in try/catch — logs Error via [LoggerMessage] (EventId 11100,
    // LogCorrelationEnrichmentFailed) and continues on exception (never propagates).

TenantIdInterceptor  [internal sealed — Interceptor]
    // Same four call-type overrides.
    // Resolves ITenantProvider from request scope via IHttpContextAccessor.HttpContext.RequestServices.
    // TenantIdKey is a thin value-forwarding alias of 01.Core's WellKnownHeaders.TenantId
    // (P-259/P-260) — retained as a locally-named const because gRPC metadata keys are
    // conventionally lowercase and the local symbol reads more naturally at call sites;
    // never re-declared as an independent literal.
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
    // Also registers a safety-net IClock (services.TryAddSingleton<IClock, SystemClock>(), P-359/WO-056 —
    // shipped) so AddGrpcClient<TClient>'s real per-call deadline enforcement works with zero new
    // caller-side setup; a consuming service's own IClock registration always wins. And registers
    // GrpcClientOptionsValidator as IValidateOptions<GrpcClientOptions> (not the enforcement mechanism
    // relied upon — see gRPC rules below).
```

### `SharedKernel.Communication.GraphQL` — public surface

```text
AddSharedKernelGraphQL(this IServiceCollection, Action<GraphQLOptions>? configure = null)
    → IRequestExecutorBuilder
    // Must be called BEFORE any service-specific AddGraphQL()/AddTypes() calls.
    // Wires: snake_case naming, SharedKernelFilterConvention, offset + cursor pagination,
    //        SharedKernelErrorFilter, MaxPageSize cap, AllowIntrospection gate.
    // Idempotent — safe to call twice (second call is a no-op).
    // CORRECTED (P-358/WO-056, shipped): GraphQLOptionsValidator.Validate(name: null, options) is now
    // called directly against the locally-constructed options instance, immediately after
    // configure?.Invoke(options) and BEFORE ModifyPagingOptions/DisableIntrospection ever apply its
    // values to HotChocolate — throws OptionsValidationException synchronously on failure. The
    // registered services.AddSingleton<IValidateOptions<GraphQLOptions>, GraphQLOptionsValidator>()
    // is retained but was never actually reachable through the IOptions<T> pipeline (GraphQLOptions
    // is never resolved via IOptions<T>.Value here), for the identical structural reason documented
    // for .Rest's RestClientOptionsValidator.
    // IMPLEMENTATION NOTE: OptionsValidationException's optionsName constructor parameter is
    // non-nullable (string, not string?) — passing null literally does not compile under this
    // project's Nullable=enable + TreatWarningsAsErrors=true. The shipped fix passes string.Empty as
    // the "no name" sentinel instead, since GraphQLOptions is a single unnamed options instance with
    // no per-client name concept (unlike RestClientOptions/GrpcClientOptions, which pass a real
    // name/clientName). Any future single-instance, non-named IValidateOptions<T> validate-at-point-
    // of-consumption fix in this domain should follow the same string.Empty convention, not null.

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
    // CORRECTED (P-357/WO-056): cache-hit and cache-write expiry comparisons now read the current
    // instant via an injected IClock, never a direct DateTimeOffset.UtcNow call (the confirmed
    // SK0001 finding P-255/T-28 flagged as an unfixed follow-up and never turned into a phase until
    // now) — see the Internal package's Cross-cutting rules below for the AddK8sServiceDiscovery
    // safety-net IClock registration.

AddK8sServiceDiscovery(this IServiceCollection, Action<K8sServiceDiscoveryOptions>? configure = null)
    → IServiceCollection
    // Registers KubernetesServiceEndpointResolver as IServiceEndpointResolver (singleton).
    // Wires Microsoft.Extensions.ServiceDiscovery DNS resolver.
    // Registers a safety-net IClock (services.TryAddSingleton<IClock, SystemClock>()) so this
    // extension keeps working with zero new caller-side setup — a consuming service's own IClock
    // registration, of any implementation, always wins (P-357/WO-056).
    // CORRECTED (P-360/WO-056): now throws InvalidOperationException if IServiceEndpointResolver is
    // already registered — the exact same guard AddStaticServiceDiscovery already had, closing an
    // asymmetry where this direction previously silently no-op'd via TryAddSingleton, leaving
    // whichever resolver was registered first (e.g. StaticServiceEndpointResolver) silently active.

AddStaticServiceDiscovery(this IServiceCollection, Dictionary<string, Uri> endpoints)
    → IServiceCollection
    // Registers StaticServiceEndpointResolver (singleton). Dev/test only.
    // Throws InvalidOperationException if IServiceEndpointResolver already registered.
    // Logs LogLevel.Warning at startup via StaticServiceDiscoveryStartupWarning (IHostedService),
    // via [LoggerMessage] (EventId 11308, LogStaticServiceDiscoveryActive).
    // Symmetric with AddK8sServiceDiscovery's identical guard as of P-360/WO-056 — whichever
    // registration call runs second against an already-registered resolver throws, in both directions.
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
- The handler pipeline order is fixed: `CorrelationIdDelegatingHandler` → `TenantIdDelegatingHandler` → `IdempotencyKeyDelegatingHandler` (conditional — only when `EnableIdempotencyKeyPropagation = true`, P-364/WO-056) → `StandardResilienceHandler` → transport. The idempotency handler must run **before** `StandardResilienceHandler` so the key is set once, before the first attempt, and survives unchanged through every retry.
- `TimeoutSeconds` is applied as a per-request timeout via `StandardResilienceHandler`, not as a global `HttpClient.Timeout`.
- `TotalRequestTimeout` formula: `TimeoutSeconds × (RetryCount + 1) + TotalTimeoutBufferSec`. The buffer (default 10 s) accounts for jitter headroom and circuit-breaker probe time. Consumers can set `TotalTimeoutBufferSec = 0` for tight latency budgets.
- **`StandardResilienceHandler` lesson (DO-05):** Polly v8's circuit-breaker strategy enforces a hard validation constraint — `CircuitBreaker.SamplingDuration` must be at least `2 × AttemptTimeout.Timeout`, or `AddStandardResilienceHandler` throws at configuration time. `RestCommunicationBuilder.AddRestClient<TClient>` guards this automatically: it computes `minimumSamplingDuration = attemptTimeout × 2 + 1 tick` and silently raises `SamplingDurationSec` to that floor whenever a caller's configured value would violate the constraint (e.g. a short `TimeoutSeconds` combined with the default 30 s `SamplingDurationSec` is safe, but a long `TimeoutSeconds` paired with a short `SamplingDurationSec` is not). This auto-adjustment is silent by design — it never throws back to the caller — so consumers tuning `TimeoutSeconds` and `RestResilienceOptions.SamplingDurationSec` together should be aware the effective sampling window may be larger than the value they set.
- `RestClientOptionsValidator` **must** be registered in `AddSharedKernelRestCommunication` — it must never be left unregistered silently. **Registration alone is not enough (P-358/WO-056 correction):** `RestClientOptions` is never resolved via `IOptions<RestClientOptions>.Value` by application code — it is constructed directly inside `AddRestClient<TClient>` and applied immediately to `IHttpClientBuilder`. A registered-but-never-actually-invoked `IValidateOptions<T>` is therefore a **structurally dead validator**, indistinguishable at review time from a correctly-wired one — this is exactly the defect P-358 closed. `RestCommunicationBuilder.AddRestClient<TClient>` **must** call `RestClientOptionsValidator`'s `Validate(name, options)` directly against the just-constructed `options` instance, immediately after `configure?.Invoke(options)` and before the `HttpClient`/resilience pipeline is built, throwing `Microsoft.Extensions.Options.OptionsValidationException(name, typeof(RestClientOptions), result.Failures)` on failure. The same rule applies to `RestResilienceOptions`'s numeric fields, which gained range validation under the same phase — see the field table above.
- **Idempotency-key propagation (P-364/WO-056):** `IdempotencyKeyDelegatingHandler` is opt-in only, enabled per typed client via `RestClientOptions.EnableIdempotencyKeyPropagation = true`. When enabled, it attaches a stable idempotency-key header before the first Polly attempt and never regenerates it on retry — `StandardResilienceHandler` retries re-send the same `HttpRequestMessage` instance, so a simple "header already present" check keeps the key stable across every retry of one logical call. Same no-overwrite contract as the correlation/tenant handlers: a caller-supplied key always wins. Disabled by default because `StandardResilienceHandler`'s default `RetryCount = 3` means every typed client already silently re-issues non-idempotent HTTP verbs (POST/PATCH/DELETE) on transient failure — this handler converts that existing, easy-to-trigger duplicate-side-effect hazard into an explicit, documented guarantee when a downstream service can consume it.
- `RestCommunicationBuilder` captures resolver presence as `bool _resolverRegistered` at construction time (`Services.Any(...)` must not be called per `AddRestClient<TClient>` invocation).
- `ServiceDiscoveryResolvingHandler` must **never** be registered as a shared DI type when service discovery is used. Each typed client gets its own instance via an inline `AddHttpMessageHandler(sp => new ServiceDiscoveryResolvingHandler(..., capturedName))` factory closure.
- `RestClientOptions.ServiceName` (nullable `string?`) overrides the `name` parameter for DNS lookup when set. When `null`, the `name` parameter is used. Document clearly in all new client registration examples.

### gRPC rules

- All interceptors are registered globally via `AddGrpcClient<T>().AddInterceptor<T>(InterceptorScope.Channel)` — no per-call interceptor injection. `InterceptorScope` is in the `Grpc.Net.ClientFactory` namespace.
- `CorrelationTracingInterceptor` must read `Activity.Current` at the **moment of the call**, not at DI registration time. When no ambient trace is active, the fallback correlation ID must be `Guid.NewGuid().ToString()` (hyphenated `"D"` format) — never `Guid.NewGuid().ToString("N")`, a confirmed SK0011 violation fixed under P-356/WO-056.
- **`GrpcClientOptions.DeadlineSeconds` is a real, enforced per-call deadline (P-359/WO-056, shipped):** every client built via `AddGrpcClient<TClient>` applies `DeadlineSeconds` to every outgoing call — a documented, type-safe, previously-never-consulted option was the same class of defect `07.Messaging`'s dead `AzureServiceBusOptions.MaxConcurrentCalls` was (WO-054/P-342). **Verified empirically against the real `Grpc.Net.ClientFactory` 2.80.0 assembly (by reflection over the installed NuGet DLL, not assumed from prose)** — `IHttpClientBuilder` has **no** `ConfigureDefaultCallOptions(...)` method in this version; that was an unverified assumption in the original design. The real mechanism is `GrpcClientFactoryOptions.CallOptionsActions` (`IList<Action<CallOptionsContext>>`), populated inside the same `(IServiceProvider, GrpcClientFactoryOptions)` configure delegate already passed to `services.AddGrpcClient<TClient>(...)` for address resolution — `GrpcCommunicationBuilder.AddGrpcClient<TClient>` resolves `IClock` once from that closure's `sp`, then adds a `CallOptionsActions` entry that reads `clock.UtcNow` fresh at call time (never captured ahead of time) to compute `CallOptions.Deadline`; never a direct `DateTime.UtcNow`/`DateTimeOffset.UtcNow` call — reintroducing that violation while fixing the adjacent P-357 `KubernetesServiceEndpointResolver` finding would have been self-defeating. **Design refinement beyond the original P-359 text:** the deadline is applied only when `CallOptions.Deadline is null`, so it never overwrites a per-call deadline the caller already supplied through the generated client's own `CallOptions` overload — mirroring this domain's existing "caller-supplied value always wins" convention for `x-correlation-id`/`x-tenant-id`. `AddSharedKernelGrpcCommunication` registers a safety-net `services.TryAddSingleton<IClock, SystemClock>()` (mirroring `AddK8sServiceDiscovery`'s identical P-357 safety net) so this newly-added `IClock` dependency needs zero new caller-side setup. `GrpcClientOptionsValidator` rejects `DeadlineSeconds <= 0` at registration time, invoked the same direct-call-at-point-of-consumption way `RestClientOptionsValidator` is (see REST client rules' P-358 note) — `GrpcClientOptions` is likewise never resolved via `IOptions<T>.Value`, so the same structural-dead-validator trap applies here by default; it is also registered as `IValidateOptions<GrpcClientOptions>` in DI for any future direct `IOptions<T>` consumer.
- Interceptors must catch **all** exceptions, log at `Error` level, and continue — they must never propagate exceptions into the gRPC call pipeline.
- `MoneyProtoExtensions` and `TimestampProtoExtensions` are pure, static, and allocation-minimal — no `new()` allocations for conversion; no `ToString()`-based intermediate representations. Money conversion: `Units` (int64) + `Nanos` (int32, billionths) where `NanosPerUnit = 1_000_000_000`.
- `GrpcChannel` instances are expensive — rely on `Grpc.Net.ClientFactory` channel caching (singleton pattern); never create a new `GrpcChannel.ForAddress()` per call.
- TLS is configured at the channel level only (`ChannelCredentials.Insecure` for HTTP, `SslCredentials` for HTTPS) — no per-call TLS configuration.
- Caller-supplied `x-correlation-id` or `x-tenant-id` metadata entries must never be overwritten by the interceptors.
- gRPC retry policy is configured via `ServiceConfig` / `MethodConfig` / `RetryPolicy` in the `Grpc.Net.Client.Configuration` namespace — not via Polly. Wired into `GrpcChannelOptions.ServiceConfig` on the channel options.
- **Namespace alias required:** The project namespace `SharedKernel.Communication.Grpc` collides with the `Grpc.Core` NuGet namespace. Always add `using GrpcCore = Grpc.Core;` in files that reference both.
- When `IServiceEndpointResolver` is present and `Address` is omitted: resolve address synchronously at channel creation using `.GetAwaiter().GetResult()` inside the `AddGrpcClient<T>((sp, o) => ...)` factory action. This is safe because (a) `KubernetesServiceEndpointResolver.ResolveAsync` never throws, and (b) the channel is a singleton created once.
- gRPC `Metadata` entries must be cloned before adding new entries to avoid mutating the original `CallOptions.Headers`. Use `GrpcMetadataHelper.CloneAndAdd` — the single authoritative implementation.
- **`Grpc.Core.Metadata` key-casing (P-260, verified empirically against `Grpc.Core.Api` 2.80.0):** `Metadata.Add`/`Metadata.Entry` normalize entry keys to lowercase internally — adding a key such as `"X-Tenant-Id"` is accepted without throwing and is stored/read back as `"x-tenant-id"`. This is why `TenantIdInterceptor.TenantIdKey`/`CorrelationTracingInterceptor.CorrelationIdKey` can source their value directly from `01.Core`'s uppercase-hyphenated `WellKnownHeaders.TenantId`/`.CorrelationId` constants (G-16/G-17) with zero behavioral or wire-format change, despite gRPC metadata keys conventionally being written in lowercase.
- `GrpcMetadataHelper` is the sole location for `HasMetadataEntry` and `CloneAndAdd` logic. Neither interceptor may define its own local copy of these helpers — all metadata manipulation delegates to `GrpcMetadataHelper`.
- The `SharedKernel.Contracts` (`04.Contracts`) project reference must **not** appear in `SharedKernel.Communication.Grpc.csproj`. The gRPC package uses Protobuf-generated types directly; `Envelope<T>`, `PagedList<T>`, and `EventEnvelope<T>` have no place in gRPC package code.

### GraphQL rules

- `AddSharedKernelGraphQL` must be called **before** any service-specific `AddGraphQL()` / `AddTypes()` calls — it establishes the base convention all types inherit.
- `AllowIntrospection` must be `false` in non-development environments — consuming services are responsible for environment-gating this flag in their `Program.cs`.
- `FilterBase<T>` and `SortBase<T>` are mandatory base classes. Direct registration of `FilterInputType<T>` or `SortInputType<T>` without the base wrapper is a platform violation.
- GraphQL error responses must map to the same `ProblemDetails` shape as REST responses — `SharedKernelErrorFilter` handles this automatically when registered via `AddSharedKernelGraphQL`.
- `MaxPageSize` default is 100. Hard cap is 500 — `GraphQLOptions` validator rejects values above 500. Any override beyond 500 requires documented justification in the consuming service.
- `AddSharedKernelGraphQL` is idempotent — calling it twice does not double-register conventions, error filters, or pagination settings.
- **`GraphQLOptions` validation must run against the exact instance applied to HotChocolate (P-358/WO-056 correction):** `AddSharedKernelGraphQL` constructs `GraphQLOptions` locally and applies it directly to `ModifyPagingOptions`/`DisableIntrospection` — never via `IOptions<T>.Value` — so `GraphQLOptionsValidator`'s DI registration alone cannot fire. Call `GraphQLOptionsValidator`'s `Validate(name: null, options)` directly against the locally-constructed instance, immediately after `configure?.Invoke(options)` and before any HotChocolate configuration reads its values, throwing `OptionsValidationException` synchronously on failure. Same structural root cause and same fix pattern as `.Rest`'s `RestClientOptionsValidator`.
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
- **The registration guard is symmetric (P-360/WO-056 correction):** `AddK8sServiceDiscovery` throws the identical `InvalidOperationException` when `IServiceEndpointResolver` is already registered — it must never silently no-op via `TryAddSingleton` when called second. Whichever service-discovery extension runs second against an already-registered resolver must produce the same clear signal, regardless of call order. A developer migrating from static to K8s discovery, or copy-pasting an example, must never lose their intended resolver silently.
- **`KubernetesServiceEndpointResolver` sources current time from an injected `IClock`, never `DateTimeOffset.UtcNow` directly (P-357/WO-056 correction):** both the cache-hit comparison and the cache-write expiry computation in `ResolveAsync` read `clock.UtcNow`. `AddK8sServiceDiscovery` registers a safety-net `services.TryAddSingleton<IClock, SystemClock>()` so the extension keeps working with zero new caller-side setup — a consuming service's own `IClock` registration, of any implementation, always wins over the safety net.
- `StaticServiceEndpointResolver.ResolveAsync` returns the K8s convention URI for unknown service names (never throws) to maintain behavioural parity with `KubernetesServiceEndpointResolver`.
- `KubernetesServiceEndpointResolver` implements a TTL-based in-memory endpoint cache controlled by `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds` (default 30, 0 = disabled). The cache uses `ConcurrentDictionary<string, CachedEntry>` keyed case-insensitively. Stale-while-revalidate: DNS failure with stale entry logs `Warning` and returns stale `Uri` (never throws). DNS failure with no prior entry falls through to K8s convention URI.
- `K8sServiceDiscoveryOptionsValidator` rejects negative `EndpointCacheTtlSeconds` values. Zero is valid (disables caching).
- Both `RestCommunicationBuilder` and `GrpcCommunicationBuilder` capture resolver presence as a `bool` field at construction time — `Services.Any(d => d.ServiceType == typeof(IServiceEndpointResolver))` must only ever be called once, at builder construction, not per client registration.

### Cross-cutting propagation rules

- **Correlation ID** must be propagated in all outbound channels: REST header `x-correlation-id`, gRPC metadata `x-correlation-id`.
- **Tenant ID** is propagated as `x-tenant-id` in REST and gRPC. GraphQL services resolve tenant from the JWT claim server-side — no header propagation.
- All propagation handlers and interceptors are **best-effort** — they silently skip propagation when ambient context is unavailable. They never throw.
- Caller-supplied `x-correlation-id` or `x-tenant-id` values always win — handlers and interceptors must not overwrite headers or metadata entries already set by the caller.
- **Single source of truth for header/metadata names (P-259/P-260):** `x-correlation-id` and `x-tenant-id` are declared exactly once, in `01.Core`'s `WellKnownHeaders` (`CorrelationId`, `TenantId`) — `CorrelationIdDelegatingHandler`, `TenantIdDelegatingHandler`, `TenantIdInterceptor`, and `CorrelationTracingInterceptor` all consume that constant directly rather than independently typing out the literal. A package may keep a locally-named `internal const` **only** as a thin value-forwarding alias to the `01.Core` constant (e.g. `TenantIdInterceptor.TenantIdKey`) when a local symbol name genuinely helps readability — never as a second, independently-typed literal that could drift out of sync. No new project reference is required for either `.Rest` or `.Grpc` — both already reference `01.Core`.

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
| `RestClientOptionsValidator`/`GraphQLOptionsValidator`/`GrpcClientOptionsValidator` not registered, **or** registered but never invoked against the actual instance applied to the client/schema (P-358/WO-056) | Hard violation — options validation silently absent or structurally dead; validation must run synchronously at the point of consumption, immediately after `configure?.Invoke(options)` |
| `GrpcClientOptions.DeadlineSeconds` configured but not applied as a real `CallOptions.Deadline` on every call (P-359/WO-056) | Hard violation — a documented, type-safe option that silently does nothing, mirroring `07.Messaging`'s dead `AzureServiceBusOptions.MaxConcurrentCalls` defect (WO-054/P-342) |
| `AddK8sServiceDiscovery` silently no-ops (e.g. via `TryAddSingleton`) when `IServiceEndpointResolver` is already registered (P-360/WO-056) | Hard violation — the guard must be symmetric with `AddStaticServiceDiscovery`'s existing throw |
| Correlation-ID fallback (`.Rest` or `.Grpc`) synthesized via `Guid.NewGuid().ToString("N")` instead of `Guid.NewGuid().ToString()` (P-356/WO-056) | Hard violation — SK0011 non-canonical GUID format |
| Direct `DateTime.UtcNow`/`DateTimeOffset.UtcNow` call anywhere in `.Internal` or `.Grpc` production code (e.g. computing a gRPC deadline instant) | Hard violation — SK0001; always inject `IClock` (P-357/P-359/WO-056) |
| A generic `EnsureSuccessOrErrorAsync<T>`-shaped method reintroduced that returns a default/unpopulated value on success | Hard violation — P-361/WO-056 retired exactly this shape; use `ReadEnvelopeAsync<T>` for a deserialized payload, the non-generic `EnsureSuccessOrErrorAsync` for a status-check-only outcome |
| `IdempotencyKeyDelegatingHandler` regenerates its key value on a Polly retry, or overwrites a caller-supplied `x-idempotency-key` header | Hard violation — P-364/WO-056; the same value must survive every retry of one logical call |
| `RestClientOptionsValidator` not registered in `AddSharedKernelRestCommunication` | Hard violation — options validation silently absent |
| `Services.Any(d => ...)` called inside `AddRestClient` or `AddGrpcClient` per-registration (O(n) probe) | Violation — resolver presence captured once at builder construction |
| `ServiceDiscoveryResolvingHandler` registered as a shared DI type when multiple clients need distinct service names | Hard violation — per-client closure factory required |
| `SharedKernel.Contracts` project reference in `SharedKernel.Communication.Grpc.csproj` | Violation — gRPC package must not reference 04.Contracts |
| `CorrelationTracingInterceptor` or `TenantIdInterceptor` defining local `HasMetadataEntry` or `CloneAndAdd` | Violation — must delegate to `GrpcMetadataHelper` |
| Negative `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds` | Violation — validator must reject |
| Direct `ILogger.LogXxx(...)` extension-method call or hand-written `LoggerMessage.Define<>()` delegate anywhere in `.Grpc` or `.Internal` production code | Hard violation — always author via `[LoggerMessage]` with an explicit `EventId` inside this domain's reserved sub-block; enforced by `00.Governance` SK0020/SK0021 (P-250) |
| `EventId` used outside this domain's reserved range (11000-11999) or outside its package's own 100-wide sub-block | Hard violation — verified by `00.Governance`'s `LoggingEventIdIntegrityAssertion` (P-250) once real-assembly wiring lands |
| `x-correlation-id` or `x-tenant-id` re-declared as an independently-typed literal `const` instead of sourcing from `01.Core`'s `WellKnownHeaders` | Violation (P-259/P-260) — a locally-named `const` may only be a thin value-forwarding alias, never a second independent literal |

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

// REST client with opt-in idempotency-key propagation (P-364/WO-056) — attaches a stable
// x-idempotency-key header before the first Polly attempt and reuses it across every retry
services.AddSharedKernelRestCommunication()
        .AddRestClient<IPaymentServiceClient>(options => {
            options.BaseAddress = "http://payment-service";
            options.EnableIdempotencyKeyPropagation = true;   // POST/PATCH/DELETE calls now safe to retry
        });

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
- **TTL cache tests must use `16.Testing`'s `FakeClock`, never a real wall-clock sleep (P-357/WO-056 correction):** cache-hit, cache-expired, and stale-while-revalidate branches are driven deterministically by advancing `FakeClock`'s current instant — a real-time-dependent test (`Thread.Sleep`/`Task.Delay` waiting for a TTL to elapse) is a regression back to the flaky, slow pattern this phase eliminated.
- **Correlation-ID/idempotency-key fallback-format tests must assert the canonical hyphenated shape specifically, not just `Guid.TryParse` success (P-356/WO-056):** a bare `TryParse` accepts both the hyphenated `"D"` format and the non-hyphenated `"N"` format, so it would not catch a regression back to `.ToString("N")`. Assert the string contains hyphens, or compare against `Guid.TryParseExact(value, "D", out _)`.
- `StaticServiceEndpointResolver` unit tests: registered service → correct `Uri`; unregistered service → K8s convention URL; `ResolveAsync` never throws.
- REST resilience tests: use `HttpMessageHandler` test doubles to simulate transient failures; assert retry fires `RetryCount` times; assert circuit breaker opens after `FailureThreshold`.
- GraphQL tests: use HotChocolate's `IRequestExecutor` test builder; assert filter + sort + paging work with configured convention; assert `MaxPageSize` enforcement; assert error mapping to `ProblemDetails` shape.
- All test projects reference `16.Testing/SharedKernel.Testing` for shared helpers.
- Integration tests making real network calls must carry `[Trait("Category", "Integration")]` and must be skipped in unit-only CI runs.
- **`consumer-verify` (P-362/WO-056):** a dedicated project at `11.Communication/consumer-verify/`, mirroring `17.Workflows/consumer-verify`'s shape exactly — a real `Host.CreateApplicationBuilder()` → `IHost.StartAsync()` composition (never `BuildServiceProvider()` alone) proving all four packages' DI entry points resolve cleanly, plus the two P-358 regression proofs (a deliberately invalid `RestClientOptions`/`GraphQLOptions` each fail `IHost.StartAsync()` loudly via `OptionsValidationException`, not silently). This is the class of proof a unit test calling a validator object directly does not provide, and is exactly what would have caught the original dead-validator defect. Not a `.Tests` project — `IsPackable=false`, `OutputType=Exe`, never shipped.
- **`[LoggerMessage]` `EventId`/`Level` regression coverage (WO-041, P-255, T-27):** verify via reflection over the compiled type (`GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).GetCustomAttribute<LoggerMessageAttribute>()`, asserting `.EventId`/`.Level`) rather than only exercising the runtime call path — several call sites (DNS-failure fallback, stale-cache-used, SRV/A-record-lookup-failed) are not reachable through the pass-through `IServiceEndpointResolver` test provider (`AddPassThroughServiceEndpointProvider`, `Microsoft.Extensions.ServiceDiscovery`'s own always-succeeds test provider) without a fault-injectable DNS provider, matching the same documented limitation `TtlCacheTests` already records for its `[Trait("Category", "Integration")]`-tagged, skipped stale-cache tests. Combine with a runtime capturing-`ILogger<T>`/`TestLogSink` assertion for every call site that IS reachable (e.g. `TenantIdInterceptor`'s injected-exception path; `KubernetesServiceEndpointResolver`'s SRV-lookup-attempt/service-resolved/cache-hit paths on a successful pass-through resolution) so the retrofit is verified both statically (attribute-correct) and dynamically (actually fires) wherever feasible.
- **Real-assembly `LoggingEventIdIntegrityAssertion` invocation (WO-041, P-255, T-28):** each package's own `.Tests` project carries a **test-project-only** `ProjectReference` to `00.Governance/SharedKernel.ArchitectureTests` (never from the production `.csproj`) — mirrors the cross-domain precedent already established by `05.Application.Behaviors.Tests` and `SharedKernel.Messaging.MassTransit.Tests`. Requires bumping `FluentAssertions` to `8.10.0` in the referencing test project (`SharedKernel.ArchitectureTests` itself pins 8.10.0; a lower pin triggers `NU1605`). A cross-assembly "no EventId collision between `.Grpc` and `.Internal`" test belongs in **`SharedKernel.Communication.Grpc.Tests` only** — never `.Internal.Tests` — because `.Grpc`'s production `.csproj` already legitimately references `.Internal` (G-09 service-discovery integration); the reverse direction from `.Internal.Tests` would invert `CommunicationLayeringRules.CommunicationInternalNeverReferencesOtherCommunicationPackages`, even as a test-only reference. When a test needs `typeof(SomeInternalType).Assembly` from outside `.Internal`'s own `InternalsVisibleTo` grant, anchor on a **public** type (`IServiceEndpointResolver`, `K8sServiceDiscoveryOptions`) — not an `internal sealed` implementation type (`KubernetesServiceEndpointResolver`), which only `SharedKernel.Communication.Internal.Tests` can see.
- **SK0020/SK0021 zero-diagnostics verification (WO-041, P-255, T-28):** verified via a **temporary** analyzer `ProjectReference` on the production `.csproj` (`<ProjectReference ... OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` pointing at `00.Governance/SharedKernel.Analyzers`), built with `-p:TreatWarningsAsErrors=false` to surface every analyzer diagnostic as a warning instead of a build-breaking error, grepped for `SK0020`/`SK0021`, then **reverted** — never a permanent wiring change (same technique as `07.Messaging`'s LR-16/LR-17). This surfaced two pre-existing, unrelated findings out of scope for the logging retrofit: `KubernetesServiceEndpointResolver`'s `DateTimeOffset.UtcNow` calls (SK0001 — should inject `IClock`) and `CorrelationTracingInterceptor`'s `Guid.ToString("N")` correlation-ID fallback (SK0011 — non-canonical GUID format) — both flagged here as a candidate follow-up work order at the time, not fixed under this phase's scope. **Fixed under P-357 and P-356 respectively (WO-056)** — see the Changelog entry below and this file's Internal/gRPC rules above for the corrected contracts.
- **SK0011/SK0001 verification-coverage extended to `.Rest`/`.GraphQL` (P-356/WO-056, T-32):** the same temporary-analyzer-`ProjectReference`-build-then-revert technique above was applied to `.Rest.csproj`/`.GraphQL.csproj` for the first time — the original P-255/T-28 pass wired it only into `.Grpc`/`.Internal`, the two packages that had logging at the time, which is exactly why the `.Rest` occurrence of the SK0011 non-canonical-GUID-fallback defect went undetected until this WO-056 review. Result: zero SK0011/SK0001 findings in either package, both builds 0 warnings/0 errors with the analyzer genuinely wired in (confirmed via `SharedKernel.Analyzers.dll` appearing in the build output, not merely a clean exit code), csproj changes fully reverted afterward (`git diff` empty). All four packages are now covered by this verification technique — treat any future SK0011/SK0001-relevant change in `.Rest`/`.GraphQL` as covered by this precedent, not as a fresh gap.
- **`KubernetesServiceEndpointResolver` TTL-cache tests seed the cache via reflection, not a "succeed once, fail later" DNS sequence (P-357/WO-056, T-33):** empirically confirmed (via a throwaway console harness against the real `Microsoft.Extensions.ServiceDiscovery` 10.7.0 assembly) that `ServiceEndpointResolver.GetEndpointsAsync` never re-invokes its `IServiceEndpointProviderFactory` for a query string that has already resolved successfully within that resolver instance — it returns a cached/watched result forever, not a fresh per-call lookup. This means a test cannot make the SAME `KubernetesServiceEndpointResolver`'s underlying DNS resolution succeed once (to populate the cache) and then fail on a later call using the real `ServiceEndpointResolver` machinery. The correct technique instead: directly instantiate `KubernetesServiceEndpointResolver` (its constructor and private `_cache` field are reachable via `InternalsVisibleTo`/reflection — `FieldInfo.GetValue`/`Activator.CreateInstance` against the private nested `CachedEntry` record struct), seed `_cache` directly with a known `Uri`/`ExpiresAt`, then let the underlying DNS provider fail (or succeed) from the very first call — never relying on a real prior success to populate the cache. This is the same class of test-only reflection already sanctioned by this domain's siblings (`16.Testing`'s own `Domain/SpecificationAssert`, `Containers/MilvusContainerFixtureTests`) — never acceptable in production code. A minimal controllable `IServiceEndpointProviderFactory`/`IServiceEndpointProvider` test double pair (one always-failing, one always-succeeding) is sufficient to drive all three TTL-cache branches deterministically via `FakeClock.Advance(...)`.
- **gRPC deadline-exceeded behavioral test technique (P-359/WO-056, T-35):** to prove a call issued through a client built via `AddGrpcClient<TClient>` genuinely faults with `RpcException`/`StatusCode.DeadlineExceeded` once `DeadlineSeconds` elapses, register the typed client normally, then call `Services.AddGrpcClient<TClient>()` (the parameterless overload) a SECOND time and chain `.ConfigurePrimaryHttpMessageHandler(() => new NeverRespondingHandler())` — additive configuration against the same named `HttpClient` `AddGrpcClient<TClient>` already registered, not a conflicting second registration. The typed client needs one real unary method backed by `CallInvoker.AsyncUnaryCall` to actually issue a call; the client's constructor must accept `CallInvoker` (not `ChannelBase`) — `Grpc.Net.ClientFactory`'s `DefaultClientActivator<T>` specifically looks for a `CallInvoker`-accepting constructor when activating a typed client through DI (confirmed by a `ActivatorUtilities`-thrown `InvalidOperationException` when only a `ChannelBase` constructor is present). `NeverRespondingHandler` simply `await Task.Delay(Timeout.Infinite, cancellationToken)`s inside `SendAsync` so the deadline timer, not a real response, is what completes the call.

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
- [2026-07-15] G-16, G-17 (P-260/WO-042) implemented — `TenantIdInterceptor.TenantIdKey`/`CorrelationTracingInterceptor.CorrelationIdKey` now source from `01.Core`'s `WellKnownHeaders.TenantId`/`.CorrelationId` as thin value-forwarding aliases (D-24); empirically verified against `Grpc.Core.Api` 2.80.0 that `Grpc.Core.Metadata` normalizes entry keys to lowercase internally, so sourcing the uppercase-hyphenated `WellKnownHeaders` literals causes zero behavioral/wire-format change — documented as a new gRPC rule; 60/60 `SharedKernel.Communication.Grpc.Tests` passing; SK.11.Grpc now 17/17 ●. T-29/DO-07 remain the only open P-260 tasks (communication-phase-implementer)
- [2026-07-15] T-29, DO-07 (P-260/WO-042) closed — test-local literal duplicates of `x-correlation-id`/`x-tenant-id` replaced with `WellKnownHeaders.CorrelationId`/`.TenantId` references in `CorrelationIdDelegatingHandlerTests.cs`/`TenantIdDelegatingHandlerTests.cs` (`.Rest.Tests`) and `GrpcMetadataHelperTests.cs` (`.Grpc.Tests`); `TenantIdInterceptorTests.cs`/`CorrelationTracingInterceptorTests.cs` already referenced the production `TenantIdKey`/`CorrelationIdKey` symbols, not literals, so needed no change. One test assertion required adjusting for the `Grpc.Core.Metadata` lowercase-normalization behavior documented under gRPC rules (`CloneAndAdd_EmptySource_ResultContainsOnlyNewEntry` now compares the stored key against `WellKnownHeaders.TenantId.ToLowerInvariant()`) — no production behavior changed. DO-07: verified the existing "Cross-cutting propagation rules" and gRPC-rules sections already fully document the `WellKnownHeaders` sourcing and the gRPC thin-alias exception; no edit needed. 66/66 `.Rest.Tests` + 60/60 `.Grpc.Tests` passing. P-260/WO-042 fully closed — every phase key in `11.Communication` is now ● except Published (communication-phase-implementer)
- [2026-07-14] P-260 (WO-042) planned — new phase closing the header-name duplication class flagged alongside P-259/P-261: `CorrelationIdDelegatingHandler`/`TenantIdDelegatingHandler` (`.Rest`) and `TenantIdInterceptor`/`CorrelationTracingInterceptor` (`.Grpc`) each independently declared their own `x-correlation-id`/`x-tenant-id` literal `const`, with no shared source of truth. Design decision (D-24): both packages now consume `01.Core`'s `WellKnownHeaders.CorrelationId`/`.TenantId` (P-259, already shipped) directly; a locally-named `const` survives only as a thin value-forwarding alias where a package benefits from a local symbol name — explicitly carved out for gRPC's lowercase-metadata-key convention (`TenantIdInterceptor.TenantIdKey`, correlation metadata key) — never as a second, independently-typed literal. No new `.csproj` reference needed in either package (both already reference `01.Core`; the P-163 rejection of a `04.Contracts` reference in `.Grpc` remains untouched and unrelated). 8 tasks added: D-24 (contract), R-19/R-20 (Rest retrofit), G-16/G-17 (Grpc retrofit), T-29 (test-literal cleanup across `.Rest.Tests`/`.Grpc.Tests`), DO-07 (CLAUDE.md update). Interface Contracts, propagation rules, and layering-violation guard table updated to document the single-source-of-truth requirement and the thin-alias exception (communication-arch-planner, WO-042)
- [2026-07-31] P-329 (WO-052) planned — new phase adopting `04.Contracts`' P-328 rename of `SharedKernel.Contracts.Envelope` → `SharedKernel.Contracts.Envelopes` (fixing a namespace/type-name collision that forced a `using EnvelopeNs = ...` alias workaround). Source audit (not assumed from prior docs) confirmed the entire blast radius in `11.Communication` is one line: `HttpResponseMessageExtensions.cs`'s `using SharedKernel.Contracts.Envelope;` import — the only production reference to the retired namespace anywhere in this domain. `ReadEnvelopeAsyncTests.cs` never imports the namespace directly (relies on `var` type inference) and needs no change; `.Grpc.csproj`'s doc comment mentioning `Envelope<T>` is prose documenting the P-163 no-reference rule, not an import, and is unaffected. 4 tasks added: D-25 (adoption-contract design confirming single-file scope), R-21 (the one-line `using` swap), T-30 (regression pass — 66 `.Rest.Tests` incl. `ReadEnvelopeAsyncTests`'s 12), DO-08 (this Current Phase note). No Scaffold/Grpc/GraphQL/Internal/Published tasks — nothing else in this domain touches the renamed namespace. Gated on `04.Contracts` shipping P-328 first (`◐` Dispatched, not yet implemented as of this entry) — implementation cannot start until then (communication-arch-planner, WO-052)
- [2026-08-11] WO-056 (P-356–P-364) — nine gap-fill phases dispatched, verified against real shipped `.cs` source (direct file reads, not domain-brain prose) rather than assumed correct because every prior phase key showed `●`. Nine confirmed defects, all design-locked in this file's sections above, all `○` Pending in `state-map.md`:
  - **P-356** — `CorrelationIdDelegatingHandler.cs:24` (`.Rest`) and `CorrelationTracingInterceptor.cs:96` (`.Grpc`) both fall back to `Guid.NewGuid().ToString("N")` — a duplicated SK0011 violation. The `.Rest` occurrence was never caught during the P-255/WO-041 logging retrofit because the temporary-analyzer-`ProjectReference` verification technique used there was wired only into `.Grpc`/`.Internal`'s `.csproj` files, never `.Rest`'s — a verification-coverage gap, not a false "clean" claim. Fix: canonical `Guid.NewGuid().ToString()` (hyphenated `"D"` format) in both call sites.
  - **P-357** — `KubernetesServiceEndpointResolver.cs:93,107` call `DateTimeOffset.UtcNow` directly — the exact SK0001 finding P-255/T-28 surfaced via a temporary SK0020/SK0021-verification analyzer pass and flagged as an unfixed "candidate follow-up," never turned into a phase until now. `16.Testing`'s `FakeClock` already exists and ships today, so no new `16.Testing` work is needed to fix it — only `.Internal`'s own constructor/DI wiring changes.
  - **P-358** — `RestCommunicationBuilder.AddRestClient<TClient>` constructs `RestClientOptions` via `new()` + direct `configure?.Invoke(options)`, never through `IOptions<T>`, so the registered `RestClientOptionsValidator` (confirmed nested inside `RestClientOptions.cs`, validating only `BaseAddress`/`TimeoutSeconds`) can never structurally fire. `AddSharedKernelGraphQL` has the identical shape — applies its locally-built `GraphQLOptions` to HotChocolate's paging config before the registered `GraphQLOptionsValidator` ever runs. Fix locked as validate-at-point-of-consumption (direct `Validate(...)` call + `OptionsValidationException` throw, immediately after `configure?.Invoke`) for both packages, deliberately not forced into the standard `.ValidateOnStart()` shape neither options type actually needs. `RestResilienceOptions`'s numeric fields (confirmed to carry zero range validation today) gain it in the same pass.
  - **P-359** — `GrpcCommunicationBuilder.cs` never references `DeadlineSeconds` anywhere (confirmed via grep); no `GrpcClientOptionsValidator` exists at all (the `Address`-required check is inline, not `IValidateOptions`-based). Design requires the deadline-instant computation route through `IClock` rather than reintroduce a fresh `DateTimeOffset.UtcNow` call while P-357 fixes an adjacent one in the same domain.
  - **P-360** — `ServiceCollectionExtensions.cs` (`.Internal`) confirmed the exact asymmetry: `AddK8sServiceDiscovery` uses `TryAddSingleton` (silent no-op) while `AddStaticServiceDiscovery` uses `services.Any(...)` + `InvalidOperationException`.
  - **P-361** — `HttpResponseMessageExtensions.EnsureSuccessOrErrorAsync<T>` confirmed returning `Result<T>.Success(default!)` unconditionally on 2xx; its own XML doc already half-admits the gap. Fix is **retirement**, not patching — this package has never been packed or published, so a breaking rename costs nothing today and avoids triplicating deserialization logic that `ReadEnvelopeAsync<T>`'s two overloads already implement correctly.
  - **P-362** — a `consumer-verify` project, scaffolded to mirror `17.Workflows/consumer-verify` (read in full as the grounding precedent for this domain's harness), proving all four DI entry points and the P-358 fix through a real `IHost.StartAsync()`. `11.Communication` was the one `consumer-verify`-precedented domain still without one, despite being referenced by every consuming service in the platform.
  - **P-363** — re-verification against the real `.csproj` files found `PB-01`–`PB-04`'s NuGet metadata (`PackageId`/`Version`/`Authors`/license/repository/copyright/`GenerateDocumentationFile`/`TreatWarningsAsErrors`/symbols) already fully complete on all four packages — corrected from `○` to `●` in `state-map.md` rather than re-planned. Only `PackageReadmeFile`/README content was genuinely missing; `PB-05`/`PB-06` (pack/publish) are now explicitly gated on the full WO-056 correctness bundle landing first, since this is the domain's first-ever release.
  - **P-364** — a new opt-in `IdempotencyKeyDelegatingHandler`, completing the platform's idempotency story (`05.Application`'s `IIdempotentRequest`, `07.Messaging`'s `IIdempotencyStore`) at the outbound-HTTP level, converting the existing (and previously undocumented) hazard that `StandardResilienceHandler`'s default retries already silently re-issue non-idempotent verbs into an explicit, opt-in guarantee.

  34 new tasks added across all nine phase keys (D-26–D-32, S-14, R-22–R-26, G-18–G-20, GQ-10, I-12/I-13, T-31–T-38, DO-09–DO-15, PB-07/PB-08), plus PB-01–PB-04 corrected `○`→`●`. Interface Contracts, Implementation Rules, DI Registration, and Test Rules sections above already describe the corrected, post-fix target design this review locked — the corresponding `.cs` source has not yet been changed to match. No Layering Rules, Package Naming, or breaking-change concerns beyond the deliberate `EnsureSuccessOrErrorAsync<T>` retirement, safe pre-publish with zero external consumers (communication-arch-planner, user request, WO-056, P-356–P-364)
- [2026-08-11] GQ-10 (P-358/WO-056) shipped — `AddSharedKernelGraphQL` now calls `GraphQLOptionsValidator.Validate(name: null, options)` directly against the locally-constructed `options` instance, immediately after `configure?.Invoke(options)` and before `services.Configure<GraphQLOptions>(...)`/`ModifyPagingOptions`/`DisableIntrospection` ever apply its values to HotChocolate, throwing `OptionsValidationException` on failure — the GraphQL half of P-358's validate-at-point-of-consumption fix (the `.Rest` half shipped earlier in R-23/R-24). Discovered during implementation: `OptionsValidationException`'s `optionsName` constructor parameter is non-nullable, so the shipped code passes `string.Empty` rather than `null` as D-28's design text literally said — documented as an implementation note directly under `AddSharedKernelGraphQL` in Interface Contracts. Also fixed a pre-existing, unrelated blocker: `SharedKernel.Communication.GraphQL.Tests.csproj` pinned `Microsoft.Extensions.DependencyInjection` to 10.0.5 (one version behind `SharedKernel.Testing`'s 10.0.9 floor), causing a hard `NU1605` restore failure — bumped to 10.0.9 to match `.Rest`/`.Grpc`/`.Internal`'s test projects. 43/43 `SharedKernel.Communication.GraphQL.Tests` passing, zero regressions. `SK.11.GraphQL` now 10/10 ●, promoted to root state-map. T-34 (startup-level DI-resolution regression test) and DO-11 (CLAUDE.md hard-rule wording correction) remain queued, deferred to future Tests/Docs-phase sessions per this phase's own scope, mirroring the R-22–R-26/G-18–G-20 precedent (communication-phase-implementer)
- [2026-08-11] G-18–G-20 (WO-056) shipped in `.Grpc` — canonical GUID fallback (P-356); real per-call `CallOptions.Deadline` via the empirically-verified `GrpcClientFactoryOptions.CallOptionsActions` (not the assumed-but-nonexistent `ConfigureDefaultCallOptions`), IClock-sourced, never overwriting a caller-supplied deadline; new `GrpcClientOptionsValidator` (P-359). `AddSharedKernelGrpcCommunication` gained a safety-net `IClock` registration. gRPC rules and `AddSharedKernelGrpcCommunication` contract corrected to name the real mechanism; Current Phase note updated — `SK.11.Design`/`Scaffold`/`Rest`/`Grpc` now `●`, five phase keys remain. 60/60 `SharedKernel.Communication.Grpc.Tests` passing; T-31/T-32/T-35 (dedicated regression tests) deliberately deferred to a future Tests-phase session (communication-phase-implementer)
- [2026-08-12] I-12, I-13 (WO-056) shipped in `.Internal` — `KubernetesServiceEndpointResolver` now takes an injected `IClock` and sources both TTL-cache time comparisons from `clock.UtcNow` (P-357), never `DateTimeOffset.UtcNow` directly; `AddK8sServiceDiscovery` gained a safety-net `services.TryAddSingleton<IClock, SystemClock>()` mirroring `.Grpc`'s identical P-359 pattern, and its registration guard is now symmetric with `AddStaticServiceDiscovery`'s `services.Any(...)` + `InvalidOperationException` shape (P-360), replacing the prior silent `TryAddSingleton` no-op. This closes the SK0001/registration-asymmetry defects P-255/T-28 had flagged as an unfixed follow-up back on 2026-07-13. Interface Contracts/Implementation Rules for `.Internal` needed no correction — they already described this exact target design ahead of implementation, matching the shipped code verbatim. 52/52 `SharedKernel.Communication.Internal.Tests` passing, zero regressions; `SK.11.Internal` now 13/13 `●`, promoted to root state-map — Rest/Grpc/GraphQL/Internal all `●`, five phase keys (Tests/Docs/Published, plus the two Design/Scaffold parents already `●`) remain, four of nine WO-056 phase keys closed. T-33/T-36 (dedicated `FakeClock` TTL-cache and symmetric-guard regression tests) deliberately deferred to a future Tests-phase session, mirroring the R-22–R-26/G-18–G-20/GQ-10 precedent (communication-phase-implementer)
- [2026-08-12] T-31–T-38 (WO-056) shipped — all eight deferred regression/DI-resolution test tasks across all four packages, closing WO-056's `SK.11.Tests` phase key. T-31: canonical hyphenated-GUID-fallback regression cases added to `CorrelationIdDelegatingHandlerTests`/`CorrelationTracingInterceptorTests`, asserting `Guid.TryParseExact(value, "D", out _)` and hyphen presence, not merely `Guid.TryParse`. T-32: the temporary-analyzer-`ProjectReference` SK0011/SK0001 verification technique extended to `.Rest`/`.GraphQL` for the first time — zero findings, closing the verification-coverage gap that let the `.Rest` SK0011 occurrence go undetected since the original P-255 pass; see the new Test Rules bullet above. T-33: new `FakeClockTtlCacheTests.cs` in `.Internal` — three deterministic tests (cache-hit/cache-expired/stale-while-revalidate) seeding `KubernetesServiceEndpointResolver`'s private `_cache` via reflection, since the real `ServiceEndpointResolver` never re-queries a successfully-resolved query string within one instance (empirically confirmed; see the new Test Rules bullet above). T-34: `AddRestClient<TClient>`/`AddSharedKernelGraphQL` both proven to throw `OptionsValidationException` synchronously at the real registration call site for invalid `TimeoutSeconds`/`RetryCount`/`TotalTimeoutBufferSec`/`MaxPageSize`. T-35: `GrpcCommunicationBuilderTests` gained a `DeadlineSeconds`-registration-time validator proof plus a genuine deadline-exceeded behavioral proof (`RpcException`/`StatusCode.DeadlineExceeded`) via a `CallInvoker`-constructed typed client and a `NeverRespondingHandler` swapped in through an additive second `AddGrpcClient<TClient>()` call — see the new Test Rules bullet above for the exact technique. T-36: `ServiceCollectionExtensionsTests` gained the missing reverse-direction case (`AddStaticServiceDiscovery` then `AddK8sServiceDiscovery` now also throws), proving the guard is symmetric in both call orders. T-37: confirmed via repo-wide grep that zero `EnsureSuccessOrErrorAsync<T>` call sites remain anywhere in this domain; a new case proves the non-generic replacement never reads the response body on the 2xx path (`ThrowingContent` test double). T-38: new `IdempotencyKeyDelegatingHandlerTests.cs` — hyphenated-key-generated-when-absent, caller-key-never-overwritten, same-key-reused-across-retries (via `HttpMessageInvoker` re-sending one `HttpRequestMessage` instance), and behavioral (not merely DI-resolution) pipeline-presence/absence proofs gated on `EnableIdempotencyKeyPropagation`. Full regression: `.Rest.Tests` 77/77 (was 66), `.Grpc.Tests` 64/64 (was 60), `.GraphQL.Tests` 45/45 (was 43), `.Internal.Tests` 57/57 (was 52) — 243/243, zero regressions. `SK.11.Tests` now 38/38 `●`, promoted to root state-map — five of nine WO-056 phase keys closed (Rest/Grpc/GraphQL/Internal/Tests); Docs (DO-09–DO-15) and Published (PB-07/PB-08) remain (communication-phase-implementer)
