# 14.Presentation — Inbound API Boundary

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read each package's own `README.md`; the folder overview is [`README.md`](README.md) and every setting
> is in [`CONFIGURATION.md`](CONFIGURATION.md).
> This brain holds what the source does not make obvious: rules, traps, invariants, couplings and decisions.
> The design and review records of the 2026-09-23/24 gold-standard pass are in [`docs/p562/`](docs/p562/); the P-563
> pass that made this domain the thin edge in front of 05.Application's use cases is recorded in
> [`05.Application/docs/p563/`](../05.Application/docs/p563/). The brain before P-562 (WO-031 through WO-078) lives in
> [`CLAUDE.history.md`](CLAUDE.history.md) and describes types that no longer exist.

## What This Domain Is

The inbound API boundary. It turns outcomes (`Result`, `Error`, exceptions) into what a caller receives: an HTTP
response or RFC 9457 problem, a SignalR `HubException`, a gRPC `google.rpc.Status`. One error contract serves all three
protocols. It also owns the concerns of the boundary: authorization against `IUserContext`, security headers, CORS,
request limits, `Idempotency-Key`, `ETag` and `If-Match`. The request's context — its correlation id, the refusal of
inbound baggage and the `RequestContextScope` every layer reads the caller from — is **not** this domain's: it belongs
to `13.ServiceDefaults/SharedKernel.ServiceDefaults.Security`'s `UseSharedKernelRequestContext()`, which a host runs
first, before `UseSharedKernelWebApi()` (P-579). This domain reads the id from that scope.

