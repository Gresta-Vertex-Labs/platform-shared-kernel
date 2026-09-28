# SharedKernel.Messaging.MassTransit

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![MassTransit 8.5.x (Apache-2.0)](https://img.shields.io/badge/MassTransit-8.5.x%20(Apache--2.0)-512BD4)

> **A configured message bus in one fluent chain: retry, circuit breaker, broker-held deferral, at-most-once
> consumption, the publisher's tenant and actor travelling with the message, payload compression and encryption,
> tracing, metrics and a readiness probe — all behind
> [`SharedKernel.Messaging.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.Abstractions/README.md).**

| You get | So that |
| --- | --- |
| `AddSharedKernelMessaging(configuration)…Build()` | One chain from configuration to a running bus, validated at startup |
| `IMessageBus`, `IEventPublisher`, `IMessageScheduler` implementations returning `Result` | Handlers see broker outages as values; unknown exceptions still propagate |
| `ConsumerBase<T>` / `ConsumerDefinitionBase<T>` | Consumers get tracing, metrics, log scope and error logging, and stay testable by calling them directly |
| `WithIdempotency()` over `IIdempotencyStore` | A redelivered message runs each consumer once |
| `WithInboundRequestContext()` | `IRequestContext` inside a consumer answers for the caller that published |
| `WithRetry`, `WithCircuitBreaker`, `WithDeadLetterPolicy`, `AddFaultConsumer` | Failure handling is declared, not hand-written |
| The `messaging` readiness probe | Kubernetes stops routing traffic while the bus is disconnected |
| No broker client and no EF Core | Transports ([RabbitMQ](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.RabbitMq/README.md), [Azure Service Bus](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.AzureServiceBus/README.md)) and the [EF Core outbox](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.EfCore/README.md) are satellites you add only when used |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.MassTransit" />
<!-- exactly one transport -->
<PackageReference Include="SharedKernel.Messaging.MassTransit.RabbitMq" />
<!-- optional: SharedKernel.Messaging.MassTransit.EfCore for the transactional outbox -->
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Api/Worker** (startup) project; application code references only the Abstractions |
| Depends on | `SharedKernel.Messaging.Abstractions`, `SharedKernel.Idempotency.Abstractions`, `SharedKernel.Contracts`, `SharedKernel.Configuration`, `SharedKernel.Compression`, `SharedKernel.Cryptography`, `MassTransit` 8.5.x |
| Namespaces | `SharedKernel.Messaging.MassTransit.Extensions` (builder; satellites extend it here), `.Consumers`, `.Options`, `.MessageBus`, `.Transports`, `.Diagnostics` |

## Quick start

```json
{
  "ConnectionStrings": { "rabbitmq": "amqp://guest:guest@localhost:5672" },
  "SharedKernel": { "Messaging": { "ServiceName": "order-service" } }
}
```

```csharp
using SharedKernel.Idempotency.Redis.Extensions;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.ServiceDefaults.Telemetry;

// The service's own IRequestContext FIRST: WithInboundRequestContext() wraps whatever is registered before it.
builder.Services.AddSharedKernelRequestContext();

// The store WithIdempotency() needs, for IdempotencyPurpose.Message (18.Idempotency).
builder.Services.AddRedisIdempotency(p => p.ForMessages());

builder.Services
    .AddSharedKernelMessaging(builder.Configuration)                        // SharedKernel:Messaging
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)   // .RabbitMq satellite
    .WithRetry()
    .WithDelayedDelivery()
    .WithIdempotency()
    .WithInboundRequestContext()
    .AddConsumer<OrderPlacedConsumer>()
    .WithSendEndpointRoute<ChargeCard>("billing-api-charge-card")
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();              // maps the "messaging" probe
builder.WithMessagingTelemetry();
```

```csharp
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.MassTransit.Consumers;

public sealed class OrderPlacedConsumer(IOrderRepository orders, ILogger<OrderPlacedConsumer> logger)
    : ConsumerBase<EventEnvelope<OrderPlaced>>(logger)
{
    protected override Task ConsumeAsync(EventEnvelope<OrderPlaced> envelope, CancellationToken ct) =>
        orders.MarkPlacedAsync(envelope.Data.OrderId, ct);
}
```

`ServiceName` prefixes every queue this service declares (`order-service-order-placed` for
`OrderPlacedConsumer`) and is the CloudEvents `source` of every event it publishes.

## How it works

```mermaid
flowchart LR
    H[Handler] -->|IEventPublisher / IMessageBus| P[Propagators, then your PublishContext]
    P --> T[Payload transform]
    T --> B[(Broker)]
    B --> F1[Inbound request context filter]
    F1 --> F2[Idempotency filter]
    F2 --> R[Retry / circuit breaker]
    R --> C[ConsumerBase.ConsumeAsync]
    R -.->|retries exhausted| E[_error queue + Fault]
    E --> FC[IFaultConsumer]
```

- **Nothing is wired until `Build()`.** It fails fast with the fix in the message: no transport, a second
  transport, `WithIdempotency()` without a store for `IdempotencyPurpose.Message`, a lease not shorter than the
  expiry window, or payload compression/encryption without the matching `01.Core` service.
- **Dispatch.** Propagators run on every publish and send in registration order; the publish callback runs last and
  wins. `IEventPublisher` wraps the event with `EventEnvelope.Wrap` (source = `ServiceName`, type and data version
  from `[IntegrationEvent]`), so its consumers consume `EventEnvelope<TEvent>`; `IMessageBus` messages travel as
  themselves. `SendAsync` goes to `queue:{route}` from `WithSendEndpointRoute<T>`, otherwise
  `queue:{service-name}-{kebab-type}`.
- **Failures.** Timeouts and connection faults → `messaging.unavailable`; broker refusals → `messaging.publish_rejected`;
  serializer failures → `messaging.serialization_failed`; an unknown send endpoint → `messaging.endpoint_not_found`.
  Anything else is rethrown; only cancellation throws by design.
- **Consume order.** The inbound request-context filter runs first, then idempotency, then retry and the circuit
  breaker (retry inner, breaker outer), then the consumer. `ConsumerBase` logs (7001) and **rethrows**, which is how
  a consumer asks for a retry.
- **Idempotency.** Key `{MessageId:D}:{sha256-hex("{receive-endpoint path}|{consumer type}")}`, scoped by the ambient
  tenant. `Completed` acknowledges without consuming; `InProgress` throws `ConcurrentMessageDeliveryException`
  so the message stays unacknowledged (expected, not an alert); an exception releases the reservation and
  rethrows; no `MessageId` passes through.
- **Caller.** `WithInboundRequestContext()` writes correlation id, tenant and actor as `WellKnownHeaders` and, on
  consume, opens a `RequestContextScope` with a `PropagatedRequestContext`; outside a consume `IRequestContext`
  still resolves to the host's own. A malformed tenant header yields no tenant; a missing correlation id is replaced.
- **Ordering.** `PublishContext.WithPartitionKey` maps to the transport's native mechanism (RabbitMQ routing key,
  Azure Service Bus session id). It holds per key with a single active consumer.
- **The window before ready.** MassTransit starts the bus in the background. A message published before the broker
  connection and bindings exist is dropped by the broker — successfully. Gate traffic on `/health/ready`.

## Recipes

### 1. Declare per-consumer endpoint settings

```csharp
using MassTransit;
using SharedKernel.Messaging.MassTransit.Consumers;

public sealed class ChargeCardConsumerDefinition : ConsumerDefinitionBase<ChargeCardConsumer>
{
    protected override int? ConcurrentMessageLimit => 4;
    protected override IReadOnlyList<Type> NonRetryableExceptions => [typeof(CardDeclinedException)];

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ChargeCardConsumer> consumerConfigurator,
        IBusRegistrationContext context)
    {
    }
}

