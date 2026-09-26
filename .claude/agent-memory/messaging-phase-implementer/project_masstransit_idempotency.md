---
name: masstransit-idempotency-filter-wiring
description: MassTransit open-generic consume pipeline filter wiring for SK.07.Idempotency — UseConsumeFilter API, DI registration, keyed IIdempotencyStore (IdempotencyPurpose.Message), test harness pattern
metadata:
  type: project
---

# MassTransit Global Consume Pipeline Filter (Idempotency — SK.07.Idempotency)

> WO-086 (2026-09): the messaging-local `IIdempotencyStore` (`HasProcessedAsync`/`MarkProcessedAsync`, later `TryBeginAsync`) is gone — the filter now uses `SharedKernel.Idempotency.Abstractions.IIdempotencyStore`, keyed `IdempotencyPurpose.Message`, implemented by `18.Idempotency` (`AddRedisIdempotency(p => p.ForMessages())` / `AddEfCoreIdempotency(...)`). Recorded on MassTransit 9.1.2; the platform is pinned to 8.5.x.

## Completed phase
SK.07.Idempotency (P-134) — all 9 tasks ●. Implemented 2026-06-09; contract replaced by P-560 and WO-086.

## Key deliverables (current)
- `IdempotencyOptions` (`LeaseDuration`, `ExpiryWindow`) in `SharedKernel.Messaging.Abstractions/Idempotency/`
- `IdempotentConsumerBehavior<TMessage>` (internal sealed, `IFilter<ConsumeContext<TMessage>>`) in `SharedKernel.Messaging.MassTransit/Consumers/`, taking `[FromKeyedServices(IdempotencyPurpose.Message)] IIdempotencyStore`
- `MessagingBusBuilder.WithIdempotency()` and `WithIdempotency(Action<IdempotencyOptions>)` in `MessagingBusBuilder.cs`

## Critical API: UseConsumeFilter with open generic type

`IBusFactoryConfigurator` implements `IConsumePipeConfigurator`. To apply a filter globally to ALL message types:

```csharp
busCfg.UseConsumeFilter(typeof(IdempotentConsumerBehavior<>), ctx);
```

- First arg: open generic type (`typeof(MyFilter<>)`, NOT closed)
- Second arg: `IBusRegistrationContext ctx` (the `ctx` lambda parameter from the transport's `Using...((ctx, busCfg) => ...)`)
- MassTransit resolves the closed generic per message type at runtime from DI
- `InboundRequestContextFilter<>` is installed the same way and runs first, so the key is tenant-scoped by the ambient request context

## DI registration (production path via MessagingBusBuilder)

```csharp
// Open-generic registration — MassTransit creates IdempotentConsumerBehavior<TMessage> per type
services.AddScoped(typeof(IdempotentConsumerBehavior<>));
```

## Build() guard

`Services.HasIdempotencyStore(IdempotencyPurpose.Message)` — no `BuildServiceProvider()`. Throws `InvalidOperationException` if `WithIdempotency()` is called but no store is registered for that purpose (register one with `AddIdempotencyStore<TStore>(IdempotencyPurpose.Message)` or an `18.Idempotency` provider).

## Test harness pattern (bypassing MessagingBusBuilder)

Tests must register the CLOSED generic (per message type used in the test), and the store keyed by purpose:

```csharp
await using var provider = new ServiceCollection()
    .AddKeyedSingleton<IIdempotencyStore>(IdempotencyPurpose.Message, fakeStore)
    .AddMassTransitTestHarness(cfg =>
    {
        cfg.AddConsumer<MyConsumer>();
        cfg.UsingInMemory((ctx, busCfg) =>
        {
            busCfg.UseConsumeFilter(typeof(IdempotentConsumerBehavior<>), ctx);
            busCfg.ConfigureEndpoints(ctx);
        });
    })
    .AddScoped<IdempotentConsumerBehavior<MyMessage>>()  // CLOSED generic — one per message type
    .BuildServiceProvider(true);
```

`SharedKernel.Idempotency.Testing`'s `FakeIdempotencyStore` / `AddFakeIdempotencyStore(purposes)` is the ready-made fake.

**Why:** MassTransit resolves from DI. Open-generic registration alone is not sufficient for `AddMassTransitTestHarness`; the closed generic must also be explicitly registered.

## Pipeline logic (IdempotentConsumerBehavior<TMessage>)

1. `MessageId` is null → pass through (no idempotency check)
2. `TryBeginAsync(IdempotencyPurpose.Message, messageId "D", fixed fingerprint, LeaseDuration)`:
   - `Completed` → return without calling next (duplicate acknowledged)
   - `InProgress` → throw `ConcurrentMessageDeliveryException` (message stays unacknowledged, so it is still processed if the running attempt fails)
   - `Started` → `await next.Send(context)`, then `CompleteAsync` with `ExpiryWindow`
3. `next.Send` throws → `ReleaseAsync` at once, then propagate (the redelivery is not discarded)
