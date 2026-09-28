# SharedKernel.Messaging.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **In-memory `IMessageBus` and `IEventPublisher` that record every publish and send, so a handler that talks to the
> bus can be unit-tested without a broker, MassTransit or containers.**

| You get | So that |
| --- | --- |
| `InMemoryMessageBus` for `IMessageBus` | Publishes and sends are recorded, split by verb, and asserted by type |
| `InMemoryEventPublisher` for `IEventPublisher` | Integration events a handler raises are asserted by type, once or never |
| The captured `PublishContext` per call | Tenant, correlation, partition key, subject and headers are assertable too |
| `IMessageHeaderPropagator` support, production precedence | Your propagators run first and explicit `configure` values win, as with the real bus |
| `AddInMemoryMessageBus()` / `AddInMemoryEventPublisher()` | One call swaps the real registration in a DI-based test host |
| Framework-free assertions (`InvalidOperationException`) | Works with xUnit, NUnit or MSTest alike |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Add it to a **test project** only. A production project that references it fails the architecture rule
`TestingNeverReferencedByProduction`.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Messaging.Abstractions`, `SharedKernel.Contracts`, `Microsoft.Extensions.DependencyInjection.Abstractions` (no MassTransit) |
| Namespaces | `SharedKernel.Testing.Messaging` |

## Quick start

```csharp
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Messaging;
using Xunit;

