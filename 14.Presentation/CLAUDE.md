# 14.Presentation — API Surface Layer

## What This Domain Is

The HTTP, real-time, and (as of WO-074, `○` Pending) server-side gRPC API surface layer. Every microservice's REST/Minimal API conventions (ProblemDetails error shape, API versioning, OpenAPI documentation), SignalR real-time conventions (hub filters, Redis scale-out backplane), and inbound gRPC service conventions (exception/`Result<T>`→`RpcException` mapping, correlation/tenant metadata extraction, declarative authorization) derive from the types defined here. This domain is framework-glue, not business logic — it translates between the platform's core primitives (`Result<T>`, `Error`, `01.Core`) and the HTTP/SignalR/gRPC wire formats clients actually see. `11.Communication.Grpc` stays outbound-only (client-side channel/interceptors); this domain owns the inbound gRPC API boundary, mirroring the same HTTP inbound/outbound split already drawn between this domain and `11.Communication.Rest`.

Philosophy: **Thin. Convention-over-configuration. RFC-compliant. AOT-Preferred (native OpenAPI over reflection-heavy generators).**

> **Layering boundary:** Per root `CLAUDE.md`, `14.Presentation` may reference `01.Core`, `04.Contracts`, `12.Security`, and `13.ServiceDefaults` only. It must never reference `05.Application`, `06.Persistence`, `07.Messaging`, or any other infrastructure layer directly — MediatR dispatch, repository calls, and message publishing are the hosting microservice's concern, not this domain's. This domain converts *outcomes* (`Result<T>`, `Error`, exceptions) into HTTP/SignalR responses; it never produces those outcomes itself. `04.Contracts` is permitted but currently unused: none of the three packages references `SharedKernel.Contracts` (`.WebApi` dropped its unused reference; `.Grpc` is forbidden from taking one).

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Presentation.WebApi` | RFC 9457 `ProblemDetails` error mapping, global `IExceptionHandler`, API versioning (`Asp.Versioning`), native OpenAPI document generation + Scalar interactive UI, inbound correlation-id middleware, `Result<T>` → `IResult`/`ActionResult` HTTP-boundary extensions | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Localization`, `SharedKernel.Security.Abstractions`, `Asp.Versioning.Http`, `Asp.Versioning.Mvc.ApiExplorer`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` |
| `SharedKernel.Presentation.SignalR` | `IHubFilter` implementations (tenant context attachment, exception-to-`HubException` mapping), Redis-backed scale-out backplane wiring, tenant-group naming convention, `AddSharedKernelSignalR` DI builder | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Security.Abstractions`, `Microsoft.AspNetCore.SignalR.StackExchangeRedis` |
| `SharedKernel.Presentation.Grpc` (`●` Shipped, WO-074/P-468) | Server-side gRPC conventions: `GrpcStatusCodeMap` (`ErrorType`→`StatusCode`, a sibling to `ErrorTypeStatusCodeMap`, never merged), `GrpcResultExtensions` (`Result<T>`→`RpcException`-throwing extensions), a global `GrpcExceptionInterceptor` (the gRPC counterpart to `IExceptionHandler`/`SharedKernelExceptionHandler`), `GrpcCorrelationInterceptor`/`GrpcTenantContextInterceptor` (inbound correlation-id/tenant metadata extraction, mirroring `CorrelationIdMiddleware`/`TenantContextHubFilter`), `GrpcAuthorizationInterceptor` (reuses `.WebApi`'s `[RequireRole]`/`[RequirePermission]`/`[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` verbatim, confirmed via real endpoint-metadata proof — no reflection fallback needed), `AddSharedKernelGrpc` DI builder | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Security.Abstractions`, `SharedKernel.Presentation.WebApi` (intra-domain — `Authorization/` attribute reuse only), `Grpc.AspNetCore` `2.80.0` — **never `SharedKernel.Contracts`/`04.Contracts`**, a named Hard-rule exception mirroring `SharedKernel.Communication.Grpc`'s P-163 rule |

All three packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`). None of the three packages has an `.Abstractions` sibling — unlike `02.Caching`/`06.Persistence`, they are not interchangeable providers of one capability; they are distinct API surfaces (HTTP, real-time, gRPC) that happen to share the same domain folder. `.Grpc` is the one exception to this domain's usual sibling-package independence: it deliberately takes a `ProjectReference` on `.WebApi` for `Authorization/` attribute reuse (see "Why `.Grpc` references `.WebApi`" below) — `.SignalR` still takes none, and that decision stands unchanged.

---

## Technology Stack

| Concern | Technology | Pinned Version | Owning Package |
| --- | --- | --- | --- |
| Error response shape | RFC 9457 `ProblemDetails` (`Microsoft.AspNetCore.Http`, built-in) | shared framework | `.WebApi` |
| Global exception-to-response pipeline | `IExceptionHandler` (ASP.NET Core 8+, built-in) + `AddProblemDetails()` | shared framework | `.WebApi` |
| API versioning | `Asp.Versioning.Http` + `Asp.Versioning.Mvc.ApiExplorer` | `10.0.0` | `.WebApi` |
| OpenAPI document generation | `Microsoft.AspNetCore.OpenApi` — native, source-gen-friendly, ships in the `net10.0` SDK | `10.0.11` | `.WebApi` |
| OpenAPI interactive UI | `Scalar.AspNetCore` | `2.16.5` | `.WebApi` |
| Correlation-id propagation (inbound) | `System.Diagnostics.Activity` (BCL) — no new dependency | shared framework | `.WebApi` |
| Real-time hub | `Microsoft.AspNetCore.SignalR` (built-in) | shared framework | `.SignalR` |
| Real-time scale-out backplane | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `10.0.9` | `.SignalR` |
| Hub pipeline extensibility | `IHubFilter` (built-in, .NET 7+) | shared framework | `.SignalR` |
| Server-side gRPC hosting | `Grpc.AspNetCore` — confirmed `net10.0`-compatible via direct NuGet flat-container listing at Scaffold phase (WO-074/P-468, shipped); transitively pulls `Grpc.AspNetCore.Server.ClientFactory`, `Google.Protobuf` `3.31.1`, `Grpc.Tools` `2.80.0` | `2.80.0` | `.Grpc` |

> Versions pinned at Scaffold (SK.14.Scaffold, 2026-06-25). All three NuGet packages confirmed compatible with `net10.0` at pin time via direct NuGet flat-container version listing (not assumed from documentation). Re-verify AOT status on any future version bump per the AOT notes below — none of the three are BCL.
>
> **`Microsoft.AspNetCore.OpenApi` re-pinned `10.0.9` → `10.0.11` (WO-063, `SK.14.Published`, P-17–P-24, 2026-08-21) for a security fix, not a feature bump.** `10.0.9` transitively pins `Microsoft.OpenApi` `2.0.0`, which carries `GHSA-v5pm-xwqc-g5wc`/`CVE-2026-49451` (CVSS 7.5 high — a crafted OpenAPI document with a circular schema reference can crash the parsing process via stack overflow; patched at `2.7.5`+ on the 2.x line). Confirmed via direct `.nuspec` inspection across `10.0.9`/`10.0.10`/`10.0.11` that only `10.0.11` (still `net10.0`, non-preview) changes its own `Microsoft.OpenApi` dependency range to `[2.7.5, 3.0.0)` — `10.0.9`/`10.0.10` both still hard-pin `2.0.0`. This is the only NuGet-version pin change ever made in this domain purely to resolve a security advisory rather than to adopt a new API surface; any future advisory affecting a pinned dependency in this table should be resolved the same way — check for a patched transitive version first, never default to a `NoWarn`/`WarningsNotAsErrors` suppression.
>
> **Note:** Swashbuckle/NSwag are deliberately not used. `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` is the .NET 9/10-idiomatic pairing — Scalar renders the native OpenAPI document directly, avoiding Swashbuckle's reflection-heavy assembly-scanning generation pipeline. This is a better fit for the root brain's AOT-preferred guidance than the Swashbuckle stack.

### Why SignalR's Redis backplane is distinct from `02.Caching.Redis.PubSub`

Both rely on `StackExchange.Redis` Pub/Sub under the hood, but they solve unrelated problems and must never be merged or treated as interchangeable:

- **`SharedKernel.Caching.Redis.PubSub`** (`02.Caching`) carries *ephemeral, at-most-once, cache-adjacent string signals* between service instances through `IRedisChannelService` — its payload is a caller-defined string on a caller-named channel, and its consumer is whatever handler the service subscribes. (Cache invalidation itself no longer uses it: FusionCache's Redis backplane propagates removals, expirations and tag removals.)
- **SignalR's Redis backplane** (`14.Presentation.SignalR`) fans out *real-time client messages* (`Hub.Clients.All.SendAsync(...)`, group broadcasts) across all pods serving the same Hub — its payload is whatever the Hub sends, and its consumer is connected WebSocket/SSE clients.

A microservice may legitimately depend on both packages simultaneously for entirely different reasons. Neither package references the other.

### Why server-side gRPC lives in `14.Presentation`, not `11.Communication.Grpc`

> **`●` Shipped (WO-074, P-468).** Recorded here at Design-lock time so the placement decision would never be re-litigated during Core-phase implementation — it wasn't; Core-phase implementation confirms every claim below.

`11.Communication.Grpc`'s entire `Interceptors/` folder is two *client* interceptors (`CorrelationTracingInterceptor`, `TenantIdInterceptor`) attached to an outbound channel factory — confirmed via direct source read at WO-074's requirement analysis. `11.Communication`'s charter is outbound service-to-service calling. Server-side gRPC — receiving a call, mapping its own exceptions/`Result<T>` failures to a client-safe response, extracting inbound correlation/tenant identity, gating access declaratively — is architecturally an *inbound API boundary* concern, the same category HTTP already falls into for this domain. It therefore belongs beside `SharedKernel.Presentation.WebApi`/`.SignalR`, not folded into `11.Communication.Grpc`, mirroring the inbound/outbound split this domain and `11.Communication.Rest` already draw for HTTP.

### Why `.Grpc` references `.WebApi`

`SharedKernel.Presentation.Grpc` takes a `ProjectReference` on `SharedKernel.Presentation.WebApi` — the one exception to this domain's usual "distinct API surfaces, no cross-references" rule (`.SignalR` still takes none). The reference exists solely to reuse `RequireRoleAttribute`/`RequirePermissionAttribute`/`RequireFreshAuthenticationAttribute`/`RequireAuthenticationMethodAttribute` verbatim, so the platform has **one** declarative authorization dialect across HTTP and gRPC, not two independently-maintained attribute sets evaluated by two independently-maintained rule engines. This is a narrower, more defensible coupling than the one `.SignalR` deliberately declined for CORS (P-418/D-65): that decline existed because a pure real-time service must not be forced to pull `Asp.Versioning`/`Microsoft.AspNetCore.OpenApi`/`Scalar.AspNetCore` transitively just to get a CORS integration point. `.Grpc`'s reference pulls in four small `System.Attribute` types with no further transitive NuGet surface, and the alternative — a second, duplicated attribute vocabulary — was explicitly rejected by WO-074's own acceptance criteria. Do not "fix" one decision by analogy to the other; they optimize for different things and both are correct for their own case.

---

## Interface Contracts

> **Status: Design-locked (WO-031, P-192) for `.WebApi`/`.SignalR`; shipped (WO-074, P-468) for `.Grpc`.** Every contract below is the confirmed, signed-off public API shape — not a draft. Implementation (Scaffold/Core) follows these signatures exactly; any deviation discovered during Core-phase implementation must come back through a Design amendment, not a silent change.

### `SharedKernel.Presentation.WebApi` — confirmed public surface

#### Error → ProblemDetails mapping (`Errors/`)

```text
ErrorTypeStatusCodeMap  (static class)
    .Resolve(ErrorType type) → int   (HTTP status code)
    NOTE: Validation → 400, Unauthorized → 401, Forbidden → 403, NotFound → 404, Conflict → 409,
          BusinessRule → 422, Unexpected → 500. Any ErrorType not explicitly mapped
          (including None) falls back to 500. This is the real shipped mapping, verified
          against SharedKernel.Primitives.Errors.ErrorType and ErrorTypeStatusCodeMap.cs
          directly. A prior revision of this note claimed a "Forbidden → 403 ... Failure → 500"
          mapping before ErrorType.Forbidden actually existed upstream (WO-058, P-381, D-19) —
          that gap is now closed: ErrorType.Forbidden shipped in 01.Core (P-384/WO-059,
          SharedKernel.Primitives 1.1.0) and the Forbidden → 403 case is implemented here
          (C-24, WO-058). ErrorType.Failure was never a real member and is not part of this
          mapping.
          Single source of truth — inline switch statements duplicating this mapping anywhere
          else in a consuming service is a platform violation.

ErrorProblemDetailsExtensions  (static class)
    .ToProblemDetails(this Error error, HttpContext? context = null) → ProblemDetails
    NOTE: Title = error.Code; Detail = error.Message — CORRECTED, a prior revision of this note
          claimed "Detail = error.Description", but Error (SharedKernel.Primitives.Errors) has
          never carried a Description member, only Code/Message/Type; verified directly against
          the real Error.cs and ErrorProblemDetailsExtensions.cs source. As of P-484/WO-078
          (shipped), Detail is the LOCALIZEDDETAILRESOLVER-mediated value: when context is
          non-null and an ILocalizationCatalog is registered in context.RequestServices AND has a
          translation for (error.Code, CultureInfo.CurrentUICulture), that translated string is
          used instead of error.Message — never blank, error.Message remains the mandatory
          fallback. Status = ErrorTypeStatusCodeMap.Resolve(error.Type);
          Type = RFC 9457 URI for the resolved status (e.g. "https://httpstatuses.io/404");
          Extensions["errorCode"] = error.Code; Extensions["traceId"] = Activity.Current?.Id
          ?? context?.TraceIdentifier. Otherwise pure mapping — no logging, no I/O beyond the
          optional DI resolution. As of P-544 (shipped): when error.Details (01.Core.Error) is
          non-empty — an Error.Validation(IReadOnlyList<Error>) aggregate — Extensions["errors"]/["errorCodes"]
          is additionally populated via the same internal LocalizedDetailResolver.AddErrorsExtensions
          helper ValidationProblemDetailsExtensions uses, so a Result.Failure(Error.Validation(errors))
          reaching the HTTP boundary through ResultHttpExtensions produces a byte-identical "errors"
          shape to a thrown ValidationException carrying the same field errors.
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
          violation.
          There is no response-wrapper DTO: 04.Contracts ships no envelope for HTTP results. The
          success body is the value itself; the failure body is always RFC 9457 ProblemDetails. A
          calling service built on 11.Communication.Rest maps that response back to Result<T> with
          ReadResultAsync<T>.
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
    .HeaderName                                              → const string = WellKnownHeaders.CorrelationId
        (WO-042, P-262, D-14/C-19 — SHIPPED). Forwarding alias over 01.Core's
        WellKnownHeaders.CorrelationId ("X-Correlation-Id") — the literal value originates from
        01.Core's SharedKernel.Primitives.Propagation.WellKnownHeaders, never independently
        retyped here; this constant remains for call-site ergonomics and backward compatibility.
        No new NuGet/ProjectReference was required — WellKnownHeaders ships in the already
        SharedKernel.Primitives package this domain already references (S-11).
    .BaggageKey                                              → const string = WellKnownBaggageKeys.CorrelationId
        (WO-041, P-256, D-13/C-17; forwarding-alias sourcing SHIPPED per WO-042/P-262/D-14/C-19).
        Public named constant for the Activity baggage key this middleware writes to — replaces
        the prior inline string literal. Exists so cross-domain consumers (13.ServiceDefaults's
        BaggageLogRecordProcessor test suite, any future consumer) reference the same compile-time
        value instead of hand-copying a literal that can silently drift out of sync (see the
        Logging EventId & Correlation Verification subsection below for the mismatch this closes).
        This constant's *value* now forwards from 01.Core's WellKnownBaggageKeys.CorrelationId
        (SharedKernel.Primitives.Propagation) rather than being an independently-owned literal —
        the constant's name and call-site usage did not change; zero behavioral change.
    ItemsKey (HttpContext.Items storage key) is presentation-local and explicitly NOT sourced from
        01.Core — it is consumed only within this domain, never a cross-service wire concept, so it
        has no shared-constant equivalent and stays untouched by WO-042.
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

#### Declarative role/permission authorization (`Authorization/`)

> **Status: Shipped end to end (WO-058, P-381, D-15–D-19).**
> Shape below is confirmed and implemented. `01.Core`'s previously-missing `Error.Forbidden(string, string)` /
> `ErrorType.Forbidden` shipped in `SharedKernel.Primitives` 1.1.0 (P-384/WO-059), unblocking C-21–C-25
> (all `●`) — see `state-map.md`'s Cross-Domain Dependencies table (now `Resolved`). Every type below
> (`RequireRoleAttribute`, `RequirePermissionAttribute`, `AuthorizationRequirementEndpointFilter`,
> `AuthorizationEndpointFilterExtensions`, `AddSharedKernelAuthorizationFilters`) is implemented in the
> `Authorization/` namespace exactly per this shape, and proven by 26 tests (T-15–T-18, all `●`) —
> see the Test Rules section below. XML docs (DO-09) were confirmed already complete from the Core
> phase; the README's "Declarative role/permission authorization" section and a matching
> `CONFIGURATION.md` entry (DO-10) document both wiring forms and the non-automatic-wiring
> caveat. `SharedKernel.Presentation.WebApi` re-packed to `1.1.0` (P-08) and `consumer-verify` proves
> `AddSharedKernelAuthorizationFilters()` composes with zero DI exceptions alongside the full WebApi
> stack — all six `SK.14.*` phases are now `●`, closing this domain end to end.

```text
RequireRoleAttribute  (sealed class : Attribute)
    ctor(params string[] roles)
    .Roles → IReadOnlyCollection<string>
    NOTE: Plain endpoint-metadata attribute. Usable directly on an MVC controller/action (MVC
          auto-surfaces it as endpoint metadata) or attached to a minimal-API endpoint via
          .WithMetadata(new RequireRoleAttribute(...)) — see AuthorizationEndpointFilterExtensions
          below for the sugar form. Roles listed within ONE attribute instance are OR'd (caller
          needs any one). Stacking multiple [RequireRole]/[RequirePermission] attributes on the
          same endpoint is AND'd (caller must satisfy every attached attribute) — mirrors, in
          spirit, 05.Application's IAuthorizeRequest.RequiredPermissions evaluated per
          PermissionMatch.All/Any; this package does not reuse that exact API shape, only the
          composition idea, since stacked attributes express AND-across/OR-within more naturally
          than a single permission list with one match mode.

RequirePermissionAttribute  (sealed class : Attribute)
    ctor(params string[] permissions)
    .Permissions → IReadOnlyCollection<string>
    NOTE: Same shape and composition semantics as RequireRoleAttribute, evaluated against
          IUserContext.HasPermission instead of HasRole.

AuthorizationRequirementEndpointFilter  (sealed class, implements IEndpointFilter)
    .InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        → ValueTask<object?>
    NOTE: Reads RequireRoleAttribute/RequirePermissionAttribute instances off
          context.HttpContext.GetEndpoint()?.Metadata. No-ops (calls next(context) immediately,
          resolving nothing) when neither attribute is present — this filter is safe to register
          globally on every route, mirroring this domain's existing "global registration, no-op
          when inapplicable" pattern already used by TenantContextHubFilter/
          HubExceptionMappingFilter in .SignalR. Resolves IUserContext (12.Security.Abstractions,
          AddScoped) from context.HttpContext.RequestServices. Evaluates each attached attribute
          per the AND-across/OR-within rule above, calling IUserContext.HasRole/HasPermission.
          On the first failing attribute, short-circuits (never calls next()) and returns
          Error.Forbidden(...).ToProblemDetails() via Results.Problem(...) — identical response
          shape to a handler-level AuthorizationBehavior rejection (05.Application), so an API
          consumer sees one consistent error contract regardless of which layer rejected the
          request. An anonymous/unauthenticated caller is correctly rejected with no dedicated
          IsAuthenticated branch in this filter, because HasRole/HasPermission already return
          false unconditionally for every unauthenticated/non-human IUserContext implementation
          shipped in 12.Security (AnonymousUserContext, and SystemUserContext's own HasRole/
          HasPermission are likewise hardcoded false) — verified against their shipped source,
          not assumed.

AuthorizationEndpointFilterExtensions  (static class)
    .RequireRole(this RouteHandlerBuilder builder, params string[] roles) → RouteHandlerBuilder
    .RequireRole(this RouteGroupBuilder builder, params string[] roles) → RouteGroupBuilder
    .RequirePermission(this RouteHandlerBuilder builder, params string[] permissions)
        → RouteHandlerBuilder
    .RequirePermission(this RouteGroupBuilder builder, params string[] permissions)
        → RouteGroupBuilder
    NOTE: Minimal-API sugar — calls .WithMetadata(new RequireRoleAttribute(roles)) /
          new RequirePermissionAttribute(permissions) under the hood. Purely metadata attachment;
          does not itself perform the check or register the filter.
          CORRECTED at Core-phase implementation: RouteGroupBuilder lives in the
          Microsoft.AspNetCore.Routing namespace, not Microsoft.AspNetCore.Builder (which only
          contains RouteHandlerBuilder) — confirmed via a real build failure (CS0246) when only
          Microsoft.AspNetCore.Builder was imported. Same "verify real API shapes, don't trust the
          name" discipline already documented above for Microsoft.OpenApi/SignalR's builder types.

AddSharedKernelAuthorizationFilters(this IServiceCollection) → IServiceCollection
    NOTE: Registers AuthorizationRequirementEndpointFilter as a singleton — mirrors the
          TenantContextHubFilter/HubExceptionMappingFilter DI-registration convention in
          .SignalR, so the filter can take constructor dependencies later even though it is
          stateless today. Unlike AddSharedKernelSignalR's global hub-filter registration via
          HubOptions.AddFilter<T>(), this filter CANNOT auto-attach itself to every endpoint a
          consuming service maps — ASP.NET Core's minimal-API/MVC endpoint routing has no
          equivalent "apply to every mapped endpoint automatically" hook exposed by this
          package's dependency surface. The consumer must additionally call
          .AddEndpointFilter<AuthorizationRequirementEndpointFilter>() on MapControllers() and/or
          each minimal-API route group. This wiring step must be documented prominently in the
          README (DO-10) — it is the one place this domain's "convention over configuration"
          philosophy cannot fully deliver a zero-wiring default, and that must be stated plainly
          rather than implied to work automatically.
```

#### Multi-field validation ProblemDetails (`Errors/`)

> **Status: Shipped end to end (WO-062, P-402).** `SharedKernel.Presentation.WebApi` re-packed to `1.2.0`. Fixes a confirmed silent-data-loss defect: `ValidationException.Errors` (`01.Core`, already shipped) carries every failing field, but `SharedKernelExceptionHandler` previously read only the base `SharedKernelException.Error` property — which `ValidationException`'s own constructor sets to `errors[0]`. A request failing validation on three fields returned a body naming only one.

```text
ValidationProblemDetailsExtensions  (static class)
    .ToProblemDetails(this ValidationException exception, HttpContext? context = null) → ProblemDetails
    NOTE: Groups exception.Errors by field path (MessageArguments[ErrorArgumentNames.PropertyPath], else
          Error.Code) into Extensions["errors"] (messages) and Extensions["errorCodes"] (codes, index-aligned),
          mirroring ASP.NET Core's own built-in ValidationProblemDetails.Errors shape so client tooling
          that already understands that convention (form-binding libraries, generated SDKs) works
          unmodified. Status/Type/traceId resolve identically to the single-Error path (still
          ErrorTypeStatusCodeMap.Resolve(ErrorType.Validation) → 400) — this extension changes the
          BODY shape only, never the status-code mapping. Additive to, never a replacement for,
          ErrorProblemDetailsExtensions.ToProblemDetails(Error) — every non-ValidationException error
          (NotFound/Conflict/Forbidden/Unauthorized/BusinessRule/Unexpected) continues to produce a
          byte-for-byte identical single-error body. As of P-544 (shipped), delegates the
          grouping/localization to the same internal LocalizedDetailResolver.AddErrorsExtensions
          helper ErrorProblemDetailsExtensions now also calls for a non-exception Error.Details
          aggregate (see the Error → ProblemDetails mapping section above), so the exception path
          and the Result<T>→HTTP path produce byte-identical "errors" shapes for the same field errors.

SharedKernelExceptionHandler  (extended)
    NOTE: Gains a ValidationException-specific branch, checked BEFORE the generic
          SharedKernelException.Error fallback, routing through ValidationProblemDetailsExtensions
          instead of the single-Error path.
```

#### Localized `ProblemDetails.Detail` (`Errors/`)

> **Status: Shipped (WO-078, P-484, `SK.14.Core` C-85–C-87 complete).** `01.Core` shipped the real `SharedKernel.Localization`/`ILocalizationCatalog` package (P-482) — this phase was no longer blocked as of this session and was implemented and tested end to end, 192/192 `SharedKernel.Presentation.WebApi.Tests` green (185 pre-existing + 7 new `LocalizedProblemDetailsTests`, zero regressions). Extends `ErrorProblemDetailsExtensions`/`ValidationProblemDetailsExtensions` (never a new, parallel `ProblemDetails`-construction path) so `error.Code` — the same `code` string every `Error` factory already requires — doubles as a translation key at the one place an `Error` crosses into a client-facing response, with **zero change to `01.Core.Primitives.Error` itself**. Implemented as a new internal `Errors/LocalizedDetailResolver.cs` helper shared by both extension methods, rather than duplicating the resolution logic in each.

```text
ErrorProblemDetailsExtensions.ToProblemDetails(this Error, HttpContext? context = null)  (extended)
    NOTE: Resolves ILocalizationCatalog via context?.RequestServices?.GetService<ILocalizationCatalog>()
          — GetService, never GetRequiredService, and BOTH null-checks matter: context itself may
          be null (the parameter's existing default), and a real DefaultHttpContext constructed
          with no RequestServices assigned also returns null from that property (confirmed by a
          real NullReferenceException hit during Tests-phase authoring when only the outer context
          null was guarded — fixed by chaining ?. on RequestServices too). When a catalog IS
          resolved, looks up (error.Code, CultureInfo.CurrentUICulture); the translated string
          becomes Detail. Falls back to error.Message verbatim — NEVER blank — when no catalog is
          registered, context/RequestServices is null, or no translation exists for that
          (code, culture) pair. Title/Status/Type/Extensions["errorCode"]/Extensions["traceId"] are
          completely unchanged by this capability — only Detail's value source becomes
          localizable. A service that never registers ILocalizationCatalog sees byte-identical
          output to today (proven by LocalizedProblemDetailsTests, T-76).

ValidationProblemDetailsExtensions.ToProblemDetails(this ValidationException, HttpContext? context = null)  (extended)
    NOTE: Applies the same lookup/fallback independently to every entry grouped into
          Extensions["errors"] — one field may translate while a sibling field falls back to its
          raw message in the same response, never an all-or-nothing decision for the whole body.

CULTURE RESOLUTION IS NOT THIS PACKAGE'S CONCERN. CultureInfo.CurrentUICulture is read as an
ambient value only — set by 13.ServiceDefaults's AddSharedKernelLocalization() middleware (P-483,
still `○` Pending as of this session — a separate domain's own phase, not part of this shipment)
when a consuming service adopts it. A service that never adopts that middleware still has a
well-defined (if unlocalized) CurrentUICulture; this package never resolves, sets, or throws over
its absence.
```

Confirmed as a deliberate design decision (not a blocker): this capability's `ILocalizationCatalog` resolution DOES take a new `ProjectReference` from `SharedKernel.Presentation.WebApi` to `01.Core/SharedKernel.Localization` — unlike `OpenApiSecuritySchemesOptions.ApiKeyHeaderName` (P-412), which deliberately stayed a plain configurable string to avoid a `SharedKernel.Security.ApiKey`/`.Mtls` reference, `ILocalizationCatalog` is itself a `01.Core` abstraction (the same layer this package already depends on for `Result<T>`/`Error`), not a concrete provider package — referencing it does not violate this domain's "no concrete-provider reference" posture.

#### Security response headers (`SecurityHeaders/`)

> **Status: Shipped end to end (WO-062, P-403).** `SharedKernel.Presentation.WebApi` re-packed to `1.2.0`. `SecurityHeadersOptions`/`SecurityHeadersMiddleware`/`UseSharedKernelSecurityHeaders` now live in `Middleware/`; every header assignment is guarded by `Headers.ContainsKey(...)` and HSTS ships enabled by default per the design below.

```text
SecurityHeadersOptions  (class)
    Hsts / ContentTypeOptions / FrameOptions / ReferrerPolicy / PermissionsPolicy
        NOTE: Each individually toggle-able and value-configurable. HSTS is opt-OUT (on by default),
              not opt-in — but ships with a documented, CAPITALIZED warning that it must be disabled
              or given a short max-age for local HTTP-only development.
    .WithContentSecurityPolicy(string policy) / .WithContentSecurityPolicy(Action<CspBuilder>)
        NOTE: The ONLY way a Content-Security-Policy header is ever set. No default CSP value exists —
              CSP is response-shape-specific (a pure JSON API vs. one also serving Scalar's interactive
              UI) and a wrong default could break this package's own MapSharedKernelOpenApi/Scalar UI.

SecurityHeadersMiddleware  (sealed class)
    .InvokeAsync(HttpContext context, RequestDelegate next) → Task
    NOTE: Every header assignment is guarded by context.Response.Headers.ContainsKey(...) first — an
          inner middleware/endpoint's more-specific header value always wins; this middleware never
          overwrites.

UseSharedKernelSecurityHeaders(this IApplicationBuilder, Action<SecurityHeadersOptions>? configure = null)
    NOTE: Registered immediately after UseSharedKernelCorrelationId() and before UseExceptionHandler().
```

#### CORS policy convention (`Cors/`)

> **Status: Shipped end to end (WO-062, P-404).** `SharedKernel.Presentation.WebApi` re-packed to `1.2.0`. `CorsPolicyOptions`/`CorsPolicyNames`/`CorsPolicyOptionsValidator`/`AddSharedKernelCors` now live in the new `Cors/` folder, with the `IValidateOptions<CorsPolicyOptions>` + `ValidateOnStart()` fail-fast guard wired exactly as designed below.

```text
CorsPolicyOptions  (class)
    AllowedOrigins (ICollection<string>) / AllowCredentials (bool) / AllowedMethods / AllowedHeaders
CorsPolicyNames  (static class)
    .Default → const string
        NOTE: A named policy convention so consuming services reference one discoverable name instead
              of re-inventing policy-name literals.

AddSharedKernelCors(this IServiceCollection, Action<CorsPolicyOptions> configure) → IServiceCollection
    NOTE: Wraps services.AddCors(...). Registers an IValidateOptions<CorsPolicyOptions> guard
          (ValidateOnStart()) that throws a clear, actionable exception at IHost.StartAsync() when
          AllowCredentials = true is combined with an empty/wildcard AllowedOrigins — the classic
          OWASP-catalogued misconfiguration the underlying CORS spec itself forbids but ASP.NET Core
          only throws on at the FIRST real credentialed cross-origin request in production. This
          package makes the dangerous combination impossible to express through its own builder
          surface, fail-fast at startup instead.
```

#### Inbound idempotency-key HTTP boundary (`Idempotency/`)

> **Status: Shipped end to end (WO-062, P-405).** `SharedKernel.Presentation.WebApi` re-packed to `1.2.0`. Closes the gap between `05.Application`'s in-process `IIdempotentRequest`/`IdempotencyBehavior` (duplicate-submission protection for a dispatched command) and `11.Communication.Rest`'s still-queued outbound propagation (P-364) — neither owns the *inbound* HTTP-boundary half: extracting and validating a client-supplied `Idempotency-Key` header before a request ever reaches MediatR.

```text
IdempotencyKeyHeader  (const string, "Idempotency-Key")
    NOTE: DOMAIN-LOCAL for now — mirrors CorrelationIdMiddleware's pre-WO-042 shape, before
          01.Core's WellKnownHeaders existed. 01.Core's WellKnownHeaders has no IdempotencyKey member
          as of this writing (confirmed via direct read) and this capability does not block on one
          being added. A future forwarding-alias promotion (mirroring D-14/C-19's CorrelationId
          precedent) is a natural follow-up only if/when 11.Communication.Rest's P-364 ships and both
          domains want the byte-identical literal.

HttpContextIdempotencyExtensions  (static class)
    .TryGetIdempotencyKey(this HttpContext context, out string? key) → bool
    NOTE: Reads and format-validates (non-empty/non-whitespace, bounded length) the header; returns
          false on missing/malformed input rather than throwing. This is the recommended way for an
          endpoint handler to read the validated value onto a command's IIdempotentRequest.
          IdempotencyKey property before dispatch — this package does not attempt automatic MediatR
          request binding, only the extraction/validation primitive and the guard filter below.

RequireIdempotencyKeyAttribute  (sealed class : Attribute)
IdempotencyKeyRequirementEndpointFilter  (sealed class, implements IEndpointFilter)
    NOTE: A deliberately SEPARATE filter from AuthorizationRequirementEndpointFilter — idempotency-key
          presence is not an authorization concern and must never be folded into that filter. Mirrors
          its exact shape: no-ops when the attribute is absent (safe to register globally), and
          short-circuits a missing/malformed key with Error.Validation(...).ToProblemDetails() → 400
          via the existing single-Error ErrorProblemDetailsExtensions path (this is a request-shape
          validation failure, not a new ErrorType).

AddSharedKernelIdempotencyFilters(this IServiceCollection) → IServiceCollection
    NOTE: Registers IdempotencyKeyRequirementEndpointFilter as a singleton, mirroring
          AddSharedKernelAuthorizationFilters's shape — same non-automatic .AddEndpointFilter<...>()
          wiring requirement and caveat.
```

### `SharedKernel.Presentation.WebApi` — declarative step-up/fresh-authentication (extends Authorization/)

> **Status: Shipped end to end (WO-062, P-406).** `SharedKernel.Presentation.WebApi` re-packed to `1.2.0`. Extends `AuthorizationRequirementEndpointFilter` (never duplicates it) to consume the step-up/fresh-authentication signals `12.Security` shipped in WO-058/P-375 — `IUserContext.AuthenticationMethods`/`.AuthContextClassReference`/`.AuthTime`/`.WasAuthenticatedWith`/`.IsAuthenticationFresherThan(TimeSpan, DateTimeOffset)` — but that no HTTP-boundary declarative consumer existed for until this phase. Confirmed via direct read of the shipped `12.Security.Abstractions/Abstractions/IUserContext.cs` that `IsAuthenticationFresherThan` already exists and takes an explicit `now` parameter (never calls `DateTimeOffset.UtcNow` internally, per the platform's injectable-time convention) — this is the method this filter must call, not a hand-rolled `AuthTime` comparison. Also confirmed `16.Testing`'s `FakeUserContext`/`SecurityTestContextBuilder` already expose settable `AuthTime`/`AuthenticationMethods` — no `16.Testing` gap, no follow-up phase needed.

```text
RequireFreshAuthenticationAttribute  (sealed class : Attribute)
    ctor(int maxAgeSeconds)
    NOTE: Rejects a request whose IUserContext.AuthTime is older than maxAgeSeconds (or absent) with
          Error.Forbidden(...) → 403 — the same rejection shape [RequireRole]/[RequirePermission]
          already use. An anonymous/unauthenticated caller is rejected via the ordinary absent-AuthTime
          path, mirroring the existing no-dedicated-IsAuthenticated-branch discipline.

RequireAuthenticationMethodAttribute  (sealed class : Attribute)
    ctor(params string[] methods)
    NOTE: Rejects a request whose IUserContext.WasAuthenticatedWith(...) does not satisfy at least one
          declared method (OR-within, mirroring RequireRoleAttribute's composition) with the same
          Error.Forbidden(...) shape.

AuthorizationRequirementEndpointFilter  (extended, not duplicated)
    NOTE: Additionally inspects the two attributes above. Composes AND-across with each other and with
          the existing [RequireRole]/[RequirePermission] — no second filter type, no second
          .AddEndpointFilter<...>() registration call required. Resolves IClock (01.Core/
          SharedKernel.Primitives, already referenced) from HttpContext.RequestServices LAZILY, only
          when a RequireFreshAuthenticationAttribute is actually present on the endpoint — an endpoint
          using only [RequireRole]/[RequirePermission] must never require IClock to be registered.

AuthorizationEndpointFilterExtensions  (extended)
    .RequireFreshAuthentication(this RouteHandlerBuilder/RouteGroupBuilder, int maxAgeSeconds)
    .RequireAuthenticationMethod(this RouteHandlerBuilder/RouteGroupBuilder, params string[] methods)
```

#### Optimistic-concurrency ETag / conditional-request helpers (`Concurrency/`, `Http/`)

> **Status: Shipped end to end (WO-062, P-407).** `SharedKernel.Presentation.WebApi` re-packed to `1.2.0`. Bridges `06.Persistence`'s `IHasConcurrency.RowVersion`-based optimistic concurrency (mapped to `Error.Conflict`/409) to HTTP's own standard conditional-request mechanism (`ETag`/`If-Match`, RFC 9110 §13) — additive to, never a replacement for, `Error.Conflict`. **Design decision, not a blocker:** declined requesting a new `ErrorType.PreconditionFailed` from `01.Core` (confirmed absent via direct read of `ErrorType.cs`) — 412 is an HTTP-protocol-native conditional-request outcome that never originates as a domain `Error`/`Result<T>` failure, unlike the platform's `ErrorType`-mapped cases, so it is constructed directly rather than routed through the `Error`/`ErrorTypeStatusCodeMap` machinery.

```text
RowVersionETag  (static class)
    .From(byte[] rowVersion) → string
    NOTE: Produces a well-formed, correctly-quoted ETag header value from a RowVersion-shaped token,
          usable directly on a GET response for a concurrency-tracked resource.

ConditionalRequestExtensions  (static class)
    .TryValidateIfMatch(this HttpContext, string currentETag, out ProblemDetails? problemDetails) → bool
    NOTE: Evaluates an inbound If-Match request header against the resource's current ETag; produces
          a 412 Precondition Failed ProblemDetails on mismatch, built via the shared internal RFC 9457
          shaping helper below — never a hand-rolled literal, never routed through Error/ErrorType.
          Zero change to ErrorTypeStatusCodeMap's existing seven-case mapping or to 06.Persistence's own
          concurrency-conflict behavior; both Error.Conflict/409 and If-Match/412 remain independently
          available — a service may adopt one, the other, or both.
          CORRECTED at Core-phase implementation: built on Microsoft.AspNetCore.Http.Headers.
          RequestHeaders(context.Request.Headers).IfMatch (IList<EntityTagHeaderValue>) and
          Microsoft.Net.Http.Headers.EntityTagHeaderValue — its RFC 9110-strong-comparison member is
          the INSTANCE method .Compare(EntityTagHeaderValue, bool useStrongComparison), not a static
          overload — confirmed via a throwaway reflection probe against the installed net10.0 shared
          framework, not assumed from an older API surface.

(internal) RFC 9457 problem-details shaping helper  (Http/)
    NOTE: Extracted from ErrorProblemDetailsExtensions's existing private ProblemTypeBaseUri
          constant/construction pattern into a shared internal helper — zero behavioral change to
          ErrorProblemDetailsExtensions's existing public output. Reused by the 429 rate-limit-
          rejection helper below so both non-Error HTTP outcomes (412, 429) share one Type-URI/
          traceId-population convention instead of two independently hand-rolled ones.
```

#### Rate-limit rejection → ProblemDetails/429 bridge (`RateLimiting/`)

> **Status: Shipped end to end (WO-062, P-408).** `SharedKernel.Presentation.WebApi` re-packed to `1.2.0`. Closes a gap `13.ServiceDefaults`'s own `AddSharedKernelRateLimiting()` (P-397/WO-061) explicitly deferred here — its README documents a *recipe* asking a consuming service's `OnRejected` callback to shape a 429 `ProblemDetails` "consistent with this domain's conventions," but no real, reusable helper was ever built to receive that handoff, leaving every consumer to hand-roll it (exactly the inline-`ProblemDetails`-construction anti-pattern `ErrorProblemDetailsExtensions`/governance already forbid everywhere else).

```text
RateLimitRejectionProblemDetails  (static class)
    .Create(HttpContext context, TimeSpan? retryAfter = null) → ProblemDetails
    NOTE: Produces a 429 ProblemDetails (RFC 9457 Type URI via the shared Http/ helper from P-407,
          Extensions["traceId"] populated identically to every other error path) plus, when a retry
          duration is supplied, sets the standard Retry-After RESPONSE HEADER (not just a body field,
          via Microsoft.Net.Http.Headers.HeaderNames.RetryAfter) so proxies/client SDKs that already
          understand Retry-After work unmodified. Zero new PackageReference — built entirely on the
          existing Microsoft.AspNetCore.Http/Mvc.ProblemDetails surface. 13.ServiceDefaults's
          AddSharedKernelRateLimiting() is this helper's primary intended caller, referenced by name
          only — no ProjectReference either direction.
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

> **Status: Shipped end to end (WO-062, P-409).** `SharedKernel.Presentation.SignalR` re-packed to `1.0.2`. `AddSharedKernelSignalR` now sets an explicit, conservative platform default for SignalR's own resource-exhaustion-relevant `HubOptions` (`MaximumReceiveMessageSize`/`MaximumParallelInvocationsPerClient`/`ClientTimeoutInterval`/`KeepAliveInterval`), closing the gap where these previously remained at whatever the framework's own current defaults were (not uniformly hardened across SignalR versions; no message-size ceiling is a real resource-exhaustion vector on a long-lived WebSocket/SSE surface).

```text
AddSharedKernelSignalR  (extended)
    NOTE: Sets an explicit, documented, conservative default for MaximumReceiveMessageSize (a concrete
          byte ceiling), MaximumParallelInvocationsPerClient, ClientTimeoutInterval, and
          KeepAliveInterval — applied BEFORE the caller's existing configureHubOptions callback runs,
          so every default remains fully overridable (raise or lower) with no signature change. Pinned
          explicitly even where a value matches SignalR's current framework default, so the platform's
          posture is documented and stable across future SignalR version changes rather than implicit.
          SHIPPED VALUES: MaximumReceiveMessageSize = 32 * 1024 (32 KB), MaximumParallelInvocationsPerClient
          = 1, ClientTimeoutInterval = TimeSpan.FromSeconds(30), KeepAliveInterval = TimeSpan.FromSeconds(15).
          Property types confirmed via reflection against the installed HubOptions before use:
          MaximumReceiveMessageSize is long?, MaximumParallelInvocationsPerClient is a non-nullable int,
          ClientTimeoutInterval/KeepAliveInterval are TimeSpan?.
```

### Payload size / JSON max-depth DoS protection (`PayloadLimits/`)

> **Status: Shipped end to end (WO-063, P-411).** `SharedKernel.Presentation.WebApi` implements this in the new `PayloadLimits/` folder. A repo-wide grep across `14.Presentation` confirmed zero existing `MaxRequestBodySize`/`RequestSizeLimit`/`MaxDepth` wiring anywhere in this domain — every WO-062 perimeter-hardening capability (security headers, CORS, rate limiting) addresses request rate/origin, none addresses request size/shape. This is the one DoS vector with zero coverage prior to this phase. **Confirmed via a real listening-Kestrel-host round trip (not `TestServer`, which does not enforce `IHttpMaxRequestBodySizeFeature` the same way):** a body exceeding `MaxRequestBodySizeBytes` throws `Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException` — a Kestrel-internal type that extends the public `Microsoft.AspNetCore.Http.BadHttpRequestException`, so catching the public base type via ordinary pattern matching correctly handles it — carrying `StatusCode = 413` and an already client-safe `Message` (e.g. `"Request body too large. The max request body size is N bytes."`); this message is explicitly exempted from `SharedKernelExceptionHandler`'s existing dev-only detail-suppression gate, alongside `SharedKernelException`.

```text
PayloadLimitsOptions  (class)
    MaxRequestBodySizeBytes (long, default 1_048_576 / 1 MB)
    MaxJsonDepth            (int, default 32)
    NOTE: Both independently configurable; conservative platform defaults, fully overridable per
          service.

AddSharedKernelPayloadLimits(this IServiceCollection, Action<PayloadLimitsOptions>? configure = null)
    → IServiceCollection
    NOTE: Service-registration-time half — wires MaxJsonDepth into BOTH
          Microsoft.AspNetCore.Http.Json.JsonOptions (Minimal API) and
          Microsoft.AspNetCore.Mvc.JsonOptions.JsonSerializerOptions (MVC, only takes effect when
          MVC services are also registered — must no-op gracefully, never throw, when MVC is
          absent), mirroring this package's existing dual Minimal-API/MVC support pattern
          (ResultHttpExtensions).

UseSharedKernelPayloadLimits(this IApplicationBuilder, Action<PayloadLimitsOptions>? configure = null)
    → IApplicationBuilder
    NOTE: Request-scoped middleware half — sets
          context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize, guarded by
          .IsReadOnly (the feature throws InvalidOperationException once body reading has started
          or when unsupported by the current server) rather than crashing the pipeline. Two-part
          registration mirrors AddSharedKernelCorrelationId/UseSharedKernelCorrelationId.

SharedKernelExceptionHandler  (extended)
    NOTE: Gains a BadHttpRequestException-specific branch, checked BEFORE the generic
          unknown-exception 500 fallback, mapping exception.StatusCode (413 for the
          body-too-large case) through the shared Http/ RFC 9457 shaping helper (P-407) instead of
          forcing 500. MUST be verified against the real installed net10.0 shared framework at
          Core-phase implementation — does BadHttpRequestException actually propagate to
          IExceptionHandler for a raw/Minimal-API body read, or is it intercepted earlier by
          model-binding machinery? — per this domain's "verify real API shapes" discipline.
```

Fully opt-in: a host calling neither extension is byte-identical to today. A JSON-depth violation surfaces via STJ's own existing `JsonException`→400 path — this phase only wires the `MaxDepth` value, it invents no new depth-violation response shape.

### OpenAPI security-scheme completeness — ApiKey + mTLS (`OpenApi/`)

> **Status: Shipped end to end (WO-063, P-412).** `OpenApiExtensions.cs` registered exactly one `OpenApiSecurityScheme` ("Bearer") before this phase, despite `12.Security` shipping three sibling authentication providers (`.Oidc`/Bearer, `.ApiKey`, `.Mtls`) — confirmed by direct source read. A partner team generating a client SDK off this domain's OpenAPI document for a service actually authenticating via `SharedKernel.Security.ApiKey`/`.Mtls` sees a misleading auth contract.
>
> **Two genuine API-shape discoveries made at Core-phase implementation, both verified via reflection before use, not assumed:**
> 1. `Microsoft.OpenApi` 2.0.0's `SecuritySchemeType` enum has **no `MutualTls`/`MutualTLS` member at all** — only `ApiKey`/`Http`/`OAuth2`/`OpenIdConnect` — despite `mutualTLS` being a valid OpenAPI 3.1 security-scheme `type` value per spec. Since `OpenApiSecurityScheme.Type` is strongly typed to that enum, the standard type cannot express this scheme. Resolved via a new internal `OpenApi/MutualTlsSecurityScheme : OpenApiSecurityScheme` subclass overriding the virtual `SerializeAsV31(IOpenApiWriter)` to write the literal `"type": "mutualTLS"` directly — `OpenApiSecurityScheme` is not sealed and its serialization methods are virtual, confirmed via reflection, and a real serialization round trip confirmed the resulting document is valid and correctly referenced.
> 2. Registering each active scheme as its own `OpenApiSecurityRequirement` (via `new OpenApiSecuritySchemeReference(schemeName, document, null)`) **silently serializes as an empty `{}` object** unless `document.RegisterComponents()` is called after `Components.SecuritySchemes` is populated but before the reference is constructed — without it, `OpenApiSecuritySchemeReference.UnresolvedReference` stays `true` and the reference's `Reference.ReferenceV3` never resolves. This is a non-obvious two-step dance (populate components → `RegisterComponents()` → construct references) that any future edit to this transformer must preserve.

```text
OpenApiSecuritySchemesOptions  (class)
    Bearer (bool, default true)          — unchanged default; existing Bearer-only consumers see
                                            zero behavior change if they never touch this parameter
    ApiKey (bool, default false)
    ApiKeyHeaderName (string, default "X-Api-Key")
    MutualTls (bool, default false)
    NOTE: ApiKeyHeaderName stays a plain configurable string — this package does NOT take a new
          ProjectReference on SharedKernel.Security.ApiKey/.Mtls merely to reuse a header-name
          constant. The consuming service's own composition root (which already references
          whichever concrete 12.Security provider it uses) is responsible for passing the matching
          value explicitly, consistent with this package's existing "IUserContext/Abstractions
          only, no concrete provider reference" posture.

AddSharedKernelOpenApi(this IServiceCollection, string title, string? description = null,
                        Action<OpenApiSecuritySchemesOptions>? configureSecuritySchemes = null)
    → IServiceCollection
    NOTE: Additive parameter — omitting it preserves today's unconditional Bearer-only behavior.
          Each active scheme is registered as its OWN SEPARATE OpenAPI security requirement object
          (OR semantics — a request satisfies ANY one active mechanism), never a single combined
          requirement object (which would mean simultaneous-auth-required/AND semantics). This is
          the opposite of this package's usual AND-across-attributes composition rule
          ([RequireRole]/[RequirePermission]) and is easy to get backwards — document explicitly.
          MutualTls registers via Microsoft.OpenApi's OpenAPI 3.1 mutualTLS scheme type; the exact
          enum/type member name must be verified via reflection against the installed
          Microsoft.OpenApi 2.0.0 package before use, mirroring this domain's existing
          Microsoft.OpenApi-namespace correction precedent (types live under Microsoft.OpenApi, not
          Microsoft.OpenApi.Models).
```

### RFC 8594 Sunset/Deprecation headers (`Versioning/`)

> **Status: Shipped end to end (WO-063, P-413).** `AddSharedKernelApiVersioning` previously only set Asp.Versioning's native `ReportApiVersions` (`api-supported-versions`/`api-deprecated-versions` headers) — informational only, carrying no retirement-date commitment. Big-fintech public/partner-facing APIs commonly need machine-readable retirement notice on deprecated versions; RFC 8594 is the standard mechanism.
>
> **Deliberately NOT built on `Asp.Versioning.Http`'s own `Policies.Sunset(...)`/`.Deprecate(...)`/`SunsetPolicyManager`/`DeprecationPolicyManager`/`DefaultApiVersionReporter` surface, despite that surface existing and being registered by `AddApiVersioning()` today.** A real round trip (constructing a policy via `options.Policies.Sunset(name, apiVersion).SetEffectiveDate(...).Link(...)`, then invoking the already-registered `IReportApiVersions.Report(HttpResponse, ApiVersionModel)`) never produced `Sunset`/`Deprecation`/`Link` headers for a policy keyed on an empty/unnamed policy name within the session's available time — the exact name-matching semantics `IPolicyManager<T>.TryGetPolicy(string name, ApiVersion, out T)` expects were not reverse-engineered successfully. Rather than block on that, this phase's own independent `ApiVersionLifecycleOptions` registry (a plain `Dictionary<ApiVersion, (DateTimeOffset? SunsetDate, Uri? Successor)>`) was implemented and proven correct via a real end-to-end host round trip instead. **A future revisit to actually drive Asp.Versioning's own policy system (rather than this parallel registry) is a legitimate simplification opportunity if the naming semantics are ever cracked** — flag this to any future session touching `Versioning/`.
>
> **Mechanism confirmed via a real round trip, not assumed:** the response-shaping component is `ApiVersionLifecycleMiddleware`, self-inserted via `ApiVersionLifecycleStartupFilter` (an `IStartupFilter`) rather than requiring a second `Use...` call — this is the standard ASP.NET Core technique for a library to insert middleware with zero host-side wiring, mirroring how `ReportApiVersions`'s own headers already work with no `Use...` call. The middleware reads `HttpContext.RequestedApiVersion` (confirmed to be an extension **property**, not a callable method, on `Microsoft.AspNetCore.Http.HttpContextExtensions`) and `context.GetEndpoint()?.Metadata.GetMetadata<Asp.Versioning.ApiVersionMetadata>().Map(ApiVersionMapping.Explicit).DeprecatedApiVersions` from inside an `HttpResponse.OnStarting` callback (mirroring `CorrelationIdMiddleware`/`SecurityHeadersMiddleware`'s existing discipline) so both are reliably populated regardless of where in the pipeline the middleware itself executes.

```text
ApiVersionLifecycleOptions  (class)
    NOTE: Per-ApiVersion registry of an optional SunsetDate (DateTimeOffset?) and an optional
          successor Uri? (for Link: rel="successor-version"). Deprecation status is read from
          Asp.Versioning's OWN already-existing per-version Deprecated declaration
          ([ApiVersion("1.0", Deprecated = true)] / ApiVersionModel.DeprecatedApiVersions) rather
          than re-declaring a parallel deprecated flag here.

AddSharedKernelApiVersioning(..., Action<ApiVersionLifecycleOptions>? configureLifecycle = null)
    NOTE: Additive parameter on the EXISTING method — never a second registration method.

(new response-shaping component, Versioning/)
    NOTE: Exact mechanism (middleware vs. endpoint filter) decided at Core phase based on where
          HttpContext.RequestedApiVersion/the endpoint's ApiVersionModel metadata is reliably
          available. Sets, per response: Sunset (RFC 8594, HTTP-date format via
          DateTimeOffset.ToString("R") / RFC 1123 pattern — never an arbitrary string) when the
          resolved version has a declared SunsetDate; Deprecation (boolean form this phase) when
          the resolved version is in DeprecatedApiVersions, independent of whether a sunset date is
          set; Link: <successor-uri>; rel="successor-version" only when a successor Uri is declared
          alongside a sunset date.
```

Zero behavior change for a service declaring no sunset date on any version. Additive to, never a rework of, the existing `ReportApiVersions` header family — both may appear on the same response simultaneously.

### Structured security-audit logging across rejection paths (extends `Authorization/`, `Idempotency/`, `Cors/`, `RateLimiting/`)

> **Status: Shipped end to end (WO-063, P-414).** A grep confirmed exactly three `[LoggerMessage]` call sites existed in the whole domain before this phase (`CorrelationIdMiddleware` 14000, `SharedKernelExceptionHandler` 14001, `HubExceptionMappingFilter` 14100) of the reserved `14000`–`14999` range — effectively unused. None of the four WO-062 HTTP-rejection paths logged anything. `12.Security` (WO-057/P-371) and `13.ServiceDefaults` (WO-061/P-395) both independently added this class of security-audit logging for their own rejection/denial paths — `14.Presentation`, the layer where an external caller's 401/403/429 actually surfaces, was the one domain in the chain still logging none of it.
>
> **`RateLimitRejectionProblemDetails` is a pure static helper with no DI-constructed instance**, so its logger cannot be constructor-injected like the other three sites — it resolves `ILogger` per call via `context.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger(...)`, guarded with `?.` because a bare/manually-constructed `HttpContext` (as several of this method's own pre-existing unit tests use) may have a `null` `RequestServices` — confirmed by a real test failure (`ArgumentNullException` on the un-guarded first attempt) before the guard was added.

```text
AuthorizationRequirementEndpointFilter       [LoggerMessage] EventId = 14002 (rejection path)
IdempotencyKeyRequirementEndpointFilter      [LoggerMessage] EventId = 14003 (rejection path)
CorsPolicyOptionsValidator                   [LoggerMessage] EventId = 14004 (startup-failure path)
RateLimitRejectionProblemDetails             [LoggerMessage] EventId = 14005 (rejection path)
    NOTE: Non-PII/non-secret context only — endpoint display name, the specific requirement/
          attribute type that failed, CORS validation failure reason, rate-limit policy name.
          NEVER raw bearer tokens, API keys, certificate bytes, or full ClaimsPrincipal dumps —
          mirrors 12.Security's (WO-057/P-371) established no-secret-material logging discipline.
```

### Caller-supplied correlation-id format validation (extends `Middleware/`)

> **Status: Shipped end to end (WO-063, P-415).** `CorrelationIdMiddleware.ResolveCorrelationId` accepted any non-empty/non-whitespace caller-supplied header value verbatim before this phase — no shape check, no length bound — before writing it into `Activity` baggage, the response header, and every downstream structured log record via `13.ServiceDefaults`'s `BaggageLogRecordProcessor`. An unvalidated, caller-controlled string flowing directly into OTel baggage is a log-injection/oversized-baggage-propagation vector — the same trust-boundary class this platform already fixed twice elsewhere (`11.Communication`'s GUID-fallback defect, WO-056; `13.ServiceDefaults`'s forwarded-header trust boundary, WO-061) but never yet at the point a raw correlation-id header first enters the system.
>
> A rejected value's raw content is never logged, only its length (`Log.CorrelationIdRejected(logger, value.Length)`, `EventId` 14006) — logging the rejected raw string itself would recreate exactly the injection vector this validation defends against.
>
> `CorrelationIdMiddleware`'s constructor now also takes an optional `CorrelationIdOptions? options = null`, falling back to a fresh default-options instance — so a host that calls `UseSharedKernelCorrelationId()` without ever calling `AddSharedKernelCorrelationId()` (previously a genuine no-op registration, now a real one) still gets the default-safe validation applied automatically rather than crashing on an unresolvable DI dependency.

```text
CorrelationIdOptions  (class)
    MaxLength (int, conservative default e.g. 128)
    AllowedCharacterPattern  — a safe-but-permissive allowlist covering GUID/ULID/general safe-token
                               shapes (alphanumerics plus -_:.)

AddSharedKernelCorrelationId(this IServiceCollection, Action<CorrelationIdOptions>? configure = null)
    NOTE: Additive parameter — default values preserve today's behavior for any well-formed
          caller-supplied value.

CorrelationIdMiddleware.ResolveCorrelationId  (extended)
    NOTE: Rejects (falls back to freshly generating, exactly as it already does for an absent/
          whitespace header) a caller-supplied value exceeding MaxLength or containing a character
          outside AllowedCharacterPattern — BEFORE that value ever reaches HttpContext.Items,
          Activity.SetBaggage, or the response header. A well-formed value (GUID, ULID, or another
          safe token shape matching the default pattern) continues to be preserved unchanged — not
          a breaking change for existing well-behaved callers.
```

### File/multipart upload size & content-type validation (`Uploads/`)

> **Status: Shipped end to end (WO-063, P-416).** No file/multipart upload size or content-type validation helper existed anywhere in this package before this phase — confirmed absent, not merely undocumented. Document-heavy fintech workflows (KYC documents, statements, dispute evidence) routinely accept uploads directly through the API boundary before handing them to `08.Storage`; every consuming service today independently invents size/type validation with no shared, tested helper. Content-type comparison strips any `;`-delimited parameter (e.g. `charset=utf-8`) before matching against `AllowedContentTypes`, and the magic-byte check reads exactly `signature.Length` leading bytes via `HttpRequest.EnableBuffering()` + a seek-back to position `0` so the endpoint handler still sees the full, unconsumed body.

```text
UploadValidationOptions  (class)
    MaxSizeBytes, AllowedContentTypes (collection), AllowedMagicBytes (optional dictionary
    mapping content-type → signature bytes for a deeper check)

AddSharedKernelUploadValidation(this IServiceCollection, Action<UploadValidationOptions> configure)
    → IServiceCollection
    NOTE: Mirrors AddSharedKernelCors's registration shape.

RequireValidatedUploadAttribute  (sealed class : Attribute)
    ctor(long? maxSizeBytes = null, params string[] allowedContentTypes)
    NOTE: Per-endpoint override args fall back to the global UploadValidationOptions defaults when
          omitted (different upload endpoints — a KYC-document endpoint vs. an avatar endpoint —
          routinely need very different limits).

UploadValidationEndpointFilter  (sealed class, implements IEndpointFilter)
    NOTE: Mirrors RequireIdempotencyKeyAttribute/IdempotencyKeyRequirementEndpointFilter's exact
          global-registration/no-op-when-absent shape. Declared Content-Type/Content-Length checked
          before the body is fully buffered; when a magic-byte signature is configured for the
          declared content type, buffers the request body (HttpRequest.EnableBuffering(), seeking
          back to position 0 after the peek) to compare leading bytes; short-circuits a mismatch
          with a 415/400 ProblemDetails via the existing shared Http/ shaping path.
```

**Explicitly and repeatedly out of scope, documented in capitals in both XML docs and README:** virus/malware scanning, antivirus-engine integration — this is a boundary-shape check ONLY, never a replacement for a real scanning pipeline. No third-party MIME-detection library is added — a small, locally-maintained, extensible magic-byte signature table is sufficient.

### `SharedKernel.Presentation.SignalR` — hub-level invocation rate limiting & argument validation (extends `Filters/`, `Extensions/`)

> **Status: Shipped end to end (WO-063, P-417).** P-409's `HubOptions` defaults are connection-level only — no per-method invocation rate limit or argument-payload validation existed before this phase. A single connection with an unbounded invocation rate can still exhaust server resources or hammer a downstream dependency even with P-409's defaults in place; real-time fintech workloads (live trading updates, payment status streams) are exactly the ones most likely to expose a hub method to high-frequency invocation.
>
> `HubInvocationRateLimitOptions` bundles BOTH the rate-limit knobs (`PermitLimit`/`Window`) AND the argument-shape knobs (`MaxStringArgumentLength`/composable `ArgumentValidators`) into one options type, evaluated by one filter — not two separate filter types — since `AddSharedKernelSignalR` only exposes a single new `configureRateLimit` parameter. Every check defaults to disabled (`PermitLimit`/`MaxStringArgumentLength` both `int?` defaulting `null`, `ArgumentValidators` an empty collection) so the filter is a genuine, zero-behavior-change no-op until explicitly configured — the filter itself is always registered.
>
> **Verified end-to-end with a live `HubConnection`, not just DI resolution:** invocations within `PermitLimit` succeed; the invocation that exceeds it throws a `HubException` whose message is the filter's own specific `"Too many requests. Please slow down."` text, unmodified — proving the composition-hazard fix below actually works together with this new filter, not merely that both exist independently. An oversized `string` argument is rejected the same way with its own specific message, before the target method body ever executes.

```text
HubInvocationRateLimitFilter  (sealed class, implements IHubFilter)
    NOTE: Built on System.Threading.RateLimiting primitives — the same underlying library
          13.ServiceDefaults's AddSharedKernelRateLimiting()/P-397 wraps for HTTP, used here
          DIRECTLY against a hub connection since ASP.NET Core's HTTP rate-limiting middleware does
          not apply to SignalR invocations. A RateLimiter instance is created lazily per connection
          (stored in Context.Items, mirroring TenantContextHubFilter's existing per-connection
          storage pattern) and consulted in InvokeMethodAsync before the target method body runs.
          A rejected invocation throws HubException carrying a specific, caller-safe "too many
          requests" message.

AddSharedKernelSignalR(..., Action<HubInvocationRateLimitOptions>? configureRateLimit = null)
    NOTE: Additive parameter. null/omitted means the filter still registers but no-ops (safe to
          register globally, mirrors every other filter in this package) — zero behavior change
          for a host that does not opt in.

HubExceptionMappingFilter  (extended — GENUINE COMPOSITION HAZARD FOUND AND FIXED)
    NOTE: The existing catch-all had NO branch recognizing an already-thrown HubException as
          terminal — a HubException raised by the new rate-limit filter would fall into the
          existing "unknown exception" branch and be RE-WRAPPED into the generic
          HubException("An unexpected error occurred."), silently discarding the specific
          rate-limit message the whole point of this capability is to surface. Gains a
          catch (HubException) { throw; } branch, checked FIRST, so an already-well-formed
          HubException (from this filter or any future filter) always passes through unchanged
          regardless of hub-filter registration order. This is now a standing hub-filter-
          composition rule for future filter authors — see SignalR hub filter rules below.

(composable argument-payload size/shape validation check)
    NOTE: Additive to and distinct from MaximumReceiveMessageSize (P-409) — that caps the whole
          transport message, this validates individual argument values before the target method
          body executes.
```

**Scaffold-phase verification (WO-063, S-27 — confirmed):** `System.Threading.RateLimiting` ships transitively via the existing `FrameworkReference Microsoft.AspNetCore.App` on `net10.0` — confirmed via a real build probe (a scratch `.cs` file referencing `RateLimiter`/`TokenBucketRateLimiter` with zero added `PackageReference`, `dotnet build -c Release` succeeded with 0 errors, scratch file deleted immediately after). No new `PackageReference` is needed for `HubInvocationRateLimitFilter`/`HubInvocationRateLimitOptions` at Core phase.

### `SharedKernel.Presentation.SignalR` — CORS/negotiate-endpoint origin policy integration (extends `Extensions/`)

> **Status: Shipped end to end (WO-063, P-418).** `AddSharedKernelSignalR` had no CORS wiring, no origin-check guidance, and no documented interaction with the hub's `/negotiate` endpoint before this phase — confirmed absent by direct source read. A service correctly adopting P-404's deny-by-default CORS guard for its REST endpoints but forgetting the SignalR hub (a separate ASP.NET Core endpoint requiring its own explicit CORS policy attachment — a well-documented real-world SignalR gotcha) ends up with either an inaccessible hub or an overly permissive fallback policy reached for out of frustration.
>
> **Two hub-endpoint marker types confirmed via reflection against the installed assemblies, not assumed, before use:**
> 1. `Microsoft.AspNetCore.SignalR.HubMetadata` (public, in `Microsoft.AspNetCore.SignalR.Core`) is the marker `MapHub<THub>()` attaches identifying "this endpoint belongs to hub type `HubType`."
> 2. The actual "a CORS decision was made for this endpoint" marker `RequireCors(...)`/`EnableCorsAttribute`/`DisableCorsAttribute` all implement is `Microsoft.AspNetCore.Cors.Infrastructure.ICorsMetadata` — **not** `ICorsPolicyMetadata` as first assumed from the type name; a real minimal-host round trip mapping one hub with `.RequireCors(...)` and one without proved `RequireCors(policyName)` attaches a concrete `Microsoft.AspNetCore.Cors.EnableCorsAttribute`, which implements `ICorsMetadata` (the general marker, covering both an applied policy and an explicit `DisableCorsAttribute` opt-out) but not `ICorsPolicyMetadata` (a narrower interface `EnableCorsAttribute` does not implement in this ASP.NET Core version).
>
> Each hub's `/negotiate` companion endpoint is skipped during the scan (identified by `Microsoft.AspNetCore.Http.Connections.NegotiateMetadata`) — confirmed via the same round trip that it always carries identical CORS metadata to its primary hub endpoint, so evaluating it too would only produce a duplicate warning for the same underlying gap.

```text
(design decision, not a blocker): declined a new ProjectReference from SharedKernel.Presentation.
SignalR to SharedKernel.Presentation.WebApi's CorsPolicyOptions — the two packages remain
deliberately independent API surfaces (see "Packages" above); a pure real-time host must not be
forced to pull in Asp.Versioning/Microsoft.AspNetCore.OpenApi/Scalar.AspNetCore transitively just
to get a CORS integration point.

(startup diagnostic check, Extensions/)
    NOTE: A hosted-service/IHostApplicationLifetime.ApplicationStarted-triggered scan over
          EndpointDataSource.Endpoints, flagging any SignalR-hub-shaped endpoint lacking CORS
          metadata (ICorsMetadata/an applied policy) via [LoggerMessage] Warning,
          EventId = LoggingEventIdRanges.Presentation + 102 (14102). The exact endpoint-metadata
          shape used to recognize "this endpoint is a mapped SignalR hub" (e.g. HubMetadata or an
          equivalent marker type carried by MapHub<THub>()'s endpoint conventions) must be verified
          via reflection at Core-phase implementation, per this domain's established discipline —
          not assumed from memory.
```

A worked "hub plus CORS composition" example (`endpoints.MapHub<THub>().RequireCors(CorsPolicyNames.Default)`, composing the WebApi package's own named policy constant by reference/documentation only, zero code coupling) is added to `SharedKernel.Presentation.SignalR/README.md`.

### `SharedKernel.Presentation.Grpc` — shipped public surface

> **Status: Shipped (WO-074, P-468, `SK.14.Core` C-75–C-84 complete).** Every contract below is the real, implemented public surface — verified by a genuine gRPC-over-HTTP2 in-process test server (never a mocked `ServerCallContext`), 36/36 tests green. Two open design questions from the original design lock are now resolved by direct empirical proof rather than assumption: (1) `ServerCallContext.GetHttpContext()?.GetEndpoint()?.Metadata` DOES surface attributes applied directly to a gRPC service implementation method — confirmed by 8 passing `GrpcAuthorizationInterceptorIntegrationTests` exercising `[RequireRole]`/`[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` applied to real service methods against a real host; the reflection fallback contemplated in the original design was never needed. (2) `Grpc.Core.ServerCallContextExtensions.GetHttpContext(this ServerCallContext)` is confirmed present and correctly wired in the installed `Grpc.AspNetCore.Server` `2.80.0`/`net10.0`. `error.Description` referenced in an earlier design draft was never a real `Error` member — the real, shipped mapping uses `Error.Message` throughout, consistent with `ErrorProblemDetailsExtensions`. See "Why server-side gRPC lives in `14.Presentation`" and "Why `.Grpc` references `.WebApi`" above for the placement/coupling rationale.

#### Error → `RpcException` mapping (`Errors/`)

```text
GrpcStatusCodeMap  (static class)
    .Resolve(ErrorType type) → StatusCode
    NOTE: A SIBLING to ErrorTypeStatusCodeMap, never a merge — HTTP status codes and gRPC
          StatusCode have no clean 1:1 correspondence. Locked mapping:
              Validation   → InvalidArgument
              Unauthorized → Unauthenticated
              Forbidden    → PermissionDenied
              NotFound     → NotFound
              Conflict     → Aborted           (gRPC's own doc: "a concurrency issue such as a
                                                 sequencer check failure or transaction abort" —
                                                 closer to optimistic-concurrency Conflict than
                                                 AlreadyExists)
              BusinessRule → FailedPrecondition (closest gRPC analogue to HTTP 422)
              Unexpected   → Internal
          Any unmapped ErrorType (including None) falls back to Unknown — gRPC's own "no more
          specific error is applicable" status, the protocol-native analogue of the HTTP map's
          500 fallback. Single source of truth for ErrorType→gRPC-status mapping — an inline
          switch duplicating this table anywhere else is a platform violation, mirroring
          ErrorTypeStatusCodeMap's own rule.
```

#### `Result<T>` → gRPC boundary (`Results/`)

```text
GrpcResultExtensions  (static class)
    .ToGrpcResult(this Result result) → void
    .ToGrpcResult<T>(this Result<T> result) → T
    NOTE: Failure always routes through GrpcStatusCodeMap.Resolve(error.Type) →
          new RpcException(new Status(code, error.Message)) — never a hand-rolled
          RpcException at a service-method call site. The gRPC-boundary sibling to
          ResultHttpExtensions; a failure travels as the RpcException status, never a response
          DTO — this package never references 04.Contracts at all (see below).
```

#### Global exception handling (`Interceptors/`)

```text
GrpcExceptionInterceptor  (sealed class : Grpc.Core.Interceptors.Interceptor)
    NOTE: MUST override all four server interceptor methods — UnaryServerHandler,
          ClientStreamingServerHandler, ServerStreamingServerHandler,
          DuplexStreamingServerHandler — never unary-only; unlike single-request/response HTTP,
          gRPC has three streaming call shapes that equally need exception mapping. Known
          SharedKernelException subtypes (01.Core) map via Error → GrpcStatusCodeMap.Resolve →
          RpcException(new Status(code, error.Message)) — caller-safe, no stack trace, no
          internal type names. Unknown exceptions map to Internal with detail suppressed outside
          IHostEnvironment.IsDevelopment(), logged at LogLevel.Error before the exception
          surfaces — the direct structural counterpart to SharedKernelExceptionHandler/
          HubExceptionMappingFilter.
```

#### Inbound correlation & tenant metadata (`Interceptors/`)

```text
GrpcCorrelationInterceptor  (sealed class : Interceptor)
    NOTE: Reads the inbound correlation-id gRPC metadata key from ServerCallContext.RequestHeaders;
          generates one when absent (mirrors CorrelationIdMiddleware). CONFIRMED at Core-phase by
          direct source read of SharedKernel.Communication.Grpc.Interceptors
          .CorrelationTracingInterceptor.CorrelationIdKey — both sides source the identical
          01.Core WellKnownHeaders.CorrelationId ("X-Correlation-Id") literal; proven byte-for-byte
          by a real round-trip integration test (GrpcCorrelationTenantRoundTripTests) using the
          real client interceptor via AddSharedKernelGrpcCommunication().AddGrpcClient<T>(), not a
          hand-rolled metadata stand-in. Grpc.Core.Metadata normalizes key casing internally
          (confirmed empirically — Metadata.Add("X-Correlation-Id", ...) round-trips through
          Metadata.GetValue with either casing), so the uppercase-hyphenated literal is safe on
          both the read and write side. Sets Activity.SetBaggage identically to the HTTP
          middleware so 13.ServiceDefaults's BaggageLogRecordProcessor covers gRPC too, with zero
          ProjectReference on SharedKernel.ServiceDefaults.

GrpcTenantContextInterceptor  (sealed class : Interceptor)
    NOTE: Mirrors TenantContextHubFilter's exact shape (call-scoped tenant attachment,
          non-rejecting). Resolves ITenantProvider (12.Security.Abstractions) via
          ServerCallContext.GetHttpContext()?.RequestServices — Grpc.Core.ServerCallContextExtensions
          .GetHttpContext(this ServerCallContext) CONFIRMED present in the installed
          Grpc.AspNetCore.Server 2.80.0/net10.0 assembly (verified via reflection over the real
          installed DLL, then proven functionally by GrpcTenantContextInterceptor's own passing
          integration tests). Stores the resolved TenantId in ServerCallContext.UserState for the
          call's lifetime (the gRPC per-call analogue of Context.Items/HttpContext.Items). Does
          not reject calls with no resolvable tenant. IMPORTANT DESIGN NOTE (proven by the T-71
          round-trip test): this interceptor NEVER reads gRPC metadata directly — it only ever
          resolves ITenantProvider, exactly like TenantContextHubFilter. A consuming service that
          wants the client-forwarded x-tenant-id metadata (written by SharedKernel.Communication
          .Grpc's TenantIdInterceptor) to actually populate the tenant seen here must supply its
          own ITenantProvider implementation that reads that header — this package deliberately
          does not do that itself, mirroring the same "no direct metadata coupling" boundary this
          domain already draws for HTTP (CorrelationIdMiddleware reads a header directly, but
          tenant resolution is always via ITenantProvider, never a header read inside this domain).
```

#### Declarative authorization reuse (`Interceptors/`)

```text
GrpcAuthorizationInterceptor  (sealed class : Interceptor)
    NOTE: Reuses RequireRoleAttribute/RequirePermissionAttribute/RequireFreshAuthenticationAttribute/
          RequireAuthenticationMethodAttribute VERBATIM from SharedKernel.Presentation.WebApi's
          Authorization/ namespace — one authorization dialect, not two (see "Why .Grpc references
          .WebApi" above). CONFIRMED at Core-phase (no reflection fallback needed): a gRPC service
          method's custom attributes DO surface via
          ServerCallContext.GetHttpContext()?.GetEndpoint()?.Metadata — the same endpoint-metadata
          mechanism AuthorizationRequirementEndpointFilter already reads for Minimal API/MVC, since
          ASP.NET Core's gRPC hosting (Grpc.AspNetCore.Server) integrates with the same endpoint
          routing system and reads a service class/method's attributes into
          Endpoint.Metadata/EndpointMetadata the same way MVC does. Proof: 8/8 passing
          GrpcAuthorizationInterceptorIntegrationTests exercise [RequireRole]/
          [RequireFreshAuthentication]/[RequireAuthenticationMethod] applied directly to a real
          TestService.TestServiceBase override method against a real gRPC-over-HTTP2 host — allow,
          deny, anonymous-caller, fresh/stale-auth, and matching/non-matching-method paths all pass.
          The identical AND-across/OR-within composition and IUserContext.HasRole/HasPermission/
          IsAuthenticationFresherThan/WasAuthenticatedWith evaluation applies unchanged from the
          HTTP side. Rejection throws RpcException mapped from Error.Forbidden(...) through
          GrpcStatusCodeMap (→ PermissionDenied) — the gRPC-boundary sibling to the HTTP path's
          Error.Forbidden(...).ToProblemDetails(). An endpoint with none of the four attributes
          never resolves IUserContext at all (proven by Echo_NoAuthorizationAttribute_
          NeverResolvesIUserContext with IUserContext deliberately unregistered).
```

#### DI extensions (`Extensions/`)

```text
AddSharedKernelGrpc(this IServiceCollection, Action<GrpcServiceOptions>? configure = null)
    → IServiceCollection
    NOTE: Wraps services.AddGrpc(options => { options.Interceptors.Add<GrpcExceptionInterceptor>();
          options.Interceptors.Add<GrpcCorrelationInterceptor>();
          options.Interceptors.Add<GrpcTenantContextInterceptor>();
          options.Interceptors.Add<GrpcAuthorizationInterceptor>(); ... }) — registering all four
          interceptors globally via GrpcServiceOptions.Interceptors, the gRPC-native equivalent of
          HubOptions.AddFilter<T>(). UNLIKE HTTP's AuthorizationRequirementEndpointFilter (which
          needs a per-route/group .AddEndpointFilter<T>() call — this domain's one documented
          "convention over configuration" gap, DO-10), GrpcServiceOptions.Interceptors genuinely
          auto-attaches to every mapped gRPC service with zero further per-service wiring — a
          strictly better story than the HTTP authorization filter's manual-wiring caveat. Also
          sets a conservative default on GrpcServiceOptions.MaxReceiveMessageSize (mirroring
          PayloadLimitsOptions/P-409's SignalR HubOptions defaults) BEFORE the caller's configure
          callback runs, so it remains fully overridable.
```

Never references `04.Contracts` — protobuf-generated messages are this package's only wire-contract surface, a named Hard-rule exception mirroring `SharedKernel.Communication.Grpc`'s existing P-163 rule; verified: no file under this package's production source imports `SharedKernel.Contracts`. No new `13.ServiceDefaults` telemetry entry point is planned or needed — ASP.NET Core's existing server-side OpenTelemetry instrumentation already traces the Kestrel/HTTP2 pipeline gRPC calls ride on, the same pipeline HTTP/1.1 endpoints are already traced through with no `14.Presentation`-specific `WithXTelemetry` entry. `MaxReceiveMessageSize` platform default is `4 * 1024 * 1024` (4 MiB), applied before the caller's `configure` callback. `EventId`s are assigned in the newly-declared `14200`–`14299` sub-block (third `14.Presentation` package, after `.WebApi` = `14000`–`14099` and `.SignalR` = `14100`–`14199`): `GrpcExceptionInterceptor.Log.UnhandledGrpcException` = `14200`, `GrpcAuthorizationInterceptor.Log.AuthorizationRequirementRejected` = `14201` — both pinned by reflection-based regression tests (`LoggerMessageEventIdTests`), never triggering the log call at runtime, mirroring `.WebApi`'s established T-11 technique.

---

## Implementation Rules

### ProblemDetails rules

- `Error.ToProblemDetails()` is the only permitted way to convert an `Error` into an HTTP error body. Hand-rolled `ProblemDetails` construction inline in endpoint/controller code is a platform violation.
- `ErrorTypeStatusCodeMap.Resolve` is the single source of truth for `ErrorType` → HTTP status mapping. Do not duplicate this switch anywhere else.
- `SharedKernelExceptionHandler` must never leak exception messages or stack traces outside `IHostEnvironment.IsDevelopment()`.
- `ProblemDetails.Extensions["traceId"]` must always be populated when `Activity.Current` is non-null — this is the platform's primary "give support this ID" field surfaced to API consumers.
- **Multi-field validation errors (WO-062, P-402 — shipped):** `ValidationException` is the one case where a single `Error` is insufficient — `ValidationProblemDetailsExtensions.ToProblemDetails(ValidationException, ...)` must be used instead of the single-`Error` path, grouping every failing field's `Error` by field path (its `PropertyPath` argument, else its `Code`) into `Extensions["errors"]`, with the codes in the parallel `Extensions["errorCodes"]`. Every other `SharedKernelException` subtype continues through the single-`Error` `ErrorProblemDetailsExtensions.ToProblemDetails(Error)` path unchanged — this is a narrow, `ValidationException`-specific exception to the "one `Error`, one body" model, not a general precedent for other exception types to grow their own bespoke body shape.
- **Localized `Detail` (WO-078, P-484 — shipped) never introduces a second `ProblemDetails`-construction path.** `Error.ToProblemDetails()`/`ValidationProblemDetailsExtensions` remain the sole entry points; localization only changes where `Detail`'s string value comes from (an optional `ILocalizationCatalog` lookup keyed by `error.Code`), never `Title`/`Status`/`Type`/`Extensions["errorCode"]`/`Extensions["traceId"]`, and never the `ErrorType`→status mapping. `error.Message` remains the mandatory, never-blank fallback for an unregistered catalog or an untranslated code — an un-translated error must behave exactly as it does today. See the "Localized `ProblemDetails.Detail`" contract above.
- **HTTP protocol-level outcomes that never originate as a domain `Error` (WO-062, P-407/P-408 — shipped) are never routed through `Error`/`ErrorType`.** A 412 Precondition Failed (`If-Match` mismatch) and a 429 Too Many Requests (rate-limit rejection) are both HTTP-boundary-native outcomes with no corresponding `Result<T>` failure ever produced deeper in the stack — unlike `NotFound`/`Conflict`/`Validation`/etc., which represent application/domain failure categories translated to HTTP as a deliberate mapping step. These two outcomes are built via a small shared internal RFC 9457 shaping helper (extracted from `ErrorProblemDetailsExtensions`'s existing `Type`-URI/`traceId` construction pattern) instead of growing the `ErrorType` enum for outcomes that were never an `Error` to begin with. Do not propose a new `ErrorType` member for a future HTTP-protocol-native outcome without first asking whether it is genuinely a domain/application failure category (belongs in `ErrorType`) or a pure HTTP-boundary concern (belongs in this shared shaping helper instead).

### `Result<T>` HTTP boundary rules

- `ResultHttpExtensions` are the only permitted `Result<T>` → HTTP conversion, and RFC 9457 `ProblemDetails` is the platform's only HTTP error format. Never wrap a response in a success/error envelope DTO (`{ isSuccess, value, error }`) — none exists in `04.Contracts`, and a second format would break `11.Communication.Rest`'s `ReadResultAsync<T>`.
- The Minimal API overloads return `IResult`; the MVC overloads return `ActionResult`/`ActionResult<T>`. There is no third "auto-detect host model" overload — callers pick the form matching their hosting model explicitly.

### Declarative role/permission authorization rules (WO-058, P-381 — Core shipped)

- `[RequireRole]`/`[RequirePermission]` must always evaluate through `IUserContext.HasRole`/`HasPermission` — never `ClaimTypes.Role`, never raw `ClaimsPrincipal`/`Claim` inspection, and never the built-in ASP.NET Core `[Authorize(Roles = "...")]` attribute. That built-in attribute reads `ClaimTypes.Role` directly, bypassing the per-scheme `IUserContextMapper` that decides which claims carry roles and permissions (P-546); `HasRole`/`HasPermission` compare ordinally. A consuming service that mixes `[Authorize(Roles=...)]` and `[RequireRole(...)]` on different endpoints has silently reintroduced that fragility on the former; this package's own docs must call this out.
- A failed check must always produce a `ProblemDetails` body via `Error.ToProblemDetails()` — never a bare, body-less ASP.NET Core 403. This is what makes an HTTP-boundary authorization rejection indistinguishable, from the API consumer's point of view, from an in-process `05.Application` `AuthorizationBehavior` rejection.
- `AuthorizationRequirementEndpointFilter` must remain a single global filter that no-ops when neither attribute is present — never a per-endpoint conditionally-registered filter. This mirrors the SignalR hub filter rule below ("prefer global registration") applied to the HTTP surface: one filter, metadata-driven, safe to attach to every route.
- Composition is AND across stacked attributes, OR within one attribute's role/permission list. This is a fixed, documented rule — do not add a configurable combination mode without a new Design phase; the acceptance criteria for this capability only requires the two composition primitives already described.
- This filter is **not** a substitute for `05.Application`'s `AuthorizationBehavior`/`IAuthorizeRequest` — it is the HTTP-boundary sibling for checks that belong at the edge (e.g., an entire endpoint requires an `Admin` role regardless of which command/query it dispatches). A command dispatched from an endpoint that already passed `[RequireRole]` may still carry its own, separate `IAuthorizeRequest` requirements evaluated deeper in the pipeline — the two layers are complementary, not exclusive.
- `ErrorType.Forbidden`/`Error.Forbidden(...)` shipped in `01.Core`'s `SharedKernel.Primitives` 1.1.0 (P-384/WO-059) and this capability's Core phase is implemented against them, never against `Error.Unauthorized(...)` as a stand-in — 401 and 403 are semantically distinct HTTP outcomes (not-authenticated vs. authenticated-but-forbidden) and this domain's `ErrorTypeStatusCodeMap` now encodes that distinction for every mapped `ErrorType`, including `Forbidden → 403`.
- **Step-up/fresh-authentication rules (WO-062, P-406 — shipped):** `[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` **extend** `AuthorizationRequirementEndpointFilter` — never a second filter type, never a second `.AddEndpointFilter<...>()` registration call. Both must evaluate through `IUserContext.IsAuthenticationFresherThan(TimeSpan, DateTimeOffset)`/`.WasAuthenticatedWith(string)` (both already shipped, `12.Security.Abstractions`, WO-058/P-375) — never a hand-rolled `AuthTime` comparison against a locally-called `DateTimeOffset.UtcNow`. `IsAuthenticationFresherThan` requires an explicit `now`; the filter resolves `IClock` (`01.Core/SharedKernel.Primitives`) from `HttpContext.RequestServices` **lazily, only when `[RequireFreshAuthentication]` is present** on the evaluated endpoint — an endpoint carrying only `[RequireRole]`/`[RequirePermission]` must never require `IClock` to be registered in the consumer's container. Composition and rejection shape (AND-across, `Error.Forbidden(...)` → 403, no dedicated `IsAuthenticated` branch) are identical to `[RequireRole]`/`[RequirePermission]`.

### API versioning rules

- `AddSharedKernelApiVersioning` must be called before `AddSharedKernelOpenApi` when both are used — the OpenAPI extension reads `IApiVersionDescriptionProvider` to discover version groups.
- `AssumeDefaultVersionWhenUnspecified = true` is non-negotiable platform default — unversioned client requests must not 400 outright; they fall back to `DefaultApiVersion`.
- URL-segment versioning is primary. The header reader is additive, never a replacement.

### OpenAPI / Scalar rules

- Swashbuckle and NSwag must never be added as dependencies of this package — see "Why Swashbuckle/NSwag are not used" above. `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` is the only sanctioned combination.
- **AOT-discovered correction (Core phase):** `Microsoft.OpenApi` 2.0.0's model types (`OpenApiDocument`, `OpenApiComponents`, `OpenApiSecurityScheme`, `SecuritySchemeType`, `OpenApiInfo`, etc.) live directly under the `Microsoft.OpenApi` namespace, **not** `Microsoft.OpenApi.Models` — the latter namespace does not exist in this version and is a holdover from the pre-2.0 Swashbuckle-era API shape. Always verify the actual namespace via reflection against the installed package version rather than assuming from older documentation/training data.
- The Bearer security scheme registered by `AddSharedKernelOpenApi`'s document transformer is metadata only (for the "Authorize" button in Scalar's UI) — it performs no token validation. Token validation is exclusively `12.Security.Oidc`'s concern.
- One OpenAPI document per discovered API version. Do not collapse multiple versions into a single document with manual `[ApiExplorerSettings]` filtering — that defeats the purpose of version-grouped documents.

### Security response header rules (WO-062, P-403 — shipped)

- `UseSharedKernelSecurityHeaders()` must be registered immediately after `UseSharedKernelCorrelationId()` and before `UseExceptionHandler()`/error-handling middleware — correlation-id-then-security-headers-then-exception-handler is the fixed pipeline ordering.
- HSTS is opt-**out**, not opt-in — it ships on by default, unlike every other capability added in this batch. Its XML docs and README must carry a capitalized warning that it needs disabling (or a short `max-age`) for local HTTP-only development, mirroring ASP.NET Core's own `UseHsts()` guidance.
- `Content-Security-Policy` is never set by default — only `WithContentSecurityPolicy(...)` sets one. A wrong or generic default CSP is a real risk to this package's own `MapSharedKernelOpenApi`/Scalar UI, so this package declines to guess one.
- Every header assignment is guarded by `context.Response.Headers.ContainsKey(...)` — an inner middleware/endpoint's more-specific header value always wins over this middleware's platform default.

### CORS policy rules (WO-062, P-404 — shipped)

- `AddSharedKernelCors`'s `CorsPolicyOptions` must make `AllowCredentials = true` combined with an empty/wildcard `AllowedOrigins` **structurally impossible to express** — enforced via an `IValidateOptions<CorsPolicyOptions>` + `ValidateOnStart()` fail-fast guard at `IHost.StartAsync()`, never deferred to the first real credentialed cross-origin request (the underlying CORS spec's own silent-until-triggered failure mode).
- Consumers reference the named default policy (`CorsPolicyNames.Default`) rather than re-inventing a policy-name literal at each call site — mirrors the platform's magic-string convention applied to CORS policy names.
- Environment-specific origin allowlists (dev/staging/prod) are a configuration concern, not a hardcoded list inside this package.

### Inbound idempotency-key rules (WO-062, P-405 — shipped)

- `IdempotencyKeyRequirementEndpointFilter` is a **separate filter type** from `AuthorizationRequirementEndpointFilter` — idempotency-key presence is a request-shape concern, not an authorization concern, and must never be folded into the authorization filter or its attribute set.
- `IdempotencyKeyHeader` is domain-local for now (see the Interface Contracts note above) — never independently re-typed as a raw string literal at a second call site inside this package.
- A missing/malformed key on an endpoint carrying `[RequireIdempotencyKey]` always produces a `ProblemDetails` body (`ErrorType.Validation` → 400) via the existing single-`Error` path — never a bare exception, never a new `ErrorType`.
- This package supplies extraction/validation and the guard filter only — it never attempts automatic binding of the validated key into a MediatR command's `IIdempotentRequest.IdempotencyKey` property; that remains the endpoint handler's own responsibility before dispatch.

### ETag / conditional-request rules (WO-062, P-407 — shipped)

- `RowVersionETag`/`ConditionalRequestExtensions` are additive helper surface — adoption is optional per consuming service, and zero existing behavior (`ErrorTypeStatusCodeMap`'s seven-case mapping, `06.Persistence`'s `Error.Conflict`/409) changes as a result of this capability existing.
- A 412 response is constructed via the shared internal RFC 9457 shaping helper (`Http/`), never via `Error`/`ErrorType` — see the "HTTP protocol-level outcomes" rule under ProblemDetails rules above for the general principle this follows.
- Docs must explicitly state the ETag/`If-Match` recipe is additive to, never a replacement for, `Error.Conflict` — a service may use either, both, or neither.

### Rate-limit rejection bridge rules (WO-062, P-408 — shipped)

- `RateLimitRejectionProblemDetails` is the only sanctioned way to shape a rate-limit rejection into `ProblemDetails` — a consuming service's `RateLimiterOptions.OnRejected` callback (wired via `13.ServiceDefaults`'s `AddSharedKernelRateLimiting()`) must call this helper directly rather than hand-rolling a 429 body, mirroring the platform's inline-`ProblemDetails`-construction prohibition applied to every other error shape.
- Reuses the same shared `Http/` RFC 9457 shaping helper as the 412 path (P-407) rather than duplicating the `Type`-URI/`traceId` construction a second time.
- `Retry-After`, when supplied, is always a real HTTP response header, never body-only.
- This package takes no `ProjectReference` on `13.ServiceDefaults` in either direction — the helper is discoverable and callable by name only, mirroring every other cross-domain "referenced by name, not by project" relationship already documented in this file (e.g. the correlation-id baggage key).
- **Flagged, not performed, follow-up (DO-17, WO-062):** `13.ServiceDefaults/README.md`'s `AddSharedKernelRateLimiting` `OnRejected` recipe (as of this writing) hand-rolls its own `ProblemDetails` literal inline instead of calling `RateLimitRejectionProblemDetails.Create(context.HttpContext, retryAfter)` — exactly the inline-`ProblemDetails`-construction anti-pattern this helper exists to close. Updating that recipe is `13.ServiceDefaults`'s own file, out of this domain's jurisdiction — flagged here for `servicedefaults-arch-planner`/`servicedefaults-phase-implementer` to pick up, mirroring the DO-07 cross-domain-flag-not-fix precedent (WO-041) rather than this domain reaching into another domain's README.

### Correlation-id rules

- `CorrelationIdMiddleware` must be registered first in the pipeline (before `UseExceptionHandler`) so correlation IDs are present even on error responses.
- The correlation ID value must flow into `Activity` baggage (`SetBaggage`, not `SetTag`) so it survives across process boundaries via W3C Baggage propagation, not just appear on the local span.
- This middleware handles the *inbound* side only. Outbound propagation to downstream services is `11.Communication.Rest`'s `CorrelationIdDelegatingHandler` — the two are designed to share the same `HttpContext.Items["CorrelationId"]` key/`Activity` baggage key so a request's correlation ID is continuous end-to-end without this domain depending on `11.Communication`.
- **Ownership (confirmed P-192, WO-031):** the `correlation.id` baggage key and the `HttpContext.Items["CorrelationId"]` storage key are `14.Presentation`'s own contract, defined and owned here — they are not borrowed from, nor do they require a `ProjectReference` on, `13.ServiceDefaults`. If `13.ServiceDefaults` wants to align its own OTel conventions with this key, that alignment flows from `13.ServiceDefaults` toward this domain's published contract, never the reverse.
- **The baggage key is a public constant, never a hand-copied literal (WO-041, P-256):** `CorrelationIdMiddleware.BaggageKey` (`"correlation.id"`) is the single source of truth for this contract's key name. Cross-domain consumers (`13.ServiceDefaults`'s `BaggageLogRecordProcessor` test suite, any future consumer) must reference this constant rather than re-typing the literal — a hand-copied literal is exactly how `13.ServiceDefaults`'s own P-251 test design ended up asserting against `"CorrelationId"` instead of this middleware's actual `"correlation.id"` value, a mismatch invisible to either domain's test suite until this phase's cross-check (see the Logging subsection below).
- **The constant's *value* now derives from `01.Core`, not a locally-owned constant (WO-042, P-262, D-14/C-19 — SHIPPED):** `CorrelationIdMiddleware.HeaderName`/`.BaggageKey` forward to `01.Core`'s `WellKnownHeaders.CorrelationId`/`WellKnownBaggageKeys.CorrelationId` (`SharedKernel.Primitives.Propagation`) rather than independently retyping the literal `"X-Correlation-Id"`/`"correlation.id"` here. The public constants on `CorrelationIdMiddleware` remain — they exist for call-site ergonomics and backward compatibility — but their value is sourced, not owned. This is what actually closes the mismatch class the P-251/DO-07 finding surfaced: a hand-copied literal is now structurally impossible to drift, since both `14.Presentation` and `13.ServiceDefaults` (once it independently aligns) read from the same `01.Core` source instead of each independently typing the string. `CorrelationIdMiddleware.ItemsKey` is explicitly excluded from this sourcing rule — it is a presentation-local `HttpContext.Items` key with no cross-service wire meaning, so it has no `01.Core` equivalent and remains untouched by this change. No new `ProjectReference` was required — `SharedKernel.Primitives` was already referenced by this domain (via the existing `LoggingEventIdRanges` usage).

### Logging EventId assignment & correlation verification (WO-041, P-249/P-250/P-251/P-256)

- Every `[LoggerMessage]`-attributed method in this domain carries an explicit `EventId` derived from `SharedKernel.Primitives.Logging.LoggingEventIdRanges.Presentation` (14000) — never a compiler-auto-assigned or ad hoc numeric literal. Compiler auto-numbering is a latent stability hazard: adding, removing, or reordering `[LoggerMessage]` methods in the same class silently renumbers every ID that follows.
- Sub-block allocation, in package declaration order per the root registry convention (100-wide sub-blocks per package within the domain's 1000-wide block): `SharedKernel.Presentation.WebApi` = 14000–14099, `SharedKernel.Presentation.SignalR` = 14100–14199.
- Assigned `EventId`s: `CorrelationIdMiddleware` = 14000, `SharedKernelExceptionHandler` = 14001 (both `.WebApi`); `HubExceptionMappingFilter` = 14100 (`.SignalR`). Any future `[LoggerMessage]` method added to either package continues sequentially within that package's sub-block — never reuse a retired ID.
- **Newly allocated and consumed (WO-063, P-414 — shipped):** `AuthorizationRequirementEndpointFilter` = 14002, `IdempotencyKeyRequirementEndpointFilter` = 14003, `CorsPolicyOptionsValidator` = 14004, `RateLimitRejectionProblemDetails` = 14005 (all `.WebApi`, within the existing 14000–14099 sub-block) — all four are live `[LoggerMessage]` call sites in shipped source, verified directly against the real `.cs` files (`Authorization/AuthorizationRequirementEndpointFilter.cs`, `Idempotency/IdempotencyKeyRequirementEndpointFilter.cs`, `Cors/CorsPolicyOptionsValidator.cs`, `RateLimiting/RateLimitRejectionProblemDetails.cs`), not assumed from design prose.
- **Also newly allocated and consumed (WO-063, P-415 — shipped, not called out by this ID's own design phase but confirmed present in shipped source):** `CorrelationIdMiddleware.Log.CorrelationIdRejected` = 14006 (`.WebApi`) — the format-validation rejection log added alongside the pre-existing `CorrelationIdGenerated` = 14000 on the same middleware. Logs only the rejected value's length, never its raw content, per the log-injection-defense rule stated in the Correlation-id format-validation rules subsection below.
- **Newly allocated and consumed (WO-063, P-418 — shipped):** the SignalR CORS startup diagnostic (`SignalRCorsStartupDiagnostic`) = 14102 (`.SignalR`, within 14100–14199; 14101 remains reserved for a future `HubInvocationRateLimitFilter` rejection log — `HubInvocationRateLimitFilter` itself (P-417) does not log on rejection, it only throws a caller-safe `HubException`, so 14101 stays unconsumed as of this writing). Both packages' sub-blocks remain overwhelmingly unused (`.WebApi` now at 7/100 — 14000–14006 — and `.SignalR` at 2/100 — 14100 and 14102 — after this WO), confirming this domain's near-total historical under-use of its own reserved `EventId` range prior to WO-063.
- This domain requires no code-shape changes to conform to `00.Governance`'s `SK0020`/`SK0021` (`LoggingAuthoringStyleAnalyzer`, P-250) — it already authors exclusively via `[LoggerMessage]`, never a direct `ILogger` extension-method call or hand-written `LoggerMessage.Define` delegate. The explicit `EventId` assignment is what closes the remaining gap against `00.Governance`'s `LoggingEventIdIntegrityAssertion` (global uniqueness + per-assembly range membership).
- **Correlation-on-log-record verification is proven without a cross-domain `ProjectReference`.** `13.ServiceDefaults`'s `BaggageLogRecordProcessor` (P-251) is what makes `CorrelationIdMiddleware`'s `Activity` baggage land on emitted `LogRecord`s — but this domain must never take a `ProjectReference` on `SharedKernel.ServiceDefaults` to prove that (per the existing `13.ServiceDefaults` non-dependency rule above). This domain's own test suite instead uses a test-local minimal `BaseProcessor<LogRecord>` that mirrors `BaggageLogRecordProcessor`'s documented contract (generic `Activity.Baggage` → `LogRecord.Attributes` copy, never overwriting an explicit attribute) to prove its own middleware's output is compatible with that mechanism — the reciprocal of the technique `13.ServiceDefaults`'s own correlation test already uses in the opposite direction (simulating this middleware's baggage-setting call via raw BCL `Activity.SetBaggage(...)` rather than referencing this package).

### SignalR hub filter rules

- `TenantContextHubFilter` and `HubExceptionMappingFilter` are registered as *global* hub filters via `HubOptions.AddFilter<T>()`, not per-hub `[HubFilter]` attributes — every hub in a consuming service gets both by default through `AddSharedKernelSignalR`.
- `HubExceptionMappingFilter` must never let a non-`HubException` cross the filter boundary — SignalR serializes unknown exception types inconsistently across transports; only `HubException` messages are guaranteed to reach the client safely.
- Filter ordering: `TenantContextHubFilter` (connection-scoped, attaches data) is independent of `HubExceptionMappingFilter` (invocation-scoped, wraps calls) — they do not depend on each other's execution order.
- **A `HubException` already thrown by any filter/hub method must always pass through `HubExceptionMappingFilter` unchanged (WO-063, P-417 — shipped, no code change needed):** the mapping filter's catch-all has a `catch (HubException) { throw; }` branch, checked first, so an already-well-formed, specific `HubException` (e.g. from `HubInvocationRateLimitFilter`'s rate-limit rejection) is never re-wrapped into the generic redacted `"An unexpected error occurred."` message. **This branch was found, via `git log` on the file, to have already existed since the domain's very first WO-031 build-out commit** — WO-063's D-64 flagged it as a hazard to fix based on reading the file at design time, but the Core-phase session confirmed (via `git log --all`) it was never actually missing; the design phase's "hazard" framing was itself based on a stale read, not a real gap. Nothing was changed; this bullet documents the standing composition rule for every future filter in this package that throws its own `HubException` — never assume the existing catch-all already handles this correctly without verifying it explicitly, even though in this instance it already did.
- `HubInvocationRateLimitFilter` (WO-063, P-417 — shipped) is registered globally like the other two filters, but is genuinely opt-in via `configureRateLimit` — omitted/`null` means the filter still registers (so it can always be enabled later without a redeploy of the filter registration itself) but no-ops on every invocation.

### SignalR CORS integration rule (WO-063, P-418 — shipped)

- `SharedKernel.Presentation.SignalR` never takes a `ProjectReference` on `SharedKernel.Presentation.WebApi` — the two packages remain deliberately independent API surfaces (see "Packages" above). The CORS gap for mapped hubs is closed via a startup-time diagnostic `Warning` (not a hard dependency, not a thrown exception) plus documentation, never a direct code coupling.
- A host correctly attaching a CORS policy to its mapped hub(s) must never see a false-positive warning — the diagnostic check inspects real endpoint metadata, not a heuristic guess.

### SignalR Redis backplane rules

- `WithRedisBackplane` is purely additive/opt-in — omitting it keeps SignalR fully in-memory (single-instance only), which is correct for local dev and single-replica deployments.
- Never share an `IConnectionMultiplexer` instance between `02.Caching.Redis.Core`'s `AddRedisConnection` and SignalR's backplane — `AddStackExchangeRedis` manages its own connection lifecycle internally and the two domains must not be wired together. This is intentional isolation, not an oversight: a backplane outage must not be conflated with a cache-connection outage in health checks or logs.
- See "Why SignalR's Redis backplane is distinct from `02.Caching.Redis.PubSub`" above before proposing any code sharing between the two.

### SignalR secure connection/message defaults (WO-062, P-409 — shipped)

- `AddSharedKernelSignalR` sets explicit, documented, conservative defaults for `MaximumReceiveMessageSize`/`MaximumParallelInvocationsPerClient`/`ClientTimeoutInterval`/`KeepAliveInterval` — pinned even where a value matches SignalR's own current framework default, so the platform's posture stays stable and documented across future SignalR version bumps rather than implicit.
- These defaults are applied **before** the caller's existing `configureHubOptions` callback runs — the platform default is a starting point, never a hard ceiling; a consuming service can always raise or lower any of the four independently. No new parameter, no signature change to `AddSharedKernelSignalR`.
- This is purely a `HubOptions` default-value change — it must never alter `TenantContextHubFilter`/`HubExceptionMappingFilter`/`WithRedisBackplane` behavior.

### Payload-limits rules (WO-063, P-411 — shipped)

- `AddSharedKernelPayloadLimits`/`UseSharedKernelPayloadLimits` are fully opt-in — a host calling neither is byte-identical to today.
- `MaxJsonDepth` must be wired into both `Microsoft.AspNetCore.Http.Json.JsonOptions` (Minimal API) and `Microsoft.AspNetCore.Mvc.JsonOptions` (MVC, when registered) — never only one, since this package supports both hosting models equally elsewhere (`ResultHttpExtensions`).
- A body-size violation is mapped through the shared `Http/` RFC 9457 shaping helper via a new `BadHttpRequestException`-specific branch on `SharedKernelExceptionHandler`, checked before the generic 500 fallback — never left to fall through to a generic redacted 500.
- `UseSharedKernelPayloadLimits` must guard `IHttpMaxRequestBodySizeFeature.IsReadOnly` before assignment — never crash the pipeline when the feature cannot be set (e.g., certain test-host shapes).

### OpenAPI security-scheme rules (WO-063, P-412 — shipped)

- Each active scheme (Bearer/ApiKey/mTLS) is registered as its own separate OpenAPI security requirement object — OR semantics. A single combined requirement object (AND/simultaneous-auth-required semantics) must never be used for this capability; this is the opposite of this package's usual AND-across-attributes composition rule and is easy to get backwards.
- `ApiKeyHeaderName` stays a plain configurable string — this package never takes a `ProjectReference` on `SharedKernel.Security.ApiKey`/`.Mtls` merely to avoid a call-site literal for a header name.
- Enabling `ApiKey`/`MutualTls` must never change the existing default-Bearer-only document shape for a consumer that does not touch the new `configureSecuritySchemes` parameter.

### API version lifecycle (Sunset/Deprecation) rules (WO-063, P-413 — shipped)

- `Sunset` is always a valid RFC 7231 HTTP-date (`DateTimeOffset.ToString("R")`) — never an arbitrary string or a bare date.
- `Deprecation` is sourced from Asp.Versioning's own `Deprecated`/`DeprecatedApiVersions` — never a second, independently-declared deprecated flag.
- `Link: rel="successor-version"` appears only alongside a declared sunset date and successor URI — never on its own.
- This capability is additive to, never a rework of, the existing `ReportApiVersions` header family — both may be present simultaneously.

### Security-audit logging rules (WO-063, P-414 — shipped)

- The four newly-logged rejection paths (`AuthorizationRequirementEndpointFilter`/`IdempotencyKeyRequirementEndpointFilter`/`CorsPolicyOptionsValidator`/`RateLimitRejectionProblemDetails`) must never log raw bearer tokens, API keys, certificate bytes, or a full `ClaimsPrincipal` dump — non-PII context only (endpoint name, failed requirement/attribute type, CORS failure reason, rate-limit policy name), mirroring `12.Security`'s (WO-057/P-371) established discipline.
- Every new `[LoggerMessage]` method carries its pre-assigned explicit `EventId` (14002–14005) — never compiler auto-numbering, per the platform-wide logging convention.
- **A newly-DI-logging-enabled type's `ILogger<T>` constructor parameter must always be optional (`ILogger<T>? logger = null`), falling back to `Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance` — never a required parameter.** Making it required broke real DI composition for real reasons: `AuthorizationRequirementEndpointFilter`/`IdempotencyKeyRequirementEndpointFilter`/`CorsPolicyOptionsValidator` (all previously dependency-free types) failed to construct against `consumer-verify`'s own bare-`ServiceCollection` negative-path test (no logging registered) once a required `ILogger<T>` was added — a scenario that is entirely legitimate (a minimal DI container, a unit test, a library consumer that hasn't wired `AddLogging()`), not a misuse. This is now a standing rule for this domain: adding logging to a previously-logging-free public type must never turn "constructs with zero services registered" into "throws `InvalidOperationException`."

### Correlation-id format-validation rules (WO-063, P-415 — shipped)

- A caller-supplied correlation-id value failing the length/character-allowlist check is regenerated, never propagated verbatim — this check runs before the value ever reaches `HttpContext.Items`, `Activity.SetBaggage`, or the response header.
- The default `MaxLength`/`AllowedCharacterPattern` must remain permissive enough that well-formed GUIDs, ULIDs, and other common safe correlation-id shapes already in production use are preserved unchanged — this is a bounds/injection guard, not a GUID-only restriction.

### Upload validation rules (WO-063, P-416 — shipped)

- `UploadValidationEndpointFilter` is a separate filter from `AuthorizationRequirementEndpointFilter`/`IdempotencyKeyRequirementEndpointFilter` — upload shape validation is neither an authorization nor an idempotency concern.
- This capability is a boundary-shape check ONLY. Virus/malware scanning and antivirus-engine integration are explicitly OUT OF SCOPE, stated in capitals in both XML docs and README — never implied as covered.
- Per-endpoint override args on `RequireValidatedUploadAttribute` take precedence over the global `UploadValidationOptions` defaults when supplied; an endpoint carrying no attribute performs zero validation (fully opt-in).
- No third-party MIME-detection library is added — a small, locally-maintained, extensible magic-byte signature table is the sanctioned mechanism.

### `SharedKernel.Presentation.Grpc` rules (WO-074, P-468 — shipped)

- `GrpcStatusCodeMap` is a **sibling to, never a merge with**, `ErrorTypeStatusCodeMap` — both key off `01.Core`'s `ErrorType`, but HTTP status codes and gRPC `StatusCode` do not correspond 1:1 (e.g. HTTP's 422 has no gRPC analogue; gRPC's `FailedPrecondition` covers ground HTTP splits across 409/412/422). `GrpcStatusCodeMap.Resolve` is the single source of truth for `ErrorType`→gRPC-status mapping — an inline switch duplicating it anywhere else is a platform violation, mirroring `ErrorTypeStatusCodeMap`'s own rule.
- `GrpcResultExtensions` are the only permitted `Result<T>`→gRPC conversion — no service method branches on `IsSuccess` by hand. A failure travels as the `RpcException` status, never as a response DTO.
- `GrpcExceptionInterceptor` MUST override all four `Grpc.Core.Interceptors.Interceptor` server methods (`UnaryServerHandler`/`ClientStreamingServerHandler`/`ServerStreamingServerHandler`/`DuplexStreamingServerHandler`) — an interceptor overriding only `UnaryServerHandler` silently leaves every streaming call unmapped, unlike HTTP where one request/response shape covers everything. Proven by `GrpcExceptionInterceptorIntegrationTests.ServerStreamingCall_KnownException_MapsToExpectedRpcException`, exercising the server-streaming override specifically, not just unary.
- `GrpcExceptionInterceptor` must never leak exception messages or stack traces outside `IHostEnvironment.IsDevelopment()` — identical discipline to `SharedKernelExceptionHandler`/`HubExceptionMappingFilter`.
- `GrpcCorrelationInterceptor`'s metadata key (`WellKnownHeaders.CorrelationId`) is CONFIRMED to match `SharedKernel.Communication.Grpc`'s real client-interceptor source byte-for-byte, proven by a real round-trip integration test using the real client interceptor type (never a hand-rolled metadata stand-in). `GrpcTenantContextInterceptor` NEVER reads gRPC metadata itself — it only ever resolves `ITenantProvider` (mirrors `TenantContextHubFilter` exactly); a consuming service that wants client-forwarded `x-tenant-id` metadata to actually populate the tenant must supply its own `ITenantProvider` reading that header — do not add metadata-reading to this interceptor by analogy to the correlation interceptor, that would break the "mirrors `TenantContextHubFilter`" contract.
- `GrpcAuthorizationInterceptor` reuses `.WebApi`'s four `Authorization/` attribute types VERBATIM — never a second, gRPC-specific attribute vocabulary. This package takes a deliberate `ProjectReference` on `SharedKernel.Presentation.WebApi` for exactly this reuse (see "Why `.Grpc` references `.WebApi`" above) — do not add a second reason to lean on that reference; if a future capability needs more from `.WebApi` than these four attribute types, treat that as a fresh design question, not an assumed extension of this one. CONFIRMED: attributes applied directly to a gRPC service implementation method surface via `ServerCallContext.GetHttpContext()?.GetEndpoint()?.Metadata` — no reflection fallback exists in the shipped code, and none is needed.
- This package never references `04.Contracts` — a named Hard-rule exception to `14.Presentation`'s otherwise-permitted `04.Contracts` reference, mirroring `SharedKernel.Communication.Grpc`'s existing P-163 rule: protobuf messages are the wire contract for a gRPC service method, not `04.Contracts` DTOs. Since `.WebApi` no longer references `SharedKernel.Contracts`, the assembly is not even available transitively through the `.Grpc`→`.WebApi` reference; `00.Governance`'s `PresentationLayeringRules.GrpcNeverReferencesContracts` still guards against a reference being added back and used.
- No new `13.ServiceDefaults` telemetry entry point (`WithXTelemetry`) exists for this capability — ASP.NET Core's own server-side OpenTelemetry instrumentation already covers the Kestrel/HTTP2 pipeline gRPC rides on. Do not propose one without first re-confirming this gap genuinely reopened.

### AOT notes

- `Microsoft.AspNetCore.OpenApi`'s schema generation uses source-generated reflection metadata where possible; verify AOT compatibility on every SDK upgrade since this is a fast-moving built-in feature.
- `Asp.Versioning.*` and `Scalar.AspNetCore` AOT status must be re-verified on every major version bump — these are third-party packages, not BCL.
- `Microsoft.AspNetCore.SignalR.StackExchangeRedis` is not fully AOT-verified as of this writing — confirm on adoption and wrap behind `WithRedisBackplane` (already an abstraction seam) if a swap is ever needed.
- `System.Threading.RateLimiting` (WO-063, P-417 — shipped) ships transitively via the existing `FrameworkReference Microsoft.AspNetCore.App` on `net10.0` — confirmed via a real build probe at Scaffold phase (S-27: a scratch `.cs` file referencing `RateLimiter`/`TokenBucketRateLimiter` compiled with zero new `PackageReference`). AOT status of the namespace itself must still be re-verified on any future SDK major-version bump, per this domain's general AOT-preferred posture.
- `Grpc.AspNetCore` `2.80.0` (server hosting, `SharedKernel.Presentation.Grpc`, WO-074/P-468 — shipped) is NOT independently claimed AOT-clean by this session — confirmed `net10.0`-compatible via NuGet flat-container listing only, not confirmed trim/AOT-safe. Re-verify AOT status on any future version bump before assuming it, per this domain's general posture; do not assume it inherits `Grpc.Net.Client`'s (the client-side package's) AOT story — that is a separate package with separate guarantees.

---

## DI Registration (shipped shape — every example below builds)

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

// WebApi — declarative role/permission authorization (WO-058/P-381 — shipped)
builder.Services.AddSharedKernelAuthorizationFilters();

// Minimal API — attribute-free sugar form
app.MapGet("/orders/{id}", GetOrderHandler)
   .RequireRole("Admin", "OrdersManager")     // OR within this call
   .RequirePermission("orders:read")           // AND against the RequireRole above
   .AddEndpointFilter<AuthorizationRequirementEndpointFilter>();

// MVC controller — attribute form (registration is still required at the route-group/
// MapControllers() level; the attribute alone does not activate the check)
[RequireRole("Admin", "OrdersManager")]
[RequirePermission("orders:read")]
[HttpGet("{id}")]
public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken ct) { /* ... */ }
// ... at composition root:
app.MapControllers().AddEndpointFilter<AuthorizationRequirementEndpointFilter>();

// WebApi — step-up/fresh-authentication (WO-062/P-406 — shipped)
app.MapPost("/payments/{id}/confirm", ConfirmPaymentHandler)
   .RequireFreshAuthentication(maxAgeSeconds: 300)      // AuthTime within the last 5 minutes
   .RequireAuthenticationMethod("mfa", "otp")            // OR within this call
   .AddEndpointFilter<AuthorizationRequirementEndpointFilter>();   // same filter as [RequireRole]

// WebApi — multi-field validation ProblemDetails (WO-062/P-402 — shipped)
// No extra wiring — SharedKernelExceptionHandler routes ValidationException through the
// multi-error path automatically; a client posting 3 invalid fields sees all 3 in the response.

// WebApi — security response headers (WO-062/P-403 — shipped)
builder.Services.Configure<SecurityHeadersOptions>(o => o.WithContentSecurityPolicy("default-src 'self'"));
// ...
app.UseSharedKernelCorrelationId();       // first
app.UseSharedKernelSecurityHeaders();     // second
app.UseExceptionHandler();                // third

// WebApi — CORS (WO-062/P-404 — shipped)
builder.Services.AddSharedKernelCors(o =>
{
    o.AllowedOrigins.Add("https://app.example.com");
    o.AllowCredentials = true;   // fine — origins are explicit, never wildcard
});
// ...
app.UseCors(CorsPolicyNames.Default);

// WebApi — inbound idempotency-key HTTP boundary (WO-062/P-405 — shipped)
builder.Services.AddSharedKernelIdempotencyFilters();
app.MapPost("/payments", CreatePaymentHandler)
   .RequireIdempotencyKey()
   .AddEndpointFilter<IdempotencyKeyRequirementEndpointFilter>();
// ... inside CreatePaymentHandler:
httpContext.TryGetIdempotencyKey(out string? key);
var command = new CreatePaymentCommand(..., IdempotencyKey: key);   // dispatched through
                                                                     // 05.Application's
                                                                     // IdempotencyBehavior

// WebApi — ETag / If-Match conditional requests (WO-062/P-407 — shipped)
app.MapGet("/accounts/{id}", async (Guid id, IAccountQueryService svc, HttpContext ctx, CancellationToken ct) =>
{
    var account = await svc.GetByIdAsync(id, ct);
    ctx.Response.Headers.ETag = RowVersionETag.From(account.RowVersion);
    return Results.Ok(account);
});
app.MapPut("/accounts/{id}", async (Guid id, UpdateAccountRequest body, HttpContext ctx, CancellationToken ct) =>
{
    // ConditionalRequestExtensions evaluates the inbound If-Match header against the current
    // ETag and short-circuits with 412 on mismatch before the update proceeds — additive to,
    // never a replacement for, the existing Error.Conflict/409 path.
});

// SignalR — minimal setup (in-memory, single replica)
builder.Services.AddSharedKernelSignalR();

// SignalR — scale-out across pods via Redis backplane
builder.Services.AddSharedKernelSignalR()
       .WithRedisBackplane(connectionString);

// SignalR — tenant-scoped group broadcast from inside a Hub method
await Clients.Group(HubGroupNaming.TenantGroup(tenantId)).SendAsync("OrderUpdated", orderId);

// WebApi — payload size / JSON max-depth DoS protection (WO-063/P-411 — shipped)
builder.Services.AddSharedKernelPayloadLimits(o => o.MaxJsonDepth = 32);
// ...
app.UseSharedKernelPayloadLimits(o => o.MaxRequestBodySizeBytes = 2 * 1024 * 1024);

// WebApi — OpenAPI ApiKey + mTLS security schemes alongside the default Bearer (WO-063/P-412 — shipped)
builder.Services.AddSharedKernelOpenApi(title: "Payments API", configureSecuritySchemes: o =>
{
    o.ApiKey = true;
    o.ApiKeyHeaderName = "X-Api-Key";   // matches this service's own SharedKernel.Security.ApiKey wiring
});

// WebApi — RFC 8594 Sunset/Deprecation headers on a retiring API version (WO-063/P-413 — shipped)
builder.Services.AddSharedKernelApiVersioning(configureLifecycle: o =>
{
    o.Configure(new ApiVersion(1, 0), sunsetDate: new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                successor: new Uri("https://api.example.com/v2/orders"));
});
// Deprecation itself still comes from Asp.Versioning's own HasDeprecatedApiVersion(...)/[ApiVersion(Deprecated = true)] —
// Configure(...) above only declares the sunset date/successor link, never a parallel deprecated flag.

// WebApi — security-audit logging (WO-063/P-414 — shipped) requires no extra wiring — AuthorizationRequirementEndpointFilter,
// IdempotencyKeyRequirementEndpointFilter, CorsPolicyOptionsValidator, and RateLimitRejectionProblemDetails
// log automatically on their existing rejection paths once each capability is already adopted.

// WebApi — correlation-id format validation (WO-063/P-415 — shipped)
builder.Services.AddSharedKernelCorrelationId(o =>
{
    o.MaxLength = 128;
    // AllowedCharacterPattern left at its conservative default (GUID/ULID/safe-token shapes)
});

// WebApi — file/multipart upload validation (WO-063/P-416 — shipped)
builder.Services.AddSharedKernelUploadValidation(o =>
{
    o.MaxSizeBytes = 10 * 1024 * 1024;   // 10 MB
    o.AllowedContentTypes.Add("application/pdf");
});
app.MapPost("/kyc/documents", UploadKycDocumentHandler)
   .RequireValidatedUpload(5 * 1024 * 1024, "application/pdf", "image/jpeg")   // maxSizeBytes, then params allowedContentTypes
   .AddEndpointFilter<UploadValidationEndpointFilter>();

// SignalR — hub invocation rate limiting + argument validation (WO-063/P-417 — shipped)
builder.Services.AddSharedKernelSignalR(configureRateLimit: o =>
{
    o.PermitLimit = 20;
    o.Window = TimeSpan.FromSeconds(10);
});

// SignalR — CORS/negotiate integration (WO-063/P-418 — shipped) — this package never wires CORS directly;
// attach the WebApi package's own named policy at the composition root:
app.MapHub<OrdersHub>("/hubs/orders").RequireCors(CorsPolicyNames.Default);
// omitting .RequireCors(...) on a mapped hub triggers a startup Warning log (EventId 14102), not a thrown exception.

// Grpc — server-side conventions (WO-074/P-468 — shipped; this example builds and is exercised
// end-to-end by SharedKernel.Presentation.Grpc.Tests.Integration)
builder.Services.AddSharedKernelGrpc(o => o.MaxReceiveMessageSize = 4 * 1024 * 1024);
// ... service implementation, reusing the exact same attributes as an HTTP endpoint:
[RequireRole("Admin", "OrdersManager")]
[RequirePermission("orders:read")]
public override async Task<GetOrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
{
    Result<Order> result = await _svc.GetByIdAsync(request.OrderId.ToGuid(), context.CancellationToken);
    return result.ToGrpcResult().ToReply();   // ToGrpcResult() throws the mapped RpcException on
                                               // failure; ToReply() is the service's own
                                               // protobuf-projection, never a 04.Contracts type
}

// WebApi — localized ProblemDetails.Detail (WO-078/P-484 — shipped; this example builds and is
// exercised end-to-end by LocalizedProblemDetailsTests)
builder.Services.AddSharedKernelLocalization(...);   // 01.Core/13.ServiceDefaults concern — this
                                                       // package never registers or resolves a
                                                       // culture, only ever reads CurrentUICulture
// No AddSharedKernelXxx() call is needed on THIS package's side — Error.ToProblemDetails()
// activates the localization step automatically the moment ILocalizationCatalog resolves from DI;
// a service that never registers a catalog sees byte-identical output to today.
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
- `RequireRoleAttribute`/`RequirePermissionAttribute`/`AuthorizationRequirementEndpointFilter` (WO-058, P-381 — Core and Tests both shipped, T-15–T-18 all `●`): covered by `Authorization/RequireRoleAttributeTests.cs`, `RequirePermissionAttributeTests.cs`, `AuthorizationRequirementEndpointFilterTests.cs`, and `AuthorizationCompositionTests.cs` — an authorized fake `IUserContext` (`HasRole`/`HasPermission` → `true`) passes and `next()` runs exactly once; an unauthorized fake short-circuits with a `ProblemDetails` body/status matching `Error.Forbidden(...).ToProblemDetails()` (403, never a bare/empty 403); an anonymous-shaped fake is rejected before `next()` runs, with an explicit assertion this happens via the ordinary false-path and not a bespoke `IsAuthenticated` check; an endpoint carrying neither attribute passes through as a no-op with zero `IUserContext` resolution attempted; multiple stacked attributes AND, a single attribute's multi-value list ORs. Plus a regression test that `ErrorTypeStatusCodeMap.Resolve(ErrorType.Forbidden)` returns 403. See the new "Testing endpoint filters without a host" subsection below for the concrete construction technique.

### Testing endpoint filters without a host (confirmed at Tests phase, WO-058/P-381)

- `EndpointFilterInvocationContext` has no public constructor — build one via the static factory `EndpointFilterInvocationContext.Create(HttpContext)` (confirmed via reflection probe against the installed `net10.0` SDK; overloads up to 8 typed arguments also exist but are unneeded here). Attach endpoint metadata (e.g. `RequireRoleAttribute`/`RequirePermissionAttribute` instances) via `httpContext.SetEndpoint(new Endpoint(requestDelegate, new EndpointMetadataCollection(metadata), displayName))` — the `RequestDelegate` parameter accepts `null`/a no-op delegate at runtime despite its non-nullable annotation, since a filter test never actually invokes it.
- `Results.Problem(ProblemDetails)` returns the concrete `Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult` (confirmed via reflection) — assert on its `.StatusCode`/`.ProblemDetails` properties directly rather than the loosely-typed `IResult` the filter's own return signature exposes.
- To prove a no-op code path resolves zero services (rather than merely "the test happened to pass"), leave `HttpContext.RequestServices` backed by an **empty** `ServiceCollection` when the test wants to prove no `IUserContext` resolution was attempted — `GetRequiredService<IUserContext>()` throws `InvalidOperationException` against an empty container, so any accidental resolution attempt fails the test loudly instead of silently passing.
- To prove a rejection path never reads `IUserContext.IsAuthenticated` (i.e. rejection flows only through the ordinary `HasRole`/`HasPermission` false-path, never a bespoke authentication branch), use a test-only `IUserContext` implementation whose `IsAuthenticated` getter throws — see `Authorization/IsAuthenticatedGuardUserContext.cs`. A test using this double fails with the thrown exception, not a clean 403, the instant any code path reads that member.
- `SharedKernel.Testing`'s `FakeUserContext` (`SharedKernel.Testing.Security`) is sufficient for the ordinary authorized/unauthorized-but-authenticated cases — its `IsAuthenticated` follows `IdentityKind` (default `User`) and `HasRole`/`HasPermission` are ordinal `Contains` checks over settable `Roles`/`Permissions` collections, so no bespoke fake was needed for those paths.

### WebApplicationFactory integration test pattern (confirmed at Tests phase)

- `OpenApiExtensions.MapSharedKernelOpenApi` requires a `WebApplication` receiver, not `IApplicationBuilder` — a `WebApplicationFactory<T>` test host that needs it cannot use the `IWebHostBuilder.Configure(IApplicationBuilder)` callback. Override `WebApplicationFactory<T>.CreateHost(IHostBuilder)` instead: build a `WebApplication` directly via `WebApplication.CreateBuilder()` + `.WebHost.UseTestServer()`, map endpoints/`MapSharedKernelOpenApi()` on it, call `app.Start()`, and return it.
- `Asp.Versioning.Http` 10.0.0 exposes the requested API version on `HttpContext` as the extension **property** `HttpContext.RequestedApiVersion` (`Microsoft.AspNetCore.Http.HttpContextExtensions`) — there is no callable `GetRequestedApiVersion()` method despite that name appearing in some docs/training data. Always verify via reflection against the installed package before writing test/endpoint code against it (see `00.Governance`-adjacent guidance: verify real API shapes, don't trust the name).
- SignalR Redis backplane fan-out test: two independent in-memory `TestServer` SignalR hosts (each built the same way — `AddSharedKernelSignalR().WithRedisBackplane(connectionString)` against the same `RedisContainerFixture` (`16.Testing/SharedKernel.Testing.Containers`) connection string) prove cross-instance fan-out by sending via one instance's `IHubContext<THub>` and asserting receipt on a `Microsoft.AspNetCore.SignalR.Client.HubConnection` connected through the other instance's `TestServer.CreateHandler()`. `SharedKernel.Presentation.SignalR.Tests` carries `Microsoft.AspNetCore.SignalR.Client` 10.0.5 (the latest available for this package — not the 10.0.9 WebApi-stack line) and `Microsoft.AspNetCore.TestHost` 10.0.9 as test-only package references.

### WO-062 test additions (design-locked; queued P-402–P-409)

- `ValidationProblemDetailsExtensions`: N = 1, 2, 3 field-error test cases asserting `Extensions["errors"]` count matches `ValidationException.Errors.Count`; a full regression sweep of every non-`ValidationException` `ErrorType` proving the single-error body is byte-for-byte unchanged.
- `SecurityHeadersMiddleware`: default header-set-present test; a CSP-omitted-means-no-header test (never a wrong default); a pre-set-header-never-overwritten test; an HSTS-individually-toggleable-off test.
- `AddSharedKernelCors`: a startup-throws-on-wildcard-plus-credentials test (via the real `IHost.StartAsync()` path, not just constructing the options object); a valid-explicit-origin-plus-credentials-succeeds test; an integration test proving the named policy allows a configured origin and rejects an unconfigured one.
- `IdempotencyKeyRequirementEndpointFilter`/`TryGetIdempotencyKey`: header present/absent/whitespace/malformed unit tests; a no-op-when-attribute-absent test (mirrors T-16's "zero unrelated-service resolution" technique); a regression test proving no unhandled exception ever leaks past the filter.
- Step-up authentication (`RequireFreshAuthentication`/`RequireAuthenticationMethod`): within-window/expired/absent-`AuthTime` cases; OR-within-list semantics for `RequireAuthenticationMethod`; a composition test stacking `[RequireRole]` + `[RequireFreshAuthentication]`; and — reusing the exact empty-container/throwing-double technique T-16 established for `IUserContext` — a test proving `IClock` is never resolved from `HttpContext.RequestServices` when no `[RequireFreshAuthentication]` attribute is present on the endpoint.
- `RowVersionETag`/`ConditionalRequestExtensions`: well-formed-ETag-output tests across representative `byte[]` inputs; match/mismatch/absent-`If-Match` cases; a regression test that `ErrorTypeStatusCodeMap` still resolves exactly its existing seven mapped `ErrorType`s (including `Forbidden → 403`, shipped WO-058) plus the 500 fallback (no new case silently added). Shipped as `Concurrency/RowVersionETagTests.cs`/`Concurrency/ConditionalRequestExtensionsTests.cs` (T-36–T-38, WO-062).
- `RateLimitRejectionProblemDetails`: a shape test (429/`Type`/`traceId`); a `Retry-After`-header-set-only-when-supplied test.
- SignalR `HubOptions` defaults: a defaults-match-documented-values test when `configureHubOptions` is omitted; an every-default-overridable test; a full existing-suite regression run proving `TenantContextHubFilter`/`HubExceptionMappingFilter`/`WithRedisBackplane` are untouched.

### WO-063 test additions (confirmed at Tests phase — shipped, T-44–T-67)

> **Status: Shipped end to end.** 63 net-new tests (52 `SharedKernel.Presentation.WebApi.Tests`, 11 `SharedKernel.Presentation.SignalR.Tests`) — `186/186` and `25/25` green respectively, zero production-code change. Three genuine test-construction discoveries below are worth preserving for any future session touching these capabilities, since each cost a first failed attempt before the working technique was found.

- **Payload limits — the 413 case needs chunked transfer encoding, not a declared `Content-Length` body (genuine discovery, T-44):** a real listening Kestrel host is mandatory here (per the Core-phase note above, `TestServer` does not enforce `IHttpMaxRequestBodySizeFeature` the same way) — but a `StringContent` body with a `Content-Length` header exceeding `MaxRequestBodySizeBytes` is rejected by Kestrel at the **connection level**, before the ASP.NET Core middleware pipeline (and therefore `SharedKernelExceptionHandler`) ever runs, producing a bare 413 with an **empty body**. To route the rejection through the documented `BadHttpRequestException` → `SharedKernelExceptionHandler` → `ProblemDetails` path (and to prove the handler marker was never reached, since Kestrel must actually start dispatching the request first), send the oversized body with `request.Headers.TransferEncodingChunked = true` instead — this forces Kestrel to read the body incrementally and throw only once the running byte count exceeds the limit, by which point the exception-handling middleware is already in the call stack. `PayloadLimitsIntegrationTests.cs` builds the real host via `WebApplication.CreateBuilder()` + `builder.WebHost.UseUrls("http://127.0.0.1:0")` + `await app.StartAsync()`, reading the bound ephemeral port back via `app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()`.
- OpenAPI security schemes: a default-Bearer-only-byte-identical-to-pre-P-412 regression test; a real-generated-OpenAPI-JSON test (fetch `/openapi/v1.json`, parse with `System.Text.Json.JsonDocument` — never a mocked `OpenApiDocument` model) proving a combined scheme configuration produces the correct `securitySchemes` dictionary and one OR'd `security` requirement object per active scheme, each keyed by the plain scheme name (a Security Requirement Object's JSON keys are the scheme names themselves, not `$ref` pointers).
- Sunset/Deprecation headers: an HTTP-date-format-round-trips test (never an arbitrary string) via `DateTimeOffset.Parse` plus an exact `"R"`-format string-equality check; a `Deprecation`-present-independent-of-sunset-date test; a `Link: rel="successor-version"`-only-with-both-sunset-and-successor test; a zero-new-headers-when-nothing-declared regression test. Built with a real `ApiVersionSetBuilder` declaring three versions (`.HasApiVersion(1.0)`, `.HasDeprecatedApiVersion(2.0)`, `.HasDeprecatedApiVersion(3.0)`) and per-version `ApiVersionLifecycleOptions.Configure(...)` calls (2.0 gets a sunset date + successor; 3.0 gets a successor with no sunset date, to prove the Link-only-with-sunset rule).
- **Security-audit logging — two of the four rejection paths are `internal` types, tested indirectly (genuine discovery, T-53/T-54):** `CorsPolicyOptionsValidator` cannot be `new`'d or `typeof()`'d from the test assembly. Exercise it through a real `HostBuilder`/`await host.StartAsync()` (mirroring `CorsExtensionsTests`' existing `OptionsValidationException` technique) with `services.AddInMemoryLoggerFactory()` (`16.Testing`) called *before* `services.AddSingleton<ILoggerFactory>(loggerFactory)` — the explicit registration wins on resolution (last-registered-wins for a single-instance service), while `AddInMemoryLoggerFactory()`'s `TryAdd` of the open-generic `Logger<>` adapter for `ILogger<T>` still lands. Retrieve captured records via `loggerFactory.GetLogger(categoryName)`, where `categoryName` for an internal type must be a hardcoded string literal (the BCL `Logger<T>`'s category name is the type's full name) since `typeof(InternalType)` does not compile outside the production assembly. `AuthorizationRequirementEndpointFilter`/`IdempotencyKeyRequirementEndpointFilter` are public and constructor-injectable, so they use `SharedKernel.Testing.Logging.InMemoryLogger<T>` directly — no host needed. The no-secret-leak proof puts a representative bearer token/sensitive value genuinely in the rejected request (an `Authorization` header, an unrelated `CorsPolicyOptions.AllowedHeaders` entry) and asserts it appears in neither `LogRecord.Message` nor any `LogRecord.State` property — never merely asserting absence without first proving presence in the input, which would pass vacuously.
- Correlation-id format validation: an oversized/malformed-header-never-reaches-baggage-or-response-header regression test (the capability's own core acceptance criterion) — assert against `Activity.Current!.Baggage`, requiring a `new Activity(name).Start()`/`.Stop()` pair around the middleware call (no `ActivityListener` registration needed; a plain `Activity.Start()` always sets `Activity.Current` regardless of listeners, unlike `ActivitySource.StartActivity`); a well-formed-GUID (both dashed and `"N"`-format)/ULID-preserved-unchanged test; an options-independently-overridable test (`MaxLength`/`AllowedCharacterPattern` each tested in isolation); a no-options-supplied-to-constructor-still-applies-defaults test.
- Upload validation: a fails-closed-before-full-body-buffering 413/415/400 test (marker technique, mirrors payload limits — but unit-level via a directly-constructed `EndpointFilterInvocationContext`/`UploadValidationEndpointFilter`, no host needed, since the filter's checks run entirely inside ASP.NET Core's endpoint-filter pipeline rather than at the Kestrel transport level); a content-type-mismatch-415 test including a `;charset=...`-parameter-stripped-before-matching case; a magic-byte-mismatch-400 and a magic-byte-match test (the latter also asserting the body stream's position is reset to `0` so the downstream handler still sees the full body); a no-attribute-means-zero-validation regression test; a per-endpoint-override-wins-over-global-default test (smaller endpoint `MaxSizeBytes` rejects even when the global default would have allowed it).
- SignalR invocation rate limiting: a per-connection-throttled-while-other-connections-unaffected test (two independently-created `HubInvocationContext`s, each with its own `HubCallerContext.Items` dictionary via `Substitute.For<HubCallerContext>().Items.Returns(new Dictionary<object, object?>())`); **the composition-hazard regression test** — a real two-filter pipeline (`HubExceptionMappingFilter.InvokeMethodAsync` wrapping `HubInvocationRateLimitFilter.InvokeMethodAsync` wrapping the target delegate, mirroring `AddSharedKernelSignalR`'s actual registration order) proving a rate-limit-rejected invocation's `HubException` message reaches the caller with its specific text intact, never the generic redacted fallback; an argument-payload-validation-rejects-before-method-body-executes test (a `bool targetInvoked` flag proves the delegate never ran); a `configureRateLimit`-omitted-means-no-op regression test (default-constructed `HubInvocationRateLimitOptions`, 20 invocations with a 100 KB string argument, zero rejections) plus a full existing-suite regression run.
- **SignalR CORS diagnostic — also an `internal` type, tested via a real `TestServer` host (T-64/T-65):** `SignalRCorsStartupDiagnostic` is an `IHostedService` registered by `AddSharedKernelSignalR`; its scan runs on `IHostApplicationLifetime.ApplicationStarted`, which fires synchronously as part of the generic host's own startup sequence — `hostBuilder.Start()` (not `StartAsync()`, to keep the test method synchronous where possible) is sufficient, no extra `Task.Delay` orchestration required, though a short bounded poll loop is a reasonable defensive habit against scheduling variance. Same `AddInMemoryLoggerFactory()` + explicit `ILoggerFactory` substitution technique as the CORS validator above; the category-name string is `"SharedKernel.Presentation.SignalR.Extensions.SignalRCorsStartupDiagnostic"`. Confirms D-65's decision (no `ProjectReference` to the WebApi package, diagnostic-only) is the actually-shipped shape — no negotiate-endpoint CORS-integration point exists to test, so T-66 has no test code, only this confirmation.

### `SharedKernel.Presentation.Grpc` test plan (WO-074, P-468 — shipped, 36/36 tests green)

- `GrpcStatusCodeMap` (`GrpcStatusCodeMapTests`, 9 tests): every `ErrorType` mapping (including `Forbidden`) plus the `None`/unmapped-falls-back-to-`Unknown` cases, plus an exhaustiveness loop over every declared `ErrorType` member, mirroring `ErrorTypeStatusCodeMapTests`' shape.
- `GrpcResultExtensions` (`GrpcResultExtensionsTests`, 5 tests): unit tests, no host needed — success paths (void-return and unwrapped-value), failure paths asserting the thrown `RpcException`'s `StatusCode`/`Status.Detail` route through `GrpcStatusCodeMap`.
- `GrpcExceptionInterceptor` (`GrpcExceptionInterceptorIntegrationTests`, 4 tests): a REAL gRPC-over-HTTP2 in-process `WebApplicationFactory`/`TestServer` host (never a mocked `ServerCallContext`) — known-exception mapping, unknown-exception mapping in Development (unsuppressed detail) and outside Development (suppressed to the generic message) via `WithWebHostBuilder(...).UseEnvironment(...)`, and a server-streaming-shape proof (`StreamThrowKnown`) confirming all four interceptor overrides are genuinely wired, not just `UnaryServerHandler`.
- `GrpcCorrelationInterceptor`/`GrpcTenantContextInterceptor` (`GrpcCorrelationTenantRoundTripTests`, 2 tests): a round-trip test using the REAL `SharedKernel.Communication.Grpc` client-side interceptors via `AddSharedKernelGrpcCommunication().AddGrpcClient<TClient>()` (never a hand-rolled metadata stand-in) — the acceptance criteria's explicit proof requirement. Discovered during authoring: `GrpcTenantContextInterceptor` never reads gRPC metadata itself (only `ITenantProvider`, per D-72), so the round-trip test's server-side composition uses a test-only `HeaderTenantProvider : ITenantProvider` reading the raw `x-tenant-id` header — a realistic simulation of what a consuming service's own `ITenantProvider` implementation would do, proving byte-for-byte metadata-key agreement end to end without this package's contract itself needing to read metadata.
- `GrpcAuthorizationInterceptor` (`GrpcAuthorizationInterceptorIntegrationTests`, 8 tests): a real host with `[RequireRole]`/`[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` applied directly to `TestServiceImpl` override methods — allow/deny/anonymous-caller/fresh-vs-stale-auth/matching-vs-non-matching-method paths, plus a no-attribute endpoint proving `IUserContext` is never resolved when no attribute is present (via `services.RemoveAll<IUserContext>()`). This is the empirical proof that `ServerCallContext.GetHttpContext()?.GetEndpoint()?.Metadata` surfaces gRPC-service-method attributes — the reflection fallback contemplated in the original design was never needed.
- `LoggerMessageEventIdTests` (3 tests): reflection-based `EventId` regression pins for the `14200`-`14299` sub-block, mirroring `.WebApi`'s T-11 technique.
- `SharedKernel.Presentation.Grpc.Tests` references `SharedKernel.Testing` (fakes) and, test-only, `SharedKernel.Communication.Grpc` (gating-proof-only, mirrors `13.ServiceDefaults.Tests`' T-43/WO-056 and D-28/WO-063 precedents — never referenced by the production `.Grpc.csproj`). A hand-written `test.proto` (`Grpc.Tools`, transitively pulled by `Grpc.AspNetCore`, compiles it — no separate `Grpc.Tools` `PackageVersion` entry was needed in `Directory.Packages.props`) backs a trivial `TestServiceImpl`; `GrpcTestWebApplicationFactory`/`ResponseVersionHandler` mirror `SharedKernel.ServiceDefaults.Tests`' identical T-43/WO-056 in-memory-`TestServer`-for-gRPC fixture shape verbatim.

### Localized `ProblemDetails.Detail` test plan (WO-078, P-484 — shipped, part of 192/192 `SharedKernel.Presentation.WebApi.Tests`)

- `LocalizedProblemDetailsTests` (7 tests): Zero-registration regression — a test with NO `ILocalizationCatalog` registered anywhere (an empty `ServiceCollection`-backed `HttpContext.RequestServices`, distinct from the `context = null` case) proves byte-identical `ProblemDetails.Detail` output to the no-`HttpContext`-at-all call — the acceptance criteria's central backward-compatibility guarantee, provable, not merely asserted. Discovered during authoring: a `DefaultHttpContext` with no `RequestServices` assigned returns `null` from that property rather than throwing, so `LocalizedDetailResolver` chains `context?.RequestServices?.GetService<...>()` — both `?.`s are load-bearing, not defensive-only.
- Fallback proof: a registered `InMemoryLocalizationCatalog` with no entry for `(error.Code, CurrentUICulture)` falls back to `error.Message` — never blank, never throws.
- Translation proof: a registered catalog WITH a matching entry, proven against two distinct `CultureInfo` values (`tr-TR`/`de-DE`) for the same `error.Code`, restoring the ambient `CultureInfo.CurrentUICulture` in a `finally` block.
- Multi-field independence: `ValidationToProblemDetails_AppliesLocalizationIndependentlyPerField` proves the P-402 validation path applies localization/fallback per field independently — one field translated, a sibling field falling back, in the same response `Extensions["errors"]` dictionary.
- Regression: `ToProblemDetails_Localization_NeverChangesTitleStatusTypeOrExtensions` plus the full pre-existing 185-test `SharedKernel.Presentation.WebApi.Tests` suite staying green (192/192 total, zero regressions) confirms `Title`/`Status`/`Type`/`Extensions["errorCode"]` and `ErrorTypeStatusCodeMap`'s mapping are unchanged.

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
- [2026-07-14] WO-042 P-262 dispatched — consume `01.Core`'s shared `WellKnownHeaders`/`WellKnownBaggageKeys` (P-259, D-30) instead of independently-owned literals. D-14 (design, locked now): `CorrelationIdMiddleware.HeaderName`/`.BaggageKey` become documented forwarding aliases over `01.Core`'s `WellKnownHeaders.CorrelationId`/`WellKnownBaggageKeys.CorrelationId` — the constants stay for call-site ergonomics/backward compatibility, but the literal value must originate from `01.Core`, never be retyped locally; `ItemsKey` confirmed explicitly out of scope (presentation-local `HttpContext.Items` key, not a cross-service wire concept, no `01.Core` equivalent) and stays untouched. New Interface Contracts note added under `CorrelationIdMiddleware` and a new Correlation-id rules bullet added documenting the pending sourcing change. **This phase's code (C-19/C-20), tests (T-14), docs (DO-08), and re-pack (P-07) are explicitly gated on `01.Core`'s `C-43` shipping** — only `01.Core`'s D-30 design is locked as of this writing, the constant does not yet exist in `01.Core` source. **DO-07 closure note:** DO-07 (recording the `13.ServiceDefaults` P-251 literal mismatch) remains `●` as the flagging task it always was — but this phase is what actually resolves the underlying concern DO-07 raised: once C-19 ships and `13.ServiceDefaults` independently aligns to the same `01.Core` source, the mismatch class becomes structurally impossible rather than merely documented. New task rows D-14 (`●`), S-11/C-19/C-20/T-14/DO-08/P-07 (all `○`, gated) added to `state-map.md`; new Cross-Domain Dependencies row added (`SK.14.Core` → `01.Core`, `Pending`) (presentation-arch-planner, WO-042)
- [2026-07-16] WO-042 P-262 closed — `01.Core`'s `C-43` confirmed shipped (`SharedKernel.Primitives/Propagation/WellKnownHeaders.cs` / `WellKnownBaggageKeys.cs`, namespace `SharedKernel.Primitives.Propagation`), unblocking the full remaining task set in one session. S-11 confirmed no new NuGet/`ProjectReference` was required — both constants ship in the already-referenced `SharedKernel.Primitives` package (the same one already used for `LoggingEventIdRanges`). C-19: `CorrelationIdMiddleware.HeaderName`/`.BaggageKey` changed from independently-owned literals to `public const string HeaderName = WellKnownHeaders.CorrelationId;` / `public const string BaggageKey = WellKnownBaggageKeys.CorrelationId;` (both C#-legal compile-time-constant forwards); `ItemsKey` confirmed untouched. C-20: build confirmed 0 warnings/0 errors beyond the pre-existing, unrelated `NU1903` advisory on `Microsoft.OpenApi` 2.0.0 (present on `main` before this session); grep confirmed no remaining call site outside the two forwarding-alias declarations re-types either literal. T-14: three new tests added to `CorrelationIdMiddlewareTests` (`HeaderName_ForwardsWellKnownHeadersCorrelationId`, `BaggageKey_ForwardsWellKnownBaggageKeysCorrelationId`, `ItemsKey_UnchangedFromPreP262Literal`) asserting byte-identical sourcing by direct reference to the `01.Core` constants, not a hand-copied literal on either side. DO-08: XML doc comments on `CorrelationIdMiddleware.HeaderName`/`.BaggageKey` rewritten to describe them as shipped forwarding aliases (`<see cref>` references to `WellKnownHeaders.CorrelationId`/`WellKnownBaggageKeys.CorrelationId`); this file's Interface Contracts and Correlation-id rules sections updated from PENDING to SHIPPED; DO-07's underlying concern now structurally resolved. P-07: `SharedKernel.Presentation.WebApi` re-packed to `1.0.2` (patch bump, additive forwarding-alias change only), `dotnet pack` 0 warnings beyond the known `NU1903` advisory; `consumer-verify` re-run confirms all 6 surfaces PASS with zero DI exceptions. 45/45 `SharedKernel.Presentation.WebApi.Tests` (+3) passing; 11/11 `SharedKernel.Presentation.SignalR.Tests` unchanged (unaffected package, re-run as regression confirmation). All six phases (`SK.14.Design` through `SK.14.Published`) now fully `●` — root Phase Backlog **P-262** closed (presentation-phase-implementer, WO-042)
- [2026-08-13] WO-058 P-381 dispatched — declarative `[RequireRole]`/`[RequirePermission]` endpoint-filter authorization for `SharedKernel.Presentation.WebApi`: an HTTP-boundary sibling to `05.Application`'s in-process `AuthorizationBehavior`, correctly routed through `IUserContext.HasRole`/`HasPermission` rather than the built-in ASP.NET Core `[Authorize(Roles=...)]` (which reads raw `ClaimTypes.Role` and would silently reintroduce the claim-shape fragility `WO-057`/`P-366` fixed). Design locked (D-15–D-19): `RequireRoleAttribute`/`RequirePermissionAttribute` as endpoint-metadata attributes usable on both MVC actions and minimal-API endpoints; a single global `AuthorizationRequirementEndpointFilter` (`IEndpointFilter`, metadata-driven, no-op when absent — mirroring the SignalR hub filter global-registration convention) that short-circuits a failing check through `Error.ToProblemDetails()`, never a bare 403; `AuthorizationEndpointFilterExtensions` minimal-API sugar and `AddSharedKernelAuthorizationFilters` DI registration, with the required (non-automatic) `.AddEndpointFilter<AuthorizationRequirementEndpointFilter>()` wiring step documented as this domain's one place "convention over configuration" cannot fully self-wire. New "Declarative role/permission authorization" Interface Contracts and Implementation Rules subsections added, both explicitly marked design-locked/implementation-gated. **Genuine cross-domain blocker recorded (D-18), not silently worked around:** direct read of the shipped `01.Core/SharedKernel.Primitives/Errors/Error.cs`/`ErrorType.cs` confirmed neither `Error.Forbidden(...)` nor `ErrorType.Forbidden` exists — substituting `Error.Unauthorized(...)` was explicitly declined since 401 (not-authenticated) and 403 (authenticated-but-forbidden) are semantically distinct outcomes this domain's own `ErrorTypeStatusCodeMap` already distinguishes for every other mapped `ErrorType`. Full task/blocker detail lives in `state-map.md`'s new Cross-Domain Dependencies row (`SK.14.Core` → `01.Core`, `Pending`) — a sibling `05.Application` phase (P-380, same WO-058) independently surfaced the identical gap. **Unrelated pre-existing documentation defect also corrected in the same pass (D-19):** the `ErrorTypeStatusCodeMap` Interface Contracts note had claimed a `Forbidden → 403`/`Failure → 500` mapping that never matched the real shipped `ErrorType` enum (no `Forbidden`/`Failure` member ever existed — only `Unexpected`/`BusinessRule`) or the real shipped `Resolve` switch; corrected above to the actual shipped six-case mapping (presentation-arch-planner, WO-058)
- [2026-08-17] WO-058 P-381 `SK.14.Core` (C-21–C-25) shipped — the `01.Core` blocker resolved (`Error.Forbidden`/`ErrorType.Forbidden` landed in `SharedKernel.Primitives` 1.1.0, P-384/WO-059); `RequireRoleAttribute`/`RequirePermissionAttribute`/`AuthorizationRequirementEndpointFilter`/`AuthorizationEndpointFilterExtensions`/`AddSharedKernelAuthorizationFilters` implemented in the new `Authorization/` namespace exactly per the D-15–D-17 locked design, plus `ErrorTypeStatusCodeMap.Resolve(ErrorType.Forbidden) → 403`. Authorization contract's "Design-locked/implementation gated" status flipped to "Scaffold and Core shipped"; the `ErrorTypeStatusCodeMap` Interface Contracts note's stale `PENDING` qualifier removed and its status list now includes `Forbidden → 403`; the "design-locked, implementation gated" Implementation Rules subheading and its D-18 rule bullet both updated to reflect shipped status; the DI Registration example's "PENDING" comment removed. **API-shape correction found this session:** `RouteGroupBuilder` lives in `Microsoft.AspNetCore.Routing`, not `Microsoft.AspNetCore.Builder` (confirmed via a real `CS0246` build failure) — noted alongside this domain's existing `Microsoft.OpenApi`/SignalR builder-type corrections. Package builds 0 warnings/0 errors; full existing 45/45 `SharedKernel.Presentation.WebApi.Tests` suite re-run green, zero regressions. Tests (T-15–T-18), Docs (DO-09/DO-10), and Published (P-08) remain open and out of scope for this session (presentation-phase-implementer)
- [2026-08-17] WO-058 P-381 `SK.14.Tests` (T-15–T-18) shipped — 26 new tests (4 `RequireRoleAttributeTests`, 4 `RequirePermissionAttributeTests`, 8 `AuthorizationRequirementEndpointFilterTests`, 9 `AuthorizationCompositionTests`, plus a new `ErrorType.Forbidden` `InlineData` row in `ErrorTypeStatusCodeMapTests`) added to `SharedKernel.Presentation.WebApi.Tests/Authorization/`. Confirmed three real API shapes via a throwaway reflection probe before writing assertions, per this domain's own "verify real API shapes" discipline: `EndpointFilterInvocationContext.Create(HttpContext)` is the correct static factory (no public constructor exists), `Results.Problem(ProblemDetails)` returns the concrete `Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult`, and `Endpoint`'s `RequestDelegate` constructor parameter accepts `null` at runtime despite its nullable annotation. New `Authorization/EndpointFilterTestHelpers.cs` (a `CreateContext(IUserContext?, params object[] metadata)` builder that only registers `IUserContext` in `HttpContext.RequestServices` when non-null, so the no-op-path test proves zero resolution was attempted simply by never registering the service) and `Authorization/IsAuthenticatedGuardUserContext.cs` (a test-only `IUserContext` whose `IsAuthenticated` getter throws, proving T-16's "rejected via the ordinary `HasRole`/`HasPermission` false-path, not a bespoke `IsAuthenticated` branch" requirement structurally rather than by inspection) added as new, reusable test infrastructure — both documented in a new "Testing endpoint filters without a host" Test Rules subsection. `16.Testing`'s existing `FakeUserContext` was sufficient for every authenticated-but-mismatched-role/permission case, so no new fake was needed there. Interface Contracts `Authorization/` Status banner updated from "Core shipped" to "Scaffold, Core, and Tests all shipped." `dotnet build`/`dotnet test SharedKernel.Presentation.WebApi.Tests.csproj --configuration Release` succeeds 0 errors and passes 71/71 (45 pre-existing + 26 net new), zero regressions. Docs (DO-09/DO-10) and Published (P-08) remain open and out of scope for this session (presentation-phase-implementer)
- [2026-08-17] WO-058 P-381 `SK.14.Docs` (DO-09/DO-10) shipped — read the four shipped `Authorization/` files directly before doing anything. **DO-09 found already-satisfied:** C-25's Core-phase XML docs were already complete, thorough `<remarks>` prose covering both non-obvious design decisions the phase called out (the deliberate absence of an `IsAuthenticated` branch, and the no-op-when-absent behavior). Verified with a real `dotnet build --configuration Release`: 0 errors, zero CS1591/CS1574/CS1580, only the pre-existing unrelated `NU1903` advisory — no doc edits made, consistent with this domain's established DO-06/DO-07/DO-08 pattern of Docs tasks turning out already-satisfied. **DO-10 (the substantive task):** added a "Declarative role/permission authorization" section to `SharedKernel.Presentation.WebApi/README.md` — Minimal API form (route-group `.AddEndpointFilter<AuthorizationRequirementEndpointFilter>()` + `.RequireRole`/`.RequirePermission` sugar), MVC form (`[RequireRole("Admin")]` action attribute + `MapControllers().AddEndpointFilter<AuthorizationRequirementEndpointFilter>()`), the AND-across/OR-within composition rule stated as fixed and non-configurable, an unsoftened blockquote callout that this filter — unlike SignalR's global hub filters — never auto-attaches and a missed `.AddEndpointFilter<...>()` call leaves every attribute silently inert, and the `[Authorize(Roles=...)]`-must-never-be-mixed-in rule tying back to `WO-057`/`P-366`. Added a matching `AddSharedKernelAuthorizationFilters` entry to `14.Presentation/CONFIGURATION.md` (judged in-scope per DO-05's "every DI extension method documented" precedent). Interface Contracts `Authorization/` Status banner updated from "Scaffold, Core, and Tests all shipped" to "Scaffold, Core, Tests, and Docs all shipped — only Published (P-08) remains open." Zero source files touched; full `SharedKernel.Presentation.WebApi.Tests` suite re-confirmed 71/71 green, unchanged. `SK.14.Docs` now `●` 10/10, root Phase Backlog **P-381** remains open pending only `SK.14.Published` (presentation-phase-implementer)
- [2026-08-17] WO-058 P-381 `SK.14.Published` (P-08) shipped — the final task of the `14.Presentation` domain. Re-packed `SharedKernel.Presentation.WebApi` to `1.1.0` (a **minor** bump per the task's explicit instruction — the `Authorization/` namespace is a genuinely new additive public surface, not a patch-level fix); `Description`/`PackageTags` extended to mention the authorization surface. `SharedKernel.Presentation.SignalR` untouched (WO-058 never targeted it), stays at `1.0.1`. `consumer-verify/Program.cs` gained **Surface 7**: composes `AddSharedKernelAuthorizationFilters()` alongside the existing full WebApi stack, resolves `AuthorizationRequirementEndpointFilter` as a registered singleton, then proves the documented wiring form itself — `.MapGroup(...).AddEndpointFilter<AuthorizationRequirementEndpointFilter>()` plus `.RequireRole`/`.RequirePermission` sugar on a mapped route — constructs with zero DI/build exceptions. All 7 surfaces PASS. Both previously-documented environment facts re-verified rather than assumed: the harness still hits the pre-existing `NU1903`-as-error on `Microsoft.OpenApi` 2.0.0 (worked around locally with `-p:WarningsNotAsErrors=NU1903` for this session's verification run only, not committed); no internal NuGet feed exists in this repo, so `dotnet pack` produced local `.nupkg`/`.snupkg` only — no `dotnet nuget push` was run, no publish occurred. Full regression: `SharedKernel.Presentation.WebApi.Tests` 71/71 green, `SharedKernel.Presentation.SignalR.Tests` 11/11 green. Interface Contracts `Authorization/` Status banner updated from "only Published (P-08) remains open" to "Shipped end to end." All six `SK.14.*` phase keys now `●` — root Phase Backlog **P-381** closes as a consequence (presentation-phase-implementer)
- [2026-08-20] WO-062 dispatched — eight new phases (P-402–P-409) processed against this already-fully-`●`-Published domain, a big-fintech/gold-standard hardening pass covering both packages. **`SharedKernel.Presentation.WebApi` (P-402–P-408, seven phases):** (1) multi-field validation `ProblemDetails` — fixes a confirmed silent-data-loss defect where `SharedKernelExceptionHandler` surfaced only `ValidationException.Errors[0]`; new `ValidationProblemDetailsExtensions.ToProblemDetails(ValidationException, ...)` groups every field by `Error.Code` into `Extensions["errors"]`, mirroring ASP.NET Core's own `ValidationProblemDetails.Errors` shape, additive to the untouched single-`Error` path. (2) Security response headers — `UseSharedKernelSecurityHeaders`/`SecurityHeadersOptions`: HSTS/`X-Content-Type-Options`/`X-Frame-Options`/`Referrer-Policy`/`Permissions-Policy` on by default, CSP deliberately opt-in-only, never overwrites an already-set header. (3) CORS convention builder — `AddSharedKernelCors`/`CorsPolicyOptions` makes the classic `AllowAnyOrigin()`+`AllowCredentials()` misconfiguration structurally inexpressible via a startup `ValidateOnStart()` fail-fast guard. (4) Inbound idempotency-key HTTP boundary — `TryGetIdempotencyKey`/`RequireIdempotencyKeyAttribute`/`IdempotencyKeyRequirementEndpointFilter`, closing the gap between `05.Application`'s in-process `IIdempotentRequest` and `11.Communication.Rest`'s still-queued outbound propagation (P-364); deliberately a **separate** filter from `AuthorizationRequirementEndpointFilter`, not an authorization concern. (5) Declarative step-up/fresh-authentication — `[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` **extend** (never duplicate) `AuthorizationRequirementEndpointFilter`, built on `IUserContext.IsAuthenticationFresherThan(TimeSpan, DateTimeOffset)`/`.WasAuthenticatedWith(string)` — both already shipped in `12.Security.Abstractions` (WO-058/P-375, confirmed via direct read of the real `IUserContext.cs`, which also confirmed `IsAuthenticationFresherThan` already takes an explicit `now` per the platform's injectable-time convention, so this filter resolves `IClock` from `HttpContext.RequestServices` lazily, only when the new attribute is present). Verified — not assumed — that `16.Testing`'s `FakeUserContext`/`SecurityTestContextBuilder` already expose settable `AuthTime`/`AuthenticationMethods`; no `16.Testing` gap, no follow-up phase escalated. (6) ETag/`If-Match` conditional-request helpers — `RowVersionETag`/`ConditionalRequestExtensions` bridge `06.Persistence`'s row-version concurrency to RFC 9110 §13, additive to `Error.Conflict`/409. (7) Rate-limit-rejection→429 bridge — `RateLimitRejectionProblemDetails` finally builds the real helper `13.ServiceDefaults`'s `AddSharedKernelRateLimiting()` (P-397/WO-061) deferred here without ever landing one. **`SharedKernel.Presentation.SignalR` (P-409, one phase):** conservative, documented, always-overridable `HubOptions` defaults (`MaximumReceiveMessageSize`/`MaximumParallelInvocationsPerClient`/`ClientTimeoutInterval`/`KeepAliveInterval`) set inside `AddSharedKernelSignalR` before the caller's `configureHubOptions` runs, closing a resource-exhaustion gap with no prior platform default. **Two design decisions made instead of escalating cross-domain blockers (mirroring D-18's `Error.Forbidden` precedent from WO-058, but resolved locally rather than gated):** `IdempotencyKeyHeader` ships as a domain-local constant (mirrors `CorrelationIdMiddleware`'s pre-WO-042 shape; `01.Core`'s `WellKnownHeaders` confirmed to have no `IdempotencyKey` member, not requested); a candidate `ErrorType.PreconditionFailed` for the 412 path was declined — HTTP protocol-level outcomes (412, 429) that never originate as a domain `Error` are built via a new shared internal RFC 9457 shaping helper (extracted from `ErrorProblemDetailsExtensions`'s existing `ProblemTypeBaseUri` pattern) instead of growing `ErrorType` for non-`Error` outcomes. Both recorded as non-blocking, informational rows in `state-map.md`'s Cross-Domain Dependencies table for future-promotion traceability, not as `Pending` gates. New Interface Contracts subsections added for all eight capabilities (all marked "Design-locked (WO-062, P-40x)"); five new Implementation Rules subsections added (Security response header rules, CORS policy rules, Inbound idempotency-key rules, ETag/conditional-request rules, Rate-limit rejection bridge rules) plus extensions to the existing ProblemDetails rules, Declarative role/permission authorization rules, and SignalR Redis backplane rules sections; DI Registration gained eight new worked examples; Test Rules gained a new "WO-062 test additions" subsection. Task rows D-20–D-45, S-13–S-20, C-26–C-50, T-19–T-43, DO-11–DO-18, P-09–P-16 added to `state-map.md`, all `○` — every prior task through D-19/S-12/C-25/T-18/DO-10/P-08 remains `●` and unaffected. Per this agent's jurisdiction, the root `CLAUDE.md` "What Goes Where" rows several of these phases' acceptance criteria request are **not** added here — flagged as a follow-up for `arch-lead`/`sync-brain`, mirroring the DO-07 cross-domain-flag-not-fix precedent (presentation-arch-planner, WO-062)
- [2026-08-20] WO-062 `SK.14.Core` shipped (C-26–C-50, 25/25) — all eight P-402–P-409 Interface Contracts/Implementation Rules status banners flipped from "Design-locked" to "Core implementation shipped — Tests/Docs/Published still pending": `ValidationProblemDetailsExtensions`, `SecurityHeadersOptions`/`SecurityHeadersMiddleware`/`UseSharedKernelSecurityHeaders`, `CorsPolicyOptions`/`AddSharedKernelCors`/`CorsPolicyOptionsValidator`, `HttpContextIdempotencyExtensions`/`RequireIdempotencyKeyAttribute`/`IdempotencyKeyRequirementEndpointFilter`, `RequireFreshAuthenticationAttribute`/`RequireAuthenticationMethodAttribute` extending `AuthorizationRequirementEndpointFilter`, `RowVersionETag`/`ConditionalRequestExtensions`/the new shared internal `Http/ProblemDetailsShaping` helper, `RateLimitRejectionProblemDetails`, and the `AddSharedKernelSignalR` `HubOptions` defaults all now live in source. Two "CORRECTED at Core-phase implementation" notes added, both verified via a throwaway reflection probe against the installed `net10.0` shared framework before use, per this domain's established discipline: `EntityTagHeaderValue`'s RFC 9110 strong-comparison member is the instance method `.Compare(EntityTagHeaderValue, bool)`, not a static overload, and `Microsoft.AspNetCore.Http.Headers.RequestHeaders(IHeaderDictionary).IfMatch` is the correct typed-header access path; `HubOptions.MaximumReceiveMessageSize` is `long?`, `.MaximumParallelInvocationsPerClient` is a non-nullable `int`, `.ClientTimeoutInterval`/`.KeepAliveInterval` are `TimeSpan?` — confirmed, not assumed. Shipped `AddSharedKernelSignalR` default values recorded verbatim (`MaximumReceiveMessageSize = 32 * 1024`, `MaximumParallelInvocationsPerClient = 1`, `ClientTimeoutInterval = 30s`, `KeepAliveInterval = 15s`). Zero new `PackageReference`/`ProjectReference` — confirmed by the prior Scaffold-phase session (S-13–S-20). Both packages build 0 warnings/0 errors beyond the pre-existing unrelated `NU1903` advisory; full regression `SharedKernel.Presentation.WebApi.Tests` 71/71 + `SharedKernel.Presentation.SignalR.Tests` 11/11 green, proving the single-`Error` `ProblemDetails` path stayed byte-for-byte unchanged (D-22's non-goal). `SK.14.Tests`/`Docs`/`Published` (T-19–T-43/DO-11–DO-18/P-09–P-16) remain open — next phase (presentation-phase-implementer, state-map-phase)
- [2026-08-20] WO-062 `SK.14.Tests` shipped (T-19–T-43, 25/25) — 63 new tests across both packages implementing every remaining WO-062 test task. `SharedKernel.Presentation.WebApi.Tests` gained `ValidationProblemDetailsExtensionsTests`, two new `SharedKernelExceptionHandlerTests` methods (multi-error handler wiring proof; a `TheoryData<SharedKernelException, Error>`-driven regression sweep across every non-`ValidationException` `ErrorType`), `Middleware/SecurityHeadersMiddlewareTests`, `Cors/CorsExtensionsTests`, `Idempotency/HttpContextIdempotencyExtensionsTests`/`IdempotencyKeyRequirementEndpointFilterTests`, `Authorization/StepUpAuthenticationTests`, `Concurrency/RowVersionETagTests`/`ConditionalRequestExtensionsTests`, and `RateLimiting/RateLimitRejectionProblemDetailsTests`; `SharedKernel.Presentation.SignalR.Tests` gained `Extensions/SignalRExtensionsHubOptionsTests`. `SharedKernel.Presentation.WebApi.Tests` now 134/134 green (71 pre-existing + 63 net new); `SharedKernel.Presentation.SignalR.Tests` now 14/14 green (11 pre-existing + 3 net new). **Two pre-existing documentation defects found and corrected in the same pass, unrelated to any new capability:** three "six-case"/"six mapped `ErrorType`s" references to `ErrorTypeStatusCodeMap` (P-407's Interface Contracts note, its Implementation Rules bullet, and the Test Rules bullet) were stale — the real shipped map has carried seven explicit cases since `ErrorType.Forbidden` shipped in WO-058, and this P-407 prose was apparently written against an older count; corrected in place, mirroring D-19's established precedent for this exact class of drift. All eight P-402–P-409 Interface Contracts status banners flipped from "Core implementation shipped — Tests/Docs/Published still pending" to "Core and Tests implementation shipped — Docs/Published still pending." `SK.14.Docs` (DO-11–DO-18)/`SK.14.Published` (P-09–P-16) remain open — next phase (presentation-phase-implementer, state-map-phase)
- [2026-08-20] WO-062 `SK.14.Published` shipped (P-09–P-16, 16/16) — the domain's final WO-062 phase, closing all six `SK.14.*` phase keys end to end. `SharedKernel.Presentation.WebApi` re-packed **once**, `1.1.0` → `1.2.0` (a single coherent minor bump covering all six additive Core-phase capabilities rather than seven sequential per-phase bumps, since none of the intermediate versions ever shipped to a consumer); `SharedKernel.Presentation.SignalR` re-packed `1.0.1` → `1.0.2` (patch — P-409's `HubOptions` default-value change is purely internal). `consumer-verify` gained Surface 8 (CORS + security headers, including a negative-path proof that `CorsPolicyOptionsValidator` genuinely fails fast), Surface 9 (idempotency filters), and Surface 10 (direct functional checks of the pure-static ETag/rate-limit helpers, which carry no DI wiring of their own); Surface 7 extended in place to also compose the step-up-authentication attributes. All 10 surfaces PASS with zero DI exceptions; `SharedKernel.Presentation.WebApi.Tests` 134/134 green, `SharedKernel.Presentation.SignalR.Tests` 14/14 green, zero regressions. All eight P-402–P-409 Interface Contracts status banners flipped from "Core, Tests, and Docs implementation shipped — Published still pending" to "Shipped end to end," each noting its package's new version. This closes `14.Presentation`'s WO-062 scope end to end (presentation-phase-implementer, state-map-phase, sync-brain)
- [2026-08-20] WO-063 dispatched — eight new phases (P-411–P-418) processed against this already-fully-`●`-Published domain (`SharedKernel.Presentation.WebApi` `1.2.0`, `.SignalR` `1.0.2`), a second big-fintech/gold-standard hardening pass distinct from WO-062's rejection-shape/perimeter focus: this pass targets resource-exhaustion (request payload size/JSON depth, SignalR invocation rate), documentation completeness (OpenAPI ApiKey/mTLS security schemes, RFC 8594 Sunset/Deprecation headers), audit-trail completeness (closing this domain's near-total historical non-use of its own reserved `14000`–`14999` `EventId` range — only 3 of ~1000 IDs consumed before this WO), and two remaining input-trust-boundary gaps (caller-supplied correlation-id format, file/multipart upload shape). **`SharedKernel.Presentation.WebApi` (P-411–P-416, six phases):** (1) Payload-size/JSON-max-depth DoS protection — `PayloadLimitsOptions`/`AddSharedKernelPayloadLimits`/`UseSharedKernelPayloadLimits` wrapping Kestrel's `IHttpMaxRequestBodySizeFeature` and STJ's `MaxDepth`; a 413 is mapped through a new `BadHttpRequestException`-specific branch on `SharedKernelExceptionHandler` rather than assumed to already be handled — flagged for Core-phase verification against the real framework. (2) OpenAPI ApiKey/mTLS security-scheme completeness — `OpenApiSecuritySchemesOptions` registers each active scheme as its own OR'd security requirement object; declined a new `ProjectReference` on `SharedKernel.Security.ApiKey` merely to reuse a header-name constant. (3) RFC 8594 Sunset/Deprecation headers — `ApiVersionLifecycleOptions` extends `AddSharedKernelApiVersioning`, sourcing deprecation status from Asp.Versioning's own existing `Deprecated` declaration rather than a parallel flag. (4) Structured security-audit logging — closes the last unlogged HTTP-boundary rejection paths in the chain `12.Security`(WO-057)/`13.ServiceDefaults`(WO-061) already covered: `AuthorizationRequirementEndpointFilter` (14002), `IdempotencyKeyRequirementEndpointFilter` (14003), `CorsPolicyOptionsValidator` (14004), `RateLimitRejectionProblemDetails` (14005). (5) Caller-supplied correlation-id format validation — a bounded length-plus-safe-character-allowlist check on `CorrelationIdMiddleware.ResolveCorrelationId`, closing the last unvalidated-external-input trust-boundary class this platform has fixed twice elsewhere (`11.Communication` WO-056, `13.ServiceDefaults` WO-061) but never yet at the point a raw correlation-id header first enters the system. (6) File/multipart upload size and content-type validation — `UploadValidationOptions`/`RequireValidatedUploadAttribute`/`UploadValidationEndpointFilter`, explicitly and repeatedly documented as boundary-shape validation only, NEVER virus/malware scanning. **`SharedKernel.Presentation.SignalR` (P-417–P-418, two phases):** (7) Hub-level per-connection invocation rate limiting plus argument-payload validation — `HubInvocationRateLimitFilter` built on `System.Threading.RateLimiting`, opt-in via a new `configureRateLimit` parameter. **Genuine pre-existing composition hazard found and design-locked, not left for Core-phase discovery:** `HubExceptionMappingFilter`'s existing catch-all had no branch recognizing an already-thrown `HubException` as terminal, so a rate-limit rejection's specific message would otherwise be silently re-wrapped into the generic redacted one — fixed via a new `catch (HubException) { throw; }` branch checked first, now a standing hub-filter-composition rule. (8) SignalR CORS/negotiate-endpoint origin-policy integration — **declined** a new `ProjectReference` from `.SignalR` to `.WebApi`'s `CorsPolicyOptions` (the two packages remain deliberately independent API surfaces); closed instead via a startup-time diagnostic `Warning` (new `EventId` 14102) plus a worked README example cross-referencing `CorsPolicyNames.Default` by name only. **Zero new cross-domain dependency across all eight phases** — every capability builds on BCL/already-referenced packages or prior-shipped `12.Security` capabilities; the one open item is a Scaffold-phase build-probe (S-27) confirming whether `System.Threading.RateLimiting` ships transitively via the existing `FrameworkReference Microsoft.AspNetCore.App` on `net10.0`. Eight new Interface Contracts subsections added (all "Design-locked, WO-063"); six new Implementation Rules subsections added (Payload-limits, OpenAPI security-scheme, API version lifecycle, Security-audit logging, Correlation-id format-validation, Upload validation rules) plus extensions to the existing SignalR hub filter rules (the composition-hazard fix) and a new SignalR CORS integration rule section; the EventId sub-block documentation updated with the six newly-allocated IDs; DI Registration gained eight new worked examples; Test Rules gained a new "WO-063 test additions" subsection. New task rows D-46–D-66, S-21–S-28, C-51–C-74, T-44–T-67, DO-19–DO-26, P-17–P-24 added to `state-map.md`, all `○` — every prior task through D-45/S-20/C-50/T-43/DO-18/P-16 remains `●` and unaffected. Per this agent's jurisdiction, no root `CLAUDE.md` "What Goes Where" rows are added here — flagged as a follow-up for `arch-lead`/`sync-brain`, mirroring the WO-062/DO-07 cross-domain-flag-not-fix precedent (presentation-arch-planner, WO-063)
- [2026-08-20] WO-063 `SK.14.Core` shipped (C-51–C-74) — all eight P-411–P-418 status banners and Implementation Rules headers flipped from "Design-locked" to "Shipped end to end"; documented two genuine `Microsoft.OpenApi` 2.0.0 API-shape discoveries (no `mutualTLS` enum member, requiring a `SerializeAsV31`-overriding subclass; `OpenApiSecuritySchemeReference` needs `document.RegisterComponents()` or it serializes as `{}`); documented the deliberate decision NOT to drive Asp.Versioning's own `Policies.Sunset`/`DefaultApiVersionReporter` surface for RFC 8594 headers (a real round trip showed it never fires for an empty-named policy within session time) in favor of an independent `ApiVersionLifecycleOptions` registry + self-inserting `IStartupFilter`; documented the real `ICorsMetadata`/`NegotiateMetadata`/`HubMetadata` reflection findings for the SignalR CORS diagnostic; documented that `HubExceptionMappingFilter`'s `catch (HubException) { throw; }` branch was already present since WO-031 (D-64's "hazard" was a stale read, not a real gap — no code changed for C-71); added a new standing rule requiring every newly-DI-logging-enabled type's `ILogger<T>` constructor parameter to be optional (`? logger = null`, falling back to `NullLogger<T>.Instance`) after a required-parameter version broke `consumer-verify`'s own bare-`ServiceCollection` negative-path test; corrected two DI Registration examples that no longer matched the shipped API shape (`ApiVersionLifecycleOptions.AddSunset(...)` → `.Configure(...)`; `RequireValidatedUpload`'s named-`params`-argument syntax → positional); corrected the AOT notes' `System.Threading.RateLimiting` entry from "not yet confirmed" to "confirmed transitively available" (S-27 had already proven this at Scaffold phase but the brain was never updated). `SharedKernel.Presentation.WebApi.Tests` 134/134 green, `SharedKernel.Presentation.SignalR.Tests` 14/14 green, `consumer-verify` all 10 surfaces PASS — zero regression (presentation-phase-implementer, sync-brain)
- [2026-08-21] WO-063 `SK.14.Tests` shipped (T-44–T-67, 24/24) — 63 net-new tests across both packages (52 WebApi + 11 SignalR), zero production code touched, confirming the Core phase's shipped surface was already correct on the first pass. The "WO-063 test additions" Test Rules subsection rewritten from "design-locked; queued" to "confirmed — shipped," with three genuine test-construction discoveries preserved for future sessions: (1) a `Content-Length`-declared oversized body is rejected by Kestrel at the connection level with an empty response body, before the exception-handling middleware ever runs — the 413-with-handler-never-reached proof requires chunked transfer encoding instead, forcing Kestrel to read incrementally and throw `BadHttpRequestException` from inside the running pipeline; (2) `CorsPolicyOptionsValidator` and `SignalRCorsStartupDiagnostic` are both `internal`, so their logging is proven indirectly via a real `IHost`/`TestServer` plus `16.Testing`'s `AddInMemoryLoggerFactory()` with an explicit `ILoggerFactory` substitution registered afterward (last-registration-wins), retrieving records via a hardcoded full-type-name category string since `typeof()` cannot reach an internal type across the assembly boundary; (3) `Activity.Current` is reliably non-null in a plain unit test via `new Activity(name).Start()` with no `ActivityListener` registration required (unlike `ActivitySource.StartActivity`, which does require one) — used to prove the correlation-id validator's rejected-value-never-reaches-baggage acceptance criterion. `SharedKernel.Presentation.WebApi.Tests` now 186/186 green (134 + 52 new), `SharedKernel.Presentation.SignalR.Tests` now 25/25 green (14 + 11 new). **Flagged, not fixed:** `consumer-verify`'s own build now fails via `TreatWarningsAsErrors=true` tripping on a pre-existing `Microsoft.OpenApi` 2.0.0 `NU1903` advisory (a consequence of the already-shipped Core phase's `MutualTlsSecurityScheme`, not this Tests phase) — all 10 harness surfaces confirmed logically PASS via a `-p:NoWarn=NU1903` override; the build-gate fix itself is `devops-lead`/Core-phase-session jurisdiction (presentation-phase-implementer, state-map-phase, sync-brain)
- [2026-08-21] WO-063 `SK.14.Published` shipped (P-17–P-24, 24/24) — closes WO-063 (P-411–P-418) and all six `SK.14.*` phase keys end to end. **The prior session's flagged `consumer-verify` build blocker is resolved, not suppressed:** investigated per the three-path instruction — a patched `Microsoft.OpenApi` version exists (`2.7.5`+ on the 2.x line, per the GitHub Advisory API for `GHSA-v5pm-xwqc-g5wc`/`CVE-2026-49451`), and `Microsoft.AspNetCore.OpenApi` `10.0.11`'s own `.nuspec` (confirmed via direct inspection, not `10.0.9`/`10.0.10`, which both still hard-pin `Microsoft.OpenApi` `2.0.0`) declares the patched range — so `SharedKernel.Presentation.WebApi`'s `Microsoft.AspNetCore.OpenApi` reference was bumped `10.0.9` → `10.0.11` (Technology Stack table updated above), resolving `Microsoft.OpenApi` to `2.7.5` transitively. `consumer-verify` now builds and runs with zero `NoWarn`/`WarningsNotAsErrors` overrides of any kind, closing out every prior session's documented workaround (WO-041/WO-042/WO-058/WO-062 all separately worked around the same advisory locally without fixing it). `consumer-verify/Program.cs` gained the two phase-mandated new permanent surfaces: **Surface 11** (P-18) — a real listening-Kestrel-host round trip generating the actual OpenAPI document with a non-default `Bearer`+`ApiKey`+`MutualTls` scheme combination, asserting on the response body rather than DI resolution alone; **Surface 12** (P-22) — `AddSharedKernelUploadValidation()` DI composition mirroring Surface 9's shape. All 12 surfaces PASS with zero DI exceptions and zero build warnings. `SharedKernel.Presentation.WebApi` re-packed once, `1.2.0` → `1.3.0`; `SharedKernel.Presentation.SignalR` re-packed once, `1.0.2` → `1.1.0` — both single coherent minor bumps per the WO-062 precedent, `Description`/`PackageTags` extended for both. Both `dotnet pack` runs: 0 warnings, 0 errors. Full regression: `SharedKernel.Presentation.WebApi.Tests` 186/186 green, `SharedKernel.Presentation.SignalR.Tests` 25/25 green (presentation-phase-implementer, state-map-phase, sync-brain)
- [2026-09-04] **WO-074/P-468 and WO-078/P-484 both shipped end to end** (Scaffold through Docs, all six phase keys' remaining tasks for these two phases complete). **WO-074/P-468:** `SharedKernel.Presentation.Grpc` created and implemented in full — `Grpc.AspNetCore` `2.80.0` pinned (already present in `Directory.Packages.props`, no new entry needed; `Grpc.Tools` compiles the test project's `.proto` transitively with no separate pin). All five design questions the Design-lock phase left open are now closed by direct empirical proof, not assumption: `Grpc.Core.ServerCallContextExtensions.GetHttpContext` confirmed present via reflection over the real installed assembly; `ServerCallContext.GetHttpContext()?.GetEndpoint()?.Metadata` CONFIRMED to surface attributes applied directly to a gRPC service method (8/8 `GrpcAuthorizationInterceptorIntegrationTests` pass against a real host — the reflection fallback was never needed); the correlation metadata key round-trips byte-for-byte against `SharedKernel.Communication.Grpc`'s real client interceptor (proven, not merely documented); and a genuine design gap was discovered and resolved during Tests-phase authoring — `GrpcTenantContextInterceptor` never reads gRPC metadata itself (only `ITenantProvider`, exactly mirroring `TenantContextHubFilter`), so the round-trip test's server composition needed a test-only `HeaderTenantProvider` reading the raw header, which is now documented as the correct integration pattern for a consuming service, not a gap in this package. `error.Description`, referenced in the original design draft (and in a pre-existing, now-fixed stale note on `ErrorProblemDetailsExtensions` itself, unrelated to this phase but corrected in passing), was never a real `Error` member — `Error.Message` is used throughout, matching the real `SharedKernel.Primitives.Errors.Error` shape. 36/36 tests green across five test files (`GrpcStatusCodeMapTests`, `GrpcResultExtensionsTests`, `LoggerMessageEventIdTests`, and three real-host integration-test classes under `Integration/`), zero warnings, zero errors. `EventId`s `14200`/`14201` assigned and pinned. A `README.md` was authored for the new package; `CONFIGURATION.md` gained a full `SharedKernel.Presentation.Grpc` section. **WO-078/P-484:** `01.Core` had already shipped the real `SharedKernel.Localization` package by this session (confirmed on disk, not from a stale CLAUDE.md claim) — the phase's blocker was cleared. `ErrorProblemDetailsExtensions.ToProblemDetails`/`ValidationProblemDetailsExtensions.ToProblemDetails` extended via a new internal `Errors/LocalizedDetailResolver.cs` helper; `SharedKernel.Presentation.WebApi.csproj` gained a `ProjectReference` on `01.Core/SharedKernel.Localization`. One implementation bug caught and fixed during Tests-phase authoring: `context?.RequestServices.GetService<...>()` threw `ArgumentNullException` against several pre-existing tests that construct a bare `DefaultHttpContext` with no `RequestServices` assigned (that property returns `null`, it does not throw) — fixed to `context?.RequestServices?.GetService<...>()`, both `?.`s load-bearing. `SharedKernel.Presentation.WebApi.Tests` now 192/192 green (185 pre-existing + 7 new `LocalizedProblemDetailsTests`), zero regressions. `CONFIGURATION.md` gained a new "`Error.ToProblemDetails()` optional localization" section documenting the "activates automatically, no `AddSharedKernelXxx()` call" behavior per DO-34. Every "design-locked"/"○ Pending"/"blocked" status marker for both phases across this file (Packages table, Technology Stack table, both Interface Contracts subsections, both Implementation Rules subsections, AOT notes, DI Registration examples, both Test Rules subsections) updated to reflect shipped status — the design content itself needed only the corrections noted above, confirming the original design-lock (WO-074) and design amendment (WO-078) were both substantially correct on the first pass (presentation-phase-implementer, per-domain `state-map-phase` and `sync-brain` still to run this session). Prior entry below (2026-08-26, design-lock) preserved unedited for history.
- [2026-08-26] Two new phases design-locked from the root `state-map.md` Phase Backlog (WO-074/P-468, WO-078/P-484), processed in dependency order. **WO-074/P-468:** a new third `14.Presentation` sibling package, `SharedKernel.Presentation.Grpc` — server-side gRPC's inbound-API-boundary counterpart to `.WebApi`'s HTTP story, added to the Packages table, Technology Stack table, and a new full Interface Contracts subsection (`GrpcStatusCodeMap` as a sibling to, never a merge with, `ErrorTypeStatusCodeMap`; `GrpcResultExtensions`; a four-call-shape `GrpcExceptionInterceptor`; `GrpcCorrelationInterceptor`/`GrpcTenantContextInterceptor`; `GrpcAuthorizationInterceptor` reusing `.WebApi`'s four `Authorization/` attributes verbatim via a new, deliberate intra-domain `ProjectReference`; `AddSharedKernelGrpc`). Two new explanatory subsections added under "Packages" — "Why server-side gRPC lives in `14.Presentation`, not `11.Communication.Grpc`" and "Why `.Grpc` references `.WebApi`" (the latter explicitly distinguished from `.SignalR`'s declined identical-shaped reference for CORS, P-418/D-65 — different, both-correct reasoning, not to be conflated). Never references `04.Contracts` (named Hard-rule exception, mirrors `SharedKernel.Communication.Grpc`'s P-163 rule); no new `13.ServiceDefaults` telemetry entry point needed. **WO-078/P-484:** `Error.ToProblemDetails()`/`ValidationProblemDetailsExtensions` gain an optional localization step (new "Localized `ProblemDetails.Detail`" Interface Contracts subsection, a new ProblemDetails-rules bullet) — an `ILocalizationCatalog` (`01.Core/SharedKernel.Localization`, P-482) lookup keyed by `(error.Code, CultureInfo.CurrentUICulture)`, falling back to `error.Message` verbatim, never blank; `01.Core.Primitives.Error` itself untouched; this package takes a new `ProjectReference` on `01.Core/SharedKernel.Localization` (a deliberate decision, distinguished from `ApiKeyHeaderName`'s declined-reference precedent, P-412 — `ILocalizationCatalog` is a `01.Core` abstraction, not a concrete provider). Both phases are `○` Pending, Design-locked only — Core-phase implementation has not started for either; WO-078's Scaffold/Core/Tests/Docs/Published work is additionally recorded-but-blocked on `01.Core` shipping the real `SharedKernel.Localization` package (P-482). New gRPC and localization subsections added under Implementation Rules, AOT notes, DI Registration, and Test Rules. `state-map.md` gained 65 new `○` task rows across all six phase keys (P-468: D-67–D-79/S-29–S-34/C-75–C-84/T-68–T-75/DO-27–DO-32/P-25–P-27; P-484: D-80–D-86/S-35/C-85–C-87/T-76–T-80/DO-33–DO-34/P-28), a new Package Board row for `.Grpc`, and a new Cross-Domain Dependencies row (`SK.14.Core` → `01.Core`, `Pending`, for P-484). Zero regression to the prior 243/243 WO-062+WO-063 closure — none of that work was reopened or altered (presentation-arch-planner)
- [2026-09-15] Contracts redesign: `ResultHttpExtensions`/`GrpcResultExtensions` rules no longer contrast with the removed `ResultEnvelopeExtensions`/`Envelope<T>` — RFC 9457 ProblemDetails is the only HTTP error format, with no response-wrapper DTO (coordinator)
- [2026-09-15] `SharedKernel.Presentation.WebApi` no longer references `SharedKernel.Contracts` (the reference was unused): Packages table corrected (and its missing `SharedKernel.Localization` reference added), layering note records that no package in this domain references `04.Contracts`, and the `.Grpc` rule notes Contracts is no longer reachable transitively through `.WebApi` (coordinator)
