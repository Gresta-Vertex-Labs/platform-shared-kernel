# 07.Messaging — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | Abstractions | ● | `IMessageBus`/`IEventPublisher` (every verb returns `Result`), `IMessageScheduler`, `ISendEndpointResolver`, `IMessageHeaderPropagator`, `IFaultConsumer`, `IMessageVersionTranslator`, `MessagingOptions`. |
| `SharedKernel.Messaging.MassTransit` | Adapter | ● | `AddSharedKernelMessaging(configuration).Use{Transport}(…)…Build()` — retry, circuit breaker, dead-letter policy, delayed delivery, consumer idempotency over `IIdempotencyStore`, ordered delivery, payload transform, telemetry, `messaging` probe, `WithInboundRequestContext()`. Pinned to MassTransit 8.5.x (last Apache-2.0 line). |
| `SharedKernel.Messaging.MassTransit.RabbitMq` | Adapter | ● | `UseRabbitMq(…)` transport satellite. Edge → MassTransit. |
| `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Adapter | ● | `UseAzureServiceBus(…)` transport satellite. Edge → MassTransit. |
| `SharedKernel.Messaging.MassTransit.EfCore` | Adapter | ● | `WithEntityFrameworkOutbox<TDbContext>()` outbox satellite. Edge → MassTransit. |

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.