It converts outcomes; it never produces them. Use cases are `05.Application` commands and queries that a service's
endpoint modules send through the kernel's `ISender`; this domain never references `05.Application` or MediatR (P-563
P1), so any `Result`-returning code maps the same way. Every package is **Host tier** (WO-086; `eng/SharedKernelTiers.targets`
enforces it): they reference Foundation, Model and Abstractions packages — `SharedKernel.Primitives`, `.Core`,
`.Configuration`, `.Localization`, `.Execution`, `SharedKernel.Security.Abstractions` and, for the paging parameters
only, `SharedKernel.Contracts` (WebApi; `.Grpc` must never) — and each other. Never an Adapter. Outbound calls are
`11.Communication`'s.

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Presentation.Core` (shared, P-570/P-579) | What every protocol must agree on, so gRPC needs no WebApi: the four authorization attributes and `AuthorizationConventionExtensions` (public, namespace `SharedKernel.Presentation.Authorization`); internal: the policy provider, requirements, requirement handler, result handler (status and challenge only; the body comes through `IAuthorizationRefusalWriter`), startup check and `AddSharedKernelAuthorization()`, `ErrorTypeStatusCodeMap`, the message half of `ErrorPresentation`, the shared `RequestFacts` (endpoint, gRPC detection, UI culture, environment, correlation id) and `ServiceDecoration` | Primitives, Execution, Localization, Security.Abstractions; ASP.NET Core shared framework. No third-party packages |
| `SharedKernel.Presentation.WebApi` (core) | One-call setup and pipeline; endpoint modules (`IEndpointModule`, the generated `MapEndpoints()`); the error contract (`ErrorPresentation.GetStatusCode` plus Core's message rule, problem details for every source, and the problem body of an authorization refusal); typed results for `Result`; security headers; CORS and the WebSocket origin check; request limits; required and accepted `Idempotency-Key`/`If-Match`; `Paging`/`CursorPaging`; `ETag`/304; the 429 body; `HttpContext.GetCorrelationId()` over the request's scope | Presentation.Core, Primitives, `.Core`, `.Configuration`, `SharedKernel.Contracts` (paging); ASP.NET Core shared framework. **No third-party packages** |
| `SharedKernel.Presentation.WebApi.Generators` (not a package, Tooling tier) | The endpoint-module source generator, `netstandard2.0`, diagnostics SKEP001–SKEP004; packed inside WebApi under `analyzers/dotnet/cs` | `Microsoft.CodeAnalysis.CSharp` (private). WebApi's `ReferenceOutputAssembly="false"` edge to it is exempt from the tier check |
| `SharedKernel.Presentation.OpenApi` (add-on) | API versioning, one OpenAPI 3.1 document per version, Scalar, sunset/deprecation policies; documents what the core enforces, never changes a response | WebApi, Presentation.Core; `Asp.Versioning.Http` 10.2.3, `Asp.Versioning.Mvc.ApiExplorer` 10.2.1, `Asp.Versioning.OpenApi` 10.2.3, `Microsoft.AspNetCore.OpenApi` 10.0.11, `Scalar.AspNetCore` 2.17.8 |
| `SharedKernel.Presentation.SignalR` (add-on) | Hub error mapping (`{code}: {message}`), `Result` hub methods, the invocation rate limit, `RequestContextHubFilter` (the connection's `RequestContextScope` around every connect, invocation and disconnect), `GetTenantId()`/`GetCorrelationId()`, group naming | WebApi, Presentation.Core, Primitives, Core, Configuration, Execution. No third-party packages. No Redis backplane (D15; `SharedKernel.Presentation.SignalR.Redis` deleted by P-579) |
| `SharedKernel.Presentation.Grpc` | The exception interceptor building the rich status, `GrpcStatusCodeMap`, `GrpcErrorCodes` | Presentation.Core, Core, Configuration — **never WebApi, never Contracts**; `Grpc.AspNetCore` 2.80.0, `Grpc.StatusProto` 2.80.0, `Google.Api.CommonProtos` 2.17.0 (the first with `FieldViolation.reason`) |
| `SharedKernel.Presentation.GraphQL` (moved from `11.Communication` by WO-086 P-570) | HotChocolate server conventions: snake_case filtering, `FilterBase<T>`/`SortBase<T>`, `PagedResponseType<T>`, the ProblemDetails-shaped error filter, `AddSharedKernelGraphQL()` | Primitives, Contracts; HotChocolate 16.1.4 (not AOT-safe) |

Every package tracks its public API (`PublicAPI.Shipped.txt` empty, `PublicAPI.Unshipped.txt` populated;
RS0016/RS0017/RS0022/RS0024/RS0025/RS0036/RS0037 and CS1591 are errors). None is published to the feed yet. Versions
come from the repo-wide MinVer tag; never add a `<Version>`.

## The model

### One error contract

- **`ErrorPresentation` decides for every protocol.** It is internal (P-563 P3) and split by P-579: the message half
  (`GetClientMessage`, `IsServerError`, `GetPresentationMessage`) and `ErrorTypeStatusCodeMap` live in
  `SharedKernel.Presentation.Core` (namespace `SharedKernel.Presentation`), so gRPC and SignalR use them without WebApi;
  WebApi's own `ErrorPresentation` adds `GetStatusCode` and forwards the rest. `PresentationErrorCodes.ForStatus` stays
  WebApi's. `GetStatusCode(error, httpContext)` is `ErrorTypeStatusCodeMap`
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

The canonical host pipeline (P-579):

```csharp
app.UseSharedKernelRequestContext();   // 13.ServiceDefaults.Security: baggage refused, correlation id, the request's scope
app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));   // tenant optional
app.MapEndpoints();
```

The request context runs first so its scope wraps the exception handler: every response, error responses included,
carries the id, and every log line of the request has it. `UseSharedKernelWebApi(configure)` order: `AtStart` hooks →
HSTS (not Development) → security headers → `UseExceptionHandler()` → status code pages (not gRPC) →
`UseRouting()` → CORS + WebSocket origin check (only with origins) → `BeforeAuthentication` hooks →
`UseAuthentication()` (only when `IAuthenticationSchemeProvider` is registered) → `BeforeAuthorization` hooks →
`UseRateLimiter()` (only when an `IConfigureOptions<RateLimiterOptions>` exists) → `UseAuthorization()` →
`HeaderRequirementsMiddleware` (headers, then paging). The reasons are load-bearing:

- HSTS and the security headers sit before the exception handler, which clears headers; `SecurityHeadersMiddleware`
  remembers the HSTS value and writes it again at `OnStarting` (R11). Moving `UseHsts()` alone does not work.
- Rate limiting sits before authorization so refused traffic is counted (R1), after authentication so policies can
  partition by caller.
- Header requirements sit after authorization so an anonymous caller is told to authenticate, never which header it
  forgot (R6).
- A second call is a no-op (`app.Properties` key); `WebApiPipelineState` feeds the 14011 warning.

### Authorization

- **The split with 05.Application (P-563 A2).** Permissions go on the use case: `05.Application`'s
  `[RequirePermission]` on a command or query, enforced by its pipeline on every path. This package's
  `[RequireEndpointPermission]` (convention `.RequireEndpointPermission(…)`) is only for what sends no command: hubs,
  gRPC services and methods, endpoints that do not call `ISender`, and `MapSharedKernelOpenApi()`. Authentication
  strength (`RequireFreshAuthentication`, `RequireAuthenticationMethod`) stays here, because only the HTTP request knows
  it. READMEs and samples never repeat a command's permission on its endpoint. Both layers answer `unauthorized.default`
  (401) and `forbidden.insufficient_permission` (403); keep them identical.
- The endpoint attribute was named `RequirePermissionAttribute` until the owner renamed it (commit `52975eed`, after
  P-563's streams): the same name in both layers made a file importing both namespaces fail with CS0104 and blurred
  which layer a permission belongs to. Never give an edge attribute a name a use-case attribute already has.
- Endpoint-level authorization metadata is all OpenAPI can see: an endpoint protected only by its command documents no
  security requirement. The OpenApi README tells services to add `.RequireAuthorization()` or a fallback policy.
- **Where it lives (P-579).** The attributes, the conventions and the policy machinery are
  `SharedKernel.Presentation.Core`'s, in `SharedKernel.Presentation.Authorization` — shared by WebApi, SignalR and gRPC,
  so a gRPC host takes no WebApi. The public types are the four attributes and `AuthorizationConventionExtensions`;
  everything else, `AddSharedKernelAuthorization()` included, is internal and visible to the four packages.
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
  start, never for gRPC, and logs 14002 for every refusal. Core sets the status and headers; the body goes through the
  internal `IAuthorizationRefusalWriter` seam, which WebApi's `AddSharedKernelWebApiAuthorization()` registers
  (`AuthorizationRefusalProblemWriter`, the platform problem) — called by `AddSharedKernelWebApi()` and
  `AddSharedKernelSignalR()`. A gRPC-only host registers none and answers with status and headers only, as before.

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

### Paging parameters (P-563 P4)

- `Paging` (`page`, `pageSize` → `PageRequest`) and `CursorPaging` (`cursor`, `limit` → `CursorPageRequest`) are
  `sealed record` classes implementing `IEndpointParameterMetadataProvider`: they add the internal `PagingMetadata`,
  which `HeaderRequirementsMiddleware` enforces after the header checks and the OpenApi add-on documents. One reader,
  `PagingQuery`, serves the middleware, `BindAsync` and OpenApi, so the three never disagree.
- Invalid input is a 400 validation problem keyed by the query parameter: `04.Contracts`' `PaginationErrorCodes` for
  a value out of range or a bad cursor, `validation.invalid_format` for a value that is not one whole number or a
  parameter sent twice. The bounds are `04.Contracts`' (`PageRequest.MaxPageSize`, `CursorPageRequest.MaxLimit`,
  `PageCursor.MaxLength`), never local constants.
- `BindAsync` throws a 400 `BadHttpRequestException` only when the middleware is absent. Minimal APIs only; MVC binds
  the values itself.

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

- **Not this domain's since P-579.** `SharedKernel.ServiceDefaults.Security`'s `UseSharedKernelRequestContext()`
  resolves the correlation id with the one platform rule (`CorrelationIds.IsValid`: ≤ 128 characters of
  `[A-Za-z0-9-_:.]`, else `CorrelationIds.New()`, a "D" GUID — never the trace id), sets it as baggage
  `WellKnownBaggageKeys.CorrelationId`, echoes it at `OnStarting` and opens the request's `RequestContextScope`
  (EventIds 13006/13007). WebApi's `CorrelationIdMiddleware`, `WebApiCorrelationIdOptions`
  (`SharedKernel:Presentation:WebApi:CorrelationId`) and its 14000/14006 logs are deleted; the ids are retired.
- **Readers.** `HttpContext.GetCorrelationId()` (WebApi, public), the problem `correlationId` member, the gRPC
  `ErrorInfo` metadata and SignalR's `HubCallerContext.GetCorrelationId()` all read the scope through Core's
  `RequestFacts.GetCorrelationId` (the registered `IRequestContextAccessor`, else `RequestContextScope.Current`).
- **Inbound baggage** is refused twice (R3), by `SharedKernel.ServiceDefaults.Security` since P-579:
  `AddSharedKernelRequestContext()` decorates the DI `DistributedContextPropagator` hosting reads before any middleware
  (so hosting's first log record carries no forged item), and `UseSharedKernelRequestContext()` removes what still
  reached the request `Activity` **before** it adds the correlation id. `RequestContextOptions.TrustInboundBaggage`
  (code only, `AddSharedKernelRequestContext(o => …)`) replaces `SharedKernelWebApiOptions.TrustInboundBaggage`.
  OpenTelemetry's own `Baggage.Current` is 13.ServiceDefaults' telemetry concern (X2).
- **gRPC and SignalR.** gRPC calls run through the HTTP pipeline, so the request context middleware opens their scope;
  no gRPC interceptor exists (D15), and a service method reads the caller from `IRequestContext`. A SignalR hub
  invocation does not run in the connect request's flow: `RequestContextHubFilter` (internal, registered first by
  `AddSharedKernelSignalR()`) captures the connect request's context and reopens it around every connect, invocation
  and disconnect. The request context middleware fixes the caller when its request ends, so a long-polling
  connection's context stays readable after that request is gone.

## Composition rules (the traps)

- **`UseSharedKernelRequestContext()` first, then `UseSharedKernelWebApi()`.** Anything a service must run inside the
  WebApi pipeline goes in a hook; `AtStart` middleware runs outside the exception handler. Without
  `UseSharedKernelWebApi()` the host still works but logs 14011; without `AddSharedKernelWebApi()` it throws
  `InvalidOperationException`. Without the request context there is no correlation id anywhere (the `correlationId`
  member is omitted) and nothing refuses inbound baggage — every test host and `consumer-verify` compose it.
- **Decorate, never replace.** `AddSharedKernelAuthorization()` decorates the `IAuthorizationPolicyProvider` and
  `IAuthorizationMiddlewareResultHandler` registered before it (`ServiceDecoration.Decorate`: last non-keyed
  registration, in place, same lifetime); `AllowsCachingPolicies` follows the inner provider.
  `SharedKernelAuthorizationStartupCheck.StartingAsync` throws when a later registration displaced either (a probe
  policy name must resolve to the platform requirement; the resolved handler must be ours). Keep that check whenever
  the decoration changes.
- **`AddSharedKernelAuthorization()` is internal to Presentation.Core, shared and idempotent** (marker service). gRPC's
  setup calls it directly; WebApi and SignalR call WebApi's `AddSharedKernelWebApiAuthorization()`, which adds the
  problem-body writer. Services never call either. It calls `AddAuthorization()` first so the framework defaults exist
  to be decorated, and `AddClock()`.
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
- **Global filters and interceptors nest by registration order.** SignalR: `RequestContextHubFilter` outermost (so the
  others log with the connection's correlation id), then `HubExceptionMappingFilter`, then
  `HubInvocationRateLimitFilter`. gRPC: `GrpcExceptionInterceptor` first, so it is outermost. Consumers must call the
  setup before adding their own.
- **The OpenApi add-on** registers `EntryAssemblyXmlComments`' transformer before `AddOpenApi()` (Asp.Versioning looks
  for the XML of the assembly that called it, which would be ours), adds the platform transformers through
  `IConfigureOptions<VersionedOpenApiOptions>` so they run after Asp.Versioning's, and reads MVC action metadata from
  the endpoint (`EndpointMetadataLookup`) because the API Explorer omits endpoint conventions. `MapSharedKernelOpenApi()`
  returns a `CompositeEndpointConventionBuilder` whose conventions the 14301 check reads; outside Development without
  `ExposeInProduction` it maps nothing and returns an empty one.
- **One public namespace per package (P-563 P3).** Every public type of WebApi is in `SharedKernel.Presentation.WebApi`,
  options and constants included; the same for OpenApi, SignalR and Grpc. Presentation.Core's public types are in
  `SharedKernel.Presentation.Authorization` (they are not WebApi-only, P-579), and its internal helpers in
  `SharedKernel.Presentation`, so WebApi, SignalR and gRPC code resolves them without a `using` and a WebApi type of the
  same name (`ErrorPresentation`, `RequestFacts`) wins inside WebApi's namespace. The folders (`Errors/`, `Http/`,
  `Idempotency/`, `Options/`, `Pagination/`) are file organization only: a file in them declares the root namespace or
  an internal-only one. Never add a public sub-namespace, and never create a namespace named `Results` (it shadows
  `Microsoft.AspNetCore.Http.Results`).
- **Add-on plumbing is internal, visible to the add-ons.** Presentation.Core grants `InternalsVisibleTo` to WebApi,
  Grpc, SignalR and OpenApi (and their test projects, and `SharedKernel.Security.Testing.Tests`, which drives the
  policies without a host); WebApi grants it to OpenApi and SignalR only (and their test projects) — never Grpc since
  P-579. The packages version in lockstep (one MinVer version), so an internal signature change is safe only because
  they always ship together: change the internal and its callers in the same commit, and never grant
  `InternalsVisibleTo` to a production package outside this domain.
- **The endpoint-module generator ships inside WebApi.** `SharedKernel.Presentation.WebApi.Generators` targets
  `netstandard2.0`, is not packable, and is packed by WebApi's `_PackEndpointModuleGenerator` target under
  `analyzers/dotnet/cs`, so `app.MapEndpoints()` comes with the WebApi package and nothing else. WebApi references it
  with `ReferenceOutputAssembly="false"` and does not run it on itself. **An in-repo project that references WebApi by
  `ProjectReference`** (the WebApi tests, `consumer-verify`) gets no analyzer from it and must add the generator itself
(the samples reference the packed package and get it from there):
  `<ProjectReference Include="…Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.
