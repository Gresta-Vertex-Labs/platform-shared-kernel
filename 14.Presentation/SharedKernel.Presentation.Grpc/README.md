# SharedKernel.Presentation.Grpc

Server-side gRPC API surface conventions for SharedKernel microservices — the inbound counterpart
to `SharedKernel.Presentation.WebApi`, giving gRPC service methods the same exception-mapping,
caller-context, and declarative-authorization story HTTP endpoints already get.

**Tier:** Host. It references `SharedKernel.Primitives`, `SharedKernel.Core`,
`SharedKernel.Execution`, `SharedKernel.Security.Abstractions`,
[`SharedKernel.Presentation.Core`](../SharedKernel.Presentation.Core/README.md) (the `[Require*]`
attributes and `GrpcStatusCodeMap`) and `Grpc.AspNetCore`. It does **not** reference
`SharedKernel.Presentation.WebApi`, so a gRPC-only host carries no OpenAPI/versioning/Scalar
dependencies.

Like its `.WebApi`/`.SignalR` siblings, this package is framework-glue: it converts outcomes your
gRPC service methods already produce (or throw) into safe, client-facing `RpcException`/`Status`
responses. It never references `SharedKernel.Contracts` — protobuf-generated messages are this
package's only wire-contract surface, mirroring `SharedKernel.Communication.Grpc`'s rule.
`SharedKernel.Communication.Grpc` stays outbound-only (client channel/interceptors); this package
owns the inbound gRPC API boundary.

---

## Installation

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.Presentation.Grpc" />
</ItemGroup>
```

Versions come from your single `SharedKernelVersion`; every SharedKernel package ships with the
same version. `SharedKernel.Presentation.Core` comes transitively.

---

## Minimal setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();   // SharedKernel.ServiceDefaults.Security: IRequestContext over IUserContext
builder.Services.AddSharedKernelGrpc();

var app = builder.Build();

app.UseSharedKernelRequestContext();   // optional for a gRPC-only host; first when present
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<OrdersService>();

app.Run();
```

`AddSharedKernelGrpc` registers gRPC plus four global server interceptors — applied to **every**
mapped gRPC service automatically, via `GrpcServiceOptions.Interceptors`, with no per-service
wiring needed (unlike HTTP's `AuthorizationRequirementEndpointFilter`, which needs an explicit
`.AddEndpointFilter<T>()` call per route/group) — and `IRequestContextAccessor` when nothing else
registered it. It returns the stock `IGrpcServerBuilder`.

- **`GrpcExceptionInterceptor`** — the gRPC counterpart to `IExceptionHandler`/
  `SharedKernelExceptionHandler`. Known `SharedKernelException` subtypes (`SharedKernel.Core`) map
  to an `RpcException` via `GrpcStatusCodeMap`; unknown exceptions are logged at `LogLevel.Error`
  (`EventId` 14200) and mapped to `StatusCode.Internal` with detail suppressed outside
  `IHostEnvironment.IsDevelopment()`. Overrides all four server interceptor methods (unary +
  three streaming shapes) — gRPC, unlike HTTP, has more than one call shape that needs mapping.
- **`GrpcCorrelationInterceptor`** — resolves the call's correlation id: the ambient one when
  `UseSharedKernelRequestContext()` already ran for the request, otherwise the inbound
  `X-Correlation-Id` metadata (the key `SharedKernel.Communication.Grpc`'s client interceptor writes),
  accepted only under `CorrelationIds`' one rule (at most 128 characters of `[A-Za-z0-9-_:.]`) and
  created when absent or invalid. It also sets the `Activity` correlation baggage.
- **`GrpcTenantContextInterceptor`** — opens a `RequestContextScope` around the call carrying the
  caller: the ambient `IRequestContext`, else the one registered in the call's request services,
  else `AnonymousRequestContext`, always with the resolved correlation id. Service code, repositories
  and outbound clients read tenant, actor and correlation id through `IRequestContext` /
  `IRequestContextAccessor`. It never reads a tenant from metadata and never rejects a call with no
  tenant; the tenant comes from the authenticated caller (or `SharedKernel.MultiTenancy`).
- **`GrpcAuthorizationInterceptor`** — evaluates `[RequireRole]`/`[RequirePermission]`/
  `[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` (namespace
  `SharedKernel.Presentation.Authorization`) applied directly to a gRPC service implementation class
  or method, read from `ServerCallContext.GetHttpContext()?.GetEndpoint()?.Metadata`. These are the
  identical attribute types your HTTP endpoints use. A rejection is logged (`EventId` 14201).

A conservative `GrpcServiceOptions.MaxReceiveMessageSize`
(`GrpcServiceCollectionExtensions.DefaultMaxReceiveMessageSizeBytes`, 4 MiB) is set before your
`configure` callback runs, so it is always overridable.

```csharp
builder.Services.AddSharedKernelGrpc(options =>
{
    options.MaxReceiveMessageSize = 16 * 1024 * 1024; // override the platform default
});
```

---

## Declarative authorization on a gRPC service method

```csharp
using SharedKernel.Presentation.Authorization;   // SharedKernel.Presentation.Core

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

Lives in `SharedKernel.Presentation.Core`, namespace `SharedKernel.Presentation.Errors`, beside its
HTTP sibling `ErrorTypeStatusCodeMap` — never merged with it. HTTP status codes and gRPC
`StatusCode` are different target enums with no clean 1:1 correspondence (HTTP's 422 has no gRPC
analogue; gRPC's `Aborted`/`FailedPrecondition` cover ground HTTP splits across 409/412/422). Both
maps key off the same `ErrorType` enum (`SharedKernel.Primitives`) — that shared vocabulary is the
generalization point, not a shared value table.

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

## No telemetry entry point of its own

gRPC calls ride the same Kestrel/HTTP2 pipeline ASP.NET Core's existing server-side OpenTelemetry
instrumentation already traces, so this package ships no `WithXTelemetry` extension. This is a
deliberate decision, not a gap.

---

## Related packages

- [`SharedKernel.Presentation.Core`](../SharedKernel.Presentation.Core/README.md) — the attributes and status maps.
- `SharedKernel.Communication.Grpc` — the outbound client side (channel factory, correlation/tenant interceptors).
- `SharedKernel.ServiceDefaults.Security` — `AddSharedKernelRequestContext()` / `UseSharedKernelRequestContext()`.
- [Configuration reference](../CONFIGURATION.md) and the domain [README](../README.md).
