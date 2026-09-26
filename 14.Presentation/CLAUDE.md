# 14.Presentation — API Surface Layer

## What This Domain Is

The inbound API surface of a service: HTTP (Minimal API/MVC), gRPC server, SignalR and GraphQL.
Every service's ProblemDetails shape, API versioning, OpenAPI, declarative endpoint authorization,
hub filters and server-side gRPC conventions derive from the types defined here. This domain is
framework glue, not business logic: it translates the platform's outcomes (`Result<T>`, `Error`,
`SharedKernelException`) into the wire formats clients see, and never produces those outcomes itself.
Outbound calling (typed REST/gRPC clients) is `11.Communication`.

Philosophy: **Thin. Convention-over-configuration. RFC-compliant. AOT-preferred where free (native
OpenAPI over reflection-heavy generators).**

User-facing docs: [`README.md`](README.md) (domain overview, canonical pipeline),
[`CONFIGURATION.md`](CONFIGURATION.md) (every option and default), each package `README.md`.
History: [`state-map.md`](state-map.md).

---

## Packages

Every package is **Host** tier (`<SharedKernelTier>Host</SharedKernelTier>`): referenced only by a
service's Api/host project or another Host package, never by Foundation/Model/Abstractions/Adapter
code. The build enforces the tier rules (SKTIER001–006, `eng/SharedKernelTiers.targets`).

