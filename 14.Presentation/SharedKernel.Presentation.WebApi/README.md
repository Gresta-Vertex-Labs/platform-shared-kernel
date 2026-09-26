# SharedKernel.Presentation.WebApi

RFC 9457 `ProblemDetails` error mapping, a global `IExceptionHandler`, API versioning
(`Asp.Versioning`), native OpenAPI document generation + Scalar interactive UI, declarative
endpoint authorization, and `Result<T>` → `IResult`/`ActionResult` HTTP-boundary extensions.

**Tier:** Host. It references `SharedKernel.Primitives`, `SharedKernel.Core`,
`SharedKernel.Localization`, `SharedKernel.Security.Abstractions` and
[`SharedKernel.Presentation.Core`](../SharedKernel.Presentation.Core/README.md) (the `[Require*]`
attributes and the `ErrorType` status maps).

This package is **framework-glue, not business logic**: it converts outcomes your application
layer already produced (`Result<T>`, `Error`, exceptions) into HTTP responses. It never produces
those outcomes itself — it references no mediator, pipeline, persistence or messaging package.

The inbound correlation id and the request's `IRequestContext` scope are **not** this package's:
they belong to `app.UseSharedKernelRequestContext()` in `SharedKernel.ServiceDefaults.Security`,
which reads, validates (at most 128 characters of `[A-Za-z0-9-_:.]`, otherwise a new id), echoes
and propagates `X-Correlation-Id`. Every example below starts the pipeline with it.

---

## Installation

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.Presentation.WebApi" />
  <PackageReference Include="SharedKernel.ServiceDefaults.Security" />
</ItemGroup>
```

Versions come from your single `SharedKernelVersion` (central package management); every
SharedKernel package ships with the same version. `WebApi` brings in `Asp.Versioning.Http`,
`Asp.Versioning.Mvc.ApiExplorer`, `Microsoft.AspNetCore.OpenApi`, and `Scalar.AspNetCore` as
transitive dependencies. Swashbuckle/NSwag are never pulled in.

---

## Minimal setup — ProblemDetails only

The smallest viable wire-up: every unhandled exception and every `Error` is surfaced to clients as
an RFC 9457 `ProblemDetails` body, with correlation IDs flowing end-to-end.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();   // SharedKernel.ServiceDefaults.Security
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();

var app = builder.Build();

// The request-context middleware must be first — before exception handling — so the correlation
// id is on error responses too.
app.UseSharedKernelRequestContext();
app.UseExceptionHandler();

app.Run();
```

What this buys you:

- Any exception thrown from an endpoint/controller is caught by `SharedKernelExceptionHandler`,
  logged at `LogLevel.Error`, and converted to a `ProblemDetails` response. Known
  `SharedKernelException` subtypes (from `SharedKernel.Core`) preserve their carried `Error`'s
  status code and message; unknown exceptions fall back to a generic 500 with the message
  suppressed outside `IHostEnvironment.IsDevelopment()`.
- Every request gets a stable `X-Correlation-Id` response header (generated if the client didn't
  send a valid one), carried on `IRequestContext.CorrelationId` and sent onward by
  `SharedKernel.Communication.Rest`/`.Grpc`, MassTransit and Temporal.

---

## Full setup — versioning + OpenAPI + Scalar

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();

// Versioning must be registered before OpenAPI — the OpenAPI extension reads
// IApiVersionDescriptionProvider to discover version groups.
builder.Services.AddSharedKernelApiVersioning();
builder.Services.AddSharedKernelOpenApi(title: "Orders API", description: "Order management endpoints.");

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelSecurityHeaders();
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

> Handlers return `Result`/`Result<T>`; `ResultHttpExtensions` is the only mapping to HTTP. By default a
> success writes the value itself as the response body (204 for a plain `Result`), and a failure writes
> RFC 9457 `ProblemDetails`. A
> calling service built on `SharedKernel.Communication.Rest` maps that response back to a `Result<T>`
> with `ReadResultAsync`.

---

## Declarative role/permission authorization

`[RequireRole]`/`[RequirePermission]` let you gate an entire endpoint on the caller's roles or
permissions without hand-rolling an `if (!userContext.HasRole(...))` check in every handler. Both
attributes evaluate through `IUserContext.HasRole`/`HasPermission` (`12.Security.Abstractions`) —
the same resolution the rest of the platform uses: the authentication package's `IUserContextMapper`
reads its configured claim types, and roles and permissions are compared ordinally (case-sensitive,
since OAuth scopes are case-sensitive).

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

