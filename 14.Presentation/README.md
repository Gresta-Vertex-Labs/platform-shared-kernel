# 14.Presentation

> **The inbound API boundary of a SharedKernel service: HTTP, OpenAPI, SignalR and gRPC with one error contract. An
> endpoint turns a request into a command or query, sends it, and maps the `Result` to the response.**

Use cases live in `05.Application`: a command or query, its permission, its validator and its handler. This domain is
the thin edge in front of them. It turns the outcome into what a caller receives — an HTTP response or RFC 9457
problem, a SignalR hub error, a gRPC status, with the same code, message and redaction on all three — and owns the
concerns of the boundary itself: correlation ids, authentication strength, security headers, CORS, request limits,
`Idempotency-Key`, `ETag`/`If-Match` and paging parameters.

## Packages

| Package | Use it for | Entry point |
| --- | --- | --- |
| [`SharedKernel.Presentation.WebApi`](SharedKernel.Presentation.WebApi/README.md) | Every HTTP API: endpoint modules, typed results for `Result`, the error contract, authorization attributes, correlation ids, security headers, CORS, limits, headers and paging. No third-party dependencies | `builder.AddSharedKernelWebApi()`, `app.UseSharedKernelWebApi()`, `app.MapEndpoints()` |
| [`SharedKernel.Presentation.OpenApi`](SharedKernel.Presentation.OpenApi/README.md) | API versioning, one OpenAPI document per version, the Scalar reference, sunset and deprecation headers | `builder.AddSharedKernelOpenApi()`, `app.MapSharedKernelOpenApi()` |
| [`SharedKernel.Presentation.SignalR`](SharedKernel.Presentation.SignalR/README.md) | Hubs: coded hub errors, `Result` hub methods, an invocation rate limit, tenant groups | `builder.AddSharedKernelSignalR()` |
| [`SharedKernel.Presentation.Grpc`](SharedKernel.Presentation.Grpc/README.md) | gRPC services: a rich `google.rpc.Status` for every error a service method produces | `builder.AddSharedKernelGrpc()` |

WebApi is the core; the other three build on it and version with it. Each package has one public namespace, the
package name: `using SharedKernel.Presentation.WebApi;` covers endpoints, controllers, options and error codes.
Outbound calls to other services are `11.Communication`'s job; identity is `12.Security`'s.

## The 10-minute path

### 1. Register and add the pipeline

```csharp
using SharedKernel.Application;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOidcAuthentication(builder.Configuration);    // who is calling (12.Security)
builder.Services.AddSharedKernelRequestContext();                 // the caller, for the use cases (13.ServiceDefaults)
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app
    .WithAuthorization());                                        // [RequirePermission] on commands and queries (05)
builder.AddSharedKernelWebApi();                                  // the HTTP boundary

var app = builder.Build();

app.UseSharedKernelWebApi();   // first, before any endpoint
app.MapEndpoints();            // every IEndpointModule of this assembly

app.Run();
```

`UseSharedKernelWebApi()` adds, in order: correlation ids, security headers, the exception handler, routing, CORS,
authentication, rate limiting, authorization, and the checks of required headers and paging parameters. If
`AddSharedKernelWebApi()` runs without it, the host logs a warning at startup.

### 2. Map the use cases in an endpoint module

