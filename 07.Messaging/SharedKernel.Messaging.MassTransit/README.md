# SharedKernel.Messaging.MassTransit

MassTransit 9.x wiring for Platform.SharedKernel microservices: `MassTransitMessageBus`,
`MassTransitEventPublisher`, `ConsumerBase<TMessage>`, `MessagingBusBuilder`, RabbitMQ and Azure
Service Bus transports, configurable retry, a transactional EF Core outbox integration (wired via a
generic `TDbContext` type parameter — no `06.Persistence.*` reference), opt-in idempotent-consumer
deduplication, header propagation (including built-in ambient correlation and tenant-context
propagators), ordered delivery, opt-in payload compression/encryption, and a bus readiness probe.
CloudEvents-compliant envelope publishing via `SharedKernel.Contracts`'s `EventEnvelope<TEvent>`.

## Install

```
dotnet add package SharedKernel.Messaging.MassTransit
```

```xml
<PackageReference Include="SharedKernel.Messaging.MassTransit" Version="1.0.0" />
```

## Usage

```csharp
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithRetry()
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// Application layer injects the abstractions only — never the MassTransit concrete types
public sealed class PlaceOrderHandler(IMessageBus bus, IEventPublisher publisher) { /* ... */ }

// Integration event — IEventPublisher only accepts IIntegrationEvent types with a declared wire name
[IntegrationEvent("orders.order-placed")]
public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;

// Consumer implementation — IEventPublisher publishes EventEnvelope<TEvent>, so that is what is consumed
public sealed class OrderPlacedConsumer : ConsumerBase<EventEnvelope<OrderPlaced>>
{
    protected override Task ConsumeAsync(EventEnvelope<OrderPlaced> message, CancellationToken ct)
    {
        // Business logic only — no MassTransit concerns here. message.Data is the event;
        // message.Type, .Source, .Subject, .TenantId and .CorrelationId are the CloudEvents metadata.
        // Do not swallow exceptions: an unhandled exception activates MassTransit's
        // retry/fault policies.
    }
}
```

## What `MassTransitEventPublisher` puts on the envelope

`IEventPublisher.PublishAsync` builds the envelope with `EventEnvelope.Wrap(...)` only:

| Envelope property | Source |
|---|---|
| `Id`, `Time` | the event's `EventId` and `OccurredOn` |
| `Type`, `DataVersion` | the event's `[IntegrationEvent(name, Version = n)]` attribute |
| `Source` | `MessagingOptions.ServiceName` |
| `Subject` | `PublishContext.WithSubject(...)`, otherwise omitted |
| `TenantId` | `PublishContext.WithTenantId(...)` (or `WithTenantContext<T>()`), otherwise omitted |
| `CorrelationId` | `PublishContext.WithCorrelationId(...)`, else `Activity.Current.TraceId`, else a new GUID |
| `CausationId` | `PublishContext.WithCausationId(...)`, otherwise omitted |

The `EventPublisher.Publish` activity and the `messaging.publish.count` counter tag
`messaging.event_type` with the attribute name (e.g. `orders.order-placed`) — the same value as
the envelope's `Type` — never the CLR class name.

See [`07.Messaging/CLAUDE.md`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/CLAUDE.md)
for the full `MessagingBusBuilder` fluent API — Azure Service Bus, the EF Core outbox, circuit
breakers, fault consumers, deferred scheduling, sagas, batch consumers, dead-letter policy, ordered
delivery, and payload transform are all documented there with worked examples.

## Recipe: ambient correlation and tenant-identity propagation

Every published `EventEnvelope<TEvent>` carries `CorrelationId` and `TenantId` attributes. Rather than
hand-rolling an `IMessageHeaderPropagator` that reads `Activity.Current` or a tenant provider
directly, use the two built-in propagators shipped in this package:

- `WithAmbientCorrelationPropagation()` — zero-argument; populates `CorrelationId` from the ambient
  `Activity.Current.TraceId` on every dispatch verb. No consumer-authored class required.
- `WithTenantContext<TAccessor>()` — bridges the locally-owned `ITenantContextAccessor` seam
  (`SharedKernel.Messaging.Abstractions`) to your service's real tenant-identity source. You write
  **only** the `ITenantContextAccessor` implementation; the built-in `TenantHeaderPropagator` does
  the rest. When no accessor is registered, tenant propagation is a provable no-op — `TenantId`
  simply stays `null`, exactly like an unset `CorrelationId`/`CausationId`.

```csharp
services
    .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    .UseRabbitMq("rabbitmq://localhost")
    .WithAmbientCorrelationPropagation()             // zero-argument — no consumer-authored class
    .WithTenantContext<AppTenantContextAccessor>()   // bridges the seam below; scoped
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

// AppTenantContextAccessor — the ONLY class you write to get tenant identity flowing into
// every published EventEnvelope<TEvent>.TenantId automatically. Bridges the locally-owned
// ITenantContextAccessor seam to your service's real tenant source (12.Security's
// ITenantProvider here), mirroring 05.Application's IAuthorizationContext/IUnitOfWork
// bridge pattern — SharedKernel.Messaging.* never references 12.Security directly.
public sealed class AppTenantContextAccessor(ITenantProvider tenantProvider) : ITenantContextAccessor
{
    public Guid? TenantId => tenantProvider.TenantId;
}
```

Both propagators run automatically before dispatch on `IMessageBus.PublishAsync`, `.SendAsync`,
`.RequestAsync`, and `IEventPublisher.PublishAsync` — you never call them directly. An explicit
`PublishContext.WithCorrelationId(...)`/`.WithTenantId(...)` in a per-call `Action<PublishContext>`
callback always overrides the propagated ambient value.

If you have an additional service-specific concern to propagate (feature flags, a custom header),
implement `IMessageHeaderPropagator` yourself and register it via `WithHeaderPropagator<T>()` —
propagators compose additively in registration order.

## Layering

```
SharedKernel.Messaging.MassTransit  →  SharedKernel.Messaging.Abstractions, SharedKernel.Contracts,
                                        SharedKernel.Compression, SharedKernel.Cryptography (01.Core),
                                        MassTransit 9.x
```

Target framework: `net10.0`. No `06.Persistence.*` reference — the EF Core outbox is wired via a
generic `TDbContext` type parameter only.

For full documentation see
[`07.Messaging/CLAUDE.md`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/CLAUDE.md).
