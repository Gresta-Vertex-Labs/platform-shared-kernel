---
name: masstransit-scheduler-api
description: MassTransit 9.x IMessageScheduler API patterns discovered during SK.07.Scheduling — cancel overloads, transport-aware wiring, obsolete APIs, name collision fix
metadata:
  type: project
---

# MassTransit 9.x Scheduler API (Discovered SK.07.Scheduling)

## SchedulePublish return type
`MassTransit.IMessageScheduler.SchedulePublish<T>(DateTime, T, CancellationToken)` returns
`Task<ScheduledMessage<T>>`. The schedule token is `.TokenId` (Guid) on the base `ScheduledMessage` type.
Always pass `.UtcDateTime` — MassTransit uses `DateTime` not `DateTimeOffset`.

## CancelScheduledPublish — non-generic overload
`CancelScheduledPublish(Type, Guid, CancellationToken)` — non-generic overload exists. This is the
correct cancel API when the message type is not known at call time (e.g., `CancelAsync(Guid, ct)`
in `MassTransitMessageScheduler`). Do NOT use `CancelScheduledSend` — that is a different API.

## ConcurrentDictionary token→type pattern
`MassTransitMessageScheduler` maintains `ConcurrentDictionary<Guid, Type>` to map schedule tokens
to message types. This is required because `CancelAsync(Guid, ct)` has no type parameter but MassTransit
cancel requires the type. Populated in `ScheduleAsync`, removed in `CancelAsync`. Per-scope only
(not persisted across restarts — consistent with in-memory scheduler semantics).

## Transport-aware in-memory scheduler wiring (WithInMemoryScheduler)
- **RabbitMQ:** `cfg.AddDelayedMessageScheduler()` + `busCfg.UseDelayedMessageScheduler()`
- **Azure Service Bus:** `cfg.AddServiceBusMessageScheduler()` + `busCfg.UseServiceBusMessageScheduler()`
- `UseDelayedMessageScheduler()` on ASB is OBSOLETE — always check `_transport` and pick the right method.

## Quartz durable scheduler wiring (WithQuartzScheduler)
- Registration: `cfg.AddQuartzConsumers(o => o.QueueName = queueName)` + `cfg.AddMessageScheduler(uri)`
- Bus factory: `busCfg.UseMessageScheduler(uri)` where `uri = new Uri($"queue:{queueName}")`
- `queueName` defaults to `QuartzSchedulerOptions.Schema` ("quartz")
- `UseInMemoryScheduler(ctx, queueName)` from `MassTransit.QuartzIntegration` is OBSOLETE — do not use

## IMessageScheduler name collision in MessagingBusBuilder.cs
Both `MassTransit.IMessageScheduler` and `SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler`
resolve in the same file. Two fixes applied:
1. `using MtScheduler = MassTransit.IMessageScheduler;` alias in `MassTransitMessageScheduler.cs`
2. Fully qualified `SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler` in `Services.AddScoped<>()`

## Scoped MassTransit.IMessageScheduler in tests
MassTransit registers `IMessageScheduler` as scoped. In tests, resolve from a child scope:
```csharp
using var scope = provider.CreateScope();
var mtScheduler = scope.ServiceProvider.GetRequiredService<global::MassTransit.IMessageScheduler>();
```
Resolving from root provider throws `InvalidOperationException: Cannot resolve scoped service from root provider`.

**Why:** Phase SK.07.Scheduling implemented 2026-06-08; 7 scheduling tests pass.
**How to apply:** Apply all patterns when implementing or testing MassTransit deferred scheduling features.
**Related:** [[masstransit-9x-testing-patterns]]
