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

### Step-up / fresh-authentication gating

`[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` **extend the same**
`AuthorizationRequirementEndpointFilter` used by `[RequireRole]`/`[RequirePermission]` above — there
is no second filter type and no second `.AddEndpointFilter<...>()` registration call. They gate an
endpoint on *how recently* and *how* the caller authenticated, built on `12.Security`'s step-up
signals (`IUserContext.AuthTime`/`.AuthenticationMethods`, WO-058/P-375) — useful for a high-risk
action that should require the caller to have completed MFA recently, even if their session token
is otherwise still valid.

Worked example: confirming a payment requires the caller's authentication to be no older than five
minutes **and** to have used MFA or a one-time passcode:

```csharp
builder.Services.AddSharedKernelAuthorizationFilters();

var app = builder.Build();

var payments = app.MapGroup("/v{version:apiVersion}/payments")
    .AddEndpointFilter<AuthorizationRequirementEndpointFilter>();

payments.MapPost("/{id:guid}/confirm", ConfirmPaymentHandler)
    .RequireFreshAuthentication(maxAgeSeconds: 300)   // AuthTime within the last 5 minutes ...
    .RequireAuthenticationMethod("mfa", "otp");        // ... AND authenticated via MFA or OTP (OR'd)
```

The equivalent MVC form:

```csharp
[HttpPost("{id:guid}/confirm")]
[RequireFreshAuthentication(300)]
[RequireAuthenticationMethod("mfa", "otp")]
public async Task<ActionResult> ConfirmPayment(Guid id, CancellationToken ct)
    => (await _payments.ConfirmAsync(id, ct)).ToActionResult();
```

A caller whose session is 10 minutes old, or who only authenticated with a password, gets
`Error.Forbidden(...).ToProblemDetails()` (403) — the same rejection shape `[RequireRole]`/
`[RequirePermission]` already use, so this is one consistent error contract regardless of which
attribute rejected the request. `[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]`
compose AND-across with each other and with any `[RequireRole]`/`[RequirePermission]` stacked on the
same endpoint; `[RequireAuthenticationMethod]`'s own method list is OR-within, mirroring
`[RequireRole]`'s composition rule. `IClock` (`01.Core`) is resolved lazily — only when
`[RequireFreshAuthentication]` is actually present on the evaluated endpoint — so an endpoint using
only `[RequireRole]`/`[RequirePermission]` never requires `IClock` to be registered.

---

## Multi-field validation errors

A `ValidationException` (`01.Core`) carries *every* failing field's `Error`, not just one.
`SharedKernelExceptionHandler` routes it through `ValidationProblemDetailsExtensions` instead of the
single-`Error` path, grouping every failing field by `Error.Code` into `Extensions["errors"]` —
mirroring ASP.NET Core's own built-in `ValidationProblemDetails.Errors` shape so client tooling that
already understands that convention (form-binding libraries, generated SDKs) works unmodified. No
extra wiring is needed — this happens automatically once `SharedKernelExceptionHandler` is
registered.

A request that throws:

```csharp
throw new ValidationException(
[
    Error.Validation("Order.CustomerId", "CustomerId is required."),
    Error.Validation("Order.Lines", "At least one order line is required."),
    Error.Validation("Order.Lines", "Line quantity must be greater than zero."),
]);
```

produces a body naming all three failures, not just the first:

```json
{
  "type": "https://httpstatuses.io/400",
  "title": "Order.CustomerId",
  "status": 400,
  "detail": "CustomerId is required.",
  "errorCode": "Order.CustomerId",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {
    "Order.CustomerId": ["CustomerId is required."],
    "Order.Lines": ["At least one order line is required.", "Line quantity must be greater than zero."]
  }
}
```

`Title`/`Detail`/`Status`/`Type`/`Extensions["errorCode"]`/`Extensions["traceId"]` are identical to
what the single-`Error` path would already produce for the exception's first error — `errors` is
purely additive. Every other `SharedKernelException` subtype (`NotFoundException`,
`ConflictException`, etc.) continues through the unchanged single-`Error`
`ErrorProblemDetailsExtensions.ToProblemDetails(Error)` path — this multi-error shape is specific to
`ValidationException`, not a general precedent for other exception types.

---

## Security response headers

`UseSharedKernelSecurityHeaders()` writes a conservative default set of HTTP response security
headers on every response. Register it immediately after `UseSharedKernelCorrelationId()` and before
`UseExceptionHandler()`:

```csharp
var app = builder.Build();

app.UseSharedKernelCorrelationId();     // 1st — correlation id must be set even on error responses
app.UseSharedKernelSecurityHeaders();   // 2nd
app.UseExceptionHandler();              // 3rd
```

