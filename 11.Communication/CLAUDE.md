# 11.Communication — Domain Brain

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read each package's own `README.md`; the folder overview is [`README.md`](README.md).
> Task history lives in [`state-map.md`](state-map.md).

## What This Domain Is

Outbound service-to-service calls: typed REST and gRPC clients, configured per client from `appsettings.json`, with
service discovery, resilience, outbound credentials, mutual TLS and caller propagation wired once at the composition
root, and failures returned as `Result` — the other service's own `Error` — instead of exceptions.

Philosophy: **Configured, Not Coded. Propagate Context Always. Never Repeat a Side Effect. Fail as a Result.**

> **Scope:** outgoing calls only. Inbound concerns — ASP.NET Core middleware, server-side gRPC and GraphQL
> conventions — belong in `14.Presentation`. Message-bus publishing belongs in `07.Messaging`; webhooks and
> notifications to parties outside the platform in `15.Integration`.

---

## Packages

| Package | Tier | Role | References |
| ------- | ---- | ---- | ---------- |
| `SharedKernel.Communication` | Adapter | `AddSharedKernelCommunication(configuration)` → `ICommunicationBuilder`; `CommunicationOptions` (`SharedKernel:Communication`: service-discovery mode); `CommunicationClientOptions` (per-client `Authentication`, `Tls`); `IAccessTokenProvider`, `AccessToken`, `AccessTokenUnavailableException`; `CommunicationErrorCodes`. Internal and shared with the satellites through `InternalsVisibleTo`: `ClientPipeline`, `ClientAuthenticationHandler`, `ClientCredentialsTokenClient`, `ClientTls`, `CommunicationClientRegistry` | `Primitives`, `Execution`, `Configuration`; `Microsoft.Extensions.Http`, `.Http.Resilience`, `.ServiceDiscovery`, `.ServiceDiscovery.Dns` |
| `SharedKernel.Communication.Rest` | Adapter | `AddRestClient<TClient, TImpl>(name)` / `AddRestClient<TClient>(name)`, `IRestClientBuilder`, `RestClientOptions`; `HttpClientResultExtensions` (`GetResultAsync`, `PostResultAsync`, `PutResultAsync`, `DeleteResultAsync`, `SendResultAsync`), `HttpResponseMessageResultExtensions` (`ToResultAsync`, `ReadResultAsync`); internal propagation and idempotency-key handlers, `RestResilience`, the ProblemDetails reader | `SharedKernel.Communication` (declared edge), `Primitives`, `Execution`; `Microsoft.Extensions.Http.Resilience` |
| `SharedKernel.Communication.Grpc` | Adapter | `AddGrpcClient<TClient>(name)`, `IGrpcClientBuilder`, `GrpcClientOptions`; `GrpcResultExtensions` (`ToResultAsync`, `ToError`); `MoneyProtoExtensions`; internal `RequestContextInterceptor` | `SharedKernel.Communication` (declared edge), `Primitives`, `Execution`, `SharedKernel.Domain` (Model, for `Money`); `Grpc.Net.ClientFactory`, `Google.Protobuf`, `Google.Api.CommonProtos`, `Grpc.StatusProto` |

Every public type is in namespace `SharedKernel.Communication` — one `using` for a service. Internals are in
`SharedKernel.Communication.Internal`, `.Rest.Internal` and `.Grpc.Internal`; the gRPC interceptor must stay under
`SharedKernel.Communication.Grpc` (`CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc`
exempts that prefix).

Test doubles: `16.Testing/SharedKernel.Communication.Testing` (`StubHttpMessageHandler` + `UseStubHttpMessageHandler`,
`GrpcCalls`, `TestServerCallContext`). Worked example: `samples/CheckoutApi` → `samples/InventoryApi`.

**Dependency rules (tier build check, `eng/SharedKernelTiers.targets`):**

- All three are **Adapter**: Foundation, Model and Abstractions packages plus the declared edges `.Rest`/`.Grpc` →
  `SharedKernel.Communication`. `.Rest` and `.Grpc` never reference each other.