| Package | Role | ProjectReferences (SharedKernel) + third-party |
| --- | --- | --- |
| `SharedKernel.Presentation.Core` | `[RequireRole]`/`[RequirePermission]`/`[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` (namespace `SharedKernel.Presentation.Authorization`); `ErrorTypeStatusCodeMap`/`GrpcStatusCodeMap` (namespace `SharedKernel.Presentation.Errors`). No ASP.NET Core reference. | Primitives; `Grpc.Core.Api` |
| `SharedKernel.Presentation.WebApi` | `Error`→`ProblemDetails` (localized, multi-field), `SharedKernelExceptionHandler`, `ResultHttpExtensions`, API versioning + RFC 8594 lifecycle headers, native OpenAPI + Scalar, endpoint filters (authorization, idempotency key, uploads), security headers, CORS, payload limits, ETag/`If-Match`, 429 bridge | Primitives, Core, Localization, Security.Abstractions, Presentation.Core; `Asp.Versioning.*`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` |
| `SharedKernel.Presentation.Grpc` | Server interceptors (exception, correlation, request-context scope, authorization), `GrpcResultExtensions`, `AddSharedKernelGrpc` | Primitives, Core, Execution, Security.Abstractions, Presentation.Core; `Grpc.AspNetCore` |
| `SharedKernel.Presentation.SignalR` | Hub filters (request-context scope, exception mapping, invocation rate limit), `HubGroupNaming`, `HubOptions` defaults, startup CORS diagnostic, `AddSharedKernelSignalR` | Primitives, Core, Execution |
| `SharedKernel.Presentation.SignalR.Redis` | `WithRedisBackplane` (namespace `SharedKernel.Presentation.SignalR.Extensions`) | Presentation.SignalR; `Microsoft.AspNetCore.SignalR.StackExchangeRedis` |
| `SharedKernel.Presentation.GraphQL` | HotChocolate conventions: `AddSharedKernelGraphQL`, `FilterBase<T>`/`SortBase<T>`, `PagedResponseType<T>`, internal filter convention and error filter | Primitives, Contracts; `HotChocolate.Data`, `HotChocolate.AspNetCore` |

All target `net10.0`, nullable and implicit usings on, `GenerateDocumentationFile` on (CS1591 and
unresolved `cref`s fail the build). Tests are nested in each package folder. None has an
`.Abstractions` sibling: they are distinct API surfaces, not interchangeable providers.

### Dependency decisions (locked by `OptionalDependencySatelliteRulesTests` and `PresentationLayeringRules`)

- **`.WebApi` and `.Grpc` never reference each other.** Both reference `.Core` for the attributes and
  status maps, so a service has one authorization dialect and a gRPC-only host carries no
  OpenAPI/versioning/Scalar packages. Trade-off accepted: WebApi hosts get `Grpc.Core.Api`
  transitively (it holds `StatusCode`). Move `GrpcStatusCodeMap` back into `.Grpc` only if that ever
  matters.
- **`.Core` stays ASP.NET-free** — `ErrorTypeStatusCodeMap` uses `System.Net.HttpStatusCode`, not
  `StatusCodes`.
- **`.SignalR` references neither `.WebApi` nor Redis.** CORS for hubs is a startup diagnostic, not
  a reference; the backplane is `.SignalR.Redis`.
- **`.Grpc` never references `SharedKernel.Contracts`** — protobuf messages are its wire contract
  (`PresentationLayeringRules.GrpcNeverReferencesContracts`). `.GraphQL` references Contracts for
  `PagedList<T>`.
- **No mediator, pipeline, persistence, messaging or caching reference** anywhere in this domain.
  Endpoints dispatch through the service's own `ISender` (`SharedKernel.Application`); this domain
  only maps the result.

---

## Technology Stack

| Concern | Technology | Version | Package |
| --- | --- | --- | --- |
| Error response shape | RFC 9457 `ProblemDetails` + `IExceptionHandler` (shared framework) | — | `.WebApi` |
| API versioning | `Asp.Versioning.Http` + `Asp.Versioning.Mvc.ApiExplorer` | 10.0.0 | `.WebApi` |
| OpenAPI document | `Microsoft.AspNetCore.OpenApi` (native; pinned for the `Microsoft.OpenApi` ≥ 2.7.5 security fix) | 10.0.11 | `.WebApi` |
| OpenAPI UI | `Scalar.AspNetCore` | 2.16.5 | `.WebApi` |
| gRPC status type | `Grpc.Core.Api` | 2.80.0 | `.Core` |
| gRPC server hosting | `Grpc.AspNetCore` | 2.80.0 | `.Grpc` |
| Real-time hub, rate limiting | `Microsoft.AspNetCore.SignalR`, `System.Threading.RateLimiting` (shared framework) | — | `.SignalR` |
| Scale-out backplane | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | 10.0.9 | `.SignalR.Redis` |
| GraphQL server | `HotChocolate.Data` + `HotChocolate.AspNetCore` (not AOT-safe) | 16.1.4 | `.GraphQL` |

Versions live in `Directory.Packages.props`. Swashbuckle/NSwag are never added. Resolve a security
advisory on a pinned dependency by moving to a patched version, never by `NoWarn`.

---

## Cross-cutting contracts this domain relies on

- **Caller context.** `IRequestContext` (`SharedKernel.Execution.Context`: `TenantId? TenantId`,
  `ActorKind`, `CorrelationId`, `ClientId`, `SessionId`) is registered by
  `SharedKernel.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()`. On HTTP,
  `app.UseSharedKernelRequestContext()` opens the request's `RequestContextScope` and owns the
  correlation id (reads/creates/echoes `X-Correlation-Id`; one rule, `CorrelationIds`: ≤ 128 chars of
  `[A-Za-z0-9-_:.]`). It is registered **first**, before `UseExceptionHandler()`. This domain has no
  correlation-id middleware or options of its own.
- **Canonical HTTP order:** `UseSharedKernelRequestContext()` → `UseSharedKernelSecurityHeaders()` →
  `UseExceptionHandler()` → `UseAuthentication()` → (tenant resolution) → `UseAuthorization()` →
  endpoints.
- **Identity for authorization** is `IUserContext` (`SharedKernel.Security.Abstractions`), populated by
  whichever authentication package the service uses. Tenant ids are `TenantId`
  (`SharedKernel.Execution.Tenancy`), never a raw `Guid` inside `IRequestContext`.
- **Header names** come from `WellKnownHeaders` (`SharedKernel.Primitives.Propagation`):
  `CorrelationId`, `IdempotencyKey` (`"Idempotency-Key"`, also what `Communication.Rest` sends).

---

## Implementation Rules

### ProblemDetails and `Result<T>` (WebApi)

- `Error.ToProblemDetails()` is the only way to turn an `Error` into an HTTP error body;
  `ResultHttpExtensions` (`ToProblemDetailsResult` for Minimal API, `ToActionResult` for MVC) the only
  `Result<T>`→HTTP conversion. Inline `ProblemDetails` construction or `IsSuccess` branching before an
  HTTP result is a violation (enforced by `PresentationLayeringRules`). There is no response envelope;
  `Communication.Rest`'s `ReadResultAsync<T>` reads this shape back.
- Body: `Title` = `error.Code`, `Detail` = localized or `error.Message` (never blank), `Status` from
  `ErrorTypeStatusCodeMap`, `Type` = RFC 9457 URI, `Extensions["errorCode"]`, `Extensions["traceId"]`
  = `Activity.Current?.Id ?? TraceIdentifier`.
- Multi-field errors (`ValidationException`, or an `Error` with non-empty `Details`) add
  `Extensions["errors"]` and index-aligned `Extensions["errorCodes"]`, keyed by
  `ErrorArgumentNames.PropertyPath` else code — the same helper for the exception path and the
  `Result` path, so both are byte-identical.
- Localization: `ILocalizationCatalog` resolved with `context?.RequestServices?.GetService<>()` (both
  `?.` matter); only `Detail` (and per-field messages) change; culture is read from
  `CurrentUICulture`, never set here.
- `SharedKernelExceptionHandler` never leaks exception detail outside `IsDevelopment()`; a
  `BadHttpRequestException` keeps its own status (413 for body too large) instead of 500.
- HTTP-native outcomes that never were an `Error` (412 `If-Match`, 429 rate limit) are built by the
  internal `Http/ProblemDetailsShaping` helper — never a new `ErrorType`. `RateLimitRejectionProblemDetails`
  is the only sanctioned 429 body and sets a real `Retry-After` header.

### Status maps (Core)

- `ErrorTypeStatusCodeMap` (HTTP) and `GrpcStatusCodeMap` (gRPC) are siblings, never merged; each is
  the single source of truth for its protocol. Mappings: Validation 400/`InvalidArgument`,
  Unauthorized 401/`Unauthenticated`, Forbidden 403/`PermissionDenied`, NotFound 404/`NotFound`,
  Conflict 409/`Aborted`, BusinessRule 422/`FailedPrecondition`, Unexpected 500/`Internal`, anything
  else 500/`Unknown`. 401 and 403 are never substituted for each other.

### Declarative authorization (Core attributes, WebApi filter, Grpc interceptor)

- Always evaluated through `IUserContext.HasRole`/`HasPermission`/`IsAuthenticationFresherThan`/
  `WasAuthenticatedWith` — never `ClaimTypes.Role`, raw claims, or `[Authorize(Roles = ...)]`, which
  bypasses the authentication package's `IUserContextMapper`.
- Composition is fixed: OR within one attribute's list, AND across stacked attributes and attribute
  kinds. No configurable mode.
- One evaluator per protocol: `AuthorizationRequirementEndpointFilter` (HTTP) and
  `GrpcAuthorizationInterceptor` (gRPC). Both no-op — resolving nothing — when no attribute is
  present; `IClock` is resolved only when `[RequireFreshAuthentication]` is present. Anonymous callers
  fail through the ordinary false path; there is no `IsAuthenticated` branch.
- The HTTP filter cannot self-attach: `AddSharedKernelAuthorizationFilters()` plus
  `.AddEndpointFilter<AuthorizationRequirementEndpointFilter>()` on `MapControllers()`/route groups.
  Say so plainly in docs. The gRPC interceptor is global.
- Rejection is `Error.Forbidden` → 403 `ProblemDetails` / `PermissionDenied`, never a bare 403. It
  complements `SharedKernel.Application.Pipeline`'s `AuthorizationBehavior`; it does not replace it.

### Other WebApi filters and middleware

- `IdempotencyKeyRequirementEndpointFilter` and `UploadValidationEndpointFilter` are separate filters
  from the authorization filter, same shape (global, no-op without their attribute, explicit
  `.AddEndpointFilter<>()`). A missing/malformed key is `Error.Validation` → 400. The key is never
  auto-bound to `IIdempotentRequest.IdempotencyKey` — the handler does that before `ISender.Send`.
- Upload validation is a boundary-shape check only — NEVER virus/malware scanning; no third-party MIME
  library. Per-endpoint attribute values override `UploadValidationOptions`.
- Security headers: HSTS on by default (with a loud local-dev warning); no default CSP; never
  overwrite a header already set.
- CORS: `AllowCredentials` with empty/wildcard origins fails at `IHost.StartAsync()`
  (`ValidateOnStart`); reference `CorsPolicyNames.Default`, never the literal.
- Payload limits: `MaxJsonDepth` into both Minimal API and MVC `JsonOptions`; guard
  `IHttpMaxRequestBodySizeFeature.IsReadOnly`. Opt-in.
- Versioning: `AddSharedKernelApiVersioning` before `AddSharedKernelOpenApi`;
  `AssumeDefaultVersionWhenUnspecified = true`; URL segment primary, `X-Api-Version` secondary. Sunset
  is an RFC 1123 date; Deprecation comes from Asp.Versioning's own `Deprecated`; `Link:
  rel="successor-version"` only with a sunset date. Driving Asp.Versioning's own sunset policies
  instead of `ApiVersionLifecycleOptions` is an open simplification.
- OpenAPI: one document per API version; each active scheme (Bearer/ApiKey/mTLS) is its own OR'd
  security requirement. `Microsoft.OpenApi` 2.x types live in `Microsoft.OpenApi` (not `.Models`),
  has no `mutualTLS` enum member (hence `MutualTlsSecurityScheme`), and requires
  `document.RegisterComponents()` before building scheme references. `ApiKeyHeaderName` stays a
  string — no reference to `Security.ApiKey`.

### SignalR

- Filters are global (`HubOptions.AddFilter<T>()`), never `[HubFilter]` attributes.
  `TenantContextHubFilter` captures the connect request's `IRequestContext` (ambient via
  `IRequestContextAccessor`, else DI) and opens a `RequestContextScope` around connect, every
  invocation and disconnect. It stores no tenant item and never rejects. It sets no correlation id of
  its own — the connect request's context carries it.
- `HubExceptionMappingFilter` lets only `HubException` cross the boundary and rethrows an existing
  `HubException` unchanged (checked first) — any filter that throws its own `HubException` relies on
  this; test it with the real two-filter pipeline.
- `HubInvocationRateLimitFilter` is always registered and a no-op until `configureRateLimit` sets
  limits; one limiter per connection.
- `HubOptions` defaults (32 KB, 1 parallel invocation, 30 s timeout, 15 s keep-alive) apply before
  `configureHubOptions`, so callers can override.
- `HubGroupNaming.TenantGroup(Guid)` (`"tenant:{id:D}"`) is the only group-name formatter.
- The backplane never shares `Caching.Redis.Core`'s `IConnectionMultiplexer`, and is not
  `Caching.Redis.PubSub` (client fan-out vs. service signals) — never merge the two.

### gRPC

- `GrpcExceptionInterceptor` overrides all four server handlers (unary + three streaming shapes) and
  suppresses unknown-exception detail outside `IsDevelopment()`.
- `GrpcCorrelationInterceptor`: ambient correlation id if present, else `X-Correlation-Id` metadata via
  `CorrelationIds.AcceptOrCreate`. `GrpcTenantContextInterceptor`: opens a `RequestContextScope` with
  ambient → DI → `AnonymousRequestContext`, carrying that correlation id. It never reads a tenant from
  metadata — the tenant comes from the authenticated caller.
- `GrpcResultExtensions.ToGrpcResult()` is the only `Result`→gRPC conversion; failures travel as the
  `RpcException` status, never a DTO.
- `MaxReceiveMessageSize` default 4 MiB, applied before `configure`. No `WithXTelemetry` entry point —
  ASP.NET Core's server instrumentation covers gRPC.

### GraphQL

- `AddSharedKernelGraphQL` is called before any service `AddGraphQL()`/`AddTypes()`; idempotent;
  `MaxPageSize` 1..500 validated at the call. Services extend `FilterBase<T>`/`SortBase<T>`, never
  `FilterInputType<T>`/`SortInputType<T>`, and return `PagedResponseType<T>.FromPagedList(...)`
  instead of unpacking pages by hand.

### Logging

- `[LoggerMessage]` only, explicit `EventId = LoggingEventIdRanges.Presentation + n`. Sub-blocks:
  `.WebApi` 14000–14099, `.SignalR` 14100–14199, `.Grpc` 14200–14299 (`.Core`, `.SignalR.Redis`,
  `.GraphQL` do not log).
- In use: 14001 `SharedKernelExceptionHandler`, 14002 authorization filter, 14003 idempotency filter,
  14004 CORS validator, 14005 rate-limit bridge; 14100 `HubExceptionMappingFilter`, 14102 SignalR CORS
  diagnostic (14101 reserved for the rate-limit filter); 14200 `GrpcExceptionInterceptor`, 14201
  `GrpcAuthorizationInterceptor`. Retired, never reuse: 14000, 14006 (the deleted correlation-id
  middleware).
- Never log tokens, keys, certificate bytes or claim dumps. A type that gains a logger takes it as an
  optional `ILogger<T>? logger = null` (fallback `NullLogger<T>.Instance`) so it still constructs in a
  bare container.

---

## Test Rules

- Tests are nested per package. `SharedKernel.Testing` (core fakes: `InMemoryLogger`,
  `TestRequestContext`/`FakeRequestContext`) and `SharedKernel.Security.Testing` (`FakeUserContext`) are
  the helpers; `.SignalR.Redis.Tests` uses `SharedKernel.Testing.Internal`'s Redis container fixture
  (Integration lane).
- `EventId`s are pinned by reflection over the `LoggerMessageAttribute` (`LoggerMessageEventIdTests`),
  never by triggering the log call.
- Endpoint filters without a host: `EndpointFilterInvocationContext.Create(HttpContext)` and
  `httpContext.SetEndpoint(new Endpoint(..., new EndpointMetadataCollection(metadata), ...))`; prove
  "resolves nothing" with an empty `RequestServices`; prove "no `IsAuthenticated` branch" with
  `IsAuthenticatedGuardUserContext`.
- gRPC tests run a real HTTP/2 `TestServer` host (`GrpcTestWebApplicationFactory`), never a mocked
  `ServerCallContext`; the round trip uses the real `SharedKernel.Communication.Grpc` client
  interceptors (test-only reference) with `HeaderRequestContext` supplying the caller.
- The 413 path needs a real listening Kestrel host and a chunked body (`TestServer` does not enforce
  the body-size feature; a `Content-Length` body is rejected before middleware runs).
- `MapSharedKernelOpenApi` needs a `WebApplication`: override `WebApplicationFactory<T>.CreateHost`.
- Internal types (`CorsPolicyOptionsValidator`, `SignalRCorsStartupDiagnostic`) are tested through a
  real host with `AddInMemoryLoggerFactory()` and a hard-coded category name.
- `consumer-verify` composes every package (WebApi stack, SignalR with and without the backplane, a
  gRPC host without WebApi, GraphQL) and must stay green with zero `NoWarn` overrides.

---

## DI Registration (shape — see package READMEs for full examples)

```csharp
// HTTP host
builder.Services.AddSharedKernelRequestContext();               // SharedKernel.ServiceDefaults.Security
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
builder.Services.AddSharedKernelAuthorizationFilters();
builder.Services.AddSharedKernelIdempotencyFilters();
builder.Services.AddSharedKernelApiVersioning();
builder.Services.AddSharedKernelOpenApi(title: "Orders API");

