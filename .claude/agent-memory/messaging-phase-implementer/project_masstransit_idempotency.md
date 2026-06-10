---
name: masstransit-idempotency-filter-wiring
description: MassTransit 9.x open-generic consume pipeline filter wiring for SK.07.Idempotency — UseConsumeFilter API, DI registration, test harness pattern
metadata:
  type: project
---

# MassTransit 9.x Global Consume Pipeline Filter (Idempotency — SK.07.Idempotency)

## Completed phase
SK.07.Idempotency (P-134) — all 9 tasks ●. Implemented 2026-06-09.

## Key deliverables
- `IIdempotencyStore` + `IdempotencyOptions` in `SharedKernel.Messaging.Abstractions/Idempotency/`
- `IdempotentConsumerBehavior<TMessage>` (internal sealed, `IFilter<ConsumeContext<TMessage>>`) in `SharedKernel.Messaging.MassTransit/Consumers/`
- `MessagingBusBuilder.WithIdempotency()` and `WithIdempotency(Action<IdempotencyOptions>)` in `MessagingBusBuilder.cs`

## Critical API: UseConsumeFilter with open generic type

`IBusFactoryConfigurator` implements `IConsumePipeConfigurator`. To apply a filter globally to ALL message types:

```csharp
busCfg.UseConsumeFilter(typeof(IdempotentConsumerBehavior<>), ctx);
```

- First arg: open generic type (`typeof(MyFilter<>)`, NOT closed)
- Second arg: `IBusRegistrationContext ctx` (the `ctx` lambda parameter from `UsingRabbitMq((ctx, busCfg) => ...)`)
- MassTransit resolves the closed generic per message type at runtime from DI

## DI registration (production path via MessagingBusBuilder)

```csharp
// Open-generic registration — MassTransit creates IdempotentConsumerBehavior<TMessage> per type
services.AddScoped(typeof(IdempotentConsumerBehavior<>));
```

## Build() guard

Uses `Services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore))` — no `BuildServiceProvider()`. Throws `InvalidOperationException` if `WithIdempotency()` called but no `IIdempotencyStore` registered.

## Test harness pattern (bypassing MessagingBusBuilder)

Tests must register the CLOSED generic (per message type used in the test):

```csharp
await using var provider = new ServiceCollection()
    .AddSingleton<IIdempotencyStore>(mockStore)
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

**Why:** MassTransit resolves from DI. Open-generic registration alone is not sufficient for `AddMassTransitTestHarness`; the closed generic must also be explicitly registered.

## Pipeline logic (IdempotentConsumerBehavior<TMessage>)

1. `MessageId` is null → pass through (no idempotency check)
2. `HasProcessedAsync(messageId)` → true: return without calling next (duplicate ack, no MarkProcessed)
3. `HasProcessedAsync(messageId)` → false: `await next.Send(context)`
4. After successful `next.Send`: `await MarkProcessedAsync(messageId)`
5. `next.Send` throws: propagate exception — MarkProcessed NOT called (message should be retried)

## IIdempotencyStore contract

SharedKernel provides ZERO implementations. The consuming service must:
1. Register `services.AddScoped<IIdempotencyStore, MyImplementation>()` BEFORE calling `WithIdempotency()`
2. `Build()` throws `InvalidOperationException` with diagnostic message if not registered

**Why:** [[masstransit-testing-patterns]] — platform cannot own persistence-backed implementations.
