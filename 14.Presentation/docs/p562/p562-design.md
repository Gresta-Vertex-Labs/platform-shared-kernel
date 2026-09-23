# Presentation gold-standard pass (P-562) — binding design decisions (2026-09-23)

Source of findings: [`p562-review-findings.md`](p562-review-findings.md); IDs B*, R*, F* refer to it.
Owner decisions (user, 2026-09-23): **native authorization policies**; remove **uploads**, **version-lifecycle
middleware**, **SignalR extras**, **gRPC duplicates**; add **one-call setup + typed results**, **unified errors +
OpenAPI**, **`ErrorType.Unavailable`/`Timeout`** (503/504, touches 01/07/08/09 plus the 11/17 mirrors), **gRPC and
SignalR error parity**; package layout **core + OpenAPI add-on**.
Nothing in 14.Presentation is published → breaking changes are free. 01.Core, 07 and 08 are published alpha →
additive changes only. No state-map phases; brains and READMEs are rewritten in the final wave. Code wins where this
record and the code disagree.

## D0. Package layout

| Package | References | Third-party packages |
| --- | --- | --- |
| `SharedKernel.Presentation.WebApi` (core) | Primitives, Core, Configuration, Localization, Security.Abstractions; `FrameworkReference Microsoft.AspNetCore.App` | none |
| `SharedKernel.Presentation.OpenApi` (new) | WebApi | Asp.Versioning.Http (10.2.3), Asp.Versioning.Mvc.ApiExplorer (10.2.1), Asp.Versioning.OpenApi (10.2.3), Microsoft.AspNetCore.OpenApi, Scalar.AspNetCore (2.17.8) |
| `SharedKernel.Presentation.SignalR` | WebApi | none (`Microsoft.AspNetCore.SignalR.StackExchangeRedis` removed) |
| `SharedKernel.Presentation.Grpc` | WebApi | Grpc.AspNetCore, Grpc.StatusProto (2.80.0 — matches the Grpc pin) |

- All four track their public API (`Microsoft.CodeAnalysis.PublicApiAnalyzers`, empty `PublicAPI.Shipped.txt`,
  populated `PublicAPI.Unshipped.txt`), like every published package.
- `[LoggerMessage]` EventId sub-blocks of `LoggingEventIdRanges.Presentation` (14000): WebApi 14000–14099,
  SignalR 14100–14199, Grpc 14200–14299, OpenApi 14300–14399. Keep existing ids where the log statement survives.
- `.Grpc` still never references 04.Contracts (`GrpcNeverReferencesContracts`).
- Everyday surface (setup, result mapping, endpoint conventions, accessors) lives in each package's **root
  namespace**, so one `using` covers the common path. Types (options, attributes, result types) live in
  sub-namespaces. Do not create a namespace named `Results` (it shadows `Microsoft.AspNetCore.Http.Results`).

## D1. One error contract, one pipeline (B3–B6, B14, B15, F3, F8)

**Wire shape** — every error response from every path is `application/problem+json`:

| Member | Value |
| --- | --- |
| `type` | Framework default (RFC 9110 section URI for the status). When `Problems.TypeBaseUri` is configured: `{TypeBaseUri}{errorCode}`. Never a third-party site. |
| `title` | Framework default reason phrase for the status ("Not Found"). Never the error code. |
| `status` | From `ErrorPresentation.GetStatusCode` |
| `detail` | `ErrorPresentation.GetClientMessage`: localized `Error.Message`; for server categories (`Unexpected`, `Unavailable`, `Timeout`) replaced outside Development by a generic, category-specific sentence |
| `instance` | Request path |
| `errorCode` | `Error.Code`; framework-generated problems (routing 404/405, 415, empty 401/403, …) get `http.{status}` — the same fallback 11.Communication.Rest already reads |
| `traceId` | Framework default |
| `correlationId` | The resolved correlation id (D5), when present |
| `errors` / `errorCodes` | Unchanged shape: field path (or code) → messages / codes, for `Error.Details` and `ValidationException` |
| `exception` | Development only (or `Problems.IncludeExceptionDetails = true`), unhandled exceptions: `{ type, message, stackTrace }` |

