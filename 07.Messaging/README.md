<div align="center">

# SharedKernel Messaging

**Events and commands between .NET services — RabbitMQ or Azure Service Bus behind one contract, with the caller's
tenant travelling with the message, at-most-once consumption, and failures you handle instead of catch.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 5](https://img.shields.io/badge/packages-5-informational)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-verified-FF6600?logo=rabbitmq&logoColor=white)](SharedKernel.Messaging.MassTransit.RabbitMq/README.md)
[![Azure Service Bus](https://img.shields.io/badge/Azure%20Service%20Bus-supported-0078D4?logo=microsoftazure&logoColor=white)](SharedKernel.Messaging.MassTransit.AzureServiceBus/README.md)
![MassTransit 8.5.x (Apache-2.0)](https://img.shields.io/badge/MassTransit-8.5.x%20(Apache--2.0)-512BD4)

[Packages](#packages) · [How they fit](#how-the-packages-fit-together) · [Get started](#get-started) · [Sample](#see-it-run) · [Guarantees](#guarantees)

</div>

---

## What this domain gives you

- **One contract for publishing and sending.** `IEventPublisher` and `IMessageBus` return `Result` — a broker outage
  is `messaging.unavailable` (503), not an exception nobody catches.
- **CloudEvents on the wire.** Every integration event travels in `EventEnvelope<TEvent>`, with its wire name and
  version from `[IntegrationEvent]`.
- **The caller travels with the message.** A consumer's `IRequestContext` answers for the tenant and actor that
  published, so tenant-scoped persistence works inside a consume exactly as behind an HTTP request.
- **At-most-once consumption** of each message id per consumer, over an atomic idempotency store.
- **Declared failure handling** — retry, circuit breaker, dead-letter TTL, fault consumers — plus broker-held
  deferral, native ordered delivery, payload compression and encryption, tracing, metrics and a readiness probe.
- **A transactional outbox** over your own `DbContext`, without the persistence packages knowing about messaging.

## Packages

| Package | Tier | When you need it |
| --- | --- | --- |
| [SharedKernel.Messaging.Abstractions](SharedKernel.Messaging.Abstractions/README.md) | Abstractions | Your Application project publishes, sends, schedules or reads the inbound caller |
| [SharedKernel.Messaging.MassTransit](SharedKernel.Messaging.MassTransit/README.md) | Adapter | Your startup project composes the bus and its consumers |
| [SharedKernel.Messaging.MassTransit.RabbitMq](SharedKernel.Messaging.MassTransit.RabbitMq/README.md) | Adapter | The bus runs on RabbitMQ |
| [SharedKernel.Messaging.MassTransit.AzureServiceBus](SharedKernel.Messaging.MassTransit.AzureServiceBus/README.md) | Adapter | The bus runs on Azure Service Bus (managed identity) |
| [SharedKernel.Messaging.MassTransit.EfCore](SharedKernel.Messaging.MassTransit.EfCore/README.md) | Adapter | You publish inside a database transaction (outbox) |

Application code references only the Abstractions. The startup project references the MassTransit core plus exactly
the transport and outbox it uses, so a RabbitMQ service never restores the Azure SDK and a service without an
outbox never restores EF Core.

## How the packages fit together

```mermaid
flowchart TB
    App["Application code<br/>IEventPublisher · IMessageBus · IMessageScheduler · IRequestContext"]
    Abs["SharedKernel.Messaging.Abstractions"]
    Core["SharedKernel.Messaging.MassTransit<br/>builder · consumers · retry · idempotency · caller propagation · probe"]
    Rmq["….MassTransit.RabbitMq<br/>UseRabbitMq"]
    Asb["….MassTransit.AzureServiceBus<br/>UseAzureServiceBus"]
    Ef["….MassTransit.EfCore<br/>WithEntityFrameworkOutbox"]
    Idem["18.Idempotency<br/>IIdempotencyStore (Message)"]
    Exec["01.Core Execution<br/>IRequestContext · RequestContextScope"]
    App --> Abs
    Core --> Abs
    Core --> Idem
    Core --> Exec
    Rmq --> Core
    Asb --> Core
    Ef --> Core
    Rmq --> R[(RabbitMQ)]
    Asb --> S[(Azure Service Bus)]
    Ef --> D[(Your DbContext)]
```

## Get started

**1 — Configure.** One setting, plus a connection string.

```json
{
  "ConnectionStrings": { "rabbitmq": "amqp://guest:guest@localhost:5672" },
  "SharedKernel": { "Messaging": { "ServiceName": "order-service" } }
}
```

`ServiceName` prefixes every queue this service declares and is the CloudEvents `source` of every event it
publishes. It is validated at startup as a lowercase slug.

**2 — Register the bus** in `Program.cs`.

```csharp
builder.Services.AddSharedKernelRequestContext();              // the service's own IRequestContext, first
builder.Services.AddRedisIdempotency(p => p.ForMessages());    // the store WithIdempotency() uses

builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)
    .WithRetry()
    .WithIdempotency()               // a redelivered message runs each consumer once
    .WithInboundRequestContext()     // the publisher's tenant and actor reach the consumer
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // the "messaging" readiness probe
builder.WithMessagingTelemetry();
```

**3 — Declare the event** — a fact, in primitives, with a wire name that outlives the class name.

```csharp
[IntegrationEvent("orders.order-placed", Version = 1)]
public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId, decimal Total)
    : IIntegrationEvent;
```

**4 — Publish it.** Tenant, actor and correlation id arrive on their own.

```csharp
public sealed class PlaceOrderHandler(IEventPublisher events, IClock clock)
{
    public async Task<Result> HandleAsync(Order order, CancellationToken ct)
    {
        Result published = await events.PublishAsync(
            new OrderPlaced(Guid.CreateVersion7(), clock.UtcNow, order.Id, order.Total), ct);
        return published;   // messaging.unavailable → 503 at the HTTP boundary
    }
}
```

**5 — Consume it.** The consumer receives the envelope and the caller that published.

```csharp
public sealed class OrderPlacedConsumer(IOrderRepository orders, ILogger<OrderPlacedConsumer> logger)
    : ConsumerBase<EventEnvelope<OrderPlaced>>(logger)
{
    protected override Task ConsumeAsync(EventEnvelope<OrderPlaced> envelope, CancellationToken ct) =>
        orders.MarkPlacedAsync(envelope.Data.OrderId, ct);   // tenant-scoped, like an HTTP request
}
```

**6 — Gate traffic on readiness.** MassTransit starts the bus in the background; a message published before the
broker connection and bindings exist is dropped by the broker, successfully. `/health/ready` stays unhealthy until
the bus is connected — wait for it in production and in tests.

## The caller across the bus

| Direction | What happens |
| --- | --- |
| **Publish** | The current caller's correlation id, tenant and actor are written as transport headers through `SharedKernel.Execution`'s one mapping, shared with REST, gRPC and Temporal |
| **Consume** | They are read back into a `PropagatedRequestContext`; the consumer runs inside a `RequestContextScope`, so `IRequestContext` and every outbound call carry the publisher's tenant and correlation id |

One handler serves the HTTP path and the message path without branching. It is **attribution, not
authorization**: a permission check inside a consume always answers `false`, because anyone with broker access can
write headers.

## Failures are values

| Code | Type (HTTP) | When |
| --- | --- | --- |
| `messaging.unavailable` | Unavailable (503) | The broker is unreachable or timed out |
| `messaging.endpoint_not_found` | NotFound (404) | A send addressed an endpoint that does not exist |
| `messaging.serialization_failed` | Unexpected (500) | The payload could not be serialized |
| `messaging.publish_rejected` | Unexpected (500) | The broker refused the message |
| `messaging.invalid_message` | Validation (400) | The event failed envelope validation before dispatch |
| `messaging.contract_violation` | Validation (400) | The event type has no valid `[IntegrationEvent]` attribute |

Anything not recognised as a transport fault is rethrown unchanged, so a bug is never laundered into a failed
`Result`.

## See it run

[samples/ShippingApi](../samples/ShippingApi/README.md) is a service on the packed packages — publish, send,
delayed delivery, idempotency, inbound caller identity, retry, a fault consumer and the readiness probe — with
end-to-end scenarios against a real RabbitMQ broker (`masstransit/rabbitmq`, which ships the delayed-message
plugin).

```bash
dotnet pack Platform.SharedKernel.slnx -c Release -o ./nupkgs -p:MinVerVersionOverride=1.0.0-local.1
dotnet test samples/ShippingApi/ShippingApi.Tests -p:SharedKernelPackageVersion=1.0.0-local.1
```

## Guarantees

- **No silent message loss.** A failed consume is never acknowledged; an in-flight duplicate stays unacknowledged
  rather than being dropped.
- **No accidental double-processing.** The idempotency reservation is a single conditional write in the store, keyed
  per message id and consumer.
- **No exceptions for expected failures.** Every verb returns `Result`; only cancellation throws by design.
- **No tenant leakage.** The tenant is carried explicitly and is absent — never inherited — when the publisher had
  none; a malformed tenant header yields no tenant.
- **No queue-name collisions.** Every queue is prefixed with the validated service name.
- **No transport in application code.** MassTransit types appear only in the startup project and consumer classes.
- **A permissive licence.** MassTransit is pinned to 8.5.x, the last Apache-2.0 release; 9.x is commercially
  licensed and is not adopted without a recorded licensing decision.

**Out of scope:** request/response over the bus (use `11.Communication`), sagas and multi-step orchestration
(`17.Workflows`), recurring jobs (`19.Scheduling`), loss-tolerant Pub/Sub (`02.Caching`'s Redis Pub/Sub), and
Kafka or Amazon SQS (no adapter today).

## Related domains

| Topic | Read |
| --- | --- |
| The idempotency store contract and its Redis and PostgreSQL stores | [18.Idempotency](../18.Idempotency/SharedKernel.Idempotency.Abstractions/README.md) |
| The CloudEvents envelope and integration event contracts | [04.Contracts](../04.Contracts/README.md) |
| Readiness endpoints and telemetry (`AddSharedKernelReadiness`, `WithMessagingTelemetry`) | [13.ServiceDefaults](../13.ServiceDefaults/SharedKernel.ServiceDefaults/README.md) |
| In-memory fakes for tests | [SharedKernel.Messaging.Testing](../16.Testing/SharedKernel.Messaging.Testing/README.md) |
| How to contribute | [CONTRIBUTING.md](../CONTRIBUTING.md) |

---

**For maintainers:** the domain rules live in [CLAUDE.md](CLAUDE.md) and the phase history in
[state-map.md](state-map.md).
