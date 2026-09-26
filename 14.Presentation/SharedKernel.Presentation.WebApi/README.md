# SharedKernel.Presentation.WebApi

> **The HTTP boundary of a SharedKernel service: one call to register it, one to add it to the pipeline, and one
> `application/problem+json` error shape for every failure.**

Application code returns `Result` or `Result<T>`, or throws. This package turns the outcome into the response: the
success body, or an RFC 9457 problem with a stable `errorCode`, the `traceId` and the `correlationId`. It also owns the
concerns of the boundary itself: the problem bodies of authorization refusals, security headers, CORS, request limits,
and the `Idempotency-Key`, `ETag` and `If-Match` headers. It has no third-party dependencies. The correlation id and the
request's context come from `SharedKernel.ServiceDefaults.Security`'s `UseSharedKernelRequestContext()`, which runs
first; the authorization attributes and policies from `SharedKernel.Presentation.Core` (P-579). The
[OpenAPI add-on](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/14.Presentation/SharedKernel.Presentation.OpenApi),
[SignalR](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/14.Presentation/SharedKernel.Presentation.SignalR)
and [gRPC](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/14.Presentation/SharedKernel.Presentation.Grpc)
packages build on it.

## Contents

- [Install](#install)
- [Setup](#setup)
- [Endpoint modules](#endpoint-modules)
- [From Result to HTTP](#from-result-to-http)
- [The error shape](#the-error-shape)
- [Authorization](#authorization)
- [Idempotency-Key](#idempotency-key)
- [ETag, If-None-Match and If-Match](#etag-if-none-match-and-if-match)
- [Paging](#paging)
- [Correlation ids](#correlation-ids)
- [Security headers, CORS and request limits](#security-headers-cors-and-request-limits)
- [Rate limiting](#rate-limiting)
- [Error codes](#error-codes)
- [Configuration](#configuration)
- [Logging](#logging)
- [Pitfalls](#pitfalls)
- [Not in this package](#not-in-this-package)

## Install

```shell
dotnet add package SharedKernel.Presentation.WebApi
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host (referenced by a service's API project) |
| Dependencies | `SharedKernel.Presentation.Core`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Configuration`, `SharedKernel.Contracts` (paging), the ASP.NET Core shared framework |
| Composed with | `SharedKernel.ServiceDefaults.Security` (the request context), which the host adds itself |
| Third-party packages | none |
| Ships with it | the endpoint-module source generator, under `analyzers/dotnet/cs` |

Every public type is in one namespace, `SharedKernel.Presentation.WebApi`: setup, options, typed results, endpoint
modules, header and paging parameters, error codes and problem member names. The four authorization attributes and
their conventions are `SharedKernel.Presentation.Core`'s, in `SharedKernel.Presentation.Authorization`, because
SignalR and gRPC use them too. The plumbing the add-ons share (`ErrorPresentation`, the status map, the
endpoint-metadata interfaces) is internal to the presentation packages, which version together.

Handlers send `05.Application` commands and queries through the kernel's `ISender`; this package does not reference
MediatR or `05.Application`, so any code that returns `Result`/`Result<T>` maps the same way.

## Setup

```csharp
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();   // the request context, correlation id and baggage refusal
builder.AddSharedKernelWebApi(options =>
{
    options.Problems.UnavailableRetryAfter = TimeSpan.FromSeconds(30);
    options.Cors.AllowedOrigins.Add("https://app.example.com");
});

var app = builder.Build();

app.UseSharedKernelRequestContext();   // first: its scope wraps everything below
app.UseSharedKernelWebApi(pipeline => pipeline
    .AtStart(web => web.UseForwardedHeaders())
    .BeforeAuthorization(web => web.UseRequestLocalization()));

app.MapEndpoints();   // every IEndpointModule of this assembly

app.Run();
```

`AddSharedKernelWebApi()` binds `SharedKernel:Presentation:WebApi`
([CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/CONFIGURATION.md#sharedkernelpresentationwebapi)),
runs the `configure` callback after binding, validates the result, and registers:

- problem details for every error source, and the platform's exception handling as the fallback of
  `UseExceptionHandler()`;
- the authorization policies behind the four authorization attributes;
- a CORS policy, only when `Cors:AllowedOrigins` lists origins;
- the 429 problem body for rate limiting that has no `OnRejected` of its own;
- Kestrel without the `Server` header and with a 4 MiB body limit, the JSON depth limit when set, HSTS settings;
- `RouteHandlerOptions.ThrowOnBadRequest = true` and MVC's model-state response, so binding failures get the platform
  shape in every environment;
- startup warnings for `UseSharedKernelWebApi()` never being called and for exception details outside Development.

`UseSharedKernelWebApi()` adds, in this order:

1. the `AtStart` hooks;
2. HSTS (outside Development), then the security headers and the default `Cache-Control`;
3. the exception handler, then problem bodies for bodiless error statuses;
4. `UseRouting()`, then CORS and the WebSocket origin check (when origins are configured);
5. the `BeforeAuthentication` hooks, then `UseAuthentication()` (when authentication is registered);
6. the `BeforeAuthorization` hooks, then `UseRateLimiter()` (when rate limiting is registered);
7. `UseAuthorization()`, then the endpoint's `Idempotency-Key`, `If-Match` and paging checks.

Call it right after `UseSharedKernelRequestContext()` (`SharedKernel.ServiceDefaults.Security`), then map endpoints;
put other middleware after it. The request context goes first so its `RequestContextScope` wraps this pipeline's
exception handler: the correlation id is on every log line and every response, error responses included. Tenant
resolution (`SharedKernel.MultiTenancy`), when used, goes in the `BeforeAuthorization` hook:
`app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()))`. A middleware that must run inside it goes in a hook:

| Hook | Position | Typical middleware |
| --- | --- | --- |
| `AtStart` | Before everything else of this pipeline (after the request context) | `UseForwardedHeaders()`, so HSTS, the scheme and the client address are right |
| `BeforeAuthentication` | After routing and CORS | `UseCertificateForwarding()` |
| `BeforeAuthorization` | After authentication, before rate limiting and authorization | `UseRequestLocalization()`, so 401, 403 and 429 bodies are translated |

Hooks run in the order they were added. An exception thrown in an `AtStart` hook is not turned into a problem.

Startup behaviour:

- Both calls are idempotent. Each `configure` of `AddSharedKernelWebApi()` is applied; a second
  `UseSharedKernelWebApi()` has no effect, hooks included.
- `UseSharedKernelWebApi()` without `AddSharedKernelWebApi()` throws `InvalidOperationException`.
- `AddSharedKernelWebApi()` without `UseSharedKernelWebApi()` logs warning 14011 when the host starts.
- Invalid settings throw `OptionsValidationException` the first time they are read: with Kestrel at `builder.Build()`,
  with `TestServer` at `UseSharedKernelWebApi()`, and at the latest when the host starts. The message names the key.

## Endpoint modules

Group the endpoints of a resource or feature in a module: a type that implements `IEndpointModule` with one static
`Map`. The group, its requirements, its version and its tags are declared inside it.

```csharp
public sealed class InvoiceEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var invoices = app.MapGroup("/invoices").WithTags("Invoices");

        invoices.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetInvoice(id), ct).ToOk());
    }
}

app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi();
app.MapEndpoints();
```

`MapEndpoints()` is generated at compile time by the source generator this package carries (`analyzers/dotnet/cs`):
an `internal` extension in the namespace `SharedKernel.Presentation.WebApi` that calls `Map` on every module of the
assembly, in the ordinal order of their full names. There is no reflection and no instance. It exists only in an
assembly that declares at least one module, and it maps that assembly's modules only.

The generator refuses a module it cannot call:

| Id | Severity | Module |
| --- | --- | --- |
| `SKEP001` | Error | Abstract |
| `SKEP002` | Error | Generic, or nested in a generic type |
| `SKEP003` | Error | Not reachable from the assembly: private, protected, private protected or file-local, or nested in such a type |
| `SKEP004` | Warning | Inherits `Map` from another module instead of declaring its own; it is not mapped a second time |

A module may implement `Map` explicitly (`static void IEndpointModule.Map(...)`); the generated code calls it through
the interface.

## From Result to HTTP

Every mapping returns a typed union, such as `Results<Ok<T>, ErrorHttpResult>`, so OpenAPI infers the success
response without annotations. `ErrorHttpResult` writes the error as a problem. The examples send `05.Application`
commands and queries with `sender.Send(…)`, which returns `Task<Result>` or `Task<Result<T>>`.

| Method | On `Result<T>` | On `Result` | Success response |
| --- | --- | --- | --- |
| `ToOk()`, `ToOk(map)` | yes | — | 200 with the value, or `map(value)` |
| `ToOkWithETag(version)`, `ToOkWithETag(version, map)` | yes | — | 200 with `ETag`; 304 for a matching `GET` or `HEAD` ([ETag](#etag-if-none-match-and-if-match)) |
| `ToCreated(location)`, `ToCreated(location, map)` | yes | `ToCreated(location)` | 201 with `Location`; no body for `Result` |
| `ToAccepted(location?)` | yes | yes | 202, `Location` when given; no body for `Result` |
| `ToNoContent()` | yes (drops the value) | yes | 204 |
| `ToHttpResult(onSuccess)` | yes | yes | Any `IResult` you build, such as `TypedResults.File(…)` |
| `error.ToErrorResult()` | — | — | The problem for an `Error` |

On `Result<T>`, `location` and `version` are functions of the value (`order => $"/orders/{order.Id}"`); on `Result`,
`location` is a string. Every method also exists on `Task<Result<T>>` and `Task<Result>`, so a handler ends with
`sender.Send(command, ct).ToCreated(order => $"/orders/{order.Id}")`. Where a method takes a header value and a body
map, the header comes first: `ToCreated(location, map)`, `ToOkWithETag(version, map)`.

MVC controllers return the same typed results; there is no `ToActionResult`:

```csharp
[ApiController]
[Route("orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public Task<Results<OkWithETag<OrderResponse>, ErrorHttpResult>> Get(Guid id, CancellationToken ct) =>
        sender.Send(new GetOrder(id), ct).ToOkWithETag(order => order.Version.ToString(), OrderResponse.From);

    [HttpPost]
    [RequireIdempotencyKey]
    public Task<Results<Created<Guid>, ErrorHttpResult>> Place(PlaceOrderRequest body, CancellationToken ct) =>
        sender.Send(new PlaceOrder(body.Customer, body.Amount, HttpContext.GetIdempotencyKey()!), ct)
            .ToCreated(id => $"/orders/{id}");
}
```

A typed result is unit-tested on the result itself:

```csharp
[Fact]
public void Missing_order_is_a_not_found_problem()
{
    Result<Order> missing = Error.NotFound("order.not_found", "Order 42 was not found.");

    var result = missing.ToOkWithETag(order => order.Version.ToString(), OrderResponse.From);

    var error = Assert.IsType<ErrorHttpResult>(result.Result);
    Assert.Equal("order.not_found", error.Error.Code);
    Assert.Equal(404, error.StatusCode);
}
```

`ErrorHttpResult.StatusCode` is the status of the error type alone. One case depends on the request: a version
conflict of a conditional request is written as 412 ([the 412 rule](#the-412-rule)).

## The error shape

Every error response, whatever produced it, is `application/problem+json` with `Cache-Control: no-store`:

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

| Member | Value |
| --- | --- |
| `type` | The RFC section of the status (RFC 6585 for 428 and 429). With `Problems:TypeBaseUri`, that URI followed by the `errorCode` |
| `title` | The framework's reason phrase for the status, never the code |
| `status` | The HTTP status |
| `detail` | The client message: the error's message, translated when a catalog has it; for a server error outside Development, a generic sentence |
| `instance` | The request path |
| `errorCode` | `Error.Code`; `http.{status}` for a response the framework produced without an error, such as an unmatched route |
| `correlationId` | The request's correlation id, also in the `X-Correlation-Id` response header |
| `traceId` | `Activity.Current.Id` (the W3C `traceparent` form) when the request is traced, else the request id |
| `errors`, `errorCodes` | For field errors (`Error.Details`, a thrown `ValidationException`): the field path, or the error code when an error names no field, mapped to its messages and to their codes, in the same order |
| `exception` | `type`, `message` and `stackTrace` of an unhandled exception, only in Development or with `Problems:IncludeExceptionDetails` |

The member names are constants on `ProblemDetailsExtensionNames`; `SharedKernel.Communication.Rest` reads them back
into a `Result<T>`.

### Status codes

| `ErrorType` | HTTP status |
| --- | --- |
| `Validation` | 400 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409, or 412 by [the 412 rule](#the-412-rule) |
| `BusinessRule` | 422 |
| `Unexpected` | 500 |
| `Unavailable` | 503, with `Retry-After` when `Problems:UnavailableRetryAfter` is set (whole seconds, rounded up) |
| `Timeout` | 504 |
| `None` or any other value | 500 |

The mapping is internal to the presentation packages, so HTTP, SignalR and gRPC present an error identically. Code
that writes a response itself returns `error.ToErrorResult()`, which applies this table and the 412 rule.

### Where each failure ends

| Source | Status | `errorCode` |
| --- | --- | --- |
| A failed `Result` returned through a typed result | From the table | `Error.Code` |
| A thrown `SharedKernelException` | From the table | `Error.Code` |
| A thrown `ValidationException` | 400 | The one error's code, or `validation.failed` with `errors`: the body a returned error gets |
| A minimal API that cannot bind a parameter or read the JSON body | 400 | `validation.invalid_format`, under the field's JSON path when known; the text never names a .NET type |
| MVC `[ApiController]` model validation | 400 | `validation.failed`; each field `validation.invalid_value` (the attribute's or binding rule's message) or `validation.invalid_format` (a value that could not be read) |
| Another `BadHttpRequestException` | Its status | `request.too_large` for 413, else `http.{status}` |
| `TimeoutException`, or an `OperationCanceledException` the client did not cause | 504 | `timeout.default` |
| Any exception after the client went away (`RequestAborted` cancelled) | 499, no body | — |
| Any other exception | 500 | `unexpected.exception` |
| A framework status without a body: unmatched route, wrong method, unsupported media type | Its status | `http.404`, `http.405`, `http.415`, … |
| Authorization, required headers, rate limiting, CORS | See the sections below | See [Error codes](#error-codes) |

A gRPC call (`Content-Type: application/grpc…`) gets the status without a body; the gRPC package carries its errors
in the gRPC status. A request whose `Accept` excludes JSON still gets `application/problem+json`.

### Messages: translation and redaction

- A server error (`Unexpected`, `Unavailable`, `Timeout`, or any type whose status is 500 or above) keeps its `errorCode`, but outside Development its `detail` is a generic sentence: "An unexpected
  error occurred.", "The service is temporarily unavailable. Try again later." or "The operation did not complete in
  time." Such messages describe internals, like a host name or a query. Server errors are logged at Error, client
  errors at Debug.
- With an `ILocalizationCatalog` registered (`SharedKernel.Localization`), `detail` and every `errors` message are
  translated into the request culture (request localization's, else `CultureInfo.CurrentUICulture`) and filled with
  `Error.MessageArguments`. An untranslated error keeps its own message. The package's own messages are translated
  under their codes, and the generic sentences under `unexpected.exception`, `unavailable.default` and
  `timeout.default`.
- `errors` and `errorCodes` are keyed by each error's `ErrorArgumentNames.PropertyPath` argument, which FluentValidation
  failures carry through 05.Application's `ValidationBehavior`. An error without one is keyed by its code.

### Your own handling

- An `IExceptionHandler` the service registers runs first; what it leaves gets the platform shape. A service that sets
  `ExceptionHandlerOptions.ExceptionHandler` or `ExceptionHandlingPath` itself replaces the platform's handling.
- A `CustomizeProblemDetails` the service configures runs after the platform's, free to add members.
- To write an error yourself, return `error.ToErrorResult()`. `error.ToProblemDetails(httpContext)` builds the body for
  a custom writer; write it through `IProblemDetailsService`.

## Authorization

**Permissions go on the use case.** A command or query declares `[RequirePermission]` (`05.Application`) and the
pipeline checks it on every path, HTTP or not; an endpoint that sends it does not repeat it.
`[RequireEndpointPermission]` is only for what sends no command — SignalR hubs, gRPC services and methods, endpoints
that do not call `ISender`. The step-up requirements (`RequireFreshAuthentication`, `RequireAuthenticationMethod`)
stay on the endpoint, since only the HTTP request knows how the caller signed in. Both layers answer 401
`unauthorized.default` and 403 `forbidden.insufficient_permission`.

The attributes and conventions are `SharedKernel.Presentation.Core`'s, shared with SignalR and gRPC:
`using SharedKernel.Presentation.Authorization;` (P-579; they were in this package's namespace before). This package
registers them and writes the problem body of every refusal.

| Requirement | Attribute | Endpoint convention | A signed-in caller who fails it |
| --- | --- | --- | --- |
| Any of the permissions | `[RequireEndpointPermission("orders.write", "orders.admin")]` | `.RequireEndpointPermission(…)` | 403 `forbidden.insufficient_permission` |
| Any of the roles | `[RequireRole("support")]` | `.RequireRole(…)` | 403 `forbidden.insufficient_permission` |
| Signed in at most N seconds ago | `[RequireFreshAuthentication(300)]` | `.RequireFreshAuthentication(300)`, or a `TimeSpan` | 401 `unauthorized.step_up_required` |
| Authenticated with any of the methods (`amr`), optionally within a maximum age | `[RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]` | `.RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp")` | 401 `unauthorized.step_up_required` |

An anonymous caller is answered 401 `unauthorized.default` by every requirement.

```csharp
using SharedKernel.Presentation.Authorization;

var admin = app.MapGroup("/admin").RequireRole("support");        // every endpoint of the group
admin.MapPost("/payouts", () => TypedResults.Accepted("/admin/payouts/1"))
    .RequireEndpointPermission("payouts.write", "payouts.admin")             // either permission
    .RequireFreshAuthentication(300);                                // and signed in in the last 5 minutes
```

- The attributes derive from `[Authorize]` and become native ASP.NET Core policies: they work on minimal-API handlers
  and groups, MVC controllers and actions, `MapControllers()`, SignalR hubs, hub methods and `MapHub<T>()`, gRPC
  services, methods and `MapGrpcService<T>()`. Nothing else needs registering.
- Values within one attribute are alternatives (OR); several attributes must all hold (AND).
- They are evaluated against the caller's `IUserContext` (`HasPermission`, `HasRole`, `IsAuthenticationFresherThan`,
  `WasAuthenticatedWith`, `GetAuthenticationMethodTime`), with ordinal comparison and the injected `IClock`. An
  `IClock` is registered when none is.
- `Policy` and `Roles` are read-only on these attributes, so the requirement cannot be replaced; `AuthenticationSchemes`
  can be set as on `[Authorize]`. Values must not be empty or contain `|`.

| Case | Response |
| --- | --- |
| Not signed in | The scheme's own challenge (its `WWW-Authenticate` is kept) and a 401 problem. Without any authentication scheme: 401 with `WWW-Authenticate: Bearer`. A scheme that redirects (cookies) keeps its redirect |
| Signed in, only freshness or authentication-method requirements unmet | 401 with an RFC 9470 challenge, for example `Bearer error="insufficient_user_authentication", error_description="More recent authentication is required", max_age="300"`. `max_age` is the smallest maximum age among the unmet requirements; `DPoP` replaces `Bearer` when the request used DPoP |
| Signed in, any other requirement unmet | 403; the message never names the permission or role |
| A gRPC call | The same status and headers, no body |

Every refusal is logged at Warning (14002) with the endpoint and the code, never with principal data.

- **Every authentication scheme needs an `IUserContextMapper`.** The SharedKernel OIDC, API key and mTLS packages
  register theirs; a custom or test scheme must register one. A signed-in principal no mapper understands is refused
  with 403 and logged at Warning (14009); at startup, every scheme without a mapper is named in a warning (14010).
- A service's own `IAuthorizationPolicyProvider` or `IAuthorizationMiddlewareResultHandler` registered **before**
  `AddSharedKernelWebApi()` is decorated and keeps working. One registered **after** it would replace the platform's,
  so the host refuses to start with an `InvalidOperationException` naming it.
- `[AllowAnonymous]` switches off every requirement of the endpoint, these included (ASP.NET Core semantics).
- `AddSharedKernelWebApi()`, `AddSharedKernelSignalR()` and `AddSharedKernelGrpc()` each register the authorization
  policies; there is nothing else to call.
- On long-lived connections a step-up needs a maximum age: a SignalR hub method is authorized at every invocation
  against the principal the connection opened with, and a gRPC stream is authorized once, when it starts. See the
  SignalR and gRPC package READMEs.

## Idempotency-Key

| Declare the header as | Required | Accepted (optional) |
| --- | --- | --- |
| Minimal-API parameter | `IdempotencyKey key` | `IdempotencyKey? key` |
| Endpoint convention | `.RequireIdempotencyKey()` | `.AcceptIdempotencyKey()` |
| Attribute on an MVC action or controller, or a handler | `[RequireIdempotencyKey]` | `[AcceptIdempotencyKey]` |

| The request sends | Required | Accepted |
| --- | --- | --- |
| No header (or a blank one) | 400 `idempotency.key_required` | Passes; the parameter is `null` |
| 1 to 256 visible ASCII characters (0x21–0x7E), optionally in one pair of double quotes | Passes; the key without the quotes | Passes |
| Anything else: too long, spaces, `""`, two header values | 400 `idempotency.key_invalid` | 400 `idempotency.key_invalid` |

- `UseSharedKernelWebApi()` checks the header after authorization, before the endpoint runs, identically for every
  kind of endpoint. An anonymous caller is told to authenticate (401) first. When an endpoint declares both, the
  requirement wins.
- The package checks the header only. Deduplication happens where the key is used: pass it to a command implementing
  05.Application's `IIdempotentRequest`, whose pipeline reserves it per tenant and caller.
- `HttpContext.GetIdempotencyKey()` returns the validated key or `null`. On an endpoint that declares the header,
  `null` means none was sent; elsewhere it also covers an invalid key, so declare the header instead of reading it raw.
- `new IdempotencyKey("order-17")` builds one in a unit test; an invalid value throws `ArgumentException`.
  `IdempotencyKey.MaxLength` is 256.
- A rejection is logged at Warning (14003) without the value.

## ETag, If-None-Match and If-Match

### ETag and 304

`ToOkWithETag(version)` answers 200 with `ETag: "<version>"`, a strong entity tag. The version is any text of visible
ASCII except `"`; with 06.Persistence it is `EntityVersion.ToString()`, an opaque token, never the row version itself.

- A `GET` or `HEAD` whose `If-None-Match` matches the version (weak comparison, RFC 9110 section 13.1.2), or is `*`,
  is answered 304 with the `ETag` and no body. Any other method always gets the 200 body.
- OpenAPI documents the 304 only for an endpoint that answers `GET` or `HEAD`.
- `HttpContext.Response.SetETag(version)` sets the header on any other response.
- A response with an `ETag` gets no default `Cache-Control`, so clients can revalidate it.

### If-Match

| Declare the header as | Required | Accepted (optional) |
| --- | --- | --- |
| Minimal-API parameter | `IfMatch<TVersion> ifMatch` | `IfMatch<TVersion>? ifMatch` |
| Endpoint convention | `.RequireIfMatch()` | `.AcceptIfMatch()` |
| Attribute on an MVC action or controller, or a handler | `[RequireIfMatch]` | `[AcceptIfMatch]` |

`TVersion` is any `IParsable<TVersion>`, typically `EntityVersion`. The handler receives the parsed version:

```csharp
orders.MapPut("/{id:guid}/address", (Guid id, ChangeAddressRequest body, IfMatch<EntityVersion> ifMatch, ISender sender, CancellationToken ct) =>
    sender.Send(new ChangeAddress(id, body.Street, ifMatch.Version), ct).ToNoContent());
```

| `If-Match` sent (RFC 9110 section 13.1.1) | Required | Accepted |
| --- | --- | --- |
| None (or blank) | 428 `precondition.required` | Passes, unconditional; the parameter is `null` |
| `*` | 428 `precondition.required` | 400 `precondition.invalid` |
| Malformed, or more than one entity tag | 400 `precondition.invalid` | 400 `precondition.invalid` |
| A weak tag, `W/"7"` (`If-Match` compares strongly, so it never matches) | 412 `precondition.failed` | 412 `precondition.failed` |
| A strong tag that does not parse as `TVersion` (parameters only) | 412 `precondition.failed` | 412 `precondition.failed` |
| One strong tag | Passes | Passes |

An accepted header is validated when it is sent and never read as missing: that would turn the client's conditional
request into an unconditional one, and `*` would let a replace-only write create.

- `HttpContext.GetIfMatch()` returns the one strong tag without quotes, `*`, or `null`.
- MVC has no binder for `IfMatch<TVersion>`: the attributes check the header, and the action parses it, for example
  with `EntityVersion.TryParse(HttpContext.GetIfMatch(), out var version)`.
- `new IfMatch<EntityVersion>(version)` builds one in a unit test.

### The 412 rule

A `Conflict` error whose code is in `Problems:PreconditionFailedErrorCodes`, in a request carrying `If-Match` or
`If-None-Match`, is answered **412 Precondition Failed**, keeping its code: the version the client named is not
current. This holds on any endpoint, whether the error is returned or thrown. Every other conflict stays 409, whatever
headers the request carries.

| Default code | Owner | Meaning |
| --- | --- | --- |
| `persistence.concurrency_conflict` | 06.Persistence | A stale `EntityVersion` |
| `storage.precondition_failed` | 08.Storage | A stale object ETag |
| `storage.already_exists` | 08.Storage | A create-only write (`If-None-Match: *`) found the object |

Add the codes of your own version conflicts to the list. The 412 carries no current `ETag`: the client reads the
resource again.

## Paging

Declare the query parameters of a list as a handler parameter; the handler receives a validated `04.Contracts`
request:

```csharp
invoices.MapGet("/", (Paging paging, ISender sender, CancellationToken ct) =>
    sender.Send(new ListInvoices(paging.Request), ct).ToOk());            // page, pageSize → PageRequest

invoices.MapGet("/browse", (CursorPaging paging, ISender sender, CancellationToken ct) =>
    sender.Send(new BrowseInvoices(paging.Request), ct).ToOk());          // cursor, limit → CursorPageRequest
```

| Parameter | Query | Absent | Invalid (400 `validation.failed`, keyed by the parameter) |
| --- | --- | --- | --- |
| `Paging` | `page`, `pageSize` | page 1 of 20 (`PageRequest.First`) | out of range: `pagination.page.out_of_range`, `pagination.page_size.out_of_range` (at most 1000); not one whole number or sent twice: `validation.invalid_format` |
| `CursorPaging` | `cursor`, `limit` | the first page of 20 (`CursorPageRequest.First`) | a blank cursor or one above 512 characters: `pagination.cursor.invalid`; limit out of range: `pagination.limit.out_of_range`; not one whole number or sent twice: `validation.invalid_format` |

- `UseSharedKernelWebApi()` checks them after authorization, before the handler, like the headers; the OpenApi add-on
  documents both parameters and the 400.
- The cursor stays opaque here; decode it with `PageCursor.Decode<TKey, TId>` where the key types are known (06.Persistence's
  `ListKeysetAsync` takes the request directly).
- Minimal APIs only. An MVC action binds the values itself and calls `PageRequest.Create`/`CursorPageRequest.Create`.
  In a unit test, construct one: `new Paging(PageRequest.First)`.

## Correlation ids

The correlation id is resolved by `SharedKernel.ServiceDefaults.Security`'s `UseSharedKernelRequestContext()`, not by
this package (P-579); this package reads it from the request's `RequestContextScope`.

- A valid inbound `X-Correlation-Id` is kept: at most 128 characters of `[A-Za-z0-9-_:.]` (`CorrelationIds.IsValid`,
  the one rule for every protocol). Otherwise the request gets a new id, `CorrelationIds.New()` (a GUID), never the
  trace id; the rejected value is logged by length only (13007).
- The id is written to the `X-Correlation-Id` header of every response, errors included, to the `correlationId` of
  every problem, and to `Activity` baggage (`correlation.id`), from where logs and outgoing calls read it. Read it with
  `HttpContext.GetCorrelationId()`, or anywhere with an injected `IRequestContext` (`CorrelationId`); in a gRPC method
  the same way, in a hub with `Context.GetCorrelationId()`. Without the request context middleware it is `null` and
  problems omit the member.
- **Inbound baggage is refused** by the same middleware: hosting reads no baggage from the request, and any item that
  still reached the request `Activity` is removed before the correlation id is added. Set
  `AddSharedKernelRequestContext(o => o.TrustInboundBaggage = true)` only behind a gateway that removes caller-supplied
  baggage. `SharedKernelWebApiOptions.CorrelationId` and `.TrustInboundBaggage` no longer exist.

## Security headers, CORS and request limits

### Security headers

| Header | Default | Setting (`SecurityHeaders:`) |
| --- | --- | --- |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains`, over HTTPS only, never to `localhost`, never in Development | `Hsts`, `HstsMaxAge`, `HstsIncludeSubDomains`, `HstsPreload` |
| `X-Content-Type-Options` | `nosniff` | `ContentTypeOptions` |
| `X-Frame-Options` | `DENY` | `FrameOptions` |
| `Referrer-Policy` | `no-referrer` | `ReferrerPolicy` |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` | `PermissionsPolicy` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | `ContentSecurityPolicy`; per endpoint `WithContentSecurityPolicy(policy)` |
| `Cache-Control` | `no-store`, on a response that sets neither `Cache-Control` nor `ETag` | `CacheControl` |
| `Server` | not sent | `RemoveServerHeader` (top level) |

- A header an endpoint set itself is never overwritten; a `null` or empty setting turns a header off;
  `SecurityHeaders:Enabled = false` turns them all off, HSTS included.
- Error responses carry the headers too: HSTS runs before the exception handler and is written again after the handler
  clears the response.
- Behind a proxy that terminates TLS, add `UseForwardedHeaders()` in the `AtStart` hook, or no request looks like HTTPS
  and HSTS is never sent.
- The default policy lets a browser load nothing from a response. An endpoint that serves a page sets its own policy,
  or none: `.WithContentSecurityPolicy("default-src 'self'")` or `.WithContentSecurityPolicy(null)`. Static files and
  Razor pages cannot use the convention; change `SecurityHeaders:ContentSecurityPolicy` for them.

### CORS

- Deny by default: no policy exists until `Cors:AllowedOrigins` lists an origin. The one policy applies to every
  endpoint, hubs included.
- Methods and headers are open unless `Cors:AllowedMethods` or `Cors:AllowedHeaders` list them. Browser scripts may
  read `X-Correlation-Id`, `ETag`, `Location`, `Retry-After`, `Sunset`, `Deprecation`, `Link`,
  `api-supported-versions` and `api-deprecated-versions`.
- Startup validation refuses `AllowCredentials` with no origin or with `*`; the origin `null`; and, outside
  Development, `AllowCredentials` with an `http://` origin. Each of these failures is also logged at Critical (14004).
- Browsers apply no CORS to WebSockets, so a WebSocket request from an origin the policy does not allow (a SignalR
  hub's WebSocket transport, for example) is refused with 403 `forbidden.origin_not_allowed` and logged at Warning
  (14013). A request without `Origin` does not come from a browser page and passes.

### Request limits

- `Limits:MaxRequestBodySize` (4 MiB) is Kestrel's body limit; a larger body is answered 413 `request.too_large`. An
  in-memory `TestServer` (`WebApplicationFactory`) enforces no limit, so test limits against Kestrel.
- Per endpoint: `.WithRequestSizeLimit(bytes)` and `.DisableRequestSizeLimit()`; MVC actions use `[RequestSizeLimit]`
  and `[DisableRequestSizeLimit]`. For large files, prefer a presigned upload straight to storage (08.Storage).
- `Limits:MaxJsonDepth` sets System.Text.Json's `MaxDepth` for minimal APIs and MVC. It limits responses too, so set it
  no lower than your deepest response. Unset, the framework's limit (64) applies.

## Rate limiting

The package has no limiter of its own. Register one with ASP.NET Core's `AddRateLimiter()` or 13.ServiceDefaults'
`AddSharedKernelRateLimiting()`:

- `UseSharedKernelWebApi()` adds `UseRateLimiter()` after authentication and before authorization, so a policy can
  partition by the caller and refused requests still count against the limit.
- When `RateLimiterOptions.OnRejected` is not set, a rejection is a 429 problem `rate_limit.exceeded`, with
  `Retry-After` in whole seconds when the limiter suggests a delay, logged at Warning (14005). An `OnRejected` the
  service sets is kept.

## Error codes

The codes this package produces are constants on `PresentationErrorCodes`; the platform's are on `ErrorCodes`
(01.Core), the paging ones on `PaginationErrorCodes` (04.Contracts).

| Code | Status | When |
| --- | --- | --- |
| `request.too_large` | 413 | The body is larger than the endpoint accepts |
| `idempotency.key_required` | 400 | A required `Idempotency-Key` is missing (`ErrorCodes.Idempotency.KeyRequired`, also the pipeline's code for a blank key) |
| `idempotency.key_invalid` | 400 | An `Idempotency-Key` is not 1 to 256 visible ASCII characters (`ErrorCodes.Idempotency.KeyInvalid`) |
| `pagination.page.out_of_range`, `pagination.page_size.out_of_range`, `pagination.cursor.invalid`, `pagination.limit.out_of_range` | 400 (in `errorCodes`) | A paging parameter out of range ([Paging](#paging)) |
| `precondition.required` | 428 | A required `If-Match` is missing or `*` |
| `precondition.invalid` | 400 | An `If-Match` is malformed or names several tags; `*` where the header is only accepted |
| `precondition.failed` | 412 | An `If-Match` tag is weak or not a version of the resource |
| `validation.invalid_value` | 400 (in `errorCodes`) | MVC model validation refused a value |
| `forbidden.origin_not_allowed` | 403 | A WebSocket request from an origin the CORS policy does not allow |
| `rate_limit.exceeded` | 429 | Rate limiting refused the request |
| `unauthorized.step_up_required` | 401 | A more recent or stronger sign-in is required |
| `http.{status}` | Any | A response the framework produced without an error, such as `http.404` for an unmatched route |
| `unauthorized.default` | 401 | Not signed in (`ErrorCodes.Unauthorized.Default`) |
| `forbidden.insufficient_permission` | 403 | Missing permission or role (`ErrorCodes.Forbidden.InsufficientPermission`) |
| `validation.failed` | 400 | Several validation errors (`ErrorCodes.Validation.Failed`) |
| `validation.invalid_format` | 400 | A value or body that could not be read (`ErrorCodes.Validation.InvalidFormat`) |
| `timeout.default` | 504 | A `TimeoutException`, or a cancellation the client did not cause (`ErrorCodes.Timeout.Default`) |
| `unexpected.exception` | 500 | An exception that is not a `SharedKernelException` (`ErrorCodes.Unexpected.Default`) |

## Configuration

Every setting binds from `SharedKernel:Presentation:WebApi` and is validated before the service handles a request:
`Cors`, `SecurityHeaders`, `Limits`, `Problems` and `RemoveServerHeader`. The correlation id and inbound baggage are
configured on `AddSharedKernelRequestContext()` (`SharedKernel.ServiceDefaults.Security`) since P-579. Keys,
types, defaults and rules are in
[CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/CONFIGURATION.md#sharedkernelpresentationwebapi).

```json
{
  "SharedKernel": {
    "Presentation": {
      "WebApi": {
        "Cors": { "AllowedOrigins": [ "https://app.example.com" ] },
        "Limits": { "MaxRequestBodySize": 8388608 },
        "Problems": { "UnavailableRetryAfter": "00:00:30" }
      }
    }
  }
}
```

## Logging

EventIds 14000–14099. 14000 and 14006 (the former correlation-id middleware's) are retired; the request context logs
those events as 13006 and 13007.

| EventId | Level | Event |
| --- | --- | --- |
| 14001 | Error | A request failed with a server error (5xx), with the exception |
| 14002 | Warning | Authorization refused a request (endpoint, code) — emitted by `SharedKernel.Presentation.Core` since P-579 |
| 14003 | Warning | A request was refused for its `Idempotency-Key` (endpoint, code; never the key) |
| 14004 | Critical | CORS settings failed startup validation |
| 14005 | Warning | Rate limiting rejected a request |
| 14007 | Debug | An exception was answered with a client error (4xx) |
| 14008 | Debug | The client closed the request before it completed (499) |
| 14009 | Warning | A signed-in caller was refused: no `IUserContextMapper` handles its authentication type — `SharedKernel.Presentation.Core` |
| 14010 | Warning | At startup: an authentication scheme has no `IUserContextMapper` — `SharedKernel.Presentation.Core` |
| 14011 | Warning | At startup: `AddSharedKernelWebApi()` ran but `UseSharedKernelWebApi()` did not |
| 14012 | Warning | At startup: exception details are enabled outside Development |
| 14013 | Warning | A WebSocket request from a disallowed origin was refused |

## Pitfalls

- **Forgetting `UseSharedKernelWebApi()`.** Typed results still write problems, so a happy-path test passes, but
  thrown exceptions, security headers, CORS and the header checks are missing. The host logs warning
  14011.
- **Forgetting `UseSharedKernelRequestContext()`.** Problems carry no `correlationId`, no `X-Correlation-Id` is echoed,
  the caller's baggage is not refused, and code reading `IRequestContext` sees no scope. Add it first.
- **Middleware in front of it.** Middleware added between the two calls runs outside the exception handler; use a hook.
- **An authentication scheme without a mapper.** Every signed-in caller gets 403 while the token carries the
  permission. Look for 14009 and 14010, and register the scheme's `IUserContextMapper`, test schemes included.
- **Reading headers raw.** `GetIdempotencyKey()` and `GetIfMatch()` also return `null` for an invalid header on an
  endpoint that does not declare it. Declare the header.
- **Unit tests and 412.** `ErrorHttpResult.StatusCode` says 409 for a version conflict the wire answers with 412.
- **Body limits in tests.** `TestServer` enforces none.
- **`MaxJsonDepth`** limits responses as well as requests.
- **Pages and the default CSP.** An HTML page served by the API cannot run scripts until it gets its own policy.
- **List settings with defaults** (`Cors:ExposedHeaders`, `Problems:PreconditionFailedErrorCodes`): configured values
  are added to the defaults. Clear the list in the `configure` callback to replace them.
- **Hand-made responses.** Never branch on `IsSuccess` to build a response, and never construct `ProblemDetails`
  outside this package; 00.Governance's architecture rules flag both.

## Not in this package

| Looking for | Use instead |
| --- | --- |
| The `.Errors`, `.Http`, `.Idempotency` and `.Options` namespaces | `SharedKernel.Presentation.WebApi` |
| `ErrorPresentation`, `ErrorTypeStatusCodeMap`, `PresentationErrorCodes.ForStatus`, the endpoint-metadata interfaces | Internal to the presentation packages; `error.ToErrorResult()` or `error.ToProblemDetails(httpContext)` |
| `services.AddSharedKernelAuthorization()` | Nothing: `AddSharedKernelWebApi()`, `AddSharedKernelSignalR()` and `AddSharedKernelGrpc()` register it |
| `GetIfMatchTags()` | `GetIfMatch()`, or an `IfMatch<TVersion>` parameter |
| `SharedKernelWebApiOptions.CorrelationId`, `.TrustInboundBaggage`, `WebApiCorrelationIdOptions`, the correlation-id middleware (removed by P-579) | `SharedKernel.ServiceDefaults.Security`: `AddSharedKernelRequestContext(o => o.TrustInboundBaggage = …)` and `UseSharedKernelRequestContext()` |
| The authorization attributes in `SharedKernel.Presentation.WebApi` | `SharedKernel.Presentation.Authorization` (`SharedKernel.Presentation.Core`, P-579) |
| Hand-written `MapXxxEndpoints()` extension methods | An `IEndpointModule`, mapped by `app.MapEndpoints()` |
| Binding `page`/`pageSize` yourself | A `Paging` or `CursorPaging` parameter |
| `ToActionResult` (MVC) | The same typed results (`ToOk()`, `ToCreated()`, …) returned from the action |
| A type removed by P-562 (`ToProblemDetailsResult`, `AddSharedKernelAuthorizationFilters`, `AddSharedKernelIdempotencyFilters`, `RowVersionETag`, `UseSharedKernelSecurityHeaders`, `AddSharedKernelCors`, `AddSharedKernelPayloadLimits`, `RequireValidatedUpload`, `RateLimitRejectionProblemDetails`, `AddSharedKernelCorrelationId`, `ValidationProblemDetailsExtensions`, …) | The one-call setup, the settings and the parameter types above; each replacement is listed in `14.Presentation/CLAUDE.history.md` |
| API versioning, OpenAPI, Scalar, `Sunset` and `Deprecation` headers | `SharedKernel.Presentation.OpenApi` |
| .NET 10 `AddValidation()` | Validation in the application pipeline (05.Application's `ValidationBehavior`) |