public sealed class ShipOrderHandlerTests
{
    [Fact]
    public async Task Shipping_an_order_publishes_OrderShipped()
    {
        var events = new InMemoryEventPublisher();
        var tenantId = new TenantId(Guid.NewGuid());
        var handler = new ShipOrderHandler(events);   // your handler, taking IEventPublisher

        var result = await handler.Handle(new ShipOrder(orderId: 42, tenantId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var shipped = events.ShouldHavePublishedOnce<OrderShipped>();
        Assert.Equal(42, shipped.OrderId);
        Assert.Equal(tenantId, events.ShouldHavePublishedContext<OrderShipped>().TenantId);
    }
}
```

With a DI container, register the double and resolve the concrete type to assert on the same instance the handler
received:

```csharp
services.AddInMemoryMessageBus();
services.AddInMemoryEventPublisher();

var events = provider.GetRequiredService<InMemoryEventPublisher>();
```

## How it works

- **Every call succeeds.** Each verb returns `Result.Success()`, as a reachable broker does. The doubles never fail,
  so broker-failure paths are tested with your own `IMessageBus` stub.
- **Recording.** Each call is appended to a thread-safe queue with a fresh `PublishContext`. The `ShouldHave*` helpers
  are read-only queries; nothing is cleared, so use one instance per test.
- **Context precedence.** For every verb (including `SendAsync`, which has no `configure` parameter) the double creates
  a new `PublishContext`, runs the constructor-supplied propagators in order, then the caller's `configure` callback.
  Explicit values win on a key both set — the same order as the production bus.
- **Reference capture.** `ShouldHave*Context<T>()` returns the actual `PublishContext` built for that call, not a copy.
- **Simplified:** `InMemoryEventPublisher` records the raw event and never builds an `EventEnvelope<TEvent>`, so a
  missing or invalid `[IntegrationEvent]` declaration is not caught here. Nothing is delivered to consumers, and there
  is no retry, outbox, scheduling or serialization.
- **Type matching.** `InMemoryMessageBus` matches on the generic type the call was made with (exact type);
  `InMemoryEventPublisher` matches any event assignable to `TEvent`.
- **Lifetime.** The `Add*` extensions register **singletons** (production registers these services scoped), so the
  recorder outlives the scope the system under test ran in.

## Recipes

### 1. Assert a command was sent, not published

```csharp
var bus = new InMemoryMessageBus();
await new ReserveStockHandler(bus).Handle(command, CancellationToken.None);

var reserve = bus.ShouldHaveSent<ReserveStock>();
bus.ShouldNotHavePublished<ReserveStock>();
```

### 2. Assert the headers a propagator wrote

```csharp
var bus = new InMemoryMessageBus([new MyTenantHeaderPropagator(tenantId)]);
await bus.PublishAsync(new PriceChanged(sku), CancellationToken.None);

var context = bus.ShouldHavePublishedContext<PriceChanged>();
Assert.Equal(tenantId, context.TenantId);
```

`PublishContext` exposes `CorrelationId`, `CausationId`, `TenantId`, `PartitionKey`, `Subject` and `Headers`.

### 3. Assert an event was not raised on a failure path

```csharp
var result = await handler.Handle(invalidCommand, CancellationToken.None);

Assert.True(result.IsFailure);
events.ShouldNotHavePublished<OrderShipped>();
```

### 4. Inspect everything that was published

```csharp
Assert.Equal(2, events.Published.Count);            // every event, in publish order
var lines = events.PublishedOf<OrderLineAdded>();   // only one type
```

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddInMemoryMessageBus(this IServiceCollection)` | `InMemoryMessageBus` and `IMessageBus` → the same singleton |
| `AddInMemoryEventPublisher(this IServiceCollection)` | `InMemoryEventPublisher` and `IEventPublisher` → the same singleton |

Registered `IMessageHeaderPropagator` services are injected into the constructor. Register them as singleton or
transient: a scoped propagator captured by the singleton double fails DI scope validation.

### Types

| Type | Implements | Constructor |
| --- | --- | --- |
| `InMemoryMessageBus` | `IMessageBus` | `(IEnumerable<IMessageHeaderPropagator>? propagators = null)` |
| `InMemoryEventPublisher` | `IEventPublisher` | `(IEnumerable<IMessageHeaderPropagator>? propagators = null)` |

### Inspection — `InMemoryMessageBus`

| Member | Returns / throws |
| --- | --- |
| `ShouldHavePublished<T>()` | The first published `T`; throws if none |
| `ShouldHavePublishedOnce<T>()` | The only published `T`; throws on zero or more than one |
| `ShouldNotHavePublished<T>()` | Throws if any `T` was published |
| `ShouldHaveSent<T>()` | The first sent `T`; throws if none |
| `ShouldHavePublishedContext<T>()` / `ShouldHaveSentContext<T>()` | The `PublishContext` of the first matching call |

### Inspection — `InMemoryEventPublisher`

| Member | Returns / throws |
| --- | --- |
| `Published` | Every event, in publish order (`IReadOnlyList<object>`) |
| `PublishedOf<TEvent>()` | Every event assignable to `TEvent` |
| `ShouldHavePublished<TEvent>()` | The first matching event; throws if none |
| `ShouldHavePublishedOnce<TEvent>()` | The only matching event; throws on zero or more than one |
| `ShouldNotHavePublished<TEvent>()` | Throws if any matching event was published |
| `ShouldHavePublishedContext<TEvent>()` | The `PublishContext` of the first matching event |

Failed assertions throw `InvalidOperationException`; event messages include the wire name and version when the type
declares a valid `[IntegrationEvent]`.

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Messaging.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Messaging.Testing/SharedKernel.Messaging.Testing.Tests),
which prove recording, propagator order and context precedence against the `IMessageBus`/`IEventPublisher` contract.
Pair it with [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md)
(`TestRequestContext`, `FakeClock`, `EventEnvelopeBuilder<TEvent>` for envelope-shaped assertions) and, for consumer
idempotency, [`SharedKernel.Idempotency.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Idempotency.Testing/README.md).
Consumer and broker-fidelity tests use MassTransit's own test harness (`AddMassTransitTestHarness`).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Assume a passing test proves the event is publishable | Keep a real `sealed` `IIntegrationEvent` with `[IntegrationEvent("name", Version = n)]` and test the envelope separately | The double never calls `EventEnvelope.Wrap`, which is where production validates the declaration |
| Register a scoped `IMessageHeaderPropagator` alongside `AddInMemory*()` | Register propagator doubles as singleton or transient | The double is a singleton; a scoped dependency is a captive dependency |
| Publish through a base type and assert on the derived type with `InMemoryMessageBus` | Assert on the type the call was made with | The bus matches the generic argument exactly |
| Reuse one instance across tests | Create or register a fresh instance per test | Recordings are never cleared |
| Test consumer retries or dead-lettering with this double | Use MassTransit's test harness or the real transport | Nothing is delivered to consumers |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
