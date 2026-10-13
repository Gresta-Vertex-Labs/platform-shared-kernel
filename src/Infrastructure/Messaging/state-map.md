# 07.Messaging — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | Abstractions | ● | `IMessageBus`/`IEventPublisher` (every verb returns `Result`), `IMessageScheduler`, `ISendEndpointResolver`, `IMessageHeaderPropagator`, `IFaultConsumer`, `IMessageVersionTranslator`, `MessagingOptions`, the public `MessagingErrorCodes`. |
| `SharedKernel.Messaging.MassTransit` | Adapter | ● | `AddSharedKernelMessaging(configuration).Use{Transport}(…)…Build()` — retry, circuit breaker, dead-letter policy, delayed delivery, consumer idempotency over `IIdempotencyStore`, ordered delivery, payload transform, telemetry, `messaging` probe, `WithInboundRequestContext()`. Pinned to MassTransit 8.5.x (last Apache-2.0 line). Owns the internal `MassTransitErrors` factory. |
| `SharedKernel.Messaging.MassTransit.RabbitMq` | Adapter | ● | `UseRabbitMq(…)` transport satellite. Edge → MassTransit. Logs nothing; its sub-block is reserved. |
| `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Adapter | ● | `UseAzureServiceBus(…)` transport satellite. Edge → MassTransit. Logs in its own sub-block (`AzureServiceBusLoggingEventIds`). |
| `SharedKernel.Messaging.MassTransit.EfCore` | Adapter | ● | `WithEntityFrameworkOutbox<TDbContext>()` outbox satellite. Edge → MassTransit. Logs nothing; its sub-block is reserved. |
| `SharedKernel.Messaging.Testing` | Testing | ● | `InMemoryMessageBus`, `InMemoryEventPublisher`. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open.
