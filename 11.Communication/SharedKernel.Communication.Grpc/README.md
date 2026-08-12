# SharedKernel.Communication.Grpc

gRPC channel factory for Platform.SharedKernel microservices, via `Grpc.Net.ClientFactory`:
`CorrelationTracingInterceptor` (W3C `traceparent`/`tracestate` + `x-correlation-id`),
`TenantIdInterceptor` (`x-tenant-id`), a real per-call `DeadlineSeconds` deadline,
`MoneyProtoExtensions` (`Money` ↔ `decimal`), `TimestampProtoExtensions` (`Timestamp` ↔
`DateTimeOffset`), and the fluent `IGrpcCommunicationBuilder` DI entry point. Channels are cached and
reused via `Grpc.Net.ClientFactory` — never construct `GrpcChannel.ForAddress()` directly.

## Install

```bash
dotnet add package SharedKernel.Communication.Grpc
```

```xml
<PackageReference Include="SharedKernel.Communication.Grpc" Version="1.0.0" />
```

## Usage

```csharp
services
    .AddSharedKernelGrpcCommunication()
    .AddGrpcClient<OrderGrpc.OrderGrpcClient>(address: "http://order-service:5001", options =>
    {
        options.DeadlineSeconds = 10;
        options.EnableRetry = true;
    });

// Inject OrderGrpc.OrderGrpcClient — pre-configured with CorrelationTracingInterceptor and
// TenantIdInterceptor registered globally, and every call carrying an enforced
// CallOptions.Deadline (unless the caller already supplied one, which always wins).
```

`AddGrpcClient<TClient>` throws `OptionsValidationException` synchronously — at the call site, not
deferred to the first RPC — when `GrpcClientOptions.DeadlineSeconds` is zero or negative.

`Address` may be omitted when an `IServiceEndpointResolver` is registered (see
[`SharedKernel.Communication.Internal`](https://www.nuget.org/packages/SharedKernel.Communication.Internal)) —
resolution then happens once, at channel-creation time (the channel itself is cached as a singleton).

## Recipe: Protobuf well-known type conversion

`MoneyProtoExtensions`/`TimestampProtoExtensions` are pure, static, allocation-minimal conversions
between Protobuf well-known types and their .NET equivalents — never hand-roll this conversion at a
call site:

```csharp
using SharedKernel.Communication.Grpc.Protobuf;

// decimal -> Google.Type.Money -> decimal, no precision loss
Money money = 42.50m.ToMoneyProto(currencyCode: "USD");
decimal amount = money.ToDecimal();

// DateTimeOffset -> Google.Protobuf.WellKnownTypes.Timestamp -> DateTimeOffset, UTC preserved
Timestamp ts = DateTimeOffset.UtcNow.ToTimestampProto();
DateTimeOffset when = ts.ToDateTimeOffset();
```

## Recipe: caller-supplied deadline always wins

The per-call deadline `AddGrpcClient<TClient>` applies is skipped entirely when the caller already
supplied one through the generated client's own `CallOptions` overload — the same "caller-supplied
value always wins" convention this domain applies to `x-correlation-id`/`x-tenant-id`:

```csharp
// The platform default DeadlineSeconds (10, from registration above) applies.
var order = await client.GetOrderAsync(request, cancellationToken: ct);

// This call's own explicit deadline wins instead — never overwritten.
var urgentOrder = await client.GetOrderAsync(
    request,
    new CallOptions(deadline: DateTime.UtcNow.AddSeconds(2)));
```

## Layering

```text
SharedKernel.Communication.Grpc  →  SharedKernel.Primitives (01.Core),
                                     SharedKernel.Security.Abstractions (12.Security),
                                     Grpc.Net.Client, Grpc.Net.ClientFactory,
                                     OpenTelemetry.Instrumentation.GrpcNetClient
```

Target framework: `net10.0`. Deliberately does **not** reference `04.Contracts` — gRPC uses
Protobuf-generated types directly; `Envelope<T>`, `PagedList<T>`, and `EventEnvelope<T>` have no
place in gRPC package code. Never references `02.Caching`, `05.Application`, `06.Persistence`, or
`07.Messaging`.

For full documentation see
[`11.Communication/CLAUDE.md`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/11.Communication/CLAUDE.md).
