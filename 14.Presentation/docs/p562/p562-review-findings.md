# P-562 — Presentation review findings (2026-09-23)

Review of `SharedKernel.Presentation.WebApi`, `.SignalR` and `.Grpc` before their first publish. Every source file
was read; framework facts were checked against .NET 10 sources and docs, Asp.Versioning 10, Scalar, OpenAPI.NET,
grpc-dotnet and the RFCs. IDs are referenced by [`p562-design.md`](p562-design.md). Line numbers are those at
review time (base `3a296bb5`).

## Evidence

- Source 4,650 lines (WebApi 3,300 / SignalR 560 / gRPC 780), tests 5,480 lines. No package tracked its public API.
- The five reference services used two things: `ToProblemDetailsResult` (~55 calls) and
  `SharedKernelExceptionHandler` (1). No sample used correlation IDs, security headers, CORS, OpenAPI, versioning,
  the authorization attributes, idempotency, payload limits, uploads, ETag helpers or the 429 bridge. BillingApi
  hand-wrote ETag/`If-Match`/428/412 with inline `Results.Problem` because the helpers could not express it.
- Full-stack wiring (consumer-verify) needed about 18 distinct calls, with ordering rules in XML remarks, plus a
  per-group `.AddEndpointFilter<…>()` for three filters.
- Domain `README.md` was empty; `CLAUDE.md` was 223 KB (956 lines of design history under "Interface Contracts").
- No test anywhere asserted the `application/problem+json` media type.

## B — Bugs