- **Generated code.** `MapEndpoints(this IEndpointRouteBuilder)` is emitted `internal`, in namespace
  `SharedKernel.Presentation.WebApi`, only into an assembly that declares a module, calling each module's static `Map`
  in ordinal order of full names, through the interface when `Map` is implemented explicitly. SKEP001 (abstract),
  SKEP002 (generic or nested in a generic type), SKEP003 (not reachable from the assembly) are errors; SKEP004 (inherits
  `Map` from another module) is a warning. No reflection, no runtime discovery: keep it that way.
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
  correlation ids log their length (13007, `SharedKernel.ServiceDefaults.Security`); idempotency keys are never logged
  (14003).
- **No caller-controlled text reaches clients or logs unvalidated.** Correlation ids are validated (by the request
  context middleware); the step-up challenge echoes only `Bearer` or `DPoP`; 403 messages never name permissions or
  roles.
- **Inbound baggage is not trusted** unless `RequestContextOptions.TrustInboundBaggage` (13.ServiceDefaults.Security).
- **One request context.** No package of this domain resolves a correlation id or opens a `RequestContextScope` for an
  HTTP or gRPC call; the SignalR hub filter only reopens the connection's.
- **Headers.** A declared header is validated before the endpoint runs; an accepted header is never read as missing;
  required wins.