// .AddConsumer<ChargeCardConsumer, ChargeCardConsumerDefinition>()
```

Also overridable: `EndpointName`, `PrefetchCount`. Declaring `NonRetryableExceptions` installs an endpoint retry
policy of three immediate attempts that ignores those types — it replaces `WithRetry()` for that endpoint.

### 2. Handle failures

```csharp
.WithRetry(r => { r.Attempts = 3; r.InitialInterval = TimeSpan.FromSeconds(1); })
.WithCircuitBreaker()
.WithDeadLetterPolicy(o => o.MessageTimeToLive = TimeSpan.FromDays(7))   // RabbitMQ only
.AddFaultConsumer<ChargeCard, ChargeCardFaultConsumer>()
```

A fault consumer implements `IFaultConsumer<ChargeCard>` and is an observer: the message is already in the error
queue, and nothing it does puts it back.

### 3. Defer a message

```csharp
.WithDelayedDelivery()   // registers IMessageScheduler

Guid token = await scheduler.ScheduleAsync(new ChaseShipment(id), clock.UtcNow.AddHours(2), ct);
```

The broker holds the message. On RabbitMQ this needs the delayed-message exchange plugin. For recurring work use
`19.Scheduling`.

### 4. Accept an old message shape during a rolling deploy

```csharp
.WithVersionTranslator<OrderPlacedV1, OrderPlaced, OrderPlacedV1Translator>()
.AddConsumer<OrderPlacedConsumer>()
```

A `VersionTranslatingConsumer` consumes `OrderPlacedV1`, calls the pure `Translate`, and republishes `OrderPlaced`. A
startup warning (7009) fires when no registered consumer handles the new type.

### 5. Compress and encrypt payloads

```csharp
builder.Services.AddSharedKernelCompression(builder.Configuration);
builder.Services.AddSharedKernelCryptography(builder.Configuration).AddSynchronousSymmetricEncryption();

