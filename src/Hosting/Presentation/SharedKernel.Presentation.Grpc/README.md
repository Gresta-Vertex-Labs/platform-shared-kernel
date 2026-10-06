# SharedKernel.Presentation.Grpc

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **gRPC services with the same error contract and authorization as a SharedKernel HTTP API, in one call. Every error a
> service method produces reaches the client as a rich `google.rpc.Status`, and nothing from another service's status
> leaks through.**

| You get | So that |
| --- | --- |
| One exception interceptor on every service and all four call shapes | No service method maps an error by hand |
| A `google.rpc.Status` with the status code from `ErrorType`, an `ErrorInfo` (code, domain, trace id, correlation id) and a `BadRequest` of field violations | Clients read one error shape with `RpcException.GetRpcStatus()` |
| The messages an HTTP client gets — translated, server errors redacted outside Development | HTTP and gRPC callers never see different texts |
| Failed `Result`s ended with `GetValueOrThrow()` / `ThrowIfFailure()` (`SharedKernel.Core`) | A method's last line is `await sender.Send(query, ct).GetValueOrThrow()` |
| Any `RpcException` rebuilt with only its status code | A downstream service's details, field paths and trace ids never reach your caller |
| Field violations capped at 50 in about 3 KB | A request with thousands of invalid fields still gets its status |
| `[RequireEndpointPermission]` and its siblings on services, methods and `MapGrpcService<T>()` | One authorization dialect for HTTP and gRPC |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Presentation.Grpc" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** project |
| Depends on | [`SharedKernel.Presentation.Core`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/SharedKernel.Presentation.Core/README.md), `SharedKernel.Core`, `SharedKernel.Configuration`, `Grpc.AspNetCore`, `Grpc.StatusProto`, `Google.Api.CommonProtos` — never `SharedKernel.Presentation.WebApi` or `SharedKernel.Contracts` |
| Namespaces | `SharedKernel.Presentation.Grpc` |

A service that compiles its own `.proto` files also references `Grpc.AspNetCore` directly (it brings `Grpc.Tools` for
`<Protobuf Include="…" GrpcServices="Server" />`). The client side is
[`SharedKernel.Communication.Grpc`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Communication/SharedKernel.Communication.Grpc/README.md).

## Quick start

```csharp
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.Grpc;
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Security;

builder.Services.AddSharedKernelRequestContext();   // each call's caller, tenant and correlation id
builder.AddSharedKernelWebApi();
builder.AddSharedKernelGrpc(options => options.ErrorDomain = "orders.example.com");

var app = builder.Build();
app.UseSharedKernelRequestContext();   // first: each call's RequestContextScope
app.UseSharedKernelWebApi();           // authentication and authorization — gRPC calls included

app.MapGrpcService<OrderGrpcService>().RequireEndpointPermission("orders.read");
app.Run();
```

```csharp
using Grpc.Core;
using SharedKernel.Application.Messaging;        // ISender
using SharedKernel.Core.Extensions;              // GetValueOrThrow, ThrowIfFailure
using SharedKernel.Presentation.Authorization;

public sealed class OrderGrpcService(ISender sender) : OrderService.OrderServiceBase
{
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        var order = await sender.Send(new GetOrderQuery(Guid.Parse(request.Id)), context.CancellationToken)
            .GetValueOrThrow();
        return new OrderReply { Id = order.Id.ToString(), Status = order.Status };
    }

    [RequireFreshAuthentication(300)]   // CancelOrderCommand carries its own [RequirePermission]
    public override async Task<CancelOrderReply> CancelOrder(CancelOrderRequest request, ServerCallContext context)
    {
        await sender.Send(new CancelOrderCommand(Guid.Parse(request.Id)), context.CancellationToken).ThrowIfFailure();
        return new CancelOrderReply();
    }
}
```

A gRPC-only host (no `UseSharedKernelWebApi()`) calls `UseSharedKernelRequestContext()`, `UseRouting()`,
`UseAuthentication()` and `UseAuthorization()` before mapping services.