- **Security headers** never overwrite an endpoint-set header; HSTS never in Development, never over HTTP, never to
  `localhost`.
- **CORS.** No policy without origins; startup validation refuses credentials with no or wildcard origins, the `null`
  origin, and outside Development credentials with `http://` origins (R24); with origins, WebSocket requests from
  disallowed origins get 403 (R25).
- **gRPC.** Cancellation wins over every other mapping (R33); a foreign `RpcException` keeps only its code (R30); a
  status always fits an 8 KB trailer limit (R31); the domain is never blank.
- **SignalR.** Hub errors are `HubException("{code}: {message}")`; `GetTenantId()` is the connection context's
  `TenantId?` — tenantless connections get `null`, and `HubGroupNaming.TenantGroup(Guid.Empty)` (or a `default`
  `TenantId`) throws; the rate limiter is one partitioned limiter with
  no per-bucket timer.
- **OpenAPI** documents only what the core enforces, adds and never replaces, and maps nothing outside Development
  unless `ExposeInProduction`.

## Logging (EventId 14000–14999)

`LoggingEventIdRanges.Presentation` is 14000. Each package owns a 100-wide sub-block. Every `[LoggerMessage]` has an
explicit id, pinned with its level by a reflection test in each package (`LoggerMessageEventIdTests`). Retired ids are
never reused; the tests assert their absence.

