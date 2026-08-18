# 14.Presentation — Configuration Reference

Every DI extension method exposed by `SharedKernel.Presentation.WebApi` and
`SharedKernel.Presentation.SignalR`, its options, and its platform-default values. See each
package's `README.md` for usage examples; see `CLAUDE.md` for the authoritative interface
contracts and implementation rules.

---

## `SharedKernel.Presentation.WebApi`

### `AddSharedKernelCorrelationId(this IServiceCollection)`

No options. Registers no services today — present so the DI registration shape mirrors
`UseSharedKernelCorrelationId` and stays stable if `CorrelationIdMiddleware` ever needs
constructor-injected configuration.

### `UseSharedKernelCorrelationId(this IApplicationBuilder)`

No options. Must be the **first** call in the pipeline — before `UseExceptionHandler` — so the
`X-Correlation-Id` response header is set even on error responses.

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

### `AddSharedKernelOpenApi(this IServiceCollection, string title, string? description = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `title` | yes | — | `OpenApiInfo.Title` in every registered document |
| `description` | no | `null` | `OpenApiInfo.Description` in every registered document |

Behavior: registers one native OpenAPI document per API-version group discovered via
`IApiVersionDescriptionProvider` (when `AddSharedKernelApiVersioning` was called first); otherwise
registers a single fallback document named `"v1"`. Every document gets a transformer that sets
`Info.Title`/`Info.Description`/`Info.Version` (the document/group name) and registers a `Bearer`
HTTP security scheme by name — metadata only, no token validation (that stays in
`12.Security.Oidc`).

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

---

## `SharedKernel.Presentation.SignalR`

### `AddSharedKernelSignalR(this IServiceCollection, Action<HubOptions>? configureHubOptions = null)`

| Parameter | Required | Default | Effect |
| --- | --- | --- | --- |
| `configureHubOptions` | no | `null` | Invoked **after** the platform registers its two global filters — use it to remove either platform filter from `options.HubFilters`, add service-specific filters, or set other `HubOptions` (e.g. `MaximumReceiveMessageSize`) |

Always registers, as singletons, and as global filters via `HubOptions.AddFilter<T>()`:

| Filter | Registered as | Scope |
| --- | --- | --- |
| `TenantContextHubFilter` | Singleton + global hub filter | Connection-scoped (`OnConnectedAsync`) |
| `HubExceptionMappingFilter` | Singleton + global hub filter | Invocation-scoped (`InvokeMethodAsync`) |

The two filters do not depend on each other's execution order — one attaches connection data, the
other wraps method invocations.

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

## Cross-cutting notes

- Neither package requires a `ProjectReference` outside `01.Core`, `04.Contracts` (WebApi only),
  and `12.Security.Abstractions`. Both are fully self-contained with respect to `13.ServiceDefaults`
  — the correlation-id baggage key is this domain's own contract (see CLAUDE.md P-192).
- No configuration option in either package accepts environment-variable-style string toggles;
  all configuration is via strongly-typed C# (`Action<TOptions>` callbacks), consistent with the
  rest of the platform's Options-pattern conventions.
