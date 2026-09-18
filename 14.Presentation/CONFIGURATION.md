# 14.Presentation — Configuration Reference

Every DI extension method exposed by `SharedKernel.Presentation.WebApi` and
`SharedKernel.Presentation.SignalR`, its options, and its platform-default values. See each
package's `README.md` for usage examples; see `CLAUDE.md` for the authoritative interface
contracts and implementation rules.

---

## `SharedKernel.Presentation.WebApi`

### `AddSharedKernelCorrelationId(this IServiceCollection, Action<CorrelationIdOptions>? configure = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configure` | no | `null` | Customises `CorrelationIdOptions`. Omit to use the documented default `MaxLength`/`AllowedCharacterPattern` — every well-formed value already in production use (GUIDs, ULIDs) continues to pass unchanged. |

| `CorrelationIdOptions` member | Default | Purpose |
| --- | --- | --- |
| `MaxLength` | `128` | Maximum accepted length, in characters, for a caller-supplied `X-Correlation-Id` header value |
| `AllowedCharacterPattern` | alphanumerics plus `-` `_` `:` `.` | A safe-but-permissive allowlist covering GUID/ULID/general safe-token shapes |

A host that never calls this method still gets the default-safe validation applied automatically —
`CorrelationIdMiddleware` falls back to a fresh default `CorrelationIdOptions` instance when none is
registered in DI, so `UseSharedKernelCorrelationId()` alone is never a broken/unvalidated
configuration.

### `UseSharedKernelCorrelationId(this IApplicationBuilder)`

No options. Must be the **first** call in the pipeline — before `UseExceptionHandler` — so the
`X-Correlation-Id` response header is set even on error responses.

**Format-validation behavior:** a caller-supplied header value exceeding `MaxLength` or containing a
character outside `AllowedCharacterPattern` is rejected exactly like an absent/whitespace header —
regenerated via a fresh `Guid.NewGuid("N")`, **before** the rejected value ever reaches
`HttpContext.Items`, `Activity.SetBaggage`, or the response header. Only the rejected value's
*length* is logged (`EventId` 14006) — never its raw content, which would recreate the exact
log-injection vector this validation defends against.

| Constant | Value | Purpose |
| --- | --- | --- |
| `CorrelationIdMiddleware.HeaderName` | `"X-Correlation-Id"` | Request/response header name |
| `CorrelationIdMiddleware.ItemsKey` | `"CorrelationId"` | `HttpContext.Items` storage key |
| `CorrelationIdMiddleware.BaggageKey` | `"correlation.id"` | `Activity` baggage key (not a tag — survives process boundaries) |

Behavior: reads the inbound header; if absent or whitespace, generates `Guid.NewGuid("N")`.
Always writes the resolved value back as a response header via `Response.OnStarting`, which fires
even when downstream middleware short-circuits the pipeline.

### `AddSharedKernelApiVersioning(this IServiceCollection)`