The attributes live in `SharedKernel.Presentation.Core`, namespace
`SharedKernel.Presentation.Authorization`; the filter and the Minimal API sugar stay in this package
(`SharedKernel.Presentation.WebApi.Authorization`). `SharedKernel.Presentation.Grpc` evaluates the
same attributes on gRPC service methods.

```csharp
using SharedKernel.Presentation.Authorization;          // [RequireRole], [RequirePermission], ...
using SharedKernel.Presentation.WebApi.Authorization;   // AuthorizationRequirementEndpointFilter

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
the identity's role claim type directly off the `ClaimsPrincipal`, bypassing this platform's
`IUserContextMapper`-based `HasRole`/`HasPermission` resolution — each authentication package maps
roles and permissions from its own configured claim types, which a raw `ClaimsPrincipal` check does
not follow. An endpoint guarded by `[Authorize(Roles=...)]`
can silently behave differently from one guarded by `[RequireRole(...)]` against the identical
token. Always use `[RequireRole]`/`[RequirePermission]`.

A failed check always returns a `ProblemDetails` body via `Error.ToProblemDetails()` (HTTP 403) —
never a bare, body-less 403 — so it is indistinguishable, from the API consumer's point of view,
from a `SharedKernel.Application.Pipeline` `AuthorizationBehavior` rejection deeper in the pipeline. This filter is a
complement to that pipeline behavior, not a replacement for it: `[RequireRole]` gates the whole
endpoint regardless of which command/query it dispatches, while `IAuthorizeRequest` gates an
individual command/query regardless of which endpoint dispatched it.

### Step-up / fresh-authentication gating

`[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` **extend the same**
`AuthorizationRequirementEndpointFilter` used by `[RequireRole]`/`[RequirePermission]` above — there
is no second filter type and no second `.AddEndpointFilter<...>()` registration call. They gate an
endpoint on *how recently* and *how* the caller authenticated, built on `12.Security`'s step-up
signals (`IUserContext.AuthTime`/`.AuthenticationMethods`) — useful for a high-risk
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
single-`Error` path, adding two members with the same keys:

- `errors` — each key's messages, mirroring ASP.NET Core's own built-in
  `ValidationProblemDetails.Errors` shape so client tooling that already understands that
  convention (form-binding libraries, generated SDKs) works unmodified.
- `errorCodes` — the `Error.Code` of the same errors, in the same order: `errorCodes[key][i]` is the
  code of the error whose message is `errors[key][i]`. Branch on these, not on the messages, which
  may be translated.

An error's key is the field it refers to — the `ErrorArgumentNames.PropertyPath` entry in its
`MessageArguments`, such as `Accounts[0].Iban` — when present, and its code otherwise.
`SharedKernel.Application.Pipeline`'s `ValidationBehavior` keeps the field path every `IRequestValidator<T>` reports (the FluentValidation bridge, `AddFluentValidationRequestValidators()`, sets it on every failure), so a
command validated there is reported by field. No extra wiring is needed — this happens
automatically once `SharedKernelExceptionHandler` is registered.

A request that throws:

```csharp
throw new ValidationException(
[
    Error.Validation("validation.required", "CustomerId is required.")
        with { MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "CustomerId" } },
    Error.Validation("order.lines_required", "At least one order line is required."),
    Error.Validation("validation.iban.invalid_check_digits", "IBAN check digits are not correct.")
        with { MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "Accounts[0].Iban" } },
]);
```

produces a body naming all three failures, not just the first:

```json
{
  "type": "https://httpstatuses.io/400",
  "title": "validation.required",
  "status": 400,
  "detail": "CustomerId is required.",
  "errorCode": "validation.required",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {
    "CustomerId": ["CustomerId is required."],
    "order.lines_required": ["At least one order line is required."],
    "Accounts[0].Iban": ["IBAN check digits are not correct."]
  },
  "errorCodes": {
    "CustomerId": ["validation.required"],
    "order.lines_required": ["order.lines_required"],
    "Accounts[0].Iban": ["validation.iban.invalid_check_digits"]
  }
}
```

`Title`/`Detail`/`Status`/`Type`/`Extensions["errorCode"]`/`Extensions["traceId"]` are identical to
what the single-`Error` path would already produce for the exception's first error — `errors` and
`errorCodes` are purely additive. Every other `SharedKernelException` subtype (`NotFoundException`,
`ConflictException`, etc.) continues through the unchanged single-`Error`
`ErrorProblemDetailsExtensions.ToProblemDetails(Error)` path — this multi-error shape is specific to
`ValidationException`, not a general precedent for other exception types.

The same two members are also produced on the `Result<T>`→HTTP path with no exception involved:
`ErrorProblemDetailsExtensions.ToProblemDetails(Error)` adds them whenever `Error.Details`
(`01.Core`) is non-empty — the case for an `Error.Validation(IReadOnlyList<Error>)` aggregate
returned as `Result.Failure(...)`, which is what `ValidationBehavior` returns. Both paths call the
same internal `LocalizedDetailResolver.AddErrorsExtensions` helper, so they produce identical
`errors` and `errorCodes` maps for the same underlying field errors.

---

## Security response headers

`UseSharedKernelSecurityHeaders()` writes a conservative default set of HTTP response security
headers on every response. Register it immediately after `UseSharedKernelRequestContext()` and before
`UseExceptionHandler()`:

```csharp
var app = builder.Build();

