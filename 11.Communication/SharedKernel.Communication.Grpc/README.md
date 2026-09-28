# SharedKernel.Communication.Grpc

Typed gRPC clients for services on SharedKernel, over `Grpc.Net.ClientFactory`. One line registers a generated client;
its address, deadline, retry policy, keepalive and credentials live in configuration; every call carries the caller
and a deadline; and `ToResultAsync()` reads a failure back into the `Error` the other service returned.

```csharp
builder.Services.AddSharedKernelCommunication(builder.Configuration)
    .AddGrpcClient<Inventory.InventoryClient>("inventory-grpc");
```

```json
"SharedKernel": {
  "Communication": {
    "ServiceDiscovery": { "Mode": "Dns" },
    "Clients": {
      "inventory-grpc": { "Address": "http://_grpc.inventory", "Deadline": "00:00:05" }
    }
  }
}
```

```csharp
Result<StockReply> stock = await client
    .GetStockAsync(new GetStockRequest { Sku = sku }, cancellationToken: ct)
    .ToResultAsync(ct);
```

Service discovery, authentication, mutual TLS and the shared error codes are described in
[`SharedKernel.Communication`](../SharedKernel.Communication/README.md).

## Settings

`SharedKernel:Communication:Clients:{name}`, validated when the host starts (`GrpcClientOptions`):

| Setting | Default | |
| --- | --- | --- |
| `Address` | — (required) | `http://inventory` or `http://_grpc.inventory` (the service's endpoint named `grpc`); `http` or `https`. |
| `Deadline` | 30 s | Every call that sets no deadline of its own, retries included. The service sees it too. |
| `Retry:MaxAttempts` | 3 | Attempts, the first included (gRPC's limit is 5); `1` turns retries off. |
| `Retry:InitialBackoff` / `MaxBackoff` / `BackoffMultiplier` | 500 ms / 5 s / 1.5 | gRPC's randomized exponential backoff. |
| `Retry:RetryableStatusCodes` | `[ "Unavailable" ]` | The status that says the call did not run. |
| `KeepAlive:PingDelay` / `PingTimeout` | 60 s / 30 s | HTTP/2 pings find a dead connection before a call waits on it. |
| `MaxReceiveMessageSize` | 4 MB | The largest response message. |
| `Authentication`, `Tls` | none | See [`SharedKernel.Communication`](../SharedKernel.Communication/README.md). |

Retries are gRPC's own (gRFC A6): a call is retried only while the service has not answered it — no response headers
yet — so a retry never repeats work the service reported doing.

## Load balancing

gRPC keeps one HTTP/2 connection per endpoint open for a long time, so a Kubernetes ClusterIP service would pin every
call to the pod the connection reached. Point the client at a **headless** service and set
`ServiceDiscovery:Mode` to `Dns`: service discovery resolves every pod address and each call goes to the next one,
round-robin; `RefreshPeriod` follows pods that come and go. Connections are kept per endpoint, several once one is
full of streams (`EnableMultipleHttp2Connections`).

## Results, not exceptions

`GrpcResultExtensions`:

- `call.ToResultAsync(ct)` awaits a unary call and returns its response or its `Error`, disposing the call; the
  caller's own cancellation is thrown.
- `rpcException.ToError()` for a streaming call or a `catch`.

| Status | Error |
| --- | --- |
| A platform rich status (`14.Presentation`'s `AddSharedKernelGrpc()`) | The service's error: `ErrorInfo.reason` → `Code`, the status message → `Message` |
| `InvalidArgument` with a `BadRequest` detail | `Error.Validation` of the field violations, each keeping its field as `PropertyPath` |
| No `ErrorInfo` | `grpc.{status}` (`grpc.failed_precondition`) with the status's `ErrorType` |
| Never reached the service (refused, reset, TLS) | `communication.unreachable` |
| Its deadline passed | `communication.timeout` |
| No access token | `communication.access_token_unavailable` |

Status → `ErrorType` is the reverse of `14.Presentation`'s `GrpcStatusCodeMap`: InvalidArgument and OutOfRange
Validation, Unauthenticated Unauthorized, PermissionDenied Forbidden, NotFound, Aborted and AlreadyExists Conflict,
FailedPrecondition BusinessRule, Unavailable and ResourceExhausted Unavailable, DeadlineExceeded Timeout, anything
else Unexpected.

## What every call carries

The caller's correlation id, tenant, actor and client as metadata (`WellKnownHeaders`, from `IRequestContextAccessor`),
written once per call before the retries — metadata the call already has wins; the deadline; then, per attempt, the
credential and the endpoint service discovery picks. Trace context (`traceparent`) comes from the HTTP handler, so the
server's span is a child of the gRPC client span.

## google.type.Money

`MoneyProtoExtensions` converts `google.type.Money` both ways:

| Method | |
| --- | --- |
| `money.ToMoney()` → `ValidationResult<Money>` | `SharedKernel.Domain`'s `Money`, rounded to the currency's minor unit; an unknown currency or a malformed message is an error |
| `domainMoney.ToMoneyProto()` | The message |
| `money.ToDecimal()` / `amount.ToMoneyProto("EUR")` | Plain amounts. Exact to nine decimal places; more are rounded to even first, so `0.9999999999` is 1 unit, never `nanos` = 10⁹ |

For `google.protobuf.Timestamp` use Google.Protobuf's own `Timestamp.FromDateTimeOffset` and `ToDateTimeOffset()`.

## Testing

`SharedKernel.Communication.Testing`'s `GrpcCalls` returns what a generated client's method returns, for a mocked client:

```csharp
client.GetStockAsync(Arg.Any<GetStockRequest>(), Arg.Any<CallOptions>())
    .Returns(GrpcCalls.Failure<StockReply>(Error.NotFound("inventory.sku_not_found", "No such SKU.")));
```

`GrpcCalls.Failure(error)` carries the rich status a platform service sends, so `ToResultAsync()` reads it back.

## Logging

EventIds 11100–11199: 11100 the caller could not be written onto a call (Error); the call goes out without it.
