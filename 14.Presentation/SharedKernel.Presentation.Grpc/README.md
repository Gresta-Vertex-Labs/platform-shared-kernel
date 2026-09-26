# SharedKernel.Presentation.Grpc

> **gRPC services with the same error contract and authorization as a SharedKernel HTTP API, in one call. Every error a
> service method produces reaches the client as a rich `google.rpc.Status`, and nothing from another service's status
> leaks through.**

The server-side gRPC sibling of
[`SharedKernel.Presentation.WebApi`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/14.Presentation/SharedKernel.Presentation.WebApi).
gRPC calls run through the same ASP.NET Core pipeline, so they share the request context of
`SharedKernel.ServiceDefaults.Security`'s `UseSharedKernelRequestContext()` (caller, tenant, correlation id) and the
error presentation (status category, translation, redaction) and authorization of `SharedKernel.Presentation.Core`. It
never references the WebApi package, so a gRPC host takes no HTTP API stack (P-579). The client side is
`SharedKernel.Communication.Grpc` (11.Communication). Protobuf messages are a gRPC service's wire contract, so this
package never references `SharedKernel.Contracts`.

| You get | So that |
| --- | --- |
| One exception interceptor on every service and all four call shapes | No service method maps an error by hand |
| A `google.rpc.Status` for every error: the status code from `ErrorType`, the client message, an `ErrorInfo` (error code, error domain, trace id, correlation id) and a `BadRequest` with the field violations | Clients read one error shape with `RpcException.GetRpcStatus()`, whatever the service |
| The messages an HTTP client gets: translated into the request culture, server errors redacted outside Development | HTTP and gRPC callers of a service never see different texts |
| Failed `Result`s ended with `SharedKernel.Core`'s `GetValueOrThrow()` or `ThrowIfFailure()` | A method's last line is `await sender.Send(query, ct).GetValueOrThrow()` |
| An `RpcException` of the service, or from a call to another service, rebuilt with only its status code | Another service's error details, field paths and trace ids never reach your caller |
| Field violations capped at 50 in about 3 KB, the rest summed up | A request with thousands of invalid fields still gets its status (many clients cap trailers at 8 KB) |
| Any exception after the call is cancelled ending as `Cancelled`, logged at Debug | A client that goes away is not an error in your logs |
| `[RequireEndpointPermission]`, `[RequireRole]`, `[RequireFreshAuthentication]`, `[RequireAuthenticationMethod]` on a service or method, and as `MapGrpcService<T>()` conventions | One authorization dialect for HTTP and gRPC |

## Install

```shell
dotnet add package SharedKernel.Presentation.Grpc
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host (referenced by a service's API project) |
| Dependencies | `SharedKernel.Presentation.Core`, `SharedKernel.Core`, `SharedKernel.Configuration`, `Grpc.AspNetCore`, `Grpc.StatusProto`, `Google.Api.CommonProtos` — never `SharedKernel.Presentation.WebApi` or `SharedKernel.Contracts` |
| Composed with | `SharedKernel.ServiceDefaults.Security` (the request context), and optionally `SharedKernel.Presentation.WebApi` for a host that also serves HTTP |

A service that compiles its own `.proto` files also references `Grpc.AspNetCore` directly, which brings `Grpc.Tools`
for the `<Protobuf Include="…" GrpcServices="Server" />` items.

## Use

```csharp
using SharedKernel.Presentation.Authorization;   // RequireEndpointPermission
using SharedKernel.Presentation.Grpc;
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();   // each call's caller, tenant and correlation id
builder.AddSharedKernelWebApi();
builder.AddSharedKernelGrpc(options => options.ErrorDomain = "orders.example.com");

var app = builder.Build();
app.UseSharedKernelRequestContext();   // first: each call's RequestContextScope and correlation id
app.UseSharedKernelWebApi();           // authentication, authorization — gRPC calls included

app.MapGrpcService<OrderGrpcService>().RequireEndpointPermission("orders.read");

app.Run();
```

A service method ends a failed `Result` with `SharedKernel.Core`'s extensions, and the interceptor turns the exception
they throw into the rich status. For this contract:

```proto
syntax = "proto3";

option csharp_namespace = "Orders.Grpc";

service OrderService {
  rpc GetOrder (GetOrderRequest) returns (OrderReply);
  rpc CancelOrder (CancelOrderRequest) returns (CancelOrderReply);
}

