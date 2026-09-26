# 11.Communication — Domain Brain

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read each package's own `README.md`; the folder overview is [`README.md`](README.md).
> Task history lives in [`state-map.md`](state-map.md).

## What This Domain Is

Outbound service-to-service calls: typed REST clients, gRPC clients and in-cluster service discovery. Resilience,
caller propagation (correlation id, tenant, actor, client) and address resolution are wired once at the composition
root and invisible to application code.

Philosophy: **Protocol-Agnostic Resilience. Propagate Context Always. Fail Informatively.**

> **Scope:** outgoing calls only. Inbound concerns — ASP.NET Core middleware, server-side gRPC conventions and the
> GraphQL server conventions (`SharedKernel.Presentation.GraphQL`, formerly `SharedKernel.Communication.GraphQL`) —
> belong in `14.Presentation`. Message-bus publishing belongs in `07.Messaging`.

---

## Packages

| Package | Tier | Role | References |
| ------- | ---- | ---- | ---------- |
| `SharedKernel.Communication.Internal` | Adapter | `IServiceEndpointResolver`, `KubernetesServiceEndpointResolver` (DNS SRV + A-record, TTL cache), `StaticServiceEndpointResolver` (dev/test), `AddK8sServiceDiscovery`/`AddStaticServiceDiscovery` | `SharedKernel.Primitives`, `Microsoft.Extensions.ServiceDiscovery`, `Microsoft.Extensions.Hosting.Abstractions` |
| `SharedKernel.Communication.Rest` | Adapter | Typed `HttpClient` with `StandardResilienceHandler`, `RequestContextDelegatingHandler`, opt-in `IdempotencyKeyDelegatingHandler`, ProblemDetails → `Error`, `ReadResultAsync<T>`/`EnsureSuccessOrErrorAsync`, `IRestCommunicationBuilder` | `SharedKernel.Primitives`, `SharedKernel.Execution`, `.Internal` (declared adapter edge), `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience` |
| `SharedKernel.Communication.Grpc` | Adapter | Typed gRPC clients via `Grpc.Net.ClientFactory`, `CorrelationTracingInterceptor`, `TenantIdInterceptor`, enforced per-call deadline, `MoneyProtoExtensions`/`TimestampProtoExtensions`, `IGrpcCommunicationBuilder` | `SharedKernel.Primitives`, `SharedKernel.Execution`, `.Internal` (declared adapter edge), `Grpc.Net.Client`, `Grpc.Net.ClientFactory`, `Google.Protobuf`, `Google.Api.CommonProtos`, `OpenTelemetry.Instrumentation.GrpcNetClient` |

Test doubles for consumers: `16.Testing/SharedKernel.Communication.Testing` (`MockServiceEndpointResolver`,
`TestServerCallContext`), Testing tier.

**Dependency rules (enforced by the tier build check, `eng/SharedKernelTiers.targets`):**

- All three packages are **Adapter** tier: they reference Foundation packages plus the one declared sibling edge
  `.Rest`/`.Grpc` → `.Internal` (`<SharedKernelAllowedAdapterReferences>`). Any other adapter reference is SKTIER002.
- **No ASP.NET Core reference** (SKTIER006) and **no `IHttpContextAccessor`**. The caller comes from
  `SharedKernel.Execution`'s `IRequestContextAccessor`, so a call made from a message consumer, workflow activity or
  scheduled job propagates exactly like one made from an HTTP request.
- `.Grpc` never references `SharedKernel.Contracts` (`CommunicationLayeringRules.GrpcNeverReferencesContracts`):
  protobuf messages are the wire contract. `.Rest` does not reference it either — there is no response envelope.
- No reference to `SharedKernel.Security.*`: identity reaches these packages only as `IRequestContext`.

---

## Technology Stack

| Concern | Technology | Version | Package |
| ------- | ---------- | ------- | ------- |
| Typed HTTP client DI | `Microsoft.Extensions.Http` | 10.x | `.Rest` |
| HTTP resilience (Polly v8) | `Microsoft.Extensions.Http.Resilience` | 10.x | `.Rest` |
| gRPC client / DI factory | `Grpc.Net.Client`, `Grpc.Net.ClientFactory` | 2.80.0 | `.Grpc` |
| Protobuf + common protos | `Google.Protobuf` 3.35.1, `Google.Api.CommonProtos` 2.17.0 | — | `.Grpc` |
| gRPC OTel instrumentation | `OpenTelemetry.Instrumentation.GrpcNetClient` | 1.15.1-beta.1 | `.Grpc` |
| K8s service discovery | `Microsoft.Extensions.ServiceDiscovery` | 10.x | `.Internal` |

