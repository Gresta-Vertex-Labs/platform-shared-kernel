# 14.Presentation — API Surface Layer

## What This Domain Is

The HTTP and real-time API surface layer. Every microservice's REST/Minimal API conventions (ProblemDetails error shape, API versioning, OpenAPI documentation) and SignalR real-time conventions (hub filters, Redis scale-out backplane) derive from the types defined here. This domain is framework-glue, not business logic — it translates between the platform's core primitives (`Result<T>`, `Error`, `01.Core`) and the HTTP/SignalR wire formats clients actually see.

Philosophy: **Thin. Convention-over-configuration. RFC-compliant. AOT-Preferred (native OpenAPI over reflection-heavy generators).**

> **Layering boundary:** Per root `CLAUDE.md`, `14.Presentation` may reference `01.Core`, `04.Contracts`, `12.Security`, and `13.ServiceDefaults` only. It must never reference `05.Application`, `06.Persistence`, `07.Messaging`, or any other infrastructure layer directly — MediatR dispatch, repository calls, and message publishing are the hosting microservice's concern, not this domain's. This domain converts *outcomes* (`Result<T>`, `Error`, exceptions) into HTTP/SignalR responses; it never produces those outcomes itself.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Presentation.WebApi` | RFC 9457 `ProblemDetails` error mapping, global `IExceptionHandler`, API versioning (`Asp.Versioning`), native OpenAPI document generation + Scalar interactive UI, inbound correlation-id middleware, `Result<T>` → `IResult`/`ActionResult` HTTP-boundary extensions | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Contracts`, `SharedKernel.Security.Abstractions`, `Asp.Versioning.Http`, `Asp.Versioning.Mvc.ApiExplorer`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` |
| `SharedKernel.Presentation.SignalR` | `IHubFilter` implementations (tenant context attachment, exception-to-`HubException` mapping), Redis-backed scale-out backplane wiring, tenant-group naming convention, `AddSharedKernelSignalR` DI builder | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Security.Abstractions`, `Microsoft.AspNetCore.SignalR.StackExchangeRedis` |

Both packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`). Neither package has an `.Abstractions` sibling — unlike `02.Caching`/`06.Persistence`, the two packages are not interchangeable providers of one capability; they are distinct API surfaces (HTTP vs. real-time) that happen to share the same domain folder.

---

## Technology Stack

| Concern | Technology | Pinned Version | Owning Package |
| --- | --- | --- | --- |
| Error response shape | RFC 9457 `ProblemDetails` (`Microsoft.AspNetCore.Http`, built-in) | shared framework | `.WebApi` |
| Global exception-to-response pipeline | `IExceptionHandler` (ASP.NET Core 8+, built-in) + `AddProblemDetails()` | shared framework | `.WebApi` |
| API versioning | `Asp.Versioning.Http` + `Asp.Versioning.Mvc.ApiExplorer` | `10.0.0` | `.WebApi` |
| OpenAPI document generation | `Microsoft.AspNetCore.OpenApi` — native, source-gen-friendly, ships in the `net10.0` SDK | `10.0.9` | `.WebApi` |
| OpenAPI interactive UI | `Scalar.AspNetCore` | `2.16.5` | `.WebApi` |
| Correlation-id propagation (inbound) | `System.Diagnostics.Activity` (BCL) — no new dependency | shared framework | `.WebApi` |
| Real-time hub | `Microsoft.AspNetCore.SignalR` (built-in) | shared framework | `.SignalR` |
| Real-time scale-out backplane | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `10.0.9` | `.SignalR` |
| Hub pipeline extensibility | `IHubFilter` (built-in, .NET 7+) | shared framework | `.SignalR` |

> Versions pinned at Scaffold (SK.14.Scaffold, 2026-06-25). All three NuGet packages confirmed compatible with `net10.0` at pin time via direct NuGet flat-container version listing (not assumed from documentation). Re-verify AOT status on any future version bump per the AOT notes below — none of the three are BCL.
>
> **Note:** Swashbuckle/NSwag are deliberately not used. `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` is the .NET 9/10-idiomatic pairing — Scalar renders the native OpenAPI document directly, avoiding Swashbuckle's reflection-heavy assembly-scanning generation pipeline. This is a better fit for the root brain's AOT-preferred guidance than the Swashbuckle stack.

### Why SignalR's Redis backplane is distinct from `02.Caching.Redis.PubSub`

Both rely on `StackExchange.Redis` Pub/Sub under the hood, but they solve unrelated problems and must never be merged or treated as interchangeable:

- **`SharedKernel.Caching.Redis.PubSub`** (`02.Caching`) propagates *cache invalidation signals* between service instances — its payload is a `CacheInvalidationMessage`, and its consumer is `ICacheService`.
- **SignalR's Redis backplane** (`14.Presentation.SignalR`) fans out *real-time client messages* (`Hub.Clients.All.SendAsync(...)`, group broadcasts) across all pods serving the same Hub — its payload is whatever the Hub sends, and its consumer is connected WebSocket/SSE clients.

A microservice may legitimately depend on both packages simultaneously for entirely different reasons. Neither package references the other.

---

## Interface Contracts

> **Status: Design-locked (WO-031, P-192).** Every contract below is the confirmed, signed-off public API shape for both packages — not a draft. Implementation (Scaffold/Core) follows these signatures exactly; any deviation discovered during Core-phase implementation must come back through a Design amendment, not a silent change.

### `SharedKernel.Presentation.WebApi` — confirmed public surface

#### Error → ProblemDetails mapping (`Errors/`)

```text
ErrorTypeStatusCodeMap  (static class)
    .Resolve(ErrorType type) → int   (HTTP status code)
    NOTE: Validation → 400, Unauthorized → 401, Forbidden → 403, NotFound → 404, Conflict → 409,
          Failure → 500. Any ErrorType not explicitly mapped falls back to 500.
          Single source of truth — inline switch statements duplicating this mapping anywhere
          else in a consuming service is a platform violation.

