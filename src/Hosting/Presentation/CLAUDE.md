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
| `SharedKernel.Presentation.Core` | Host | What every protocol shares, so gRPC needs no WebApi: authorization attributes and their internal machinery, `ErrorTypeStatusCodeMap`, the client-message half of `ErrorPresentation`, `RequestFacts`, `ServiceDecoration`. No third-party packages |
| `SharedKernel.Presentation.WebApi` | Host | One-call setup and pipeline, endpoint modules, the problem contract, typed results, security headers, CORS, limits, header requirements, paging, `ETag`/304, the 429 body. References Contracts for paging only; **no third-party packages** |
| `SharedKernel.Presentation.WebApi.Generators` | Tooling | Endpoint-module source generator (`netstandard2.0`, SKEP001–SKEP004); not packable, packed inside WebApi under `analyzers/dotnet/cs` |
| `SharedKernel.Presentation.OpenApi` | Host | Asp.Versioning, one OpenAPI 3.1 document per version, Scalar, sunset/deprecation, security schemes; documents what the core enforces, never changes a response |
| `SharedKernel.Presentation.SignalR` | Host | Hub error mapping, `Result` hub methods, invocation rate limit, `RequestContextHubFilter`, `HubGroupNaming`. No third-party packages, no backplane |
| `SharedKernel.Presentation.Grpc` | Host | Exception interceptor building the rich status, `GrpcStatusCodeMap`, `GrpcErrorCodes`. **Never references WebApi or Contracts** |
| `SharedKernel.Presentation.GraphQL` | Host | HotChocolate conventions: snake_case filtering, `FilterBase<T>`/`SortBase<T>`, `PagedResponseType<T>`, `SharedKernelErrorFilter` (ProblemDetails-shaped errors), code-only `GraphQLOptions`. Not AOT-safe |

`consumer-verify/` (untiered) composes Core, WebApi, OpenApi, SignalR and gRPC over Kestrel in CI's required lane.
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
  `Context.GetCorrelationId()`, `HubErrorMessage.TryParse`, `HubGroupNaming.TenantGroup(TenantId)`. Backplane: Microsoft's
  `AddStackExchangeRedis(...)`.
- **Grpc** (`SharedKernel:Presentation:Grpc`) — `builder.AddSharedKernelGrpc()`; services end a failed `Result` with
  `Error.ToException()` (`SharedKernel.Core`).
- **GraphQL** — `services.AddSharedKernelGraphQL()` (calls HotChocolate's `AddGraphQLServer()`, so `app.MapGraphQL()`
  works) before the service's own `AddGraphQLServer()`.

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
   cancellation → 504; else 500 `unexpected.exception` (exception detail only in Development or with
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
   is resolved with `UserContextResolver`, never raw claims; an unmapped principal → 403 + log 14009. Max-age checks
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
11. **Every authentication scheme needs an `IUserContextMapper`** — the startup check warns per scheme (14010).
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
    production package outside this domain.
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
28. **Never log principal data or secrets:** refusals log endpoint and code (14002); idempotency keys are never logged
    (14003).
29. **GraphQL:** `AllowIntrospection` gates introspection both ways (HotChocolate disables it outside Development by
    default). Filter/sort types derive from `FilterBase<T>`/`SortBase<T>`, never HotChocolate's `FilterInputType<T>`/
    `SortInputType<T>` directly (`CommunicationLayeringRules.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL`).

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

## Logging

`LoggingEventIdRanges.Presentation` (14000–14999). Every id is pinned with its level by a `LoggerMessageEventIdTests`
in each package; retired ids are never reused and their absence is asserted. A new statement takes the next free id of
its package's sub-block and a row in that test.

| Sub-block | Package | In use |
| --- | --- | --- |
| 14000–14099 | WebApi and Core | 14001 Error server error from an exception · 14002 Warning authorization refused (Core) · 14003 Warning idempotency key refused · 14004 Critical CORS settings invalid · 14005 Warning rate limit rejected · 14007 Debug client error from an exception · 14008 Debug client closed the request (499) · 14009 Warning principal without mapper (Core) · 14010 Warning scheme without mapper (Core, startup) · 14011 Warning `UseSharedKernelWebApi()` never called · 14012 Warning exception details outside Development · 14013 Warning WebSocket origin refused. Retired: 14000, 14006 |
| 14100–14199 | SignalR | 14100 Error unhandled hub exception · 14101 Warning invocation rate limited · 14103 Error hub server error · 14104 Debug hub client error · 14106 Debug connection closed during an invocation · 14107 Error stream inside a `Result`. Retired: 14102, 14105 |
| 14200–14299 | Grpc | 14200 Error unhandled exception · 14202 Error server error · 14203 Debug client error · 14204 Debug call cancelled. Retired: 14201 |
| 14300–14399 | OpenApi | 14300 Information documents not mapped (environment) · 14301 Warning documents exposed without authorization |
| — | GraphQL | none |

## Cross-Domain Couplings

- **01.Core:** `Error`/`ErrorType`/`ErrorCodes` (incl. `ErrorCodes.Idempotency`, shared with 05's idempotency
  behavior)/`ErrorArgumentNames`, `WellKnownHeaders`, `ILocalizationCatalog`, `IClock`, `Error.ToException()`;
  **Execution:** `IRequestContextAccessor`, `RequestContextScope`, `TenantId`.
- **12.Security:** `IUserContext`, `IUserContextMapper`, `UserContextResolver`, `GetAuthenticationMethodTime`,
  `UserContext.MaxFutureAuthTime` — the only inputs the attributes read.
- **13.ServiceDefaults:** `UseSharedKernelRequestContext()` (composed first by every host); `AddSharedKernelRateLimiting()`
  leaves `OnRejected` null so our 429 body applies; `TenantResolutionMiddleware` goes in the `BeforeAuthorization` hook.
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
- SignalR tests use a real `HubConnection`, gRPC a real `Grpc.Net.Client` channel, OpenAPI tests generate documents.
- Time through `FakeClock`; never `Task.Delay`. Log assertions check EventId and level through `16.Testing`'s
  in-memory logger, never rendered text.
- A security-relevant test must be able to fail: mutate the condition and confirm the assertion catches it.
- Fakes: `SharedKernel.Presentation.Testing` — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- An endpoint protected only by its command's `[RequirePermission]` shows no security requirement in OpenAPI; add
  `.RequireAuthorization()` or a fallback policy.
- `Paging`/`CursorPaging` and `IfMatch<T>` tag parsing are minimal-API features; MVC actions bind and parse themselves.
- GraphQL depends on HotChocolate, is not AOT-safe, and has no PublicAPI tracking or log events.
