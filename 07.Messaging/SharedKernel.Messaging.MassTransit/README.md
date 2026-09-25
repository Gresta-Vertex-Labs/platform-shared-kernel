# SharedKernel.Messaging.MassTransit

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![MassTransit 8.5](https://img.shields.io/badge/MassTransit-8.5.x%20(Apache--2.0)-512BD4)](#why-the-85x-pin)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-verified-FF6600?logo=rabbitmq&logoColor=white)
![Azure Service Bus](https://img.shields.io/badge/Azure%20Service%20Bus-supported-0078D4?logo=microsoftazure&logoColor=white)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **A configured message bus in one chain: RabbitMQ or Azure Service Bus, retry, transactional outbox,
> at-most-once consumption, the caller's tenant travelling with the message, and tracing and metrics on every
> verb.**

This package is the implementation behind
[`SharedKernel.Messaging.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.Abstractions/README.md).
**Only your startup project references it.** Application code references the abstractions, which is what keeps
MassTransit out of your handlers, your tests and your type signatures.

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [The builder](#the-builder)
- [Writing a consumer](#writing-a-consumer)
- [Idempotency](#idempotency)
- [The caller across the bus](#the-caller-across-the-bus)
- [Outbox](#transactional-outbox)
- [Failure handling](#failure-handling)
- [Deferred delivery](#deferred-delivery)
- [Observability](#observability)
- [The window before ready](#the-window-before-ready)
- [Why the 8.5.x pin](#why-the-85x-pin)

## Install

```bash
dotnet add package SharedKernel.Messaging.MassTransit
```

## Quick start

```json
{
  "ConnectionStrings": { "rabbitmq": "amqp://guest:guest@localhost:5672" },
  "SharedKernel": { "Messaging": { "ServiceName": "order-service" } }
}
```

```csharp
// Register the service's own IRequestContext FIRST — the container resolves the last
// registration, and WithInboundRequestContext() must be the one that wins.
builder.AddSharedKernelRequestContext();

builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)
    .WithRetry()
    .WithIdempotency()
    .WithInboundRequestContext()
    .WithAmbientCorrelationPropagation()
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

builder.Services.AddHealthChecks().AddMessagingReadinessCheck();
```

`ServiceName` prefixes every queue this service declares (`order-service-order-placed` for an
`OrderPlacedConsumer`) and is the CloudEvents `source` on every event it publishes. It is validated at startup as
a lowercase slug — stricter than any one broker requires, so that switching transports is never a rename of every
queue in the deployment.

## The builder

| Call | What it does |
| --- | --- |
| `UseRabbitMq(connectionString)` / `UseRabbitMq(configure)` | RabbitMQ, with TLS, prefetch, concurrency and cluster options on the overload |
| `UseAzureServiceBus(connectionString)` / `UseAzureServiceBus(configure)` | Azure Service Bus, including a fully-qualified namespace with `DefaultAzureCredential` |
| `AddConsumer<T>()` / `AddConsumer<T, TDefinition>()` | Registers a consumer; the definition overload sets per-consumer retry, concurrency and non-retryable exceptions |
| `AddBatchConsumer<T>(configure)` | A consumer that receives messages in batches |
| `AddFaultConsumer<TMessage, TConsumer>()` | Observes messages that exhausted their retries |
| `WithRetry(configure)` | Incremental retry, in-process, before a message is faulted |
| `WithCircuitBreaker(configure)` | Stops hammering a dependency that is already failing |
| `WithDeadLetterPolicy(configure)` | RabbitMQ error-queue TTL (a no-op on Azure Service Bus, which owns this at the resource level — a startup warning says so) |
| `WithIdempotency()` | At-most-once consumption per `MessageId` |
| `WithInboundRequestContext()` | The publisher's tenant and actor, both directions |
| `WithDelayedDelivery()` | Transport-native scheduled delivery |
| `WithEntityFrameworkOutbox<TDbContext>()` | Publish inside the same transaction as your data |
| `WithSendEndpointRoute<T>(queue)` | Routes a command type to a named queue |
| `WithHeaderPropagator<T>()` | Your own ambient-value propagator |
| `WithAmbientCorrelationPropagation()` | Correlation id from `Activity.Current`, no consumer code |
| `WithTenantContext<TAccessor>()` | Tenant from your own accessor, when you are not using `WithInboundRequestContext()` |
| `WithVersionTranslator<TOld, TNew, T>()` | Projects an old message shape to the current one before the consumer sees it |
| `WithPayloadTransform(configure)` | Compress-then-encrypt on publish, the reverse on consume |
| `Build()` | Validates, registers everything, returns the `IServiceCollection` |

`Build()` fails fast and says what to do: no transport configured, `WithIdempotency()` without a registered
store, or payload encryption without the matching `01.Core` service each throw at registration with the fix in
the message.

## Writing a consumer

```csharp
public sealed class OrderPlacedConsumer(IOrderRepository orders, ILogger<OrderPlacedConsumer> log)
    : ConsumerBase<EventEnvelope<OrderPlaced>>(log)
{
    protected override Task ConsumeAsync(EventEnvelope<OrderPlaced> envelope, CancellationToken ct) =>
        orders.MarkPlacedAsync(envelope.Data.OrderId, ct);
}
```

`ConsumerBase` gives you the consume `Activity`, the structured log scope (including every `x-sk-*` header),
the consume counter and duration histogram, and error logging — and hands you the deserialized message and a
token, nothing else. A consumer body that cannot reach the transport cannot accidentally depend on it, and the
same body is testable by calling it directly.

`IEventPublisher` publishes `EventEnvelope<TEvent>`, so a consumer of an integration event consumes the
envelope and reads `envelope.Data`. A message published through `IMessageBus` is consumed as itself.

> **Never swallow an exception in `ConsumeAsync`.** Letting it propagate is how a consumer asks for a retry.
> Catching and logging acknowledges the message: the work is lost and the queue looks healthy. If a failure is
> genuinely not worth retrying, declare its type in `ConsumerDefinitionBase.NonRetryableExceptions`.

Queue names come from the consumer type: `{service-name}-{consumer-type-in-kebab-case}`, with the `Consumer`
suffix dropped.

## Idempotency

```csharp
services.AddSharedKernelIdempotencyRedis(configuration);   // or .EfCore, from 18.Idempotency

builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(connectionString)
    .WithIdempotency(o => o.ExpiryWindow = TimeSpan.FromHours(48))
    .Build();
```

The filter reserves the `MessageId` atomically before the consumer runs, completes on success, and **releases on
failure** so a redelivery can retry immediately instead of waiting out the lease. A duplicate that is still in
flight throws `ConcurrentMessageDeliveryException`, which keeps the message unacknowledged — expected behaviour,
not something to alert on.

A message with no `MessageId` passes through: there is nothing to deduplicate on, and inventing an id would make
every delivery look unique anyway.

## The caller across the bus

```csharp
.WithInboundRequestContext()
```

Publishes the current caller's tenant and actor as transport headers, and rebuilds them on the consumer so that
injecting `IRequestContext` resolves to the caller that published. Outside a consume it still resolves to the
service's own caller, so **one handler serves both the HTTP path and the message path without branching**.

Register it **after** your own `IRequestContext` — the container resolves the last registration. Get it wrong and
consumers silently see no tenant, which looks like a persistence bug; a startup warning (EventId 7011) fires when
that happens.

## Transactional outbox

```csharp
.WithEntityFrameworkOutbox<OrderDbContext>(o => o.QueryDelay = TimeSpan.FromSeconds(1))
```

Messages published inside a `SaveChangesAsync` are written to outbox tables in the **same transaction** as your
data and delivered afterwards by a background worker: no lost event when the process dies between the commit and
the publish, and no phantom event when the transaction rolls back.

Your `DbContext` arrives as a generic parameter, so this package takes no dependency on `06.Persistence`. Add
MassTransit's outbox entities to your model and run the migration before the bus starts.

## Failure handling

```csharp
.WithRetry(r => { r.Attempts = 3; r.InitialInterval = TimeSpan.FromSeconds(1); })
.WithCircuitBreaker()
.WithDeadLetterPolicy(o => o.MessageTimeToLive = TimeSpan.FromDays(7))
.AddFaultConsumer<ChargeCard, ChargeCardFaultConsumer>()
```

Retries happen in-process. When they run out, MassTransit moves the message to the error queue and publishes a
fault, which the fault consumer observes — an observer, not a recovery mechanism: by then the message is already
in the error queue, and nothing a fault consumer does puts it back.

Dispatch failures are `Result` values, not exceptions:

```csharp
Result published = await bus.PublishAsync(message, ct);
if (published.IsFailure && published.Error.Code == MessagingErrorCodes.Unavailable)
{
    // the broker is down — degrade, queue locally, or surface it
}
```

## Deferred delivery

```csharp
.WithDelayedDelivery()
```

```csharp
Guid token = await scheduler.ScheduleAsync(new ChaseShipment(id), DateTimeOffset.UtcNow.AddHours(2), ct);
```

The **broker** holds the message, so it is delivered even if every replica of this service is redeployed in the
meantime.

> **RabbitMQ needs a plugin.** The delayed-message exchange is a community plugin
> (`rabbitmq_delayed_message_exchange`) the official `rabbitmq` image does not ship. Use `masstransit/rabbitmq`,
> which has it enabled. Without it the bus starts and the first scheduled message fails when the
> `x-delayed-message` exchange cannot be declared.

For recurring or cron work, use `19.Scheduling` — this defers one message once.

## Observability

An `ActivitySource` and a `Meter`, both named `SharedKernel.Messaging`:

| Instrument | Recorded |
| --- | --- |
| `messaging.publish.count` | After a publish succeeds |
| `messaging.send.count` | After a send succeeds |
| `messaging.consume.count` | After `ConsumeAsync` returns without throwing |
| `messaging.consume.duration` | Always, success or failure |
| `messaging.retry.count` | On a retry-filter redelivery |
| `messaging.fault.count` | On every delivered fault |

Spans: `MessageBus.Publish`, `MessageBus.Send`, `EventPublisher.Publish`, `Consumer.Consume`. Wire them with
`13.ServiceDefaults`' `WithMessagingTelemetry()`.

`IMessageBusProbe` reports the health of the **real configured bus** — never a second connection built from
copied configuration. `AddMessagingReadinessCheck()` (from `SharedKernel.ServiceDefaults.Messaging`) exposes it
to Kubernetes.

## The window before ready

MassTransit starts the bus in the background and returns, so the host reports "started" before the broker
connection exists and before any queue has been declared. **A message published in that window goes to an
exchange with nothing bound to it and is dropped by the broker — silently and successfully.**

Gate traffic on `/health/ready`, in tests as well as in production. Never substitute a sleep.

## Why the 8.5.x pin

MassTransit 9.x carries a bare `licenseUrl` with no SPDX expression and a "Massient, Inc." copyright; 8.5.10
carries `<license type="expression">Apache-2.0</license>`. This package declares MIT, so shipping on a 9.x pin
would put out a package claiming MIT while imposing a commercial obligation on every service consuming it.

8.5.10 has a native `net10.0` target, so the pin costs no framework fidelity. Do not bump it without a recorded
licensing decision.

## Not here

| You want | Use |
| --- | --- |
| Request/response with an answer | `11.Communication` (HTTP or gRPC) |
| Sagas, routing slips, multi-step orchestration | `17.Workflows` |
| Recurring or cron jobs | `19.Scheduling` |
| An `IIdempotencyStore` for `IdempotencyPurpose.Message` | `18.Idempotency` (`AddRedisIdempotency(p => p.ForMessages())`, `AddEfCoreIdempotency(...)`) |
| Cache invalidation signalling | `02.Caching`'s Redis Pub/Sub — no delivery guarantee needed, and none given |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
See the [domain overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md),
and [samples/ShippingApi](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/ShippingApi/README.md)
for a service built on this package and tested against a real broker.
