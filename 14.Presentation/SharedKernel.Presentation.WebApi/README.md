# SharedKernel.Presentation.WebApi

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The HTTP boundary of a SharedKernel service: one call to register it, one to add it to the pipeline, and one
> RFC 9457 `application/problem+json` error shape for every failure — with security headers, CORS, request limits and
> the `Idempotency-Key`, `ETag` and `If-Match` headers handled at the edge.**

| You get | So that |
| --- | --- |
| `builder.AddSharedKernelWebApi()` + `app.UseSharedKernelWebApi()` | The whole HTTP pipeline, in a fixed and reviewed order, from two calls |
| Typed results: `ToOk`, `ToOkWithETag`, `ToCreated`, `ToAccepted`, `ToNoContent`, `ToHttpResult` | An endpoint is one line: `sender.Send(command, ct).ToCreated(...)`, and OpenAPI infers the success type |
| One problem shape for every error source, with `errorCode`, `traceId`, `correlationId`, `errors`, `errorCodes` | Clients read every failure the same way; server errors are redacted outside Development |
| `IEndpointModule` + the generated `app.MapEndpoints()` | Endpoints are grouped per feature and mapped at compile time — no reflection |
| `Paging` / `CursorPaging` parameters | List endpoints receive a validated `PageRequest` / `CursorPageRequest`, or answer 400 |
| `IdempotencyKey` and `IfMatch<TVersion>` parameters, `ToOkWithETag` | Duplicate-submission keys and optimistic concurrency are checked before the handler; stale writes answer 412 |
| Security headers, deny-by-default CORS, body and JSON-depth limits | Secure defaults without per-service boilerplate |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Presentation.WebApi" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** project |
| Depends on | [`SharedKernel.Presentation.Core`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/SharedKernel.Presentation.Core/README.md), `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Configuration`, `SharedKernel.Contracts` (paging); no third-party packages |
| Namespaces | `SharedKernel.Presentation.WebApi` (every public type); the authorization attributes are in `SharedKernel.Presentation.Authorization` |
| Ships with it | The endpoint-module source generator, under `analyzers/dotnet/cs` |

It does not reference MediatR or `05.Application`: any code that returns `Result`/`Result<T>` maps the same way. The
[OpenAPI add-on](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/SharedKernel.Presentation.OpenApi/README.md)
and [SignalR](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/SharedKernel.Presentation.SignalR/README.md)
build on it.

## Quick start

```csharp
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();   // the request context and correlation id
builder.AddSharedKernelWebApi();                    // SharedKernel:Presentation:WebApi

var app = builder.Build();

app.UseSharedKernelRequestContext();                // first: its scope wraps everything below
app.UseSharedKernelWebApi();
app.MapEndpoints();                                 // every IEndpointModule of this assembly

app.Run();
```

```csharp
using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi;

public sealed class InvoiceEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var invoices = app.MapGroup("/invoices").WithTags("Invoices");

        invoices.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetInvoice(id), ct).ToOk());

        invoices.MapPost("/", (CreateInvoiceRequest body, IdempotencyKey key, ISender sender, CancellationToken ct) =>
            sender.Send(new CreateInvoice(body.Customer, body.Amount, key.Value), ct)
                .ToCreated(id => $"/invoices/{id}"));
    }
}
```

`samples/OrderApi` is the compiled reference for this setup.

## How it works

### The pipeline

`AddSharedKernelWebApi()` binds `SharedKernel:Presentation:WebApi`, runs `configure` after binding, validates, and
registers: problem details for every error source and the platform's exception handling; the authorization policies of
`Presentation.Core`; a CORS policy (only when origins are listed); the 429 body for rate limiting without an
`OnRejected`; Kestrel without the `Server` header and with the body limit; the JSON depth limit; HSTS settings;
`RouteHandlerOptions.ThrowOnBadRequest` and MVC's model-state response, so binding failures get the platform shape.

`UseSharedKernelWebApi()` adds, in order:

