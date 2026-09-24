# 14.Presentation

> **The inbound API boundary of a SharedKernel service: HTTP, OpenAPI, SignalR and gRPC with one error contract and
> one authorization model.**

Application code returns `Result` or `Result<T>`, or throws. These packages turn the outcome into what a caller
receives: an HTTP response or RFC 9457 problem, a SignalR hub error, a gRPC status. The same error has the same code,
the same message and the same redaction on all three protocols. The packages also handle the boundary itself:
authorization against `IUserContext`, correlation ids, security headers, CORS, request limits, idempotency keys and
optimistic concurrency with `ETag` and `If-Match`.

## Packages

| Package | Use it for | Entry point |
| --- | --- | --- |
| [`SharedKernel.Presentation.WebApi`](SharedKernel.Presentation.WebApi/README.md) | Every HTTP API: the error contract, typed results for `Result`, authorization attributes, correlation ids, security headers, CORS, limits, `Idempotency-Key`, `ETag`/`If-Match`. No third-party dependencies | `builder.AddSharedKernelWebApi()`, `app.UseSharedKernelWebApi()` |
| [`SharedKernel.Presentation.OpenApi`](SharedKernel.Presentation.OpenApi/README.md) | API versioning, one OpenAPI document per version, the Scalar reference, sunset and deprecation headers | `builder.AddSharedKernelOpenApi()`, `app.MapSharedKernelOpenApi()` |
| [`SharedKernel.Presentation.SignalR`](SharedKernel.Presentation.SignalR/README.md) | Hubs: coded hub errors, `Result` hub methods, an invocation rate limit, tenant groups | `builder.AddSharedKernelSignalR()` |
| [`SharedKernel.Presentation.Grpc`](SharedKernel.Presentation.Grpc/README.md) | gRPC services: a rich `google.rpc.Status` for every error a service method produces | `builder.AddSharedKernelGrpc()` |

The WebApi package is the core; the other three build on it. A service references the core plus what it serves.
Outbound calls to other services are `11.Communication`'s job and identity is `12.Security`'s.

## The 10-minute path

### 1. Register and add the pipeline

```csharp
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Oidc.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOidcAuthentication(builder.Configuration);   // who is calling (12.Security)
builder.AddSharedKernelWebApi();                                   // the HTTP boundary

builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

app.UseSharedKernelWebApi();   // first, before any endpoint
app.MapOrderEndpoints();

app.Run();
```

`UseSharedKernelWebApi()` adds, in order: correlation ids, security headers, the exception handler, routing, CORS,
authentication, rate limiting, authorization and the checks of required headers. Call it before mapping endpoints. If
`AddSharedKernelWebApi()` runs without it, the host logs a warning at startup.

`AddOidcAuthentication` reads `SharedKernel:Security:Oidc`. Any authentication works as long as its package registers
an `IUserContextMapper`, as the 12.Security packages do; without one, every signed-in caller is refused with 403.

### 2. Return results

```csharp
using SharedKernel.Persistence.Abstractions.Repositories;   // EntityVersion
using SharedKernel.Presentation.WebApi;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").RequirePermission("orders.read");

        // 200 with an ETag, 304 when If-None-Match names the current version, 404 problem when there is no order.
        orders.MapGet("/{id:guid}", (Guid id, IOrderService service, CancellationToken ct) =>
            service.GetAsync(id, ct).ToOkWithETag(order => order.Version.ToString(), OrderResponse.From));

        // Idempotency-Key required: without a valid key, 400 before the handler runs.
        orders.MapPost("/", (PlaceOrderRequest body, IdempotencyKey key, IOrderService service, CancellationToken ct) =>
                service.PlaceAsync(body, key.Value, ct).ToCreated(order => $"/orders/{order.Id}", OrderResponse.From))
            .RequirePermission("orders.write");

        // If-Match required: 428 without it, 412 when the version is not current.
        orders.MapPut("/{id:guid}/address", (Guid id, ChangeAddressRequest body, IfMatch<EntityVersion> ifMatch, IOrderService service, CancellationToken ct) =>
                service.ChangeAddressAsync(id, body, ifMatch.Version, ct).ToNoContent())
            .RequirePermission("orders.write");

        // Step-up: a one-time code verified in the last five minutes.
        orders.MapPost("/{id:guid}/refunds", (Guid id, IOrderService service, CancellationToken ct) =>
                service.RefundAsync(id, ct).ToAccepted($"/orders/{id}"))
            .RequirePermission("orders.refund")
            .RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp");

        return app;
    }
}
```

