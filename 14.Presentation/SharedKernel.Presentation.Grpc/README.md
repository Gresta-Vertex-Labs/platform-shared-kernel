# SharedKernel.Presentation.Grpc

> **gRPC services with the same error contract and authorization as a SharedKernel HTTP API, in one call — every
> failure reaches the client as a rich `google.rpc.Status`, and nothing from another service's status leaks through.**

The server-side gRPC sibling of [`SharedKernel.Presentation.WebApi`](../SharedKernel.Presentation.WebApi/README.md):
gRPC calls run through the same ASP.NET Core pipeline, so they share its error presentation (status category,
translation, redaction), its correlation ids and its authorization. The client side is
`SharedKernel.Communication.Grpc`. Protobuf messages are a gRPC service's wire contract, so this package never
references `SharedKernel.Contracts`.

| You get | So that |
| --- | --- |
| One exception interceptor on every service and all four call shapes | No service method maps an error by hand |
| A `google.rpc.Status` for every failure: the status code from `ErrorType`, the client message, an `ErrorInfo` (error code, error domain, trace id, correlation id) and a `BadRequest` with the field violations | Clients read one error shape with `RpcException.GetRpcStatus()`, whatever the service |
| The messages an HTTP client gets: translated into the request culture, server errors redacted outside Development | HTTP and gRPC callers of a service never see different texts |
| Failed `Result`s ended with `SharedKernel.Core`'s `GetValueOrThrow()`/`ThrowIfFailure()` | A method's last line is `return Map(await sender.Send(query, ct).GetValueOrThrow());` |
| An `RpcException` of the service, or received from another service, rebuilt with only its status code | Another service's error details, field paths and trace ids never reach your caller |
| Field violations capped at 50 in about 3 KB, the rest summed up | A request with thousands of invalid fields still gets its status (clients cap trailers at 8 KB) |
| Any exception after the call is cancelled ending as `Cancelled`, logged at Debug | A client that goes away is not an error in your logs |
| `[RequirePermission]`, `[RequireRole]`, `[RequireFreshAuthentication]`, `[RequireAuthenticationMethod]` on a service or method, and as `MapGrpcService<T>()` conventions | One authorization dialect for HTTP and gRPC |

## Install

```xml
<PackageReference Include="SharedKernel.Presentation.Grpc" Version="x.y.z" />
```

It references `SharedKernel.Presentation.WebApi` (the shared error presentation and authorization), `Grpc.AspNetCore`
and `Grpc.StatusProto`.

## Use

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddSharedKernelWebApi();
builder.AddSharedKernelGrpc(options => options.ErrorDomain = "orders.example.com");

var app = builder.Build();
app.UseSharedKernelWebApi();   // correlation ids, authentication, authorization — gRPC calls included

app.MapGrpcService<OrdersService>().RequirePermission("orders.read");

app.Run();
```

A service method ends a failed `Result` with `SharedKernel.Core`'s extensions, and the interceptor turns the exception
they throw into the rich status:

```csharp
using SharedKernel.Core.Extensions;       // GetValueOrThrow, ThrowIfFailure
using SharedKernel.Presentation.WebApi;   // RequirePermission, RequireFreshAuthentication