ErrorProblemDetailsExtensions  (static class)
    .ToProblemDetails(this Error error, HttpContext? context = null) → ProblemDetails
    NOTE: Title = error.Code; Detail = error.Description; Status = ErrorTypeStatusCodeMap.Resolve(error.Type);
          Type = RFC 9457 URI for the resolved status (e.g. "https://httpstatuses.io/404");
          Extensions["errorCode"] = error.Code; Extensions["traceId"] = Activity.Current?.Id
          ?? context?.TraceIdentifier. Pure mapping — no logging, no I/O.
```

#### `Result<T>` → HTTP boundary (`Results/`)

```text
ResultHttpExtensions  (static class)
    .ToProblemDetailsResult(this Result result)                                   → IResult
    .ToProblemDetailsResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) → IResult
    .ToActionResult(this Result result)                                           → ActionResult
    .ToActionResult<T>(this Result<T> result)                                     → ActionResult<T>
    NOTE: Failure path always routes through Error.ToProblemDetails() → Results.Problem(...) /
          ObjectResult(ProblemDetails) using the mapped status code. Success path defaults to
          Results.Ok(value) / new OkObjectResult(value) unless the caller supplies onSuccess
          (Minimal API overload only — controllers always use the default 200 OK shape).
          This is the canonical Result<T>→HTTP mapping; inline
          "if (result.IsSuccess) ... else ..." in endpoint or controller code is a platform
          violation, mirroring the WO-026 P-166/167 precedent for Result<T>→Envelope<T> mapping.
          Distinct purpose from SharedKernel.Contracts.Mapping.ResultEnvelopeExtensions (04.Contracts):
          Envelope<T> is a wire DTO for service-to-service payloads; ProblemDetails is the RFC 9457
          HTTP *error* response shape. A REST endpoint may combine both (Envelope<T> success body +
          ProblemDetails failure body) or use ProblemDetails alone — this package does not decide
          that policy, it only supplies the conversion primitives.
```

#### Global exception handling (`ExceptionHandling/`)

```text
SharedKernelExceptionHandler  (sealed class, implements IExceptionHandler)
    .TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct) → ValueTask<bool>
    NOTE: Registered via services.AddExceptionHandler<SharedKernelExceptionHandler>() +
          services.AddProblemDetails(). Known SharedKernelException subtypes (01.Core) that carry
          an Error are mapped via Error.ToProblemDetails(); unknown exceptions fall back to a
          generic 500 ProblemDetails with Detail suppressed outside IHostEnvironment.IsDevelopment().
          Logs the full exception at LogLevel.Error before writing the response. Always returns
          true — this is the terminal handler in the exception-handling chain. [LoggerMessage]
          EventId = LoggingEventIdRanges.Presentation + 1 (14001, WO-041 P-256).
```

#### API versioning (`Versioning/`)

```text
SharedKernelApiVersioningDefaults  (static class)
    .DefaultApiVersion   → ApiVersion (1.0)
    .ApiVersionReader    → IApiVersionReader
                            (UrlSegmentApiVersionReader combined with
                             HeaderApiVersionReader("X-Api-Version") via ApiVersionReader.Combine)
    NOTE: URL-segment versioning ("/v{version}/...") is primary; the header reader is a secondary
          override for clients that cannot template the URL.

AddSharedKernelApiVersioning(this IServiceCollection) → IServiceCollection
    NOTE: Wraps services.AddApiVersioning(...).AddApiExplorer(...) with the defaults above plus
          AssumeDefaultVersionWhenUnspecified = true, ReportApiVersions = true (adds
          api-supported-versions / api-deprecated-versions response headers), and
          GroupNameFormat = "'v'VVV" so ApiExplorer group names match the OpenAPI document grouping
          consumed by AddSharedKernelOpenApi.
```

#### OpenAPI + Scalar (`OpenApi/`)

```text
AddSharedKernelOpenApi(this IServiceCollection, string title, string? description = null)
    → IServiceCollection
    NOTE: Wraps the native services.AddOpenApi(...) (Microsoft.AspNetCore.OpenApi — no Swashbuckle)
          once per discovered API version group (sourced from IApiVersionDescriptionProvider when
          AddSharedKernelApiVersioning was called first; falls back to a single "v1" document
          otherwise). Registers a document transformer setting Info.Title/Description and adding
          the Bearer JWT security scheme by name (scheme metadata only — token validation logic
          stays in 12.Security.Oidc, never duplicated here).

