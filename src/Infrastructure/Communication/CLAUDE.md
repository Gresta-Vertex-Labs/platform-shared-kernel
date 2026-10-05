# 11.Communication — Domain Brain

> Outbound service-to-service calls: typed REST and gRPC clients configured per client from `appsettings.json`, with
> service discovery, resilience, outbound credentials, mutual TLS and caller propagation wired once at the composition
> root, and failures returned as `Result` (the other service's own `Error`) instead of exceptions. This domain owns
> **outgoing calls only** — inbound middleware and server-side gRPC/GraphQL conventions are `14.Presentation`, bus
> publishing is `07.Messaging`, webhooks and notifications to parties outside the platform are `15.Integration`.
> Consumers read each package's `README.md`; the folder overview is `README.md`; the living board is `state-map.md`.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Communication` | Adapter | Base: `AddSharedKernelCommunication(configuration)`, service discovery (`Microsoft.Extensions.ServiceDiscovery` + `.Dns`), outbound authentication (client credentials, API key, `IAccessTokenProvider`), client TLS, `CommunicationErrorCodes`. Internals shared with the satellites through `InternalsVisibleTo` (`ClientPipeline`, `ClientAuthenticationHandler`, `ClientCredentialsTokenClient`, `ClientTls`, `CommunicationClientRegistry`). |
| `SharedKernel.Communication.Rest` | Adapter | Typed `HttpClient`s: `AddRestClient`, resilience mapped onto `Microsoft.Extensions.Http.Resilience`, caller and `Idempotency-Key` propagation, `Result<T>` verbs, ProblemDetails → `Error`. Declared edge → base. |
| `SharedKernel.Communication.Grpc` | Adapter | Typed gRPC clients: `AddGrpcClient<T>`, deadline, channel retry policy, keepalive, rich status → `Error`, `google.type.Money` ↔ `Money`. Declared edge → base; references `SharedKernel.Domain` (Model) for `Money`. |

`consumer-verify/` composes both satellites through a real generic host. Every public type is in namespace
`SharedKernel.Communication`; internals are in `SharedKernel.Communication.Internal`, `.Rest.Internal`, `.Grpc.Internal`.

## Public Entry Points

**Base** — `services.AddSharedKernelCommunication(configuration)` → `ICommunicationBuilder` (idempotent: a second call
returns a builder over the same registrations; `TryAdd`s `IRequestContextAccessor`, `IClock` and the `IConfiguration`).

- `SharedKernel:Communication` → `CommunicationOptions` (`ISectionBoundOptions`): `ServiceDiscovery` →
  `CommunicationDiscoveryOptions` (`Mode` `Configuration`/`Dns`/`DnsSrv`, `RefreshPeriod`, `DnsSrvQuerySuffix`).
- `Services:{service}:{endpoint}` — Microsoft.Extensions.ServiceDiscovery's own section (the shape Aspire emits). Keep it
  there, never under `SharedKernel:`.
- Per client (`CommunicationClientOptions`): `Authentication` → `ClientAuthenticationOptions` (`Mode` ClientCredentials /
  ApiKey / AccessTokenProvider; `ClientCredentialsOptions`, `ClientApiKeyOptions`), `Tls` → `ClientTlsOptions`
  (client certificate, `TrustedCertificateAuthoritiesPath`).
- `IAccessTokenProvider` / `AccessToken` / `AccessTokenUnavailableException`; `CommunicationErrorCodes`
  (`communication.unreachable`, `.timeout`, `.circuit_open`, `.access_token_unavailable`, `.empty_body`, `.invalid_body`).

**Rest** — `.AddRestClient<TClient, TImpl>("name")` / `.AddRestClient<TClient>("name")` → `IRestClientBuilder`
(`UseHedging()`, `UseAccessTokenProvider<T>()`, `Configure(...)`, `HttpClientBuilder`).

- `SharedKernel:Communication:Clients:{name}` → `RestClientOptions` (`Retry` → `RestRetryOptions`,
  `CircuitBreaker` → `RestCircuitBreakerOptions`, `Hedging` → `RestHedgingOptions`, `PropagateIdempotencyKey`).
- `HttpClientResultExtensions`: `GetResultAsync`, `PostResultAsync`, `PutResultAsync`, `DeleteResultAsync`,
  `SendResultAsync` → `Result<T>`; `HttpResponseMessageResultExtensions`: `ToResultAsync`, `ReadResultAsync<T>`.

**Grpc** — `.AddGrpcClient<Service.ServiceClient>("name")` → `IGrpcClientBuilder`.

- `SharedKernel:Communication:Clients:{name}` → `GrpcClientOptions` (`Address`, `Deadline`, `Retry` →
  `GrpcRetryOptions`, `KeepAlive` → `GrpcKeepAliveOptions`).
- `GrpcResultExtensions`: `call.ToResultAsync(ct)`, `RpcException.ToError()`; `MoneyProtoExtensions`: `ToMoneyProto`,
  `ToMoney`, `ToDecimal`.

## Rules & Invariants

1. **No ASP.NET Core, no `IHttpContextAccessor`** (SKTIER006). The caller is `IRequestContextAccessor.Current`, read at call time.
2. **No `SharedKernel.Security.*`**: identity arrives only as `IRequestContext`; outbound credentials are this domain's own options.
3. `.Grpc` never references `SharedKernel.Contracts` (`CommunicationLayeringRules.GrpcNeverReferencesContracts`) — protobuf is the wire contract. `.Rest` and `.Grpc` never reference each other.
4. gRPC interceptors live under `SharedKernel.Communication.Grpc` only (`NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc` exempts that prefix).
5. **One propagation mapping**: headers come from `SharedKernel.Execution`'s `RequestContextPropagation.WriteHeaders` and `WellKnownHeaders` — never a literal header name.
6. **Caller-supplied header or metadata always wins**; propagation never overwrites, never throws, never holds request state (gRPC logs 11100 and continues).
7. **Propagate once per call, before retries**: the REST propagation and idempotency-key handlers sit outside the resilience handler; the gRPC interceptor runs before the channel's retries.
8. **Correlation id** = `CorrelationIds.Current(caller)`, else `CorrelationIds.New()`; never `Activity.Id`/`TraceId`. Never write `traceparent`/`tracestate` by hand — the `System.Net.Http` diagnostics handler writes them from the client span.
9. **Never repeat a side effect**: POST/PATCH are not retried (`Retry.DisableFor`) or hedged unless `RetriesNonIdempotentMethods` (`PropagateIdempotencyKey || Retry:RetryNonIdempotentMethods`). A hedged non-idempotent request gets an infinite delay and `ShouldHandle = false`.
10. **Options are read per call** through `IOptionsMonitor<T>.Get(name)`; the only registration-time reads are the discovery mode and code-level switches (`UseHedging()`, `UseAccessTokenProvider<T>()`).
11. **Switches must switch**: `MaxRetryAttempts = 0` keeps the strategy with `ShouldHandle = false`; `CircuitBreaker:Enabled = false` disables the breaker (and raises its sampling window to the validated floor). An enabled breaker with a too-short window is a startup validation error.
12. **Resilience is mapped, never hand-built** (`RestResilience`). `HttpClient.Timeout` is infinite; the pipeline owns every timeout. Do not wrap the hedging `ActionGenerator` (the standard handler replaces it).
13. **Results, not exceptions**: `HttpFailure.TryMap` maps `AccessTokenUnavailableException` → its error, `BrokenCircuitException` → `CircuitOpen`, timeout/non-caller cancellation → `Timeout`, `HttpRequestException` → `Unreachable`; only the caller's own cancellation and unrelated exceptions pass through.
14. **ProblemDetails → `Error`**: `errorCode` → `Code`, else `http.{status}` — never `title` or `type`. `detail` → `Message`. Status → `ErrorType` via `HttpStatusErrorTypeMap` (hand-kept reverse of `14.Presentation`'s map; do not reference `14.Presentation`). Only 400/422 rebuild `errors`/`errorCodes` into `Error.Validation`. `ProblemDetailsDto` binds only `detail`, `errorCode`, `errors`, `errorCodes`.
15. **gRPC**: deadline = `IClock.UtcNow + Deadline` only when the call has none; retries are the channel `ServiceConfig` policy (`MaxAttempts` 1–5, `Unavailable` by default), never `DeadlineExceeded`; `Address` must be `http`/`https`.
16. **Money**: round to nine places, then split units/nanos; refuse invalid wire messages (`money.proto.invalid`).
17. A client name is reserved once across REST and gRPC (`CommunicationClientRegistry`); no `:` in a name.
18. Option collections default to `null` (the binder appends to non-null defaults).
19. **Credentials never leave the process**: no token, secret or API key in a log, exception message or `ToString()` (`AccessToken.ToString()` redacts).
20. `IClock`, never `DateTime.UtcNow` (SK0001); `[LoggerMessage]` only.

## Decisions

| Decision | Why |
| --- | --- |
| Service discovery is `Microsoft.Extensions.ServiceDiscovery` (Configuration → DNS/DNS SRV → pass-through) | The library Aspire uses; a home-grown resolver never queried DNS and broke on relative URIs |
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

## Logging

EventId block **11000–11999**.

| Sub-block | Package | Events |
| --- | --- | --- |
| 11000–11099 | `SharedKernel.Communication` (`Internal/CommunicationLog.cs`) | 11000 token acquired · 11001 token endpoint refused the client · 11002 token endpoint unreachable · 11003 no access token, request not sent · 11004 401 answered with a new token |
| 11100–11199 | `.Grpc` (`RequestContextInterceptor`) | 11100 the caller could not be written onto a call |
| 11200–11299 | `.Rest` | none |
| 11300–11399 | — | retired; do not reuse |

## Cross-Domain Couplings

- **01.Core** — `Primitives` (`Result`, `Error`, `IClock`, `WellKnownHeaders`), `Execution` (`IRequestContextAccessor`, `RequestContextPropagation`, `CorrelationIds`), `Configuration` (`AddValidatedOptions`).
- **03.Domain** — `.Grpc` uses `Money` (`SharedKernel.Domain.Monetary`).
- **13.ServiceDefaults** — `WithCommunicationTelemetry()`; `ServiceDefaults.Security`'s `EndToEndPropagationTests` prove HTTP → REST/gRPC, consumer → REST and job → REST propagation.
- **14.Presentation** — the server side of the same contracts: ProblemDetails shape and `GrpcErrorCodes.ForStatus` names; mirrored here by hand, never referenced.
- **16.Testing** — `SharedKernel.Communication.Testing` supplies the doubles below.
- **00.Governance** — `CommunicationLayeringRules`; SK0013 (no explicit constructor taking `HttpClient` in a typed client).
- **samples** — `samples/CheckoutApi` → `samples/InventoryApi` over both protocols.

## Testing

- Unit lane only (no Docker): `SharedKernel.Communication.Tests`, `.Rest.Tests`, `.Grpc.Tests` (nested in each package), plus `src/Testing/SharedKernel.Communication.Testing/SharedKernel.Communication.Testing.Tests`.
- Consumer doubles: `StubHttpMessageHandler` + `UseStubHttpMessageHandler(clientName, stub)`, `GrpcCalls`, `TestServerCallContext`.
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