- **No ASP.NET Core** (SKTIER006) and no `IHttpContextAccessor`: the caller is `IRequestContextAccessor.Current`.
- `.Grpc` never references `SharedKernel.Contracts` (`CommunicationLayeringRules.GrpcNeverReferencesContracts`):
  protobuf messages are the wire contract. `.Rest` has no response envelope either.
- No `SharedKernel.Security.*`: identity reaches these packages only as `IRequestContext`, and outbound credentials are
  this domain's own (`ClientAuthenticationOptions`).
- The OpenTelemetry gRPC instrumentation is `13.ServiceDefaults`' (`WithCommunicationTelemetry()`), not a dependency here.

---

## Technology Stack

| Concern | Technology | Package |
| ------- | ---------- | ------- |
| Typed HTTP clients | `Microsoft.Extensions.Http` (`IHttpClientFactory`, `UseSocketsHttpHandler`) | base, `.Rest` |
| Resilience | `Microsoft.Extensions.Http.Resilience` (standard pipeline, standard hedging) | base (token endpoint), `.Rest` |
| Service discovery | `Microsoft.Extensions.ServiceDiscovery` (+ `.Dns`, which brings DnsClient, Apache-2.0) — MIT, the library .NET Aspire uses | base |
| gRPC clients | `Grpc.Net.Client`, `Grpc.Net.ClientFactory` | `.Grpc` |
| Rich status, common protos | `Grpc.StatusProto`, `Google.Api.CommonProtos`, `Google.Protobuf` | `.Grpc` |

Versions are central in `Directory.Packages.props`. The two old home-grown pieces are gone: the `IServiceEndpointResolver`
package `SharedKernel.Communication.Internal` (its REST path never worked — `HttpClient` rejects a relative URI before a
handler runs — and it never queried DNS), and the OTel gRPC instrumentation reference in `.Grpc` (unused; its prerelease
version forced an NU5104 suppression).

---

## Configuration model

- `SharedKernel:Communication` → `CommunicationOptions` (`ServiceDiscovery:Mode` `Configuration`/`Dns`/`DnsSrv`,
  `RefreshPeriod`, `DnsSrvQuerySuffix`), bound with `AddValidatedOptions` and **also read once at registration**, since
  the mode decides which endpoint providers are registered.
- `SharedKernel:Communication:Clients:{name}` → `RestClientOptions` / `GrpcClientOptions`, **named options** (the
  client name), bound with `AddValidatedOptions(section, name)` — DataAnnotations plus `IValidatableObject` — and
  validated on start. `client.Configure(...)` is a `PostConfigure` for that name.
- Options are read at call or handler-build time through `IOptionsMonitor<T>.Get(name)` — never captured at
  registration — so the only registration-time switches are code-level: `UseHedging()`, `UseAccessTokenProvider<T>()`.
- `Services:{service}:{endpoint}` is Microsoft.Extensions.ServiceDiscovery's own section (the shape Aspire emits); do not
  move it under `SharedKernel:`.
- A client name is reserved once across REST and gRPC (`CommunicationClientRegistry`): it is one `IHttpClientFactory`
  name and one configuration section. No `:` in a name.
- `AddSharedKernelCommunication` is idempotent (a second call returns a builder over the same registrations) and
  `TryAdd`s `IRequestContextAccessor`, `IClock` and the `IConfiguration` it was given (the configuration endpoint
  provider resolves `IConfiguration` from the container).
- Collections in options default to `null` (`GrpcRetryOptions.RetryableStatusCodes`): the configuration binder appends to
  a non-null default instead of replacing it.

---

## Caller propagation (the core contract)