`IOrderService` is your application layer: its methods return `Task<Result<Order>>` or `Task<Result>`. A MediatR
`sender.Send(query, ct)` maps the same way. `ToOk`, `ToOkWithETag`, `ToCreated`, `ToAccepted` and `ToNoContent` return
typed results, so a failure is written as a problem, OpenAPI sees the success type, and a unit test asserts on the
result. MVC controllers return the same typed results.

### 3. What an error looks like

Every error response, whatever produced it (a failed `Result`, an exception, the framework, authorization, a missing
header, rate limiting), is `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "2 validation errors occurred.",
  "instance": "/orders",
  "errorCode": "validation.failed",
  "correlationId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {
    "quantity": ["Quantity must be at least 1."],
    "sku": ["A SKU is required."]
  },
  "errorCodes": {
    "quantity": ["order.quantity_invalid"],
    "sku": ["order.sku_required"]
  }
}
```

- Clients branch on `errorCode`; support staff search for `traceId` and `correlationId`.
- The status comes from the error's type: `Validation` 400, `Unauthorized` 401, `Forbidden` 403, `NotFound` 404,
  `Conflict` 409, `BusinessRule` 422, `Unexpected` 500, `Unavailable` 503 (with an optional `Retry-After`),
  `Timeout` 504.
- Outside Development, the `detail` of a server error (500, 503, 504) is a generic sentence; the `errorCode` stays.
- With an `ILocalizationCatalog` registered, messages are translated into the request culture.

### 4. Authorization and step-up

| Attribute or convention | Anonymous caller | Signed in, requirement not met |
| --- | --- | --- |
| `RequirePermission("a", "b")` (either) | 401 `unauthorized.default` | 403 `forbidden.insufficient_permission` |
| `RequireRole("support")` | 401 | 403 |
| `RequireFreshAuthentication(300)` | 401 | 401 `unauthorized.step_up_required`, `WWW-Authenticate: Bearer error="insufficient_user_authentication", …, max_age="300"` |
| `RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp")` | 401 | 401 `unauthorized.step_up_required` with `max_age` |

The attributes (`[RequirePermission]`, …) are native `[Authorize]` attributes: they work on minimal APIs, MVC,
SignalR hubs and gRPC services alike, with nothing else to register. Values within one attribute are alternatives;
several attributes must all hold. They are checked against `IUserContext`, never raw claims.

### 5. Idempotency-Key and If-Match

Declare a header once; the pipeline checks it after authorization, before the handler runs.

| Declaration | Header missing | Header invalid |
| --- | --- | --- |
| `IdempotencyKey key` (required) | 400 `idempotency.key_required` | 400 `idempotency.key_invalid` |
| `IdempotencyKey? key` (accepted) | Handler runs, `key` is `null` | 400 `idempotency.key_invalid` |
| `IfMatch<EntityVersion> ifMatch` (required) | 428 `precondition.required` (also for `*`) | 400 `precondition.invalid`, or 412 `precondition.failed` for a weak tag or one that is not a version |
| `IfMatch<EntityVersion>? ifMatch` (accepted) | Handler runs unconditionally, `ifMatch` is `null` | Same as required; `*` is 400 |

MVC actions and endpoints without a parameter use `[RequireIdempotencyKey]`, `[AcceptIdempotencyKey]`,
`[RequireIfMatch]`, `[AcceptIfMatch]` or the matching conventions. The key only identifies the request: pass it to a
command implementing 05.Application's `IIdempotentRequest`, which deduplicates.