| ID | Finding | Where |
| --- | --- | --- |
| B1 | `[RequireRole]`/`[RequirePermission]`/fresh-auth attributes did nothing unless `.AddEndpointFilter<AuthorizationRequirementEndpointFilter>()` was also added to each group or `MapControllers()` — fail-open by omission. | `WebApi/Authorization/AuthorizationEndpointFilterExtensions.cs:137` |
| B2 | Anonymous callers were rejected with 403 instead of 401 (HTTP and gRPC), unlike 05.Application's `AuthorizationBehavior`. | `AuthorizationRequirementEndpointFilter.cs:96`, `Grpc/Interceptors/GrpcAuthorizationInterceptor.cs:148` |
| B3 | Localization (P-484) never ran on the primary path: `ToProblemDetailsResult`/`ToActionResult` called `ToProblemDetails()` without the `HttpContext`, so no `ILocalizationCatalog` was resolved. Untested. | `WebApi/Results/ResultHttpExtensions.cs:49,76,94,114` |
| B4 | The exception handler wrote `application/json` (`WriteAsJsonAsync` without content type) instead of `application/problem+json`, bypassing `IProblemDetailsService`, so `CustomizeProblemDetails` never applied. | `WebApi/ExceptionHandling/SharedKernelExceptionHandler.cs:97` |
| B5 | The Development exception-detail gate was dead: both branches produced "An unexpected error occurred." | `SharedKernelExceptionHandler.cs:90,113` |
| B6 | Server-category `Error.Message` text reached clients verbatim on the `Result` path (e.g. `search.unreachable` carries the internal endpoint URL). Only unknown exceptions were redacted. | `WebApi/Errors/ErrorProblemDetailsExtensions.cs:50`, `09.Search/.../SearchErrors.cs:173` |
| B7 | Outages returned 500: `storage.unavailable`, `search.unreachable`, `search.timeout`, `messaging.unavailable` were `ErrorType.Unexpected`; no 503/504 existed. | `WebApi/Errors/ErrorTypeStatusCodeMap.cs:28` |
| B8 | `AddSharedKernelOpenApi` called `services.BuildServiceProvider()` (ASP0000) at registration time, before any minimal-API endpoint was mapped. | `WebApi/OpenApi/OpenApiExtensions.cs:71` |
| B9 | `RequireValidatedUpload(maxSizeBytes)` could not lift the 1 MB payload limit (the filter never set the request's size limit); multipart uploads either failed the content-type allow-list (`multipart/form-data`) or skipped it. | `WebApi/Uploads/UploadValidationEndpointFilter.cs:72,77` |
| B10 | `Deprecation: true` is the pre-RFC draft format; RFC 9745 requires a structured-field date (`@1688169599`). | `WebApi/Versioning/ApiVersionLifecycleMiddleware.cs:86` |
| B11 | gRPC dropped the error code and every field error but the first (the defect P-402 fixed for HTTP); the gRPC correlation interceptor re-read the raw header and overwrote the HTTP middleware's validated baggage value. | `Grpc/Interceptors/GrpcExceptionInterceptor.cs:133`, `GrpcCorrelationInterceptor.cs:99` |
| B12 | Tenantless SignalR connections and gRPC calls stored `Guid.Empty`; with `HubGroupNaming.TenantGroup` they shared one `tenant:00000000-…` group. | `SignalR/Filters/TenantContextHubFilter.cs:36`, `GrpcTenantContextInterceptor.cs:90` |
| B13 | `RowVersionETag` took `byte[]`, while the persistence stack versions aggregates with the opaque `EntityVersion` (xmin); there was no 428 path. | `WebApi/Concurrency/RowVersionETag.cs:27`, `samples/BillingApi/Api/BillingEndpoints.cs:95` |
| B14 | Invented error codes (`Authorization.Forbidden`, `Idempotency.KeyRequired`, `error.unexpected`) instead of the platform's `ErrorCodes`; 403 messages listed the exact roles/permissions required. | `AuthorizationRequirementEndpointFilter.cs:59,105,116`, `IdempotencyKeyRequirementEndpointFilter.cs:34`, `SharedKernelExceptionHandler.cs:49` |
| B15 | Every error's `type` pointed at `https://httpstatuses.io/…` (a third-party site); `title` carried the error code instead of a human-readable summary (RFC 9457 §3.1.3). | `WebApi/Http/ProblemDetailsShaping.cs:25`, `ErrorProblemDetailsExtensions.cs:51` |
| B16 | The SignalR rate limiter created a `TokenBucketRateLimiter` with auto-replenishment (one timer) per connection. | `SignalR/Filters/HubInvocationRateLimitFilter.cs:132` |

Also noted: every handled exception was logged at Error (including 4xx `SharedKernelException`s); client
disconnects were logged and answered as 500; CORS invoked `configure` twice, could not bind from configuration and
exposed no response headers; the security-headers middleware sent HSTS over plain HTTP and to localhost.

## R — Remove or replace (owner-approved)

| ID | Item | Why | Replacement |
| --- | --- | --- | --- |
| R1 | Custom authorization (4 attributes, endpoint filter, gRPC interceptor, 2 registrations; ~700 lines) | Parallel system to ASP.NET Core authorization; B1, B2, B14; no SignalR support; forced `.Grpc` → `.WebApi` | Native policies evaluated against `IUserContext` (D3) |
| R2 | Version-lifecycle middleware, options and startup filter (187 lines) | Asp.Versioning 10 has sunset/deprecation policies emitting RFC 9745/8594 headers; B10 | Asp.Versioning policies (D11) |
| R3 | Per-version OpenAPI registration via `BuildServiceProvider`; `MutualTlsSecurityScheme` subclass | B8; `Asp.Versioning.OpenApi` 10.x provides `WithDocumentPerVersion()`; the resolved Microsoft.OpenApi 2.7.5 has `SecuritySchemeType.MutualTLS` | D11 |
| R4 | Upload validation (318 lines) | Unused; B9; files go direct to storage via 08.Storage presigned uploads | Removed |
| R5 | Payload-limit middleware | Kestrel's limit plus per-endpoint `IRequestSizeLimitMetadata`, enforced by routing since .NET 8 | Options + `.WithRequestSizeLimit()` (D6) |
| R6 | Security-headers middleware (5 option classes + `CspBuilder`, 306 lines) | Re-implemented HSTS worse than `UseHsts()` | Built-in HSTS + one small middleware (D6) |
| R7 | Idempotency attribute + filter + `TryGetIdempotencyKey` + registration (230 lines) | Four pieces for "this header is required", plus manual filter wiring | One declaration (D9) |
| R8 | Public `RateLimitRejectionProblemDetails` | Every service had to hand-write `OnRejected` | Automatic (D8) |
| R9 | SignalR: CORS startup diagnostic, `WithRedisBackplane` alias (forced a Redis dependency), four `HubOptions` pins equal to SignalR's defaults, argument validators on the rate-limit options | Noise or pass-through | Removed (D12) |
| R10 | gRPC: correlation, tenant and authorization interceptors; 4 MiB pin (= gRPC default) | Duplicated the HTTP pipeline, native `[Authorize]`, or defaults; tenant `UserState` had no reader | Removed (D13) |
| R11 | CORS wrapper | Double `configure`, no configuration binding, no exposed headers | Config-bound options (D7) |

## F — Additions (owner-approved)

| ID | Addition | Design |
| --- | --- | --- |
| F1 | One-call setup (`AddSharedKernelWebApi()`/`UseSharedKernelWebApi()`), configuration-bound, validated at startup | D4 |
| F2 | Typed results for `Result`/`Result<T>` (`Results<Ok<T>, ErrorHttpResult>` …) with `Task` overloads | D2 |
| F3 | One error pipeline through `IProblemDetailsService` for every error source | D1 |
| F4 | OpenAPI: ProblemDetails schema and default error response, per-operation security, documented required headers, docs gated to Development | D11 |
| F5 | `ErrorType.Unavailable`/`Timeout` → 503 (+ optional `Retry-After`) / 504; gRPC `Unavailable`/`DeadlineExceeded` | D14 |
| F6 | Conditional requests on `EntityVersion`: ETag + 304, required `If-Match` (428), stale version → 412 | D10 |
| F7 | Typed accessors (`GetCorrelationId()`, `GetIdempotencyKey()`, `GetIfMatch()`, `GetTenantId()`) | D5, D9, D10, D12 |
| F8 | Correct logging (5xx Error, 4xx Debug, client abort not a 500) and secure defaults (no `Server` header, strict CSP on API responses) | D1, D6 |
| F9 | gRPC rich status (`ErrorInfo` + all `BadRequest` field violations); SignalR errors carry the code; `Result`-returning hub methods | D12, D13 |
| F10 | Public API tracking, one-shape tests, consumer-verify and samples on the one-call path, README rewrite, package bumps | D16 |

Not adopted: .NET 10 `AddValidation()` (DataAnnotations-only, key parts experimental ASP0029, no FluentValidation
hook; the platform validates in the MediatR pipeline).
