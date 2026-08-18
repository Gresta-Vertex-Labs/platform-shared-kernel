# SharedKernel.Presentation.WebApi

RFC 9457 `ProblemDetails` error mapping, a global `IExceptionHandler`, API versioning
(`Asp.Versioning`), native OpenAPI document generation + Scalar interactive UI, inbound
correlation-id middleware, and `Result<T>` → `IResult`/`ActionResult` HTTP-boundary extensions.

This package is **framework-glue, not business logic**: it converts outcomes your application
layer already produced (`Result<T>`, `Error`, exceptions) into HTTP responses. It never produces
those outcomes itself, and it never references `05.Application`, `06.Persistence`, `07.Messaging`,
or any other infrastructure layer.

---

## Installation

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.Presentation.WebApi" Version="x.y.z" />
</ItemGroup>
```

Brings in `Asp.Versioning.Http`, `Asp.Versioning.Mvc.ApiExplorer`, `Microsoft.AspNetCore.OpenApi`,
and `Scalar.AspNetCore` as transitive dependencies. Swashbuckle/NSwag are never pulled in — see
"Why no Swashbuckle/NSwag" in `14.Presentation/CLAUDE.md` for the AOT rationale.

---

## Minimal setup — ProblemDetails only

The smallest viable wire-up: every unhandled exception and every `Error` is surfaced to clients as
an RFC 9457 `ProblemDetails` body, with correlation IDs flowing end-to-end.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
builder.Services.AddSharedKernelCorrelationId();

var app = builder.Build();

// CorrelationId middleware must be first — before exception handling — so the header is present
// on error responses too.
app.UseSharedKernelCorrelationId();
app.UseExceptionHandler();

app.Run();
```

What this buys you:

- Any exception thrown from an endpoint/controller is caught by `SharedKernelExceptionHandler`,
  logged at `LogLevel.Error`, and converted to a `ProblemDetails` response. Known
  `SharedKernelException` subtypes (from `01.Core`) preserve their carried `Error`'s status code
  and message; unknown exceptions fall back to a generic 500 with the message suppressed outside
  `IHostEnvironment.IsDevelopment()`.
- Every request gets a stable `X-Correlation-Id` response header (generated if the client didn't
  send one), propagated into `Activity` baggage for OpenTelemetry and `11.Communication.Rest`'s
  outbound correlation handler.

---

## Full setup — versioning + OpenAPI + Scalar

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
builder.Services.AddSharedKernelCorrelationId();

// Versioning must be registered before OpenAPI — the OpenAPI extension reads
// IApiVersionDescriptionProvider to discover version groups.
builder.Services.AddSharedKernelApiVersioning();
builder.Services.AddSharedKernelOpenApi(title: "Orders API", description: "Order management endpoints.");

var app = builder.Build();

app.UseSharedKernelCorrelationId();
app.UseExceptionHandler();

app.MapSharedKernelOpenApi();   // maps /openapi/{version}.json and /scalar/{version}

app.Run();
```

With versioning enabled, clients can request a version via the URL segment
(`/v1/orders/{id}`) or the `X-Api-Version` header — both resolve to the same endpoint. Requests
that specify neither fall back to `DefaultApiVersion` (`1.0`) rather than failing outright.
Browse the generated docs at `/scalar/v1` (or `/scalar/{version}` per discovered group).

---

## `Result<T>` at the HTTP boundary

### Minimal API

```csharp
app.MapGet("/v{version:apiVersion}/orders/{id:guid}", async (
    Guid id,
    IOrderQueryService svc,
    CancellationToken ct) =>
{
    Result<OrderDto> result = await svc.GetByIdAsync(id, ct);

    // Default success shape: 200 OK wrapping the value.
    return result.ToProblemDetailsResult();
})
.WithApiVersionSet(apiVersionSet)
.MapToApiVersion(1.0);

// Or project the success value to a custom IResult:
app.MapGet("/v{version:apiVersion}/orders/{id:guid}/summary", async (
    Guid id,
    IOrderQueryService svc,
    CancellationToken ct) =>
{
    Result<OrderDto> result = await svc.GetByIdAsync(id, ct);
    return result.ToProblemDetailsResult(order => Results.Ok(order.ToSummary()));
});
```

A non-generic `Result` (no payload) maps to `204 No Content` on success.

### MVC controllers

```csharp
[ApiController]
[Route("v{version:apiVersion}/orders")]
[ApiVersion(1.0)]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderQueryService _svc;

    public OrdersController(IOrderQueryService svc) => _svc = svc;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken ct)
    {
        Result<OrderDto> result = await _svc.GetByIdAsync(id, ct);
        return result.ToActionResult();
    }
}
```

On every failure path, `ResultHttpExtensions` always routes through `Error.ToProblemDetails()` —
hand-rolled `if (result.IsSuccess) ... else ...` branching immediately before returning an HTTP
result type is a platform violation, not just a style preference.

> `ResultHttpExtensions` (HTTP error shape) and `SharedKernel.Contracts.Mapping.ResultEnvelopeExtensions`
> (wire DTO shape, `04.Contracts`) are not interchangeable. An endpoint may use either, or both —
> e.g. `Envelope<T>` for the success body and `ProblemDetails` for the failure body.

---

## Declarative role/permission authorization

`[RequireRole]`/`[RequirePermission]` let you gate an entire endpoint on the caller's roles or
permissions without hand-rolling an `if (!userContext.HasRole(...))` check in every handler. Both
attributes evaluate through `IUserContext.HasRole`/`HasPermission` (`12.Security.Abstractions`) —
the same claim-mapping-aware, case-insensitive resolution the rest of the platform uses.

> **This filter does not auto-attach to every mapped endpoint.** Unlike
> `AddSharedKernelSignalR`'s global hub filters (`HubOptions.AddFilter<T>()`, which every hub gets
> automatically), ASP.NET Core's minimal-API/MVC endpoint routing has **no equivalent
> "apply-to-every-route" hook** that this package can plug into. Registering
> `AddSharedKernelAuthorizationFilters()` in DI only makes `AuthorizationRequirementEndpointFilter`
> resolvable — it does **not** wire it into the pipeline. You must additionally call
> `.AddEndpointFilter<AuthorizationRequirementEndpointFilter>()` on `MapControllers()` and/or each
> minimal-API route group yourself. Skip this step and every `[RequireRole]`/`[RequirePermission]`
> attribute you write is silently inert — the attribute is present as endpoint metadata, but
> nothing ever reads it, so the endpoint remains fully open. This is the one place in this domain
> where "convention over configuration" cannot deliver a zero-wiring default.

### Minimal API endpoints

```csharp
builder.Services.AddSharedKernelAuthorizationFilters();