The use cases are `05.Application` commands and queries, each with its permission
([05.Application](../05.Application/README.md#2-write-a-use-case)). A module maps them:

```csharp
using MediatR;
using SharedKernel.Persistence.Abstractions.Repositories;   // EntityVersion
using SharedKernel.Presentation.WebApi;

public sealed class OrderEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        // 200 with an ETag; 304 when If-None-Match names the current version; a 404 problem when there is none.
        orders.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetOrder(id), ct).ToOkWithETag(order => order.Version.ToString()));

        // page and pageSize are validated before the handler: 400 with the pagination.* codes.
        orders.MapGet("/", (Paging paging, ISender sender, CancellationToken ct) =>
            sender.Send(new ListOrders(paging.Request), ct).ToOk());

        // Idempotency-Key required: 400 without a valid one. The command (IIdempotentRequest) deduplicates.
        orders.MapPost("/", (PlaceOrderRequest body, IdempotencyKey key, ISender sender, CancellationToken ct) =>
            sender.Send(new PlaceOrder(body.Customer, body.Amount, key.Value), ct).ToCreated(id => $"/orders/{id}"));

        // If-Match required: 428 without it, 412 when the version is no longer current.
        orders.MapPut("/{id:guid}/address", (Guid id, ChangeAddressRequest body, IfMatch<EntityVersion> ifMatch, ISender sender, CancellationToken ct) =>
            sender.Send(new ChangeAddress(id, body.Street, ifMatch.Version), ct).ToNoContent());

        // Authentication strength is an HTTP concern: a one-time code verified in the last five minutes.
        orders.MapPost("/{id:guid}/refunds", (Guid id, ISender sender, CancellationToken ct) =>
                sender.Send(new RefundOrder(id), ct).ToAccepted($"/orders/{id}"))
            .RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp");
    }
}
```

- `app.MapEndpoints()` is generated at compile time by a source generator the WebApi package carries: it calls `Map`
  on every module of the assembly. No reflection, no registration.
- The endpoints repeat no permission: `[RequirePermission]` is on each command and query, and the pipeline checks it
  on every path the use case can take.
- `ToOk`, `ToOkWithETag`, `ToCreated`, `ToAccepted` and `ToNoContent` return typed results: a failure is written as
  a problem, OpenAPI sees the success type, and a unit test asserts on the result. MVC controllers return the same.

### 3. What an error looks like

Every error response, whatever produced it (a failed `Result`, an exception, the framework, authorization, a missing
header, invalid paging, rate limiting), is `application/problem+json`:

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
  "errors": { "Customer": ["'Customer' must not be empty."], "Amount": ["'Amount' must be greater than '0'."] },
  "errorCodes": { "Customer": ["NotEmptyValidator"], "Amount": ["GreaterThanValidator"] }
}
```

That is a validator like 05.Application's `PlaceOrder` example failing twice: each field is keyed by its path, with
FluentValidation's message and error code (set your own with `WithErrorCode`).

- Clients branch on `errorCode`; support staff search for `traceId` and `correlationId`.
- The status comes from the error's type: `Validation` 400, `Unauthorized` 401, `Forbidden` 403, `NotFound` 404,
  `Conflict` 409, `BusinessRule` 422, `Unexpected` 500, `Unavailable` 503 (with an optional `Retry-After`),
  `Timeout` 504.
- Outside Development, the `detail` of a server error (500, 503, 504) is a generic sentence; the `errorCode` stays.
- With an `ILocalizationCatalog` registered, messages are translated into the request culture.

### 4. Authorization: the use case, and the edge

| Where | What | Anonymous | Signed in, not met |
| --- | --- | --- | --- |
| On the command or query (05.Application) | `[RequirePermission("orders.write")]` | 401 `unauthorized.default` | 403 `forbidden.insufficient_permission` |
| On a hub, a gRPC service, or an endpoint that sends no command | `RequireEndpointPermission`, `RequireRole` | 401 `unauthorized.default` | 403 `forbidden.insufficient_permission` |
| On an endpoint, hub method or gRPC method | `RequireFreshAuthentication(300)`, `RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp")` | 401 | 401 `unauthorized.step_up_required` with an RFC 9470 challenge |

Permissions go on the use case with `[RequirePermission]`. `[RequireEndpointPermission]` is only for hubs, gRPC
services and methods, and endpoints that send no command; the step-up attributes stay on the endpoint because only
the HTTP request knows how the caller signed in. The edge attributes are native `[Authorize]` attributes evaluated
against `IUserContext`; values within one attribute are alternatives, several attributes must all hold. Both layers
answer with the same codes.

### 5. Headers and paging

| Declaration | Missing | Invalid |
| --- | --- | --- |
| `IdempotencyKey key` (required) | 400 `idempotency.key_required` | 400 `idempotency.key_invalid` |
| `IdempotencyKey? key` (accepted) | the handler runs, `key` is `null` | 400 `idempotency.key_invalid` |
| `IfMatch<EntityVersion> ifMatch` (required) | 428 `precondition.required` (also for `*`) | 400 `precondition.invalid`, or 412 `precondition.failed` for a weak tag or one that is not a version |
| `IfMatch<EntityVersion>? ifMatch` (accepted) | the handler runs unconditionally | as required; `*` is 400 |
| `Paging paging` (`page`, `pageSize`) | page 1 of the default size | 400 `validation.failed`, `pagination.*` or `validation.invalid_format` per parameter |
| `CursorPaging paging` (`cursor`, `limit`) | the first page of the default limit | 400 `validation.failed`, `pagination.*` or `validation.invalid_format` per parameter |

All are checked after authorization, before the handler. MVC actions and endpoints without a parameter declare the
headers with `[RequireIdempotencyKey]`, `[AcceptIdempotencyKey]`, `[RequireIfMatch]`, `[AcceptIfMatch]` or the
matching conventions. The key only identifies the request: the command, implementing 05.Application's
`IIdempotentRequest`, deduplicates.

### 6. ETags and stale versions

- `ToOkWithETag(order => order.Version.ToString())` sends `ETag: "<version>"`; with 06.Persistence the version is an
  opaque `EntityVersion` token. A `GET` whose `If-None-Match` names the current version is answered 304.
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

app.MapEndpoints();
app.MapHub<OrdersHub>("/hubs/orders").RequireEndpointPermission("orders.read");
app.MapGrpcService<OrderGrpcService>();
app.MapSharedKernelOpenApi();   // /openapi/v1.json and /scalar, in Development only
```

- OpenAPI documents every operation's problem responses, security requirements, required or accepted headers and
  paging parameters.
- A hub error reaches the client as `{code}: {message}` behind SignalR's own sentence; read it with
  `HubErrorMessage.TryParse`.
- A gRPC error carries a `google.rpc.Status` with an `ErrorInfo` (code, domain, trace and correlation ids) and the field
  violations; a service method ends a failed `Result` with `SharedKernel.Core`'s `GetValueOrThrow()`.

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
| Business logic, or calling repositories from an endpoint | A command or query sent through `ISender` (05.Application) |
| Hand-mapped `app.MapOrderEndpoints()` extension methods | An `IEndpointModule`, mapped by `app.MapEndpoints()` |
| Permissions repeated on an endpoint that sends a command | `[RequirePermission]` on the command or query |
| `ErrorPresentation`, `ErrorTypeStatusCodeMap`, `GrpcStatusCodeMap`, `PresentationErrorCodes.ForStatus`, `AddSharedKernelAuthorization()` as public API | Internal since P-563; `error.ToErrorResult()` and `error.ToProblemDetails(httpContext)` for your own writer; the setup calls register authorization |
| The `.Errors`, `.Http`, `.Idempotency` and `.Options` namespaces | The package's root namespace |
| `GetIfMatchTags()` | `GetIfMatch()`, or an `IfMatch<TVersion>` parameter |
| File upload validation | A presigned upload straight to storage (08.Storage), or `WithRequestSizeLimit(bytes)` |
| A response envelope around every body | The success value is the body; errors are problems. `11.Communication.Rest` reads both back into a `Result<T>` |
| MVC `ToActionResult` | The same typed results, returned from the action |
| A SignalR Redis backplane wrapper | `AddSharedKernelSignalR().AddStackExchangeRedis(…)` |
| gRPC interceptors for correlation, tenant and authorization; gRPC result extensions | The shared HTTP pipeline, `ITenantProvider`, the attributes; `SharedKernel.Core`'s `GetValueOrThrow()` |
| Rate limiting policies | ASP.NET Core `AddRateLimiter()` or 13.ServiceDefaults' `AddSharedKernelRateLimiting()`; the 429 body is automatic |
| .NET 10 `AddValidation()` | Validation in the MediatR pipeline (05.Application) |

## Read next

- The package READMEs above, for every rule and status code.
- [CLAUDE.md](CLAUDE.md), for maintainers: rules, traps, invariants, EventIds and decisions.
- [05.Application/docs/p563](../05.Application/docs/p563/), the P-563 design (one application model, this thin edge),
  and [docs/p562](docs/p562/), the P-562 decisions behind the error contract and authorization.