MapSharedKernelOpenApi(this WebApplication app) → WebApplication
    NOTE: Calls app.MapOpenApi() per registered document, then app.MapScalarApiReference(...)
          (Scalar.AspNetCore) configured to list every discovered version. No Swagger UI mapping —
          see "Why Swashbuckle/NSwag are not used" above.
```

#### Correlation-id middleware (`Middleware/`)

```text
CorrelationIdMiddleware  (sealed class)
    .BaggageKey                                              → const string = "correlation.id"
        (WO-041, P-256, D-13/C-17). Public named constant for the Activity baggage key this
        middleware writes to — replaces the prior inline string literal. Exists so cross-domain
        consumers (13.ServiceDefaults's BaggageLogRecordProcessor test suite, any future consumer)
        reference the same compile-time value instead of hand-copying a literal that can silently
        drift out of sync (see the Logging EventId & Correlation Verification subsection below for
        the mismatch this closes).
    .InvokeAsync(HttpContext context, RequestDelegate next) → Task
    NOTE: Reads the "X-Correlation-Id" request header; generates Guid.NewGuid("N") when absent or
          whitespace. Stores the resolved value in HttpContext.Items["CorrelationId"] and calls
          Activity.Current?.SetBaggage(BaggageKey, value) so OTel spans and 11.Communication's
          outbound CorrelationId delegating handler can propagate the same identifier end-to-end.
          Always writes the resolved value back as a response header, including on early
          pipeline short-circuits — this requires the middleware to be registered first, before
          exception handling. [LoggerMessage] EventId = LoggingEventIdRanges.Presentation + 0
          (14000, WO-041 P-256).

AddSharedKernelCorrelationId(this IServiceCollection) / UseSharedKernelCorrelationId(this IApplicationBuilder)
    NOTE: Two-part registration matching the standard ASP.NET Core middleware convention.
          UseSharedKernelCorrelationId must be the first call in the pipeline.
```

### `SharedKernel.Presentation.SignalR` — confirmed public surface

#### Hub filters (`Filters/`)

```text
TenantContextHubFilter  (sealed class, implements IHubFilter)
    .OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next) → Task
    NOTE: Resolves ITenantProvider (12.Security.Abstractions) from the connection's HttpContext at
          connect time; stores TenantId in Context.Items["TenantId"] for the lifetime of the
          connection so hub methods read it without re-resolving per invocation.
          Does not reject connections with no tenant — that policy decision belongs to the
          consuming service's Hub (via [Authorize] or explicit checks); this filter only attaches data.

HubExceptionMappingFilter  (sealed class, implements IHubFilter)
    .InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
        → ValueTask<object?>
    NOTE: Wraps next(context) in try/catch. Known SharedKernelException subtypes (01.Core) are
          rethrown as HubException using Error.Description as the message — caller-safe, no stack
          trace, no internal type names. Unknown exceptions are logged at LogLevel.Error and
          rethrown as HubException("An unexpected error occurred.") — SignalR already redacts
          unhandled exception detail from clients by default; this filter is the explicit,
          auditable safety net rather than relying on that default silently. [LoggerMessage]
          EventId = LoggingEventIdRanges.Presentation + 100 (14100, WO-041 P-256).
```

#### Group naming convention (`GroupNaming/`)

```text
HubGroupNaming  (static class)
    .TenantGroup(Guid tenantId) → string
    NOTE: Format: "tenant:{tenantId:D}". Single source of truth for tenant-scoped SignalR groups —
          mirrors the 02.Caching ICacheKeyProvider discipline ("one formatter, many callers")
          applied to group names instead of cache keys.
```

#### DI extensions (`Extensions/`)

```text
AddSharedKernelSignalR(this IServiceCollection, Action<HubOptions>? configureHubOptions = null)
    → ISignalRServerBuilder   (the stock Microsoft.AspNetCore.SignalR builder — no custom wrapper type;
                               CORRECTED at Core-phase implementation: the actual stock type returned by
                               services.AddSignalR(...) in this SDK is ISignalRServerBuilder, not
                               ISignalRBuilder, which does not exist as a type in this package version)
    NOTE: Thin wrapper over services.AddSignalR(...) that also registers TenantContextHubFilter and
          HubExceptionMappingFilter as global filters via HubOptions.AddFilter<T>() (the
          HubOptionsExtensions.AddFilter<TFilter>() extension method). Both filters are additionally
          registered as singletons in DI so SignalR's filter resolution can satisfy their constructor
          dependencies (e.g. ILogger<T>). Both are opt-out via configureHubOptions if a consuming
          service needs different behavior.

WithRedisBackplane(this ISignalRServerBuilder builder, string connectionString,
                    Action<RedisOptions>? configure = null)
    → ISignalRServerBuilder
    NOTE: Thin wrapper over builder.AddStackExchangeRedis(connectionString, configure) — exists so
          consuming services have one discoverable, platform-named extension method instead of
          reaching for the underlying NuGet package's extension directly. No added behavior beyond
          pass-through. CORRECTED at Core-phase implementation: the options type shipped by
          Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.9 is
          Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisOptions, not StackExchangeRedisOptions
          — the latter type name does not exist in this package.