message GetOrderRequest { string id = 1; }
message CancelOrderRequest { string id = 1; }
message OrderReply { string id = 1; string status = 2; }
message CancelOrderReply {}
```

the service reads:

```csharp
using Grpc.Core;
using SharedKernel.Application.Messaging; // ISender
using SharedKernel.Core.Extensions;       // GetValueOrThrow, ThrowIfFailure
using SharedKernel.Presentation.Authorization;   // RequireEndpointPermission, RequireFreshAuthentication

[RequireEndpointPermission("orders.read")]      // guards every method of the service
public sealed class OrderGrpcService(ISender sender) : OrderService.OrderServiceBase
{
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        var order = await sender.Send(new GetOrderQuery(Guid.Parse(request.Id)), context.CancellationToken).GetValueOrThrow();
        return new OrderReply { Id = order.Id.ToString(), Status = order.Status };
    }

    // CancelOrderCommand declares its own [RequirePermission]; the method adds authentication strength.
    [RequireFreshAuthentication(300)]
    public override async Task<CancelOrderReply> CancelOrder(CancelOrderRequest request, ServerCallContext context)
    {
        await sender.Send(new CancelOrderCommand(Guid.Parse(request.Id)), context.CancellationToken).ThrowIfFailure();
        return new CancelOrderReply();
    }
}
```

A service method sends `05.Application` commands and queries through `ISender`, as an HTTP endpoint does.

- `GetValueOrThrow()` and `ThrowIfFailure()` exist for `Result`, `Result<T>`, `Task<…>` and `ValueTask<…>`. They throw
  `Error.ToException()`: the `SharedKernelException` of the error's type (a `DomainException` for `Unavailable` and
  `Timeout`), carrying the error unchanged. This package has no result extensions of its own, so importing both
  namespaces never makes a call ambiguous.
- `AddSharedKernelGrpc()` calls `AddGrpc()` with the exception interceptor, registers the shared authorization
  policies of `SharedKernel.Presentation.Core`, and returns `AddGrpc()`'s `IGrpcServerBuilder`. It binds `SharedKernel:Presentation:Grpc`, validates it when the host
  starts and is idempotent.
- Global interceptors run in the order they are added, the first outermost: call it before adding your own, so the
  exceptions they throw are mapped too.
- Without `UseSharedKernelWebApi()` (a gRPC-only host), call `UseSharedKernelRequestContext()`, `UseRouting()`,
  `UseAuthentication()` and `UseAuthorization()` before mapping services; errors keep their rich status and their
  correlation id. Without `UseSharedKernelRequestContext()` they carry no correlation id and methods see no scope.

## The error status

Every error ends as a `google.rpc.Status` in the `grpc-status-details-bin` trailer:

- `code`: from the error's `ErrorType` (below); `message`: the client message, the same text as an HTTP problem's
  `detail`.
- `ErrorInfo`: `reason` = the error code, `domain` = `ErrorDomain`, `metadata` = `traceId` and `correlationId` (the
  names and values of the HTTP problem members).
- `BadRequest`, when the error has field errors (`Error.Details`, as HTTP's `errors`): one violation per field error,
  with `field` = its field path (its code when it names none), `description` = its client message, `reason` = its code.

```csharp
catch (RpcException exception) when (exception.GetRpcStatus() is { } status)
{
    var errorInfo = status.GetDetail<ErrorInfo>();     // errorInfo.Reason == "order.not_found"
    var badRequest = status.GetDetail<BadRequest>();   // null unless there were field errors
}
```

`GetRpcStatus()` (namespace `Grpc.Core`) and `GetDetail<T>()` (namespace `Google.Rpc`) come from `Grpc.StatusProto`.

| `ErrorType` | gRPC status |
| --- | --- |
| `Validation` | `InvalidArgument` |
| `Unauthorized` | `Unauthenticated` |
| `Forbidden` | `PermissionDenied` |
| `NotFound` | `NotFound` |
| `Conflict` | `Aborted` |
| `BusinessRule` | `FailedPrecondition` |
| `Unexpected` | `Internal` |
| `Unavailable` | `Unavailable` |
| `Timeout` | `DeadlineExceeded` |
| `None` or any other value | `Unknown` |

The table is internal to this package, a sibling of the HTTP core's status map rather than a merge: gRPC and HTTP
status codes do not correspond one to one. HTTP's 412 rule has no gRPC counterpart; a version
conflict is `Aborted`.

### What the interceptor does with each exception

| Thrown | Status | Logged |
| --- | --- | --- |
| Anything, once the call is cancelled (client cancellation or deadline): `OperationCanceledException`, the `IOException` of an aborted stream, the `InvalidOperationException` of a write to a completed call, … | `Cancelled`, "The call was cancelled." | Debug (14204) |
| `ValidationException` | `InvalidArgument`, exactly the status of a returned error of the same field errors: one error is itself, several are `Error.Validation(errors)` | Debug (14203) |
| Any other `SharedKernelException`, including those of `GetValueOrThrow()` and `ThrowIfFailure()` | Its error's status | Debug (14203), or Error (14202) for `Unexpected`, `Unavailable`, `Timeout` |
| `RpcException` (the service's own, or from a gRPC call to another service) | Rebuilt: its code, this service's `ErrorInfo` (`reason` = `grpc.{status}`), none of its trailers; its detail for a client category, the generic sentence outside Development for `Unknown`, `Internal`, `DataLoss`, `Unavailable`, `DeadlineExceeded`. A thrown `OK` ends as `Unknown` | Debug (14203), or Error (14202) for those five |
| `TimeoutException`, or an `OperationCanceledException` while the call is not cancelled (a timeout inside the service) | `DeadlineExceeded` `timeout.default`, the status of a returned `Error.Timeout`, as HTTP answers 504 | Error (14202) |
| Anything else | `Internal` `unexpected.exception`, a generic message (the exception message in Development) | Error (14200) |

The generic sentences are those of the HTTP core ("An unexpected error occurred.", "The service is temporarily
unavailable. Try again later.", "The operation did not complete in time."), translated under `unexpected.exception`,
`unavailable.default` and `timeout.default`.

An `RpcException` is rebuilt because it may come from anywhere: a downstream service's status carries its own
`ErrorInfo`, domain, trace ids and field paths, and in Development its internals. None of that reaches this service's
caller. To send trailers of its own, a method adds them to `context.ResponseTrailers`.

### Field violations are capped

A status travels in HTTP/2 trailers, which clients cap (8 KB by default for many gRPC clients, 64 KB for
`Grpc.Net.Client`); a call whose status is larger fails at the client with an unrelated error. A `BadRequest` lists at
most 50 violations in at most about 3 KB; when there are more, a last violation says how many were left out:

| `field` | `description` | `reason` |
| --- | --- | --- |
| `grpc.more_field_violations` | `… and 2950 more.` | `grpc.more_field_violations` |

Its description is translated under `grpc.more_field_violations` with a `{count}` placeholder.

### Codes this package produces

| Code | When |
| --- | --- |
| `grpc.{status}`, such as `grpc.resource_exhausted` or `grpc.unavailable` | `ErrorInfo.reason` of a rebuilt `RpcException`: `grpc.` and the status's canonical name in lower case |
| `grpc.more_field_violations` | The last violation of a capped `BadRequest` (`GrpcErrorCodes.MoreFieldViolations`) |
| `timeout.default` | A `TimeoutException`, or a cancellation while the call is open |
| `unexpected.exception` | An exception that is neither a `SharedKernelException` nor an `RpcException` |

## Authorization

`AddSharedKernelGrpc()` registers `SharedKernel.Presentation.Core`'s authorization, so the attributes and conventions
(`using SharedKernel.Presentation.Authorization;`) are native
ASP.NET Core authorization for gRPC too: on a service class or method, and on `MapGrpcService<T>()`. Requirements of
different attributes all apply (AND); the values of one attribute are alternatives (OR).

| Caller | Status |
| --- | --- |
| Anonymous | `Unauthenticated` |
| Signed in, without the permission or role | `PermissionDenied` |
| Signed in, failing only a freshness or authentication-method requirement | `Unauthenticated`, with the RFC 9470 `WWW-Authenticate` step-up challenge |

- The method never runs for a refused call. Refusals are answered by the HTTP pipeline with a status and headers only:
  they carry no rich status and no `ErrorInfo`.
- A call is authorized once, when it starts. A streaming call that started in time keeps running past a step-up's
  maximum age: keep step-up operations in unary calls, or check `IUserContext.GetAuthenticationMethodTime(…)` inside
  the stream.

## Correlation, caller and tenant

A gRPC call runs through the HTTP pipeline, so `UseSharedKernelRequestContext()` (`SharedKernel.ServiceDefaults.Security`)
opens its `RequestContextScope` before the method runs — no gRPC interceptor is involved. In a method, read the caller,
tenant and correlation id from an injected `IRequestContext` (or `IRequestContextAccessor`), exactly as in an HTTP
handler; `IUserContext` gives the caller's claims. The correlation id is the inbound `x-correlation-id` metadata when it
is valid (`CorrelationIds.IsValid`), else a new one, and it is the `correlationId` of every error status.

```csharp
public sealed class OrderGrpcService(ISender sender, IRequestContext caller) : OrderService.OrderServiceBase
{
    // caller.UserId, caller.TenantId, caller.CorrelationId: the call's, as UseSharedKernelRequestContext() resolved them
}
```

## Settings

Bound from `SharedKernel:Presentation:Grpc` and validated when the host starts; the `configure` callback runs after
binding.

| Setting | Default | Meaning |
| --- | --- | --- |
| `ErrorDomain` | The application name | The `ErrorInfo` domain: the logical owner of this service's error codes, such as `orders.example.com`. A blank value falls back to the application name; keep it stable once clients depend on it |

```json
{
  "SharedKernel": {
    "Presentation": {
      "Grpc": {
        "ErrorDomain": "orders.example.com"
      }
    }
  }
}
```

See [CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/CONFIGURATION.md#sharedkernelpresentationgrpc).

## Unit-testing a service method

Called directly with a hand-built `ServerCallContext` (such as `Grpc.Core.Testing`'s `TestServerCallContext`), a
method runs without the interceptor: a failed result is the exception `GetValueOrThrow()` threw, so assert the error
it carries. The rich status is the interceptor's; a client sees it from a real host, which can run in process with
`Microsoft.AspNetCore.TestHost`.

```csharp
var context = TestServerCallContext.Create(
    "GetOrder", null, DateTime.UtcNow.AddMinutes(1), new Metadata(), CancellationToken.None, "peer", null, null,
    _ => Task.CompletedTask, () => new WriteOptions(), _ => { });