// ...
.WithPayloadTransform(o => { o.EnableCompression = true; o.EnableEncryption = true; })
```

Always compress-then-encrypt on publish and decrypt-then-decompress on consume. Encryption needs the **synchronous**
`ISynchronousSymmetricEncryptionService` (keys in memory) because MassTransit's serializer is synchronous; a
KMS-only key provider cannot be used. The associated data is the CLR message type name; any mismatch throws
`PayloadTransformMismatchException`. Every service on the queue must use the same settings.

### 6. Customise MassTransit beyond the builder

```csharp
.ConfigureMassTransit(cfg =>
{
    // any IBusRegistrationConfigurator call the builder does not expose
})
```

Never call `AddMassTransit` yourself; `ConfigureMassTransit` runs inside the one registration (the EF Core outbox
satellite is built on it). A new broker is a new `MessagingTransport` passed to `UseTransport`.

## Configuration

Only `MessagingOptions` is bound from configuration, validated at startup (`ValidateOnStart`):

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Messaging:ServiceName` | `string` | — (required) | Lowercase slug `^[a-z0-9]+(-[a-z0-9]+)*$`, ≤ 100 chars; queue prefix and CloudEvents `source` |

The builder options are set in code, through the `With…(o => …)` actions:

| Options (method) | Property | Default |
| --- | --- | --- |
| `RetryOptions` (`WithRetry`) | `Attempts` / `InitialInterval` / `IntervalIncrement` / `ImmediateAttempts` | `3` / 1 s / 1 s / `0` |
| `CircuitBreakerOptions` (`WithCircuitBreaker`) | `TripThreshold` / `ActiveThreshold` / `ResetInterval` / `TrackingPeriod` | `5` / `10` / 60 s / 60 s |
| `DeadLetterOptions` (`WithDeadLetterPolicy`) | `MessageTimeToLive` / `QueueNameSuffix` | `null` / `_error` (no effect) |
| `IdempotencyOptions` (`WithIdempotency`) | `LeaseDuration` / `ExpiryWindow` | 30 s / 24 h |
| `BatchOptions` (`AddBatchConsumer`) | `MessageLimit` / `TimeLimit` / `ConcurrencyLimit` | `10` / 1 s / `1` |
| `PayloadTransformOptions` (`WithPayloadTransform`) | `EnableCompression` / `EnableEncryption` | `false` / `false` |

`RetryOptions.MaxInterval` (30 s) is declared but not applied by the incremental policy.

## Reference

### Registration

| Method | Does |
| --- | --- |
| `AddSharedKernelMessaging(IConfiguration, Action<MessagingOptions>?)` | Binds `SharedKernel:Messaging`, returns the builder |
| `AddSharedKernelMessaging(Action<MessagingOptions>?)` | Same without binding (trim/AOT-friendly) |
| `UseTransport(MessagingTransport)` / `ConfigureMassTransit(Action<IBusRegistrationConfigurator>)` | Extension points the satellites use |
| `AddConsumer<T>()`, `AddConsumer<T, TDefinition>()`, `AddBatchConsumer<T>(…)`, `AddFaultConsumer<TMessage, TConsumer>()` | Consumers |
| `WithRetry`, `WithCircuitBreaker`, `WithDeadLetterPolicy` | Failure handling |
| `WithDelayedDelivery()` | Registers `IMessageScheduler` over the transport's native scheduler |
| `WithSendEndpointRoute<T>(queueName)` | Routes a command type to a named queue |
| `WithIdempotency()`, `WithIdempotency(Action<IdempotencyOptions>)` | Consumer deduplication |
| `WithInboundRequestContext()`, `WithAmbientCorrelationPropagation()`, `WithTenantContext()`, `WithHeaderPropagator<T>()` | Caller and header propagation |
| `WithVersionTranslator<TOld, TNew, TTranslator>()`, `WithPayloadTransform(…)` | Schema evolution, payload transform |
| `Build()` | Validates and registers: `IMessageBus`, `IEventPublisher` (scoped), the `messaging` probe, MassTransit |

Authoring bases (`SharedKernel.Messaging.MassTransit.Consumers`): `ConsumerBase<T>` (`ConsumeAsync`),
`BatchConsumerBase<T>` (`ConsumeAsync(IReadOnlyList<T>, …)`, only via `AddBatchConsumer`),
`ConsumerDefinitionBase<T>`, `ConcurrentMessageDeliveryException`.

### Errors

