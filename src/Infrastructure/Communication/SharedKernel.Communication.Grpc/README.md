# SharedKernel.Communication.Grpc

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Generated gRPC clients registered in one line over `Grpc.Net.ClientFactory`: address, deadline, retry policy,
> keepalive and credentials from configuration, the caller on every call, and `ToResultAsync()` to read a failure
> back into the `Error` the other service returned.**

| You get | So that |
| --- | --- |
| `AddGrpcClient<Service.ServiceClient>("name")` | A generated client configured from `SharedKernel:Communication:Clients:{name}` — never inject a `GrpcChannel` |
| A default deadline on every call | No call waits forever; the service sees the deadline too |
| gRPC's own retry policy (gRFC A6) | A call is retried only while the service has not answered it |
| DNS discovery over a headless service | Calls spread across pods instead of pinning to one HTTP/2 connection |
| `call.ToResultAsync(ct)` / `RpcException.ToError()` | A rich status becomes the service's `Error`, field violations included |
| `MoneyProtoExtensions` | `google.type.Money` ↔ `Money` / `decimal`, exact to nine decimal places |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Communication.Grpc" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project |
| Depends on | [`SharedKernel.Communication`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Communication/SharedKernel.Communication/README.md), `SharedKernel.Domain` (for `Money`), `Grpc.Net.ClientFactory`, `Google.Api.CommonProtos`, `Grpc.StatusProto` |
| Namespaces | `SharedKernel.Communication` |

## Quick start

```csharp
using SharedKernel.Communication;

builder.Services.AddSharedKernelCommunication(builder.Configuration)
    .AddGrpcClient<Inventory.InventoryClient>("inventory-grpc");
```

```json
{
  "SharedKernel": {
    "Communication": {
      "ServiceDiscovery": { "Mode": "Dns" },
      "Clients": {
        "inventory-grpc": { "Address": "http://_grpc.inventory", "Deadline": "00:00:05" }
      }
    }
  }
}
```

```csharp
using SharedKernel.Communication;
using SharedKernel.Primitives.Results;

public sealed class StockReader(Inventory.InventoryClient client)
{
    public Task<Result<StockReply>> GetAsync(string sku, CancellationToken ct) =>
        client.GetStockAsync(new GetStockRequest { Sku = sku }, cancellationToken: ct).ToResultAsync(ct);
}
```

## How it works

- **What every call carries:** the caller's correlation id, tenant, actor and client as metadata (`WellKnownHeaders`,
  from `IRequestContextAccessor`), written once per call before the retries — metadata the call already has wins; the
  configured deadline unless the call sets its own; then, per attempt, the credential and the endpoint service
  discovery picks. Trace context (`traceparent`) comes from the HTTP handler, so the server's span is a child of the
  client span.
- **Retries are gRPC's own:** a call is retried only while the service has not answered — no response headers yet —
  so a retry never repeats work the service reported doing. Only `Unavailable` is retried by default.
- **Load balancing:** gRPC keeps one HTTP/2 connection per endpoint open for a long time, so a Kubernetes ClusterIP
  service would pin every call to one pod. Point the client at a **headless** service with
  `ServiceDiscovery:Mode` = `Dns`: each call goes to the next pod, round-robin. Several connections per endpoint are
  opened once one is full of streams (`EnableMultipleHttp2Connections`).
