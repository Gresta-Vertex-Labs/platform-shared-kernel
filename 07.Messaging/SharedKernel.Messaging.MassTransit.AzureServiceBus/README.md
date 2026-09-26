# SharedKernel.Messaging.MassTransit.AzureServiceBus

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![MassTransit 8.5](https://img.shields.io/badge/MassTransit-8.5.x%20(Apache--2.0)-512BD4)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md#why-masstransit-85-and-not-9x)
![Azure Service Bus](https://img.shields.io/badge/Azure%20Service%20Bus-supported-0078D4?logo=microsoftazure&logoColor=white)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The Azure Service Bus transport for
> [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md):
> one `UseAzureServiceBus(...)` call on the bus builder, by connection string or by namespace with a managed
> identity, with native scheduled enqueue and session-based ordering behind the builder's transport-neutral
> options.**

The core package references neither the Azure SDK nor `Azure.Identity`. Reference this package only in the startup
project of a service whose bus runs on Azure Service Bus; a RabbitMQ service never restores them.

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.MassTransit" />
<PackageReference Include="SharedKernel.Messaging.MassTransit.AzureServiceBus" />
```

Versions come from your single `SharedKernelVersion`. **Adapter** tier; references the MassTransit core (a declared
Adapter → Adapter edge), `MassTransit.Azure.ServiceBus.Core` 8.5.x (the last Apache-2.0 MassTransit release) and
`Azure.Identity`.

## Registration

`UseAzureServiceBus` is an extension method on `MessagingBusBuilder`, declared in the builder's own namespace
(`SharedKernel.Messaging.MassTransit.Extensions`), so the chain needs no extra `using`. A bus has exactly one
transport: `Build()` throws without one, and a second `Use*` call throws.

```csharp
// Production: managed identity (DefaultAzureCredential — workload identity on Kubernetes).
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)                       // SharedKernel:Messaging:ServiceName
    .UseAzureServiceBus(o => o.FullyQualifiedNamespace = "my-namespace.servicebus.windows.net")
    .WithRetry()
    .WithDelayedDelivery()                                                 // native scheduled enqueue
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();              // the bus's "messaging" probe
```

```csharp
// Local development and CI: a connection string. Never commit one.
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseAzureServiceBus(builder.Configuration.GetConnectionString("servicebus")!)
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

The options can also be bound from `SharedKernel:Messaging:AzureServiceBus` (`AzureServiceBusOptions.SectionName`)
inside the `configure` action.

## Options

`AzureServiceBusOptions` (namespace `SharedKernel.Messaging.MassTransit.Options`):

| Option | Default | Notes |
| --- | --- | --- |
| `FullyQualifiedNamespace` | `null` | `my-namespace.servicebus.windows.net`, authenticated with `DefaultAzureCredential` |
| `ConnectionString` | `null` | Local development and CI only |
| `MaxConcurrentCalls` | `1` | Applied as the bus-level `ConcurrentMessageLimit`; `ConsumerDefinitionBase.ConcurrentMessageLimit` overrides it per consumer |
| `TransportType` | `AmqpTcp` | `AmqpWebSockets` when port 5671 is blocked |

Set **exactly one** of `FullyQualifiedNamespace` and `ConnectionString`; both or neither throws
`InvalidOperationException` at registration.

## What the builder options mean on Azure Service Bus

| Builder call | On Azure Service Bus |
| --- | --- |
| `WithDelayedDelivery()` | MassTransit's Service Bus scheduler (`ScheduledEnqueueTimeUtc`); no plugin, the broker holds the message |
| `WithDeadLetterPolicy(...)` | **Does nothing.** Dead-lettering is native (`MaxDeliveryCount` on the entity, set in infrastructure); a startup warning (EventId 7010) says so |
| `PublishContext.WithPartitionKey(key)` | The session id (and the routing key). Enable sessions on the receiving entity for ordered delivery — an infrastructure responsibility |

## Testing

`SharedKernel.Messaging.MassTransit.AzureServiceBus.Tests` asserts what the transport configures (option guards,
concurrency, session ids, the dead-letter advisory). There are no live-broker tests: Azure Service Bus has no
container image.

## Related packages

| Package | Why |
| --- | --- |
| [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md) | The bus this transport plugs into |
| [`SharedKernel.Messaging.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.Abstractions/README.md) | What application code injects |
| [`SharedKernel.Messaging.MassTransit.RabbitMq`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.RabbitMq/README.md) | The alternative transport |
| [`SharedKernel.Messaging.MassTransit.EfCore`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.EfCore/README.md) | The transactional outbox, combinable with this transport |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[domain overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md).