### Mechanics

- `AddSharedKernelWebApi` registers `AddProblemDetails(o => o.CustomizeProblemDetails = …)`, which fills
  `instance`, `correlationId`, the `errorCode` fallback and the `type` override. Every writer in all four packages
  writes through `IProblemDetailsService.TryWriteAsync`; when it returns false (the `Accept` header excludes JSON)
  fall back to `Response.WriteAsJsonAsync(problem, …, contentType: "application/problem+json")`.
- `ErrorPresentation` (public static, namespace `SharedKernel.Presentation.WebApi.Errors`) is the one place that
  decides status and client text, used by HTTP, SignalR and gRPC:
  - `int GetStatusCode(Error error, HttpContext? httpContext)` — `ErrorTypeStatusCodeMap` plus the `If-Match`
    rule (D10).
  - `string GetClientMessage(Error error, HttpContext? httpContext)` — culture from `IRequestCultureFeature`
    (falling back to `CultureInfo.CurrentUICulture`), translation via an optional `ILocalizationCatalog` from
    `RequestServices`, redaction of server categories outside Development (`IHostEnvironment` from
    `RequestServices`; no context → treated as production).
  - `bool IsServerError(ErrorType type)`.
- `ErrorTypeStatusCodeMap.Resolve`: add `Unavailable → 503`, `Timeout → 504`. A 503 gets `Retry-After` (whole
  seconds) when `Problems.UnavailableRetryAfter` is set.
- `ErrorHttpResult` (public sealed, `…WebApi.Errors`): `IResult`, `IStatusCodeHttpResult`, `IContentTypeHttpResult`;
  exposes `Error`; builds the ProblemDetails at `ExecuteAsync` time, with the `HttpContext` (fixes B3 by design).
  `ErrorProblemDetailsExtensions.ToProblemDetails(this Error, HttpContext)` stays public with a **required**
  context.
- Exception handler (registered automatically; the type becomes internal):
  - Client abort (`OperationCanceledException` while `RequestAborted` is cancelled) → status 499, no body, Debug log.
  - `BadHttpRequestException` → its own status (413 → code `request.too_large`), its client-safe message.
  - `ValidationException` → 400 with all field errors; other `SharedKernelException` → its `Error` via
    `ErrorPresentation`.
  - Anything else → 500 `ErrorCodes.Unexpected.Default`, generic detail; Development adds `exception`.
  - Logging: server errors (≥ 500) at Error with the exception, 4xx at Debug. .NET 10's exception middleware no
    longer logs handled exceptions, so this handler is the only log.
- Status code pages produce the same shape for framework-generated empty 4xx/5xx responses, except gRPC requests
  (`Content-Type: application/grpc*`), which are left untouched.
- Presentation-originated codes live in `PresentationErrorCodes` (public constants): `request.too_large`,
  `idempotency.key_required`, `idempotency.key_invalid`, `precondition.required`, `rate_limit.exceeded`,
  `unauthorized.step_up_required`, and `ForStatus(int)` → `http.{status}`. Platform codes come from `ErrorCodes`.

## D2. `Result` → HTTP (F2)

Minimal APIs, root namespace `SharedKernel.Presentation.WebApi`:

```csharp
Results<Ok<T>, ErrorHttpResult>            ToOk<T>(this Result<T> result)
Results<Ok<TOut>, ErrorHttpResult>         ToOk<T, TOut>(this Result<T> result, Func<T, TOut> map)
Results<OkWithETag<T>, ErrorHttpResult>    ToOkWithETag<T>(this Result<T> result, Func<T, string> version)                      // D10
Results<OkWithETag<TOut>, ErrorHttpResult> ToOkWithETag<T, TOut>(this Result<T> result, Func<T, TOut> map, Func<T, string> version)
Results<Created<T>, ErrorHttpResult>       ToCreated<T>(this Result<T> result, Func<T, string> location)
Results<Created<TOut>, ErrorHttpResult>    ToCreated<T, TOut>(this Result<T> result, Func<T, string> location, Func<T, TOut> map)
Results<Accepted<T>, ErrorHttpResult>      ToAccepted<T>(this Result<T> result, Func<T, string>? location = null)
Results<NoContent, ErrorHttpResult>        ToNoContent(this Result result)
Results<NoContent, ErrorHttpResult>        ToNoContent<T>(this Result<T> result)
Results<TSuccess, ErrorHttpResult>         ToHttpResult<T, TSuccess>(this Result<T> result, Func<T, TSuccess> onSuccess) where TSuccess : IResult
Results<TSuccess, ErrorHttpResult>         ToHttpResult<TSuccess>(this Result result, Func<TSuccess> onSuccess) where TSuccess : IResult
ErrorHttpResult                            ToErrorResult(this Error error)
```