- **Results, not exceptions:**

  | Status | Error |
  | --- | --- |
  | A platform rich status (`14.Presentation`'s `AddSharedKernelGrpc()`) | The service's error: `ErrorInfo.reason` → `Code`, status message → `Message` |
  | `InvalidArgument` with a `BadRequest` detail | `Error.Validation` of the field violations |
  | No `ErrorInfo` | `grpc.{status}` (`grpc.failed_precondition`) with the status's `ErrorType` |
  | Never reached the service (refused, reset, TLS) | `communication.unreachable` |
  | Its deadline passed | `communication.timeout` |
  | No access token | `communication.access_token_unavailable` |
  | The caller's own cancellation | Thrown (`OperationCanceledException`) |

  Status → `ErrorType`: InvalidArgument and OutOfRange → Validation, Unauthenticated → Unauthorized, PermissionDenied
  → Forbidden, NotFound → NotFound, Aborted and AlreadyExists → Conflict, FailedPrecondition → BusinessRule,
  Unavailable and ResourceExhausted → Unavailable, DeadlineExceeded → Timeout, anything else → Unexpected — the reverse
  of `14.Presentation`'s map.

## Configuration

Section `SharedKernel:Communication:Clients:{name}` (`GrpcClientOptions`), validated when the host starts.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `…:Clients:{name}:Address` | `Uri` | — (required) | `http://inventory`, or `http://_grpc.inventory` for the endpoint named `grpc`; `http` or `https` |
| `…:Clients:{name}:Deadline` | `TimeSpan` | `00:00:30` | Deadline of a call that sets none, retries included (up to 1 h) |
| `…:Clients:{name}:Retry:MaxAttempts` | `int` | `3` | Attempts, the first included (1–5); `1` turns retries off |
| `…:Clients:{name}:Retry:InitialBackoff` | `TimeSpan` | `00:00:00.5` | Upper bound of the first retry's randomized delay |
| `…:Clients:{name}:Retry:MaxBackoff` | `TimeSpan` | `00:00:05` | Largest delay between attempts |
| `…:Clients:{name}:Retry:BackoffMultiplier` | `double` | `1.5` | Growth factor of each next delay |
| `…:Clients:{name}:Retry:RetryableStatusCodes` | `StatusCode[]` | `[ "Unavailable" ]` | Status codes retried |
| `…:Clients:{name}:KeepAlive:PingDelay` | `TimeSpan` | `00:01:00` | Idle time before a ping; `Timeout.InfiniteTimeSpan` turns pings off |
| `…:Clients:{name}:KeepAlive:PingTimeout` | `TimeSpan` | `00:00:30` | How long a ping may go unanswered |
| `…:Clients:{name}:MaxReceiveMessageSize` | `int?` | gRPC's 4 MB | Largest response message, in bytes |
| `…:Clients:{name}:Authentication:*`, `…:Tls:*` | | none | See [`SharedKernel.Communication`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Communication/SharedKernel.Communication/README.md#configuration) |

`…` stands for `SharedKernel:Communication`.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddGrpcClient<TClient>(name, Action<IGrpcClientBuilder>?)` | The generated client through `Grpc.Net.ClientFactory` |
| `IGrpcClientBuilder.Configure(Action<GrpcClientOptions>)` | Options changed in code after binding |
| `IGrpcClientBuilder.UseAccessTokenProvider<TProvider>()` | An `IAccessTokenProvider` as the credential |
| `IGrpcClientBuilder.HttpClientBuilder` | The underlying `IHttpClientBuilder`, for a handler of your own |

### Extensions

| Method | Purpose |
| --- | --- |
| `AsyncUnaryCall<T>.ToResultAsync(ct)` | Awaits a unary call, returns its response or its `Error`, disposes the call |
| `RpcException.ToError()` | For a streaming call or a `catch` |
| `ProtoMoney.ToMoney()` → `ValidationResult<Money>` | `Money`, rounded to the currency's minor unit; unknown currency or malformed message is an error |
| `Money.ToMoneyProto()` | The `google.type.Money` message |
| `ProtoMoney.ToDecimal()` / `decimal.ToMoneyProto("EUR")` | Plain amounts; more than nine decimals are rounded to even first |

For `google.protobuf.Timestamp` use Google.Protobuf's own `Timestamp.FromDateTimeOffset` and `ToDateTimeOffset()`.

### Errors

The `communication.*` codes are listed in
[`SharedKernel.Communication`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Communication/SharedKernel.Communication/README.md#errors);
an uncoded failure is `grpc.{status}`.

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 11100 | Error | The caller could not be written onto the gRPC call `{GrpcMethod}`; it goes out without the caller's metadata |

## Testing

Reference [`SharedKernel.Communication.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Communication/SharedKernel.Communication.Testing/README.md).
`GrpcCalls` returns what a generated client's method returns, for a mocked client:

```csharp
using SharedKernel.Testing.Communication;

client.GetStockAsync(Arg.Any<GetStockRequest>(), Arg.Any<CallOptions>())
    .Returns(GrpcCalls.Failure<StockReply>(Error.NotFound("inventory.sku_not_found", "No such SKU.")));
```

`GrpcCalls.Failure<T>(error)` carries the rich status a platform service sends, so `ToResultAsync()` reads it back;
`GrpcCalls.Success(response)` returns a completed call.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Inject `GrpcChannel` or build one with `GrpcChannel.ForAddress` | Inject the generated client registered by `AddGrpcClient` | The channel would skip discovery, credentials, deadline and caller metadata |
| Call a ClusterIP service in `Configuration` mode | Use a headless service and `ServiceDiscovery:Mode` = `Dns` | Long-lived HTTP/2 connections pin every call to one pod |
| Add non-idempotent status codes like `Internal` to `RetryableStatusCodes` | Keep `Unavailable` (maybe `ResourceExhausted`) | Other statuses may mean the work ran |
| `await call.ResponseAsync` in a try/catch | `await call.ToResultAsync(ct)` | It maps the rich status and disposes the call |
| Convert `google.type.Money` by hand | `ToMoney()` / `ToMoneyProto()` | `units` and `nanos` share a sign and must round correctly |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Communication domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Communication/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
