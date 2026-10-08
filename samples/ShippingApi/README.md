<div align="center">

# ShippingApi

**A shipping service on RabbitMQ: publish, send and schedule through the bus, with the caller's tenant, actor and correlation id arriving on the consumer.**

<sub>📂 <code>samples/ShippingApi</code> · <a href="../README.md">all samples</a> · needs RabbitMQ (Docker)</sub>

</div>

## What it shows

- **CloudEvents publish and point-to-point send** in one `AddSharedKernelMessaging(configuration).UseRabbitMq(…)` chain.
- **The publisher's caller on the consumer.** `WithInboundRequestContext()` rebuilds the tenant and actor, so a consumer
  reads `IRequestContext` exactly as an HTTP handler does; `WithAmbientCorrelationPropagation()` carries the
  correlation id.
- **Broker-side delayed delivery** (`WithDelayedDelivery()`), **at-most-once consumption** over an `IIdempotencyStore`
  (`WithIdempotency()`), and **retries, then a fault you can see** (`AddFaultConsumer`).
- **Asynchronous work over HTTP.** 202 Accepted with a `Location` (`ToAccepted`); a broker outage is a 503 problem.
- **Readiness that gates traffic** on the bus probe that `Build()` registers.

**Packages it uses:**
[SharedKernel.Messaging.MassTransit](../../src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit/README.md) ·
[SharedKernel.Messaging.MassTransit.RabbitMq](../../src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit.RabbitMq/README.md) ·
[SharedKernel.Contracts](../../src/Model/Contracts/SharedKernel.Contracts/README.md) ·
[SharedKernel.Execution](../../src/Foundation/SharedKernel.Execution/README.md) ·
[SharedKernel.Idempotency.Abstractions](../../src/Infrastructure/Idempotency/SharedKernel.Idempotency.Abstractions/README.md) ·
[SharedKernel.Application.Pipeline](../../src/Application/SharedKernel.Application.Pipeline/README.md) ·
[SharedKernel.Application.Mediator.MediatR](../../src/Application/SharedKernel.Application.Mediator.MediatR/README.md) ·
[SharedKernel.ServiceDefaults](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults/README.md) ·
[SharedKernel.ServiceDefaults.Security](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md) ·
[SharedKernel.Presentation.WebApi](../../src/Hosting/Presentation/SharedKernel.Presentation.WebApi/README.md)

Capabilities: [Messaging](../../src/Infrastructure/Messaging/README.md) · [Contracts](../../src/Model/Contracts/README.md) ·
[Idempotency](../../src/Infrastructure/Idempotency/README.md) · [Application](../../src/Application/README.md) ·
[Service defaults](../../src/Hosting/ServiceDefaults/README.md) · [Presentation](../../src/Hosting/Presentation/README.md)

## The endpoints

It consumes the **packed** NuGet packages the way any other service would, all through an HTTP surface you can drive
with `curl`:

```
POST /shipments               publish  → 202 + Location; ShipmentDispatched, consumed into a read model
GET  /shipments/{id}          read     → 404 until the consumer has run (an honest asynchronous write)
POST /shipments/{id}/hold     send     → one endpoint, not a broadcast
POST /shipments/{id}/chase    schedule → the broker holds it, not this process
POST /shipments/{id}/check    fail     → exhausts retries, then a fault consumer observes it
GET  /shipments/{id}/fault    read     → what the fault consumer saw
GET  /health/live /health/ready        → the bus-backed readiness probe
```

## Where to look

| Feature | Where to look |
| --- | --- |
| CloudEvents publish and consume | `POST /shipments` → `ShipmentDispatchedConsumer` |
| Point-to-point send with an explicit route | `WithSendEndpointRoute<HoldShipment>` → `HoldShipmentConsumer` |
| **The publisher's tenant and actor on the consumer** | `WithInboundRequestContext()`; the consumer injects `IRequestContext` and reads it like an HTTP handler would |
| **The request's correlation id on the consumer** | `UseSharedKernelRequestContext()` owns `X-Correlation-Id`; `WithAmbientCorrelationPropagation()` carries it across the broker (`RequestCorrelationId_ReachesTheConsumer`) |
| At-most-once consumption | `WithIdempotency()` over `InMemoryIdempotencyStore` |
| Retry, then a fault you can see | `WithRetry()` + `AddFaultConsumer<FailingShipmentCheck, ShipmentCheckFaultConsumer>()` |
| Transport-native deferred delivery | `WithDelayedDelivery()` → `IMessageScheduler.ScheduleAsync` |
| Readiness that actually gates traffic | the bus probe `Build()` registers, mapped by `AddSharedKernelReadiness()` |
| Commands and queries behind every endpoint | `AddSharedKernelApplication(typeof(Program).Assembly, app => app.UseMediatR())`; `ShipmentEndpoints` is an endpoint module (`IEndpointModule`, mapped by `app.MapEndpoints()`) whose endpoints only send through the kernel's `ISender`; the handlers in `Features/Shipments/` publish, send and schedule |
| `Result` at the HTTP boundary | `UseSharedKernelRequestContext()`, then `AddSharedKernelWebApi()` + `UseSharedKernelWebApi()`; `sender.Send(new PutShipmentOnHold(id, reason), ct).ToAccepted($"/shipments/{id}")` — 202 with a `Location` to watch (`ToHttpResult(id => TypedResults.Accepted(location, body))` where the 202 carries a body, as `POST /shipments` does), or an RFC 9457 problem (`messaging.unavailable` is 503); a missing shipment is a `shipment.not_found` 404 problem |