`messaging.unavailable` (Unavailable, 503), `messaging.endpoint_not_found` (NotFound), `messaging.serialization_failed`
and `messaging.publish_rejected` (Unexpected), `messaging.invalid_message` and `messaging.contract_violation`
(Validation) — constants in `MessagingErrorCodes` (Abstractions).

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 7001 | Error | Unhandled exception consuming a message (`ConsumerBase`) |
| 7002 | Information | Processing a batch (`BatchConsumerBase`) |
| 7003 | Error | Unhandled exception processing a batch |
| 7004 | Error | Handling a fault; invoking the fault consumer |
| 7005 | Error | Unhandled exception in a fault consumer |
| 7008 | Debug | Translating a message from an old to a new type |
| 7009 | Warning | A version translator has no consumer for its new type |
| 7010 | Warning | Dead-letter policy ignored under Azure Service Bus (`.AzureServiceBus`) |
| 7011 | Warning | `IRequestContext` was registered after `WithInboundRequestContext()` |
| 7012 | Debug | `IRequestContext` could not be resolved during the startup check |

### Telemetry

`ActivitySource` and `Meter` named `SharedKernel.Messaging` (`MessagingDiagnostics`), wired by
`WithMessagingTelemetry()`. Spans `MessageBus.Publish`, `MessageBus.Send`, `EventPublisher.Publish`,
`Consumer.Consume`; instruments `messaging.publish.count`, `messaging.send.count`, `messaging.consume.count`,
`messaging.consume.duration`, `messaging.retry.count`, `messaging.fault.count`; tags from `MessagingTagKeys`.

### Health

`Build()` registers the `messaging` readiness probe (`MessagingReadinessProbeNames.Bus`) over MassTransit's bus health
check for the configured bus; `AddSharedKernelReadiness()` exposes it on `/health/ready`.

## Testing

In a service's unit tests, replace the bus with
[`SharedKernel.Messaging.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Messaging.Testing/README.md):
`services.AddInMemoryMessageBus().AddInMemoryEventPublisher()` and assert with `ShouldHavePublished<T>()` /
`ShouldHaveSent<T>()`. A consumer body is tested by calling it directly or through MassTransit's
`AddMassTransitTestHarness()` (package `MassTransit.TestFramework`), waiting on `harness.InactivityTask`. For
idempotent consumers, `SharedKernel.Idempotency.Testing`'s `AddFakeIdempotencyStore(IdempotencyPurpose.Message)`
satisfies `Build()`.
[`samples/ShippingApi`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/ShippingApi/README.md)
runs the whole chain against real RabbitMQ.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Catch and log in `ConsumeAsync` | Let the exception propagate; list non-retryable types in the definition | Catching acknowledges the message and the work is lost |
| Register `IRequestContext` after `WithInboundRequestContext()` | Register it before the bus chain | The last registration wins; consumers would see no tenant (7011) |
| Authorize inside a consume | Authorize before publishing | A propagated caller has no permissions — headers are unauthenticated |
| Consume `OrderPlaced` published through `IEventPublisher` | Consume `EventEnvelope<OrderPlaced>` | The envelope is what travels |
| Hard-code `queue:` addresses | `WithSendEndpointRoute<T>("{target-service}-{command}")` | One place names a cross-service route |
| Inject MassTransit's `IBus`, `IPublishEndpoint`, `ISendEndpointProvider` | Inject the Abstractions | SK0706; those bypass propagation and `Result` |
| Publish before the bus is ready | Gate on `/health/ready` | Early messages are dropped by the broker without an error |
| Hand-roll deduplication in `ConsumeAsync` | `WithIdempotency()` | A check-then-write is not atomic |
| Use a KMS-only key provider with `WithPayloadTransform` encryption | A synchronous key provider | The serializer is synchronous |
| `Task.Delay` before sending | `IMessageScheduler` | The broker holds a scheduled message across restarts |

## Design decisions

**Why MassTransit 8.5.x?** 8.5.10 is the last Apache-2.0 release and has a native `net10.0` target; 9.x is
commercially licensed. Every package here declares MIT, so a 9.x bump is a licence change disguised as a dependency
update — it needs a recorded licensing decision.

**Why satellites for transports and the outbox?** The core carries no broker client or EF Core, so a service
restores only what it runs; a new broker is a new satellite, never a branch in the core.

**Why no sagas, routing slips or request/response?** Long-running coordination belongs to `17.Workflows`, synchronous
calls to `11.Communication`.

**Why does the idempotency key include the endpoint and consumer?** Two consumers of one message must each process
it once; a key on `MessageId` alone would let the first suppress the second.

**Why only native ordering?** A kernel sequencing buffer would duplicate and fight the broker's own mechanism.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Messaging domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