var app = builder.Build();

var admin = app.MapGroup("/v{version:apiVersion}/admin")
    .AddEndpointFilter<AuthorizationRequirementEndpointFilter>(); // <-- required, see caveat above

admin.MapGet("/reports", GetReportsAsync)
    .RequireRole("Admin");

// Composition: multiple attributes on the same endpoint are AND'd; multiple values within
// one attribute are OR'd.
admin.MapPost("/reports/{id:guid}/approve", ApproveReportAsync)
    .RequireRole("Admin", "Auditor")       // caller needs Admin OR Auditor ...
    .RequirePermission("reports:approve"); // ... AND the reports:approve permission
```

### MVC controller actions

```csharp
[ApiController]
[Route("v{version:apiVersion}/admin")]
[ApiVersion(1.0)]
public sealed class AdminController : ControllerBase
{
    [HttpGet("reports")]
    [RequireRole("Admin")]
    public async Task<ActionResult<IReadOnlyCollection<ReportDto>>> GetReports(CancellationToken ct)
        => Ok(await _reports.ListAsync(ct));
}
```

```csharp
builder.Services.AddSharedKernelAuthorizationFilters();

var app = builder.Build();

app.MapControllers()
    .AddEndpointFilter<AuthorizationRequirementEndpointFilter>(); // <-- required, see caveat above
```

### Composition rule

- Roles/permissions listed **within one** `[RequireRole]`/`[RequirePermission]` attribute are
  **OR'd** — the caller needs any one of them.
- **Stacking multiple** `[RequireRole]`/`[RequirePermission]` attributes on the same endpoint is
  **AND'd** — the caller must satisfy every attached attribute.
- This is a fixed rule, not a configurable mode.

### Never mix in `[Authorize(Roles = "...")]`

Never combine the built-in ASP.NET Core `[Authorize(Roles = "...")]` with `[RequireRole]`/
`[RequirePermission]` on different endpoints of the same service. The built-in attribute reads
`ClaimTypes.Role` directly off the `ClaimsPrincipal`, bypassing this platform's `ClaimMapping`-aware
`HasRole`/`HasPermission` resolution — exactly the claim-shape fragility fixed for short-name JWT
claims in `WO-057`/`P-366` (`12.Security.Oidc`). An endpoint guarded by `[Authorize(Roles=...)]`
can silently behave differently from one guarded by `[RequireRole(...)]` against the identical
token. Always use `[RequireRole]`/`[RequirePermission]`.

A failed check always returns a `ProblemDetails` body via `Error.ToProblemDetails()` (HTTP 403) —
never a bare, body-less 403 — so it is indistinguishable, from the API consumer's point of view,
from a `05.Application` `AuthorizationBehavior` rejection deeper in the pipeline. This filter is a
complement to that pipeline behavior, not a replacement for it: `[RequireRole]` gates the whole
endpoint regardless of which command/query it dispatches, while `IAuthorizeRequest` gates an
individual command/query regardless of which endpoint dispatched it.

---

## What you get out of the box

| Concern | Type |
| --- | --- |
| `ErrorType` → HTTP status code | `ErrorTypeStatusCodeMap.Resolve` |
| `Error` → `ProblemDetails` | `ErrorProblemDetailsExtensions.ToProblemDetails` |
| `Result<T>` → `IResult`/`ActionResult` | `ResultHttpExtensions` |
| Global unhandled-exception handling | `SharedKernelExceptionHandler` |
| API versioning defaults | `SharedKernelApiVersioningDefaults`, `AddSharedKernelApiVersioning` |
| OpenAPI + Scalar | `AddSharedKernelOpenApi`, `MapSharedKernelOpenApi` |
| Correlation ID propagation | `CorrelationIdMiddleware`, `AddSharedKernelCorrelationId`, `UseSharedKernelCorrelationId` |
| Declarative role/permission authorization | `RequireRoleAttribute`, `RequirePermissionAttribute`, `AuthorizationRequirementEndpointFilter`, `AddSharedKernelAuthorizationFilters` |

See the [Configuration Reference](../CONFIGURATION.md) for every DI extension method's options and
defaults.