```

---

## Implementation Rules

### ProblemDetails rules

- `Error.ToProblemDetails()` is the only permitted way to convert an `Error` into an HTTP error body. Hand-rolled `ProblemDetails` construction inline in endpoint/controller code is a platform violation.
- `ErrorTypeStatusCodeMap.Resolve` is the single source of truth for `ErrorType` → HTTP status mapping. Do not duplicate this switch anywhere else.
- `SharedKernelExceptionHandler` must never leak exception messages or stack traces outside `IHostEnvironment.IsDevelopment()`.
- `ProblemDetails.Extensions["traceId"]` must always be populated when `Activity.Current` is non-null — this is the platform's primary "give support this ID" field surfaced to API consumers.

### `Result<T>` HTTP boundary rules

- `ResultHttpExtensions` are the only permitted `Result<T>` → HTTP conversion. They are conceptually parallel to, but functionally distinct from, `SharedKernel.Contracts.Mapping.ResultEnvelopeExtensions` (04.Contracts) — see the note in Interface Contracts above. Do not conflate the two; an endpoint is free to use either or both.
- The Minimal API overloads return `IResult`; the MVC overloads return `ActionResult`/`ActionResult<T>`. There is no third "auto-detect host model" overload — callers pick the form matching their hosting model explicitly.

### API versioning rules

- `AddSharedKernelApiVersioning` must be called before `AddSharedKernelOpenApi` when both are used — the OpenAPI extension reads `IApiVersionDescriptionProvider` to discover version groups.
- `AssumeDefaultVersionWhenUnspecified = true` is non-negotiable platform default — unversioned client requests must not 400 outright; they fall back to `DefaultApiVersion`.
- URL-segment versioning is primary. The header reader is additive, never a replacement.

### OpenAPI / Scalar rules

- Swashbuckle and NSwag must never be added as dependencies of this package — see "Why Swashbuckle/NSwag are not used" above. `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` is the only sanctioned combination.
- **AOT-discovered correction (Core phase):** `Microsoft.OpenApi` 2.0.0's model types (`OpenApiDocument`, `OpenApiComponents`, `OpenApiSecurityScheme`, `SecuritySchemeType`, `OpenApiInfo`, etc.) live directly under the `Microsoft.OpenApi` namespace, **not** `Microsoft.OpenApi.Models` — the latter namespace does not exist in this version and is a holdover from the pre-2.0 Swashbuckle-era API shape. Always verify the actual namespace via reflection against the installed package version rather than assuming from older documentation/training data.
- The Bearer security scheme registered by `AddSharedKernelOpenApi`'s document transformer is metadata only (for the "Authorize" button in Scalar's UI) — it performs no token validation. Token validation is exclusively `12.Security.Oidc`'s concern.
- One OpenAPI document per discovered API version. Do not collapse multiple versions into a single document with manual `[ApiExplorerSettings]` filtering — that defeats the purpose of version-grouped documents.

### Correlation-id rules

- `CorrelationIdMiddleware` must be registered first in the pipeline (before `UseExceptionHandler`) so correlation IDs are present even on error responses.
- The correlation ID value must flow into `Activity` baggage (`SetBaggage`, not `SetTag`) so it survives across process boundaries via W3C Baggage propagation, not just appear on the local span.
- This middleware handles the *inbound* side only. Outbound propagation to downstream services is `11.Communication.Rest`'s `CorrelationIdDelegatingHandler` — the two are designed to share the same `HttpContext.Items["CorrelationId"]` key/`Activity` baggage key so a request's correlation ID is continuous end-to-end without this domain depending on `11.Communication`.
- **Ownership (confirmed P-192, WO-031):** the `correlation.id` baggage key and the `HttpContext.Items["CorrelationId"]` storage key are `14.Presentation`'s own contract, defined and owned here — they are not borrowed from, nor do they require a `ProjectReference` on, `13.ServiceDefaults`. If `13.ServiceDefaults` wants to align its own OTel conventions with this key, that alignment flows from `13.ServiceDefaults` toward this domain's published contract, never the reverse.
- **The baggage key is a public constant, never a hand-copied literal (WO-041, P-256):** `CorrelationIdMiddleware.BaggageKey` (`"correlation.id"`) is the single source of truth for this contract's key name. Cross-domain consumers (`13.ServiceDefaults`'s `BaggageLogRecordProcessor` test suite, any future consumer) must reference this constant rather than re-typing the literal — a hand-copied literal is exactly how `13.ServiceDefaults`'s own P-251 test design ended up asserting against `"CorrelationId"` instead of this middleware's actual `"correlation.id"` value, a mismatch invisible to either domain's test suite until this phase's cross-check (see the Logging subsection below).

### Logging EventId assignment & correlation verification (WO-041, P-249/P-250/P-251/P-256)

- Every `[LoggerMessage]`-attributed method in this domain carries an explicit `EventId` derived from `SharedKernel.Primitives.Logging.LoggingEventIdRanges.Presentation` (14000) — never a compiler-auto-assigned or ad hoc numeric literal. Compiler auto-numbering is a latent stability hazard: adding, removing, or reordering `[LoggerMessage]` methods in the same class silently renumbers every ID that follows.
- Sub-block allocation, in package declaration order per the root registry convention (100-wide sub-blocks per package within the domain's 1000-wide block): `SharedKernel.Presentation.WebApi` = 14000–14099, `SharedKernel.Presentation.SignalR` = 14100–14199.
- Assigned `EventId`s: `CorrelationIdMiddleware` = 14000, `SharedKernelExceptionHandler` = 14001 (both `.WebApi`); `HubExceptionMappingFilter` = 14100 (`.SignalR`). Any future `[LoggerMessage]` method added to either package continues sequentially within that package's sub-block — never reuse a retired ID.
- This domain requires no code-shape changes to conform to `00.Governance`'s `SK0020`/`SK0021` (`LoggingAuthoringStyleAnalyzer`, P-250) — it already authors exclusively via `[LoggerMessage]`, never a direct `ILogger` extension-method call or hand-written `LoggerMessage.Define` delegate. The explicit `EventId` assignment is what closes the remaining gap against `00.Governance`'s `LoggingEventIdIntegrityAssertion` (global uniqueness + per-assembly range membership).
- **Correlation-on-log-record verification is proven without a cross-domain `ProjectReference`.** `13.ServiceDefaults`'s `BaggageLogRecordProcessor` (P-251) is what makes `CorrelationIdMiddleware`'s `Activity` baggage land on emitted `LogRecord`s — but this domain must never take a `ProjectReference` on `SharedKernel.ServiceDefaults` to prove that (per the existing `13.ServiceDefaults` non-dependency rule above). This domain's own test suite instead uses a test-local minimal `BaseProcessor<LogRecord>` that mirrors `BaggageLogRecordProcessor`'s documented contract (generic `Activity.Baggage` → `LogRecord.Attributes` copy, never overwriting an explicit attribute) to prove its own middleware's output is compatible with that mechanism — the reciprocal of the technique `13.ServiceDefaults`'s own correlation test already uses in the opposite direction (simulating this middleware's baggage-setting call via raw BCL `Activity.SetBaggage(...)` rather than referencing this package).

### SignalR hub filter rules

- `TenantContextHubFilter` and `HubExceptionMappingFilter` are registered as *global* hub filters via `HubOptions.AddFilter<T>()`, not per-hub `[HubFilter]` attributes — every hub in a consuming service gets both by default through `AddSharedKernelSignalR`.
- `HubExceptionMappingFilter` must never let a non-`HubException` cross the filter boundary — SignalR serializes unknown exception types inconsistently across transports; only `HubException` messages are guaranteed to reach the client safely.
- Filter ordering: `TenantContextHubFilter` (connection-scoped, attaches data) is independent of `HubExceptionMappingFilter` (invocation-scoped, wraps calls) — they do not depend on each other's execution order.

### SignalR Redis backplane rules

- `WithRedisBackplane` is purely additive/opt-in — omitting it keeps SignalR fully in-memory (single-instance only), which is correct for local dev and single-replica deployments.
- Never share an `IConnectionMultiplexer` instance between `02.Caching.Redis.Core`'s `AddRedisConnection` and SignalR's backplane — `AddStackExchangeRedis` manages its own connection lifecycle internally and the two domains must not be wired together. This is intentional isolation, not an oversight: a backplane outage must not be conflated with a cache-connection outage in health checks or logs.
- See "Why SignalR's Redis backplane is distinct from `02.Caching.Redis.PubSub`" above before proposing any code sharing between the two.

### AOT notes

- `Microsoft.AspNetCore.OpenApi`'s schema generation uses source-generated reflection metadata where possible; verify AOT compatibility on every SDK upgrade since this is a fast-moving built-in feature.
- `Asp.Versioning.*` and `Scalar.AspNetCore` AOT status must be re-verified on every major version bump — these are third-party packages, not BCL.
- `Microsoft.AspNetCore.SignalR.StackExchangeRedis` is not fully AOT-verified as of this writing — confirm on adoption and wrap behind `WithRedisBackplane` (already an abstraction seam) if a swap is ever needed.

---

## DI Registration (planned shape)

```csharp
// WebApi — minimal setup (ProblemDetails + exception handling only)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
builder.Services.AddSharedKernelCorrelationId();
// ...
app.UseSharedKernelCorrelationId();   // first in the pipeline
app.UseExceptionHandler();

