# SharedKernel.Messaging.Abstractions

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Transport dependencies: 0](https://img.shields.io/badge/transport%20dependencies-0-brightgreen)
![Provider: neutral](https://img.shields.io/badge/provider-neutral-informational)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Messaging contracts for .NET services: publish and send as `Result` values, at-most-once consumption through
> an atomic reservation, and the publishing caller's tenant and actor available to the consumer.**

Messaging goes wrong in quiet ways:

- a consumer runs with no tenant, so every tenant-scoped write fails closed while the queue looks healthy;
- a redelivered message runs the consumer twice, and the second charge goes out;
- a broker outage throws a transport-specific exception nobody wrote a `catch` for, in the middle of a request;
- a "have I processed this?" check and a "mark it processed" write race, and both deliveries run.

This package defines the contracts that make those mistakes hard to write. Application code depends only on it;
the host picks a transport — [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md)
for RabbitMQ and Azure Service Bus.

| You get | So that |
| --- | --- |
| `IMessageBus` / `IEventPublisher`, every verb returning `Result` | An unreachable broker is a value you handle, not an exception you must know to catch |
| Consumer idempotency options (the store is `SharedKernel.Idempotency.Abstractions`' `IIdempotencyStore`) | A duplicate delivery is refused atomically; "in flight" and "already done" are different answers |
| `IInboundMessageContextAccessor` (over `SharedKernel.Execution`'s `IRequestContext`) | A consumer knows which tenant and which actor caused the message |
| `PublishContext` | Correlation, causation, tenant, subject, partition key and headers, per dispatch |
| `IMessageHeaderPropagator` | Ambient values reach every message without a line at each call site |
| `IMessageScheduler` | The broker holds a deferred message, so it survives this process restarting |
| `IFaultConsumer` | A message that exhausted its retries becomes visible instead of only ending up in an error queue |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Which type do I need?](#which-type-do-i-need)
- [Publishing](#publishing)
- [Idempotency](#idempotency)
- [The caller across the bus](#the-caller-across-the-bus)
- [Errors](#errors)
- [Dependencies](#dependencies)

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.Abstractions" />
```

The version comes from your single `SharedKernelVersion`, like every SharedKernel package. This is an
**Abstractions-tier** package: your application and domain projects reference it. Your **startup project**
additionally references `SharedKernel.Messaging.MassTransit` and one transport satellite
(`SharedKernel.Messaging.MassTransit.RabbitMq` or `.AzureServiceBus`), which register the implementations. That
split is what keeps the transport out of the type signatures your tests have to construct.

## Quick start

```csharp
// Publish a fact. Tenant, actor and correlation arrive on their own from registered propagators.
public sealed class PlaceOrderHandler(IEventPublisher events)
{
    public async Task<Result> HandleAsync(PlaceOrder command, CancellationToken ct)
    {
        // ... place the order ...

        return await events.PublishAsync(
            new OrderPlaced(Guid.CreateVersion7(), DateTimeOffset.UtcNow, command.OrderId), ct);
    }
}

// Send a command. Point-to-point: exactly one consumer, however many replicas are running.
public sealed class HoldShipmentHandler(IMessageBus bus)
{
    public Task<Result> HandleAsync(Guid shipmentId, CancellationToken ct) =>
        bus.SendAsync(new HoldShipment(shipmentId, "customs"), ct);
}
```

## Which type do I need?

| I want to… | Use |
| --- | --- |
| Tell everyone a fact happened | `IEventPublisher.PublishAsync` with an `IIntegrationEvent` — wrapped in a CloudEvents envelope |
| Broadcast a message that is not an integration event | `IMessageBus.PublishAsync` |
| Ask exactly one consumer to do something | `IMessageBus.SendAsync` |
| Attach a tenant, correlation id, partition key or header to one dispatch | The `Action<PublishContext>` overload |
| Deliver a message later | `IMessageScheduler.ScheduleAsync` |
| Stop a duplicate delivery from running the consumer twice | Register an `IIdempotencyStore` for `IdempotencyPurpose.Message` — a ready-made store (below) or your own |
| Push an ambient value onto every outgoing message | Implement `IMessageHeaderPropagator` |
| See what failed after its retries ran out | Implement `IFaultConsumer<TMessage>` |
| Report bus health to Kubernetes | Nothing here — the MassTransit package registers an `IReadinessProbe` named `messaging`; the host maps it with `AddSharedKernelReadiness()` |
| Accept an old message shape during a rolling deploy | Implement `IMessageVersionTranslator<TOld, TNew>` |

## Publishing

Every verb returns `Result`. A transport being unreachable is an operational condition, not a defect:

```csharp
Result published = await events.PublishAsync(orderPlaced, ct);

if (published.IsFailure)
{
    // published.Error.Code is a stable messaging.* string — branch on it, log it, or return it.
    return published;
}
```

`PublishContext` carries everything about a dispatch that is not the message:

```csharp
await bus.PublishAsync(
    reportRequested,
    ctx => ctx.WithTenantId(tenantId)         // a background job acting for a tenant it is not scoped to
              .WithPartitionKey(accountId)    // per-account ordering
              .WithHeader("x-sk-priority", "high"),
    ct);
```

**Propagators run first; your callback runs last.** An explicit value here always wins over the ambient one, so
you never have to disable a propagator to override it once.

## Idempotency

Brokers deliver at least once. The consumer filter refuses the second delivery through
`SharedKernel.Idempotency.Abstractions`' `IIdempotencyStore`, registered for `IdempotencyPurpose.Message` (the same
contract guards the application pipeline's commands under `IdempotencyPurpose.Request`). It is deliberately a
*reservation*, not a check followed by a write; the message id ("D" form) is the key, scoped by the ambient tenant,
the fingerprint is fixed, and this package's `IdempotencyOptions` (section `SharedKernel:Messaging:Idempotency`)
supplies the lease (`LeaseDuration`, 30 s) and the retention (`ExpiryWindow`, 24 h):

```csharp
Task<IdempotencyReservation> TryBeginAsync(IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken ct);
Task<bool> CompleteAsync(IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken ct);
Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken ct);
```

| `Status` | Meaning | What the filter does |
| --- | --- | --- |
| `Started` | This delivery now holds the reservation | Runs the consumer, then completes — or releases if it throws |
| `InProgress` | Another delivery holds it right now | Leaves the message unacknowledged so the broker redelivers it |
| `Completed` | A previous delivery consumed it to completion | Returns without running the consumer, acknowledging the message |
| `FingerprintMismatch` | Cannot happen for messages (the fingerprint is fixed) | Throws — it indicates a store defect |

> **`TryBeginAsync` must be one conditional write** — a Redis `SET NX`, an `INSERT … ON CONFLICT DO NOTHING` —
> never a read followed by a write. No caller can make a check-then-act pair atomic from outside.
>
> The three-status answer is not ceremony. The previous two-method contract returned a boolean, which cannot
> distinguish "in flight" from "completed": a redelivery following a **failed** attempt was reported as a
> duplicate, acknowledged, and dropped. Silent message loss, in the component whose job is not losing messages.

**Use a ready-made store** rather than writing one: `SharedKernel.Idempotency.Redis`
(`AddRedisIdempotency(p => p.ForMessages())`, atomic Lua reservation) or `SharedKernel.Idempotency.EfCore`
(`AddEfCoreIdempotency(..., p => p.ForMessages())`, `INSERT … ON CONFLICT` on a unique key). Both are tenant-scoped
and both have been verified against real infrastructure. A custom store registers with
`AddIdempotencyStore<T>(IdempotencyPurpose.Message)`.

> **Known limitation:** the key is the message id alone, with no consumer or endpoint in it. If one service has two
> receive endpoints (or two polymorphic consumers) that receive the same message, the second is skipped as a
> duplicate. Do not enable consumer idempotency in such a service until the key includes the endpoint.

## The caller across the bus

A consumer has no HTTP request, so `IRequestContext.TenantId` is `null` and tenant-scoped persistence fails
closed. The caller contract is `SharedKernel.Execution`'s (Foundation tier), shared with HTTP, gRPC, Temporal
and scheduled jobs; this package adds only the accessor for the current delivery:

| Type | Package | Role |
| --- | --- | --- |
| `WellKnownHeaders` (`CorrelationId`, `TenantId`, `ActorId`, `ActorKind`, `ClientId`) | `SharedKernel.Primitives` | The header names the caller travels under (`X-Correlation-Id`, `X-Tenant-Id`, `x-sk-actor-*`, `x-sk-client-id`) |
| `RequestContextPropagation` | `SharedKernel.Execution` | The one mapping between an `IRequestContext` and those headers |
| `PropagatedRequestContext` | `SharedKernel.Execution` | An `IRequestContext` rebuilt from those headers |
| `IInboundMessageContextAccessor` | this package | The current delivery's identity, or `null` outside a consume |

Turn it on with `MessagingBusBuilder.WithInboundRequestContext()` in `SharedKernel.Messaging.MassTransit`. After
that, injecting `IRequestContext` into a consumer just works — it answers for the caller that published — and the
consumer runs inside a `RequestContextScope`, so the calls it makes carry the same tenant and correlation id.

> **Attribution, not authorization.** `PropagatedRequestContext.HasPermissionAsync` always returns `false`,
> whatever the message said. Headers are attacker-controllable by anyone who can reach the broker, so a
> permission carried on one would be a permission granted by the wire.

## Errors

`MessagingErrorCodes` — stable strings, safe to branch on and to alert on:

| Code | Meaning |
| --- | --- |
| `messaging.unavailable` | Transport unreachable, or the connection dropped |
| `messaging.endpoint_not_found` | A send addressed a queue that does not exist |
| `messaging.serialization_failed` | The payload could not be serialized |
| `messaging.publish_rejected` | The broker refused the message |
| `messaging.invalid_message` | The event failed validation before dispatch |
| `messaging.contract_violation` | The event type has no valid `[IntegrationEvent]` attribute |

Only cancellation throws. Anything the transport package does not recognise as an operational fault is rethrown
unchanged, so a bug in your code is never laundered into a failed `Result`.

## Dependencies

| Package | Why |
| --- | --- |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `IMessagingBuilder.Services` |
| `SharedKernel.Primitives` | `Result` / `Error` |
| `SharedKernel.Contracts` | The `IIntegrationEvent` constraint on `IEventPublisher` |
| `SharedKernel.Execution` | `IRequestContext` and `TenantId` (`PublishContext.WithTenantId`) — Foundation tier |

This package is in the **Abstractions** tier: it references Foundation and Model packages plus
`Microsoft.Extensions.DependencyInjection.Abstractions` only. **No transport dependency, and no configuration binder.** A service that consumes `IMessageBus` without composing
a bus inherits neither.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
See the [domain overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md)
for how the pieces fit together, and
[samples/ShippingApi](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/ShippingApi/README.md)
for a working service.
