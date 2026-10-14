# 14.Presentation — Domain Brain

> The inbound API boundary: turns `Result`, `Error` and exceptions into an HTTP response or RFC 9457 problem, a SignalR
> `HubException` or a gRPC `google.rpc.Status` under one error contract, and owns the edge (endpoint authorization,
> security headers, CORS, limits, `Idempotency-Key`, `ETag`/`If-Match`, paging, versioning/OpenAPI, GraphQL
> conventions). It converts outcomes, never produces them, and references neither `05.Application` nor MediatR. Not
> owned here: the request context (`13.ServiceDefaults`' `UseSharedKernelRequestContext()`), outbound calls
> (`11.Communication`), the rate limiter itself. Every setting: [`CONFIGURATION.md`](CONFIGURATION.md).

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Presentation.Core` | Host | What every protocol shares, so gRPC needs no WebApi: authorization attributes and their internal machinery, `ErrorTypeStatusCodeMap`, the client-message half of `ErrorPresentation`, `RequestFacts` (correlation id, trace id), `EdgeFailureRecording`, `ServiceDecoration`. No third-party packages |
| `SharedKernel.Presentation.WebApi` | Host | One-call setup and pipeline, endpoint modules, the problem contract, typed results, security headers, CORS, limits, header requirements, paging, `ETag`/304, the 429 body. References Contracts for paging only; **no third-party packages** |
| `SharedKernel.Presentation.WebApi.Generators` | Tooling | Endpoint-module source generator (`netstandard2.0`, SKEP001–SKEP004); not packable, packed inside WebApi under `analyzers/dotnet/cs` |
| `SharedKernel.Presentation.OpenApi` | Host | Asp.Versioning, one OpenAPI 3.1 document per version, Scalar, sunset/deprecation, security schemes; documents what the core enforces, never changes a response |
| `SharedKernel.Presentation.SignalR` | Host | Hub error mapping (with the id trailer), `Result` hub methods, invocation rate limit, `RequestContextHubFilter`, `HubGroupNaming`. No third-party packages, no backplane |
| `SharedKernel.Presentation.Grpc` | Host | Exception interceptor building the rich status, `GrpcStatusCodeMap`, `GrpcErrorCodes`. **Never references WebApi or Contracts** |
| `SharedKernel.Presentation.GraphQL` | Host | HotChocolate conventions: snake_case filtering, `FilterBase<T>`/`SortBase<T>`, `PagedResponseType<T>`, `SharedKernelErrorFilter` (ProblemDetails parity: shared status table, client message, ids; logs 14400-14401), `GraphQLErrorCodes`, code-only `GraphQLOptions`. References Core and WebApi (internals visible to it) and `SharedKernel.Core`. Not AOT-safe |

`consumer-verify/` (untiered) composes Core, WebApi, OpenApi, SignalR and gRPC over Kestrel in CI's required lane.
`SharedKernel.Presentation.EdgeCases` (Testing tier, never packed) is the edge-mapper conformance catalogue the four protocol suites assert (rule 34).
Core/WebApi/OpenApi/SignalR/Grpc track `PublicAPI.*.txt`; GraphQL does not.

## Public Entry Points

Overloads, options and defaults: each package's `README.md` and [`CONFIGURATION.md`](CONFIGURATION.md).

- **WebApi** (`SharedKernel:Presentation:WebApi`) — `builder.AddSharedKernelWebApi()`; `app.UseSharedKernelWebApi(p => …)`
  with `WebApiPipeline` hooks `AtStart`/`BeforeAuthentication`/`BeforeAuthorization`; `IEndpointModule` + generated
  `app.MapEndpoints()`; `ToOk`/`ToCreated`/`ToOkWithETag`/`ToHttpResult`/`ToErrorResult`, `Error.ToProblemDetails()`;
  `IdempotencyKey`/`IfMatch<TVersion>` parameters with `[Require…]`/`[Accept…]` attributes or conventions; `Paging`,
  `CursorPaging`; `PresentationErrorCodes`, `ProblemDetailsExtensionNames`.
- **Core** — `[RequireEndpointPermission]`, `[RequireRole]`, `[RequireFreshAuthentication]`,
  `[RequireAuthenticationMethod]` and matching conventions. No registration call: WebApi, SignalR and gRPC register it.
- **OpenApi** (`SharedKernel:Presentation:OpenApi`) — `builder.AddSharedKernelOpenApi()`, `app.MapSharedKernelOpenApi()`
  (maps nothing outside Development unless `ExposeInProduction`).
- **SignalR** (`SharedKernel:Presentation:SignalR`) — `builder.AddSharedKernelSignalR()`; `Context.GetTenantId()`,
  `Context.GetCorrelationId()`, `HubErrorMessage.TryParse` (two overloads: code and message, or also the ids of the
  trailer), `HubGroupNaming.TenantGroup(TenantId)`. Backplane: Microsoft's `AddStackExchangeRedis(...)`.
- **Grpc** (`SharedKernel:Presentation:Grpc`) — `builder.AddSharedKernelGrpc()`; services end a failed `Result` with
  `Error.ToException()` (`SharedKernel.Core`).
- **GraphQL** — `services.AddSharedKernelGraphQL()` (calls HotChocolate's `AddGraphQLServer()`, so `app.MapGraphQL()`
  works) before the service's own `AddGraphQLServer()`; `GraphQLErrorCodes.RequestInvalid`.

Canonical host pipeline (`samples/Shop/Ordering/Shop.Ordering.Api/Program.cs`, `samples/Shop/Catalog/Shop.Catalog.Api/Program.cs`):

```csharp
app.UseSharedKernelRequestContext();   // 13.ServiceDefaults.Security — first: correlation id, baggage refused, scope
app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));
app.MapEndpoints();
```

`UseSharedKernelWebApi` order: `AtStart` → HSTS (not Development) → security headers → `UseExceptionHandler()` →
status code pages (not gRPC) → `UseRouting()` → CORS + WebSocket origin check (only with origins) →
`BeforeAuthentication` → `UseAuthentication()` (when schemes exist) → `BeforeAuthorization` → `UseRateLimiter()`
(when configured) → `UseAuthorization()` → `HeaderRequirementsMiddleware` (headers, then paging).

## Rules & Invariants

1. **One error decision for every protocol.** `ErrorPresentation` (message half and `ErrorTypeStatusCodeMap` in Core,
   `GetStatusCode` in WebApi) decides status and client text for HTTP, SignalR and gRPC. Never map `ErrorType` in a
   switch of your own. gRPC's `GrpcStatusCodeMap` is a sibling, never merged. BusinessRule → 422, Unavailable → 503
   (+`Retry-After`), Timeout → 504, unknown → 500/`Unknown`. Never renumber `ErrorType` (17.Workflows persists it).
2. **One problem shape.** Every HTTP error is `application/problem+json` with `type`, `title` (reason phrase, never the
   code), `status`, `instance`, `errorCode`, `traceId`, `correlationId` and `Cache-Control: no-store`, even when
   `Accept` excludes JSON. New error paths use `ProblemFactory` + `ProblemResponseWriter`; framework problems are
   completed by `ProblemDetailsCustomizer`. Never write problem JSON or construct `ProblemDetails` by hand
   (`PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi`).
3. **Redaction.** Server-category text is replaced outside Development on HTTP, SignalR and gRPC (including rebuilt
   foreign `RpcException`s); the `errorCode` is kept. No request/environment = production.
4. **Exception handling is the fallback** (`SharedKernelExceptionHandler`): a service's `IExceptionHandler` runs first;
   aborted request → 499 no body; `BadHttpRequestException` → 400 `validation.invalid_format` (no .NET type names);
   `ValidationException` identical to a returned error; `SharedKernelException` by type; timeout/non-abort
   cancellation → 504; else 500 `unexpected.default` (exception detail only in Development or with
   `IncludeExceptionDetails`, which logs 14012). Each handled exception is logged once, by it.
5. **Framework 400s are ours:** `ThrowOnBadRequest = true` everywhere; MVC's invalid-model factory is replaced only
   when it is MVC's own; `AllowInputFormatterExceptionMessages = false`.
6. **Field errors** are keyed by `ErrorArgumentNames.PropertyPath`, else by code; gRPC mirrors them as `BadRequest`
   violations capped at 50 and 3 KB (`grpc.more_field_violations`); a status always fits an 8 KB trailer.
7. **Middleware order is load-bearing:** HSTS/security headers precede the exception handler (which clears headers;
   `SecurityHeadersMiddleware` re-applies HSTS at `OnStarting`); rate limiting sits after authentication (partition by
   caller) and before authorization (refused traffic is counted); header requirements sit after authorization (an
   anonymous caller is told to authenticate, not which header it forgot). `UseSharedKernelRequestContext()` runs
   before all of it so error responses carry the correlation id.
8. **Permissions go on the use case.** `05.Application`'s `[RequirePermission]` applies on every path;
   `[RequireEndpointPermission]` is only for what sends no command (hubs, gRPC methods, endpoints without `ISender`,
   `MapSharedKernelOpenApi()`). Authentication strength (fresh/method) stays here. Both layers answer
   `unauthorized.default` (401) and `forbidden.insufficient_permission` (403) — keep them identical. Never give an edge
   attribute a name a use-case attribute already has (CS0104 when both namespaces are imported).
9. **Authorization fails closed.** The four attributes derive from `AuthorizeAttribute`; the requirement is encoded in
   the policy name `SharedKernel:{kind}:{values}` (`permission`, `role`, `fresh`, `amr`, `amr-max-age`) and cannot be
   replaced (setters throw). Every platform policy requires an authenticated user (anonymous → 401 first). The caller
   is resolved with `UserContextResolver`, never raw claims; an unmapped principal → 403 + log 14501. Max-age checks
   use `IUserContext.GetAuthenticationMethodTime`: unknown or more than `UserContext.MaxFutureAuthTime` ahead never
   passes. Step-up (RFC 9470 challenge, scheme `Bearer` or `DPoP` only) applies only when **every** unmet requirement
   is a freshness/method one. 403 messages never name the permission or role. `[AllowAnonymous]` switches off every
   requirement (ASP.NET Core semantics).
10. **Decorate, never replace.** `AddSharedKernelAuthorization()` (internal, idempotent) decorates the
    `IAuthorizationPolicyProvider` and `IAuthorizationMiddlewareResultHandler` registered before it
    (`ServiceDecoration.Decorate`); `SharedKernelAuthorizationStartupCheck` stops the host when a later registration
    displaced either — keep that check whenever the decoration changes. Refusal bodies go through the internal
    `IAuthorizationRefusalWriter` seam (WebApi's `AddSharedKernelWebApiAuthorization()`); a gRPC-only host answers with
    status and headers only.
11. **Every authentication scheme needs an `IUserContextMapper`** — the startup check warns per scheme (14502).
12. **Declared headers are validated before the endpoint runs.** Endpoint metadata is the single source; a parameter
    declared not-null is required, nullable is accepted, unreadable nullability is required. Required wins; an accepted
    header that is sent is validated like a required one and is never read as missing. `IdempotencyKey`/`IfMatch<T>`
    are `sealed record` classes (minimal APIs read no metadata from `Nullable<T>`). `BindAsync` throws 400 only when
    the middleware is absent.
13. **`If-Match`:** missing → 428 if required; `*` → 428 if required, 400 if accepted; malformed or several → 400;
    weak → 412; a strong tag that does not parse as `TVersion` → 412 before binding.
14. **The 412 rule:** a `Conflict` whose code is in `Problems:PreconditionFailedErrorCodes`, in a request with a
    non-blank `If-Match`/`If-None-Match`, is 412 — decided only in `ErrorPresentation.GetStatusCode`;
    `ErrorHttpResult.StatusCode` stays the type's status. The defaults are literals (14 may not reference 06/08),
    pinned by `PresentationPreconditionCodesTests`. `OkWithETag<T>` answers 304 only for `GET`/`HEAD`.
15. **Paging input** is refused with 400 before the handler using `04.Contracts`' `PaginationErrorCodes` and bounds
    (`PageRequest.MaxPageSize`, `CursorPageRequest.MaxLimit`, `PageCursor.MaxLength`) — never local constants. One
    reader (`PagingQuery`) serves middleware, `BindAsync` and OpenApi. Minimal APIs only.
16. **One request context.** No package here resolves a correlation id or opens a `RequestContextScope` for HTTP or
    gRPC; readers go through Core's `RequestFacts.GetCorrelationId`. gRPC calls run through the HTTP pipeline (no
    interceptor for context). SignalR's `RequestContextHubFilter` only **reopens** the connection's captured context
    around every connect, invocation and disconnect.
17. **Security headers** never overwrite an endpoint-set header; HSTS never in Development, over HTTP or to
    `localhost`. **CORS:** no policy without origins; startup refuses credentials with no/wildcard origins, the `null`
    origin, and (outside Development) credentials with `http://` origins (SK0032 flags wildcard + credentials in
    services); WebSocket requests from disallowed origins → 403.