### 6. ETags and stale versions

- `ToOkWithETag(order => order.Version.ToString(), …)` sends `ETag: "<version>"`. With 06.Persistence the version is
  an opaque `EntityVersion` token.
- A `GET` whose `If-None-Match` names the current version is answered 304 without a body.
- When a write fails with a version conflict (`persistence.concurrency_conflict`, `storage.precondition_failed`,
  `storage.already_exists`, or a code you add to `Problems:PreconditionFailedErrorCodes`) and the request carried
  `If-Match` or `If-None-Match`, the answer is **412**, keeping the code. Every other conflict stays 409.

### 7. OpenAPI, SignalR and gRPC

One call each, after `AddSharedKernelWebApi()`:

```csharp
builder.AddSharedKernelOpenApi(options => options.Title = "Orders API");
builder.AddSharedKernelSignalR();
builder.AddSharedKernelGrpc(options => options.ErrorDomain = "orders.example.com");

var app = builder.Build();
app.UseSharedKernelWebApi();

app.MapOrderEndpoints();
app.MapHub<OrdersHub>("/hubs/orders");
app.MapGrpcService<OrderGrpcService>();
app.MapSharedKernelOpenApi();   // /openapi/v1.json and /scalar, in Development only
```

- OpenAPI documents every operation's problem responses, security requirements and required or accepted headers.
- A hub error reaches the client as `{code}: {message}` behind SignalR's own sentence; read it with
  `HubErrorMessage.TryParse`.
- A gRPC error carries a `google.rpc.Status` with an `ErrorInfo` (code, domain, trace and correlation ids) and the
  field violations; end a failed `Result` with `SharedKernel.Core`'s `GetValueOrThrow()`.

## Configuration

Settings bind from `SharedKernel:Presentation:WebApi`, `:OpenApi`, `:SignalR` and `:Grpc` and are validated before the
service handles a request. Every key, default and rule is in [CONFIGURATION.md](CONFIGURATION.md).

```json
{
  "SharedKernel": {
    "Presentation": {
      "WebApi": {
        "Cors": { "AllowedOrigins": [ "https://app.example.com" ] },
        "Problems": { "UnavailableRetryAfter": "00:00:30" }
      },
      "OpenApi": { "Title": "Orders API" },
      "SignalR": { "InvocationRateLimit": { "PermitLimit": 20, "Window": "00:00:01" } },
      "Grpc": { "ErrorDomain": "orders.example.com" }
    }
  }
}
```

## Deliberately not here

| Not here | Use instead |
| --- | --- |
| File upload validation | A presigned upload straight to storage (08.Storage), or `WithRequestSizeLimit(bytes)` on the endpoint |
| A response envelope around every body | The success value is the body; errors are problems. `11.Communication.Rest` reads both back into a `Result<T>` |
| MVC `ToActionResult` | The same typed results, returned from the action |
| Custom authorization filters | The four attributes, which are native policies |
| A SignalR Redis backplane wrapper | `AddSharedKernelSignalR().AddStackExchangeRedis(…)` |
| gRPC interceptors for correlation, tenant and authorization | The shared HTTP pipeline, `ITenantProvider`, the attributes |
| gRPC `ThrowIfFailure`/`GetValueOrThrow` of its own | `SharedKernel.Core`'s |
| A version-lifecycle middleware | Asp.Versioning's sunset and deprecation policies, through the OpenApi add-on |
| Rate limiting policies | ASP.NET Core `AddRateLimiter()` or 13.ServiceDefaults' `AddSharedKernelRateLimiting()`; the 429 body is automatic |
| Outbound HTTP or gRPC clients | `11.Communication` |
| .NET 10 `AddValidation()` | Validation in the MediatR pipeline (05.Application) |

## Read next

- The package READMEs above, for every rule and status code.
- [CLAUDE.md](CLAUDE.md), for maintainers: rules, traps, invariants, EventIds and decisions.
- [docs/p562](docs/p562/), the decisions behind the current design (2026-09-23/24).
