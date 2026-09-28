# SharedKernel.Messaging.Abstractions

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Abstractions](https://img.shields.io/badge/tier-Abstractions-1f6feb)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Transport dependencies: 0](https://img.shields.io/badge/transport%20dependencies-0-brightgreen)

> **The messaging contracts your application code injects: publish events and send commands as `Result` values,
> schedule broker-held messages, and read the publishing caller inside a consumer, with no transport in any
> signature.**

| You get | So that |
| --- | --- |
| `IEventPublisher` for `IIntegrationEvent`s, wrapped in a CloudEvents envelope | Facts leave the service in one platform-wide wire shape |
| `IMessageBus.PublishAsync` / `SendAsync`, every verb returning `Result` | An unreachable broker is a value you handle, not a transport exception nobody catches |
| `PublishContext` (correlation, causation, tenant, subject, partition key, headers) | Per-dispatch metadata without touching the transport |
| `IMessageHeaderPropagator` | Ambient values reach every outgoing message without a line at each call site |
| `IMessageScheduler` | A deferred message is held by the broker and survives this process restarting |
| `IInboundMessageContextAccessor` (an `IRequestContext`) | A consumer knows which tenant and which actor caused the message |
| `IFaultConsumer<T>`, `IMessageVersionTranslator<TOld, TNew>` | Retry-exhausted messages become visible; old message shapes survive a rolling deploy |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.Abstractions" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Abstractions — reference it from your **Application** project |
| Depends on | `SharedKernel.Primitives`, `SharedKernel.Execution`, `SharedKernel.Contracts`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Namespaces | `SharedKernel.Messaging.Abstractions.EventPublisher`, `.MessageBus`, `.Scheduling`, `.Context`, `.Faults`, `.HeaderPropagation`, `.SchemaEvolution`, `.Errors`, `.Options`, `.Idempotency`, `.Extensions` |

The implementations come from
[`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md)
plus one transport satellite, referenced by the **Api/Worker** project only.

## Quick start

The host registers the bus once (see the MassTransit package). Application code only injects:

```csharp
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Primitives.Results;

[IntegrationEvent("shipping.shipment-dispatched", Version = 1)]
public sealed record ShipmentDispatched(Guid EventId, DateTimeOffset OccurredOn, Guid ShipmentId) : IIntegrationEvent;

public sealed record HoldShipment(Guid ShipmentId, string Reason);

public sealed class ShipmentService(IEventPublisher events, IMessageBus bus)
{
    // A fact, fanned out to every subscriber, inside a CloudEvents EventEnvelope<ShipmentDispatched>.
    public Task<Result> DispatchedAsync(Guid shipmentId, DateTimeOffset at, CancellationToken ct) =>
        events.PublishAsync(new ShipmentDispatched(Guid.CreateVersion7(), at, shipmentId), ct);

    // A command, delivered to exactly one queue however many replicas consume it.
    public Task<Result> HoldAsync(Guid shipmentId, CancellationToken ct) =>
        bus.SendAsync(new HoldShipment(shipmentId, "customs"), ct);
}
```

```csharp
Result published = await events.PublishAsync(
    evt,
    ctx => ctx.WithTenantId(tenantId)        // a job acting for a tenant it is not scoped to
              .WithPartitionKey(accountId)   // per-account ordering
              .WithSubject(accountId),
    ct);

if (published.IsFailure)
    return published;                        // published.Error.Code is a stable messaging.* string
```

## How it works

| I want to… | Use |
| --- | --- |
| Tell everyone a fact happened | `IEventPublisher.PublishAsync` with an `IIntegrationEvent` |
| Broadcast a message that is not an integration event | `IMessageBus.PublishAsync` |
| Ask exactly one consumer to do something | `IMessageBus.SendAsync` |
| Attach tenant, correlation, partition key or a header to one dispatch | The `Action<PublishContext>` overload of `PublishAsync` |
| Deliver a message later | `IMessageScheduler.ScheduleAsync` (needs `WithDelayedDelivery()` on the bus) |
| Push an ambient value onto every outgoing message | Implement `IMessageHeaderPropagator` |
| See what failed after its retries ran out | Implement `IFaultConsumer<TMessage>` |
| Accept an old message shape during a rolling deploy | Implement `IMessageVersionTranslator<TOld, TNew>` |
| Know who published the message being consumed | Inject `IRequestContext` (or `IInboundMessageContextAccessor`) |

- **Propagators first, your callback last.** Every registered `IMessageHeaderPropagator` runs on every publish and
  send, in registration order; the `Action<PublishContext>` runs after them, so an explicit value always wins.
  `SendAsync` has no callback — propagator output alone shapes a send.
- **Failures are values.** A recognised transport fault becomes a `messaging.*` `Error`; anything unrecognised is
  rethrown by the implementation, so a bug is never laundered into a failed `Result`. Only cancellation throws.
- **Consumer idempotency.** Brokers deliver at least once. With `WithIdempotency()` on the bus, each delivery is
  reserved atomically through `SharedKernel.Idempotency.Abstractions`' `IIdempotencyStore` for
  `IdempotencyPurpose.Message`: `Started` runs the consumer, `InProgress` leaves the message unacknowledged for
  redelivery, `Completed` acknowledges without running it, `FingerprintMismatch` throws (a store defect). The key
  is `{MessageId:D}:{sha256(receive-endpoint path | consumer type)}`, so each consumer deduplicates its own deliveries,
  scoped by the ambient tenant. `IdempotencyOptions` supplies the lease and retention.
- **The caller across the bus.** With `WithInboundRequestContext()` the publisher's correlation id, tenant and actor
  travel as `WellKnownHeaders`; the consumer runs inside a `RequestContextScope` whose `IRequestContext` is a
  `PropagatedRequestContext` rebuilt from them. `IInboundMessageContextAccessor.Current` is that context, or `null`
  outside a consume.

## Configuration

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Messaging:ServiceName` | `string` | — (required) | Lowercase slug `^[a-z0-9]+(-[a-z0-9]+)*$`, ≤ 100 chars. Prefixes every queue this service declares and is the CloudEvents `source`. Validated at startup by the MassTransit package |

`IdempotencyOptions` (`LeaseDuration` 30 s, `ExpiryWindow` 24 h; the lease must be shorter than the window) declares
the section name `SharedKernel:Messaging:Idempotency`, but the bus sets it through
`WithIdempotency(o => …)` rather than binding it from configuration.

## Reference

### Contracts

| Type | Lifetime (as registered by the bus) | Members |
| --- | --- | --- |
| `IEventPublisher` | Scoped | `PublishAsync<TEvent>(evt, ct)`, `PublishAsync<TEvent>(evt, Action<PublishContext>, ct)`; `TEvent : class, IIntegrationEvent` |
| `IMessageBus` | Scoped | `PublishAsync<T>(msg, ct)`, `PublishAsync<T>(msg, Action<PublishContext>, ct)`, `SendAsync<T>(cmd, ct)` |
| `IMessageScheduler` | Scoped | `Task<Guid> ScheduleAsync<T>(msg, DateTimeOffset deliverAt, ct)`, `CancelAsync(Guid token, ct)` |
| `IInboundMessageContextAccessor` | Scoped | `IRequestContext? Current` |
| `IMessageHeaderPropagator` | Scoped | `void Propagate(PublishContext context)` |
| `IFaultConsumer<TMessage>` | Scoped | `HandleAsync(faultId, faultTimestamp, faultedMessage, IReadOnlyList<FaultExceptionInfo>, ct)` |
| `IMessageVersionTranslator<in TOld, out TNew>` | Singleton | `TNew Translate(TOld old)` — pure, synchronous |
| `ISendEndpointResolver` | — | `Uri Resolve<T>()` — the send-address convention `queue:{service-name}-{kebab-type}` |
| `IMessagingBuilder` | — | `IServiceCollection Services` — the base of transport builders |

`PublishContext`: `WithCorrelationId(Guid)`, `WithCausationId(Guid)`, `WithTenantId(TenantId)`,
`WithSubject(string)`, `WithPartitionKey(string)`, `WithHeader(string, string)`; read back through
`CorrelationId`, `CausationId`, `TenantId`, `Subject`, `PartitionKey`, `Headers`.

### Errors

`MessagingErrorCodes` (factories in `MessagingErrors`):

| Code | Type | When |
| --- | --- | --- |
| `messaging.unavailable` | Unavailable (503) | The broker is unreachable or timed out; retryable |
| `messaging.endpoint_not_found` | NotFound | A send addressed an endpoint that does not exist |
| `messaging.serialization_failed` | Unexpected | The payload could not be serialized (including the payload transform) |
| `messaging.publish_rejected` | Unexpected | The broker refused this message |
| `messaging.invalid_message` | Validation | The integration event failed envelope validation before dispatch |
| `messaging.contract_violation` | Validation | The event type has no valid `[IntegrationEvent]` attribute |

### Logging

This package does not log. The implementation's events (7000–7099) are listed in the
[MassTransit package README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md#logging).

## Testing

Reference [`SharedKernel.Messaging.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Messaging.Testing/README.md)
from your test project:

```csharp
using SharedKernel.Testing.Messaging;

services.AddInMemoryEventPublisher().AddInMemoryMessageBus();   // singletons, recording every dispatch

var events = provider.GetRequiredService<InMemoryEventPublisher>();
ShipmentDispatched evt = events.ShouldHavePublishedOnce<ShipmentDispatched>();
PublishContext ctx = events.ShouldHavePublishedContext<ShipmentDispatched>();

var bus = provider.GetRequiredService<InMemoryMessageBus>();
bus.ShouldHaveSent<HoldShipment>();
```

Both fakes run the propagators you pass them, so a propagator can be tested through them. For consumer idempotency,
`SharedKernel.Idempotency.Testing`'s `AddFakeIdempotencyStore(IdempotencyPurpose.Message)` provides the store.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Inject MassTransit's `IBus`, `IPublishEndpoint` or `ISendEndpointProvider` | Inject `IEventPublisher` / `IMessageBus` | Their failures are exceptions and bypass propagation; SK0706 flags them |
| Ignore the returned `Result` | Return or branch on it | A broker outage is reported only there; SK0030 flags a discarded `Result` |
| Consume `ShipmentDispatched` when it was published with `IEventPublisher` | Consume `EventEnvelope<ShipmentDispatched>` and read `.Data` | The envelope is what travels |
| Read the tenant from the message body | Inject `IRequestContext` with `WithInboundRequestContext()` on the bus | One caller contract on every channel |
| Rely on a permission check inside a consume | Authorize before publishing | `PropagatedRequestContext.HasPermissionAsync` always returns `false` — broker headers are unauthenticated |
| Use `typeof(TEvent).Name` as an event's wire name | `IntegrationEventDescriptor.For<TEvent>().Name` | The attribute name is the contract |
| `Task.Delay` before sending | `IMessageScheduler.ScheduleAsync` | An in-process delay dies with the process |
| Cancel a schedule token from another scope or process | Cancel in the scope that scheduled it | The implementation tracks tokens per scope; an unknown token is a silent no-op |
| Do I/O in `IMessageVersionTranslator.Translate` | Keep it a pure projection | It runs synchronously in the consume path |

## Design decisions

**Why `Result` and not exceptions?** A broker outage is an operational condition that a handler should be able to
return as a 503, not a defect. Unrecognised exceptions still propagate, so bugs stay loud.

**Why a separate `IEventPublisher`?** It is constrained to `IIntegrationEvent` and always builds the CloudEvents
envelope with `EventEnvelope.Wrap` — the only construction path — so every event on the platform has one shape.

**Why is the caller attribution only?** Anyone with broker access can write headers. Carrying permissions on a
message would be a permission granted by the wire.

**Why no configuration binder here?** An Abstractions-tier package takes no runtime dependencies beyond
`Microsoft.Extensions.*.Abstractions`; binding and validation live in the MassTransit package.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Messaging domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
