# SharedKernel.Communication.Grpc

gRPC client factory for Platform.SharedKernel microservices, via `Grpc.Net.ClientFactory`:
`CorrelationTracingInterceptor` (W3C `traceparent`/`tracestate` + `X-Correlation-Id`),
`TenantIdInterceptor` (`X-Tenant-Id` plus the caller's actor and client), a real per-call
`DeadlineSeconds` deadline, `MoneyProtoExtensions` (`Money` ↔ `decimal`), `TimestampProtoExtensions`
(`Timestamp` ↔ `DateTimeOffset`), and the fluent `IGrpcCommunicationBuilder` DI entry point. Channels are
cached and reused via `Grpc.Net.ClientFactory` — never construct `GrpcChannel.ForAddress()` directly.

**Tier:** Adapter. No ASP.NET Core dependency: both interceptors read the caller from
`SharedKernel.Execution`'s `IRequestContextAccessor`. Server-side gRPC conventions (exception mapping,
inbound metadata, authorization attributes) are `SharedKernel.Presentation.Grpc` in `14.Presentation`.

## Install

```xml
<PackageReference Include="SharedKernel.Communication.Grpc" />
```

The version comes from the consumer's single `SharedKernelVersion`; every SharedKernel package is released
together.

## Usage

```csharp
services
    .AddSharedKernelGrpcCommunication()
    .AddGrpcClient<OrderGrpc.OrderGrpcClient>(address: "http://order-service:5001", configure: options =>
    {
        options.DeadlineSeconds = 10;
        options.EnableRetry = true;
    });

// Inject OrderGrpc.OrderGrpcClient — both interceptors are attached to its channel, and every call
// carries an enforced CallOptions.Deadline (unless the caller already supplied one, which always wins).
```

`AddGrpcClient<TClient>` throws `OptionsValidationException` synchronously — at the call site, not
deferred to the first RPC — when `GrpcClientOptions.DeadlineSeconds` is zero or negative.

`Address` may be omitted when an `IServiceEndpointResolver` is registered (see
[`SharedKernel.Communication.Internal`](../SharedKernel.Communication.Internal/README.md)) — resolution then
happens once, at channel-creation time (the channel itself is cached as a singleton).

## Caller propagation

The interceptors write the ambient caller (`IRequestContextAccessor.Current`) as call metadata, through the
same `RequestContextPropagation` mapping REST, MassTransit and Temporal use:

| Metadata | Interceptor | Written when |
| --- | --- | --- |
| `traceparent`, `tracestate` | `CorrelationTracingInterceptor` | An `Activity` is current |
| `x-correlation-id` | `CorrelationTracingInterceptor` | Always: the caller's id, or a new one for a new operation (never `Activity.Id`) |
| `x-tenant-id` | `TenantIdInterceptor` | The caller has a tenant |
| `x-sk-actor-id`, `x-sk-actor-kind`, `x-sk-client-id` | `TenantIdInterceptor` | As for REST: user id present / any caller / client id present |

gRPC lowercases metadata keys, so the `WellKnownHeaders` constants arrive as shown. A metadata entry the
caller supplied is never overwritten, and an interceptor failure is logged (EventId 11100/11101) and never
fails the call. Without an ambient caller only the trace and correlation entries are written.

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
supplied one through the generated client's own `CallOptions` overload:

```csharp
// The platform default DeadlineSeconds (10, from registration above) applies.
var order = await client.GetOrderAsync(request, cancellationToken: ct);

// This call's own explicit deadline wins instead — never overwritten.
var urgentOrder = await client.GetOrderAsync(
    request,
    new CallOptions(deadline: DateTime.UtcNow.AddSeconds(2)));
```

The deadline is computed from `IClock` at call time; `AddSharedKernelGrpcCommunication` registers
`SystemClock` only when no `IClock` is registered.

## Dependencies

```text
SharedKernel.Communication.Grpc  →  SharedKernel.Primitives, SharedKernel.Execution (Foundation),
                                     SharedKernel.Communication.Internal (declared adapter edge),
                                     Grpc.Net.Client, Grpc.Net.ClientFactory, Google.Protobuf,
                                     Google.Api.CommonProtos, OpenTelemetry.Instrumentation.GrpcNetClient
```

Adapter tier, `net10.0`. Deliberately does **not** reference `SharedKernel.Contracts` — gRPC uses
Protobuf-generated types directly (locked by `CommunicationLayeringRules.GrpcNeverReferencesContracts`).

Related packages: [`SharedKernel.Communication.Rest`](../SharedKernel.Communication.Rest/README.md),
[`SharedKernel.Communication.Internal`](../SharedKernel.Communication.Internal/README.md),
`SharedKernel.Communication.Testing` (`TestServerCallContext`, test projects only).

For full documentation see
[`11.Communication/CLAUDE.md`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/11.Communication/CLAUDE.md).