Versions are centrally managed in `Directory.Packages.props`. `.Grpc` suppresses `NU5104` (stable package depending on
the prerelease OTel instrumentation, the only net10.0-compatible version); remove the suppression when a stable
version is adopted.

---

## Caller propagation (the core contract)

One mapping, `SharedKernel.Execution`'s `RequestContextPropagation.WriteHeaders`, is used by REST, gRPC, MassTransit
and Temporal, so every hop agrees on names and formats. Header names are `01.Core`'s `WellKnownHeaders`:

| Header | Constant | Written when |
| --- | --- | --- |
| `X-Correlation-Id` | `WellKnownHeaders.CorrelationId` | Always — the ambient caller's id (`CorrelationIds.Current`), else `CorrelationIds.New()` when the call starts a new operation. **Never `Activity.Id`**, which changes at every new trace |
| `X-Tenant-Id` | `WellKnownHeaders.TenantId` | The caller has a tenant |
| `x-sk-actor-id` | `WellKnownHeaders.ActorId` | The caller has a user id |
| `x-sk-actor-kind` | `WellKnownHeaders.ActorKind` | There is a caller (`Anonymous` is a real answer) |
| `x-sk-client-id` | `WellKnownHeaders.ClientId` | The caller has a client id |
| `Idempotency-Key` | `WellKnownHeaders.IdempotencyKey` | REST only, opt-in per client (below) |

Rules:

- The caller is `IRequestContextAccessor.Current`, the ambient context every inbound adapter opens (HTTP
  `UseSharedKernelRequestContext()`, the gRPC server interceptor, the message consume filter, the workflow activity
  interceptor, the scheduler). Both entry points `TryAdd` a `RequestContextAccessor` singleton.
- **A caller-supplied header or metadata entry always wins** — handlers and interceptors never overwrite.
- **Best-effort, never throws.** A propagation failure must not fail the call; gRPC interceptors log the failure
  (11100/11101) and continue.
- Handlers and interceptors hold no request state: REST handlers are transient, gRPC interceptors are singletons.
- Never re-declare a header name as a literal. A package-local `const` may exist only as a thin alias of the
  `WellKnownHeaders` constant (`TenantIdInterceptor.TenantIdKey`, `CorrelationTracingInterceptor.CorrelationIdKey`).
  `Grpc.Core.Metadata` lowercases keys, so the mixed-case constants are wire-identical in gRPC.

---

## REST rules (`.Rest`)

- `AddRestClient<TClient>(string name, Action<RestClientOptions>? configure)` — `name` is required and is the
  service-discovery name unless `RestClientOptions.ServiceName` overrides it.
- **Handler order is fixed** (outer → inner): `ServiceDiscoveryResolvingHandler` (only without `BaseAddress`) →
  `RequestContextDelegatingHandler` → `IdempotencyKeyDelegatingHandler` (only when
  `EnableIdempotencyKeyPropagation`) → `StandardResilienceHandler` → transport. Header handlers run **before**
  resilience so they run once per logical call and every retry re-sends identical values.
- `StandardResilienceHandler` is mandatory on every client; never hand-build Polly pipelines. `TimeoutSeconds` is
  the per-attempt timeout (`HttpClient.Timeout` is infinite); total timeout = `TimeoutSeconds × (RetryCount + 1) +
  TotalTimeoutBufferSec`. Polly requires `SamplingDuration ≥ 2 × AttemptTimeout`; `AddRestClient` silently raises
  `SamplingDurationSec` to that floor.
- **Validate at the point of consumption.** `RestClientOptions` is built with `new()` and never resolved through
  `IOptions<T>`, so `AddRestClient` calls `RestClientOptionsValidator.Validate` directly after `configure` and throws
  `OptionsValidationException` synchronously. The DI registration of the validator alone would be a dead validator.
  `.Grpc` follows the same rule with `GrpcClientOptionsValidator`.
- A client needs either `BaseAddress` or a registered `IServiceEndpointResolver`, else `InvalidOperationException`.
  Resolver presence is captured once at builder construction. `ServiceDiscoveryResolvingHandler` is created per
  client through a closure factory, never registered as a shared DI type.
- **Idempotency key:** opt-in (`EnableIdempotencyKeyPropagation`), a hyphenated GUID set once before the first
  attempt; retries re-send the same `HttpRequestMessage`, so the "already present" check keeps it stable. The header
  is `Idempotency-Key` — the one `14.Presentation`'s `[RequireIdempotencyKey]` reads.