// WebApi — full stack (versioning + OpenAPI + Scalar)
builder.Services.AddSharedKernelApiVersioning();
builder.Services.AddSharedKernelOpenApi(title: "Orders API");
// ...
app.MapSharedKernelOpenApi();

// WebApi — Result<T> at the endpoint boundary (Minimal API)
app.MapGet("/orders/{id}", async (Guid id, IOrderQueryService svc, CancellationToken ct) =>
{
    Result<OrderDto> result = await svc.GetByIdAsync(id, ct);
    return result.ToProblemDetailsResult(order => Results.Ok(order));
});

// WebApi — Result<T> at the endpoint boundary (MVC controller)
[HttpGet("{id}")]
public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken ct)
{
    Result<OrderDto> result = await _svc.GetByIdAsync(id, ct);
    return result.ToActionResult();
}

// SignalR — minimal setup (in-memory, single replica)
builder.Services.AddSharedKernelSignalR();

// SignalR — scale-out across pods via Redis backplane
builder.Services.AddSharedKernelSignalR()
       .WithRedisBackplane(connectionString);

// SignalR — tenant-scoped group broadcast from inside a Hub method
await Clients.Group(HubGroupNaming.TenantGroup(tenantId)).SendAsync("OrderUpdated", orderId);
```

---

## Test Rules

- Test projects are nested inside each package folder: `SharedKernel.Presentation.WebApi/SharedKernel.Presentation.WebApi.Tests/`, `SharedKernel.Presentation.SignalR/SharedKernel.Presentation.SignalR.Tests/`.
- `ErrorTypeStatusCodeMap` / `ErrorProblemDetailsExtensions`: unit tests covering every `ErrorType` → status code mapping, plus the unmapped/unknown fallback to 500.
- `ResultHttpExtensions`: unit tests for both Minimal API and MVC overloads, success and failure paths, including the `onSuccess` projection overload.
- `SharedKernelExceptionHandler`: tests verifying known `SharedKernelException` subtypes map to their carried `Error`'s status code, unknown exceptions fall back to 500, and `Detail` is suppressed outside `IsDevelopment()`.
- API versioning: integration test (via `WebApplicationFactory`) verifying unversioned requests resolve to `DefaultApiVersion`, URL-segment and header version readers both work, and `api-supported-versions` response header is present.
- OpenAPI: integration test asserting `MapOpenApi()` produces a valid document per registered version group and `MapScalarApiReference` route responds successfully.
- `CorrelationIdMiddleware`: unit tests for header-present, header-absent (generates new), and response-header-always-set (including on a short-circuited pipeline).
- `TenantContextHubFilter` / `HubExceptionMappingFilter`: unit tests using SignalR's hub-testing harness — tenant attached to `Context.Items` on connect, known exceptions surfaced as `HubException` with safe messages, unknown exceptions logged and redacted.
- `HubGroupNaming`: unit test for the `tenant:{tenantId:D}` format.
- SignalR Redis backplane: integration test via Testcontainers (`16.Testing/SharedKernel.Testing`) verifying a message sent from one `IHubContext` instance is received by a client connected through a second, independently-configured instance sharing the same Redis backplane.
- `EventId` regression pins (WO-041, P-256): unit tests asserting each of the three `[LoggerMessage]`-attributed methods carries its exact assigned `EventId` (14000, 14001, 14100) — read via reflection over the compiled `[LoggerMessage]` attribute, not by triggering the log call and inspecting a captured `EventId` at runtime, so the test fails immediately if a future edit silently renumbers the method.
- `CorrelationIdMiddleware.BaggageKey` (WO-041, P-256): unit test asserting the constant's value equals the literal `"correlation.id"` exactly.
- Correlation-on-log-record integration test (WO-041, P-256): exercises a request through `CorrelationIdMiddleware`, emits a log record downstream via `ILogger`, and — using a test-local minimal `BaseProcessor<LogRecord>` mirroring `13.ServiceDefaults`'s documented `BaggageLogRecordProcessor` contract — asserts the correlation id appears in `LogRecord.Attributes` under `CorrelationIdMiddleware.BaggageKey`. This test must never take a `ProjectReference` on `SharedKernel.ServiceDefaults` — see the Logging EventId assignment & correlation verification subsection above.

### WebApplicationFactory integration test pattern (confirmed at Tests phase)

- `OpenApiExtensions.MapSharedKernelOpenApi` requires a `WebApplication` receiver, not `IApplicationBuilder` — a `WebApplicationFactory<T>` test host that needs it cannot use the `IWebHostBuilder.Configure(IApplicationBuilder)` callback. Override `WebApplicationFactory<T>.CreateHost(IHostBuilder)` instead: build a `WebApplication` directly via `WebApplication.CreateBuilder()` + `.WebHost.UseTestServer()`, map endpoints/`MapSharedKernelOpenApi()` on it, call `app.Start()`, and return it.
- `Asp.Versioning.Http` 10.0.0 exposes the requested API version on `HttpContext` as the extension **property** `HttpContext.RequestedApiVersion` (`Microsoft.AspNetCore.Http.HttpContextExtensions`) — there is no callable `GetRequestedApiVersion()` method despite that name appearing in some docs/training data. Always verify via reflection against the installed package before writing test/endpoint code against it (see `00.Governance`-adjacent guidance: verify real API shapes, don't trust the name).
- SignalR Redis backplane fan-out test: two independent in-memory `TestServer` SignalR hosts (each built the same way — `AddSharedKernelSignalR().WithRedisBackplane(connectionString)` against the same `RedisContainerFixture` (`16.Testing/SharedKernel.Testing.Containers`) connection string) prove cross-instance fan-out by sending via one instance's `IHubContext<THub>` and asserting receipt on a `Microsoft.AspNetCore.SignalR.Client.HubConnection` connected through the other instance's `TestServer.CreateHandler()`. `SharedKernel.Presentation.SignalR.Tests` carries `Microsoft.AspNetCore.SignalR.Client` 10.0.5 (the latest available for this package — not the 10.0.9 WebApi-stack line) and `Microsoft.AspNetCore.TestHost` 10.0.9 as test-only package references.

### Documentation build enforcement (confirmed at Docs phase)

- Both production `.csproj` files set `<GenerateDocumentationFile>true</GenerateDocumentationFile>` — this is what actually turns missing-XML-doc (CS1591) and unresolved-`cref` (CS1574/CS1580) warnings on; without it the compiler silently skips doc validation even when every member already has a `///` comment block. Enabling it after the Core phase surfaced 4 pre-existing unresolved-`cref` warnings (`IHostEnvironment.IsDevelopment()` and `MapOpenApi(IEndpointRouteBuilder, string)` lacked a `using` for their containing namespace; `HubOptions.HubFilters` doesn't exist under that exact member name; `HttpContext` was ambiguous without a `Microsoft.AspNetCore.Http` `using`) — fixed via `<c>` plain-text references or fully-qualified `cref`s rather than adding usings that would pull unrelated types into scope. Any future PR that adds a new public member must build clean with this flag already on — do not defer doc-comment correctness to a later "Docs phase" cleanup pass.
- READMEs live at the package root (`SharedKernel.Presentation.WebApi/README.md`, `SharedKernel.Presentation.SignalR/README.md`); a single shared `14.Presentation/CONFIGURATION.md` documents every DI extension method's parameters/options/defaults for both packages and is linked from both READMEs — not duplicated per-package.