| Range | Package | Events in use |
| --- | --- | --- |
| 14000–14099 | WebApi (and Presentation.Core) | 14001 Error server error from an exception · 14002 Warning authorization refused (endpoint, code) · 14003 Warning idempotency key refused (endpoint, code) · 14004 Critical CORS settings invalid · 14005 Warning rate limit rejected · 14007 Debug client error from an exception · 14008 Debug client closed the request (499) · 14009 Warning principal without `IUserContextMapper` · 14010 Warning scheme without mapper (startup) · 14011 Warning `UseSharedKernelWebApi()` never called (startup) · 14012 Warning exception details outside Development (startup) · 14013 Warning WebSocket origin refused. **Since P-579, 14002, 14009 and 14010 are emitted by `SharedKernel.Presentation.Core`**, which took over the authorization machinery and kept the numbers (pinned by `SharedKernel.Presentation.Core.Tests`' `LoggerMessageEventIdTests`; WebApi's test asserts it no longer declares them). Retired: 14000 (correlation id assigned) and 14006 (correlation id rejected) — the deleted correlation-id middleware's; `SharedKernel.ServiceDefaults.Security` logs those events as 13006/13007 |
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
| .NET 10 `AddValidation()` not adopted | DataAnnotations only, experimental parts, no FluentValidation hook; validation lives in the application pipeline |
| `[AllowAnonymous]` switches off every requirement | ASP.NET Core semantics; not changed |
| The automatic 412 carries no current `ETag` | Clients re-read, the safer choice |
| gRPC authorization refusals carry no rich status | They are answered by the HTTP pipeline before the service runs; not changed |
| Endpoint modules discovered by a source generator shipped in WebApi (P-563 P2) | No reflection or runtime scanning, one reference; hand-written `MapXxxEndpoints()` extensions are no longer the documented path |
| One public namespace; add-on plumbing internal (P-563 P3) | The surface a service needs is small and in one `using`; `InternalsVisibleTo` is safe because the four packages version in lockstep |
| `Paging`/`CursorPaging` parameters validated by the header-requirements middleware (P-563 P4) | Invalid paging is refused before the handler, with the platform problem, and documented by OpenApi from the same metadata |
| WebApi stays free of MediatR and 05.Application (P-563 P1) | Any `Result`-returning code maps the same way; the command/query pattern is taught by the READMEs and samples, not enforced by a reference |
| P-579: `SharedKernel.Presentation.Core` holds the authorization (attributes public in `SharedKernel.Presentation.Authorization`, machinery internal), `ErrorTypeStatusCodeMap` and the client-message rule; WebApi keeps `GetStatusCode` and writes refusal bodies through an internal seam | WO-086's intent (P-570): gRPC shares these with HTTP, so it must not reference WebApi; main's P-562 design put them in WebApi. The status half stays in WebApi because it reads WebApi's `Problems:PreconditionFailedErrorCodes` |
| P-579: `GrpcStatusCodeMap` back in Grpc (P-570 had moved it to Core) | Main's placement: a WebApi host then takes no `Grpc.Core.Api`, which was P-570's accepted trade-off |
| P-579: the correlation id belongs to `SharedKernel.ServiceDefaults.Security`'s `UseSharedKernelRequestContext()`; WebApi's middleware and `CorrelationId` options deleted, `GetCorrelationId()` reads the request's scope | WO-086 P-566: one middleware owns the request's scope and its id, with one validation rule (`CorrelationIds.IsValid`) for every protocol; two resolvers would disagree (main's used the trace id and a configurable pattern) |
| P-579: inbound-baggage refusal moved with it (`RequestContextOptions.TrustInboundBaggage`) | The edge that owns the correlation id must remove caller baggage before it adds the id; in WebApi, a host without WebApi (gRPC-only, SignalR-only) had no refusal |
| P-579: `SharedKernel.Presentation.SignalR.Redis` dropped | Main removed the backplane as unused (D15); a package with nothing in it has no reason to exist |
| P-579: gRPC gets its scope from the HTTP pipeline, no interceptor | Main deleted the correlation/tenant/authorization interceptors (D15); with the request context middleware first, gRPC calls already run in their scope. Proven by `RequestContextTests` in `SharedKernel.Presentation.Grpc.Tests` |
| P-579: SignalR re-adds WO-086's hub filter (`RequestContextHubFilter`) | Hub invocations do not run in the connect request's flow, so without it hub code sees no caller; `GetTenantId()` returns `TenantId?` from that context, never an `ITenantProvider` (deleted by WO-086). `HubGroupNaming.TenantGroup(Guid)` kept (owner question), `TenantGroup(TenantId)` added |

## Cross-Domain Couplings

- **01.Core:** `Error`, `ErrorType` (with `Unavailable = 8`, `Timeout = 9`), `ErrorCodes`, `ErrorArgumentNames`,
  `WellKnownHeaders` (`CorrelationId`, `IdempotencyKey`), `WellKnownBaggageKeys.CorrelationId`,
  `LoggingEventIdRanges.Presentation`, `AddValidatedOptions`/`ISectionBoundOptions`, `ILocalizationCatalog`,
  `IClock`/`AddClock`. `Error.ToException()` (Core) is how gRPC and streaming hubs end a failed `Result`.
- **01.Core/SharedKernel.Execution:** `IRequestContext`, `IRequestContextAccessor`, `RequestContextScope`, `TenantId`,
  `ActorKind` — the correlation id readers (Core), SignalR's hub filter and `GetTenantId()`.
- **12.Security:** `IUserContext` (`ActorKind`, `TenantId?`), `IUserContextMapper`, `UserContextResolver`; X1's
  `GetAuthenticationMethodTime`, `amr_time` and `UserContext.MaxFutureAuthTime`. The attributes read only these.
  `ITenantProvider`/`IdentityKind` no longer exist (WO-086 P-565).
- **05.Application:** no reference in either direction. Endpoint modules send its commands and queries; its
  `[RequirePermission]` on a use case replaces the edge attribute for that endpoint, and `AuthorizationBehavior`
  answers with the same codes (401 `unauthorized.default`, 403 `forbidden.insufficient_permission`).
  `IIdempotentRequest` consumes the key; both sides take the idempotency codes from `01.Core`'s
  `ErrorCodes.Idempotency` (`PresentationErrorCodes.IdempotencyKeyRequired`/`KeyInvalid` are those constants), so no
  drift test exists any more.
- **04.Contracts:** `PageRequest`, `CursorPageRequest`, `PageCursor.MaxLength`, `PaginationErrorCodes` for the paging
  parameters (WebApi only; `GrpcNeverReferencesContracts` still holds for `.Grpc`).
- **06.Persistence / 08.Storage:** `EntityVersion` (`IParsable`, opaque sealed token since X4) is the usual `TVersion`;
  the default `PreconditionFailedErrorCodes` are their codes, pinned by `PresentationPreconditionCodesTests`. Presigned
  uploads (08) replace upload validation.
- **11.Communication.Rest** reads the problem back: `errorCode` (fallback `http.{status}`, never `title`), `errors`
  only for 400/422, 412 → Conflict, 413/415/428 → Validation, 429/503 → Unavailable, 504 → Timeout (R38).
  `PresentationErrorCodes.ForStatus` and its fallback must stay identical.
- **13.ServiceDefaults:** `SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()` /
  `UseSharedKernelRequestContext()` own the correlation id, the inbound-baggage refusal and the request's scope that
  this domain reads (P-579); every host composes them first. `AddSharedKernelRateLimiting()` leaves `OnRejected` null
  so our 429 body applies; the base's `BaggageLogRecordProcessor` allow-list and `RequestBaggageRefusingPropagator`
  complete the baggage refusal (X2). `TenantResolutionMiddleware` goes in the `BeforeAuthorization` hook.
- **16.Testing:** `SharedKernel.Presentation.Testing`'s gRPC `TestServerCallContext` (adds
  `WellKnownHeaders.CorrelationId`; no pipeline, so a test that needs the ambient caller opens a `RequestContextScope`
  itself); `SharedKernel.Security.Testing`'s `FakeUserContext.WithAuthenticationMethodTime` for step-up tests, proven
  against Core's policies by `AuthenticationMethodMaxAgeTests`.