- **ProblemDetails → `Error` (P-544, P-562 R38):** `errorCode` → `Error.Code`, else `"http.{status}"` — never
  `title` (the status reason phrase since P-562, and free text from a non-platform upstream that the caller would
  adopt and re-send as its own `errorCode`) and never `type` (a URI). `detail` → `Error.Message`, else
  `"HTTP {status} error"`. Status → `ErrorType` through `HttpStatusErrorTypeMap`, the hand-maintained reverse of
  `14.Presentation`'s `ErrorTypeStatusCodeMap` (`SharedKernel.Presentation.Core`) (503 → `Unavailable`, 504 → `Timeout`) plus the statuses an
  HTTP boundary answers outside it (412 → Conflict, 413/415/428 → Validation, 429 → Unavailable); everything else →
  Unexpected; do not reference `14.Presentation` to share it. Only on a 400 or a 422 is the `errors` extension (with
  the parallel `errorCodes` map) rebuilt into `Error.Validation(IReadOnlyList<Error>)` — so a 422 with field errors is
  Validation, without them BusinessRule; on any other status both maps are ignored, so a 401/409/503 keeps its
  category. `ProblemDetailsDto` binds only `detail`, `errorCode`, `errors` and `errorCodes`. A body with none of those
  members (non-JSON, empty, a bare `{"title":"Not Found","status":404}` from a gateway) still takes its `ErrorType` from
  the status, code `"http.{status}"` — a bodiless 503/429 is `Unavailable`, a 504 `Timeout`. Never throws. A 2xx with
  no body gives `http.empty-body`.
- STJ source-generated `ProblemDetailsJsonContext` is the primary path; the reflection fallback uses one
  `static readonly JsonSerializerOptions`.
- `EnsureSuccessOrErrorAsync` is status-only (`Task<Result>`); `ReadResultAsync<T>` deserializes. Never reintroduce a
  generic `EnsureSuccessOrErrorAsync<T>` that returns `default` on success.

## gRPC rules (`.Grpc`)

- Interceptors are attached per client at channel scope (`InterceptorScope.Channel`) by `AddGrpcClient<TClient>`;
  application code never subclasses `Interceptor` directly outside this package
  (`CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc`).
