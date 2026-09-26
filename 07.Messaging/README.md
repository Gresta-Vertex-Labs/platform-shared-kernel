<div align="center">

# 📨 SharedKernel Messaging

**Events and commands between .NET services — RabbitMQ and Azure Service Bus behind one contract, with the
caller's tenant travelling with the message, at-most-once consumption, and failures you handle instead of catch.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-verified-FF6600?logo=rabbitmq&logoColor=white)](SharedKernel.Messaging.MassTransit.RabbitMq/README.md)
[![Azure Service Bus](https://img.shields.io/badge/Azure%20Service%20Bus-supported-0078D4?logo=microsoftazure&logoColor=white)](SharedKernel.Messaging.MassTransit.AzureServiceBus/README.md)
[![MassTransit 8.5](https://img.shields.io/badge/MassTransit-8.5.x%20(Apache--2.0)-512BD4)](#why-masstransit-85-and-not-9x)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 5](https://img.shields.io/badge/packages-5-informational)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

[Packages](#the-packages) · [10-minute start](#a-messaging-service-in-10-minutes) · [Caller identity](#the-caller-travels-with-the-message) · [Failures](#failures-are-values) · [Sample](#see-it-run) · [Guarantees](#what-you-can-rely-on)

</div>

---

```csharp
builder.Services.AddRedisConnection(builder.Configuration);
builder.Services.AddRedisIdempotency(p => p.ForMessages());          // the store WithIdempotency() uses

builder.Services
    .AddSharedKernelMessaging(builder.Configuration)                      // SharedKernel:Messaging
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!) // SharedKernel.Messaging.MassTransit.RabbitMq
    .WithRetry()
    .WithIdempotency()                  // a redelivered message runs the consumer once
    .WithInboundRequestContext()        // the publisher's tenant and actor reach the consumer
    .AddConsumer<OrderPlacedConsumer>()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // includes the bus's "messaging" probe
```

That is the whole registration. Queue names, retry shape and dead-letter policy follow from it; a consumer
is a class with one method:

```csharp
public sealed class OrderPlacedConsumer(IOrderRepository orders, IRequestContext caller, ILogger<OrderPlacedConsumer> log)
    : ConsumerBase<EventEnvelope<OrderPlaced>>(log)
{
    protected override Task ConsumeAsync(EventEnvelope<OrderPlaced> envelope, CancellationToken ct) =>
        // caller.TenantId is the tenant that published — so this write is tenant-scoped,
        // exactly as it would be behind an HTTP request.
        orders.MarkPlacedAsync(envelope.Data.OrderId, ct);
}
```

## Why

Every service that publishes events makes the same decisions, and each one fails quietly when it is wrong:

- a consumer runs with **no tenant**, so every tenant-scoped write fails closed and the queue still looks healthy;
- a redelivered message runs the consumer twice, and the second charge goes out;
- a transport outage throws a broker-specific exception nobody wrote a `catch` for;
- two services pick the same queue name and quietly steal each other's work;
- a message is published one second before the bus finished connecting, and the broker drops it — successfully.

This domain is the set of decisions that make those hard to write, taken once.

## The packages

| Package | What it is | Take a dependency on it when |
| --- | --- | --- |
| [**SharedKernel.Messaging.Abstractions**](SharedKernel.Messaging.Abstractions/README.md) | Abstractions tier. The contracts: `IMessageBus`, `IEventPublisher`, `PublishContext`, `IMessageScheduler`, `IMessageHeaderPropagator`, `IFaultConsumer`, `IInboundMessageContextAccessor`, `MessagingErrorCodes`. No transport dependency. | Your library or application layer publishes or consumes messages |
| [**SharedKernel.Messaging.MassTransit**](SharedKernel.Messaging.MassTransit/README.md) | Adapter tier. The bus behind those contracts: the builder, consumers, retry, idempotency, dead-letter policy, caller propagation, tracing, metrics and the readiness probe. No broker client. | You are the composition root — the startup project |
| [**SharedKernel.Messaging.MassTransit.RabbitMq**](SharedKernel.Messaging.MassTransit.RabbitMq/README.md) | Adapter tier. `UseRabbitMq(...)`. | Your bus runs on RabbitMQ |
| [**SharedKernel.Messaging.MassTransit.AzureServiceBus**](SharedKernel.Messaging.MassTransit.AzureServiceBus/README.md) | Adapter tier. `UseAzureServiceBus(...)`, managed identity. | Your bus runs on Azure Service Bus |
| [**SharedKernel.Messaging.MassTransit.EfCore**](SharedKernel.Messaging.MassTransit.EfCore/README.md) | Adapter tier. `WithEntityFrameworkOutbox<TDbContext>()`. | You publish inside a database transaction |

Application code references the first. Only `Program.cs` references the others — the core plus exactly the
transport and integrations it uses, so a RabbitMQ service never restores the Azure SDK and a service without an
outbox never restores EF Core. That is what makes the transport replaceable, and what keeps MassTransit out of the
type signatures your tests have to construct.

## Architecture

```
your application code                    IMessageBus / IEventPublisher / IRequestContext
                                                   │  (abstractions only)
────────────────────────────────────────────────────────────────────────────────
composition root (Program.cs)            AddSharedKernelMessaging(configuration)
                                                   │
SharedKernel.Messaging.MassTransit       bus wiring · retry · idempotency · readiness
                                         propagators · consume filters · diagnostics
                                                   │  MessagingTransport / ConfigureMassTransit
             ┌─────────────────────────────────────┼─────────────────────────────┐
  .MassTransit.RabbitMq          .MassTransit.AzureServiceBus          .MassTransit.EfCore
  UseRabbitMq                    UseAzureServiceBus                    WithEntityFrameworkOutbox
             │                                     │                             │
         RabbitMQ                          Azure Service Bus              your DbContext
```

## A messaging service in 10 minutes

**1 — Configure.** One setting, plus a connection string.

```json
{
  "ConnectionStrings": { "rabbitmq": "amqp://guest:guest@localhost:5672" },
  "SharedKernel": { "Messaging": { "ServiceName": "order-service" } }
}
```

`ServiceName` is not a label: it prefixes every queue this service declares and is the CloudEvents `source` of
every event it publishes. It is validated at startup as a lowercase slug, because a capital or a space produces a
queue name a broker either rejects or quietly mangles — far from the configuration that caused it.

**2 — Declare the event.** A fact, in primitives, with a wire name that outlives the class name.

```csharp
[IntegrationEvent("orders.order-placed", Version = 1)]
public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId, decimal Total)
    : IIntegrationEvent;
```

**3 — Publish it.** No tenant, no correlation id, no headers at the call site — those arrive on their own.

```csharp
// In an endpoint, with SharedKernel.Presentation.WebApi: 202, or a problem response (messaging.unavailable is 503).
return eventPublisher.PublishAsync(
        new OrderPlaced(Guid.CreateVersion7(), clock.UtcNow, order.Id, order.Total), ct)
    .ToAccepted($"/orders/{order.Id}");
```

When the handler goes on after publishing, return the failure through the same boundary:
`if (published.IsFailure) return published.Error.ToErrorResult();`

**4 — Consume it**, as in the snippet at the top of this page.

**5 — Gate traffic on readiness.** `Build()` registers a readiness probe named `messaging`;
`services.AddHealthChecks().AddSharedKernelReadiness()` (`SharedKernel.ServiceDefaults`) maps it, and it reports
unhealthy until the bus is connected.
This matters more than it looks — see [the window before ready](#the-window-before-ready).

## The caller travels with the message

A consumer runs on a background thread with no HTTP request. Without help, `IRequestContext.TenantId` is `null`,
and `06.Persistence` — which fails closed on a null tenant — rejects every tenant-scoped write the consumer
attempts. The usual workaround is to put the tenant in the message body and enter a cross-tenant scope to act on
it, which is both boilerplate and a standing invitation to get tenancy wrong.

`WithInboundRequestContext()` wires both directions:

| Direction | What happens |
| --- | --- |
| **Publish** | The current caller's correlation id, tenant and actor are written as transport headers (`SharedKernel.Execution`'s one mapping, shared with REST, gRPC and Temporal). An anonymous caller writes no actor headers at all, rather than empty ones a consumer must tell apart from absent ones |
| **Consume** | They are read back into a `PropagatedRequestContext`, the consumer runs inside a `RequestContextScope`, and `IRequestContext` resolves to that caller inside the consume — and to the service's own caller everywhere else. Outbound calls the consumer makes carry the same tenant and correlation id |

So one handler serves the HTTP path and the message path without branching. Published from *inside* a consumer,
the original caller is carried onward, so a chain of consumers keeps attributing work to whoever started it.

> **Attribution, not authorization.** A permission check inside a consume always answers `false`, whatever the
> message said. Headers are attacker-controllable by anyone who can reach the broker, so a permission carried on
> one would be a permission granted by the wire. A consumer that must authorize re-resolves permissions from the
> identity provider using the subject id; it never trusts the message.

## Failures are values

A broker being unreachable is an operational condition a caller can act on, not a defect. Every dispatch verb
returns `Result`:

| Code | `ErrorType` (HTTP) | When |
| --- | --- | --- |
| `messaging.unavailable` | Unavailable (503) | The transport is unreachable or the connection dropped |
| `messaging.endpoint_not_found` | NotFound (404) | A send addressed a queue that does not exist |
| `messaging.serialization_failed` | Unexpected (500) | The payload could not be serialized |
| `messaging.publish_rejected` | Unexpected (500) | The broker refused the message |
| `messaging.invalid_message` | Validation (400) | The event failed validation before dispatch (unset `EventId`, wrong declared type) |
| `messaging.contract_violation` | Validation (400) | The event type has no valid `[IntegrationEvent]` attribute |

`messaging.unavailable` is `ErrorType.Unavailable` since P-562 (it was `Unexpected`, so an outage answered 500).

Anything the classifier does **not** recognise as a transport fault is rethrown unchanged, so a genuine bug in
your code is never laundered into a failed `Result`.

## What you get

| Capability | How |
| --- | --- |
| **At-most-once consumption of a message id** | `WithIdempotency()` over `SharedKernel.Idempotency.Abstractions`' atomic reserve/complete/release store, registered for `IdempotencyPurpose.Message` — `18.Idempotency` ships Redis and EF Core implementations |
| **Transactional outbox** | `WithEntityFrameworkOutbox<TDbContext>()` from `.MassTransit.EfCore` — your `DbContext` by generic parameter; no reference to `06.Persistence` |
| **Retry and circuit breaking** | `WithRetry()`, `WithCircuitBreaker()`; a consumer declares its own non-retryable exception types |
| **Dead-letter policy** | `WithDeadLetterPolicy()` (RabbitMQ), plus `AddFaultConsumer<T, TConsumer>()` to observe what exhausted its retries |
| **Deferred delivery** | `WithDelayedDelivery()` — the *broker* holds the message, so it survives this process restarting |
| **Ordered delivery** | A partition key, mapped to RabbitMQ routing-key affinity or Azure Service Bus session identity |
| **Compression and encryption** | `WithPayloadTransform()`, built on `01.Core`'s primitives; compress-then-encrypt, and the reverse on consume |
| **Schema evolution** | `WithVersionTranslator<TOld, TNew, T>()` — an old message is projected to the current shape before the consumer sees it |
| **Tracing and metrics** | An `ActivitySource` and a `Meter` on every verb; wired by `13.ServiceDefaults`' `WithMessagingTelemetry()` |
| **Readiness** | An `IReadinessProbe` named `messaging` over the real configured bus — never a second connection built from copied configuration |

## The window before ready

MassTransit starts the bus in the background and returns, so the host reports "started" before the broker
connection exists and before any queue has been declared. **A message published in that window is routed to an
exchange with nothing bound to it and is dropped by the broker — silently and successfully.** `PublishAsync`
returns success, because the publish itself succeeded.

That is what the readiness probe is for. In production, Kubernetes holds traffic until `/health/ready` passes.
Do the same in tests: `samples/ShippingApi` waits for readiness before its first publish, and before it did,
roughly one run in three lost a message and looked like a messaging defect.

## Why MassTransit 8.5 and not 9.x

**Licensing.** MassTransit 9.x carries a bare `licenseUrl` with no SPDX expression and a "Massient, Inc."
copyright; 8.5.10 carries `<license type="expression">Apache-2.0</license>`. Every package in this repo declares
MIT, so shipping on a 9.x pin would put out a package claiming MIT while imposing a commercial obligation on
every service that consumes it.

8.5.10 has a native `net10.0` target, so the pin costs no framework fidelity, and the whole migration was one
API change. Do not bump it without a recorded licensing decision.

## See it run

[**samples/ShippingApi**](../samples/ShippingApi/README.md) is a service on the packed packages (MassTransit core
and the RabbitMQ transport) — publish, send,
delayed delivery, idempotency, inbound caller identity, retry, a fault consumer and the readiness probe — with
nine end-to-end scenarios against a real RabbitMQ broker.

```bash
dotnet pack Platform.SharedKernel.slnx -c Release -o ./nupkgs -p:MinVerVersionOverride=1.0.0-local.1
dotnet test samples/ShippingApi/ShippingApi.Tests -p:SharedKernelPackageVersion=1.0.0-local.1
```

It earns its place: it found three defects no unit test had — the event-publisher path never wrote the tenant
transport header, it stamped the transport correlation id only when a custom header happened to be set, and the
endpoint-name formatter produced an **empty** queue prefix when the service name came from configuration, so two
services on one broker would have contended for the same queues. All three were invisible in isolation.

## What you can rely on

| Guarantee | How |
| --- | --- |
| **No silent message loss** | A failed consume is never acknowledged; the idempotency filter reports an in-flight duplicate rather than acknowledging a message that may never be consumed |
| **No accidental double-processing** | Reservation is a single conditional write in the store, never a read followed by a write |
| **No exceptions for expected failures** | `Result` with a stable `messaging.*` code; unrecognised exceptions are rethrown, never swallowed |
| **No tenant leakage** | The tenant is carried explicitly and is absent — not inherited — when the publisher had none |
| **No queue-name collisions** | Every queue is prefixed with the validated service name |
| **No transport in your application code** | Consumers receive the deserialized message and a token; MassTransit types appear only in `Program.cs` |
| **No untracked API change** | Every package's public surface is tracked in `PublicAPI.*.txt`; an addition or signature change fails the build until reviewed |
| **Tested for real** | RabbitMQ through Testcontainers in CI, plus a full sample service end to end |

**Deliberately out of scope:** request/response over the bus (use `11.Communication`); sagas, routing slips and
multi-step orchestration (use `17.Workflows`); recurring and cron jobs (use `19.Scheduling`); Kafka and
Amazon SQS (no adapter today).

## Where to go next

| Topic | Read |
| --- | --- |
| The programming model: bus, publisher, contexts, idempotency, faults, errors | [Abstractions](SharedKernel.Messaging.Abstractions/README.md) |
| Wiring a bus: transports, retry, outbox, dead-letter, payload transform, diagnostics | [MassTransit](SharedKernel.Messaging.MassTransit/README.md) |
| A complete service, tested against a real broker | [samples/ShippingApi](../samples/ShippingApi/README.md) |
| The idempotency store contract and its Redis and PostgreSQL stores | [18.Idempotency](../18.Idempotency/SharedKernel.Idempotency.Abstractions/README.md) |
| Readiness endpoints and telemetry wiring (`AddSharedKernelReadiness`, `WithMessagingTelemetry`) | [ServiceDefaults](../13.ServiceDefaults/SharedKernel.ServiceDefaults/README.md) |
| Maintainer rules and design decisions | [CLAUDE.md](CLAUDE.md) |