The consumer is the point of the whole sample:

```csharp
public sealed class ShipmentDispatchedConsumer(
    ShipmentProjection projection,
    IRequestContext caller,                 // ← the caller that PUBLISHED, not "nobody"
    ILogger<ShipmentDispatchedConsumer> logger)
    : ConsumerBase<EventEnvelope<ShipmentDispatched>>(logger)
{
    protected override Task ConsumeAsync(EventEnvelope<ShipmentDispatched> message, CancellationToken ct)
    {
        projection.RecordDispatch(/* … */, caller);   // caller.TenantId is set
        return Task.CompletedTask;
    }
}
```

It never reads a tenant out of the message body and never takes the actor as a parameter. Without
`WithInboundRequestContext()`, `caller.TenantId` would be `null` and every tenant-scoped write a real service
made here would fail closed.

## Run it

The tests start their own broker, so this is only needed to poke at it by hand.

```bash
docker compose -f samples/ShippingApi/compose.yaml up -d

dotnet pack Platform.SharedKernel.slnx -c Release -o ./nupkgs -p:MinVerVersionOverride=1.0.0-local.1
dotnet run --project samples/ShippingApi -p:SharedKernelPackageVersion=1.0.0-local.1
```

```bash
# Acting as a tenant and an actor — this sample reads them from two headers in place of a real
# identity provider. A production service authenticates the caller (AddOidcAuthentication(...)).
TENANT=$(uuidgen); H="-H X-Demo-Tenant:$TENANT -H X-Demo-Actor:operator-7"

ID=$(curl -sf $H -X POST localhost:5000/shipments \
      -H 'Content-Type: application/json' \
      -d '{"carrier":"acme-freight","trackingNumber":"TRK-100"}' | jq -r .shipmentId)

curl -sf localhost:5000/shipments/$ID | jq
# { "tenantId": "…", "actorId": "operator-7", "actorKind": 0, "deliveries": 1, … }
```

The RabbitMQ management UI is at <http://localhost:15672> (`guest` / `guest`) — the queues, the message rates and
the error queue are all worth a look while you drive the endpoints.

> **`masstransit/rabbitmq`, not the official image.** `WithDelayedDelivery()` uses the broker's delayed-message
> exchange, a community plugin the official image does not ship. This one has it enabled.

## Test it

```bash
dotnet test samples/ShippingApi/ShippingApi.Tests -p:SharedKernelPackageVersion=1.0.0-local.1
```

Nine scenarios against a real RabbitMQ broker (Testcontainers; Docker required). Nothing is faked — a publish
leaves the process and comes back to a consumer.

## What it found

This sample is not a demo. It found three defects in the messaging packages that no unit test had, all of which
looked correct in isolation and only showed up against a real broker:

1. **The event-publisher path never wrote the tenant transport header.** The value reached the CloudEvents
   envelope's body, which a transport-level consume filter cannot read without deserializing a payload it has no
   type for — so the envelope carried the right tenant and the consumer saw none.
2. **It stamped the transport correlation id only when the caller supplied a custom header or a partition key**,
   because that combination was the condition on a fast path that skipped the pipe callback entirely. The
   ordinary publish — the common case — lost it.
3. **The endpoint-name formatter produced an empty queue prefix** when the service name came from configuration
   rather than an inline action. Queues were declared as `hold-shipment` instead of `shipping-api-hold-shipment`,
   so two services sharing a broker would have contended for the same queues. Nothing threw; the wrong queues
   were simply declared.

All three are fixed and have regression tests, and both dispatch verbs now share one context-mapping path so they
cannot drift apart again.

It also surfaced an operational fact worth knowing: **MassTransit starts the bus in the background**, so the host
reports "started" before any queue exists, and a message published in that window is dropped by the broker
silently and successfully. The test suite waits for `/health/ready` before its first publish — exactly what
Kubernetes does in production. Before it did, roughly one run in three lost a message and looked like a
messaging defect.

## Why `PackageReference`

Like every sample here, this one resolves `SharedKernel.*` from the local `nupkgs/` feed rather than by
`ProjectReference`. The point is to prove the **packed** packages work for a consumer who has only the published
artifacts — a project reference would bypass exactly the thing under test. See
[samples/README.md](../README.md).

## Shortcuts a real service would not take

| Here | In production |
| --- | --- |
| `AddDemoIdentity()` builds the `IUserContext` from two request headers | An authentication package (`AddOidcAuthentication(...)`) builds it from a token; `AddSharedKernelRequestContext()` and everything after it are unchanged |
| `InMemoryIdempotencyStore` deduplicates within one process | `SharedKernel.Idempotency.Redis` or `.EfCore`, which reserve atomically across replicas |
| `ShipmentProjection` is a dictionary | A real read model written through [Persistence](../../src/Infrastructure/Persistence/README.md) inside the consumer's transaction |

Each is a deliberate trade: a sample about messaging should not require a database and a cache to run.
