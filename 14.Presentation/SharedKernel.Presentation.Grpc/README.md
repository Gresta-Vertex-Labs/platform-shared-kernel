# SharedKernel.Presentation.Grpc

Server-side gRPC API surface conventions for SharedKernel microservices — the inbound counterpart
to `SharedKernel.Presentation.WebApi`, giving gRPC service methods the same exception-mapping,
correlation/tenant-propagation, and declarative-authorization story HTTP endpoints already get.

Like its `.WebApi`/`.SignalR` siblings, this package is framework-glue: it converts outcomes your
gRPC service methods already produce (or throw) into safe, client-facing `RpcException`/`Status`
responses. It never references `04.Contracts` — protobuf-generated messages are this package's
only wire-contract surface, mirroring `SharedKernel.Communication.Grpc`'s existing rule that
client-side gRPC code never references `04.Contracts` either. `SharedKernel.Communication.Grpc`
stays outbound-only (client channel/interceptors); this package owns the inbound gRPC API
boundary.

---

## Installation

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.Presentation.Grpc" Version="x.y.z" />
</ItemGroup>
```

Brings in `Grpc.AspNetCore` (server hosting) as a transitive dependency, plus a `ProjectReference`
on `SharedKernel.Presentation.WebApi` — the one deliberate exception to this domain's usual
"distinct API surfaces, no cross-references" rule — solely to reuse `RequireRoleAttribute`/
`RequirePermissionAttribute`/`RequireFreshAuthenticationAttribute`/`RequireAuthenticationMethodAttribute`
verbatim, so the platform has **one** declarative authorization dialect across HTTP and gRPC.

---

## Minimal setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelGrpc();

var app = builder.Build();

app.MapGrpcService<OrdersService>();

app.Run();
```

`AddSharedKernelGrpc` registers gRPC plus four global server interceptors — applied to **every**
mapped gRPC service automatically, via `GrpcServiceOptions.Interceptors`, with no per-service
wiring needed (unlike HTTP's `AuthorizationRequirementEndpointFilter`, which needs an explicit
`.AddEndpointFilter<T>()` call per route/group):

- **`GrpcExceptionInterceptor`** — the gRPC counterpart to `IExceptionHandler`/
  `SharedKernelExceptionHandler`. Known `SharedKernelException` subtypes (`01.Core`) map to an
  `RpcException` via `GrpcStatusCodeMap`; unknown exceptions are logged at `LogLevel.Error` and
  mapped to `StatusCode.Internal` with detail suppressed outside
  `IHostEnvironment.IsDevelopment()`. Overrides all four server interceptor methods (unary +
  three streaming shapes) — gRPC, unlike HTTP, has more than one call shape that needs mapping.
- **`GrpcCorrelationInterceptor`** — reads the inbound `X-Correlation-Id` gRPC metadata key
  (the same key `SharedKernel.Communication.Grpc`'s client-side `CorrelationTracingInterceptor`
  writes), generating one when absent, and sets `Activity.Current`'s correlation baggage exactly
  like `CorrelationIdMiddleware` does for HTTP.
- **`GrpcTenantContextInterceptor`** — resolves `ITenantProvider` (`12.Security.Abstractions`)
  from the call's `HttpContext` and stores the resolved tenant id in
  `ServerCallContext.UserState["TenantId"]` for the call's lifetime. Mirrors
  `TenantContextHubFilter`'s policy exactly: it never rejects a call with no resolvable tenant.
- **`GrpcAuthorizationInterceptor`** — evaluates `[RequireRole]`/`[RequirePermission]`/
  `[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` applied directly to a gRPC
  service implementation class or method, via the exact same endpoint-metadata mechanism ASP.NET
  Core uses for MVC controller actions — `ServerCallContext.GetHttpContext()?.GetEndpoint()?.Metadata`.
  No gRPC-specific attribute vocabulary exists; these are the identical attribute types your HTTP
  endpoints already use.

A conservative `GrpcServiceOptions.MaxReceiveMessageSize` (4 MiB) is set before your `configure`
callback runs, so it is always overridable.

```csharp
builder.Services.AddSharedKernelGrpc(options =>
{
    options.MaxReceiveMessageSize = 16 * 1024 * 1024; // override the platform default
});
```

---

## Declarative authorization on a gRPC service method

```csharp
public sealed class OrdersService : Orders.OrdersBase
{
    [RequireRole("admin")]
    public override Task<CancelOrderReply> CancelOrder(CancelOrderRequest request, ServerCallContext context)
    {
        // ...
    }
}
```

Rejection throws an `RpcException` with `StatusCode.PermissionDenied` — the target method body
never runs. Composition rules (roles/permissions/methods within one attribute are OR'd; multiple
attribute types stacked on the same method are AND'd) are identical to the HTTP side.

---

## `Result<T>` → `RpcException`

```csharp
public override async Task<GetOrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
{
    var result = await _orderQuery.GetAsync(request.OrderId, context.CancellationToken);

    var order = result.ToGrpcResult(); // throws RpcException on failure, returns the value on success

    return new GetOrderReply { /* map order -> reply */ };
}
```

`Result.ToGrpcResult()` (non-generic) and `Result<T>.ToGrpcResult()` are the gRPC-boundary
siblings of `SharedKernel.Presentation.WebApi.Results.ResultHttpExtensions` — never hand-construct
`new RpcException(new Status(...))` at a service-method call site; always route through these
extensions (or let a thrown `SharedKernelException` reach `GrpcExceptionInterceptor`).

---

## `GrpcStatusCodeMap`

A sibling to `SharedKernel.Presentation.WebApi.Errors.ErrorTypeStatusCodeMap` — never a merge.
HTTP status codes and gRPC `StatusCode` are different target enums with no clean 1:1
correspondence (HTTP's 422 has no gRPC analogue; gRPC's `Aborted`/`FailedPrecondition` cover
ground HTTP splits across 409/412/422). Both maps key off the same `01.Core` `ErrorType` enum —
that shared vocabulary is the generalization point, not a shared value table.

| `ErrorType` | gRPC `StatusCode` |
|---|---|
| `Validation` | `InvalidArgument` |
| `Unauthorized` | `Unauthenticated` |
| `Forbidden` | `PermissionDenied` |
| `NotFound` | `NotFound` |
| `Conflict` | `Aborted` |
| `BusinessRule` | `FailedPrecondition` |
| `Unexpected` | `Internal` |
| *(unmapped, including `None`)* | `Unknown` |

---

## No new `13.ServiceDefaults` telemetry entry point

gRPC calls ride the same Kestrel/HTTP2 pipeline ASP.NET Core's existing server-side OpenTelemetry
instrumentation already traces — the same pipeline HTTP/1.1 Minimal API/MVC endpoints are already
traced through with no `14.Presentation`-specific `WithXTelemetry` entry point needed there
either. This is a deliberate decision, not a gap.

---

See `14.Presentation/CLAUDE.md` for the full design rationale, interface contracts, and layering
rules.