public sealed class OrdersService(ISender sender) : Orders.OrdersBase
{
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        var order = await sender.Send(new GetOrder(request.Id), context.CancellationToken).GetValueOrThrow();
        return new OrderReply { Id = order.Id, Status = order.Status };
    }

    [RequirePermission("orders.cancel")]
    [RequireFreshAuthentication(300)]
    public override async Task<Empty> CancelOrder(CancelOrderRequest request, ServerCallContext context)
    {
        await sender.Send(new CancelOrder(request.Id), context.CancellationToken).ThrowIfFailure();
        return new Empty();
    }
}
```

`GetValueOrThrow()` and `ThrowIfFailure()` exist for `Result`, `Result<T>`, `Task<…>` and `ValueTask<…>`. They throw
`Error.ToException()`: the `SharedKernelException` of the error's type (a `DomainException` for `Unavailable` and
`Timeout`), carrying the error unchanged. This package deliberately has no result extensions of its own: until P-562
it declared ones with identical signatures, and a file importing both namespaces could not compile (CS0121).

Without `UseSharedKernelWebApi()`, call `UseRouting()`, `UseAuthentication()` and `UseAuthorization()` before mapping
services; errors keep their rich status but carry no correlation id. `AddSharedKernelGrpc()` returns the
`IGrpcServerBuilder` of `AddGrpc()`. Global interceptors run in the order they are added, the first outermost: call it
before adding your own, so exceptions they throw are mapped too.

## The error status

Every failure ends as a `google.rpc.Status` in the `grpc-status-details-bin` trailer:

- `code`: from the error's `ErrorType` (below); `message`: the client message (the same text as an HTTP problem's
  `detail`).
- `ErrorInfo`: `reason` = the error code, `domain` = `ErrorDomain`, `metadata` = `traceId` and `correlationId` (the
  names and values of the HTTP problem members).
- `BadRequest`, when the error has field errors (`Error.Details`, as HTTP's `errors`): one violation per field error —
  `field` = its field path (its code when it names none), `description` = its client message, `reason` = its code.

```csharp
catch (RpcException exception) when (exception.GetRpcStatus() is { } status)
{
    var errorInfo = status.GetDetail<ErrorInfo>();          // errorInfo.Reason == "order.not_found"
    var badRequest = status.GetDetail<BadRequest>();        // null unless there were field errors
}
```

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

`GrpcStatusCodeMap.Resolve(ErrorType)` is this table; it is a sibling of the HTTP core's `ErrorTypeStatusCodeMap`, not
a merge, because gRPC and HTTP status codes do not correspond one to one.

### What the interceptor does with each exception

| Thrown | Status | Logged |
| --- | --- | --- |
| Anything, once the call is cancelled (client cancellation or deadline): `OperationCanceledException`, the `IOException` of an aborted stream, the `InvalidOperationException` of a write to a completed call, … | `Cancelled` | Debug (14204) |
| `ValidationException` | `InvalidArgument`, exactly the status of a returned error of the same field errors: one error is itself, several are `Error.Validation(errors)` | Debug (14203) |
| Any other `SharedKernelException` — including those of `GetValueOrThrow()`/`ThrowIfFailure()` | Its error's status | Debug (14203), or Error (14202) for `Unexpected`, `Unavailable`, `Timeout` |
| `RpcException` (the service's own, or from a gRPC call to another service) | Rebuilt: its code, this service's `ErrorInfo` (`reason` = `grpc.{status}`), none of its trailers; its detail for a client category, the generic sentence outside Development for `Unknown`, `Internal`, `DataLoss`, `Unavailable`, `DeadlineExceeded` | Debug (14203), or Error (14202) for those five |
| Anything else | `Internal` `unexpected.exception`, a generic message (the exception message in Development) | Error (14200) |

The generic sentences are those of the HTTP core ("An unexpected error occurred.", "The service is temporarily
unavailable. Try again later.", "The operation did not complete in time."), translated under `unexpected.exception`,
`unavailable.default` and `timeout.default`.

An `RpcException` is rebuilt because it may come from anywhere: a downstream service's status carries its own
`ErrorInfo`, domain, trace ids and field paths, and in Development its internals. None of that reaches this service's
caller. To send trailers of its own, a method adds them to `context.ResponseTrailers`. A thrown status of `OK` is no
answer and ends as `Unknown`.

### Field violations are capped

A status travels in HTTP/2 trailers, which clients cap — 8 KB by default for many gRPC clients, 64 KB for
`Grpc.Net.Client` — and a call whose status is larger fails at the client with an unrelated error. A `BadRequest` lists
at most 50 violations in at most about 3 KB; when there are more, a last violation says how many were left out:

| `field` | `description` | `reason` |
| --- | --- | --- |
| `grpc.more_field_violations` | `… and 2950 more.` | `grpc.more_field_violations` |

Its description is translated under `grpc.more_field_violations` with a `{count}` placeholder
(`GrpcErrorCodes.MoreFieldViolations`).

### Codes this package produces

| Code | When |
| --- | --- |
| `grpc.{status}`, such as `grpc.resource_exhausted` or `grpc.unavailable` | `ErrorInfo.reason` of a rebuilt `RpcException`: `grpc.` and the status's canonical name in lower case (`GrpcErrorCodes.ForStatus`) |
| `grpc.more_field_violations` | The last violation of a capped `BadRequest` |
| `unexpected.exception` | An exception that is neither a `SharedKernelException` nor an `RpcException` |

## Authorization

`AddSharedKernelGrpc()` registers the HTTP core's authorization, so the attributes and conventions of
`SharedKernel.Presentation.WebApi` are native ASP.NET Core authorization for gRPC too: on a service class or method,
and on `MapGrpcService<T>()`. Requirements of different attributes all apply (AND); the values of one attribute are
alternatives (OR).

| Caller | Status |
| --- | --- |
| Anonymous | `Unauthenticated` |
| Signed in, without the permission or role | `PermissionDenied` |
| Signed in, failing only a freshness or authentication-method requirement | `Unauthenticated`, with the RFC 9470 `WWW-Authenticate` step-up challenge |

The method never runs for a refused call. Refusals carry no rich status.

## Correlation and tenant

A gRPC call gets its correlation id from the HTTP pipeline's correlation middleware: in a method it is
`context.GetHttpContext().GetCorrelationId()`, and it is the `correlationId` of every error status. The caller and the
tenant come from an injected `IUserContext`, `ITenantProvider` or `IRequestContext`.

## Settings

Bound from `SharedKernel:Presentation:Grpc` and validated when the host starts; the `configure` callback runs after
binding.

| Setting | Default | Meaning |
| --- | --- | --- |
| `ErrorDomain` | application name | The `ErrorInfo` domain: the logical owner of the error codes this service returns, such as `orders.example.com`. Must not be empty; keep it stable once clients depend on it |

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

## Unit-testing a service method

Called directly with a hand-built `ServerCallContext` (such as `Grpc.Core.Testing`'s `TestServerCallContext.Create(…)`),
a method runs without the interceptor: a failed result is the exception `GetValueOrThrow()` threw, so assert the error
it carries. The rich status is the interceptor's; a client sees it from a real host, which can run in process with
`Microsoft.AspNetCore.TestHost`.

```csharp
var exception = await Assert.ThrowsAsync<NotFoundException>(() => service.GetOrder(request, context));
Assert.Equal("order.not_found", exception.Error.Code);
```

## Logging

| EventId | Level | When |
| --- | --- | --- |
| 14200 | Error | An exception that is neither a `SharedKernelException` nor an `RpcException` |
| 14202 | Error | A server error: an error of type `Unexpected`, `Unavailable` or `Timeout`, or a rebuilt `RpcException` of a server category |
| 14203 | Debug | A client error: every other error, `ValidationException` and rebuilt `RpcException` |
| 14204 | Debug | Any exception after the call was cancelled |

Every entry carries the exception and the gRPC method; 14202 and 14203 carry the status code and the error code.
14201 belonged to a removed authorization interceptor and is not reused.

## Telemetry

gRPC calls are ASP.NET Core requests, traced and measured by its own instrumentation; there is no gRPC-specific
`WithXTelemetry` entry point.