- **00.Governance:** `PresentationLayeringRules` (`NoDirectProblemDetailsConstructionOutsideWebApi`,
  `NoInlineResultBranchBeforeHttpResultOutsideWebApi`, `NoOpenApiStackDependencyOutsideOpenApiAddOn`,
  `GrpcNeverReferencesContracts`); SK0022 (magic strings), SK0032 (CORS wildcard with credentials), SK0036 (raw
  `RpcException`/`Status` outside `.Grpc`); `PresentationPreconditionCodesTests`; `OptionalDependencySatelliteRulesTests`
  (Grpc never reaches WebApi; GraphQL is a 14 Host package). The tier check (`eng/SharedKernelTiers.targets`) makes
  every package here Host.
- **samples** (OrderApi, BillingApi, DocumentsApi, ShippingApi, CatalogApi) and `consumer-verify` use the one-call
  path, endpoint modules and `ISender` (P-563 S1); a public API change updates them.

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
- [2026-09-24] **Root P-563 — the thin edge in front of 05.Application's use cases (P2–P4, S1).** Endpoint modules:
  `IEndpointModule` and a `MapEndpoints()` source generator (SKEP001–SKEP004) packed inside WebApi. One public
  namespace per package: `.Errors`, `.Http`, `.Idempotency`, `.Options` folded into the root; `ErrorPresentation`,
  `ErrorTypeStatusCodeMap`, `PresentationErrorCodes.ForStatus`, `GrpcStatusCodeMap`, `GrpcErrorCodes.ForStatus`,
  `AddSharedKernelAuthorization()` and the header metadata interfaces internal, visible to the add-ons;
  `GetIfMatchTags()` removed. `Paging`/`CursorPaging` parameters (WebApi now references `04.Contracts`). The
  idempotency codes come from `01.Core`'s `ErrorCodes.Idempotency`. All five samples use modules and send commands
  and queries; permissions moved to the use cases. The endpoint attribute and convention were renamed
  `RequireEndpointPermission` (`52975eed`) so they no longer share a name with 05's `[RequirePermission]`. Docs updated
  to the command/query path. Not yet published.
