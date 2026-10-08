# SharedKernel.Presentation.OpenApi

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **API versioning and one OpenAPI 3.1 document per version for a SharedKernel HTTP API, in two calls. The documents
> describe what `SharedKernel.Presentation.WebApi` enforces — its error shape, which operations need a caller, and
> which headers they require — and are published outside Development only by decision.** It is an add-on to
> `SharedKernel.Presentation.WebApi`: add it when a REST API needs versions or a published description.

| You get | So that |
| --- | --- |
| API versioning: 1.0 by default and assumed when a request names none, from the URL segment (`/v1/…`) or `X-Api-Version`, reported in `api-supported-versions` | Every service versions its API the same way |
| One OpenAPI 3.1 document per version (`/openapi/v1.json`, …) and a Scalar reference at `/scalar` | Clients and generators see the operations of the version they use |
| A `default` `application/problem+json` response on every operation | Generated clients can read every error the API returns |
| Security requirements and 401/403 on protected operations only | The documents say which operations need a caller |
| `Idempotency-Key`, `If-Match` and `ETag` documented with their refusals | Clients learn the headers the API checks and every way it refuses them |
| Bearer, API-key and mutual-TLS security schemes | The reference offers the credentials the service accepts |
| RFC 9745 `Deprecation`, RFC 8594 `Sunset` and `Link` headers, and notices in the documents | Clients are told on every response when a version goes away |
| Documents served in Development only, unless `ExposeInProduction` | An API description is published by decision |

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
<PackageReference Include="SharedKernel.Presentation.OpenApi" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** / **Worker** project |
| Depends on | [`SharedKernel.Presentation.WebApi`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/SharedKernel.Presentation.WebApi/README.md), `SharedKernel.Presentation.Core`, `Asp.Versioning.Http`, `Asp.Versioning.Mvc.ApiExplorer`, `Asp.Versioning.OpenApi`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` |
| Namespaces | `SharedKernel.Presentation.OpenApi` |

The WebApi core has no third-party dependencies; this add-on brings Asp.Versioning, `Microsoft.AspNetCore.OpenApi` and
Scalar. It only describes responses; it never changes one.

## Quick start

```csharp
using SharedKernel.Application.Messaging;   // ISender
using SharedKernel.Presentation.OpenApi;
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();   // the correlationId every documented problem carries
builder.AddSharedKernelWebApi();
builder.AddSharedKernelOpenApi(options => options.Title = "Orders API");

var app = builder.Build();
app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi();

app.MapEndpoints();
app.MapSharedKernelOpenApi();   // before or after the API's endpoints

app.Run();

public sealed class OrderEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var orders = app.NewVersionedApi("Orders")
            .MapGroup("/v{version:apiVersion}/orders")
            .HasApiVersion(1.0)
            .HasApiVersion(2.0);

        orders.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetOrder(id), ct).ToOkWithETag(order => order.Version.ToString(), OrderResponse.From));
    }
}
```

## How it works

- `AddSharedKernelOpenApi()` binds `SharedKernel:Presentation:OpenApi`, runs `configure` after binding, and validates
  the result at host start. It registers API versioning (1.0 by default and assumed; URL segment, then
  `X-Api-Version`), then your `Versioning` callback, the API Explorer (one group per version: `v1`, `v2`, `v1.5`),
  one document per version, and the platform's problem shape for versioning's own errors. It is idempotent.
- `MapSharedKernelOpenApi()` maps `/openapi/{documentName}.json` and `/scalar` (the newest non-deprecated version opens
  first) and returns one convention builder for both. It throws `InvalidOperationException` without
  `AddSharedKernelOpenApi()`. Outside Development it maps nothing unless `ExposeInProduction` is set (EventId 14300).
- An endpoint that declares no version (`app.MapGet("/ping", …)`) appears in every version's document. MVC controllers
  are documented the same way (`[ApiVersion(1.0)]`, the authorization and header attributes).
- What each operation documents:

  | When the endpoint… | The operation documents |
  | --- | --- |
  | Always | A `default` response: `application/problem+json`, `#/components/schemas/ProblemDetails` |
  | Has authorization metadata (or a fallback policy applies) and no `[AllowAnonymous]` | One security requirement per declared scheme (any one suffices), and 401 and 403 |
  | Requires / accepts `Idempotency-Key` | A required / optional header parameter, and 400 |
  | Requires `If-Match` | A required header, and 400, 412 and 428 |
  | Accepts `If-Match` | An optional header, and 400 and 412 |
  | Returns `OkWithETag<T>` (`ToOkWithETag`) | `ETag` on 200, and a 304 for `GET`/`HEAD` |
  | Takes a `Paging` / `CursorPaging` parameter | `page`/`pageSize` or `cursor`/`limit` with bounds and defaults, and 400 |

  Header rules come from endpoint metadata, so the convention, the attribute and the `IdempotencyKey`/`IfMatch<T>`
  parameters document identically. Platform responses follow the operation's own; nothing it declares is replaced.