| Header | Default | Toggle-able? |
| --- | --- | --- |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` | Yes — `options.Hsts.Enabled` |
| `X-Content-Type-Options` | `nosniff` | Yes — `options.ContentTypeOptions.Enabled` |
| `X-Frame-Options` | `DENY` | Yes — `options.FrameOptions.Enabled` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Yes — `options.ReferrerPolicy.Enabled` |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` | Yes — `options.PermissionsPolicy.Enabled` |
| `Content-Security-Policy` | *(none)* | Only set via `WithContentSecurityPolicy(...)` — no default |

> **HSTS IS ENABLED BY DEFAULT — THIS WILL BREAK LOCAL HTTP-ONLY DEVELOPMENT.** A browser that
> caches a long-lived HSTS policy for a host refuses plain HTTP connections to that host until the
> policy expires. **DISABLE IT OR GIVE IT A SHORT `MaxAge` FOR LOCAL DEV:**
>
> ```csharp
> if (builder.Environment.IsDevelopment())
> {
>     app.UseSharedKernelSecurityHeaders(o => o.Hsts.Enabled = false);
> }
> else
> {
>     app.UseSharedKernelSecurityHeaders();
> }
> ```

`Content-Security-Policy` is never set by default — a wrong generic default could break this
package's own `MapSharedKernelOpenApi`/Scalar UI. Opt in explicitly:

```csharp
app.UseSharedKernelSecurityHeaders(o =>
    o.WithContentSecurityPolicy(csp => csp
        .AddDirective("default-src", "'self'")
        .AddDirective("script-src", "'self' 'unsafe-inline'")));
```

Every header assignment is guarded — an inner middleware/endpoint that already set a header keeps
its value; this middleware never overwrites.

---

## CORS

`AddSharedKernelCors` registers one named policy (`CorsPolicyNames.Default`) and makes the classic
`AllowCredentials` + wildcard/empty-origin misconfiguration **impossible to express** — it fails
fast at `IHost.StartAsync()` rather than at the first real credentialed cross-origin request.

Worked example — a browser-hosted SPA that calls this service's SignalR hub with credentials
(cookies/`Authorization` header) attached to the WebSocket/SSE negotiate request:

```csharp
builder.Services.AddSharedKernelCors(o =>
{
    o.AllowedOrigins.Add("https://app.example.com");   // explicit origin — never "*" with credentials
    o.AllowCredentials = true;                          // required for a credentialed SignalR connection
    o.AllowedHeaders.Add("Authorization");
});

var app = builder.Build();

app.UseCors(CorsPolicyNames.Default);   // before MapHub<T>()
app.MapHub<OrdersHub>("/hubs/orders");
```

On the client:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://api.example.com/hubs/orders", { withCredentials: true })
    .build();
```

Omitting `AllowedOrigins` (or leaving it wildcarded) while `AllowCredentials = true` throws at
startup instead of silently deploying a broken or insecure policy:

```text
CorsPolicyOptions.AllowCredentials cannot be combined with an empty or wildcard AllowedOrigins
list. Specify one or more explicit origins, or set AllowCredentials to false.
```

Environment-specific allowlists (dev/staging/prod origins) are a configuration concern for the
consuming service — read them from `IConfiguration` rather than hardcoding them.

---

## Inbound idempotency-key HTTP boundary

`[RequireIdempotencyKey]` guards an endpoint on the presence of a valid client-supplied
`Idempotency-Key` request header — the HTTP-boundary half that neither `05.Application`'s in-process
`IIdempotentRequest`/`IdempotentCommandBehavior` nor `11.Communication.Rest`'s outbound propagation
covers. The end-to-end recipe, header to dispatch:

```csharp
builder.Services.AddSharedKernelIdempotencyFilters();

var app = builder.Build();

var payments = app.MapGroup("/v{version:apiVersion}/payments")
    .AddEndpointFilter<IdempotencyKeyRequirementEndpointFilter>();

payments.MapPost("/", CreatePaymentHandler)
    .RequireIdempotencyKey();