Every method also exists on `Task<Result<T>>`/`Task<Result>` (returning `Task<…>`), so handlers write
`sender.Send(cmd, ct).ToCreated(o => $"/orders/{o.Id}")`. Typed unions let OpenAPI infer the success response.

MVC, same namespace: `IActionResult ToActionResult(this Result)` (204 | problem),
`ActionResult<T> ToActionResult<T>(this Result<T>)` (200 | problem),
`IActionResult ToActionResult<T>(this Result<T>, Func<T, IActionResult> onSuccess)`; failures run the same logic as
`ErrorHttpResult`. `ToProblemDetailsResult` is deleted.

## D3. Authorization — native policies (R1, B1, B2, B14)

- Attributes (namespace `…WebApi.Authorization`, names unchanged): `RequirePermissionAttribute(params string[])`,
  `RequireRoleAttribute(params string[])`, `RequireFreshAuthenticationAttribute(int maxAgeSeconds)`,
  `RequireAuthenticationMethodAttribute(params string[])`. Each is `Attribute, IAuthorizeData` whose `Policy` is an
  internal encoded name (prefix `SharedKernel:`); `Roles`/`AuthenticationSchemes` stay null. Semantics unchanged:
  OR within one attribute, AND across attributes (the framework combines them). Constructor arguments are
  validated (non-empty, no separator characters).
- Conventions (root namespace, generic over `TBuilder : IEndpointConventionBuilder`): `RequirePermission(…)`,
  `RequireRole(…)`, `RequireFreshAuthentication(int seconds)` / `(TimeSpan)`, `RequireAuthenticationMethod(…)` →
  `RequireAuthorization(new …Attribute(…))`. They work on route handlers, groups, `MapControllers()`,
  `MapHub<T>()` and `MapGrpcService<T>()`.
- `services.AddSharedKernelAuthorization()` (public, idempotent; called by the WebApi, SignalR and gRPC setup):
  `AddAuthorization()`, a policy provider that decodes `SharedKernel:` names and delegates every other name to the
  default provider, the requirement handlers, and an `IAuthorizationMiddlewareResultHandler` decorator.
- Handlers evaluate `UserContextResolver.Resolve(context.User, IEnumerable<IUserContextMapper>)` — the principal
  being authorized, identical for HTTP, SignalR and gRPC — never raw claims or `ClaimTypes.Role`. Freshness uses
  `IClock` (resolved only when needed) with `IsAuthenticationFresherThan`; methods use `WasAuthenticatedWith`.
  Every generated policy requires an authenticated user, so anonymous → challenge → 401.
- Result handler (HTTP bodies; nothing is written for gRPC requests, which map HTTP 401/403 themselves):
  - Challenge → the scheme's challenge (keeps `WWW-Authenticate`) + ProblemDetails 401 `ErrorCodes.Unauthorized.Default`.
  - Forbid → ProblemDetails 403 `ErrorCodes.Forbidden.InsufficientPermission`; the message never names roles or
    permissions.
  - Step-up (the failed requirements are freshness or authentication-method requirements) → 401 with
    `WWW-Authenticate: Bearer error="insufficient_user_authentication", error_description="…"` (+ `max_age` for
    freshness) per RFC 9470, code `unauthorized.step_up_required`.
  - Warning audit log (EventId 14002 kept): endpoint display name + code, no principal data.
- Rules kept from the old brain: evaluation always through `IUserContext`; HTTP failures always carry ProblemDetails.
- Deleted: `AuthorizationRequirementEndpointFilter`, `AddSharedKernelAuthorizationFilters`, the per-builder
  extension overloads, `GrpcAuthorizationInterceptor`.