18. **gRPC:** cancellation wins over every other mapping; a foreign `RpcException` keeps only its code; the error
    domain is never blank. No `ThrowIfFailure`/`GetValueOrThrow` extensions (they collide with `SharedKernel.Core`'s).
    Raw `RpcException`/`Status` outside `.Grpc`: SK0036.
19. **SignalR:** hub errors are `HubException("{code}: {message}")`; tenantless connections get `null` from
    `GetTenantId()`; `TenantGroup(Guid.Empty)`/`default` throws; one partitioned rate limiter, no per-bucket timer.
    Filter order: `RequestContextHubFilter` → `HubExceptionMappingFilter` → `HubInvocationRateLimitFilter`; gRPC's
    `GrpcExceptionInterceptor` is registered first (outermost). Consumers call the setup before adding their own.
20. **Setup is idempotent** through markers (`WebApiPipelineState`, `OpenApiSetupState`, `SignalRServicesMarker`,
    `GrpcServicesMarker`, GraphQL's registration marker); `configure` is applied on every call. Without
    `UseSharedKernelWebApi()` the host logs 14011; without `AddSharedKernelWebApi()` it throws.
21. **Options:** every section-bound options type has a validator. List options are get-only and configuration
    **appends** — document collection defaults as "added to". Invalid WebApi settings surface at first read (Kestrel at
    `Build()`), `ValidateOnStart` at the latest.
22. **Chain, never overwrite, service hooks:** `CustomizeProblemDetails` (platform first), `SuppressDiagnosticsCallback`,
    a service's `OnRejected` and `InvalidModelStateResponseFactory` are kept.
23. **One public namespace per package** (`SharedKernel.Presentation.WebApi`, `.OpenApi`, `.SignalR`, `.Grpc`; Core's
    public types in `SharedKernel.Presentation.Authorization`, internals in `SharedKernel.Presentation`). Folders are
    file organization only. Never add a public sub-namespace, never a namespace named `Results`.
24. **Add-on plumbing is internal.** Core grants `InternalsVisibleTo` to WebApi, Grpc, SignalR, OpenApi; WebApi to
    OpenApi and SignalR only (never Grpc). Change an internal and its callers in the same commit; never grant IVT to a
    production package outside this domain. Core and WebApi also grant it to GraphQL, which references both.
25. **Generated `MapEndpoints()`** is emitted `internal` in `SharedKernel.Presentation.WebApi`, only into an assembly
    that declares a module, calling each module's `Map` in ordinal order of full names. SKEP001 (abstract), SKEP002
    (generic/nested in generic), SKEP003 (unreachable) are errors, SKEP004 (inherits `Map`) a warning. No reflection or
    runtime discovery. An in-repo project referencing WebApi by `ProjectReference` must add the generator itself:
    `<ProjectReference Include="…Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.
26. **OpenApi** adds and never replaces; registers the entry assembly's XML comments before `AddOpenApi()`, adds
    platform transformers after Asp.Versioning's, and reads MVC metadata from the endpoint (`EndpointMetadataLookup`).
    It sees only endpoint-level authorization metadata. The OpenAPI stack stays in the add-on
    (`PresentationLayeringRules.NoOpenApiStackDependencyOutsideOpenApiAddOn`).
27. **No reflection invocation** (`HubMethodResult` uses cached compiled accessors). Domain constants: headers
    `PresentationHeaderNames`, problem members `ProblemDetailsExtensionNames`, codes `PresentationErrorCodes`,
    `GrpcErrorCodes`.
28. **Never log principal data or secrets:** refusals log endpoint and code (14500); idempotency keys are never logged
    (14003).
29. **GraphQL:** `AllowIntrospection` gates introspection both ways (HotChocolate disables it outside Development by
    default). Filter/sort types derive from `FilterBase<T>`/`SortBase<T>`, never HotChocolate's `FilterInputType<T>`/
    `SortInputType<T>` directly (`CommunicationLayeringRules.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL`).
30. **Failed spans are recorded once, at the edge, through one Core helper.** The HTTP exception handler, a returned
    error's `ErrorHttpResult` and the gRPC interceptor record on the ambient server span (`Activity.Current`; this
    domain starts no span and owns no `ActivitySource`) through `EdgeFailureRecording`, which alone decides and uses the
    shared `ActivityFailureExtensions` recorder: never hand-written `SetStatus`/`AddException`/`error.*` anywhere else.
    Server category (`ErrorPresentation.IsServerError`; gRPC `Unknown`/`Internal`/`DataLoss`/`Unavailable`/
    `DeadlineExceeded`): status `Error`, `error.type`, `error.code`; an exception adds one exception event and its
    status description is the type name, never the message (an unclassified exception is tagged with the code it is
    answered with); the timeout branch, a returned error and a foreign `RpcException` record the failure without an
    event and without the foreign detail. Client category: only the `error.code` tag, status untouched
    (OpenTelemetry server-span convention; `EdgeFailureRecording.FailsSpan` is the one line that flips it). Client
    abort, a cancellation, `Cancelled` and 499: nothing. The middleware's own log and diagnostic events stay
    suppressed (rule 4); an exception a service's own `IExceptionHandler` handles first is that service's to record.
31. **Every client-facing error carries trace id and correlation id on all four protocols**, the trace id from Core's
    `RequestFacts.GetTraceId` only. SignalR appends the trailer ` [traceId=…; correlationId=…]` to `{code}: {message}`
    (the last bracketed group; ids without white space, `;` or `]`; read back by `HubErrorMessage.TryParse`, whose
    message excludes it). **GraphQL** reaches ProblemDetails parity through `SharedKernelErrorFilter`: the same
    classification as rule 4 (`ExceptionClassification`, shared with the handler), status from Core's map, `type`/`title`
    the framework's (`ProblemDefaults`), `detail` and the top-level `message` the client message, `errorCode`, `traceId`,
    `correlationId`, field `errors`/`errorCodes` (`FieldErrorGroups`), an `exception` member only in Development and
    only for an unclassified exception; an `IError` without an exception is a client error (400,
    `graphql.request_invalid`, HotChocolate's `code` kept). `RequestErrorMiddleware` runs the same filters over the
    errors of a request HotChocolate rejects before executing it, which it does not filter itself. Logged once per error
    under 14400-14499. A gRPC authorization refusal, answered by the HTTP pipeline, carries the `X-Correlation-Id`
    header and no `ErrorInfo`.
32. **Logging shape.** Each package owns one 100-wide sub-block of 14000-14999 (WebApi 0, SignalR 1, Grpc 2, OpenApi 3,
    GraphQL 4, Core 5), one internal `{Package}LoggingEventIds` (const arithmetic from `LoggingEventIdRanges`) and one
    internal `{Package}Log` under `Diagnostics/`; templates are one sentence ending in a period with every `…Name` value
    in its own single quotes (remedy text lives in the README, not the template), the exception is the last parameter of
    every statement on an exception path, nothing on the root L5 list is logged, loggers are required constructor
    dependencies and categories are `ILogger<KernelType>` (a static class borrows a kernel type:
    `IdempotencyKeyGuard` is a sealed class with a private constructor for that reason, OpenApi uses
    `OpenApiSetupState`). A statement at Error or above always has an exception: a returned failure is Warning
    (SignalR 14108), a misconfiguration is Warning (14004). The three edges log an unclassified exception with the same
    `ServerError` shape and the code `unexpected.default`. `EdgeErrors` (Core) holds the generic errors once;
    `GrpcStatusCodeMap` keeps its inverse (`ServerCategory`) beside it so the redaction category and the status cannot
    drift from `IsServerError` (a consistency test runs over every `ErrorType` and `StatusCode`).
33. **Protocol-mirror codes are exempt from the domain-prefix shape.** `grpc.{status}` (`GrpcErrorCodes`) and
    `http.{status}` (`PresentationErrorCodes.ForStatus`) mirror the protocol's own names, which `11.Communication` reads
    back by hand; the error-code-format assertion tolerates the `grpc` and `http` prefixes and they never change.
    `unexpected.default` is the one code an unclassified exception gets on all four protocols.
34. **The edge-mapper conformance matrix.** `SharedKernel.Presentation.EdgeCases` (Testing tier,
    never packed) lists every `SharedKernelException` another domain throws or returns, built from the owners' public
    constants; the WebApi, gRPC, SignalR and GraphQL suites assert status or code, ids and the redaction rule for each,
    thrown and returned, in Development and Production. A new `SharedKernelException` elsewhere gets a row here; a row
    whose owner type is missing is marked blocked, never faked.

## Decisions

| Decision | Why |
| --- | --- |
| Native authorization policies behind attributes, not endpoint filters | Policies fail closed, answer anonymous callers 401, and reach SignalR hub methods |
| `Unavailable`/`Timeout` → 503/504 (gRPC `Unavailable`/`DeadlineExceeded`) | Clients must tell "retry later" from a defect |
| OpenAPI in a separate add-on | Keeps the core free of third-party packages; Asp.Versioning supplies per-version documents |
| Presentation.Core holds authorization and the client-message rule | gRPC shares them with HTTP and must not reference WebApi; `GetStatusCode` stays in WebApi because it reads WebApi's precondition codes |
| `GrpcStatusCodeMap` lives in Grpc | A WebApi host takes no `Grpc.Core.Api` |
| 412 from the error; accepted headers validated like required ones | RFC 9110 answers 412 for any conditional write; an unvalidated accepted header would silently disable a precondition or idempotency |
| Typed results for MVC; no `ToActionResult` | Typed results work in controllers; one family to maintain |
| Foreign `RpcException`s rebuilt without trailers | Another service's `ErrorInfo`, field paths and trace ids must not leak |
| Endpoint modules found by a source generator | No reflection or scanning; one package reference |
| No upload validation, payload-limit or version-lifecycle middleware, no SignalR backplane package | Presigned uploads (08.Storage) replace uploads; the rest duplicates the framework |
| .NET 10 `AddValidation()` not adopted | DataAnnotations only, experimental parts; validation lives in the application pipeline |
| The automatic 412 carries no current `ETag` | Clients re-read, the safer choice |
| gRPC authorization refusals carry no rich status | The HTTP pipeline answers them before the service runs |
| A client-category failure tags `error.code` but leaves the server span's status Unset | OpenTelemetry's HTTP and gRPC server conventions: 4xx and gRPC client codes are not span errors; marking every validation failure as failed would flood error dashboards. One line (`EdgeFailureRecording.FailsSpan`) flips it |
| GraphQL references Core and WebApi (internals visible to it) | It must take the status table, client-message rule, field-error grouping, exception classification and problem defaults from one place (rule 1), and `new ProblemDetails` stays in WebApi (rule 2); copying them would let GraphQL drift |
| SignalR ids travel as a trailer in the `HubException` message | A hub error is one string; a result envelope would break the `Result` hub contract and exception `Data` is never sent |
| GraphQL has no `IncludeExceptionDetails` override | Development only; one less setting that can leak stack traces in production |
| `SharedKernelErrorFilter` takes a `Func<HttpContext?>`, not an `IHttpContextAccessor` | SK0031 flags the accessor in a constructor; the factory in `AddSharedKernelGraphQL()` closes over the accessor and the filter stays testable without one |
| The hub filter records on the framework's hub-invocation span (source `Microsoft.AspNetCore.SignalR.Server`, kind Server, current inside `IHubFilter`) | Verified on .NET 10; the framework later marks the span Error and overwrites `error.type` with `HubException` itself, whatever the category, so our contribution there is `error.code` and the exception event |
| The matrix catalogue is a Testing-tier, never-packed project and the negative-control defect is linked source | One place for the cases four suites assert; the defect (an exception exposing an `Error` without deriving `SharedKernelException`) is what SK0043 reports in production code, so it is compiled into the tests only |
| The middleware's `SuppressDiagnosticsCallback` is kept, and loses nothing | Verified on .NET 10: it silences the middleware's log and diagnostic events, not the `aspnetcore.diagnostics.exceptions` counter, so the counter, the 5xx counts and the server span still carry the signal. `OpenTelemetry.Instrumentation.AspNetCore` 1.15.2 with `RecordException = true` records only exceptions that leave the pipeline, so the handler's one event stays the only one |
| GraphQL errors HotChocolate raises for a request it rejects (unknown field, bad argument, validation) are shaped by a request middleware | HotChocolate 16 returns them without running the error filters; without the middleware they would reach a client without status, `errorCode` and ids. The middleware hands them to the schema's `IErrorHandler`, so a service's own filters see them too |
| A `GraphQLErrorCodes` constant class is public; the abort code `graphql.request_aborted` is logged only | Clients branch on `graphql.request_invalid`; a client that went away reads nothing |
| Presentation owns no `ActivitySource` or `Meter` | The framework's server spans and `http.server.request.duration` already carry status and `error.type`; the edges only enrich them. An error counter would be a new feature |
| Default `PreconditionFailedErrorCodes` stay literals, pinned against the owners' public constants (here and in `PresentationPreconditionCodesTests`) | Rule 14: this domain may not reference 06/08 |

## Logging

`LoggingEventIdRanges.Presentation` (14000–14999). Every id is pinned with its level by a `LoggerMessageEventIdTests`
in each package; retired ids are never reused and their absence is asserted. A new statement takes the next free id of
its package's sub-block and a row in that test.

| Sub-block | Package | In use |
| --- | --- | --- |
| 14000–14099 | WebApi | 14001 Error server error from an exception · 14003 Warning idempotency key refused · 14004 Warning CORS settings invalid · 14005 Warning rate limit rejected · 14007 Debug client error from an exception · 14008 Debug client closed the request (499) · 14011 Warning `UseSharedKernelWebApi()` never called · 14012 Warning exception details outside Development · 14013 Warning WebSocket origin refused. Retired: 14000, 14002, 14006, 14009, 14010 |
| 14100–14199 | SignalR | 14101 Warning invocation rate limited · 14103 Error hub server error, an unclassified exception included (`unexpected.default`) · 14104 Debug hub client error · 14106 Debug connection closed during an invocation · 14107 Error stream inside a `Result` · 14108 Warning server-category failure returned as a `Result`. Retired: 14100, 14102, 14105 |
| 14200–14299 | Grpc | 14202 Error server error, an unclassified exception included (`unexpected.default`) · 14203 Debug client error · 14204 Debug call cancelled. Retired: 14200, 14201 |
| 14300–14399 | OpenApi | 14300 Information documents not mapped (environment) · 14301 Warning documents exposed without authorization |
| 14400–14499 | GraphQL | 14400 Error server-category error · 14401 Debug client-category error (a request HotChocolate rejected, a client that went away) |
| 14500–14599 | Core | 14500 Warning authorization refused · 14501 Warning principal without mapper · 14502 Warning scheme without mapper (startup). They were 14002, 14009 and 14010 in the WebApi block |

## Cross-Domain Couplings

- **01.Core:** `Error`/`ErrorType`/`ErrorCodes` (incl. `ErrorCodes.Idempotency`, shared with 05's idempotency
  behavior)/`ErrorArgumentNames`, `WellKnownHeaders`, `ILocalizationCatalog`, `IClock`, `Error.ToException()`;
  **Execution:** `IRequestContextAccessor`, `RequestContextScope`, `TenantId`.
- **12.Security:** `IUserContext`, `IUserContextMapper`, `UserContextResolver`, `GetAuthenticationMethodTime`,
  `UserContext.MaxFutureAuthTime` — the only inputs the attributes read.
- **13.ServiceDefaults:** `UseSharedKernelRequestContext()` (composed first by every host); `AddSharedKernelRateLimiting()`
  leaves `OnRejected` null so our 429 body applies; `TenantResolutionMiddleware` goes in the `BeforeAuthorization` hook.
  It tags the server span with `correlation.id`/`tenant.id` before `UseExceptionHandler()` and keeps instrumentation
  `RecordException` off, so the handler's single exception event (rule 30) is the only one; it subscribes the framework
  SignalR sources, and no hub source is created here; inbound actor/tenant headers are never applied by HTTP or gRPC
  servers.
- **01.Core / 16.Testing:** the shared `ActivityFailureExtensions` recorder and `WellKnownTagKeys.ErrorCode` (rule 30),
  `LoggingEventIdRanges.PackageSubBlockWidth` (GraphQL's sub-block), and, for tests, the span/log capture and
  convention assertions (`ActivityRecorder`, `ShouldBeRecordedFailure`/`ShouldBeRecordedException`, `MetricRecorder`,
  `InMemoryLoggerFactory`); `ErrorCodes.Unexpected.Default` is `unexpected.default` (rule 4's 500
  code follows it).
- **05.Application:** no reference either way; endpoint modules send its commands; same 401/403 codes.
- **04.Contracts:** paging types and codes (WebApi and GraphQL only; never Grpc — `PresentationLayeringRules.GrpcNeverReferencesContracts`).
- **06.Persistence / 08.Storage:** `EntityVersion` is the usual `IfMatch<TVersion>`; their conflict codes are the
  default `PreconditionFailedErrorCodes`.
- **11.Communication.Rest** reads our problems back (`errorCode`, fallback `http.{status}`; 412 → Conflict, 429/503 →
  Unavailable, 504 → Timeout) — keep `PresentationErrorCodes.ForStatus` and its fallback identical.
- **00.Governance:** `PresentationLayeringRules` (also `NoInlineResultBranchBeforeHttpResultOutsideWebApi`), SK0032,
  SK0036, `PresentationPreconditionCodesTests`.
- **samples** (every service, including Shop) and `consumer-verify` use the one-call path and endpoint modules; a
  public API change updates them.

## Testing

- All presentation test projects are in the **Unit** lane (in-process `TestServer`/Kestrel, no Docker).
- Behaviour is tested through real in-process hosts built with the one-call setup (`WebApiTestHost`, `FullStackHost`);
  what `TestServer` does not enforce (body limits, `Server` header) runs on Kestrel (`StartKestrelAsync`); HSTS needs an
  `https` non-`localhost` base address.
- Every HTTP error assertion goes through `ShouldBeProblemAsync` (media type, member set, `X-Correlation-Id` vs
  `correlationId`).
- SignalR tests use a real `HubConnection`, gRPC a real `Grpc.Net.Client` channel, OpenAPI tests generate documents,
  GraphQL tests post over real HTTP (`MapGraphQL`) with the request context composed first.
- Span assertions use the framework's server span (`HttpRequestIn`, source `Microsoft.AspNetCore`) of a trace the test owns
  (send a `traceparent`, read `ActivityRecorder.ForTrace`): the source is static and shared by parallel tests. Assert the
  failure shapes with `ShouldBeRecordedFailure`/`ShouldBeRecordedException`, never by string search.
- Time through `FakeClock`; never `Task.Delay`. Log assertions check EventId and level through `16.Testing`'s
  in-memory logger, never rendered text.
- A security-relevant test must be able to fail: mutate the condition and confirm the assertion catches it.
- The edge-mapper matrix (rule 34) runs in every protocol suite over the real host: HTTP endpoints `/edge/throw|result/{name}`, the gRPC `Fail` method (`edge:`/`edge-result:`), the `Edge`/`EdgeResult` hub methods, the `edge(name:)` GraphQL field; plus two negative controls (a plain exception, an `Error`-bearing one) per protocol.
- Log conventions are asserted on real paths with `ShouldFollowLogConvention`, `ShouldOnlyLogEventIdsInSubBlock` and `ShouldHaveCategory`; `ShouldNotLeak` scans an exception's message too, so assert the line, not the exception, for a canary.
- Hub-invocation spans are asserted with a hub of the test class's own (the source is static and shared), recorded through `ActivityRecorder`.
- Fakes: `SharedKernel.Presentation.Testing` — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- An endpoint protected only by its command's `[RequirePermission]` shows no security requirement in OpenAPI; add
  `.RequireAuthorization()` or a fallback policy.
- `Paging`/`CursorPaging` and `IfMatch<T>` tag parsing are minimal-API features; MVC actions bind and parse themselves.
- GraphQL depends on HotChocolate, is not AOT-safe, and has no PublicAPI tracking. HotChocolate's own `type` extension
  of a validation error (the type name) is replaced by the RFC 9110 type URI; its `code` is kept.
- A hub error that SignalR writes itself (an unknown method, its authorization refusal, a stream that fails after it
  started) carries no code and no ids; a `HubException` a hub throws itself has no trailer.
- A service-owned `IExceptionHandler` that handles an exception first is not recorded on the span by the kernel; the
  service records it. A hub invocation span is marked Error by SignalR itself for every `HubException`, client category
  included, and its `error.type` is then `HubException`; the kernel's `error.code` and exception event stay.
