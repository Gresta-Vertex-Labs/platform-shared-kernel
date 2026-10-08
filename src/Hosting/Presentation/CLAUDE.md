# 14.Presentation — Domain Brain

> The inbound API boundary. It turns outcomes (`Result`, `Error`, exceptions) into what a caller receives — an HTTP
> response or RFC 9457 problem, a SignalR `HubException`, a gRPC `google.rpc.Status` — with one error contract for all
> three, and owns the concerns of the edge: endpoint authorization against `IUserContext`, security headers, CORS,
> request limits, `Idempotency-Key`, `ETag`/`If-Match`, paging parameters, API versioning/OpenAPI, and HotChocolate
> conventions. It converts outcomes; it never produces them: use cases are `05.Application` commands and queries that
> endpoint modules send through `ISender`, and this domain references neither `05.Application` nor MediatR. It does
> **not** own the request context — correlation id, inbound-baggage refusal and the request's `RequestContextScope`
> belong to `13.ServiceDefaults`' `UseSharedKernelRequestContext()` — nor outbound calls (`11.Communication`), nor the
> rate limiter itself (`AddSharedKernelRateLimiting()` in ServiceDefaults). Every setting is documented in
> [`CONFIGURATION.md`](CONFIGURATION.md).

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Presentation.Core` | Host | What every protocol shares, so gRPC needs no WebApi: the four authorization attributes + `AuthorizationConventionExtensions` (public, namespace `SharedKernel.Presentation.Authorization`); internal policy provider, requirement handler, result handler, startup check, `AddSharedKernelAuthorization()`, `ErrorTypeStatusCodeMap`, the client-message half of `ErrorPresentation`, `RequestFacts`, `ServiceDecoration`. References Primitives, Execution, Localization, Security.Abstractions; no third-party packages |
| `SharedKernel.Presentation.WebApi` | Host | One-call setup and pipeline, `IEndpointModule` + generated `MapEndpoints()`, the problem contract for every error source, typed results for `Result`, security headers, CORS + WebSocket origin check, request limits, required/accepted `Idempotency-Key`/`If-Match`, `Paging`/`CursorPaging`, `ETag`/304, the 429 body, `GetCorrelationId()`. References Core, Primitives, `SharedKernel.Core`, Configuration, Contracts (paging only); **no third-party packages** |
| `SharedKernel.Presentation.WebApi.Generators` | Tooling | The endpoint-module source generator (`netstandard2.0`, SKEP001–SKEP004); not packable, packed inside WebApi under `analyzers/dotnet/cs` |
| `SharedKernel.Presentation.OpenApi` | Host | API versioning (Asp.Versioning), one OpenAPI 3.1 document per version, Scalar, sunset/deprecation, security schemes; documents what the core enforces, never changes a response. References WebApi, Core |
| `SharedKernel.Presentation.SignalR` | Host | Hub error mapping (`{code}: {message}`), `Result` hub methods, invocation rate limit, `RequestContextHubFilter`, `GetTenantId()`/`GetCorrelationId()`, `HubGroupNaming`. References WebApi, Presentation.Core, Primitives, `SharedKernel.Core`, Configuration, Execution; no third-party packages, no backplane |
| `SharedKernel.Presentation.Grpc` | Host | The exception interceptor building the rich status, `GrpcStatusCodeMap`, `GrpcErrorCodes`. References Core, `SharedKernel.Core`, Configuration — **never WebApi, never Contracts**; `Grpc.AspNetCore`, `Grpc.StatusProto`, `Google.Api.CommonProtos` |
| `SharedKernel.Presentation.GraphQL` | Host | HotChocolate server conventions: snake_case filtering (`SharedKernelFilterConvention`), `FilterBase<T>`/`SortBase<T>`, `PagedResponseType<T>`, `SharedKernelErrorFilter` (ProblemDetails-shaped errors), `GraphQLOptions`. References Primitives, Contracts; HotChocolate (not AOT-safe) |

`consumer-verify/` (untiered) composes Core, WebApi, OpenApi, SignalR and gRPC over Kestrel and runs in CI's required
lane. The Core/WebApi/OpenApi/SignalR/Grpc packages track `PublicAPI.*.txt` (RS0016/RS0017/… and CS1591 are errors).

## Public Entry Points

**WebApi** (namespace `SharedKernel.Presentation.WebApi`, section `SharedKernel:Presentation:WebApi` —
`SharedKernelWebApiOptions`: `Cors`, `SecurityHeaders`, `Limits`, `Problems`, `RemoveServerHeader`)

- `builder.AddSharedKernelWebApi(o => …)`; `app.UseSharedKernelWebApi(p => p.AtStart(…).BeforeAuthentication(…)
  .BeforeAuthorization(…))` (`WebApiPipeline` hooks).
- Endpoints: `IEndpointModule` (`static void Map(IEndpointRouteBuilder app)`) + the generated internal
  `app.MapEndpoints()`.
- Results: `ToOk`, `ToCreated`, `ToAccepted`, `ToNoContent`, `ToOkWithETag` (`OkWithETag<T>`), `ToHttpResult`,
  `ToErrorResult` (`ErrorHttpResult`) — sync and `Task` forms; `Error.ToProblemDetails()`.
- Headers: `IdempotencyKey`, `IfMatch<TVersion>` parameters; `[RequireIdempotencyKey]`/`[AcceptIdempotencyKey]`,
  `[RequireIfMatch]`/`[AcceptIfMatch]` or `.RequireIdempotencyKey()`/`.AcceptIdempotencyKey()`/`.RequireIfMatch()`/
  `.AcceptIfMatch()`; `GetIdempotencyKey()`, `GetIfMatch()`, `SetETag()`.
- Paging: `Paging` (`page`, `pageSize` → `PageRequest`), `CursorPaging` (`cursor`, `limit` → `CursorPageRequest`).
- Per-endpoint: `WithRequestSizeLimit`/`DisableRequestSizeLimit`, `WithContentSecurityPolicy`.
- `HttpContext.GetCorrelationId()`; constants `PresentationErrorCodes`, `ProblemDetailsExtensionNames`.

**Core** (namespace `SharedKernel.Presentation.Authorization`) — `[RequireEndpointPermission]`, `[RequireRole]`,
`[RequireFreshAuthentication]`, `[RequireAuthenticationMethod(…, MaxAgeSeconds = n)]` and the conventions
`.RequireEndpointPermission(…)`, `.RequireRole(…)`, `.RequireFreshAuthentication(…)`, `.RequireAuthenticationMethod(…)`.
No registration call: WebApi, SignalR and gRPC register the machinery.

**OpenApi** (section `SharedKernel:Presentation:OpenApi`, `SharedKernelOpenApiOptions`: `Title`, `Description`,
`Versioning`, `Bearer`, `ApiKeyHeaderName`, `MutualTls`, `ExposeInProduction`) — `builder.AddSharedKernelOpenApi(o => …)`
and `app.MapSharedKernelOpenApi()` (maps nothing outside Development unless `ExposeInProduction`).

**SignalR** (section `SharedKernel:Presentation:SignalR`, `SharedKernelSignalROptions.InvocationRateLimit`) —
`builder.AddSharedKernelSignalR()`; `Context.GetTenantId()` (`TenantId?`), `Context.GetCorrelationId()`;
`HubErrorMessage.TryParse`; `HubGroupNaming.TenantGroup(TenantId)`. A Redis backplane is Microsoft's
`AddStackExchangeRedis(...)`.

**Grpc** (section `SharedKernel:Presentation:Grpc`, `SharedKernelGrpcOptions.ErrorDomain`) —
`builder.AddSharedKernelGrpc()`; `GrpcErrorCodes`. Services end a failed `Result` with `Error.ToException()`
(`SharedKernel.Core`).

**GraphQL** — `services.AddSharedKernelGraphQL()` (HotChocolate's `AddGraphQLServer()`, so `app.MapGraphQL()` works) before the service's own `AddGraphQLServer()`; `FilterBase<T>`,
`SortBase<T>`, `PagedResponseType<T>.FromPagedList(...)`/`FromConnection`/`FromPage`/`From`.

Canonical host pipeline (`samples/Shop/Ordering/Shop.Ordering.Api/Program.cs`, `samples/Shop/Catalog/Shop.Catalog.Api/Program.cs`):

```csharp
app.UseSharedKernelRequestContext();   // 13.ServiceDefaults.Security — first: correlation id, baggage refused, scope
app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));
app.MapEndpoints();
```

`UseSharedKernelWebApi` order: `AtStart` hooks → HSTS (not Development) → security headers → `UseExceptionHandler()` →
status code pages (not gRPC) → `UseRouting()` → CORS + WebSocket origin check (only with origins) →
`BeforeAuthentication` → `UseAuthentication()` (when schemes exist) → `BeforeAuthorization` → `UseRateLimiter()`
(when configured) → `UseAuthorization()` → `HeaderRequirementsMiddleware` (headers, then paging).

## Rules & Invariants

1. **One error decision for every protocol.** `ErrorPresentation` (internal; message half and `ErrorTypeStatusCodeMap`
   in Core, `GetStatusCode` in WebApi) decides status and client text for HTTP, SignalR and gRPC. Never map
   `ErrorType` in a switch of your own. HTTP: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict
   409, BusinessRule 422, Unexpected 500, Unavailable 503 (+`Retry-After`), Timeout 504, else 500. gRPC
   (`GrpcStatusCodeMap`, a sibling, never merged): InvalidArgument, Unauthenticated, PermissionDenied, NotFound,
   Aborted, FailedPrecondition, Internal, Unavailable, DeadlineExceeded, else Unknown. Never renumber `ErrorType`
   (17.Workflows persists the number).
2. **One problem shape.** Every HTTP error is `application/problem+json` with `type`, `title` (reason phrase, never
   the code), `status`, `instance`, `errorCode`, `traceId`, `correlationId` and `Cache-Control: no-store`, even when
   `Accept` excludes JSON. A new error path uses `ProblemFactory` + `ProblemResponseWriter`; framework problems are
   completed by `ProblemDetailsCustomizer`. Never write problem JSON or construct `ProblemDetails` by hand.
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
8. **Permissions go on the use case.** `05.Application`'s `[RequirePermission]` on a command/query applies on every
   path; `[RequireEndpointPermission]` is only for what sends no command (hubs, gRPC methods, endpoints without
   `ISender`, `MapSharedKernelOpenApi()`). Authentication strength (fresh/method) stays here. Both layers answer
   `unauthorized.default` (401) and `forbidden.insufficient_permission` (403) — keep them identical. Never give an edge
   attribute a name a use-case attribute already has (CS0104 when both namespaces are imported).
9. **Authorization fails closed.** The four attributes derive from `AuthorizeAttribute`; the requirement is encoded in
   the policy name `SharedKernel:{kind}:{values}` (`permission`, `role`, `fresh`, `amr`, `amr-max-age`) and cannot be
   replaced (setters throw). Every platform policy requires an authenticated user (anonymous → 401 first). The caller
   is resolved with `UserContextResolver`, never raw claims; an unmapped principal → 403 + log 14009. Max-age checks
   use `IUserContext.GetAuthenticationMethodTime`: unknown or future-beyond-`MaxFutureAuthTime` never passes.
   Step-up (RFC 9470 challenge, scheme `Bearer` or `DPoP` only) applies only when **every** unmet requirement is a
   freshness/method one. 403 messages never name the permission or role.
10. **Decorate, never replace.** `AddSharedKernelAuthorization()` (internal, idempotent) decorates the
    `IAuthorizationPolicyProvider` and `IAuthorizationMiddlewareResultHandler` registered before it
    (`ServiceDecoration.Decorate`); `SharedKernelAuthorizationStartupCheck` stops the host when a later registration
    displaced either. Keep that check whenever the decoration changes. Refusal bodies go through the internal
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
    origin, and (outside Development) credentials with `http://` origins; WebSocket requests from disallowed origins →
    403.
