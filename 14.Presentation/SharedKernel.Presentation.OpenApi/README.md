# SharedKernel.Presentation.OpenApi

> **API versioning and one OpenAPI 3.1 document per version for a SharedKernel HTTP API, in two calls — documents that
> describe what the WebApi core actually enforces: its error shape, which operations need authentication, and which
> headers they require.**

The add-on of [`SharedKernel.Presentation.WebApi`](../SharedKernel.Presentation.WebApi/README.md). The core stays free of
third-party packages; this one brings Asp.Versioning, `Microsoft.AspNetCore.OpenApi` and Scalar.

| You get | So that |
| --- | --- |
| API versioning: version 1.0 by default and assumed when a request names none, read from the URL segment (`/v1/…`) or the `X-Api-Version` header, reported in `api-supported-versions` | Every service versions its API the same way |
| One OpenAPI 3.1 document per version (`/openapi/v1.json`, `/openapi/v2.json`) and a Scalar reference listing them all (`/scalar/`) | Clients and generators see exactly the operations of the version they use |
| A `default` `application/problem+json` response on every operation, referencing a `ProblemDetails` schema with `errorCode`, `traceId`, `correlationId`, `errors` and `errorCodes` | Generated clients can read every error the API returns |
| A security requirement and 401/403 on protected operations only — `RequirePermission`, `RequireRole`, `[Authorize]`, a fallback policy — and none on anonymous ones | The documents say which operations need a token, and which do not |
| A required `Idempotency-Key` header with its 400, and a required `If-Match` header with its 428, 400 and 412 — however the endpoint requires them: convention, attribute or handler parameter ([Required headers](#required-headers)) | Clients learn the headers the API rejects requests without, and every way it rejects them |
| Bearer, API key and mutual TLS security schemes (OR) | The reference offers the credentials the service accepts |
| Sunset and deprecation policies: RFC 9745 `Deprecation: @<epoch>`, RFC 8594 `Sunset` HTTP-date and `Link` headers, and notices in the documents | Clients are told, on every response, when a version goes away |
| Documents served in Development only, unless `ExposeInProduction`; a startup warning when they are then served without authorization | An API description is published by decision, not by default — and to everyone only on purpose |
| API versioning's own errors (unsupported or malformed version) in the platform's problem shape | One error shape, including for a wrong version |

## Install

```xml
<PackageReference Include="SharedKernel.Presentation.OpenApi" Version="x.y.z" />
```

It references `SharedKernel.Presentation.WebApi`, whose error responses the documents describe.

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

orders.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => sender.Send(new GetOrder(id), ct).ToOk())
    .RequirePermission("orders.read");
orders.MapPost("/", (PlaceOrder command, ISender sender, CancellationToken ct) =>
        sender.Send(command, ct).ToCreated(order => $"/v1/orders/{order.Id}"))
    .RequirePermission("orders.write")
    .RequireIdempotencyKey();

app.MapSharedKernelOpenApi();   // before or after the API's endpoints

app.Run();
```

`MapSharedKernelOpenApi()` returns one convention builder for the documents and the reference, so they can be protected
like any endpoint:

```csharp
app.MapSharedKernelOpenApi().RequirePermission("docs.read");
```

The reference page loads the documents from the browser, so a bearer-token requirement stops it from loading them;
protect them with a scheme the browser sends by itself (a cookie), or behind a gateway. The page is served without a
`Content-Security-Policy`, which would stop its scripts; the documents keep the configured one.

When the documents are served outside Development (`ExposeInProduction`), the host logs a warning at startup (EventId
14301) unless a convention applied to that builder requires authorization — `RequirePermission`, `RequireRole`,
`RequireAuthorization`, … — or a fallback authorization policy is set. Documents meant for everyone say so with
`app.MapSharedKernelOpenApi().AllowAnonymous()`, which silences it. Only conventions on the returned builder count: when
the documents are mapped inside a protected route group, apply the requirement to the builder as well.

MVC controllers work the same way: `[ApiVersion(1.0)]`, `[RequirePermission]`, `[RequireIdempotencyKey]` and
`[RequireIfMatch]` are documented, and so are conventions applied with `app.MapControllers().RequirePermission(…)`.
Actions returning the typed results minimal APIs return (`Results<Ok<Order>, ErrorHttpResult>`) document their success
response the same way. Endpoints that declare no version belong to every version's document.

## Required headers

The core refuses a request without a required `Idempotency-Key` or `If-Match` header before the endpoint runs; the
documents describe the header as a required parameter and the refusals as problem responses. Every way of requiring the
header is documented identically:

| Required by | `Idempotency-Key` | `If-Match` |
| --- | --- | --- |
| Convention on an endpoint, group or `MapControllers()` | `RequireIdempotencyKey()` | `RequireIfMatch()` |
| Attribute on a controller, an action or a handler lambda | `[RequireIdempotencyKey]` | `[RequireIfMatch]` |
| Handler parameter (minimal APIs) | `IdempotencyKey key` | `IfMatch<TVersion> ifMatch` |

| Header | Response | Code | When |
| --- | --- | --- | --- |
| `Idempotency-Key` | 400 | `idempotency.key_required`, `idempotency.key_invalid` | Missing; not 1 to 256 visible ASCII characters |
| `If-Match` | 428 | `precondition.required` | Missing, or `*`: the request must name the version it changes |
| `If-Match` | 400 | `precondition.invalid` | Malformed, or more than one entity tag |
| `If-Match` | 412 | `precondition.failed`, or the conflict's own code | A weak tag, one that is not a version of the resource, or a version that is no longer current (`persistence.concurrency_conflict`) |

An endpoint requiring both headers documents one 400 naming both. An `IdempotencyKey` or `IfMatch<TVersion>` parameter
is documented only as its header, never as a query value or a request body:

```csharp
orders.MapPut("/{id:guid}", (Guid id, ChangeOrder change, IfMatch<EntityVersion> ifMatch, ISender sender, CancellationToken ct) =>
        sender.Send(change with { Id = id, ExpectedVersion = ifMatch.Version }, ct).ToNoContent())
    .RequirePermission("orders.write");
```

## Settings

Bound from `SharedKernel:Presentation:OpenApi` and validated when the host starts; the `configure` callback runs after
binding.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Title` | application name | Title of every document and of the reference page |
| `Description` | none | Markdown description of every document; a version's deprecation and sunset notices follow it |
| `Versioning` | none | Code only: `Action<ApiVersioningOptions>` run after the platform defaults |
| `Bearer` | `true` | Declare an HTTP bearer (JWT) scheme |
| `ApiKeyHeaderName` | none | Declare an API key scheme sent in this header, such as `X-Api-Key` |
| `MutualTls` | `false` | Declare a mutual TLS scheme (OpenAPI 3.1) |
| `ExposeInProduction` | `false` | Serve the documents and the reference outside Development; protect them, or declare them public with `AllowAnonymous()` |

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

## Sunset and deprecation

Declare the policies in the `Versioning` callback; every response of that version then carries the headers:

```csharp
builder.AddSharedKernelOpenApi(options => options.Versioning = versioning =>
{
    versioning.Policies.Deprecate(1.0)
        .Effective(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero))
        .Link("https://docs.example.com/orders/v1").Title("Migrating to v2").Type("text/html");
    versioning.Policies.Sunset(1.0)
        .Effective(new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero));
});
```

```http
Deprecation: @1782777600
Sunset: Sun, 31 Jan 2027 00:00:00 GMT
Link: <https://docs.example.com/orders/v1>; rel="deprecation"; title="Migrating to v2"; type="text/html"
```

## Logging

| EventId | Level | When |
| --- | --- | --- |
| 14300 | Information | `MapSharedKernelOpenApi()` maps nothing because the environment is not Development and `ExposeInProduction` is off |
| 14301 | Warning | At startup: the documents are served outside Development, and neither a convention on the builder `MapSharedKernelOpenApi()` returned (an authorization requirement, or `AllowAnonymous()`) nor a fallback authorization policy decides who may read them |
