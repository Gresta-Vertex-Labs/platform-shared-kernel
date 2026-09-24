# SharedKernel.Presentation.OpenApi

> **API versioning and one OpenAPI 3.1 document per version for a SharedKernel HTTP API, in two calls. The documents
> describe what the WebApi core enforces: its error shape, which operations need a caller, and which headers they
> require or accept.**

The add-on of
[`SharedKernel.Presentation.WebApi`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/14.Presentation/SharedKernel.Presentation.WebApi).
The core has no third-party dependencies; this package brings Asp.Versioning, `Microsoft.AspNetCore.OpenApi` and
Scalar. It only describes responses; it never changes one.

| You get | So that |
| --- | --- |
| API versioning: 1.0 by default and assumed when a request names none, read from the URL segment (`/v1/…`) or the `X-Api-Version` header, reported in `api-supported-versions` | Every service versions its API the same way |
| One OpenAPI 3.1 document per version (`/openapi/v1.json`, `/openapi/v2.json`) and a Scalar reference listing them all (`/scalar`) | Clients and generators see the operations of the version they use |
| A `default` `application/problem+json` response on every operation, referencing a `ProblemDetails` schema with `errorCode`, `traceId`, `correlationId`, `errors` and `errorCodes` | Generated clients can read every error the API returns |
| A security requirement and 401/403 on protected operations only | The documents say which operations need a caller |
| The `Idempotency-Key` and `If-Match` headers, required or optional, with the responses that refuse them; the `ETag` response header | Clients learn the headers the API checks and every way it refuses them |
| Bearer, API key and mutual TLS security schemes | The reference offers the credentials the service accepts |
| Sunset and deprecation policies: RFC 9745 `Deprecation`, RFC 8594 `Sunset` and `Link` headers, and notices in the documents | Clients are told on every response when a version goes away |
| Documents served in Development only, unless `ExposeInProduction`; a startup warning when they are then served without authorization | An API description is published by decision, and to everyone only on purpose |
| API versioning's own errors in the platform's problem shape | One error shape, a wrong version included |

## Install

```shell
dotnet add package SharedKernel.Presentation.OpenApi
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | `SharedKernel.Presentation.WebApi`, `Asp.Versioning.Http`, `Asp.Versioning.Mvc.ApiExplorer`, `Asp.Versioning.OpenApi`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` |

## Use

```csharp
using SharedKernel.Presentation.OpenApi;
using SharedKernel.Presentation.WebApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedKernelWebApi();
builder.AddSharedKernelOpenApi(options => options.Title = "Orders API");

var app = builder.Build();
app.UseSharedKernelWebApi();

var orders = app.NewVersionedApi("Orders")
    .MapGroup("/v{version:apiVersion}/orders")
    .HasApiVersion(1.0)
    .HasApiVersion(2.0);

orders.MapGet("/{id:guid}", (Guid id, IOrderService service, CancellationToken ct) =>
        service.GetAsync(id, ct).ToOkWithETag(order => order.Version.ToString(), OrderResponse.From))
    .RequirePermission("orders.read");

app.MapSharedKernelOpenApi();   // before or after the API's endpoints

app.Run();
```

`AddSharedKernelOpenApi()` binds `SharedKernel:Presentation:OpenApi`, runs the `configure` callback after binding and
validates the result when the host starts. It registers API versioning (1.0 by default and assumed when a request names
none; the URL segment, then the `X-Api-Version` header; versions reported on every response), then the service's
`Versioning` callback, the API Explorer (one group per version, named `v1`, `v2`, `v1.5`, with the version written into
the URLs), one document per version, and the platform's problem shape for versioning's own errors. It is idempotent.

- `MapSharedKernelOpenApi()` maps the documents at `/openapi/{documentName}.json` and the Scalar reference at
  `/scalar`, listing every version; the newest version that is not deprecated opens first. It throws
  `InvalidOperationException` without `AddSharedKernelOpenApi()`.
- Outside Development it maps nothing unless `ExposeInProduction` is set ("production" here means every environment
  but Development), and logs that at Information (14300).
- It returns one convention builder for the documents and the reference:
  `app.MapSharedKernelOpenApi().RequirePermission("docs.read")` protects both.
- An endpoint that declares no version, such as `app.MapGet("/ping", …)`, appears in every version's document.
- MVC controllers are documented the same way: `[ApiVersion(1.0)]`, the authorization and header attributes, and
  conventions applied with `app.MapControllers().RequirePermission(…)`. Actions returning typed results document
  their success responses like minimal APIs.

## What every operation documents

| When the endpoint… | The operation documents |
| --- | --- |
| Always | A `default` response: `application/problem+json`, `#/components/schemas/ProblemDetails` |
| Has authorization metadata (`RequirePermission`, `RequireRole`, `[Authorize]`, …), or no metadata while a fallback policy is set, and no `[AllowAnonymous]` | One security requirement per declared scheme (any one suffices), and 401 and 403 |
| Requires `Idempotency-Key` | A required header parameter whose pattern admits exactly what the server accepts, and 400 |
| Accepts `Idempotency-Key` | An optional header parameter, and 400 |
| Requires `If-Match` | A required header parameter, and 400, 412 and 428 |
| Accepts `If-Match` | An optional header parameter, and 400 and 412 (never 428) |
| Returns `OkWithETag<T>` (`ToOkWithETag`) | The `ETag` header on 200, and a 304 with its `ETag` when the endpoint answers `GET` or `HEAD` |