1. the `AtStart` hooks;
2. HSTS (outside Development), the security headers and the default `Cache-Control`;
3. the exception handler, then problem bodies for bodiless error statuses;
4. `UseRouting()`, CORS and the WebSocket origin check (when origins are configured);
5. the `BeforeAuthentication` hooks, then `UseAuthentication()` (when registered);
6. the `BeforeAuthorization` hooks, then `UseRateLimiter()` (when registered);
7. `UseAuthorization()`, then the endpoint's `Idempotency-Key`, `If-Match` and paging checks.

| Hook | Position | Typical middleware |
| --- | --- | --- |
| `AtStart` | Before everything else of this pipeline | `UseForwardedHeaders()`, `MtlsForwardedHeaderMiddleware` |
| `BeforeAuthentication` | After routing and CORS | `UseCertificateForwarding()` |
| `BeforeAuthorization` | After authentication, before rate limiting and authorization | `TenantResolutionMiddleware`, `UseRequestLocalization()` |

Both calls are idempotent. `UseSharedKernelWebApi()` without `AddSharedKernelWebApi()` throws; the reverse logs
warning 14011 at startup. Invalid settings throw `OptionsValidationException` naming the key, at the latest when the
host starts.

### Endpoint modules

`MapEndpoints()` is generated at compile time: an `internal` extension in `SharedKernel.Presentation.WebApi` that calls
`Map` on every `IEndpointModule` of the assembly, in ordinal order of their full names. It exists only in an assembly
that declares a module, and maps that assembly's modules only. A module may implement `Map` explicitly. The generator
refuses a module it cannot call:

| Id | Severity | Module |
| --- | --- | --- |
| `SKEP001` | Error | Abstract |
| `SKEP002` | Error | Generic, or nested in a generic type |
| `SKEP003` | Error | Not reachable from the assembly (private, protected, file-local, or nested in such a type) |
| `SKEP004` | Warning | Inherits `Map` from another module instead of declaring its own; not mapped twice |

### From Result to HTTP

| Method | On `Result<T>` | On `Result` | Success response |
| --- | --- | --- | --- |
| `ToOk()`, `ToOk(map)` | yes | — | 200 with the value, or `map(value)` |
| `ToOkWithETag(version)`, `ToOkWithETag(version, map)` | yes | — | 200 with `ETag`; 304 for a matching `GET`/`HEAD` |
| `ToCreated(location)`, `ToCreated(location, map)` | yes | `ToCreated(location)` | 201 with `Location` |
| `ToAccepted(location?)` | yes | yes | 202 |
| `ToNoContent()` | yes | yes | 204 |
| `ToHttpResult(onSuccess)` | yes | yes | Any `IResult` you build |
| `error.ToErrorResult()` | — | — | The problem for an `Error` |

Each returns a typed union such as `Results<Ok<T>, ErrorHttpResult>` and exists on `Task<Result<T>>`/`Task<Result>`
too. On `Result<T>`, `location` and `version` are functions of the value. MVC actions return the same typed results.