18. **gRPC:** cancellation wins over every other mapping; a foreign `RpcException` keeps only its code; the error
    domain is never blank. No `ThrowIfFailure`/`GetValueOrThrow` extensions (they collide with `SharedKernel.Core`'s).
19. **SignalR:** hub errors are `HubException("{code}: {message}")`; tenantless connections get `null` from
    `GetTenantId()`; `TenantGroup(Guid.Empty)`/`default` throws; one partitioned rate limiter, no per-bucket timer.
    Filter order: `RequestContextHubFilter` → `HubExceptionMappingFilter` → `HubInvocationRateLimitFilter`; gRPC's
    `GrpcExceptionInterceptor` is registered first (outermost). Consumers call the setup before adding their own.
20. **Setup is idempotent** through markers (`WebApiPipelineState`, `OpenApiSetupState`, `SignalRServicesMarker`,
    `GrpcServicesMarker`); `configure` is applied on every call. Without `UseSharedKernelWebApi()` the host logs 14011;
    without `AddSharedKernelWebApi()` it throws.
21. **Options:** `AddValidatedOptions` + `ISectionBoundOptions` + a validator. List options are get-only and
    configuration **appends** — document collection defaults as "added to". Invalid WebApi settings surface at first
    read (Kestrel at `Build()`), `ValidateOnStart` at the latest.
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
    Endpoint-level authorization metadata is all it can see: an endpoint protected only by its command documents no
    security requirement.
27. **No reflection invocation** (`HubMethodResult` uses cached compiled accessors); **constants, never literals** for
    headers (`WellKnownHeaders`, `HeaderNames`, `PresentationHeaderNames`), problem members
    (`ProblemDetailsExtensionNames`) and codes (`PresentationErrorCodes`, `GrpcErrorCodes`, `ErrorCodes`).
28. **Never log principal data or secrets:** refusals log endpoint and code (14002); idempotency keys are never logged
    (14003).

## Decisions

| Decision | Why |
| --- | --- |
| Native authorization policies behind attributes, not endpoint filters | Filters were fail-open by omission, gave anonymous callers 403, and did not reach SignalR hub methods |
| One error pipeline through `IProblemDetailsService` and `ErrorPresentation` | Localization, redaction and media type were inconsistent across error sources |
| `Unavailable`/`Timeout` → 503/504 (gRPC `Unavailable`/`DeadlineExceeded`) | Clients must tell "retry later" from a defect |
| OpenAPI in a separate add-on | Keeps the core free of third-party packages; Asp.Versioning supplies per-version documents |
| Presentation.Core holds authorization and the client-message rule | gRPC shares them with HTTP and must not reference WebApi; `GetStatusCode` stays in WebApi because it reads WebApi's precondition codes |
| `GrpcStatusCodeMap` lives in Grpc | A WebApi host takes no `Grpc.Core.Api` |
| Correlation id and baggage refusal belong to `ServiceDefaults.Security` | One middleware owns the request's scope and its id with one validation rule for every protocol, including gRPC-only and SignalR-only hosts |
| 412 decided from the error, not the endpoint | An optional conditional write got 409 where RFC 9110 answers 412 |
| Required **and** accepted headers | Unvalidated accepted headers silently disabled a precondition or idempotency |
| Typed results for MVC; no `ToActionResult` | The MVC family was weaker; typed results work in controllers |
| Foreign `RpcException`s rebuilt without trailers | Another service's `ErrorInfo`, field paths and trace ids leaked |
| Endpoint modules found by a source generator | No reflection or scanning; one package reference |
| WebApi free of MediatR and 05.Application | Any `Result`-returning code maps the same way |
| No upload validation, payload-limit or version-lifecycle middleware, no SignalR backplane package | Presigned uploads (08.Storage) replace uploads; the rest duplicated the framework or was unused |
| .NET 10 `AddValidation()` not adopted | DataAnnotations only, experimental parts; validation lives in the application pipeline |
| `[AllowAnonymous]` switches off every requirement | ASP.NET Core semantics |
| The automatic 412 carries no current `ETag` | Clients re-read, the safer choice |
| gRPC authorization refusals carry no rich status | The HTTP pipeline answers them before the service runs |

## Logging

EventId block **14000–14999** (`LoggingEventIdRanges.Presentation`). Every id is pinned with its level by a
`LoggerMessageEventIdTests` in each package; retired ids are never reused and their absence is asserted. A new
statement takes the next free id of its package's sub-block and a row in that test.

| Sub-block | Package | In use |
| --- | --- | --- |
| 14000–14099 | WebApi and Core | 14001 Error server error from an exception · 14002 Warning authorization refused (Core) · 14003 Warning idempotency key refused · 14004 Critical CORS settings invalid · 14005 Warning rate limit rejected · 14007 Debug client error from an exception · 14008 Debug client closed the request (499) · 14009 Warning principal without mapper (Core) · 14010 Warning scheme without mapper (Core, startup) · 14011 Warning `UseSharedKernelWebApi()` never called · 14012 Warning exception details outside Development · 14013 Warning WebSocket origin refused. Retired: 14000, 14006 (correlation id events are now 13006/13007 in ServiceDefaults.Security) |
| 14100–14199 | SignalR | 14100 Error unhandled hub exception · 14101 Warning invocation rate limited · 14103 Error hub server error · 14104 Debug hub client error · 14106 Debug connection closed during an invocation · 14107 Error stream inside a `Result`. Retired: 14102, 14105 |
| 14200–14299 | Grpc | 14200 Error unhandled exception · 14202 Error server error · 14203 Debug client error · 14204 Debug call cancelled. Retired: 14201 |
| 14300–14399 | OpenApi | 14300 Information documents not mapped (environment) · 14301 Warning documents exposed without authorization |
| — | GraphQL | none |

## Cross-Domain Couplings

- **01.Core:** `Error`/`ErrorType`/`ErrorCodes` (incl. `ErrorCodes.Idempotency`, shared with 05's idempotency
  behavior)/`ErrorArgumentNames`, `WellKnownHeaders`, `LoggingEventIdRanges`, `AddValidatedOptions`,
  `ILocalizationCatalog`, `IClock`/`AddClock`, `Error.ToException()`. **Execution:** `IRequestContextAccessor`,
  `RequestContextScope`, `TenantId` — correlation readers, the hub filter, `GetTenantId()`.
- **12.Security:** `IUserContext`, `IUserContextMapper`, `UserContextResolver`, `GetAuthenticationMethodTime`,
  `UserContext.MaxFutureAuthTime` — the only inputs the attributes read.
- **13.ServiceDefaults:** `AddSharedKernelRequestContext()`/`UseSharedKernelRequestContext()` (composed first by every
  host); `AddSharedKernelRateLimiting()` leaves `OnRejected` null so our 429 body applies; `TenantResolutionMiddleware`
  goes in the `BeforeAuthorization` hook.
- **05.Application:** no reference either way; endpoint modules send its commands; same 401/403 codes.
- **04.Contracts:** paging types and codes (WebApi and GraphQL only; never Grpc).
- **06.Persistence / 08.Storage:** `EntityVersion` is the usual `IfMatch<TVersion>`; their conflict codes are the
  default `PreconditionFailedErrorCodes`.
- **11.Communication.Rest** reads our problems back (`errorCode`, fallback `http.{status}`; 412 → Conflict, 429/503 →
  Unavailable, 504 → Timeout) — keep `PresentationErrorCodes.ForStatus` and its fallback identical.
- **00.Governance:** `PresentationLayeringRules` (`NoDirectProblemDetailsConstructionOutsideWebApi`,
  `NoInlineResultBranchBeforeHttpResultOutsideWebApi`, `NoOpenApiStackDependencyOutsideOpenApiAddOn`,
  `GrpcNeverReferencesContracts`), SK0022, SK0032 (CORS wildcard + credentials), SK0036 (raw `RpcException`/`Status`
  outside `.Grpc`), `PresentationPreconditionCodesTests`.
- **samples** (the Shop's services in `samples/Shop`: Catalog, Ordering, Inventory, Billing, Merchant, Notify, Reports) and
  `consumer-verify` use the one-call path and endpoint modules; a public API change updates them.

## Testing

- All presentation test projects are in the **Unit** lane (in-process `TestServer`/Kestrel, no Docker).
- Behaviour is tested through real in-process hosts built with the one-call setup (`WebApiTestHost`, `FullStackHost`);
  what `TestServer` does not enforce (body limits, `Server` header) runs on Kestrel (`StartKestrelAsync`); HSTS needs an
  `https` non-`localhost` base address.
- Every HTTP error assertion goes through `ShouldBeProblemAsync` (media type, member set, `X-Correlation-Id` vs
  `correlationId`).
- SignalR tests use a real `HubConnection`, gRPC a real `Grpc.Net.Client` channel, OpenAPI tests generate documents.
- Time through `FakeClock` as `IClock`; never `Task.Delay`. Log assertions check EventId and level through
  `16.Testing`'s in-memory logger, never rendered text.
- A security-relevant test must be able to fail: mutate the condition and confirm the assertion catches it.
- Consumer fakes: `src/Hosting/Presentation/SharedKernel.Presentation.Testing` (gRPC `TestServerCallContext`; open a
  `RequestContextScope` yourself for an ambient caller) and `SharedKernel.Security.Testing`'s `FakeUserContext`
  (`WithAuthenticationMethodTime` for step-up tests).

## Known Limitations

- An endpoint protected only by its command's `[RequirePermission]` shows no security requirement in OpenAPI; services
  add `.RequireAuthorization()` or a fallback policy.
- `Paging`/`CursorPaging` and the `IfMatch<T>` tag parsing are minimal-API features; MVC actions bind and parse
  themselves.
- The automatic 412 carries no current `ETag`.
- GraphQL depends on HotChocolate and is not AOT-safe; it has no PublicAPI tracking or log events.