No parameters — wraps `Asp.Versioning`'s `AddApiVersioning` + `AddApiExplorer` with fixed platform
defaults. There is no override parameter on this method itself; to deviate, call
`services.AddApiVersioning(...)` directly instead (i.e. don't call this extension).

| Option | Value | Source |
| --- | --- | --- |
| `DefaultApiVersion` | `1.0` | `SharedKernelApiVersioningDefaults.DefaultApiVersion` |
| `ApiVersionReader` | URL segment + `X-Api-Version` header, combined | `SharedKernelApiVersioningDefaults.ApiVersionReader` |
| `AssumeDefaultVersionWhenUnspecified` | `true` | non-negotiable platform default — unversioned requests never 400 |
| `ReportApiVersions` | `true` | adds `api-supported-versions` / `api-deprecated-versions` response headers |
| `GroupNameFormat` (ApiExplorer) | `"'v'VVV"` | must match the document grouping `AddSharedKernelOpenApi` consumes |
| `SubstituteApiVersionInUrl` (ApiExplorer) | `true` | required for the `{version}` URL-segment template to resolve in generated docs |

**Ordering requirement:** call this before `AddSharedKernelOpenApi` — the OpenAPI extension reads
`IApiVersionDescriptionProvider` to discover version groups.

### `AddSharedKernelApiVersioning(..., Action<ApiVersionLifecycleOptions>? configureLifecycle = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configureLifecycle` | no | `null` | Additive parameter on the same method above — declares a per-`ApiVersion` optional sunset date and successor link (RFC 8594). Omitting it produces zero new response headers. |

```csharp
builder.Services.AddSharedKernelApiVersioning(configureLifecycle: o =>
{
    o.Configure(new ApiVersion(2, 0),
        sunsetDate: new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
        successor: new Uri("https://api.example.com/v3/orders"));
});
```

| Response header | When set | Source |
| --- | --- | --- |
| `Sunset` | The resolved API version has a declared `SunsetDate` | This registry — an RFC 7231 HTTP-date (`.ToString("R")`), never an arbitrary string |
| `Deprecation` | The resolved API version is in Asp.Versioning's own `DeprecatedApiVersions` | Asp.Versioning's existing `Deprecated`/`HasDeprecatedApiVersion(...)` declaration — never a second, parallel flag on this registry |
| `Link: rel="successor-version"` | A successor `Uri` is declared **alongside** a sunset date | This registry |

Additive to, never a rework of, the existing `ReportApiVersions` header family
(`api-supported-versions`/`api-deprecated-versions`) — both may appear on the same response
simultaneously. **Deliberately not built on `Asp.Versioning.Http`'s own `Policies.Sunset(...)`/
`SunsetPolicyManager`** — a real round trip showed that surface never fires its headers for an
empty/unnamed policy name; this is an independent, simpler registry instead.

### `AddSharedKernelOpenApi(this IServiceCollection, string title, string? description = null, Action<OpenApiSecuritySchemesOptions>? configureSecuritySchemes = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `title` | yes | — | `OpenApiInfo.Title` in every registered document |
| `description` | no | `null` | `OpenApiInfo.Description` in every registered document |
| `configureSecuritySchemes` | no | `null` | Additive parameter — omitting it preserves the original unconditional Bearer-only document shape |

Behavior: registers one native OpenAPI document per API-version group discovered via
`IApiVersionDescriptionProvider` (when `AddSharedKernelApiVersioning` was called first); otherwise
registers a single fallback document named `"v1"`. Every document gets a transformer that sets
`Info.Title`/`Info.Description`/`Info.Version` (the document/group name) and registers a `Bearer`
HTTP security scheme by name — metadata only, no token validation (that stays in
`12.Security.Oidc`).

| `OpenApiSecuritySchemesOptions` member | Default | Effect |
| --- | --- | --- |
| `Bearer` | `true` | Unchanged default — existing Bearer-only consumers see zero behavior change |
| `ApiKey` | `false` | Registers an `ApiKey`-type scheme named `ApiKey`, `in: header` |
| `ApiKeyHeaderName` | `"X-Api-Key"` | The header name documented on the `ApiKey` scheme — a plain configurable string; this package never takes a `ProjectReference` on `SharedKernel.Security.ApiKey` to reuse its header-name constant |
| `MutualTls` | `false` | Registers an OpenAPI 3.1 `mutualTLS`-typed scheme |

**Each active scheme is registered as its own separate security requirement object — OR semantics**
(a request satisfies the document by presenting *any one* active mechanism), the opposite of this
package's `[RequireRole]`/`[RequirePermission]` AND-across-attributes composition rule. See the
WebApi `README.md`'s "OpenAPI security schemes" section for worked Bearer+ApiKey/Bearer+mTLS
examples and the Scalar-UI manual-verification record (T-49).

### `MapSharedKernelOpenApi(this WebApplication app)`

No options. Must be called on a `WebApplication` (not `IApplicationBuilder`) — this is a native
`Microsoft.AspNetCore.OpenApi` requirement, not a platform restriction. Maps:

- `/openapi/{documentName}.json` for every registered document (`{documentName}` is the version
  group name, e.g. `v1`, or the `"v1"` fallback when versioning isn't configured).
- `/scalar/{documentName}` via `MapScalarApiReference`, listing every discovered document.

No Swagger UI route is ever mapped.

### `AddSharedKernelAuthorizationFilters(this IServiceCollection)`

No options. Registers `AuthorizationRequirementEndpointFilter` as a **singleton** — mirrors the
`TenantContextHubFilter`/`HubExceptionMappingFilter` DI-registration convention in
`SharedKernel.Presentation.SignalR`, so the filter can take constructor dependencies later even
though it is stateless today.

**This call alone does not attach the filter to any endpoint.** Unlike
`AddSharedKernelSignalR`'s global hub-filter registration via `HubOptions.AddFilter<T>()`, ASP.NET
Core's minimal-API/MVC endpoint routing exposes no "apply to every mapped endpoint automatically"
hook this package can use. You must additionally call
`.AddEndpointFilter<AuthorizationRequirementEndpointFilter>()` on `MapControllers()` and/or each
minimal-API route group — see the WebApi `README.md`'s "Declarative role/permission authorization"
section for both wiring forms. Omitting this step leaves every `[RequireRole]`/`[RequirePermission]`
attribute inert (present as metadata, never evaluated) — the endpoint stays fully open.

| Type | Ctor | Composition | Failure response |
| --- | --- | --- | --- |
| `RequireRoleAttribute` | `params string[] roles` | roles within one instance OR'd; stacked instances AND'd | `Error.Forbidden(...).ToProblemDetails()` (403) |
| `RequirePermissionAttribute` | `params string[] permissions` | permissions within one instance OR'd; stacked instances AND'd | `Error.Forbidden(...).ToProblemDetails()` (403) |

Evaluated against `IUserContext.HasRole`/`HasPermission` (`12.Security.Abstractions`) — never
`ClaimTypes.Role` and never the built-in `[Authorize(Roles = "...")]`, which bypasses this
platform's claim-mapping-aware role/permission resolution. An endpoint carrying neither attribute
passes through the filter as a no-op — safe to register the filter on every route.

### `SharedKernelExceptionHandler` (registered manually, not via an extension method)

```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
```

No options — constructor-injects `ILogger<SharedKernelExceptionHandler>` and `IHostEnvironment`
from DI. Behavior is environment-gated, not configuration-gated:

| Environment | `Detail` on unknown exceptions |
| --- | --- |
| `IHostEnvironment.IsDevelopment() == true` | Full exception message |
| Otherwise | `"An unexpected error occurred."` (constant, never the real message) |

Known `SharedKernelException` subtypes always surface their carried `Error`'s message and status
code regardless of environment — that detail is intentional/curated by the throwing code, not
leaked internals.

A `ValidationException` is handled through a dedicated branch — checked before the generic
`SharedKernelException.Error` fallback — that routes through `ValidationProblemDetailsExtensions`
(below) instead of the single-`Error` path, so every failing field is preserved in the response, not
just the first.

### `ValidationProblemDetailsExtensions.ToProblemDetails(this ValidationException, HttpContext? context = null)`

Not a DI extension — a pure static mapping method, consumed automatically by
`SharedKernelExceptionHandler` once it and `AddProblemDetails()`/`AddExceptionHandler<...>()` are
registered. No options. Groups `ValidationException.Errors` by `Error.Code` into
`Extensions["errors"]` (`Dictionary<string, string[]>`) — every other field of the produced
`ProblemDetails` (`Title`/`Detail`/`Status`/`Type`/`Extensions["errorCode"]`/`Extensions["traceId"]`)
resolves identically to the single-`Error` path applied to the exception's first error.

### `Error.ToProblemDetails()` optional localization (P-484/WO-078 — no DI extension method)

Unlike every other opt-in capability in this domain, this one activates **automatically** once a
consuming service registers `SharedKernel.Localization`'s `ILocalizationCatalog` — there is no
`AddSharedKernelXxx()` call to make. `ErrorProblemDetailsExtensions.ToProblemDetails(this Error,
HttpContext? context = null)` resolves `ILocalizationCatalog` via `context?.RequestServices
.GetService<ILocalizationCatalog>()` (never `GetRequiredService`) and, when a translation exists
for `(error.Code, CultureInfo.CurrentUICulture)`, fills it with `Error.MessageArguments` and uses it as `Detail` instead of `Error.Message`, through `catalog.Localize(error, CultureInfo.CurrentUICulture)`. Errors built from a `LocalizedMessage` definition (`OrderMessages.NotFound.ToError(ErrorType.NotFound, orderId)`) carry those arguments; see `SharedKernel.Localization`'s README.

```csharp
// A service that wants localized ProblemDetails.Detail values registers a catalog — nothing else:
builder.Services.AddLocalizationCatalog(catalog =>
    catalog.AddJsonDirectory(Path.Combine(AppContext.BaseDirectory, "Localization")));
// Localization/tr.json: { "order.not_found": "{orderId} numaralı sipariş bulunamadı." }

// A service that never registers ILocalizationCatalog sees byte-identical output to before P-484.
```

| Condition | `ProblemDetails.Detail` |
| --- | --- |
| No `ILocalizationCatalog` registered | `Error.Message` (unchanged pre-P-484 behavior) |
| Catalog registered, no entry for `(error.Code, CurrentUICulture)` | `Error.Message` (fallback — never blank) |
| Catalog registered, entry found, but it uses a placeholder the error has no value for | `Error.Message` (never a raw `{placeholder}`) |
| Catalog registered, entry found | The translation, with its placeholders filled from `Error.MessageArguments` in `CurrentUICulture` |

`CultureInfo.CurrentUICulture` is read as an ambient value only — this package never resolves or
sets culture itself; that is `13.ServiceDefaults`'s `AddSharedKernelLocalization()` middleware's
job (P-483) when a consuming service opts in. `Title`/`Status`/`Type`/`Extensions["errorCode"]`/
`Extensions["traceId"]` are never affected. The multi-field `ValidationProblemDetailsExtensions`
path (above) applies this same localization/fallback independently per failing field's
`Error.Code` — one field may translate while a sibling falls back in the same response body.

### `UseSharedKernelSecurityHeaders(this IApplicationBuilder, Action<SecurityHeadersOptions>? configure = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configure` | no | `null` | Customises `SecurityHeadersOptions`. Omit to use every documented default value. |

**Ordering requirement:** register immediately after `UseSharedKernelCorrelationId()` and before
`UseExceptionHandler()`/error-handling middleware.

| `SecurityHeadersOptions` member | Header | Default value | Enabled by default? |
| --- | --- | --- | --- |
| `Hsts` | `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` (365 days, `IncludeSubDomains = true`, `Preload = false`) | **Yes — opt-OUT, unlike every other header below.** See the CAPITALIZED local-dev warning in the WebApi `README.md`. |
| `ContentTypeOptions` | `X-Content-Type-Options` | `nosniff` | Yes |
| `FrameOptions` | `X-Frame-Options` | `DENY` | Yes |
| `ReferrerPolicy` | `Referrer-Policy` | `strict-origin-when-cross-origin` | Yes |
| `PermissionsPolicy` | `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` | Yes |
| `WithContentSecurityPolicy(...)` | `Content-Security-Policy` | *(no header set)* | No — only set via an explicit call; there is no default CSP value |

Every header assignment is guarded by `Response.Headers.ContainsKey(...)` — an inner
middleware/endpoint's more-specific header value always wins; this middleware never overwrites an
already-set header.

### `AddSharedKernelCors(this IServiceCollection, Action<CorsPolicyOptions> configure)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configure` | yes | — | Configures `CorsPolicyOptions.AllowedOrigins`/`AllowCredentials`/`AllowedMethods`/`AllowedHeaders` for the single named policy (`CorsPolicyNames.Default`) this method registers |

Deny-by-default: an empty `AllowedOrigins` permits no cross-origin requests. An empty
`AllowedMethods`/`AllowedHeaders` allows any method/header (ASP.NET Core CORS's own default), since
those two are rarely the security-sensitive knob — `AllowedOrigins` combined with
`AllowCredentials` is.

Also registers `CorsPolicyOptions` through the standard `Microsoft.Extensions.Options` pipeline with
`ValidateOnStart()`, so the fail-fast guard below runs — and fails — at `IHost.StartAsync()`:

| Combination | Result |
| --- | --- |
| `AllowCredentials = true` **and** `AllowedOrigins` empty or contains `"*"` | Startup throws with a clear, actionable message — never deferred to the first real credentialed cross-origin request |
| `AllowCredentials = true` **and** `AllowedOrigins` is one or more explicit origins | Succeeds |

Apply the registered policy with `app.UseCors(CorsPolicyNames.Default)` — reference the constant,
never re-type the policy-name literal.

### `AddSharedKernelIdempotencyFilters(this IServiceCollection)`

No options. Registers `IdempotencyKeyRequirementEndpointFilter` as a **singleton** — mirrors
`AddSharedKernelAuthorizationFilters`'s exact registration shape, including the same caveat: **this
call alone does not attach the filter to any endpoint.** You must additionally call
`.AddEndpointFilter<IdempotencyKeyRequirementEndpointFilter>()` on `MapControllers()` and/or each
minimal-API route group — see the WebApi `README.md`'s "Inbound idempotency-key HTTP boundary"
section for the full end-to-end recipe. Omitting this step leaves `[RequireIdempotencyKey]` inert.

| Constant | Value | Purpose |
| --- | --- | --- |
| `HttpContextIdempotencyExtensions.IdempotencyKeyHeader` | `"Idempotency-Key"` | Request header name (domain-local for now — see the type's XML docs) |
| `HttpContextIdempotencyExtensions.MaxIdempotencyKeyLength` | `256` | Maximum accepted key length, in characters |

A missing/malformed key on an endpoint carrying `[RequireIdempotencyKey]` short-circuits with
`Error.Validation(...).ToProblemDetails()` (400) via the ordinary single-`Error` path — never a new
`ErrorType`, never a bare exception.

### Step-up/fresh-authentication attributes (extend `AuthorizationRequirementEndpointFilter`)

`RequireFreshAuthenticationAttribute(int maxAgeSeconds)` and
`RequireAuthenticationMethodAttribute(params string[] methods)` are evaluated by the **same**
`AuthorizationRequirementEndpointFilter`/`AddSharedKernelAuthorizationFilters` registered above —
there is no second filter type and no second `.AddEndpointFilter<...>()` call. See the WebApi
`README.md`'s "Step-up / fresh-authentication gating" section for a worked example.

| Attribute | Evaluated against | Rejection |
| --- | --- | --- |
| `[RequireFreshAuthentication(maxAgeSeconds)]` | `IUserContext.IsAuthenticationFresherThan(TimeSpan, DateTimeOffset)` — `DateTimeOffset` supplied by `IClock` (`01.Core`), resolved lazily only when this attribute is present | `Error.Forbidden(...).ToProblemDetails()` (403) |
| `[RequireAuthenticationMethod(params string[] methods)]` | `IUserContext.WasAuthenticatedWith(string)` — OR across the supplied methods | `Error.Forbidden(...).ToProblemDetails()` (403) |

An endpoint carrying only `[RequireRole]`/`[RequirePermission]` never requires `IClock` to be
registered in the consumer's container — it is resolved only when `[RequireFreshAuthentication]` is
actually present on the evaluated endpoint.

### `RowVersionETag` / `ConditionalRequestExtensions` (no DI registration — pure static helpers)

No options, no registration. `RowVersionETag.From(byte[] rowVersion)` produces a well-formed, quoted
`ETag` header value; `HttpContext.TryValidateIfMatch(string currentETag, out ProblemDetails?)`
evaluates the inbound `If-Match` header, producing a 412 `ProblemDetails` on mismatch. See the WebApi
`README.md`'s "Optimistic concurrency — ETag / If-Match" section for the full GET → If-Match → 412 →
re-fetch recipe. Additive to, never a replacement for, `Error.Conflict`/409 — zero change to
`ErrorTypeStatusCodeMap`'s existing mapping.

### `RateLimitRejectionProblemDetails.Create(HttpContext context, TimeSpan? retryAfter = null)` (no DI registration — pure static helper)

No options, no registration. Call it directly from a consuming service's
`RateLimiterOptions.OnRejected` callback (wired via `13.ServiceDefaults`'s
`AddSharedKernelRateLimiting()` — referenced by name only, no `ProjectReference` either direction).
Produces a 429 `ProblemDetails`; when `retryAfter` is supplied, also sets a real `Retry-After`
response header (whole seconds), not just a body field.

### `AddSharedKernelPayloadLimits(this IServiceCollection, Action<PayloadLimitsOptions>? configure = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configure` | no | `null` | Customises `PayloadLimitsOptions` |

| `PayloadLimitsOptions` member | Default | Effect |
| --- | --- | --- |
| `MaxRequestBodySizeBytes` | `1_048_576` (1 MB) | Wired into `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` by `UseSharedKernelPayloadLimits` |
| `MaxJsonDepth` | `32` | Wired into both `Microsoft.AspNetCore.Http.Json.JsonOptions` (Minimal API) and `Microsoft.AspNetCore.Mvc.JsonOptions.JsonSerializerOptions` (MVC, no-ops gracefully when MVC isn't registered) |

Registration-time half. Fully opt-in — a host calling neither this method nor
`UseSharedKernelPayloadLimits` is byte-identical to before this capability existed.

### `UseSharedKernelPayloadLimits(this IApplicationBuilder, Action<PayloadLimitsOptions>? configure = null)`

Request-scoped middleware half. Sets
`context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize`, guarded by
`.IsReadOnly` (the feature throws once body reading has started or is unsupported by the current
server) rather than crashing the pipeline. A body-size violation surfaces as
`Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException`, mapped by
`SharedKernelExceptionHandler`'s new branch to a 413 `ProblemDetails` — see the WebApi `README.md`'s
"Payload size / JSON max-depth limits" section for the Kestrel connection-level-vs-pipeline caveat
(a `Content-Length`-declared oversized body never reaches this path; only a chunked body does).

### `AddSharedKernelUploadValidation(this IServiceCollection, Action<UploadValidationOptions> configure)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configure` | yes | — | Configures the service-wide `UploadValidationOptions` defaults |

| `UploadValidationOptions` member | Purpose |
| --- | --- |
| `MaxSizeBytes` | Service-wide default maximum upload size |
| `AllowedContentTypes` | Service-wide default allow-list (compared after stripping any `;`-delimited parameter, e.g. `charset=utf-8`) |
| `AllowedMagicBytes` | Optional `content-type → signature bytes` map for a deeper leading-byte check |

### `RequireValidatedUploadAttribute(long? maxSizeBytes = null, params string[] allowedContentTypes)` / `UploadValidationEndpointFilter`

Mirrors `RequireIdempotencyKeyAttribute`/`IdempotencyKeyRequirementEndpointFilter`'s exact
global-registration/no-op-when-absent shape — the filter is always registered, but performs zero
validation on an endpoint carrying no `[RequireValidatedUpload]`/`.RequireValidatedUpload(...)`
metadata. Per-endpoint `maxSizeBytes`/`allowedContentTypes` args, when supplied, take precedence over
the global `UploadValidationOptions` defaults. Rejects with 413 (size)/415 (content type)/400
(magic-byte mismatch) `ProblemDetails`, before the endpoint handler runs. See the WebApi
`README.md`'s "File / multipart upload validation" section for a worked KYC-document example.

> **THIS IS A BOUNDARY-SHAPE CHECK ONLY — VIRUS/MALWARE SCANNING IS NEVER PERFORMED.** Wire a real
> scanning pipeline separately for any upload that needs one.

---

## `SharedKernel.Presentation.SignalR`

### `AddSharedKernelSignalR(this IServiceCollection, Action<HubOptions>? configureHubOptions = null, Action<HubInvocationRateLimitOptions>? configureRateLimit = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configureHubOptions` | no | `null` | Invoked **after** the platform registers its three global filters — use it to remove any platform filter from `options.HubFilters`, add service-specific filters, or set other `HubOptions` (e.g. `MaximumReceiveMessageSize`) |
| `configureRateLimit` | no | `null` | Configures `HubInvocationRateLimitOptions`. Omitted/`null` still registers `HubInvocationRateLimitFilter` (so it can be enabled later with no redeploy of the registration itself), but every check defaults to disabled — a genuine no-op |

Always registers, as singletons, and as global filters via `HubOptions.AddFilter<T>()`:

| Filter | Registered as | Scope |
| --- | --- | --- |
| `TenantContextHubFilter` | Singleton + global hub filter | Connection-scoped (`OnConnectedAsync`) |
| `HubExceptionMappingFilter` | Singleton + global hub filter | Invocation-scoped (`InvokeMethodAsync`) |
| `HubInvocationRateLimitFilter` | Singleton + global hub filter | Invocation-scoped (`InvokeMethodAsync`), consulted before the target method body runs |

The filters do not depend on each other's execution order for their own logic, with one documented
exception: `HubExceptionMappingFilter` has a `catch (HubException) { throw; }` branch, checked
first, so a `HubException` thrown by `HubInvocationRateLimitFilter` (or any future filter) always
reaches the caller with its specific message intact instead of being re-wrapped into the generic
redacted one.

| `HubInvocationRateLimitOptions` member | Default | Effect |
| --- | --- | --- |
| `PermitLimit` | `null` (disabled) | Token-bucket permit count per `Window`, per connection — rate limiting is disabled until this is set |
| `Window` | `TimeSpan.FromSeconds(1)` | The replenishment window paired with `PermitLimit`; only meaningful once `PermitLimit` is set |
| `MaxStringArgumentLength` | `null` (disabled) | Rejects a hub-method `string` argument longer than this, before the method body executes |
| `ArgumentValidators` | empty | Composable additional argument-shape checks |

A rejected invocation throws a `HubException` with a specific, caller-safe message (e.g. `"Too many
requests. Please slow down."`) — additive to, and distinct from, `MaximumReceiveMessageSize` (which
caps the whole transport message, not an individual argument).

### `SignalRCorsStartupDiagnostic` (registered automatically — no separate DI call)

No options, no separate registration call — `AddSharedKernelSignalR` registers this
`IHostedService` unconditionally. On `IHostApplicationLifetime.ApplicationStarted`, it scans every
mapped endpoint and logs a `Warning` (`[LoggerMessage]`, `EventId` 14102) for any SignalR-hub-shaped
endpoint (identified via `Microsoft.AspNetCore.SignalR.HubMetadata`) lacking CORS metadata
(`Microsoft.AspNetCore.Cors.Infrastructure.ICorsMetadata` — e.g. an attached `.RequireCors(...)`
policy). Each hub's `/negotiate` companion endpoint is skipped to avoid a duplicate warning for the
same gap. This package never takes a `ProjectReference` on `SharedKernel.Presentation.WebApi` to
close the CORS gap directly — the check is diagnostic-only (a logged `Warning`, never a thrown
exception or a blocked startup). See the SignalR `README.md`'s "SignalR + CORS" section for the
worked hub-plus-CORS example, referencing `SharedKernel.Presentation.WebApi`'s
`CorsPolicyNames.Default` by name only.

Also sets four conservative, explicitly documented resource-exhaustion defaults on `HubOptions`,
applied **before** `configureHubOptions` runs (so every value below is fully overridable, raise or
lower, with no signature change):

| `HubOptions` member | Platform default | Type |
| --- | --- | --- |
| `MaximumReceiveMessageSize` | `32 * 1024` (32 KB) | `long?` |
| `MaximumParallelInvocationsPerClient` | `1` | `int` (non-nullable) |
| `ClientTimeoutInterval` | `TimeSpan.FromSeconds(30)` | `TimeSpan?` |
| `KeepAliveInterval` | `TimeSpan.FromSeconds(15)` | `TimeSpan?` |

These are pinned explicitly even where a value matches SignalR's own current framework default, so
the platform's posture stays documented and stable across future SignalR version bumps rather than
implicit. See the SignalR `README.md`'s "Resource-exhaustion defaults" section for the rationale
behind each value.

Returns the stock `ISignalRServerBuilder` from `Microsoft.AspNetCore.SignalR`'s own `AddSignalR` —
no custom wrapper type — so it composes with any other `ISignalRServerBuilder` extension,
including `WithRedisBackplane` below.

### `WithRedisBackplane(this ISignalRServerBuilder builder, string connectionString, Action<RedisOptions>? configure = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `connectionString` | yes | — | Passed directly to `StackExchange.Redis` via `AddStackExchangeRedis` |
| `configure` | no | `null` | Passed directly through to `AddStackExchangeRedis`'s own `RedisOptions` configuration callback (e.g. to set a channel prefix) |

Pure pass-through — no platform-added behavior, no shared `IConnectionMultiplexer` with
`02.Caching.Redis.Core`. Omitting this call keeps SignalR fully in-memory (correct for local dev
and single-replica deployments only — connections will not fan out across pods without it).

### `HubGroupNaming.TenantGroup(Guid tenantId)`

Not configurable — pure static formatter. Always returns `"tenant:{tenantId:D}"`. This is the
single source of truth for tenant-scoped group names; never format a group name string inline
elsewhere in a consuming service.

### Hub filter `Items` keys (for reading inside Hub methods)

| Key | Set by | Type | Notes |
| --- | --- | --- | --- |
| `TenantContextHubFilter.ItemsKey` (`"TenantId"`) | `TenantContextHubFilter.OnConnectedAsync` | `Guid` | `Guid.Empty` when no `ITenantProvider` resolves a tenant — filter never rejects the connection itself |

---

## `SharedKernel.Presentation.Grpc`

### `AddSharedKernelGrpc(this IServiceCollection, Action<GrpcServiceOptions>? configure = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configure` | no | `null` | Runs after the platform defaults below — always wins |

Registers `Grpc.AspNetCore`'s `AddGrpc(...)` plus four global server interceptors — applied to
**every** mapped gRPC service automatically via `GrpcServiceOptions.Interceptors.Add<T>()`, the
gRPC-native equivalent of `HubOptions.AddFilter<T>()`. Unlike HTTP's
`AuthorizationRequirementEndpointFilter` (which needs a per-route/group
`.AddEndpointFilter<T>()` call), this needs zero further per-service wiring.

Registration order (outermost → innermost): `GrpcExceptionInterceptor` →
`GrpcCorrelationInterceptor` → `GrpcTenantContextInterceptor` → `GrpcAuthorizationInterceptor` —
mirrors HTTP's conceptual pipeline ordering (exception handling outermost, authorization closest
to the handler).

| `GrpcServiceOptions` member | Platform default | Overridable via |
| --- | --- | --- |
| `MaxReceiveMessageSize` | `4 * 1024 * 1024` (4 MiB) | `configure` callback |

### `GrpcExceptionInterceptor` (registered by `AddSharedKernelGrpc`, no separate call)

No options — constructor-injects `ILogger<GrpcExceptionInterceptor>` and `IHostEnvironment` from
DI. Behavior is environment-gated, mirroring `SharedKernelExceptionHandler` exactly:

| Environment | Unknown-exception `Status.Detail` |
| --- | --- |
| `IHostEnvironment.IsDevelopment() == true` | Full exception message |
| Otherwise | `"An unexpected error occurred."` (constant) |

Known `SharedKernelException` subtypes always map through `GrpcStatusCodeMap` using the carried
`Error`'s message, regardless of environment. Overrides all four server interceptor methods
(`UnaryServerHandler`/`ClientStreamingServerHandler`/`ServerStreamingServerHandler`/
`DuplexStreamingServerHandler`) — every gRPC call shape is covered, not unary-only.

### `GrpcCorrelationInterceptor` (registered by `AddSharedKernelGrpc`, no separate call)

Not configurable. Reads the `X-Correlation-Id` gRPC metadata key (`GrpcCorrelationInterceptor
.MetadataKey`) — the same key `SharedKernel.Communication.Grpc`'s client-side
`CorrelationTracingInterceptor` writes — generating `Guid.NewGuid("N")` when absent or whitespace.
Stores the resolved value in `ServerCallContext.UserState["CorrelationId"]` and calls
`Activity.Current?.SetBaggage(WellKnownBaggageKeys.CorrelationId, value)`.

### `GrpcTenantContextInterceptor` (registered by `AddSharedKernelGrpc`, no separate call)

Not configurable. Resolves `ITenantProvider` (`12.Security.Abstractions`) from the call's
`HttpContext.RequestServices` and stores the resolved `TenantId` in
`ServerCallContext.UserState["TenantId"]`. Mirrors `TenantContextHubFilter`'s policy exactly:
`Guid.Empty` when no `ITenantProvider` resolves a tenant — never rejects the call itself.

### `GrpcAuthorizationInterceptor` (registered by `AddSharedKernelGrpc`, no separate call)

Reuses `SharedKernel.Presentation.WebApi.Authorization`'s four attributes **verbatim** — apply
them directly to a gRPC service implementation class or method, exactly as you would on an MVC
controller action:

| Attribute | Evaluated against | Rejection |
| --- | --- | --- |
| `RequireRoleAttribute` | `IUserContext.HasRole(string)` — roles within one instance OR'd; stacked instances AND'd | `Error.Forbidden(...)` → `StatusCode.PermissionDenied` |
| `RequirePermissionAttribute` | `IUserContext.HasPermission(string)` | `Error.Forbidden(...)` → `StatusCode.PermissionDenied` |
| `RequireFreshAuthenticationAttribute` | `IUserContext.IsAuthenticationFresherThan(TimeSpan, DateTimeOffset)` — `DateTimeOffset` supplied by `IClock`, resolved lazily only when this attribute is present | `Error.Forbidden(...)` → `StatusCode.PermissionDenied` |
| `RequireAuthenticationMethodAttribute` | `IUserContext.WasAuthenticatedWith(string)` — OR across the supplied methods | `Error.Forbidden(...)` → `StatusCode.PermissionDenied` |

An endpoint with none of the four attributes never resolves `IUserContext` at all — safe to
register unconditionally.

### `GrpcStatusCodeMap.Resolve(ErrorType)` / `GrpcResultExtensions.ToGrpcResult()` (no DI registration — pure static helpers)

See the package `README.md` for the full `ErrorType → StatusCode` table. `Result.ToGrpcResult()`
(non-generic) and `Result<T>.ToGrpcResult()` throw the mapped `RpcException` on failure; the
generic overload returns the unwrapped value on success. Never hand-construct
`new RpcException(new Status(...))` at a gRPC service-method call site.

### `ServerCallContext.UserState` keys (for reading inside gRPC service methods)

| Key | Set by | Type | Notes |
| --- | --- | --- | --- |
| `GrpcCorrelationInterceptor.ItemsKey` (`"CorrelationId"`) | `GrpcCorrelationInterceptor` | `string` | Always non-empty — generated when the inbound metadata key was absent |
| `GrpcTenantContextInterceptor.ItemsKey` (`"TenantId"`) | `GrpcTenantContextInterceptor` | `Guid` | `Guid.Empty` when no `ITenantProvider` resolves a tenant — interceptor never rejects the call itself |

---

## Cross-cutting notes

- None of the three packages requires a `ProjectReference` outside `01.Core`,
  `04.Contracts` (`.WebApi` only — never `.Grpc`, never `.SignalR`), `12.Security.Abstractions`,
  and (for `.Grpc` only, a deliberate intra-domain exception) `.WebApi` itself. All three are
  fully self-contained with respect to `13.ServiceDefaults` — the correlation-id baggage key is
  this domain's own contract (see CLAUDE.md P-192).
- No configuration option in any of the three packages accepts environment-variable-style string
  toggles; all configuration is via strongly-typed C# (`Action<TOptions>` callbacks), consistent with the
  rest of the platform's Options-pattern conventions.