## D4. One-call setup (F1)

```csharp
public static IHostApplicationBuilder AddSharedKernelWebApi(this IHostApplicationBuilder builder, Action<WebApiOptions>? configure = null)
public static IApplicationBuilder UseSharedKernelWebApi(this IApplicationBuilder app)
```

`WebApiOptions : ISectionBoundOptions` (`SharedKernel:Presentation:WebApi`), bound and validated at startup via
`AddValidatedOptions` + `ValidateOnStart`; `configure` runs after binding. Idempotent.

```text
WebApiOptions
  CorrelationId   { Enabled = true, MaxLength = 128, AllowedCharacterPattern = "^[A-Za-z0-9\-_:.]+$" }
  Cors            { AllowedOrigins = [], AllowedMethods = [], AllowedHeaders = [], ExposedHeaders = <platform set>,
                    AllowCredentials = false, PreflightMaxAge = 00:10:00 }
  SecurityHeaders { Enabled = true, Hsts = true, HstsMaxAge = 365 days, HstsIncludeSubDomains = true, HstsPreload = false,
                    ContentTypeOptions = "nosniff", FrameOptions = "DENY", ReferrerPolicy = "no-referrer",
                    PermissionsPolicy = "geolocation=(), microphone=(), camera=()",
                    ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'" }   // null/empty = header off
  Limits          { MaxRequestBodySize = 4 MiB (long?; null = server default), MaxJsonDepth = 32 }
  Problems        { TypeBaseUri = null, IncludeExceptionDetails = null (= Development), UnavailableRetryAfter = null }
  RemoveServerHeader = true
```

Registers: problem details (D1), exception handler, `AddSharedKernelAuthorization()` (D3), CORS policy when origins
are configured (D7), the 429 body (D8), Kestrel (`AddServerHeader = false`, `Limits.MaxRequestBodySize`), JSON
`MaxDepth` on both the minimal-API and MVC `JsonOptions`, HSTS options.

`UseSharedKernelWebApi()` order: correlation → security headers → exception handler → status code pages →
HSTS (not in Development) → `UseRouting` → CORS (when configured) → `UseAuthentication` (only when an
authentication scheme provider is registered) → `UseAuthorization` → `UseRateLimiter` (only when rate limiting is
configured). Services call it first, then map endpoints; custom middleware goes after it.

## D5. Correlation id (B11, F7)

- The middleware becomes internal and is applied by `UseSharedKernelWebApi()`. Valid inbound
  `WellKnownHeaders.CorrelationId` → used; missing/invalid → `Activity.Current?.TraceId` hex, else `Guid` "N" (one id
  for logs and traces). Baggage `WellKnownBaggageKeys.CorrelationId` and the response header as today. The default
  pattern uses `[GeneratedRegex]`; a custom pattern is compiled once.
- Accessor: `HttpContext.GetCorrelationId()` (root namespace, `string?`). The public `ItemsKey` and forwarding
  constants go.
- One middleware covers REST, SignalR (negotiate/connect) and gRPC — they share the pipeline. No protocol-specific
  correlation code remains.

## D6. Security headers and request limits (R5, R6, F8)

- Internal middleware writes the configured headers at `OnStarting` unless the header is already set. Endpoint
  convention `WithContentSecurityPolicy(string? policy)` (root namespace; null omits CSP for that endpoint) — used
  by the OpenAPI add-on's UI endpoints.
- HSTS: the framework's `UseHsts()`/`AddHsts` (HTTPS only, localhost excluded), never in Development.
- Kestrel: `AddServerHeader = false`; global `MaxRequestBodySize`. Per endpoint: `WithRequestSizeLimit(long bytes)`
  and `DisableRequestSizeLimit()` conventions attaching the framework's `RequestSizeLimitAttribute` /
  `DisableRequestSizeLimitAttribute` metadata (enforced by routing since .NET 8); MVC uses the attributes directly.
  413 → ProblemDetails `request.too_large` (D1).
- Deleted: `SecurityHeadersOptions` family, `CspBuilder`, `PayloadLimits/*`.

