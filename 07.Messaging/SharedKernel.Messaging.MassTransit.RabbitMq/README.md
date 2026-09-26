# SharedKernel.Messaging.MassTransit.RabbitMq

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![MassTransit 8.5](https://img.shields.io/badge/MassTransit-8.5.x%20(Apache--2.0)-512BD4)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md#why-masstransit-85-and-not-9x)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-verified-FF6600?logo=rabbitmq&logoColor=white)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The RabbitMQ transport for
> [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md):
> one `UseRabbitMq(...)` call on the bus builder, with RabbitMQ's delayed-message exchange, dead-letter queue TTL
> and routing-key affinity behind the builder's transport-neutral options.**

The core package references no broker client. Reference this package only in the startup project of a service
whose bus runs on RabbitMQ; a service on Azure Service Bus never restores the RabbitMQ client.

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.MassTransit" />
<PackageReference Include="SharedKernel.Messaging.MassTransit.RabbitMq" />
```

Versions come from your single `SharedKernelVersion`. **Adapter** tier; references the MassTransit core (a declared
Adapter → Adapter edge) and `MassTransit.RabbitMQ` 8.5.x, the last Apache-2.0 MassTransit release.

## Registration

`UseRabbitMq` is an extension method on `MessagingBusBuilder`, declared in the builder's own namespace
(`SharedKernel.Messaging.MassTransit.Extensions`), so the chain needs no extra `using`. A bus has exactly one
transport: `Build()` throws without one, and a second `Use*` call throws.

```csharp
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)                       // SharedKernel:Messaging:ServiceName
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)  // "amqps://user:pass@host/vhost"
    .WithRetry()
    .WithDelayedDelivery()                                                 // delayed-message exchange
    .WithDeadLetterPolicy(o => o.MessageTimeToLive = TimeSpan.FromDays(7))
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();              // the bus's "messaging" probe
```

Or with explicit options — for example bound from `SharedKernel:Messaging:RabbitMq`
(`RabbitMqBusOptions.SectionName`), with the credentials supplied by a secret store, never committed:

```csharp
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(o =>
    {
        builder.Configuration.GetSection(RabbitMqBusOptions.SectionName).Bind(o);
        o.Prefetch = 32;
    })
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

## Options

`RabbitMqBusOptions` (namespace `SharedKernel.Messaging.MassTransit.Options`):

| Option | Default | Notes |
| --- | --- | --- |
| `Host` | `rabbitmq://localhost` | AMQP URI; `amqps://` for TLS |
| `Username` / `Password` | `guest` / `guest` | Always override outside local development |
| `VirtualHost` | `/` | |
| `Prefetch` | `16` | Unacknowledged messages delivered per channel |
| `RequestedHeartbeat` | 60 s | Detects stale connections |
| `ConcurrentMessageLimit` | `null` (MassTransit's default) | Bus-level default; `ConsumerDefinitionBase.ConcurrentMessageLimit` overrides it per consumer |

## What the builder options mean on RabbitMQ

| Builder call | On RabbitMQ |
| --- | --- |
| `WithDelayedDelivery()` | MassTransit's delayed-message scheduler over the `x-delayed-message` exchange. **Needs the `rabbitmq_delayed_message_exchange` plugin**, which the official `rabbitmq` image does not ship — use `masstransit/rabbitmq`. Without it the first scheduled message fails |
| `WithDeadLetterPolicy(o => o.MessageTimeToLive = …)` | `x-message-ttl` on MassTransit's `_error` and `_skipped` queues. `QueueNameSuffix` is accepted but has no effect: MassTransit exposes no hook to rename those queues |
| `PublishContext.WithPartitionKey(key)` | The routing key (routing-key affinity). Ordering holds per key and only with a single active consumer on the endpoint |

## Testing

`SharedKernel.Messaging.MassTransit.RabbitMq.Tests` covers the builder configuration in the unit lane and runs a
real broker through Testcontainers (`[Trait("Category","Integration")]`) for dead-lettering and ordered delivery.
[samples/ShippingApi](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/ShippingApi/README.md)
runs the packed packages end to end against `masstransit/rabbitmq`.

## Related packages

| Package | Why |
| --- | --- |
| [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md) | The bus this transport plugs into |
| [`SharedKernel.Messaging.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.Abstractions/README.md) | What application code injects |
| [`SharedKernel.Messaging.MassTransit.AzureServiceBus`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.AzureServiceBus/README.md) | The alternative transport |
| [`SharedKernel.Messaging.MassTransit.EfCore`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.EfCore/README.md) | The transactional outbox, combinable with this transport |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[domain overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md).