app.UseSharedKernelRequestContext();
app.UseSharedKernelSecurityHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/v{version:apiVersion}/payments")
   .AddEndpointFilter<AuthorizationRequirementEndpointFilter>()
   .AddEndpointFilter<IdempotencyKeyRequirementEndpointFilter>()
   .MapPost("/", CreatePayment)
   .RequirePermission("payments:write")
   .RequireFreshAuthentication(maxAgeSeconds: 300)
   .RequireIdempotencyKey();
app.MapSharedKernelOpenApi();

// gRPC host
builder.Services.AddSharedKernelGrpc();                          // interceptors are global
app.MapGrpcService<OrdersService>();                             // [RequireRole] etc. on methods

// SignalR host (multi-replica)
builder.Services.AddSharedKernelSignalR(configureRateLimit: o => { o.PermitLimit = 20; o.Window = TimeSpan.FromSeconds(10); })
       .WithRedisBackplane(connectionString);                    // SharedKernel.Presentation.SignalR.Redis
app.MapHub<OrdersHub>("/hubs/orders").RequireCors(CorsPolicyNames.Default);

// GraphQL
builder.Services.AddSharedKernelGraphQL(o => o.AllowIntrospection = builder.Environment.IsDevelopment())
       .AddQueryType<QueryType>();
```