## D7. CORS (R11)

- `WebApiOptions.Cors`, configuration-bound. A single named policy is registered only when `AllowedOrigins` is
  non-empty (deny by default) and applied globally by `UseSharedKernelWebApi()` (covers hubs too).
- Startup validation fails `AllowCredentials` with empty or `*` origins.
- `ExposedHeaders` default: `X-Correlation-Id`, `ETag`, `Location`, `Retry-After`, `Sunset`, `Deprecation`, `Link`,
  `api-supported-versions`, `api-deprecated-versions`. Constants, never literals at call sites.
- Deleted: `AddSharedKernelCors`, `CorsPolicyOptions`. SK0032 stays.

## D8. 429 (R8)

- `IPostConfigureOptions<RateLimiterOptions>`: when `OnRejected` is null, set it to write 429 ProblemDetails
  (`rate_limit.exceeded`, `Retry-After` from the lease's `MetadataName.RetryAfter` when present) and log Warning
  (EventId 14005, endpoint display name).
- 13.ServiceDefaults' `AddSharedKernelRateLimiting` is unchanged (it already sets 429); its docs and recipe tests
  change to the automatic body.
- Deleted: public `RateLimitRejectionProblemDetails`.

## D9. Idempotency key (R7, F7)

- Header `WellKnownHeaders.IdempotencyKey` (added in wave 1).
- Minimal APIs: `RequireIdempotencyKey()` convention (root namespace) adds the metadata **and** the endpoint filter
  — no separate registration. MVC: `[RequireIdempotencyKey]` (namespace `…WebApi.Idempotency`) implements
  `IAsyncActionFilter`, so it applies itself, and doubles as the OpenAPI metadata.
- Valid key: after removing one pair of surrounding double quotes (the IETF draft sends a quoted structured-field
  string), 1–256 characters of visible ASCII (0x21–0x7E). Missing → 400 `idempotency.key_required`; malformed →
  400 `idempotency.key_invalid`. Warning log (EventId 14003) without the value.
- Accessor `HttpContext.GetIdempotencyKey()` → validated key or null.
- Deleted: `AddSharedKernelIdempotencyFilters`, `TryGetIdempotencyKey`, the public filter type.

## D10. Conditional requests (B13, F6)

- `RequireIfMatch()` convention (root) and `[RequireIfMatch]` MVC attribute (self-applying action filter): a missing
  `If-Match` → 428 `precondition.required`.
- `HttpContext.GetIfMatch()` → the first entity tag with quotes and `W/` removed, or null; handlers pass it to
  `EntityVersion.TryParse`.
- On an endpoint that requires `If-Match`, an `ErrorType.Conflict` failure (a `Result` or a thrown
  `ConflictException`) is reported as **412 Precondition Failed** (RFC 9110 §13.1.1), keeping its error code.
  `ErrorPresentation.GetStatusCode` applies this rule, so every path agrees.
- `ToOkWithETag(…)` returns `OkWithETag<T>` (public, `…WebApi.Http`): sets `ETag: "<version>"`; when the request's
  `If-None-Match` matches (weak comparison, RFC 9110 §13.1.2) it answers 304 with the ETag and no body. It implements
  `IEndpointMetadataProvider` (200 with `T`, 304).
- `HttpResponse.SetETag(string version)` helper (root).
- Deleted: `RowVersionETag`, `TryValidateIfMatch`.

## D11. OpenAPI add-on — `SharedKernel.Presentation.OpenApi` (R2, R3, F4)

- `builder.AddSharedKernelOpenApi(Action<SharedKernelOpenApiOptions>? configure = null)`, options bound from
  `SharedKernel:Presentation:OpenApi`: `Title` (default: application name), `Description`, `Versioning`
  (`Action<ApiVersioningOptions>` for defaults and sunset/deprecation policies), `Bearer` (true),
  `ApiKeyHeaderName` (null = off), `MutualTls` (false; `SecuritySchemeType.MutualTLS`, OpenAPI 3.1),
  `ExposeInProduction` (false).
- Registration: `AddApiVersioning` (default version 1.0, assume-default, report versions, reader = URL segment +
  `X-Api-Version` header) → `AddApiExplorer` (`'v'VVV`, substitute version in URL) → Asp.Versioning `AddOpenApi(…)`
  with the platform transformers. No `BuildServiceProvider`.
- Transformers:
  - Document: title, description, version; security-scheme components.
  - Operation:
    - Endpoints with authorization metadata (`IAuthorizeData`, no `IAllowAnonymous`) get a security requirement
      (OR across enabled schemes) plus 401/403 responses.
    - Every operation gets a `default` response `application/problem+json` referencing the `ProblemDetails` schema.
    - A required `Idempotency-Key` header parameter where D9 metadata is present.
    - A required `If-Match` header parameter plus 412/428 responses where D10 metadata is present.
  - Schema: a `ProblemDetails` component with the D1 members.
- `app.MapSharedKernelOpenApi()` → `MapOpenApi().WithDocumentPerVersion()` + Scalar with one document per version
  (`DescribeApiVersions()`), UI endpoints carry `WithContentSecurityPolicy(null)`. It returns one
  `IEndpointConventionBuilder` covering both, so `.RequirePermission("docs.read")` works, and maps nothing outside
  Development unless `ExposeInProduction`.
- Sunset/deprecation: Asp.Versioning policies (`Deprecation: @epoch`, `Sunset` HTTP-date, `Link` rel
  sunset/deprecation). The lifecycle middleware, options and startup filter are deleted, as are
  `MutualTlsSecurityScheme`, `OpenApiSecuritySchemesOptions`, `SharedKernelApiVersioningDefaults` (folded into
  options).
- Asp.Versioning.OpenApi reflects over Microsoft.AspNetCore.OpenApi internals; consumer-verify generates two versioned
  documents so an upgrade that breaks it fails CI.

## D12. SignalR (R9, B12, B16, F9)

- `builder.AddSharedKernelSignalR(Action<SharedKernelSignalROptions>? configure = null)` (bound from
  `SharedKernel:Presentation:SignalR`) → `AddSignalR`, `AddSharedKernelAuthorization()` (so the attributes and
  conventions work on hubs and hub methods natively), global filters: exception mapping, invocation rate limit.
  Returns `ISignalRServerBuilder`.
- Errors: `HubException` message `"{code}: {client message}"` via `ErrorPresentation` (localization + redaction);
  unknown → `"unexpected.exception: An unexpected error occurred."` (Development: the exception message). Hub
  methods may return `Result`/`Result<T>`: a failure throws that `HubException`; `Result<T>` success returns the
  value. No `MakeGenericMethod` (platform rule): use a non-generic interface on `Result<T>` if Primitives exposes one,
  else a cached compiled accessor per closed type. Server categories log at Error, client categories at Debug.
- Rate limit: `InvocationRateLimit { PermitLimit (null = off), Window = 1 s }` implemented with one
  `PartitionedRateLimiter` keyed by connection id (token-bucket partitions, single replenishment timer); rejection →
  `"rate_limit.exceeded: Too many requests."`.
- Accessors: `HubCallerContext.GetTenantId()` → `Guid?` (null when no provider or `Guid.Empty`),
  `HubCallerContext.GetCorrelationId()`. `HubGroupNaming.TenantGroup(Guid)` throws for `Guid.Empty`.
- Deleted: `RedisBackplaneExtensions` and the package reference, `SignalRCorsStartupDiagnostic`, the four
  `HubOptions` pins, `ArgumentValidators`/`MaxStringArgumentLength`, `TenantContextHubFilter`.

## D13. gRPC (R10, B11, F9)

- `builder.AddSharedKernelGrpc(Action<SharedKernelGrpcOptions>? configure = null)` (bound from
  `SharedKernel:Presentation:Grpc`; `ErrorDomain`, default = application name) → `AddGrpc` with the exception
  interceptor + `AddSharedKernelAuthorization()`. Returns `IGrpcServerBuilder`.
- `GrpcStatusCodeMap`: add `Unavailable → StatusCode.Unavailable`, `Timeout → StatusCode.DeadlineExceeded`.
- Rich status (Grpc.StatusProto) built by one internal factory used by the interceptor and the Result extensions:
  - `Google.Rpc.Status`: code; message = the client message.
  - Detail `ErrorInfo`: `Reason` = error code, `Domain` = `ErrorDomain`, `Metadata` = `traceId`, `correlationId`.
  - Detail `BadRequest` with one `FieldViolation` per `Error.Details` entry (field path or code, client message, and
    the code as `Reason` when the proto has that field).
  - Thrown via `ToRpcException()`.
- Interceptor:
  - `RpcException` passes through unchanged.
  - Client cancellation → `StatusCode.Cancelled`, no error log.
  - `ValidationException` → `InvalidArgument` with **all** field violations.
  - `SharedKernelException` → its mapped status.
  - Unknown → `Internal`, generic text outside Development.
  - Server categories log at Error, client categories at Debug.
- Result extensions (root namespace `SharedKernel.Presentation.Grpc`): `ThrowIfFailure(this Result)`,
  `GetValueOrThrow<T>(this Result<T>)` and their `Task` overloads. `ToGrpcResult` is deleted. SK0036 unchanged.
- Correlation and tenant in services: `context.GetHttpContext().GetCorrelationId()`; inject
  `ITenantProvider`/`IRequestContext`.
- Deleted: correlation, tenant and authorization interceptors; the `MaxReceiveMessageSize` pin.
- 16.Testing `TestServerCallContext` uses `WellKnownHeaders.CorrelationId`; its self-tests follow.

## D14. `ErrorType.Unavailable` / `Timeout` (wave 1, F5)

- 01.Core Primitives: `Unavailable = 8`, `Timeout = 9` (appended — 17.Workflows persists the number);
  `Error.Unavailable`/`Error.Timeout` factories; `ErrorCodes.Unavailable.Default`, `ErrorCodes.Timeout.Default`;
  `WellKnownHeaders.IdempotencyKey`.
- 07 `messaging.unavailable`, 08 `storage.unavailable`, 09 `search.unreachable` → `Unavailable`;
  09 `search.timeout` → `Timeout`. Codes unchanged.
- 11.Communication.Rest reads them back: 412 → Conflict, 413/415/428 → Validation, 429/503 → Unavailable,
  504 → Timeout.
- 17.Workflows: `Unexpected`/`Unavailable`/`Timeout` are retryable; the reverse map gains `Forbidden` (previously
  lost), `Unavailable`, `Timeout`.
- Follow-ups outside this pass: 06's classifier (statement timeouts stay transient `Conflict`), outage errors in
  10/15/19, 02's `DistributedLockUnavailableException`, reading gRPC rich status back in 11.Communication.Grpc.

## D15. Deleted (summary)

WebApi: `Uploads/*`, `PayloadLimits/*`, `Versioning/*` and `OpenApi/*` (moved to the add-on, lifecycle and mTLS hack
deleted), `SecurityHeaders*` + `CspBuilder`, `AuthorizationRequirementEndpointFilter` + old extensions, the
idempotency filter registration shape, public `RateLimitRejectionProblemDetails`, `RowVersionETag`,
`ConditionalRequestExtensions.TryValidateIfMatch`, `ToProblemDetailsResult`, `Http/ProblemDetailsShaping`,
public `ValidationProblemDetailsExtensions`, `Cors/*` (reworked).
SignalR: see D12. gRPC: see D13.

## D16. Tests, integration, docs

- Tests (in addition to replacing tests of changed code):
  - One-shape test across every source: `Result` failure, thrown `SharedKernelException`, unknown exception,
    routing 404/405, 415, 401/403/step-up, 413, 428/412, 429, Asp.Versioning's unsupported version. Each must be
    `application/problem+json` with the D1 member set.
  - Authorization matrix on a minimal API endpoint, an MVC action, a hub method and a gRPC method: anonymous → 401;
    missing permission → 403; OR within an attribute and AND across attributes; freshness and authentication method
    → 401 with the RFC 9470 challenge.
  - Localization on the `Result` path; redaction outside Development and exposure in Development.
  - 503/504 and `Retry-After`; `If-Match` 412/428; ETag + 304.
  - CORS: exposed headers and startup validation. Security headers and the CSP opt-out; `Server` header off.
  - Request-size limit with a per-endpoint override (413).
  - Correlation id: trace-id fallback, invalid value replaced, accessor.
  - gRPC: rich status (`ErrorInfo`, `BadRequest` with all violations), cancellation, Result extensions.
  - SignalR: code-prefixed errors, `Result` hub methods, partitioned limiter.
  - OpenAPI: per-version documents, security only on protected operations, default problem response, required
    headers, docs not mapped in Production by default.
  - A regression test for every B finding.
- Samples: all five move to `AddSharedKernelWebApi`/`UseSharedKernelWebApi` and typed results
  (`AddSharedKernelOpenApi` where it helps). BillingApi's hand-written ETag/428/412 is replaced by `RequireIfMatch()`
  and `ToOkWithETag`. Their tests follow.
- consumer-verify is rebuilt on the one-call path against packed packages, including versioned document generation.
- 13.ServiceDefaults: the rate-limit docs and `RateLimitRejectionRecipeTests` change to the automatic 429.
  00.Governance: `PresentationLayeringRules` and tests are adjusted (new OpenApi package; SignalR now references
  WebApi); SK0032/SK0036 are unchanged.
- `Directory.Packages.props`:
  - Asp.Versioning.Http 10.2.3 and Asp.Versioning.Mvc.ApiExplorer 10.2.1.
  - Add Asp.Versioning.OpenApi 10.2.3 (requires ApiExplorer 10.2.1, Microsoft.AspNetCore.OpenApi ≥ 10.0.10 and
    Microsoft.OpenApi [2.7.5, 3.0.0)).
  - Add Grpc.StatusProto 2.80.0 (Google.Api.CommonProtos 2.16.0).
  - Scalar.AspNetCore 2.17.8.
  - Remove Microsoft.AspNetCore.SignalR.StackExchangeRedis.

  The `.slnx` gains the OpenApi project and its tests.
- Final wave: domain `README.md` (10-minute path), package READMEs (new OpenApi one), `CONFIGURATION.md`, domain
  `CLAUDE.md` rewritten to rules (history → changelog), root `CLAUDE.md` rows, these records + `waves.md`.

## Waves

| Wave | Scope | Owns |
| --- | --- | --- |
| 1 | D14 cross-domain `ErrorType` | 01.Core, 07, 08, 09, 11.Rest, 17 + tests (never 14) |
| 2 | WebApi core: D1–D10 | `14.Presentation/SharedKernel.Presentation.WebApi/**` |
| 3 | Three parallel streams in `C:\wt` worktrees: **O** OpenApi add-on (D11, new project + slnx), **S** SignalR (D12), **G** gRPC (D13 + 16.Testing gRPC helper) | each its own package folder |
| 4 | Integration: samples, consumer-verify, 13 rate-limit docs/tests, 00 governance, full solution build + tests | as listed |
| Review | Read-only correctness, security and DX reviewers | — |
| 5 | Remediation of review findings | as assigned |
| Docs | Brains, READMEs, root `CLAUDE.md`, changelog, these records | docs only |

`Directory.Packages.props` changes are made by the orchestrator before wave 3.

## Engineering rules for every wave

- Read root `CLAUDE.md` and `14.Presentation/CLAUDE.md` ("Implementation Rules", "Test Rules") for conventions:
  - `[LoggerMessage]` with explicit EventIds (D0 sub-blocks).
  - Magic strings as constants (SK0022).
  - XML docs on every public member.
  - PublicAPI tracking is build-breaking.
  - `AddValidatedOptions` + `ISectionBoundOptions`.
  - No `IsAotCompatible`.
- Build output is Turkish: judge by exit codes and `grep -E "error (CS|MSB|RS)"`.
- Use the Edit tool for structural edits; never `perl -i`.
- Do NOT commit (the orchestrator commits). Touch only the files your wave owns.
- Tests follow the existing style in each test project. Delete tests of removed features, replace tests of changed
  code, and add a regression test for every B finding your wave fixes.
- End each wave with the touched projects building green and their tests green (container suites in isolation if
  they are flaky). Write a log file `scratchpad/p562-<wave>-log.md` listing what changed, deviations and open items.