One mapping, `SharedKernel.Execution`'s `RequestContextPropagation.WriteHeaders`, is used by REST, gRPC, MassTransit and
Temporal, so every hop agrees on names and formats (`01.Core`'s `WellKnownHeaders`):

| Header | Written when |
| --- | --- |
| `X-Correlation-Id` | Always — `CorrelationIds.Current(caller)`, else `CorrelationIds.New()` for the first hop of a new operation. **Never `Activity.Id`** |
| `X-Tenant-Id` | The caller has a tenant |
| `x-sk-actor-id` | The caller has a user id |
| `x-sk-actor-kind` | There is a caller (`Anonymous` is a real answer) |
| `x-sk-client-id` | The caller has a client id |
| `Idempotency-Key` | REST POST/PATCH only, when `PropagateIdempotencyKey` |

Rules:

- The caller is `IRequestContextAccessor.Current`, read at call time.
- **A caller-supplied header or metadata entry always wins.** Handlers and interceptors never overwrite.
- **Best effort, never throws.** A propagation failure must not fail the call (gRPC logs 11100 and continues).
- **Once per call, before the retries**: the REST propagation and idempotency-key handlers are registered before the
  resilience handler; the gRPC interceptor runs before the channel's own retries. Every attempt carries the same values.
- Trace context is not written by this domain: `System.Net.Http`'s diagnostics handler writes `traceparent` from the
  current HTTP/gRPC client span. (The old gRPC interceptor wrote it from the caller's span, before the client span
  existed, which parented the server span on the wrong span.)
- Never re-declare a header name as a literal; use `WellKnownHeaders`.

---

## Handler order (outer → inner)

REST (`RestCommunicationBuilderExtensions.Register`):

1. `RequestContextPropagationHandler` — once per call.
2. `IdempotencyKeyHandler` — once per call; POST/PATCH only; no-op unless `PropagateIdempotencyKey`.
3. Handlers the service adds through `IRestClientBuilder.HttpClientBuilder` (the `configure` callback runs here).
4. `AddStandardResilienceHandler()` or, with `UseHedging()`, `AddStandardHedgingHandler()`.
5. `ClientAuthenticationHandler` — per attempt, so each attempt has a valid token.
6. `AddServiceDiscovery()` (Microsoft's `ResolvingHttpDelegatingHandler`) — per attempt, so a retry can reach another pod.
7. Primary: `SocketsHttpHandler` via `UseSocketsHttpHandler`, with `ClientTls.Apply`.

gRPC: `RequestContextInterceptor` (channel scope) and the deadline (`CallOptionsActions`) before the channel's retry
policy; then handlers added in the `configure` callback, `ClientAuthenticationHandler`, `AddServiceDiscovery()`, and the
primary `SocketsHttpHandler` with keepalive and `EnableMultipleHttp2Connections`.

The resolving handler puts the original request URI back after the call; a test that inspects the URI must copy it at
send time (`StubHttpMessageHandler` does).

---

## REST rules (`.Rest`)

- **Resilience is mapped, never hand-built**: `RestResilience` maps `RestClientOptions` onto the standard pipeline or the
  standard hedging pipeline. Defaults are Microsoft's (3 retries, 10 s attempt, 30 s total, breaker 10% of ≥ 100 calls in
  30 s, open 5 s) except `Retry:BaseDelay` (500 ms). `HttpClient.Timeout` is infinite; the pipeline owns every timeout.
- **Idempotent methods only**: retries are disabled for POST and PATCH (`Retry.DisableFor`) unless
  `RetriesNonIdempotentMethods` (`PropagateIdempotencyKey || Retry:RetryNonIdempotentMethods`). Hedging: a non-idempotent
  request gets an infinite `DelayGenerator` (no parallel copy) and a `ShouldHandle` of `false` (no copy after a failure).
  Do not wrap `ActionGenerator` — the standard hedging handler replaces it after `Configure` runs.
- **Switches are real**: `MaxRetryAttempts = 0` keeps the strategy (it requires ≥ 1) with `ShouldHandle = false`;
  `CircuitBreaker:Enabled = false` sets the breaker's `ShouldHandle = false` and raises its sampling window to the
  2 × attempt-timeout floor the pipeline validates even for a disabled breaker. An enabled breaker with a too-short
  window is a startup validation error, not a silent adjustment.
- **Result helpers**: `HttpFailure.TryMap` turns a send's exception into an `Error` — `AccessTokenUnavailableException`
  → its error, `BrokenCircuitException` → `CircuitOpen`, `TimeoutRejectedException` or a non-caller cancellation →
  `Timeout`, `HttpRequestException` → `Unreachable` — and lets the caller's own cancellation and every other exception
  through. A body of unknown length is buffered so an empty body (`EmptyBody`) is told from invalid JSON (`InvalidBody`).
  The verbs build their own request and dispose it. The reflection overloads carry
  `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`; RS0026 is suppressed in the two files because the overloads
  mirror `System.Net.Http.Json`.
- **ProblemDetails → `Error` (P-544, P-562 R38)**: `errorCode` → `Code`, else `http.{status}` — never `title` (a reason
  phrase, free text from a non-platform upstream) or `type` (a URI). `detail` → `Message`, else `HTTP {status} error`.
  Status → `ErrorType` through `HttpStatusErrorTypeMap`, the hand-kept reverse of `14.Presentation`'s map plus 412 →
  Conflict, 413/415/428 → Validation, 429 → Unavailable; do not reference `14.Presentation` to share it. Only a 400 or
  422 rebuilds `errors` (with the parallel `errorCodes`) into `Error.Validation(IReadOnlyList<Error>)`. A body with none of
  the read members still takes its status's `ErrorType`. `ProblemDetailsDto` binds only `detail`, `errorCode`, `errors`,
  `errorCodes`. The source-generated context is the `application/problem+json` path.

## gRPC rules (`.Grpc`)

- **Deadline**: `CallOptionsActions` sets `IClock.UtcNow + Deadline` at call time, only when the call has none.
- **Retries** are the channel's `ServiceConfig` retry policy (not Polly): `MaxAttempts` 1–5 (1 = none), `Unavailable`
  alone by default. Never retry `DeadlineExceeded` (the deadline covers every attempt).
- **Address** must be `http`/`https`: a gRPC channel refuses the composite `https+http` scheme.
- **`ToError`**: an `AccessTokenUnavailableException` in the debug-exception chain → its error; an `HttpRequestException`
  there → `Unreachable` (Grpc.Net.Client reports a bare one as `Internal`); then `ErrorInfo.reason` → `Code`, the
  status message → `Message`, `GrpcStatusErrorTypeMap` → `ErrorType`; `InvalidArgument` + `BadRequest` →
  `Error.Validation` with `PropertyPath`; no `ErrorInfo` → `DeadlineExceeded` is `Timeout`, anything else `grpc.{status}`
  (the names of `14.Presentation`'s `GrpcErrorCodes.ForStatus`). A malformed status-details trailer is ignored.
- **Money**: round to nine places first, then split — scaling first produced `nanos` = 10⁹. Invalid wire messages
  (`nanos` out of range or sign-mismatched) are refused (`money.proto.invalid`). No `Timestamp` helpers: Google.Protobuf has them.

## Base rules (`SharedKernel.Communication`)

- **Service discovery** is Microsoft's: `AddServiceDiscoveryCore` + configuration provider, then DNS or DNS SRV by
  mode, then pass-through. DNS providers set `ShouldApplyHostNameMetadata` so a pod address keeps the service's host name.
- **Client credentials**: `ClientCredentialsTokenClient` — cache per (endpoint, id, scope, audience, resource, transport,
  secret hash); refresh at `lifetime − min(RefreshBeforeExpiry, lifetime/2)`; `expires_in` missing → 5 min; one request
  per credential (`SemaphoreSlim`); a `rejectedToken` is never returned again; failures are `Error.Unavailable(
  AccessTokenUnavailable)` and are not cached. The token endpoint's own named client (`HttpClientName`) retries twice
  (a token POST is idempotent). Basic authentication form-encodes id and secret first (RFC 6749 §2.3.1).
- **`ClientAuthenticationHandler`**: options read per request; `ApiKey` adds its header unless present; token modes skip a
  request with its own `Authorization`; a 401 to a token it added is resent once with a fresh token when the body can be
  resent (`null`, `ByteArrayContent`, `JsonContent`). No token → `AccessTokenUnavailableException` (an
  `HttpRequestException`), logged 11003. A provider is resolved keyed by the client name.
- **TLS**: `ClientTls.Apply` on the primary handler: PEM (+ key file, or in the same file) or PKCS#12; on Windows a PEM
  certificate is re-imported as PKCS#12 (SChannel cannot use an ephemeral key). `TrustedCertificateAuthoritiesPath` →
  `CustomRootTrust`, no revocation check, the server's intermediates added, a name mismatch never forgiven.
- **`AccessToken.ToString()`** redacts the value. Tokens and secrets are never logged.

---

## Hard violations

| Violation | Why |
| --- | --- |
| A reference to ASP.NET Core, `IHttpContextAccessor`, `SharedKernel.Security.*`, `SharedKernel.Contracts` (in `.Grpc`), or `.Rest` ↔ `.Grpc` | Tier and purity rules |
| An option read once at registration and captured (other than the discovery mode) | Configuration reloads and `PostConfigure` would be ignored |
| A retry or hedge of POST/PATCH without an `Idempotency-Key` by default | Duplicate side effects |
| A switch that does not switch (`Enabled = false` that leaves the breaker on, a retry count that cannot be 0) | Dead options |
| A handler or interceptor that throws on propagation, overwrites a caller-supplied value, or holds request state | Best-effort, caller-wins, stateless |
| Propagation handlers registered inside (after) the resilience handler | Headers must be set once per call |
| Writing `traceparent`/`tracestate` by hand | The HTTP diagnostics handler writes them from the right span |
| A correlation id from `Activity.Id`/`TraceId`, or formatted other than `"D"` | Correlation is the caller's id |
| A header or metadata name typed as a literal | `WellKnownHeaders` is the one source |
| A result helper that throws for a failed call (other than the caller's cancellation) | Results, not exceptions |
| `ProblemDetailsDeserializer` reading `type`/`title` as `Error.Code`, or `errors` on a status other than 400/422 | P-544, P-562 R38 |
| A token, secret or API key in a log message, exception message or `ToString()` | Credentials never leave the process |
| `DateTime.UtcNow`/`DateTimeOffset.UtcNow` in production code | SK0001 — inject `IClock` |
| Direct `ILogger.LogXxx` or `LoggerMessage.Define` | `[LoggerMessage]` only (SK0020/SK0021) |

---

## Logging (EventId 11000–11999)

| Sub-block | Package | Events |
| --- | --- | --- |
| 11000–11099 | `SharedKernel.Communication` | 11000 token acquired (Debug) · 11001 token endpoint refused the client (Warning) · 11002 token endpoint unreachable (Warning) · 11003 no access token, request not sent (Warning) · 11004 401 answered with a new token (Debug) |
| 11100–11199 | `.Grpc` | 11100 the caller could not be written onto a call (Error) |
| 11200–11299 | `.Rest` | none |
| 11300–11399 | — | retired (was `.Internal`) |

---

## Test Rules

- Tests are nested in each project folder, all in the Unit lane (no Docker): `SharedKernel.Communication.Tests`,
  `.Rest.Tests`, `.Grpc.Tests`, and `16.Testing/SharedKernel.Communication.Testing.Tests`.
- REST: a client registered with `AddRestClient` over a `StubHttpMessageHandler` (`UseStubHttpMessageHandler`), so the
  whole pipeline runs. Settings through in-memory configuration, validated with `IStartupValidator.Validate()`.
  `Retry:BaseDelay` 0 in tests. Slow services with `RespondAsync` and the handler's cancellation token.
- gRPC: a real service on `TestServer` (`Grpc.AspNetCore`, a test proto) behind a `ResponseVersionHandler`, set as the
  client's primary handler inside the `configure` callback. Transport failures: a stub primary handler that throws an
  `HttpRequestException` with a `SocketException` inner, as a real refusal does.
- Token caching with `FakeClock`; certificates made in memory (`CertificateRequest`), PEM/PKCS#12 files in a temp directory.
- EventIds are checked over the real assemblies with `LoggingEventIdIntegrityAssertion` (base and `.Grpc` together).
- End-to-end caller propagation (HTTP → REST/gRPC, consumer → REST, job → REST) is `13.ServiceDefaults.Security`'s
  `EndToEndPropagationTests`; two real services over both protocols is `samples/CheckoutApi.Tests`.
- `consumer-verify/` composes both packages through a real generic host from configuration: startup validation,
  discovery, propagation, ProblemDetails and unreachable results, rich status and `Money`.
