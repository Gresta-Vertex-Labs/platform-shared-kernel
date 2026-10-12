# 11.Communication — Domain Brain

> Outbound service-to-service calls: typed REST and gRPC clients configured per client from `appsettings.json`, with
> service discovery, resilience, outbound credentials, mutual TLS and caller propagation wired once at the composition
> root, and failures returned as `Result` (the other service's own `Error`) instead of exceptions. This domain owns
> **outgoing calls only** — inbound middleware and server-side gRPC/GraphQL conventions are `14.Presentation`, bus
> publishing is `07.Messaging`, webhooks and notifications to parties outside the platform are `15.Integration`.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Communication` | Adapter | Base: `AddSharedKernelCommunication(configuration)`, service discovery (`Microsoft.Extensions.ServiceDiscovery` + `.Dns`), outbound authentication (client credentials, API key, `IAccessTokenProvider`), client TLS, `CommunicationErrorCodes`. Internals shared with the satellites through `InternalsVisibleTo` (`ClientPipeline`, `ClientAuthenticationHandler`, `ClientCredentialsTokenClient`, `ClientTls`, `CommunicationClientRegistry`) |
| `SharedKernel.Communication.Rest` | Adapter | Typed `HttpClient`s: `AddRestClient`, resilience mapped onto `Microsoft.Extensions.Http.Resilience`, caller and `Idempotency-Key` propagation, `Result<T>` verbs, ProblemDetails → `Error`. Declared edge → base |
| `SharedKernel.Communication.Grpc` | Adapter | Typed gRPC clients: `AddGrpcClient<T>`, deadline, channel retry policy, keepalive, rich status → `Error`, `google.type.Money` ↔ `Money`. Declared edge → base; references `SharedKernel.Domain` (Model) for `Money` |

`consumer-verify/` composes both satellites through a real generic host. Every public type is in namespace
`SharedKernel.Communication`; internals are in `SharedKernel.Communication.Internal`, `.Rest.Internal`, `.Grpc.Internal`.

## Public Entry Points

Overloads, options and defaults: each package's `README.md`.

- `services.AddSharedKernelCommunication(configuration)` → `ICommunicationBuilder` (idempotent; `TryAdd`s
  `IRequestContextAccessor`, `IClock`, `IConfiguration`). `CommunicationOptions` at `SharedKernel:Communication`
  (discovery mode); per-client settings at `SharedKernel:Communication:Clients:{name}` (`Authentication`, `Tls`, plus
  the protocol options below).
- Endpoints live in Microsoft.Extensions.ServiceDiscovery's own `Services:{service}:{endpoint}` section (the shape
  Aspire emits) — never under `SharedKernel:`.
- `IAccessTokenProvider` / `AccessToken` / `AccessTokenUnavailableException` (a `SharedKernelException`, `Unavailable`); `CommunicationErrorCodes`
  (`communication.unreachable`, `.timeout`, `.circuit_open`, `.access_token_unavailable`, `.empty_body`, `.invalid_body`).
- **Rest** — `.AddRestClient<TClient, TImpl>("name")` → `IRestClientBuilder` (`UseHedging()`,
  `UseAccessTokenProvider<T>()`); `RestClientOptions`. `HttpClientResultExtensions` (`GetResultAsync`,
  `PostResultAsync`, …) and `HttpResponseMessageResultExtensions` (`ToResultAsync`, `ReadResultAsync<T>`) → `Result<T>`.
- **Grpc** — `.AddGrpcClient<Service.ServiceClient>("name")` → `IGrpcClientBuilder`; `GrpcClientOptions`.
  `call.ToResultAsync(ct)`, `RpcException.ToError()`; `MoneyProtoExtensions` (`ToMoneyProto`, `ToMoney`, `ToDecimal`).

## Rules & Invariants

1. **No `IHttpContextAccessor`** (ASP.NET Core is SKTIER006 here). The caller is `IRequestContextAccessor.Current`, read at call time.
2. **No `SharedKernel.Security.*`**: identity arrives only as `IRequestContext`; outbound credentials are this domain's own options.
3. `.Grpc` never references `SharedKernel.Contracts` (`CommunicationLayeringRules.GrpcNeverReferencesContracts`) — protobuf is the wire contract. `.Rest` and `.Grpc` never reference each other.
4. gRPC interceptors live under `SharedKernel.Communication.Grpc` only (`CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc`).
5. **One propagation mapping**: headers come from `SharedKernel.Execution`'s `RequestContextPropagation.WriteHeaders` — never a hand-built header set.
6. **Caller-supplied header or metadata always wins**; propagation never overwrites, never throws, never holds request state (gRPC logs 11100 and continues).
7. **Propagate once per call, before retries**: the REST propagation and idempotency-key handlers sit outside the resilience handler; the gRPC interceptor runs before the channel's retries.
8. **Correlation id** = `CorrelationIds.Current(caller)`, else `CorrelationIds.New()`; never `Activity.Id`/`TraceId`. Never write `traceparent`/`tracestate` by hand — the `System.Net.Http` diagnostics handler writes them from the client span.
9. **Never repeat a side effect**: POST/PATCH are not retried (`Retry.DisableFor`) or hedged unless `RetriesNonIdempotentMethods` (`PropagateIdempotencyKey || Retry:RetryNonIdempotentMethods`). A hedged non-idempotent request gets an infinite delay and `ShouldHandle = false`.
10. **Options are read per call** through `IOptionsMonitor<T>.Get(name)`; the only registration-time reads are the discovery mode and code-level switches (`UseHedging()`, `UseAccessTokenProvider<T>()`).
11. **Switches must switch**: `MaxRetryAttempts = 0` keeps the strategy with `ShouldHandle = false`; `CircuitBreaker:Enabled = false` disables the breaker (and raises its sampling window to the validated floor). An enabled breaker with a too-short window is a startup validation error.
12. **Resilience is mapped, never hand-built** (`RestResilience`). `HttpClient.Timeout` is infinite; the pipeline owns every timeout. Do not wrap the hedging `ActionGenerator` (the standard handler replaces it).
13. **Results, not exceptions**: `HttpFailure.TryMap` maps `AccessTokenUnavailableException` → its error (first arm, by intent), `BrokenCircuitException` → `CircuitOpen`, timeout/non-caller cancellation → `Timeout`, `HttpRequestException` → `Unreachable`; only the caller's own cancellation and unrelated exceptions pass through.
14. **ProblemDetails → `Error`**: `errorCode` → `Code`, else `http.{status}` — never `title` or `type`. `detail` → `Message`. Status → `ErrorType` via `HttpStatusErrorTypeMap` (hand-kept reverse of `14.Presentation`'s map; never reference `14.Presentation`). Only 400/422 rebuild `errors`/`errorCodes` into `Error.Validation`. `ProblemDetailsDto` binds only `detail`, `errorCode`, `errors`, `errorCodes`.
15. **gRPC**: deadline = `IClock.UtcNow + Deadline` only when the call has none; retries are the channel `ServiceConfig` policy (`MaxAttempts` 1–5, `Unavailable` by default), never `DeadlineExceeded`; `Address` must be `http`/`https`.
16. **Money**: round to nine places, then split units/nanos; refuse invalid wire messages (`money.proto.invalid`).
17. A client name is reserved once across REST and gRPC (`CommunicationClientRegistry`); no `:` in a name.
18. Option collections default to `null` (the binder appends to non-null defaults).
19. **Credentials never leave the process**: no token, secret or API key in a log, exception message or `ToString()` (`AccessToken.ToString()` redacts).
20. A typed client takes `HttpClient` in its primary constructor and is registered with `AddRestClient` (SK0013 flags an explicit constructor taking `HttpClient`).
21. **`AccessTokenUnavailableException` is a `SharedKernelException`** (E1), not an `HttpRequestException`: an inbound edge maps it to `Unavailable`. REST keeps retrying, breaking and hedging on it through `RestResilience`'s explicit predicate (rules 9 and 12 still hold); the gRPC channel reports it as `Internal`, so its channel retry does not cover it.
22. **No URL, host, OAuth client id or remote free text** in a log or an `Error` on the token path: logs name the client by its kernel client name; the token endpoint's `error` is reduced to the RFC 6749 section 5.2 values (else `unrecognized`); a provider's failure gets a fixed message; the token `HttpClient` has the factory's request logging removed (`RemoveAllLoggers()`), which prints the endpoint URL.
23. *(planned, SK.11.CommunicationConformance)* **Observability gold standard**: every `[LoggerMessage]` takes its id from the package's `*LoggingEventIds` class and its logger category from `ILogger<KernelType>`; one `*Log` class per package; templates are one sentence with quoted identifier values; every swallowed exception is logged with the exception object (never silent), except the caller's cancellation which propagates; every `Error` is built by the internal `CommunicationErrors` factory; every code is a `CommunicationErrorCodes` constant. The domain emits no `ActivitySource` or `Meter`.

## Decisions

| Decision | Why |
| --- | --- |
| Service discovery is `Microsoft.Extensions.ServiceDiscovery` (Configuration → DNS/DNS SRV → pass-through) | The library Aspire uses; it queries DNS and handles relative URIs, so no kernel-owned resolver |
| DNS providers set `ShouldApplyHostNameMetadata` | A pod address keeps the service host name for TLS/SNI |
| Handler order (outer → inner): propagation → idempotency key → service handlers → resilience/hedging → `ClientAuthenticationHandler` → `AddServiceDiscovery()` → `SocketsHttpHandler` + `ClientTls.Apply` | Headers once per call; token and address resolved per attempt so a retry can reach another pod |
| Microsoft's standard-pipeline defaults, except `Retry:BaseDelay` 500 ms | Well-known behaviour; no bespoke Polly pipeline to maintain |
| Client-credentials cache per (endpoint, id, scope, audience, resource, transport, secret hash); refresh at `lifetime − min(RefreshBeforeExpiry, lifetime/2)`; one request per credential; failures not cached | No token storms; a rejected token is never returned again |
| 401 to a token we added → resent once with a fresh token when the body can be resent | Survives key rotation without replaying unbufferable bodies |
| On Windows a PEM client certificate is re-imported as PKCS#12 | SChannel cannot use an ephemeral key |
| Private CA via `CustomRootTrust`, name mismatch never forgiven | Private PKI without disabling validation |
| No `Timestamp` helpers in `.Grpc` | Google.Protobuf already ships them |
| OpenTelemetry gRPC/HTTP instrumentation lives in `13.ServiceDefaults` (`WithCommunicationTelemetry()`) | Telemetry is host composition, not a client dependency |
| No response envelope on REST | Handlers return `Result`; ProblemDetails is the error shape |
| `AccessTokenUnavailableException` derives `SharedKernelException`, and REST preserves its retry/breaker/hedging behaviour by an explicit predicate | C# single inheritance: the edges match `SharedKernelException`; token failures send nothing, so repeating them is side-effect free. Supersedes "a provider's own failure keeps its detail": the `Error` crosses the trust boundary, so its message is fixed and the provider logs its own detail |
| `Grpc.Net.Client` reports a token failure (not an `HttpRequestException`) as `Internal`, so the channel retry does not cover it | Accepted; the call still returns the same `Error`. Pinned by a test, not worked around |
| *(planned, SK.11.CommunicationConformance)* `communication.unreachable` becomes `communication.unavailable`; `money.proto.invalid` becomes `communication.grpc.invalid_money`; `grpc.{status}` and `http.{status}` keep their values and move into `CommunicationErrorCodes` | C3 (same condition, same reason word across domains); the `grpc.*`/`http.*` values are the hand-kept mirror of `14.Presentation`'s names and must not drift |
| *(planned, SK.11.CommunicationConformance)* No `ActivitySource`/`Meter` in this domain (name `SharedKernel.Communication.*` reserved) | Runtime HttpClient/gRPC client spans and metrics already cover the call; a `Result` failure is recorded by the span owner (the caller) |
| *(planned, SK.11.CommunicationConformance)* Rest reaches its logger through `HttpRequestMessage.Options` (set by the per-client propagation handler), `NullLogger` when the response did not come through a registered client | The `Result` verbs are static extensions over `HttpClient`/`HttpResponseMessage` and hold no container |

## Logging

`LoggingEventIdRanges.Communication` (11000–11999).

| Sub-block | Package | Events |
| --- | --- | --- |
| 11000–11099 | `SharedKernel.Communication` (`Internal/CommunicationLog.cs`; 11000–11002 name the client by its kernel client name, never the endpoint or OAuth client id) | 11000 token acquired · 11001 token endpoint refused the client · 11002 token endpoint unreachable · 11003 no access token, request not sent · 11004 401 answered with a new token |
| 11100–11199 | `.Grpc` (`RequestContextInterceptor`) | 11100 the caller could not be written onto a call |
| 11200–11299 | `.Rest` | none |
| 11300–11399 | — | retired; do not reuse |

*(planned, SK.11.CommunicationConformance)* Each package gets `{Package}LoggingEventIds` (const arithmetic from `LoggingEventIdRanges.Communication`) and one `{Package}Log` under `Diagnostics/`. Existing ids keep their numbers; `.Rest` (11200–11299) gains 11200 caller not written onto a request · 11201 problem-details body unreadable · 11202 success body unreadable. Templates move to the one-sentence style; 11000–11002 log the kernel client name, never the endpoint or OAuth client id.

## Cross-Domain Couplings

- **01.Core** — `Primitives` (`Result`, `Error`, `IClock`, `WellKnownHeaders`), `Core` (`SharedKernelException`, the base of `AccessTokenUnavailableException`), `Execution` (`IRequestContextAccessor`, `RequestContextPropagation`, `CorrelationIds`), `Configuration`.
- **03.Domain** — `.Grpc` uses `Money` (`SharedKernel.Domain.Monetary`).
- **13.ServiceDefaults** — `WithCommunicationTelemetry()`; `ServiceDefaults.Security`'s `EndToEndPropagationTests` prove HTTP → REST/gRPC, consumer → REST and job → REST propagation.
- **14.Presentation** — the server side of the same contracts: ProblemDetails shape and `GrpcErrorCodes.ForStatus` names; mirrored here by hand, never referenced.
- **00.Governance** — `CommunicationLayeringRules`; SK0013.
- **samples** — the Shop (`samples/Shop`): Ordering → Inventory over gRPC with mutual TLS, Ordering → Billing over REST with an API key.

## Testing

- Unit lane only (no Docker): `SharedKernel.Communication.Tests`, `.Rest.Tests`, `.Grpc.Tests` (nested in each package), plus `SharedKernel.Communication.Testing.Tests`.
- Fakes: `SharedKernel.Communication.Testing` — catalogue in `src/Testing/CLAUDE.md`.
- REST tests register a real client over a `StubHttpMessageHandler`, so the whole pipeline runs; settings through in-memory configuration validated with `IStartupValidator.Validate()`; `Retry:BaseDelay` 0.
- The resolving handler restores the original request URI after the call — a test inspecting the URI must copy it at send time (`StubHttpMessageHandler` does).
- gRPC tests run a real service on `TestServer` (test proto) as the client's primary handler (`GrpcHarness`). Transport failures: a stub that throws `HttpRequestException` with a `SocketException` inner.
- Token caching with `FakeClock`; certificates made in memory (`CertificateRequest`).
- EventIds checked over the real assemblies (`LoggingEventIdIntegrityRealAssemblyTests`).

## Known Limitations

- `HttpStatusErrorTypeMap` and the gRPC status map are hand-kept mirrors of `14.Presentation`'s maps; a change there must be mirrored here.
- A gRPC channel refuses the composite `https+http` scheme, so gRPC clients cannot use Aspire's scheme-fallback addresses.
- The reflection-based `Result` verb overloads carry `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`; AOT callers pass a `JsonTypeInfo`.
- A 401 retry only happens for resendable bodies (`null`, `ByteArrayContent`, `JsonContent`).
- A gRPC channel reports a token failure as `Internal`, so the channel retry policy (`Unavailable` only) does not repeat it; the call still ends in `communication.access_token_unavailable`.
- *(planned, SK.11.CommunicationConformance)* A call that fails before any HTTP exchange (no access token, open circuit) has no client span of its own, because this domain emits none; the failure is a `Result` the caller records.