app.UseSharedKernelRequestContext();    // 1st — correlation id must be set even on error responses
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
`Idempotency-Key` request header (`WellKnownHeaders.IdempotencyKey`, the same name `SharedKernel.Communication.Rest` sends) — the HTTP-boundary half that neither `SharedKernel.Application.Pipeline`'s in-process
`IIdempotentRequest`/`IdempotencyBehavior` nor `11.Communication.Rest`'s outbound propagation
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

    // 3. Dispatch as normal — SharedKernel.Application.Pipeline's IdempotencyBehavior reserves the
    //    key atomically and never re-executes the handler for it: a completed duplicate replays the
    //    original response, one still in flight returns 409 (idempotency.in_progress), and the same
    //    key sent with a different payload returns 409 (idempotency.key_reused).
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

`RowVersionETag`/`ConditionalRequestExtensions` bridge `06.Persistence`'s optimistic concurrency (PostgreSQL `xmin`,
exposed as the opaque `EntityVersion` via `ConcurrencyVersion.Get(db, aggregate)`, normally surfaced as `Error.Conflict`/409) to HTTP's own
standard conditional-request mechanism (RFC 9110 §13). **This is additive — never a replacement for
`Error.Conflict`.** A service may use either, both, or neither.

The end-to-end recipe (the read model carries the aggregate's `EntityVersion Version`, taken from `ConcurrencyVersion.Get` when it was loaded):

```csharp
// 1. GET returns the resource's current state and its ETag.
app.MapGet("/v{version:apiVersion}/accounts/{id:guid}", async (
    Guid id, IAccountQueryService svc, HttpContext ctx, CancellationToken ct) =>
{
    var account = await svc.GetByIdAsync(id, ct);
    ctx.Response.Headers.ETag = RowVersionETag.From(BitConverter.GetBytes(account.Version.ToRowVersion()));
    return Results.Ok(account);
});

// 2. The client sends that ETag back as If-Match on its update.
// 3. A stale If-Match (someone else updated the resource first) short-circuits with 412 —
//    before the update is attempted — instead of racing to a 409 deeper in the write path.
app.MapPut("/v{version:apiVersion}/accounts/{id:guid}", async (
    Guid id, UpdateAccountRequest body, IAccountQueryService reads, HttpContext ctx, CancellationToken ct) =>
{
    var current = await reads.GetByIdAsync(id, ct);
    var currentETag = RowVersionETag.From(BitConverter.GetBytes(current.Version.ToRowVersion()));

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
`ProblemDetails` body — closing the handoff `SharedKernel.ServiceDefaults`' `AddSharedKernelRateLimiting()`
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

## Payload size / JSON max-depth limits

`AddSharedKernelPayloadLimits`/`UseSharedKernelPayloadLimits` close the one DoS vector none of the
WO-062 perimeter hardening (security headers, CORS, rate limiting) addresses: request **size and
shape**, as opposed to rate or origin. Both are fully opt-in — a host calling neither is
byte-identical to today.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Registration-time half: wires MaxJsonDepth into both Minimal API's and MVC's JsonOptions
// (the MVC half no-ops gracefully when MVC services are not registered).
builder.Services.AddSharedKernelPayloadLimits(o =>
{
    o.MaxRequestBodySizeBytes = 2 * 1024 * 1024;   // 2 MB (default: 1 MB)
    o.MaxJsonDepth = 32;                             // default: 32
});

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelSecurityHeaders();

// Request-scoped half: sets IHttpMaxRequestBodySizeFeature.MaxRequestBodySize, guarded by
// .IsReadOnly so it never throws on a server/test-host shape that can't set the feature.
app.UseSharedKernelPayloadLimits();

app.UseExceptionHandler();

app.Run();
```

A request body exceeding `MaxRequestBodySizeBytes` throws
`Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException` (a Kestrel-internal type
extending the public `Microsoft.AspNetCore.Http.BadHttpRequestException`), which
`SharedKernelExceptionHandler` maps to a 413 `ProblemDetails` — its already client-safe `Message`
(e.g. `"Request body too large. The max request body size is N bytes."`) is exempted from the
handler's usual dev-only detail-suppression gate.

> **Kestrel enforces body size at two different points, and only one of them reaches
> `ProblemDetails`.** A body sent with a declared `Content-Length` exceeding the limit is rejected
> by Kestrel **at the connection level**, before the ASP.NET Core middleware pipeline — and
> therefore `SharedKernelExceptionHandler` — ever runs, producing a bare 413 with an **empty body**.
> Only a **chunked-transfer-encoded** body (or any body Kestrel reads incrementally past the limit)
> throws `BadHttpRequestException` from inside the running pipeline, where the exception handler can
> catch it and shape a real `ProblemDetails` response. Do not assume every oversized request
> produces a `ProblemDetails` body — a client sending a declared `Content-Length` will see a plain,
> bodyless 413 instead. This was confirmed via a real listening Kestrel host, not `TestServer`
> (which does not enforce `IHttpMaxRequestBodySizeFeature` the same way).

A JSON payload exceeding `MaxJsonDepth` surfaces via STJ's own existing `JsonException` → 400 path —
this capability only wires the `MaxDepth` value into both hosting models' `JsonOptions`, it invents
no new depth-violation response shape.

---

## OpenAPI security schemes — Bearer, ApiKey, mTLS

`AddSharedKernelOpenApi`'s `configureSecuritySchemes` parameter registers additional OpenAPI
security schemes alongside the default `Bearer` scheme, so a service authenticating via
`SharedKernel.Security.ApiKey` or `.Mtls` gets an accurate OpenAPI document instead of a misleading
Bearer-only one. Omitting the parameter is byte-identical to before this capability existed.

**Bearer-only (default, unchanged):**

```csharp
builder.Services.AddSharedKernelOpenApi(title: "Orders API");
// Equivalent to: configureSecuritySchemes: o => { } — o.Bearer defaults to true, everything else false.
```

**Bearer + ApiKey** (a service also accepting `SharedKernel.Security.ApiKey` machine-client tokens):

```csharp
builder.Services.AddSharedKernelOpenApi(title: "Orders API", configureSecuritySchemes: o =>
{
    o.ApiKey = true;
    o.ApiKeyHeaderName = "X-Api-Key";   // match this service's own SharedKernel.Security.ApiKey wiring
});
```

**Bearer + mTLS** (a service also accepting `SharedKernel.Security.Mtls` client-certificate auth):

```csharp
builder.Services.AddSharedKernelOpenApi(title: "Payments API", configureSecuritySchemes: o =>
{
    o.MutualTls = true;
});
```

All three flags (`Bearer`/`ApiKey`/`MutualTls`) can be combined. `ApiKeyHeaderName` is a plain
configurable string — this package never takes a `ProjectReference` on
`SharedKernel.Security.ApiKey`/`.Mtls` merely to reuse a header-name constant; pass the value that
matches your own service's concrete provider wiring.

> **OR semantics, not AND.** Each active scheme is registered as its **own separate** OpenAPI
> security requirement object — a request satisfies the document's security requirement by
> presenting **any one** active mechanism (Bearer *or* ApiKey *or* mTLS), never all of them at once.
> This is the **opposite** of this package's usual `[RequireRole]`/`[RequirePermission]`
> AND-across-attributes composition rule and is easy to get backwards if you're used to that
> convention — the two features compose in opposite directions on purpose.

### Manual verification result (T-49)

Scalar's "Authorize" affordance is rendered entirely client-side from the fetched OpenAPI document
— this package's own responsibility ends at producing a correct document. A real listening host was
used to confirm the full chain, not just the JSON in isolation:

- `GET /openapi/v1.json` with `ApiKey`/`MutualTls` both enabled alongside the default `Bearer`
  returns a `security` array with exactly **3** entries (one OR'd requirement object per active
  scheme) and a `components.securitySchemes` dictionary naming all three (`Bearer`, `ApiKey`, and
  the `mutualTLS`-typed scheme) — matching T-47/T-48's automated assertions against the real
  generated document.
- `GET /scalar/v1` returns `200 text/html`, and the returned page's bootstrap script correctly
  configures Scalar's client-side renderer with `sources: [{"title":"v1","url":"openapi/v1.json"}]`
  — i.e. Scalar is wired to fetch and render the exact document already proven correct.
- A **pixel-level browser check** of the rendered "Authorize" button/dialog was not performed — this
  session's environment has no browser. Scalar's client-side rendering of a standards-compliant
  OpenAPI `securitySchemes`/`security` shape is stable, well-exercised upstream behavior that this
  platform does not re-implement or need to visually re-verify; confirming the document contract
  reaching it (above) is the correct and sufficient scope of verification for this package.

---

## RFC 8594 Sunset / Deprecation headers

`ApiVersionLifecycleOptions` (via `AddSharedKernelApiVersioning`'s additive `configureLifecycle`
parameter) lets a retiring API version carry a machine-readable `Sunset` date and an optional
successor link, per [RFC 8594](https://www.rfc-editor.org/rfc/rfc8594). This is **additive to**,
never a replacement for, the existing `ReportApiVersions` header family
(`api-supported-versions`/`api-deprecated-versions`) — both header families may appear on the same
response simultaneously.

```csharp
var apiVersionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .HasDeprecatedApiVersion(new ApiVersion(2, 0))   // deprecation itself still declared here —
    .Build();                                          // Asp.Versioning's own mechanism, unchanged

builder.Services.AddSharedKernelApiVersioning(configureLifecycle: o =>
{
    o.Configure(
        new ApiVersion(2, 0),
        sunsetDate: new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
        successor: new Uri("https://api.example.com/v3/orders"));
});
```

A response served under API version `2.0` now carries:

```text
Sunset: Fri, 01 Jan 2027 00:00:00 GMT
Deprecation: true
Link: <https://api.example.com/v3/orders>; rel="successor-version"
```

- `Sunset` is always a well-formed RFC 7231 HTTP-date (`DateTimeOffset.ToString("R")`) — never an
  arbitrary string or a bare date.
- `Deprecation` is sourced from Asp.Versioning's **own** `HasDeprecatedApiVersion(...)`/
  `[ApiVersion(Deprecated = true)]` declaration, independent of whether a sunset date is set — this
  package never grows a second, parallel "deprecated" flag.
- `Link: rel="successor-version"` appears only when **both** a sunset date and a successor `Uri` are
  declared together for that version.
- A service that declares no sunset date on any version sees zero new headers — this capability is
  a pure addition, not a rework of the versioning pipeline.

---

## Correlation id

This package no longer handles the correlation id. `app.UseSharedKernelRequestContext()`
(`SharedKernel.ServiceDefaults.Security`) owns it: it accepts a caller-supplied `X-Correlation-Id`
only when it is at most 128 characters of `[A-Za-z0-9-_:.]` (dashed and `"N"` GUIDs, ULIDs and similar
tokens pass unchanged), otherwise it creates a new one; it echoes the value on every response and
carries it on `IRequestContext.CorrelationId` for every outbound hop. The rule is fixed — one rule for
every channel, `SharedKernel.Execution`'s `CorrelationIds` — so there is no options type to tune.
`ProblemDetails.Extensions["traceId"]` is the trace id (`Activity.Current?.Id`), not the correlation id.

---

## File / multipart upload validation

`RequireValidatedUploadAttribute` gates an upload endpoint on declared size, declared content type,
and (optionally) a magic-byte signature check — a boundary-shape check for document-heavy workflows
(KYC documents, statements, dispute evidence) that routinely accept uploads directly through the API
before handing them to `08.Storage`.

Worked example — a KYC document-upload endpoint accepting PDFs and JPEGs up to 5 MB, against a
service-wide default of 10 MB:

```csharp
builder.Services.AddSharedKernelUploadValidation(o =>
{
    o.MaxSizeBytes = 10 * 1024 * 1024;   // 10 MB service-wide default
    o.AllowedContentTypes.Add("application/pdf");
    o.AllowedContentTypes.Add("image/jpeg");
    // Optional deeper check: verify the declared content type against its actual leading bytes.
    o.AllowedMagicBytes["application/pdf"] = [0x25, 0x50, 0x44, 0x46];   // "%PDF"
});

var app = builder.Build();

app.MapPost("/v{version:apiVersion}/kyc/documents", UploadKycDocumentHandler)
    .RequireValidatedUpload(maxSizeBytes: 5 * 1024 * 1024, "application/pdf", "image/jpeg")
    .AddEndpointFilter<UploadValidationEndpointFilter>();
```

A request whose declared `Content-Length` exceeds the resolved limit, whose declared `Content-Type`
(stripped of any `;`-delimited parameter, e.g. `charset=utf-8`, before matching) isn't in the allowed
list, or whose leading bytes don't match a configured magic-byte signature for its declared type is
rejected — 413/415/400 respectively, via `ProblemDetails` — **before** the endpoint handler ever runs.
When a magic-byte check is configured, the filter buffers the request body
(`HttpRequest.EnableBuffering()`) and seeks back to position `0` after the peek, so
`UploadKycDocumentHandler` still sees the full, unconsumed body. Per-endpoint override args on
`RequireValidatedUpload`/`RequireValidatedUploadAttribute` take precedence over the global
`UploadValidationOptions` defaults; an endpoint carrying no attribute performs zero validation
(fully opt-in).

> **THIS IS A BOUNDARY-SHAPE CHECK ONLY. VIRUS/MALWARE SCANNING AND ANTIVIRUS-ENGINE INTEGRATION ARE
> EXPLICITLY OUT OF SCOPE AND NEVER PERFORMED BY THIS CAPABILITY.** Passing this validation says
> nothing about whether the uploaded content is safe to store or open — it only proves the upload is
> the declared size and (optionally) the declared shape. Wire a real scanning pipeline separately
> for any upload that needs one. No third-party MIME-detection library is added here — the
> magic-byte table is a small, locally-maintained, extensible signature list, not a general-purpose
> file-type sniffer.

---

## What you get out of the box

| Concern | Type |
| --- | --- |
| `ErrorType` → HTTP status code | `ErrorTypeStatusCodeMap.Resolve` (`SharedKernel.Presentation.Core`, namespace `SharedKernel.Presentation.Errors`) |
| `Error` → `ProblemDetails` | `ErrorProblemDetailsExtensions.ToProblemDetails` |
| `Result<T>` → `IResult`/`ActionResult` | `ResultHttpExtensions` |
| Global unhandled-exception handling | `SharedKernelExceptionHandler` |
| API versioning defaults | `SharedKernelApiVersioningDefaults`, `AddSharedKernelApiVersioning` |
| OpenAPI + Scalar | `AddSharedKernelOpenApi`, `MapSharedKernelOpenApi` |
| Correlation id (inbound) | `UseSharedKernelRequestContext()` in `SharedKernel.ServiceDefaults.Security` — not this package |
| Declarative role/permission authorization | `RequireRoleAttribute`, `RequirePermissionAttribute` (both in `SharedKernel.Presentation.Core`), `AuthorizationRequirementEndpointFilter`, `AddSharedKernelAuthorizationFilters` |
| Declarative step-up/fresh-authentication gating | `RequireFreshAuthenticationAttribute`, `RequireAuthenticationMethodAttribute` (`SharedKernel.Presentation.Core`) |
| Multi-field validation `ProblemDetails` | `ValidationProblemDetailsExtensions.ToProblemDetails` |
| Security response headers | `SecurityHeadersOptions`, `UseSharedKernelSecurityHeaders` |
| CORS policy convention | `CorsPolicyOptions`, `CorsPolicyNames`, `AddSharedKernelCors` |
| Inbound idempotency-key HTTP boundary | `HttpContextIdempotencyExtensions.TryGetIdempotencyKey`, `RequireIdempotencyKeyAttribute`, `IdempotencyKeyRequirementEndpointFilter`, `AddSharedKernelIdempotencyFilters` |
| ETag / `If-Match` conditional requests | `RowVersionETag`, `ConditionalRequestExtensions` |
| Rate-limit rejection → `ProblemDetails`/429 bridge | `RateLimitRejectionProblemDetails` |
| Payload size / JSON max-depth limits | `PayloadLimitsOptions`, `AddSharedKernelPayloadLimits`, `UseSharedKernelPayloadLimits` |
| OpenAPI ApiKey/mTLS security schemes | `OpenApiSecuritySchemesOptions` (via `AddSharedKernelOpenApi`) |
| RFC 8594 Sunset/Deprecation headers | `ApiVersionLifecycleOptions` (via `AddSharedKernelApiVersioning`) |
| File/multipart upload validation | `UploadValidationOptions`, `RequireValidatedUploadAttribute`, `AddSharedKernelUploadValidation` |

See the [Configuration Reference](../CONFIGURATION.md) for every DI extension method's options and
defaults.