---

## Changelog

> Maintained by the presentation domain agent. One line per significant change.

- [2026-06-25] Domain brain initialized — packages, technology stack (ProblemDetails, API versioning, native OpenAPI + Scalar, correlation-id middleware, `Result<T>`→HTTP extensions, SignalR hub filters + Redis backplane), interface contracts, implementation rules, DI registration shape, test rules (claude)
- [2026-06-25] WO-031 P-192 Design sign-off: all Interface Contracts reclassified from "planned" to "confirmed" public surface — no shape changes, formal lock only. `CorrelationIdMiddleware`'s baggage/`HttpContext.Items` keys confirmed as this domain's own contract; the prior `13.ServiceDefaults` coupling note removed from both this file and `state-map.md`'s Cross-Domain Dependencies (ownership flows from `13.ServiceDefaults` toward this domain, not the reverse). Scaffold/Core/Tests/Docs/Published phases (P-193–P-198) dispatched into `state-map.md` (presentation-arch-planner, WO-031)
- [2026-06-25] SK.14.Scaffold complete (P-193): pinned NuGet versions confirmed via NuGet flat-container listing — `Asp.Versioning.Http`/`Asp.Versioning.Mvc.ApiExplorer` 10.0.0, `Microsoft.AspNetCore.OpenApi` 10.0.9, `Scalar.AspNetCore` 2.16.5, `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.9; Technology Stack table gains a Pinned Version column. Both `.csproj` files wired with `FrameworkReference Microsoft.AspNetCore.App`, correct `ProjectReference`s, and folder structure; both nested `.Tests` projects created referencing `SharedKernel.Testing`; both packages confirmed already registered in `Platform.SharedKernel.slnx`. All four projects build 0 warnings/0 errors (presentation-phase-implementer)
- [2026-06-25] SK.14.Core complete (C-01–C-13, P-194/P-195): both packages fully implemented. Both `.csproj` files gained a `SharedKernel.Core` ProjectReference (within the `01.Core` layering allowance) for `SharedKernelException`/`NotFoundException`/etc. Three design-doc type-name corrections discovered via reflection against the installed packages and now reflected in this file's Interface Contracts: `AddSharedKernelSignalR`/`WithRedisBackplane` return/accept `ISignalRServerBuilder` (not `ISignalRBuilder`, which does not exist in this SDK); `WithRedisBackplane`'s configure callback type is `Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisOptions` (not `StackExchangeRedisOptions`); `Microsoft.OpenApi` 2.0.0's model types live under namespace `Microsoft.OpenApi`, not `Microsoft.OpenApi.Models`. 39 unit tests added and passing (30 WebApi, 9 SignalR); a standalone end-to-end smoke test confirmed `/openapi/v1.json` and `/scalar/v1` both return HTTP 200 with and without API versioning enabled. Both projects build 0 warnings/0 errors (presentation-phase-implementer)
- [2026-06-25] SK.14.Tests complete (T-01–T-10, P-196): T-01/T-02/T-03/T-06/T-07/T-08 were already fully covered by Core-phase unit tests. Three integration tests added: `ApiVersioningIntegrationTests` and `OpenApiIntegrationTests` (WebApi, `WebApplicationFactory<T>.CreateHost` override pattern) and `RedisBackplaneIntegrationTests` (SignalR, Testcontainers via `RedisContainerFixture`, two independent `TestServer` SignalR hosts cross-instance fan-out). New Test Rules subsection documents the `WebApplicationFactory`/`CreateHost` pattern and the `HttpContext.RequestedApiVersion` extension-property correction (not a method). No `16.Testing` capability gap found. 48/48 tests passing (38 WebApi + 10 SignalR) (presentation-phase-implementer)
- [2026-06-25] SK.14.Docs complete (DO-01–DO-05, P-197): both `.csproj` files gained `GenerateDocumentationFile=true`, which surfaced and fixed 4 pre-existing unresolved-`cref` warnings; new Test Rules subsection documents this enforcement pattern. Added per-package `README.md`s and a shared `14.Presentation/CONFIGURATION.md`. 48/48 tests still passing (presentation-phase-implementer)
- [2026-06-25] SK.14.Published complete (P-01–P-05, P-198) — verification-only, no design/interface changes. Both `.csproj` files gained full NuGet packaging metadata mirroring the `12.Security`/`13.ServiceDefaults` convention and now pack cleanly to `.nupkg`+`.snupkg` with zero warnings. A new shared `14.Presentation/consumer-verify` harness (mirroring `13.ServiceDefaults/consumer-verify`, registered in `Platform.SharedKernel.slnx`) proved both packages compose with zero DI exceptions — including `WithRedisBackplane` against a connection string with no live Redis instance required, since `AddStackExchangeRedis` defers the actual connection until first use, making DI-graph resolution alone a valid, low-cost proof of wiring correctness. 48/48 tests still passing. Both packages now `●` Published — 14.Presentation domain complete end to end (presentation-phase-implementer)
- [2026-07-09] WO-041 P-256 dispatched — explicit `EventId` assignment and correlation verification. Added `CorrelationIdMiddleware.BaggageKey` public constant (replacing the inline `"correlation.id"` literal) and locked `EventId`s for all three existing `[LoggerMessage]` methods against `LoggingEventIdRanges.Presentation` (14000): `CorrelationIdMiddleware` = 14000, `SharedKernelExceptionHandler` = 14001 (both `.WebApi`, sub-block 14000–14099), `HubExceptionMappingFilter` = 14100 (`.SignalR`, sub-block 14100–14199). New "Logging EventId assignment & correlation verification" Implementation Rules subsection added. Correlation-on-log-record verification designed as a self-contained integration test using a test-local minimal `BaseProcessor<LogRecord>` mirroring `13.ServiceDefaults`'s documented `BaggageLogRecordProcessor` contract — deliberately zero `ProjectReference` to `SharedKernel.ServiceDefaults`, mirroring the reciprocal technique `13.ServiceDefaults`'s own correlation test (T-27, P-251) already uses in the opposite direction. **Cross-domain finding:** `13.ServiceDefaults`'s own P-251 test design (T-27) was found to hardcode a different, incorrect baggage-key literal (`"CorrelationId"`) instead of this middleware's actual `"correlation.id"` value — flagged in `state-map.md` (DO-07) as a correction for `servicedefaults-arch-planner`/`servicedefaults-phase-implementer`; out of this domain's jurisdiction to fix directly. New Cross-Domain Dependencies row added: `SK.14.Core` (C-14–C-16) is gated on `01.Core`'s P-249 `LoggingEventIdRanges.Presentation` constant landing (still `○` as of this writing). Task rows D-12/D-13, C-14–C-18, T-11–T-13, DO-06/DO-07, P-06 added to `state-map.md`, all `○` (presentation-arch-planner, WO-041)
- [2026-07-14] WO-041 P-256 closed — `01.Core`'s `LoggingEventIdRanges.Presentation` constant confirmed landed, unblocking the full remaining task set in one session. D-12/D-13 confirmed against ground-truth source (`CorrelationIdMiddleware.BaggageKey` was already present in code from a prior partial pass — no change needed, design lock formalized). C-14–C-17 added `EventId = LoggingEventIdRanges.Presentation + N` to all three `[LoggerMessage]` attributes (`using SharedKernel.Primitives.Logging;` added to all three files); C-18 confirmed both packages build 0 warnings/0 errors post-change (informational — no `SK0020`/`SK0021` violations, this domain already conformed). T-11–T-13 added: two reflection-based `EventId` regression-pin tests (`LoggerMessageEventIdTests` in `.WebApi.Tests`, `HubExceptionMappingFilterEventIdTests` in `.SignalR.Tests` — read the compiled `LoggerMessageAttribute` via `BindingFlags.NonPublic | BindingFlags.Static` reflection over the private nested `Log` class, never trigger-and-capture at runtime); a `BaggageKey` literal-value pin test; and `CorrelationLogRecordIntegrationTests` — a test-local `BaseProcessor<LogRecord>` mirroring `13.ServiceDefaults`'s `BaggageLogRecordProcessor` contract exactly, requiring a new test-only `OpenTelemetry.Extensions.Hosting` 1.16.0 package reference in `SharedKernel.Presentation.WebApi.Tests.csproj` (matching `13.ServiceDefaults`'s existing pin) — zero `ProjectReference` to `SharedKernel.ServiceDefaults`. DO-06/DO-07 confirmed already satisfied by the existing XML doc comments and the prior changelog entry above. P-06: both packages re-packed to `1.0.1` (patch bump, additive-only changes), `dotnet pack` 0 warnings; `consumer-verify` re-run confirms all 6 surfaces PASS with zero DI exceptions (the harness's own build hit a pre-existing, unrelated `NU1903`-as-error on `Microsoft.OpenApi` 2.0.0 — confirmed present identically on `main` before this session via `git stash`, worked around locally with `-p:WarningsNotAsErrors=NU1903` for verification purposes only, not committed). 42/42 `SharedKernel.Presentation.WebApi.Tests` (+4) and 11/11 `SharedKernel.Presentation.SignalR.Tests` (+1) passing. All six phases (`SK.14.Design` through `SK.14.Published`) now fully `●` (presentation-phase-implementer, WO-041)
