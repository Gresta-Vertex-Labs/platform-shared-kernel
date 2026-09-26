# 14.Presentation

The inbound API surface of a SharedKernel service: HTTP (REST/Minimal API/MVC), gRPC server,
SignalR and GraphQL. These packages translate the platform's outcomes — `Result<T>`, `Error`,
`SharedKernelException` — into the wire formats clients see (RFC 9457 `ProblemDetails`,
`RpcException`, `HubException`, GraphQL errors), and apply the boundary's own conventions:
declarative authorization, versioning, OpenAPI, security headers, CORS, payload limits.

They never produce those outcomes themselves: no package here references a mediator, the
application pipeline, persistence or messaging. The outbound side (typed REST and gRPC clients) is
`11.Communication`.

## Packages

Every package is **Host** tier: referenced by a service's Api/host project only, never by its
Domain, Application or Infrastructure projects.

| Package | Purpose | References (SharedKernel) |
| --- | --- | --- |
| [`SharedKernel.Presentation.Core`](SharedKernel.Presentation.Core/README.md) | `[RequireRole]`, `[RequirePermission]`, `[RequireFreshAuthentication]`, `[RequireAuthenticationMethod]` (namespace `SharedKernel.Presentation.Authorization`); `ErrorTypeStatusCodeMap` and `GrpcStatusCodeMap` (namespace `SharedKernel.Presentation.Errors`). No ASP.NET Core reference. | Primitives |
| [`SharedKernel.Presentation.WebApi`](SharedKernel.Presentation.WebApi/README.md) | `Error` → `ProblemDetails` (localized, multi-field validation), `SharedKernelExceptionHandler`, `Result<T>` → `IResult`/`ActionResult`, API versioning + Sunset/Deprecation headers, native OpenAPI + Scalar, endpoint filters for authorization, idempotency keys and uploads, security headers, CORS, payload limits, ETag/`If-Match`, 429 `ProblemDetails`. | Primitives, Core, Localization, Security.Abstractions, Presentation.Core |
| [`SharedKernel.Presentation.Grpc`](SharedKernel.Presentation.Grpc/README.md) | Server interceptors: exception → `RpcException`, correlation id, the caller's `RequestContextScope`, the same `[Require*]` attributes; `Result<T>` → `RpcException`. | Primitives, Core, Execution, Security.Abstractions, Presentation.Core |
| [`SharedKernel.Presentation.SignalR`](SharedKernel.Presentation.SignalR/README.md) | Hub filters (caller's `RequestContextScope` for every hub call, exception → `HubException`, invocation rate limiting), tenant group naming, conservative `HubOptions`, a startup CORS diagnostic. | Primitives, Core, Execution |
| [`SharedKernel.Presentation.SignalR.Redis`](SharedKernel.Presentation.SignalR.Redis/README.md) | `WithRedisBackplane` — the Redis scale-out backplane, for hosts with more than one replica. | Presentation.SignalR |
| [`SharedKernel.Presentation.GraphQL`](SharedKernel.Presentation.GraphQL/README.md) | HotChocolate server conventions: snake_case, `FilterBase<T>`/`SortBase<T>`, paging capped at `MaxPageSize`, `PagedResponseType<T>`, error filter. | Primitives, Contracts |

Install only what the host serves, without versions — every SharedKernel package ships with the
consumer's single `SharedKernelVersion`:

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Security" />   <!-- request context + correlation id -->
<PackageReference Include="SharedKernel.Presentation.WebApi" />
<!-- as needed: SharedKernel.Presentation.Grpc, .SignalR, .SignalR.Redis, .GraphQL -->
```

## How they fit

- **`.Core` is the shared vocabulary.** WebApi and Grpc both reference it and never each other, so
  a service has one authorization dialect across HTTP and gRPC and a gRPC-only host carries no
  OpenAPI/versioning dependencies. Both status maps key off the same `ErrorType`, but stay separate
  tables — HTTP and gRPC codes do not correspond 1:1.
- **The caller comes from `SharedKernel.Execution`.** `IRequestContext` (tenant as `TenantId?`,
  actor as `ActorKind`, correlation id) is built by `SharedKernel.ServiceDefaults.Security`'s
  `AddSharedKernelRequestContext()`. `UseSharedKernelRequestContext()` opens the request's scope on
  the HTTP pipeline; the gRPC interceptors and the SignalR hub filter carry the same context into each
  call and hub invocation. Code reads it from `IRequestContext` / `IRequestContextAccessor`.
- **Authorization reads `IUserContext`** (`SharedKernel.Security.Abstractions`), whichever
  authentication package (`.Oidc`, `.ApiKey`, `.Mtls`) populated it. The HTTP filter is attached per
  route group (`.AddEndpointFilter<AuthorizationRequirementEndpointFilter>()`); the gRPC interceptor
  is global.
- **SignalR has no Redis dependency.** A multi-replica hub host adds `.SignalR.Redis`; the backplane
  opens its own connection, never the one `SharedKernel.Caching.Redis.Core` registers.

## Canonical HTTP pipeline

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();                 // SharedKernel.ServiceDefaults.Security
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
builder.Services.AddSharedKernelAuthorizationFilters();
builder.Services.AddSharedKernelApiVersioning();
builder.Services.AddSharedKernelOpenApi(title: "Orders API");

var app = builder.Build();

app.UseSharedKernelRequestContext();    // 1. correlation id + the request's IRequestContext scope
app.UseSharedKernelSecurityHeaders();   // 2. security response headers
app.UseExceptionHandler();              // 3. SharedKernelExceptionHandler → ProblemDetails
app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/v{version:apiVersion}/orders")
   .AddEndpointFilter<AuthorizationRequirementEndpointFilter>()
   .MapGet("/{id:guid}", GetOrder)
   .RequirePermission("orders:read");

app.MapSharedKernelOpenApi();
app.Run();
```

`UseSharedKernelRequestContext()` goes first so every response — errors included — carries
`X-Correlation-Id`. It accepts a caller-supplied id only when it is at most 128 characters of
`[A-Za-z0-9-_:.]`, and creates one otherwise. Tenant resolution (`SharedKernel.MultiTenancy`), when
used, goes after `UseAuthentication()` and refines the tenant in an inner scope.

## Further reading

- [Configuration reference](CONFIGURATION.md) — every registration method, option and default.
- [`CLAUDE.md`](CLAUDE.md) — maintainer rules for this domain.
- [`samples/OrderApi`](../samples/OrderApi) — a four-project reference service on these packages.