var exception = await Assert.ThrowsAsync<NotFoundException>(() => service.GetOrder(request, context));
Assert.Equal("order.not_found", exception.Error.Code);
```

## Logging

EventIds 14200–14299.

| EventId | Level | Event |
| --- | --- | --- |
| 14200 | Error | An exception that is neither a `SharedKernelException`, an `RpcException` nor a timeout |
| 14202 | Error | A server error: an error of type `Unexpected`, `Unavailable` or `Timeout`, or a rebuilt `RpcException` of a server category |
| 14203 | Debug | A client error: every other error, `ValidationException` and rebuilt `RpcException` |
| 14204 | Debug | Any exception after the call was cancelled |

Every entry carries the exception and the gRPC method; 14202 and 14203 carry the status code and the error code.
14201 belonged to a removed authorization interceptor and is not reused. Authorization refusals are logged by
`SharedKernel.Presentation.Core` (14002).

## Telemetry

gRPC calls are ASP.NET Core requests, traced and measured by its own instrumentation; there is no gRPC-specific
server telemetry entry point.

## Pitfalls

- **Constructing `RpcException` or `Status` yourself.** 00.Governance's SK0036 flags it outside this package: return a
  `Result` and end it with `GetValueOrThrow()` or `ThrowIfFailure()`, which gets the rich status. A thrown
  `RpcException` keeps only its code.
- **Own interceptors added before `AddSharedKernelGrpc()`.** They run outside the exception interceptor, so their
  exceptions are not mapped.
- **The request context in unit tests.** A hand-built context never passes the HTTP pipeline, so no scope is open: a
  test of a method that reads `IRequestContext` opens `RequestContextScope.Begin(…)` itself
  (`SharedKernel.Presentation.Testing`'s `TestServerCallContext` and `SharedKernel.Testing`'s `TestRequestContext`).

## Not in this package

| Looking for | Use instead |
| --- | --- |
| `ToGrpcResult`, the gRPC `ThrowIfFailure`/`GetValueOrThrow` | `SharedKernel.Core`'s `GetValueOrThrow()` and `ThrowIfFailure()` |
| `GrpcCorrelationInterceptor` | `UseSharedKernelRequestContext()`'s correlation id: an injected `IRequestContext.CorrelationId` |
| `GrpcTenantContextInterceptor`, `ServerCallContext.UserState` keys | An injected `IRequestContext` (the call's scope) |
| `GrpcAuthorizationInterceptor` | The `SharedKernel.Presentation.Core` attributes and conventions, enforced by ASP.NET Core authorization |
| `GrpcStatusCodeMap` in `SharedKernel.Presentation.Core` (WO-086 P-570) | It is internal to this package again (P-579) |
| A `MaxReceiveMessageSize` pin | gRPC's own default (4 MiB), or `GrpcServiceOptions.MaxReceiveMessageSize` |
| `AddSharedKernelGrpc(IServiceCollection, …)` | `builder.AddSharedKernelGrpc(…)` on the host builder |