### The error shape

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
  "errors": { "quantity": ["Quantity must be at least 1."] },
  "errorCodes": { "quantity": ["order.quantity_invalid"] }
}
```

- `type`: the status's RFC section, or `Problems:TypeBaseUri` + `errorCode`. `title`: the reason phrase, never the code.
  `detail`: the client message, translated through an `ILocalizationCatalog` when registered; for a server error
  (status ≥ 500) outside Development, a generic sentence. `errorCode`: `Error.Code`, or `http.{status}` for a
  framework response without an error. `exception`: only in Development or with `Problems:IncludeExceptionDetails`.
- Status per `ErrorType`: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409 (or 412, below),
  BusinessRule 422, Unexpected 500, Unavailable 503 (with `Retry-After` when `Problems:UnavailableRetryAfter` is set),
  Timeout 504, anything else 500. Every problem has `Cache-Control: no-store`.
- **The 412 rule.** A `Conflict` whose code is in `Problems:PreconditionFailedErrorCodes`, on a request carrying
  `If-Match` or `If-None-Match`, is 412 with its code kept (defaults: `persistence.concurrency_conflict`,
  `storage.precondition_failed`, `storage.already_exists`).

| Source | Status | `errorCode` |
| --- | --- | --- |
| A failed `Result` through a typed result, or a thrown `SharedKernelException` | From the table | `Error.Code` |
| A thrown `ValidationException` | 400 | The one error's code, or `validation.failed` with `errors` |
| A minimal API that cannot bind a parameter or read the body | 400 | `validation.invalid_format`, under the field's JSON path |
| MVC `[ApiController]` model validation | 400 | `validation.failed`; each field `validation.invalid_value` or `validation.invalid_format` |
| Another `BadHttpRequestException` | Its status | `request.too_large` for 413, else `http.{status}` |
| `TimeoutException`, or a cancellation the client did not cause | 504 | `timeout.default` |
| Any exception after the client went away | 499, no body | — |
| Any other exception | 500 | `unexpected.exception` |
| Unmatched route, wrong method, unsupported media type | Its status | `http.404`, `http.405`, `http.415` |

A gRPC call gets the status without a body. An `IExceptionHandler` the service registers runs first; a
`CustomizeProblemDetails` runs after the platform's.

### Authorization, headers and paging

- **Permissions go on the use case** (`[RequirePermission]`, `05.Application`). `[RequireEndpointPermission]`,
  `[RequireRole]`, `[RequireFreshAuthentication]` and `[RequireAuthenticationMethod]` (from `Presentation.Core`) are
  for endpoints that send no command and for step-up; this package writes the problem body of every refusal: 401
  `unauthorized.default`, 401 `unauthorized.step_up_required` with an RFC 9470 challenge, 403
  `forbidden.insufficient_permission`. An `IAuthorizationPolicyProvider` or `IAuthorizationMiddlewareResultHandler`
  registered before `AddSharedKernelWebApi()` is decorated; one registered after it stops the host.
- **`Idempotency-Key`** (`IdempotencyKey` / `IdempotencyKey?` parameter, `.RequireIdempotencyKey()` /
  `.AcceptIdempotencyKey()`, `[RequireIdempotencyKey]` / `[AcceptIdempotencyKey]`): 1 to 256 visible ASCII characters,
  optionally quoted. Missing when required → 400 `idempotency.key_required`; malformed → 400 `idempotency.key_invalid`.
  This package checks the header only; deduplication is `IIdempotentRequest` in the application pipeline.
- **`ETag` and `If-Match`.** `ToOkWithETag(version)` sends a strong `ETag`; a `GET`/`HEAD` whose `If-None-Match`
  matches gets 304. `IfMatch<TVersion>` (any `IParsable<TVersion>`, typically `EntityVersion`) with
  `.RequireIfMatch()`/`.AcceptIfMatch()` or the attributes:

  | `If-Match` sent | Required | Accepted |
  | --- | --- | --- |
  | None | 428 `precondition.required` | Passes; the parameter is `null` |
  | `*` | 428 `precondition.required` | 400 `precondition.invalid` |
  | Malformed, or several tags | 400 `precondition.invalid` | 400 `precondition.invalid` |
  | A weak tag, or a tag that does not parse as `TVersion` | 412 `precondition.failed` | 412 `precondition.failed` |
  | One strong tag | Passes | Passes |

- **Paging.** A `Paging` parameter reads `page`/`pageSize` (default page 1 of 20; `pageSize` at most 1000); a
  `CursorPaging` parameter reads `cursor`/`limit` (default first page of 20; cursor up to 512 characters). Invalid input
  answers 400 `validation.failed` with `pagination.*` codes. Minimal APIs only.
- All three checks run after authorization, before the handler, identically for every kind of endpoint.

### Security headers, CORS, limits, rate limiting

- Headers: HSTS (`max-age=31536000; includeSubDomains`, HTTPS only, never to `localhost` or in Development),
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
  `Permissions-Policy: geolocation=(), microphone=(), camera=()`,
  `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `Cache-Control: no-store` (when a response
  sets neither `Cache-Control` nor `ETag`), and no `Server` header. A header an endpoint set itself is never overwritten.