- The `ProblemDetails` schema lists `type`, `title`, `status`, `detail`, `instance`, `errorCode`, `traceId`,
  `correlationId`, `errors`, `errorCodes` and the Development-only `exception`; `status`, `errorCode` and `traceId` are
  required.

## Recipes

### 1. Protect the documents outside Development

```csharp
using SharedKernel.Presentation.Authorization;

app.MapSharedKernelOpenApi().RequireEndpointPermission("docs.read");
// or, for documents meant for everyone:
app.MapSharedKernelOpenApi().AllowAnonymous();
```

Outside Development, when neither a convention on the returned builder nor a fallback policy decides who may read the
documents, the host warns at startup (EventId 14301). Only conventions on the returned builder count. The Scalar page
loads documents from the browser, so a bearer-token requirement stops it: protect them with a cookie scheme or behind
a gateway. The reference page is served without a `Content-Security-Policy` (it would stop its scripts).

### 2. Deprecate and sunset a version

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

Every response of version 1.0 then carries `Deprecation: @1782777600`, `Sunset: Sun, 31 Jan 2027 00:00:00 GMT` and
`Link: <https://docs.example.com/orders/v1>; rel="deprecation"`, and its document carries the notices. The callback
runs after the platform defaults, so it can also change the default version or the version readers.

### 3. Show that a command-protected endpoint needs a caller

The document reads endpoint metadata only, so an endpoint whose use case carries `[RequirePermission]` and declares
nothing itself documents no security requirement. Add `.RequireAuthorization()` on the group, or a fallback policy
(`AddAuthorizationBuilder().SetFallbackPolicy(...)`); the specific permission stays the use case's.

## Configuration

Section `SharedKernel:Presentation:OpenApi` (`SharedKernelOpenApiOptions`), validated at host start. Full reference:
[CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/CONFIGURATION.md#sharedkernelpresentationopenapi).

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Presentation:OpenApi:Title` | `string` | The application name | Title of every document and of the reference page |
| `SharedKernel:Presentation:OpenApi:Description` | `string` | — | Markdown description; deprecation and sunset notices follow it |
| `SharedKernel:Presentation:OpenApi:Bearer` | `bool` | `true` | Declare the HTTP bearer scheme (JWT) |
| `SharedKernel:Presentation:OpenApi:ApiKeyHeaderName` | `string` | — | Declare an API-key scheme in this header; must be a valid header name |
| `SharedKernel:Presentation:OpenApi:MutualTls` | `bool` | `false` | Declare the mutual-TLS scheme (OpenAPI 3.1 `mutualTLS`) |
| `SharedKernel:Presentation:OpenApi:ExposeInProduction` | `bool` | `false` | Serve the documents and the reference outside Development |
| `Versioning` (code only) | `Action<ApiVersioningOptions>` | — | Runs after the platform's versioning defaults |

## Reference

| Member | Purpose |
| --- | --- |
| `IHostApplicationBuilder.AddSharedKernelOpenApi(Action<SharedKernelOpenApiOptions>?)` | Versioning, API Explorer, one document per version |
| `IEndpointRouteBuilder.MapSharedKernelOpenApi()` | Maps the documents and `/scalar`; returns `IEndpointConventionBuilder` |
| `SharedKernelOpenApiOptions` | The options above; `SectionName` |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 14300 | Information | `MapSharedKernelOpenApi()` mapped nothing: not Development and `ExposeInProduction` is off |
| 14301 | Warning | Documents served outside Development with no authorization convention, `AllowAnonymous()` or fallback policy |

## Testing

Host the service with `WebApplicationFactory<Program>` in the `Development` environment and fetch
`/openapi/v1.json`; assert on the operations, security requirements and headers of each version's document.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Write `versioning.Policies.Deprecate(1.0)` without `using Asp.Versioning;` | Add the using | They are extension methods of that namespace; otherwise another overload binds and it does not compile |
| Assume `ExposeInProduction` means Production only | Treat it as every environment but Development | Staging is "production" here |
| Protect documents with a bearer token and expect Scalar to load them | Use a cookie scheme or a gateway | The reference page fetches documents from the browser |
| Expect `/openapi/v3.json` for an unknown version to be a problem | Know it is a plain-text 404 | Only mapped documents exist |
| Upgrade `Asp.Versioning.OpenApi` or `Microsoft.AspNetCore.OpenApi` alone | Upgrade them together | The former reflects over the latter's internals |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Presentation packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
