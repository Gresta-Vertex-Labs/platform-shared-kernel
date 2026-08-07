---
name: masstransit-retry-observability-gap
description: MassTransit 9.1.2's IRetryObserver/IRetryObserverConnector are unreachable from any configurator — ConsumeContext.GetRetryAttempt() is the verified working alternative (P-348/WO-054)
metadata:
  type: project
---

## The gap (confirmed via reflection over the shipped assembly, not assumed from docs)

`MassTransit.dll` 9.1.2 (physically named `MassTransit.dll`, but its runtime assembly
*identity* is `MassTransit.Abstractions` — both `MassTransit.IBus` and
`MassTransit.IRetryObserverConnector` report that same assembly identity when reflected)
declares `IRetryObserver` (`PostCreate`/`PostFault`/`PreRetry`/`RetryFault`/`RetryComplete`,
each taking `RetryContext<T>`/`RetryPolicyContext<T>`) and `IRetryObserverConnector`
(`ConnectRetryObserver(IRetryObserver) → ConnectHandle`) in its public API.

**Nothing in the assembly implements `IRetryObserverConnector`.** Brute-force reflection
(`GetInterfaces().Contains(typeof(IRetryObserverConnector))` over every type, public and
non-public, in the assembly) returns zero hits. `IBus`, `IBusControl`,
`IBusFactoryConfigurator`, `IReceiveEndpointConfigurator`, `IReceiveEndpoint` — none of them
carry it. `ConnectRetryObserver` is a dead end from `MessagingBusBuilder`'s configuration-time
API surface. This is undocumented — the shipped `MassTransit.xml` has zero `<member>` entries
for `IRetryObserver`, `IRetryObserverConnector`, or `RetryContextExtensions`.

## The verified working alternative

`MassTransit.RetryContextExtensions` (static class) exposes three extension methods on
`ConsumeContext`:
- `GetRetryAttempt(ConsumeContext)` → `int`
- `GetRetryCount(ConsumeContext)` → `int`
- `GetRedeliveryCount(ConsumeContext)` → `int`

Confirmed empirically via a live `dotnet test`-based `AddMassTransitTestHarness` probe (see
[[project-masstransit-ordered-delivery]] for why `dotnet run` cannot be used — real-bus license
gate) with `busCfg.UseMessageRetry(r => r.Incremental(2, TimeSpan.FromMilliseconds(10), ...))`
and a consumer that fails twice then succeeds:

| Consume() invocation | GetRetryAttempt() | GetRetryCount() |
|---|---|---|
| 1st (original delivery) | 0 | 0 |
| 2nd (1st retry) | 1 | 0 |
| 3rd (2nd retry, succeeds) | 2 | 1 |

`GetRetryAttempt()` is the clean "is this invocation itself a retry redelivery" signal — `> 0`
means yes. Also confirmed: calling it with **no** `UseMessageRetry(...)` configured at all
returns `0` safely, never throws — safe to call unconditionally in `ConsumerBase<TMessage>.Consume()`
regardless of whether the consuming service opted into retry.

## Where this was used

`07.Messaging/SharedKernel.Messaging.MassTransit/Consumers/ConsumerBase.cs` — at the top of
`Consume()`, before dispatching to `ConsumeAsync`: `if (context.GetRetryAttempt() > 0)
MessagingDiagnostics.RetryCounter.Add(1, tag);`. This is a **per-invocation context inspection**,
not a subscribed observer callback — documented as such in `MessagingDiagnostics.RetryCounter`'s
XML doc and a "RETRY OBSERVATION GAP" note in `07.Messaging/CLAUDE.md`'s Interface Contracts
section, so a future reader doesn't assume `IRetryObserver` is actually wired up somewhere.

**Why:** P-348/WO-054 (`SK.07.DiagnosticsCoverage`, 2026-08-06) — completing `messaging.retry.count`
Meter coverage per the phase's own "document any gap where MassTransit 9.x does not expose a
clean per-retry-attempt hook" acceptance criterion.
