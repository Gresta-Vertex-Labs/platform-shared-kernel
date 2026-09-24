# 14.Presentation — Inbound API Boundary

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read each package's own `README.md`; the folder overview is [`README.md`](README.md) and every setting
> is in [`CONFIGURATION.md`](CONFIGURATION.md).
> This brain holds what the source does not make obvious: rules, traps, invariants, couplings and decisions.
> The design and review records of the 2026-09-23/24 gold-standard pass are in [`docs/p562/`](docs/p562/). The brain
> before that pass (WO-031 through WO-078) lives in [`CLAUDE.history.md`](CLAUDE.history.md) and describes types that
> no longer exist.

## What This Domain Is

The inbound API boundary. It turns outcomes (`Result`, `Error`, exceptions) into what a caller receives: an HTTP
response or RFC 9457 problem, a SignalR `HubException`, a gRPC `google.rpc.Status`. One error contract serves all three
protocols. It also owns the concerns of the boundary: authorization against `IUserContext`, correlation ids, inbound
baggage, security headers, CORS, request limits, `Idempotency-Key`, `ETag` and `If-Match`.

It converts outcomes; it never produces them. The domain references `01.Core` and `12.Security.Abstractions` only:
never `05.Application` (MediatR dispatch is the service's), never an infrastructure layer, and in practice never
`04.Contracts` (`.Grpc` must never). Outbound calls are `11.Communication`'s.

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Presentation.WebApi` (core) | One-call setup and pipeline; the error contract (`ErrorPresentation`, problem details for every source); typed results for `Result`; the four authorization attributes as native policies; correlation ids and inbound-baggage refusal; security headers; CORS and the WebSocket origin check; request limits; required and accepted `Idempotency-Key`/`If-Match`; `ETag`/304; the 429 body | `SharedKernel.Primitives`, `.Core`, `.Configuration`, `.Localization`, `SharedKernel.Security.Abstractions`; ASP.NET Core shared framework. **No third-party packages** |
| `SharedKernel.Presentation.OpenApi` (add-on) | API versioning, one OpenAPI 3.1 document per version, Scalar, sunset/deprecation policies; documents what the core enforces, never changes a response | WebApi; `Asp.Versioning.Http` 10.2.3, `Asp.Versioning.Mvc.ApiExplorer` 10.2.1, `Asp.Versioning.OpenApi` 10.2.3, `Microsoft.AspNetCore.OpenApi` 10.0.11, `Scalar.AspNetCore` 2.17.8 |
| `SharedKernel.Presentation.SignalR` | Hub error mapping (`{code}: {message}`), `Result` hub methods, the invocation rate limit, tenant and correlation accessors, group naming | WebApi, Primitives, Core, Configuration, Security.Abstractions. No third-party packages |
| `SharedKernel.Presentation.Grpc` | The exception interceptor building the rich status, `GrpcStatusCodeMap`, `GrpcErrorCodes` | WebApi; `Grpc.AspNetCore` 2.80.0, `Grpc.StatusProto` 2.80.0, `Google.Api.CommonProtos` 2.17.0 (the first with `FieldViolation.reason`) |

Every package tracks its public API (`PublicAPI.Shipped.txt` empty, `PublicAPI.Unshipped.txt` populated;
RS0016/RS0017/RS0022/RS0024/RS0025/RS0036/RS0037 and CS1591 are errors). None is published to the feed yet. Versions
come from the repo-wide MinVer tag; never add a `<Version>`.

## The model

### One error contract

- **`ErrorPresentation` decides for every protocol.** `GetStatusCode(error, httpContext)` is `ErrorTypeStatusCodeMap`
  plus [the 412 rule](#conditional-requests); `GetClientMessage(error, httpContext)` translates (an optional
  `ILocalizationCatalog` from `RequestServices`, culture from `IRequestCultureFeature`, else `CurrentUICulture`) and
  redacts server categories outside Development (`IHostEnvironment` from `RequestServices`; no context = production);
  `IsServerError(type)` is "status ≥ 500". HTTP, SignalR and gRPC all call it, so the same error has the same text
  everywhere. Never map `ErrorType` in a switch of your own.
- **Status maps.** HTTP: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, BusinessRule 422,
  Unexpected 500, Unavailable 503, Timeout 504, anything else 500. gRPC (`GrpcStatusCodeMap`, a sibling never merged):
  InvalidArgument, Unauthenticated, PermissionDenied, NotFound, Aborted, FailedPrecondition, Internal, Unavailable,
  DeadlineExceeded, anything else Unknown. `ErrorType.Unavailable = 8` and `Timeout = 9` were appended by P-562
  (01.Core); 17.Workflows persists the number, so never renumber.
- **One writer.** Every HTTP error goes through `ProblemFactory` (builds the body: framework title and type via
  `TypedResults.Problem`, then `ProblemDetailsCustomizer.Apply`) and `ProblemResponseWriter.WriteAsync` (status,
  `Cache-Control: no-store`, `Retry-After` on 503, `IProblemDetailsService.TryWriteAsync`, and a direct
  `application/problem+json` write when no writer accepts or a writer writes nothing). Framework-generated problems are
  completed by `ProblemDetailsCustomizer` through `CustomizeProblemDetails` (instance, `errorCode` `http.{status}`,
  `correlationId`, `traceId`, `type` for 428/429, `TypeBaseUri`). A new error path uses these two; it never writes
  JSON itself.
- **Exception handling is the fallback.** `SharedKernelExceptionHandler.Install` sets
  `ExceptionHandlerOptions.ExceptionHandler` (PostConfigure) unless the service set `ExceptionHandler` or
  `ExceptionHandlingPath`, so a service's `IExceptionHandler` runs first. It sets `AllowStatusCode404Response` (a
  `NotFoundException` is a real 404, a gRPC answer starts no response) and chains `SuppressDiagnosticsCallback` so each
  exception it handled is logged once, by it. Order of cases: aborted request (499, no body, any exception type);
  `BadHttpRequestException` 400 (validation shape, `validation.invalid_format`, no .NET type names); other
  `BadHttpRequestException` (its status); `ValidationException` (400, identical to a returned error);
  `SharedKernelException`; `TimeoutException`/non-abort `OperationCanceledException` (504 `timeout.default`);
  anything else (500 `unexpected.exception`, `exception` member only in Development or with
  `IncludeExceptionDetails`). A gRPC request gets the status, no body.
- **Framework 400s are ours.** `RouteHandlerOptions.ThrowOnBadRequest = true` in every environment; MVC's
  `InvalidModelStateResponseFactory` is replaced only when it is MVC's own (`RequestValidationErrors`), and
  `AllowInputFormatterExceptionMessages = false` keeps System.Text.Json's type names out of model state.
- **Field errors.** `errors`/`errorCodes` are keyed by `ErrorArgumentNames.PropertyPath`, else by the code; JSON paths
  lose `$.`; each message is presented on its own. gRPC mirrors this as `BadRequest` violations, capped at 50 and 3 KB
  with a `grpc.more_field_violations` summary (R31).

### The pipeline

`UseSharedKernelWebApi(configure)` order: inbound-baggage removal (unless `TrustInboundBaggage`) → `AtStart` hooks →
correlation id → HSTS (not Development) → security headers → `UseExceptionHandler()` → status code pages (not gRPC) →
`UseRouting()` → CORS + WebSocket origin check (only with origins) → `BeforeAuthentication` hooks →
`UseAuthentication()` (only when `IAuthenticationSchemeProvider` is registered) → `BeforeAuthorization` hooks →
`UseRateLimiter()` (only when an `IConfigureOptions<RateLimiterOptions>` exists) → `UseAuthorization()` →
`HeaderRequirementsMiddleware`. The reasons are load-bearing:

- HSTS and the security headers sit before the exception handler, which clears headers; `SecurityHeadersMiddleware`
  remembers the HSTS value and writes it again at `OnStarting` (R11). Moving `UseHsts()` alone does not work.
- Rate limiting sits before authorization so refused traffic is counted (R1), after authentication so policies can
  partition by caller.
- Header requirements sit after authorization so an anonymous caller is told to authenticate, never which header it
  forgot (R6).
- A second call is a no-op (`app.Properties` key); `WebApiPipelineState` feeds the 14011 warning.

### Authorization

- The four attributes derive from `AuthorizeAttribute` (SignalR authorizes hub methods only through it). The requirement
  is encoded in the policy name, `SharedKernel:{kind}:{v1}|{v2}…` (`permission`, `role`, `fresh`, `amr`, and
  `amr-max-age` with the age first); `SharedKernelAuthorizationPolicyProvider` decodes it and builds a policy with
  `RequireAuthenticatedUser()` plus the requirement, so anonymous is always 401 first. `Policy` and `Roles` are hidden
  read-only members, and the `IAuthorizeData` setters throw: the requirement cannot be replaced.
- `SharedKernelRequirementHandler` resolves the caller with `UserContextResolver.Resolve(context.User, mappers)`, never
  raw claims. A principal no mapper understands fails with a reason and log 14009 (R16). The clock (`IClock`, else
  `SystemClock`) is resolved only when a freshness or max-age requirement is evaluated.
- `AuthenticationMethodRequirement` with a max age uses `IUserContext.GetAuthenticationMethodTime` (X1, `amr_time`):
  unknown time never passes, a time more than `UserContext.MaxFutureAuthTime` ahead never passes.
- `SharedKernelAuthorizationResultHandler` writes every refusal: challenge (the scheme's own, or
  `WWW-Authenticate: Bearer` without a scheme, also for gRPC — R15), step-up (only when **every** unmet requirement is
  freshness or method; RFC 9470 challenge, smallest `max_age`, scheme `DPoP` or `Bearer` only — R26), forbid (403,
  message never names the requirement). It writes a body only for a plain 401/403 the scheme did not redirect or
  start, never for gRPC, and logs 14002 for every refusal.

### Required and accepted headers

- Endpoint metadata is the single source: `IIdempotencyKeyRequiredMetadata`/`IIdempotencyKeyAcceptedMetadata`,
  `IIfMatchRequiredMetadata`/`IIfMatchAcceptedMetadata`. The attributes and conventions only add metadata; the
  `IdempotencyKey` and `IfMatch<TVersion>` parameters add it through `IEndpointParameterMetadataProvider`, required
  when declared not-null, accepted when nullable, **required when nullability cannot be read** (the safe reading).
  `HeaderRequirementsMiddleware` enforces it for every endpoint kind; OpenAPI documents the same metadata.
- Required wins over accepted. An accepted header that is sent is validated exactly like a required one and is never
  read as missing (J1): that would turn a conditional request unconditional, or run a retry twice.
- `IdempotencyKey` and `IfMatch<TVersion>` are `sealed record` classes: minimal APIs read no parameter metadata from
  `Nullable<T>`, so a struct could not express "accepted".
- `IfMatch<TVersion>` adds an internal `IEntityTagValidator`, so a strong tag that does not parse as `TVersion` is 412
  before binding. The attributes and conventions cannot parse, so MVC actions parse the tag themselves.
- The parameters' `BindAsync` throws a 400 `BadHttpRequestException` for an unusable header, so a host without the
  middleware still never binds it as missing.

### Conditional requests

- **The 412 rule (R7).** A `Conflict` whose code is in `Problems:PreconditionFailedErrorCodes`, in a request with a
  non-blank `If-Match` or `If-None-Match`, is 412 with its code, returned or thrown, on any endpoint. Every other
  conflict is 409. The rule lives only in `ErrorPresentation.GetStatusCode`; `ErrorHttpResult.StatusCode` stays the
  type's status (R28). The defaults are literals because 14 may not reference 06/08; 00.Governance pins them.
- `If-Match` checks (RFC 9110 section 13.1.1): missing → 428 if required; `*` → 428 if required, 400 if accepted;
  malformed or several tags → 400; weak → 412 (strong comparison); otherwise pass.
- `OkWithETag<T>` writes a strong `ETag`, answers 304 only for `GET`/`HEAD` with a weakly matching `If-None-Match`
  (R14), and adds `IETagResponseMetadata` (200, plus 304 only for read methods) for OpenAPI.

### Correlation and baggage

- `CorrelationIdMiddleware`: one inbound value, length ≤ `MaxLength`, no control characters, pattern match (the
  default is a `[GeneratedRegex]`, a custom one is compiled once with a 100 ms timeout); else the W3C trace id, else a
  GUID. Stored in `HttpContext.Items` for `GetCorrelationId()`, set as baggage `WellKnownBaggageKeys.CorrelationId`,
  written at `OnStarting`. A rejected value is logged by length only.
- Inbound baggage is refused twice (R3): `InboundBaggagePropagator` decorates the DI `DistributedContextPropagator`
  that hosting reads before any middleware (so hosting's first log record carries no forged item), and
  `InboundBaggage` removes what still reached the request `Activity`. Outgoing `Inject` is unchanged. OpenTelemetry's
  own `Baggage.Current` is 13.ServiceDefaults' concern (X2).

## Composition rules (the traps)

- **`UseSharedKernelWebApi()` first.** Anything a service must run inside it goes in a hook; `AtStart` middleware runs
  outside the exception handler. Without it the host still works but logs 14011. Without `AddSharedKernelWebApi()` it
  throws `InvalidOperationException`.
- **Decorate, never replace.** `AddSharedKernelAuthorization()` decorates the `IAuthorizationPolicyProvider` and
  `IAuthorizationMiddlewareResultHandler` registered before it (`ServiceDecoration.Decorate`: last non-keyed
  registration, in place, same lifetime); `AllowsCachingPolicies` follows the inner provider.
  `SharedKernelAuthorizationStartupCheck.StartingAsync` throws when a later registration displaced either (a probe
  policy name must resolve to the platform requirement; the resolved handler must be ours). Keep that check whenever
  the decoration changes.
- **`AddSharedKernelAuthorization()` is shared and idempotent** (marker service). WebApi, SignalR and gRPC setups all
  call it; it calls `AddAuthorization()` first so the framework defaults exist to be decorated, and `AddClock()`.
- **Every authentication scheme needs an `IUserContextMapper`.** The startup check warns per scheme (14010), skipping
  remote sign-in handlers (`IAuthenticationRequestHandler`) and policy schemes.
- **Setup methods are idempotent** through a marker (`WebApiPipelineState`, `OpenApiSetupState`,
  `SignalRServicesMarker`, `GrpcServicesMarker`); each `configure` is applied on every call.
- **Options bind with `AddValidatedOptions` + `ISectionBoundOptions`** and a validator (`WebApiOptionsValidator`,
  `SharedKernelOpenApiOptionsValidator`, `SharedKernelSignalROptionsValidator`, data annotations for gRPC). List options
  with defaults are get-only lists: configuration appends. Collection defaults must stay documented as "added to".
- **Invalid WebApi settings surface at first read**: Kestrel reads them at `Build()`, `UseSharedKernelWebApi()` reads
  them with `TestServer`, `ValidateOnStart` at the latest (R28). Document exceptions that way, never "at startup".
- **Chained hooks keep the service's.** `CustomizeProblemDetails` is chained in PostConfigure (platform first, service
  after); `ApiVersioningProblems.Chain` prepends its normalization; `SuppressDiagnosticsCallback` chains the service's;
  a service's own `OnRejected` and `InvalidModelStateResponseFactory` are kept.
- **Global filters and interceptors nest by registration order.** SignalR: `HubExceptionMappingFilter` outermost, then
  `HubInvocationRateLimitFilter`. gRPC: `GrpcExceptionInterceptor` first, so it is outermost. Consumers must call the
  setup before adding their own.
- **The OpenApi add-on** registers `EntryAssemblyXmlComments`' transformer before `AddOpenApi()` (Asp.Versioning looks
  for the XML of the assembly that called it, which would be ours), adds the platform transformers through
  `IConfigureOptions<VersionedOpenApiOptions>` so they run after Asp.Versioning's, and reads MVC action metadata from
  the endpoint (`EndpointMetadataLookup`) because the API Explorer omits endpoint conventions. `MapSharedKernelOpenApi()`
  returns a `CompositeEndpointConventionBuilder` whose conventions the 14301 check reads; outside Development without
  `ExposeInProduction` it maps nothing and returns an empty one.
- **Namespaces.** Everyday types in each package's root namespace (R21); options in `.Options`; `ErrorPresentation`,
  maps and constants in `.Errors`; metadata interfaces in `.Http`/`.Idempotency`. Never create a namespace named
  `Results` (it shadows `Microsoft.AspNetCore.Http.Results`).
- **No duplicate result extensions.** No `ToActionResult` (MVC returns typed results, R19) and no gRPC
  `ThrowIfFailure`/`GetValueOrThrow` (they collided with `SharedKernel.Core`'s, CS0121, R32).
- **No reflection invocation.** `HubMethodResult` reads `Result<T>` through compiled expression accessors cached per
  closed type; never `MakeGenericMethod`.
- **Constants, never literals** for header names (`WellKnownHeaders`, `HeaderNames`, `PresentationHeaderNames`),
  problem members (`ProblemDetailsExtensionNames`) and codes (`PresentationErrorCodes`, `GrpcErrorCodes`, `ErrorCodes`).

## Invariants

- **One shape.** Every HTTP error is `application/problem+json` with `type`, `title` (reason phrase, never the code),
  `status`, `instance`, `errorCode`, `traceId`, `correlationId`, and `Cache-Control: no-store` (R27), even when the
  `Accept` header excludes JSON. `type` is never a third-party site.
- **Redaction.** Server-category text is replaced outside Development on HTTP, SignalR and gRPC (including rebuilt
  foreign `RpcException`s, R30); the `errorCode` is kept. Without a request, the environment is treated as production.
- **Fail closed.** Unmapped principal → 403; every platform policy requires an authenticated user; policy names cannot
  be forged; a later provider or result handler stops the host; step-up applies only when every unmet requirement is a
  step-up requirement.
- **No principal data or secrets in logs.** Refusals log the endpoint display name and code (14002); rejected
  correlation ids log their length (14006); idempotency keys are never logged (14003).
- **No caller-controlled text reaches clients or logs unvalidated.** Correlation ids are validated; the step-up
  challenge echoes only `Bearer` or `DPoP`; 403 messages never name permissions or roles.
- **Inbound baggage is not trusted** unless `TrustInboundBaggage`.
- **Headers.** A declared header is validated before the endpoint runs; an accepted header is never read as missing;
  required wins.
- **Security headers** never overwrite an endpoint-set header; HSTS never in Development, never over HTTP, never to
  `localhost`.
- **CORS.** No policy without origins; startup validation refuses credentials with no or wildcard origins, the `null`
  origin, and outside Development credentials with `http://` origins (R24); with origins, WebSocket requests from
  disallowed origins get 403 (R25).
- **gRPC.** Cancellation wins over every other mapping (R33); a foreign `RpcException` keeps only its code (R30); a
  status always fits an 8 KB trailer limit (R31); the domain is never blank.
- **SignalR.** Hub errors are `HubException("{code}: {message}")`; tenantless connections get `null`, never
  `Guid.Empty`, and `HubGroupNaming.TenantGroup(Guid.Empty)` throws; the rate limiter is one partitioned limiter with
  no per-bucket timer.
- **OpenAPI** documents only what the core enforces, adds and never replaces, and maps nothing outside Development
  unless `ExposeInProduction`.

## Logging (EventId 14000–14999)

`LoggingEventIdRanges.Presentation` is 14000. Each package owns a 100-wide sub-block. Every `[LoggerMessage]` has an
explicit id, pinned with its level by a reflection test in each package (`LoggerMessageEventIdTests`). Retired ids are
never reused; the tests assert their absence.

| Range | Package | Events in use |
| --- | --- | --- |
| 14000–14099 | WebApi | 14000 Debug correlation id assigned · 14001 Error server error from an exception · 14002 Warning authorization refused (endpoint, code) · 14003 Warning idempotency key refused (endpoint, code) · 14004 Critical CORS settings invalid · 14005 Warning rate limit rejected · 14006 Warning inbound correlation id rejected (length) · 14007 Debug client error from an exception · 14008 Debug client closed the request (499) · 14009 Warning principal without `IUserContextMapper` · 14010 Warning scheme without mapper (startup) · 14011 Warning `UseSharedKernelWebApi()` never called (startup) · 14012 Warning exception details outside Development (startup) · 14013 Warning WebSocket origin refused |
| 14100–14199 | SignalR | 14100 Error unhandled hub exception · 14101 Warning invocation rate limited · 14103 Error hub server error · 14104 Debug hub client error · 14106 Debug connection closed during an invocation · 14107 Error stream inside a `Result`. Retired: 14102 (CORS diagnostic), 14105 (hub-method authorization filter) |
| 14200–14299 | Grpc | 14200 Error unhandled exception · 14202 Error server error · 14203 Debug client error · 14204 Debug call cancelled. Retired: 14201 (authorization interceptor; refusals are 14002 now) |
| 14300–14399 | OpenApi | 14300 Information documents not mapped (environment) · 14301 Warning documents exposed without authorization (startup) |

A new log statement takes the next free id of its package's sub-block and a row in that package's test.

## Decisions

The full records: [`docs/p562/p562-design.md`](docs/p562/p562-design.md) (D0–D16),
[`p562-final-review-findings.md`](docs/p562/p562-final-review-findings.md) (R1–R38, X1–X4, integration round 2,
follow-ups) and [`p562-review-findings.md`](docs/p562/p562-review-findings.md) (B, R, F findings).

| Decision | Why |
| --- | --- |
| Native authorization policies instead of endpoint filters (D3) | Filters were fail-open by omission (B1), gave anonymous callers 403 (B2), and did not reach SignalR hub methods |
| One error pipeline through `IProblemDetailsService` and `ErrorPresentation` (D1) | Localization never ran, server text leaked, and the exception handler wrote `application/json` (B3–B6) |
| `ErrorType.Unavailable`/`Timeout` → 503/504 and `Unavailable`/`DeadlineExceeded` (D14) | Outages were 500s; clients could not tell "retry later" from a defect (B7) |
| OpenAPI in a separate add-on (D0, D11) | Keeps the core free of third-party packages; Asp.Versioning 10 supplies per-version documents and sunset/deprecation policies |
| 412 decided from the error, not the endpoint (R7) | An optional conditional write got 409 where RFC 9110 answers 412 |
| Required **and** accepted headers (J1) | Accepted headers were unvalidated, so a malformed one silently disabled a precondition or idempotency |
| Typed results for MVC; no `ToActionResult` (R19) | The MVC family was weaker and documented wrong statuses; typed results work in controllers |
| No gRPC result extensions (R32) | Identical signatures to `SharedKernel.Core`'s made calls ambiguous (CS0121) |
| Foreign `RpcException`s rebuilt without trailers (R30) | Another service's `ErrorInfo`, field paths and trace ids leaked to our callers |
| Removed: uploads, payload-limit middleware, version-lifecycle middleware, security-header option family, CORS wrapper, SignalR backplane/CORS diagnostic/`HubOptions` pins, gRPC correlation/tenant/authorization interceptors (D15) | Unused, duplicated the framework or the shared pipeline, or were defective (B9, B10, B11, B16) |
| .NET 10 `AddValidation()` not adopted | DataAnnotations only, experimental parts, no FluentValidation hook; validation lives in the MediatR pipeline |
| `[AllowAnonymous]` switches off every requirement | ASP.NET Core semantics; not changed |
| The automatic 412 carries no current `ETag` | Clients re-read, the safer choice |
| gRPC authorization refusals carry no rich status | They are answered by the HTTP pipeline before the service runs; not changed |

## Cross-Domain Couplings

- **01.Core:** `Error`, `ErrorType` (with `Unavailable = 8`, `Timeout = 9`), `ErrorCodes`, `ErrorArgumentNames`,
  `WellKnownHeaders` (`CorrelationId`, `IdempotencyKey`), `WellKnownBaggageKeys.CorrelationId`,
  `LoggingEventIdRanges.Presentation`, `AddValidatedOptions`/`ISectionBoundOptions`, `ILocalizationCatalog`,
  `IClock`/`AddClock`. `Error.ToException()` (Core) is how gRPC and streaming hubs end a failed `Result`.
- **12.Security:** `IUserContext`, `IUserContextMapper`, `UserContextResolver`, `ITenantProvider`; X1's
  `GetAuthenticationMethodTime`, `amr_time` and `UserContext.MaxFutureAuthTime`. The attributes read only these.
- **05.Application:** `IIdempotentRequest` consumes the key; `idempotency.key_required` is pinned to
  `IdempotencyErrorCodes` by a 00.Governance test (X3). `AuthorizationBehavior` answers like the attributes (401
  anonymous, 403 missing permission).
- **06.Persistence / 08.Storage:** `EntityVersion` (`IParsable`, opaque sealed token since X4) is the usual `TVersion`;
  the default `PreconditionFailedErrorCodes` are their codes, pinned by `PresentationPreconditionCodesTests`. Presigned
  uploads (08) replace upload validation.
- **11.Communication.Rest** reads the problem back: `errorCode` (fallback `http.{status}`, never `title`), `errors`
  only for 400/422, 412 → Conflict, 413/415/428 → Validation, 429/503 → Unavailable, 504 → Timeout (R38).
  `PresentationErrorCodes.ForStatus` and its fallback must stay identical.
- **13.ServiceDefaults:** `AddSharedKernelRateLimiting()` leaves `OnRejected` null so our 429 body applies; its
  `BaggageLogRecordProcessor` allow-list and propagator decoration complete the baggage refusal (X2).
- **16.Testing:** the gRPC `TestServerCallContext` (adds `WellKnownHeaders.CorrelationId`; no pipeline, so
  `GetCorrelationId()` is `null`); `FakeUserContext.WithAuthenticationMethodTime` for step-up tests.
- **00.Governance:** `PresentationLayeringRules` (`NoDirectProblemDetailsConstructionOutsideWebApi`,
  `NoInlineResultBranchBeforeHttpResultOutsideWebApi`, `NoOpenApiStackDependencyOutsideOpenApiAddOn`,
  `GrpcNeverReferencesContracts`); SK0022 (magic strings), SK0032 (CORS wildcard with credentials), SK0036 (raw
  `RpcException`/`Status` outside `.Grpc`); `PresentationPreconditionCodesTests`, `PresentationIdempotencyCodesTests`.
- **samples** (OrderApi, BillingApi, DocumentsApi, ShippingApi, CatalogApi) and `consumer-verify` use the one-call
  path; a public API change updates them.

## Test Rules

- Behaviour is tested through real in-process hosts built with the one-call setup (`WebApiTestHost`, `FullStackHost`
  on `TestServer`); unit tests cover pure logic. What `TestServer` does not enforce (body limits, the `Server` header)
  runs on Kestrel (`StartKestrelAsync`); HSTS needs an `https` base address that is not `localhost`.
- Every HTTP error assertion goes through `ShouldBeProblemAsync`, which checks the media type, the member set and the
  `X-Correlation-Id` header against `correlationId`.
- SignalR tests use a real `HubConnection`; gRPC tests a real `Grpc.Net.Client` channel; OpenAPI tests generate the
  documents. `consumer-verify` composes all four packages over Kestrel, a `HubConnection` and a gRPC channel and runs
  in CI's required lane: keep it passing.
- Time through `FakeClock` as `IClock`; never `Task.Delay`.
- A security-relevant test must be able to fail: mutate the condition and confirm the assertion catches it (the R11
  HSTS test fails without the re-apply).
- Log assertions use `16.Testing`'s in-memory logger and check the EventId and level, never rendered text.

## Changelog

> Entries before 2026-09-23 are in [`CLAUDE.history.md`](CLAUDE.history.md).

- [2026-09-24] **Root P-562 — gold-standard pre-publish pass.** Breaking rewrite of the three packages and a new fourth,
  `SharedKernel.Presentation.OpenApi` (API versioning, OpenAPI, Scalar moved out of the core, which now has no
  third-party packages). One-call setup (`AddSharedKernelWebApi`/`UseSharedKernelWebApi` with ordered hooks), one
  problem shape for every source through `ErrorPresentation`, typed results for `Result` (MVC included), native
  authorization policies behind the four attributes (SignalR and gRPC included), required and accepted
  `Idempotency-Key`/`If-Match` with parameter types, ETag/304, the error-driven 412 rule, inbound-baggage refusal,
  503/504, coded SignalR errors with `HubErrorMessage.TryParse`, gRPC rich status with capped violations and sanitized
  foreign statuses. Cross-domain: `ErrorType.Unavailable`/`Timeout`, step-up expiry on long-lived connections (X1),
  baggage allow-listing (X2), idempotency per caller (X3), opaque ETags (X4). Removed types are listed in
  `CLAUDE.history.md`. Brain rewritten to rules; READMEs, `CONFIGURATION.md` and the domain README rewritten. Not yet
  published.
