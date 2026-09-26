# SharedKernel.Messaging.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**`InMemoryMessageBus` and `InMemoryEventPublisher` record every publish and send made through
`SharedKernel.Messaging.Abstractions`, so a handler that publishes can be tested without a broker or MassTransit.**
Both return `Result.Success()` like a reachable broker, capture the `PublishContext` each call configured, and run
any registered `IMessageHeaderPropagator` against it, so propagated headers can be asserted too.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Messaging.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Messaging`.

## Contents

| Type | Stands in for | Assertions |
| --- | --- | --- |
| `InMemoryMessageBus` | `IMessageBus` | `ShouldHavePublished<T>()`, `ShouldHavePublishedOnce<T>()`, `ShouldNotHavePublished<T>()`, `ShouldHaveSent<T>()` (each returns the message), `ShouldHavePublishedContext<T>()`, `ShouldHaveSentContext<T>()` (return the captured `PublishContext`) |
| `InMemoryEventPublisher` | `IEventPublisher` | `Published`, `PublishedOf<TEvent>()`, `ShouldHavePublished<TEvent>()`, `ShouldHavePublishedOnce<TEvent>()`, `ShouldNotHavePublished<TEvent>()`, `ShouldHavePublishedContext<TEvent>()` |

`InMemoryEventPublisher` accepts only real integration events — a `sealed` `IIntegrationEvent` with
`[IntegrationEvent("name", Version = n)]` — as the production publisher does.

## Registration

```csharp
services.AddInMemoryMessageBus();
services.AddInMemoryEventPublisher();
```

Both are singletons, registered as the concrete type and as the interface, so the test resolves the same instance
the handler used. Register `IMessageHeaderPropagator` doubles (singleton or transient) before resolving if you want
them applied.

## Example

```csharp
var events = new InMemoryEventPublisher();
var handler = new ShipOrderHandler(repository, events);

await handler.Handle(new ShipOrder(orderId), ct);

events.ShouldHavePublishedOnce<OrderShipped>().OrderId.Should().Be(orderId.Value);
events.ShouldHavePublishedContext<OrderShipped>().TenantId.Should().Be(tenantId);
```

## Related packages

- References `SharedKernel.Messaging.Abstractions` and `SharedKernel.Contracts`; no MassTransit.
- For consumer tests against MassTransit's own in-memory harness this repository uses `TestHarnessFactory` in the
  non-packable `SharedKernel.Testing.Internal`; a consuming service uses MassTransit's `AddMassTransitTestHarness`
  directly.
- [`SharedKernel.Idempotency.Testing`](../SharedKernel.Idempotency.Testing/README.md) — the consumer idempotency
  store (`IdempotencyPurpose.Message`).
