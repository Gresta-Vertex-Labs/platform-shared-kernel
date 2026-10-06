# SharedKernel.Messaging.MassTransit.AzureServiceBus

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The Azure Service Bus transport for
> [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit/README.md):
> one `UseAzureServiceBus(...)` call, managed identity by default, native scheduled enqueue and session-based
> ordering behind the builder's transport-neutral options.**

| You get | So that |
| --- | --- |
| `UseAzureServiceBus(o => o.FullyQualifiedNamespace = …)` with `DefaultAzureCredential` | Production runs on managed identity, with no secret to rotate |
| `UseAzureServiceBus(connectionString)` | Local development and CI work with one line |
| `WithDelayedDelivery()` over `ScheduledEnqueueTimeUtc` | `IMessageScheduler` is broker-held, no plugin required |
| `PublishContext.WithPartitionKey(key)` as the session id | Related messages keep their order on session-enabled entities |
| A startup warning when `WithDeadLetterPolicy()` cannot apply | A no-op configuration is visible instead of silently ignored |
| A separate package | A service on RabbitMQ never restores the Azure SDK |

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.MassTransit" />
<PackageReference Include="SharedKernel.Messaging.MassTransit.AzureServiceBus" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Api/Worker** (startup) project, next to the MassTransit core |
| Depends on | `SharedKernel.Messaging.MassTransit` (declared adapter edge), `MassTransit.Azure.ServiceBus.Core` 8.5.x, `Azure.Identity` |
| Namespaces | `SharedKernel.Messaging.MassTransit.Extensions` (`UseAzureServiceBus`), `SharedKernel.Messaging.MassTransit.Options` (`AzureServiceBusOptions`) |

## Quick start

```csharp
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.Services
    .AddSharedKernelMessaging(builder.Configuration)          // SharedKernel:Messaging:ServiceName
    .UseAzureServiceBus(o =>
    {
        o.FullyQualifiedNamespace = "orders-prod.servicebus.windows.net";   // DefaultAzureCredential
        o.MaxConcurrentCalls = 8;
    })
    .WithRetry()
    .WithDelayedDelivery()                                     // native scheduled enqueue
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // the bus's "messaging" probe
```

For local development and CI, pass a connection string (never commit one):

```csharp
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseAzureServiceBus(builder.Configuration.GetConnectionString("servicebus")!)
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

## How it works

| Builder call | On Azure Service Bus |
| --- | --- |
| `WithDelayedDelivery()` | MassTransit's Service Bus scheduler (`ScheduledEnqueueTimeUtc`); the broker holds the message |
| `WithDeadLetterPolicy(...)` | **Does nothing.** Dead-lettering is native (`MaxDeliveryCount` on the entity, set in infrastructure); a startup warning (7010) says so |
| `PublishContext.WithPartitionKey(key)` | The session id (and the routing key). Ordering needs sessions enabled on the receiving entity — an infrastructure responsibility |
| `ConsumerDefinitionBase.ConcurrentMessageLimit` | Overrides `MaxConcurrentCalls` on that consumer's endpoint |

- Set **exactly one** of `FullyQualifiedNamespace` and `ConnectionString`: the options overload throws
  `InvalidOperationException` at registration when both or neither are set.
- `MaxConcurrentCalls` is applied as the bus-wide `ConcurrentMessageLimit` on both overloads — its default `1`
  also applies to the connection-string overload.
- A bus has exactly one transport: a second `Use*` call throws.

## Configuration

`AzureServiceBusOptions` declares `SectionName = "SharedKernel:Messaging:AzureServiceBus"`. The package does not bind
it; bind it inside the `configure` action if you keep it in configuration
(`builder.Configuration.GetSection(AzureServiceBusOptions.SectionName).Bind(o)`).

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Messaging:AzureServiceBus:FullyQualifiedNamespace` | `string?` | `null` | `my-namespace.servicebus.windows.net`, authenticated with `DefaultAzureCredential` |
| `SharedKernel:Messaging:AzureServiceBus:ConnectionString` | `string?` | `null` | Local development and CI only |
| `SharedKernel:Messaging:AzureServiceBus:MaxConcurrentCalls` | `int` | `1` | Bus-wide concurrent messages per endpoint |
| `SharedKernel:Messaging:AzureServiceBus:TransportType` | `ServiceBusTransportType` | `AmqpTcp` | `AmqpWebSockets` when port 5671 is blocked |

## Reference

| Method | Does |
| --- | --- |
| `MessagingBusBuilder.UseAzureServiceBus(string connectionString)` | Sets the transport from a connection string |
| `MessagingBusBuilder.UseAzureServiceBus(Action<AzureServiceBusOptions>)` | Sets the transport from options, validated at registration |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 7010 | Warning | `WithDeadLetterPolicy()` was called under Azure Service Bus; it has no effect |

Error codes, the other log events and the `messaging` readiness probe belong to the core — see the
[MassTransit package README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit/README.md#reference).

## Testing

Application code is tested against
[`SharedKernel.Messaging.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Messaging/SharedKernel.Messaging.Testing/README.md)'s
in-memory fakes. Azure Service Bus has no container image, so this package's own tests assert the configuration it
applies (option guards, concurrency, session ids, the advisory); end-to-end tests need a real namespace.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Use a connection string in production | `FullyQualifiedNamespace` with managed identity | No secret to leak or rotate |
| Set both `ConnectionString` and `FullyQualifiedNamespace` | Set exactly one | Registration throws |
| Expect `WithDeadLetterPolicy()` to set a TTL | Configure `MaxDeliveryCount` and TTL on the entity | Service Bus dead-lettering is native |
| Rely on partition keys without sessions | Enable sessions on the receiving queue or subscription | Without sessions the session id is ignored |
| Forget that `MaxConcurrentCalls` defaults to `1` | Set it, or override per consumer | Every endpoint otherwise processes one message at a time |

## Design decisions

**Why a satellite?** The core references no Azure SDK, so a service restores only the transport it runs on.
**Why MassTransit 8.5.x?** It is the last Apache-2.0 release; 9.x is commercially licensed.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Messaging domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Messaging/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