The header rules come from endpoint metadata, so every way of declaring a header is documented identically: the
convention (`RequireIdempotencyKey()`, `AcceptIfMatch()`, …), the attribute, or the `IdempotencyKey` and
`IfMatch<TVersion>` parameters, nullable or not. A parameter is documented as its header only, never as a query value
or a body. An endpoint declaring both headers documents one 400 naming the refusals of both. The platform's responses
follow the operation's own, in status order, and nothing the operation already declares is replaced.

The `ProblemDetails` schema lists `type`, `title`, `status`, `detail`, `instance`, `errorCode`, `traceId`,
`correlationId`, `errors`, `errorCodes` and the Development-only `exception`; `status`, `errorCode` and `traceId` are
required.

## Protecting the documents

- Outside Development, when no convention on the builder that `MapSharedKernelOpenApi()` returned requires
  authorization and no fallback authorization policy applies, the host logs a warning at startup (14301). Documents
  meant for everyone say so with `app.MapSharedKernelOpenApi().AllowAnonymous()`, which silences it.
- Only conventions on the returned builder count: documents mapped inside a protected route group still need the
  requirement on the builder.
- The reference page loads the documents from the browser, so a bearer-token requirement stops it from loading them.
  Protect the documents with a scheme the browser sends by itself (a cookie), or behind a gateway.
- The reference page is served without a `Content-Security-Policy`, which would stop its scripts; the documents keep
  the configured one.

## Security schemes

| Setting | Scheme declared |
| --- | --- |
| `Bearer` (`true` by default) | `Bearer`: HTTP bearer, format JWT |
| `ApiKeyHeaderName`, such as `X-Api-Key` | `ApiKey`: an API key in that header |
| `MutualTls` (`false` by default) | `MutualTls`: a client certificate (OpenAPI 3.1 `mutualTLS`) |

A protected operation lists one requirement per declared scheme, so any one of them satisfies it. With no scheme
declared, protected operations still document 401 and 403.

## Sunset and deprecation

Declare the policies in the `Versioning` callback (code only; it needs `using Asp.Versioning;`). Every response of
that version then carries the headers, and that version's document carries the notices after the configured
description:

```csharp
using Asp.Versioning;

builder.AddSharedKernelOpenApi(options =>
{
    options.Title = "Orders API";
    options.Versioning = versioning =>
    {
        versioning.Policies.Deprecate(1.0)
            .Effective(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero))
            .Link("https://docs.example.com/orders/v1");
        versioning.Policies.Sunset(1.0)
            .Effective(new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero));
    };
});
```

```http
Deprecation: @1782777600
Sunset: Sun, 31 Jan 2027 00:00:00 GMT
Link: <https://docs.example.com/orders/v1>; rel="deprecation"
```

The callback runs after the platform defaults, so it can also change the default version or the version readers.

## Settings

Bound from `SharedKernel:Presentation:OpenApi`; the full reference is in
[CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/CONFIGURATION.md#sharedkernelpresentationopenapi).

| Setting | Default | Meaning |
| --- | --- | --- |
| `Title` | The application name | Title of every document and of the reference page |
| `Description` | none | Markdown description of every document; a version's deprecation and sunset notices follow it |
| `Versioning` | none | Code only: `Action<ApiVersioningOptions>` run after the platform defaults |
| `Bearer` | `true` | Declare the HTTP bearer scheme |
| `ApiKeyHeaderName` | none | Declare an API key scheme in this header; must be a valid header name |
| `MutualTls` | `false` | Declare the mutual TLS scheme |
| `ExposeInProduction` | `false` | Serve the documents and the reference outside Development |

```json
{
  "SharedKernel": {
    "Presentation": {
      "OpenApi": {
        "Title": "Orders API",
        "ApiKeyHeaderName": "X-Api-Key",
        "ExposeInProduction": true
      }
    }
  }
}
```

## Logging

EventIds 14300–14399.

| EventId | Level | Event |
| --- | --- | --- |
| 14300 | Information | `MapSharedKernelOpenApi()` mapped nothing: the environment is not Development and `ExposeInProduction` is off |
| 14301 | Warning | At startup: the documents are served outside Development, and neither an authorization convention on the returned builder, `AllowAnonymous()`, nor a fallback policy decides who may read them |

## Pitfalls

- **`Versioning` without `using Asp.Versioning;`.** `versioning.Policies.Deprecate(1.0)` and `Sunset(1.0)` are
  extension methods of that namespace; without it the call binds to another overload and does not compile.
- **Documents in Staging.** `ExposeInProduction` means every environment but Development.
- **Bearer-protected documents.** The Scalar page cannot load them; see [Protecting the documents](#protecting-the-documents).
- **An unknown document**, such as `/openapi/v3.json`, is a plain-text 404, not a problem.
- **Asp.Versioning.OpenApi** reflects over `Microsoft.AspNetCore.OpenApi` internals. Upgrade the two together;
  `14.Presentation/consumer-verify` generates two versioned documents so a breaking upgrade fails CI.

## Not in this package

| Looking for | Use instead |
| --- | --- |
| `AddSharedKernelApiVersioning`, `SharedKernelApiVersioningDefaults` | `AddSharedKernelOpenApi()` and its `Versioning` callback |
| `AddSharedKernelOpenApi(IServiceCollection, title, …)`, `OpenApiSecuritySchemesOptions`, `MutualTlsSecurityScheme` | `builder.AddSharedKernelOpenApi()` with `Title`, `ApiKeyHeaderName` and `MutualTls` |
| `ApiVersionLifecycleOptions` and its middleware | Asp.Versioning's sunset and deprecation policies in `Versioning` |
| A Scalar customization hook | Not offered: `MapSharedKernelOpenApi()` configures the reference itself |
