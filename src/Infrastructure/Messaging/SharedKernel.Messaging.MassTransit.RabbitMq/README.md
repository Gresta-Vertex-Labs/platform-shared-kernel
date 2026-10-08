# SharedKernel.Messaging.MassTransit.RabbitMq

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-verified-FF6600?logo=rabbitmq&logoColor=white)

> **The RabbitMQ transport for
> [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit/README.md):
> one `UseRabbitMq(...)` call on the bus builder, with the delayed-message exchange, dead-letter queue TTL and
> routing-key ordering behind the builder's transport-neutral options.**

| You get | So that |
| --- | --- |
| `UseRabbitMq(connectionString)` / `UseRabbitMq(o => …)` | The bus runs on RabbitMQ with one call |
| `WithDelayedDelivery()` over the `x-delayed-message` exchange | `IMessageScheduler` is broker-held and survives a restart |
| `WithDeadLetterPolicy(o => o.MessageTimeToLive = …)` as `x-message-ttl` | Error and skipped queues do not grow forever |
| `PublishContext.WithPartitionKey(key)` as the routing key | Related messages keep their order |
| Prefetch, heartbeat and a bus-wide concurrency limit | Throughput is tuned in one place |
| A separate package | A service on Azure Service Bus never restores the RabbitMQ client |

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.MassTransit" />
<PackageReference Include="SharedKernel.Messaging.MassTransit.RabbitMq" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project, next to the MassTransit core |
| Depends on | `SharedKernel.Messaging.MassTransit` (declared adapter edge), `MassTransit.RabbitMQ` 8.5.x |
| Namespaces | `SharedKernel.Messaging.MassTransit.Extensions` (`UseRabbitMq`), `SharedKernel.Messaging.MassTransit.Options` (`RabbitMqBusOptions`) |

## Quick start

`UseRabbitMq` lives in the builder's own namespace, so the chain needs no extra `using`.

```csharp
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.Services
    .AddSharedKernelMessaging(builder.Configuration)                        // SharedKernel:Messaging:ServiceName
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)   // amqps://user:pass@host/vhost
    .WithRetry()
    .WithDelayedDelivery()
    .WithDeadLetterPolicy(o => o.MessageTimeToLive = TimeSpan.FromDays(7))
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();              // the bus's "messaging" probe
```

```json
{
  "ConnectionStrings": { "rabbitmq": "amqp://guest:guest@localhost:5672" },
  "SharedKernel": { "Messaging": { "ServiceName": "orders-api" } }
}
```

## How it works

| Builder call | On RabbitMQ |
| --- | --- |
| `WithDelayedDelivery()` | MassTransit's delayed-message scheduler over the `x-delayed-message` exchange. **Needs the `rabbitmq_delayed_message_exchange` plugin**, which the official `rabbitmq` image does not ship — use `masstransit/rabbitmq` or install it |
| `WithDeadLetterPolicy(o => o.MessageTimeToLive = …)` | `x-message-ttl` on MassTransit's `_error` and `_skipped` queues. `QueueNameSuffix` is accepted but has no effect |
| `PublishContext.WithPartitionKey(key)` | The routing key. Ordering holds per key and only with a single active consumer on the endpoint |
| `ConsumerDefinitionBase.ConcurrentMessageLimit` | Overrides `RabbitMqBusOptions.ConcurrentMessageLimit` on that consumer's endpoint |

- A bus has exactly one transport: `Build()` throws without one, and a second `Use*` call throws.
- The connection-string overload sets only the host URI; credentials and virtual host come from the URI and the
  other settings keep MassTransit's defaults. Use the options overload to set prefetch, heartbeat or concurrency.

## Recipes

### 1. Bind the options from configuration

`UseRabbitMq(o => …)` does not bind anything on its own. Bind `RabbitMqBusOptions.SectionName` yourself, with the
password supplied by a secret store:

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

## Configuration

`RabbitMqBusOptions` (`SharedKernel:Messaging:RabbitMq` when you bind it as above):

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Messaging:RabbitMq:Host` | `string` | `rabbitmq://localhost` | AMQP URI; `amqps://` for TLS |
| `SharedKernel:Messaging:RabbitMq:Username` | `string` | `guest` | Always override outside local development |
| `SharedKernel:Messaging:RabbitMq:Password` | `string` | `guest` | From a secret store, never committed |
| `SharedKernel:Messaging:RabbitMq:VirtualHost` | `string` | `/` | Virtual host |
| `SharedKernel:Messaging:RabbitMq:Prefetch` | `ushort` | `16` | Unacknowledged messages delivered per channel |
| `SharedKernel:Messaging:RabbitMq:RequestedHeartbeat` | `TimeSpan` | `00:01:00` | Detects stale connections |
| `SharedKernel:Messaging:RabbitMq:ConcurrentMessageLimit` | `int?` | `null` (MassTransit's default) | Bus-wide parallelism per endpoint |

## Reference

| Method | Does |
| --- | --- |
| `MessagingBusBuilder.UseRabbitMq(string connectionString)` | Sets the RabbitMQ transport from an AMQP URI |
| `MessagingBusBuilder.UseRabbitMq(Action<RabbitMqBusOptions>)` | Sets the transport from explicit options |

Error codes, log events (7000–7099) and the `messaging` readiness probe belong to the core — see the
[MassTransit package README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit/README.md#reference).

## Testing

Application code is tested against
[`SharedKernel.Messaging.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Messaging/SharedKernel.Messaging.Testing/README.md)'s
in-memory fakes; no broker needed. To test the wiring itself, run a `masstransit/rabbitmq` container
(Testcontainers) and gate the test on the `messaging` readiness probe before publishing.
The [Shop sample](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/Shop/README.md)
runs the packages end to end against real RabbitMQ.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Use the official `rabbitmq` image with `WithDelayedDelivery()` | `masstransit/rabbitmq`, or install the delayed-exchange plugin | Without the plugin the first scheduled message fails |
| Commit `Username`/`Password` to `appsettings.json` | A secret store or environment variables | The defaults are `guest`/`guest` |
| Expect `QueueNameSuffix` to rename the error queue | Accept MassTransit's `_error`/`_skipped` names | MassTransit exposes no rename hook |
| Rely on partition-key ordering with several consumer replicas | One active consumer per ordered endpoint | RabbitMQ orders per queue, not across competing consumers |
| Publish right after startup | Gate traffic on `/health/ready` | The bus connects in the background; a message sent before bindings exist is dropped by the broker |
| Reference both transport satellites | Reference exactly one | A bus has one transport; a second `Use*` throws |

## Design decisions

**Why a satellite?** The core references no broker client, so a service restores only the transport it runs on.
**Why MassTransit 8.5.x?** It is the last Apache-2.0 release; 9.x is commercially licensed, and every package here
is MIT.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Messaging packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Messaging/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