- CORS is deny by default; one policy applies to every endpoint, hubs included. Startup validation refuses
  `AllowCredentials` with no origin or `*`, the origin `null`, and (outside Development) credentials with an `http://`
  origin (logged at Critical, 14004). WebSockets from a disallowed origin get 403 `forbidden.origin_not_allowed`.
- `Limits:MaxRequestBodySize` is Kestrel's body limit (413 `request.too_large`); per endpoint
  `.WithRequestSizeLimit(bytes)` / `.DisableRequestSizeLimit()`. `Limits:MaxJsonDepth` limits requests and responses.
- The package has no limiter: register ASP.NET Core's or `AddSharedKernelRateLimiting()`. `UseRateLimiter()` runs
  after authentication and before authorization; a rejection without `OnRejected` is 429 `rate_limit.exceeded` with
  `Retry-After` (14005).

## Recipes

### 1. Conditional update with optimistic concurrency

```csharp
orders.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
    sender.Send(new GetOrder(id), ct).ToOkWithETag(order => order.Version.ToString(), OrderResponse.From));

orders.MapPut("/{id:guid}/address",
    (Guid id, ChangeAddressRequest body, IfMatch<EntityVersion> ifMatch, ISender sender, CancellationToken ct) =>
        sender.Send(new ChangeAddress(id, body.Street, ifMatch.Version), ct).ToNoContent());
```

A stale version fails with `persistence.concurrency_conflict`, which the 412 rule answers as 412.

### 2. A paged list

```csharp
invoices.MapGet("/", (Paging paging, ISender sender, CancellationToken ct) =>
    sender.Send(new ListInvoices(paging.Request), ct).ToOk());          // PageRequest

invoices.MapGet("/browse", (CursorPaging paging, ISender sender, CancellationToken ct) =>
    sender.Send(new BrowseInvoices(paging.Request), ct).ToOk());        // CursorPageRequest
```

### 3. Tenant resolution, localization and a proxy in front

```csharp
app.UseSharedKernelWebApi(pipeline => pipeline
    .AtStart(web => web.UseForwardedHeaders())
    .BeforeAuthorization(web =>
    {
        web.UseMiddleware<TenantResolutionMiddleware>();
        web.UseRequestLocalization();   // so 401, 403 and 429 bodies are translated
    }));
```

### 4. A page with its own content security policy

```csharp
app.MapGet("/status", () => TypedResults.Content(html, "text/html"))
    .WithContentSecurityPolicy("default-src 'self'");
```

## Configuration