- `CorrelationTracingInterceptor` writes `traceparent`/`tracestate` from `Activity.Current` and the correlation id;
  `TenantIdInterceptor` writes tenant, actor and client (skipping the correlation header) and is a no-op without a
  caller. Both read at call time and delegate all metadata work to `GrpcMetadataHelper.CloneAndAdd`/`HasMetadataEntry`
  (never mutate the caller's `Metadata`).
- **`DeadlineSeconds` is enforced** through `GrpcClientFactoryOptions.CallOptionsActions` (there is no
  `ConfigureDefaultCallOptions` in Grpc.Net.ClientFactory 2.80.0), computed from `IClock` at call time, and only when
  the call has no deadline of its own. `AddSharedKernelGrpcCommunication` `TryAdd`s `IClock` → `SystemClock`.
- Without `address`, the address is resolved once at channel creation (`GetAwaiter().GetResult()` inside the factory
  delegate — safe because `ResolveAsync` never throws and the channel is cached).
- Retries use gRPC `ServiceConfig`/`RetryPolicy` (`Grpc.Net.Client.Configuration`), not Polly. TLS is channel-level.
- `MoneyProtoExtensions`/`TimestampProtoExtensions` are pure and allocation-minimal (`NanosPerUnit = 1_000_000_000`).
- Namespace collision: use `using GrpcCore = Grpc.Core;` where both namespaces appear.

## Service discovery rules (`.Internal`)

- `ResolveAsync` **never throws** for an unresolvable name: SRV → A-record → the K8s convention URI
  `{scheme}://{service}.{namespace}.svc.{clusterDomain}`; the transport surfaces the connection error.
- TTL cache (`EndpointCacheTtlSeconds`, default 30, `0` disables, negative rejected), case-insensitive keys,
  stale-while-revalidate on DNS failure (11304). Time from an injected `IClock`; never `DateTimeOffset.UtcNow`.
- Exactly one resolver: `AddK8sServiceDiscovery` and `AddStaticServiceDiscovery` both throw when
  `IServiceEndpointResolver` is already registered, in either order.
- `StaticServiceEndpointResolver` is dev/test only and logs a startup warning (11308); unknown names return the K8s
  convention URI for parity.
- Typed clients never build a `Uri` from configuration themselves.

---

## Hard violations

| Violation | Why |
| --- | --- |
| A reference to ASP.NET Core, `IHttpContextAccessor`, `SharedKernel.Security.*`, or any adapter other than `.Internal` | Tier rules (SKTIER002/006); the caller is `IRequestContextAccessor` |
| Correlation id taken from `Activity.Id`/`TraceId`, or formatted `"N"` | Correlation is the caller's id; canonical GUIDs are `"D"` (SK0011) |
| A header/metadata name typed as a literal instead of `WellKnownHeaders` | One source of truth for wire names |
| A handler or interceptor that throws, overwrites a caller-supplied value, or holds request state | Best-effort, caller-wins, stateless |
| A typed client without `StandardResilienceHandler`, or header handlers registered after it | Headers must be set once, before retries |
| An options validator registered but not invoked on the instance actually applied | Dead validation |
| `DeadlineSeconds` not applied as a real `CallOptions.Deadline` | Dead option |
| `DateTime.UtcNow`/`DateTimeOffset.UtcNow` in production code | SK0001 — inject `IClock` |
| `ResolveAsync` throwing, or `AddStaticServiceDiscovery` in production | Discovery contract |
| A `SharedKernel.Contracts` reference in `.Grpc`, or a response-envelope type in `.Rest` | Failures travel as ProblemDetails and map back to `Result<T>` |
| `ProblemDetailsDeserializer` reading `type` or `title` as `Error.Code` (P-544, P-562 R38) | `type` is a URI and `title` the status reason phrase (free text from a non-platform upstream); use `errorCode`, else `"http.{status}"` |
| `ProblemDetailsDeserializer` turning an `errors` map into `Error.Validation` on a status other than 400 or 422 (P-562 R38) | A 401/403/409/5xx must keep its status category |
| The outbound idempotency header under any name other than `WellKnownHeaders.IdempotencyKey` (`"Idempotency-Key"`), e.g. the old `x-idempotency-key` | P-562; `14.Presentation` reads only that name |
| Direct `ILogger.LogXxx` or `LoggerMessage.Define` | `[LoggerMessage]` only (SK0020/SK0021) |

---

## Logging (EventId 11000–11999)

| Sub-block | Package | Events |
| --- | --- | --- |
| 11000–11099 | `.Rest` | none |
| 11100–11199 | `.Grpc` | 11100 `LogCorrelationEnrichmentFailed` (Error) · 11101 `LogTenantIdEnrichmentFailed` (Error) |
| 11200–11299 | — | unused (was `.GraphQL`, now `14.Presentation`) |
| 11300–11399 | `.Internal` | 11300 SRV lookup attempt · 11301 A-record attempt · 11302 DNS fallback (Warning) · 11303 resolved · 11304 stale cache used (Warning) · 11305 cache hit · 11306 SRV failed · 11307 A-record failed (Debug unless noted) · 11308 static discovery active (Warning) |

---

## Test Rules

- Tests are nested in each project folder; shared doubles come from `SharedKernel.Testing` (`FakeClock`,
  `TestRequestContext`, `InMemoryLogger`) and `SharedKernel.Communication.Testing`.
- Propagation tests open a `RequestContextScope` (or set a `RequestContextAccessor`) — never an `HttpContext`.
  End-to-end HTTP→REST/gRPC propagation is proven in `13.ServiceDefaults`' `EndToEndPropagationTests`.
- HTTP handler tests use `HttpMessageHandler` doubles; gRPC interceptor tests use `TestServerCallContext` or an
  in-memory call stub; no real network except `[Trait("Category", "Integration")]` tests.
- TTL-cache tests drive `FakeClock`, never `Task.Delay`; they seed the resolver's private cache via reflection because
  `ServiceEndpointResolver` never re-queries a name it has already resolved.
- GUID fallback tests assert `Guid.TryParseExact(value, "D", out _)`, not `TryParse`.
- Deadline tests re-register the typed client with `ConfigurePrimaryHttpMessageHandler(() => new
  NeverRespondingHandler())` and a `CallInvoker`-constructed client, and expect `StatusCode.DeadlineExceeded`.
- `[LoggerMessage]` EventIds are asserted by reflection plus runtime capture; the real-assembly EventId integrity check
  (`LoggingEventIdIntegrityAssertion`) runs from `.Grpc.Tests`/`.Internal.Tests` through a test-only reference to
  `SharedKernel.ArchitectureTests`.
- `consumer-verify/` (not a test project, never packed) composes all three entry points through a real
  `Host.CreateApplicationBuilder()` → `IHost.StartAsync()` and proves invalid `RestClientOptions` fail loudly.