// CreatePaymentHandler:
static async Task<IResult> CreatePaymentHandler(
    CreatePaymentRequest body,
    HttpContext httpContext,
    ISender sender,
    CancellationToken ct)
{
    // 1. The filter already guaranteed the header is present and well-formed — read it.
    httpContext.TryGetIdempotencyKey(out string? idempotencyKey);

    // 2. Construct the command carrying that key.
    var command = new CreatePaymentCommand(body.AccountId, body.Amount, IdempotencyKey: idempotencyKey!);

    // 3. Dispatch as normal — 05.Application's IdempotentCommandBehavior<TRequest,TResponse>
    //    short-circuits a duplicate submission of the same key without re-executing the handler.
    Result<PaymentDto> result = await sender.Send(command, ct);
    return result.ToProblemDetailsResult();
}
```

A request missing the header, or carrying only whitespace, is rejected by the filter itself — before
`CreatePaymentHandler` ever runs — with a 400 `ProblemDetails` body via the ordinary single-`Error`
path (`Error.Validation`). This package supplies extraction/validation and the guard filter only; it
never automatically binds the header value onto `IIdempotentRequest.IdempotencyKey` — that mapping
step is always the endpoint handler's own responsibility, as shown above.

`IdempotencyKeyRequirementEndpointFilter` is a deliberately **separate** filter from
`AuthorizationRequirementEndpointFilter` — idempotency-key presence is a request-shape concern, not
an authorization concern, and the two are never folded together.

---

## Optimistic concurrency — ETag / If-Match

`RowVersionETag`/`ConditionalRequestExtensions` bridge `06.Persistence`'s row-version optimistic
concurrency (`IHasConcurrency.RowVersion`, normally surfaced as `Error.Conflict`/409) to HTTP's own
standard conditional-request mechanism (RFC 9110 §13). **This is additive — never a replacement for
`Error.Conflict`.** A service may use either, both, or neither.

The end-to-end recipe:

```csharp
// 1. GET returns the resource's current state and its ETag.
app.MapGet("/v{version:apiVersion}/accounts/{id:guid}", async (
    Guid id, IAccountQueryService svc, HttpContext ctx, CancellationToken ct) =>
{
    var account = await svc.GetByIdAsync(id, ct);
    ctx.Response.Headers.ETag = RowVersionETag.From(account.RowVersion);
    return Results.Ok(account);
});

// 2. The client sends that ETag back as If-Match on its update.
// 3. A stale If-Match (someone else updated the resource first) short-circuits with 412 —
//    before the update is attempted — instead of racing to a 409 deeper in the write path.
app.MapPut("/v{version:apiVersion}/accounts/{id:guid}", async (
    Guid id, UpdateAccountRequest body, IAccountQueryService reads, HttpContext ctx, CancellationToken ct) =>
{
    var current = await reads.GetByIdAsync(id, ct);
    var currentETag = RowVersionETag.From(current.RowVersion);

    if (!ctx.TryValidateIfMatch(currentETag, out var problemDetails))
    {
        return Microsoft.AspNetCore.Http.Results.Problem(problemDetails!);   // 412 Precondition Failed
    }

    // 4. If-Match matched — proceed with the update. Error.Conflict/409 remains the write path's
    //    own defense against a race that slips in between the check above and the actual write.
    Result<AccountDto> result = await accountService.UpdateAsync(id, body, ct);
    return result.ToProblemDetailsResult();
});
```

On a 412, the client's correct recovery is to **re-fetch** the resource (step 1 again), pick up the
new `ETag`, and retry the update against the fresh state — the same recovery a client already
performs on a 409 `Error.Conflict` response. A 412 is constructed directly via a shared internal RFC
9457 shaping helper, deliberately **not** routed through `Error`/`ErrorType` — it is an
HTTP-protocol-native outcome that never originates as a domain failure, unlike `Error.Conflict`.

---

## Rate-limit rejection → ProblemDetails bridge

`RateLimitRejectionProblemDetails.Create` shapes a rate-limiter rejection into an RFC 9457 429
`ProblemDetails` body — closing the handoff `13.ServiceDefaults`'s `AddSharedKernelRateLimiting()`
leaves for a consuming service's own `RateLimiterOptions.OnRejected` callback (referenced by name
only; neither package takes a `ProjectReference` on the other):

```csharp
builder.AddSharedKernelRateLimiting(options =>
{
    options.OnRejected = async (context, ct) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? delay
            : (TimeSpan?)null;

        var problemDetails = RateLimitRejectionProblemDetails.Create(context.HttpContext, retryAfter);

        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, ct);
    };
});
```

When `retryAfter` is supplied, it is written as a real `Retry-After` HTTP response header (in whole
seconds) — not just a body field — so proxies and client SDKs that already understand `Retry-After`
work unmodified.

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
| Declarative step-up/fresh-authentication gating | `RequireFreshAuthenticationAttribute`, `RequireAuthenticationMethodAttribute` |
| Multi-field validation `ProblemDetails` | `ValidationProblemDetailsExtensions.ToProblemDetails` |
| Security response headers | `SecurityHeadersOptions`, `UseSharedKernelSecurityHeaders` |
| CORS policy convention | `CorsPolicyOptions`, `CorsPolicyNames`, `AddSharedKernelCors` |
| Inbound idempotency-key HTTP boundary | `HttpContextIdempotencyExtensions.TryGetIdempotencyKey`, `RequireIdempotencyKeyAttribute`, `IdempotencyKeyRequirementEndpointFilter`, `AddSharedKernelIdempotencyFilters` |
| ETag / `If-Match` conditional requests | `RowVersionETag`, `ConditionalRequestExtensions` |
| Rate-limit rejection → `ProblemDetails`/429 bridge | `RateLimitRejectionProblemDetails` |

See the [Configuration Reference](../CONFIGURATION.md) for every DI extension method's options and
defaults.