Section `SharedKernel:Presentation:WebApi` (`SharedKernelWebApiOptions`), validated before the first request. The full
reference with rules is in
[CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/CONFIGURATION.md#sharedkernelpresentationwebapi).
`…` stands for `SharedKernel:Presentation:WebApi`.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `…:RemoveServerHeader` | `bool` | `true` | Omit Kestrel's `Server` header |
| `…:Cors:AllowedOrigins` | `string[]` | empty (no policy) | Allowed origins; `*` allows any |
| `…:Cors:AllowedMethods` / `AllowedHeaders` | `string[]` | empty (any) | Allowed request methods / headers |
| `…:Cors:ExposedHeaders` | `string[]` | `X-Correlation-Id`, `ETag`, `Location`, `Retry-After`, `Sunset`, `Deprecation`, `Link`, `api-supported-versions`, `api-deprecated-versions` | Readable by scripts; configured values are added |
| `…:Cors:AllowCredentials` | `bool` | `false` | Allow cookies and HTTP authentication; needs explicit origins |
| `…:Cors:PreflightMaxAge` | `TimeSpan` | `00:10:00` | Preflight cache duration |
| `…:SecurityHeaders:Enabled` | `bool` | `true` | `false` turns every security header off, HSTS included |
| `…:SecurityHeaders:Hsts` / `HstsMaxAge` / `HstsIncludeSubDomains` / `HstsPreload` | `bool` / `TimeSpan` / `bool` / `bool` | `true` / 365 days / `true` / `false` | HSTS |
| `…:SecurityHeaders:ContentTypeOptions` | `string` | `nosniff` | `X-Content-Type-Options`; empty turns it off |
| `…:SecurityHeaders:FrameOptions` | `string` | `DENY` | `X-Frame-Options` |
| `…:SecurityHeaders:ReferrerPolicy` | `string` | `no-referrer` | `Referrer-Policy` |
| `…:SecurityHeaders:PermissionsPolicy` | `string` | `geolocation=(), microphone=(), camera=()` | `Permissions-Policy` |
| `…:SecurityHeaders:ContentSecurityPolicy` | `string` | `default-src 'none'; frame-ancestors 'none'` | `Content-Security-Policy` |
| `…:SecurityHeaders:CacheControl` | `string` | `no-store` | Default `Cache-Control` |
| `…:Limits:MaxRequestBodySize` | `long?` | `4194304` (4 MiB) | Kestrel's body limit |
| `…:Limits:MaxJsonDepth` | `int?` | framework's (64) | System.Text.Json `MaxDepth`, requests and responses |
| `…:Problems:TypeBaseUri` | `Uri` | — | Base of `type`, followed by the `errorCode` |
| `…:Problems:IncludeExceptionDetails` | `bool?` | `null` (Development only) | Return exception details; warns outside Development (14012) |
| `…:Problems:UnavailableRetryAfter` | `TimeSpan?` | — | `Retry-After` on 503 |
| `…:Problems:PreconditionFailedErrorCodes` | `string[]` | the three version-conflict codes | Conflicts answered 412 on conditional requests; configured values are added |

The correlation id and inbound baggage are configured on `AddSharedKernelRequestContext()`
(`SharedKernel.ServiceDefaults.Security`).

## Reference

### Registration and types

| Member | Purpose |
| --- | --- |
| `IHostApplicationBuilder.AddSharedKernelWebApi(Action<SharedKernelWebApiOptions>?)` | Registers the boundary |
| `IApplicationBuilder.UseSharedKernelWebApi(Action<WebApiPipeline>?)` | Adds the pipeline; `WebApiPipeline.AtStart`, `.BeforeAuthentication`, `.BeforeAuthorization` |
| `IEndpointModule` / generated `MapEndpoints()` | Endpoint grouping |
| Typed-result extensions, `ErrorHttpResult`, `OkWithETag<T>` | Result → HTTP |
| `error.ToErrorResult()`, `error.ToProblemDetails(httpContext)` | Write an error yourself |
| `IdempotencyKey` (`MaxLength` = 256), `HttpContext.GetIdempotencyKey()` | The idempotency header |
| `IfMatch<TVersion>`, `HttpContext.GetIfMatch()`, `HttpResponse.SetETag(version)` | Conditional requests |
| `Paging`, `CursorPaging` | Validated paging parameters |
| `HttpContext.GetCorrelationId()` | The request's correlation id |
| `PresentationErrorCodes`, `ProblemDetailsExtensionNames` | Code and member-name constants |

### Errors

| Code | Status | When |
| --- | --- | --- |
| `request.too_large` | 413 | The body is larger than the endpoint accepts |
| `idempotency.key_required` / `idempotency.key_invalid` | 400 | A required `Idempotency-Key` is missing / malformed |
| `precondition.required` | 428 | A required `If-Match` is missing or `*` |
| `precondition.invalid` | 400 | An `If-Match` is malformed or names several tags |
| `precondition.failed` | 412 | An `If-Match` tag is weak or not a version of the resource |
| `pagination.page.out_of_range`, `pagination.page_size.out_of_range`, `pagination.cursor.invalid`, `pagination.limit.out_of_range` | 400 (in `errorCodes`) | Paging input out of range |
| `validation.failed`, `validation.invalid_format`, `validation.invalid_value` | 400 | Validation and binding failures |
| `unauthorized.default` / `unauthorized.step_up_required` | 401 | Not signed in / a stronger or more recent sign-in is required |
| `forbidden.insufficient_permission` / `forbidden.origin_not_allowed` | 403 | Missing permission or role / a WebSocket from a disallowed origin |
| `rate_limit.exceeded` | 429 | Rate limiting refused the request |
| `timeout.default` | 504 | A timeout, or a cancellation the client did not cause |
| `unexpected.exception` | 500 | An exception that is not a `SharedKernelException` |
| `http.{status}` | Any | A framework response without an error |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 14001 | Error | A request failed with a server error (5xx) |
| 14003 | Warning | A request was refused for its `Idempotency-Key` (never the key) |
| 14004 | Critical | CORS settings failed startup validation |
| 14005 | Warning | Rate limiting rejected a request |
| 14007 | Debug | An exception was answered with a client error (4xx) |
| 14008 | Debug | The client closed the request before it completed (499) |
| 14011 | Warning | `AddSharedKernelWebApi()` ran but `UseSharedKernelWebApi()` did not |
| 14012 | Warning | Exception details are enabled outside Development |
| 14013 | Warning | A WebSocket request from a disallowed origin was refused |

14002, 14009 and 14010 (authorization) are emitted by `SharedKernel.Presentation.Core`.

## Testing

A typed result is unit-tested on the result itself:

```csharp
Result<Order> missing = Error.NotFound("order.not_found", "Order 42 was not found.");

var result = missing.ToOkWithETag(order => order.Version.ToString(), OrderResponse.From);

var error = Assert.IsType<ErrorHttpResult>(result.Result);
Assert.Equal("order.not_found", error.Error.Code);
Assert.Equal(404, error.StatusCode);
```

`new IdempotencyKey("order-17")`, `new IfMatch<EntityVersion>(version)` and `new Paging(PageRequest.First)` build the
parameters for a handler test. For the pipeline itself (problems, headers, authorization), host the service with
`WebApplicationFactory<Program>`, as `samples/OrderApi`'s tests do.
[`SharedKernel.Presentation.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Presentation.Testing/README.md)'s
`FakeHttpContextAccessor` supplies an `IHttpContextAccessor` for code that reads one.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Forget `UseSharedKernelWebApi()` | Call it right after `UseSharedKernelRequestContext()` | Typed results still write problems, but exceptions, headers, CORS and header checks are missing (warning 14011) |
| Forget `UseSharedKernelRequestContext()` | Add it first | Problems lose `correlationId`, and caller baggage is not refused |
| Add middleware between the two calls | Use a hook (`AtStart`, `BeforeAuthentication`, `BeforeAuthorization`) | It would run outside the exception handler |
| Register an authentication scheme without an `IUserContextMapper` | Register the scheme's mapper, test schemes included | Every signed-in caller gets 403 (14009, 14010) |
| Read `Idempotency-Key` / `If-Match` raw | Declare the header (parameter, convention or attribute) | Undeclared, `GetIdempotencyKey()`/`GetIfMatch()` return `null` for invalid values too |
| Assert 412 on `ErrorHttpResult.StatusCode` | Assert the error code, or test through the host | The 412 rule depends on the request; the result alone says 409 |
| Test body limits with `TestServer` | Test them against Kestrel | `TestServer` enforces none |
| Branch on `IsSuccess` to build a response, or construct `ProblemDetails` | Use the typed results or `ToErrorResult()` | 00.Governance flags both |
| Expect configured `Cors:ExposedHeaders` / `PreconditionFailedErrorCodes` to replace the defaults | Clear the list in `configure` | Configured values are added to the defaults |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Presentation domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