## How it works

- `AddSharedKernelGrpc()` calls `AddGrpc()` with the exception interceptor, registers `Presentation.Core`'s
  authorization policies, binds and validates `SharedKernel:Presentation:Grpc`, and returns `IGrpcServerBuilder`. It is
  idempotent. Global interceptors run in the order added, the first outermost — call it before adding your own.
- **The status.** Every error ends as a `google.rpc.Status` in the `grpc-status-details-bin` trailer: `code` from the
  `ErrorType`, `message` = the client message (the same text as an HTTP problem's `detail`); `ErrorInfo` with
  `reason` = the error code, `domain` = `ErrorDomain`, `metadata` = `traceId` and `correlationId`; and, when the error
  has field errors, a `BadRequest` (one violation per field error: `field`, `description`, `reason`).

  | `ErrorType` | gRPC status | `ErrorType` | gRPC status |
  | --- | --- | --- | --- |
  | `Validation` | `InvalidArgument` | `BusinessRule` | `FailedPrecondition` |
  | `Unauthorized` | `Unauthenticated` | `Unexpected` | `Internal` |
  | `Forbidden` | `PermissionDenied` | `Unavailable` | `Unavailable` |
  | `NotFound` | `NotFound` | `Timeout` | `DeadlineExceeded` |
  | `Conflict` | `Aborted` | anything else | `Unknown` |

- **What the interceptor does with each exception:**

  | Thrown | Status | Logged |
  | --- | --- | --- |
  | Anything once the call is cancelled (client or deadline) | `Cancelled` | Debug (14204) |
  | `ValidationException` | `InvalidArgument`, as a returned validation error | Debug (14203) |
  | Any other `SharedKernelException` (incl. `GetValueOrThrow()`/`ThrowIfFailure()`) | Its error's status | Debug (14203); Error (14202) for `Unexpected`/`Unavailable`/`Timeout` |
  | `RpcException` (own, or from a downstream call) | Rebuilt: its code, this service's `ErrorInfo` (`grpc.{status}`), none of its trailers; generic text outside Development for `Unknown`, `Internal`, `DataLoss`, `Unavailable`, `DeadlineExceeded`; a thrown `OK` becomes `Unknown` | Debug (14203); Error (14202) for those five |
  | `TimeoutException`, or a cancellation while the call is open | `DeadlineExceeded`, `timeout.default` | Error (14202) |
  | Anything else | `Internal`, `unexpected.exception`, generic message (the exception's in Development) | Error (14200) |

- **Capped violations.** Status travels in HTTP/2 trailers, which many clients cap at 8 KB. A `BadRequest` lists at most
  50 violations in about 3 KB; a last violation `grpc.more_field_violations` says how many were left out (translated
  with a `{count}` placeholder).
- **Authorization.** Refused calls never reach the method and carry a status and headers only (no rich status):
  anonymous → `Unauthenticated`; missing permission or role → `PermissionDenied`; failing only freshness or method →
  `Unauthenticated` with the RFC 9470 step-up challenge. A call is authorized once, when it starts.
- **Caller, tenant, correlation id.** A gRPC call runs through the HTTP pipeline, so `UseSharedKernelRequestContext()`
  opens its scope; inject `IRequestContext` as in an HTTP handler. The correlation id is the inbound
  `x-correlation-id` when valid, else a new one, and is the `correlationId` of every error status.
- **Telemetry.** gRPC calls are ASP.NET Core requests, traced and measured by its own instrumentation.

## Configuration

Section `SharedKernel:Presentation:Grpc` (`SharedKernelGrpcOptions`), validated at host start; `configure` runs after
binding. See [CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/CONFIGURATION.md#sharedkernelpresentationgrpc).

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Presentation:Grpc:ErrorDomain` | `string` | The application name | The `ErrorInfo` domain — the logical owner of this service's error codes (`orders.example.com`). Blank falls back to the application name; keep it stable once clients depend on it |

## Reference

### Registration

| Member | Purpose |
| --- | --- |
| `IHostApplicationBuilder.AddSharedKernelGrpc(Action<SharedKernelGrpcOptions>?)` | Interceptor, authorization, options; returns `IGrpcServerBuilder` |
| `SharedKernelGrpcOptions` | `ErrorDomain`, `SectionName` |
| `GrpcErrorCodes.MoreFieldViolations` | `"grpc.more_field_violations"` |

Reading the status on the client: `exception.GetRpcStatus()` (`Grpc.Core`) and `status.GetDetail<ErrorInfo>()` /
`GetDetail<BadRequest>()` (`Google.Rpc`), from `Grpc.StatusProto` — or `SharedKernel.Communication.Grpc`'s
`ToResultAsync()`, which reads it back into an `Error`.

### Errors

| Code | When |
| --- | --- |
| `grpc.{status}` (`grpc.resource_exhausted`, `grpc.unavailable`, …) | `ErrorInfo.reason` of a rebuilt `RpcException` |
| `grpc.more_field_violations` | The last violation of a capped `BadRequest` |
| `timeout.default` | A `TimeoutException`, or a cancellation while the call is open |
| `unexpected.exception` | An exception that is neither a `SharedKernelException` nor an `RpcException` |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 14200 | Error | An exception that is neither a `SharedKernelException`, an `RpcException` nor a timeout |
| 14202 | Error | A server error: `Unexpected`, `Unavailable`, `Timeout`, or a rebuilt `RpcException` of a server category |
| 14203 | Debug | A client error: every other error, `ValidationException` and rebuilt `RpcException` |
| 14204 | Debug | Any exception after the call was cancelled |

Every entry carries the exception and the gRPC method; 14202 and 14203 add the status and error code. Authorization
refusals are logged by `Presentation.Core` (14002).

## Testing

Call a method directly with [`SharedKernel.Presentation.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/SharedKernel.Presentation.Testing/README.md)'s
`TestServerCallContext` (namespace `SharedKernel.Testing.Grpc`). It runs without the interceptor, so a failed result
is the exception `GetValueOrThrow()` threw — assert its `Error`:

```csharp
using SharedKernel.Core.Exceptions;
using SharedKernel.Testing.Grpc;

ServerCallContext context = TestServerCallContext.Create(method: "GetOrder");
var exception = await Assert.ThrowsAsync<NotFoundException>(() => service.GetOrder(request, context));
Assert.Equal("order.not_found", exception.Error.Code);
```

A hand-built context never passes the HTTP pipeline: open `RequestContextScope.Begin(TestRequestContext.ForTenant(…))`
around a method that reads `IRequestContext`. The rich status and authorization need a real host, which can run in
process with `Microsoft.AspNetCore.TestHost`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Construct `RpcException` or `Status` yourself | Return a `Result` and end it with `GetValueOrThrow()`/`ThrowIfFailure()` | SK0036 flags it; a thrown `RpcException` keeps only its code |
| Add your own interceptors before `AddSharedKernelGrpc()` | Call `AddSharedKernelGrpc()` first | Earlier interceptors run outside the exception interceptor |
| Rely on a step-up requirement to bound a long stream | Keep step-up operations unary, or check `GetAuthenticationMethodTime` inside the stream | A call is authorized once, when it starts |
| Change `ErrorDomain` after clients depend on it | Pick a stable domain up front | Clients branch on `(domain, reason)` |
| Send sensitive text in an `RpcException` you catch from downstream | Map it to a `Result` error of your own | Downstream details are dropped by design |

## Design decisions

**Why rebuild every `RpcException`?** It may come from anywhere: a downstream service's status carries its own
`ErrorInfo`, domain, trace ids and field paths, and in Development its internals. None of that belongs to this
service's caller. A method that needs trailers of its own adds them to `context.ResponseTrailers`.

**Why a separate status map instead of Core's?** gRPC and HTTP codes do not correspond one to one; HTTP's 412 rule
has no gRPC counterpart, so a version conflict is `Aborted`.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Presentation domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