- [2026-09-26] **Root P-579 — main's P-562/P-563 redesign merged onto the WO-086 foundation.** WO-086's architecture
  kept, main's features re-applied on it. `SharedKernel.Presentation.Core` (Host) now holds what gRPC shares with HTTP:
  the four authorization attributes and conventions (public, `SharedKernel.Presentation.Authorization`), the policy
  machinery and `AddSharedKernelAuthorization()`, `ErrorTypeStatusCodeMap`, the client-message half of
  `ErrorPresentation` and the shared request facts; Grpc references Core, never WebApi; `GrpcStatusCodeMap` back in Grpc.
  The correlation id and the inbound-baggage refusal moved to `SharedKernel.ServiceDefaults.Security`
  (`UseSharedKernelRequestContext()`, `RequestContextOptions.TrustInboundBaggage`); WebApi's `CorrelationIdMiddleware`,
  `WebApiCorrelationIdOptions`, `SharedKernelWebApiOptions.CorrelationId`/`.TrustInboundBaggage` deleted; EventIds
  14000/14006 retired; 14002/14009/14010 emitted by Core. `GetCorrelationId()` reads the request's scope. gRPC gets
  its scope from the pipeline (no interceptor); SignalR's `RequestContextHubFilter` reopens the connection's scope around
  every invocation and `GetTenantId()` returns `TenantId?`. `SharedKernel.Presentation.SignalR.Redis` deleted;
  `SharedKernel.Presentation.GraphQL` (moved here by WO-086) kept. `consumer-verify` composes the request context
  first and proves a hub method and a gRPC method read the caller and correlation id.
