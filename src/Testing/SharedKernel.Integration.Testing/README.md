# SharedKernel.Integration.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **In-memory doubles for outbound webhooks and customer notifications (email, SMS), so the code that sends them is
> tested without HTTP, SendGrid or Twilio.** Every dispatch and send is recorded, and the result each call returns
> can be scripted to drive failure paths.

| You get | So that |
| --- | --- |
| `InMemoryWebhookDispatcher` (`IWebhookDispatcher`) | You assert which integration events your code pushed to subscribers — no subscription store, signing or HTTP |
| `InMemoryNotificationSender` (`INotificationSender`, one per channel) | You assert which emails and SMS were sent, with which template model |
| `SetDispatchResult` / `SetTestDeliveryResult` / `SetSendResult` | Delivery failures are scripted as returned results, exactly as production reports them |
| `InMemoryWebhookDeliveryObserver`, `InMemoryNotificationDeliveryObserver` | You record the attempt/completion callbacks a real dispatcher or sender makes |
| `AddInMemory…()` singletons, keyed senders | The recorder outlives the SUT's scope and resolves exactly as production code resolves it |

## Install

```xml
<PackageReference Include="SharedKernel.Integration.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only** — the `TestingNeverReferencedByProduction` architecture rule fails any
production project that references a testing package.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Integration.Webhooks`, `SharedKernel.Integration.Notifications.Abstractions`, `SharedKernel.Contracts`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Namespaces | `SharedKernel.Testing.Integration` (webhooks), `SharedKernel.Testing.Notifications` |

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Events;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Testing.Integration;
using SharedKernel.Testing.Notifications;
using Xunit;

[IntegrationEvent("orders.shipped", Version = 1)]
public sealed record OrderShipped(Guid OrderId) : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ShippedModel(Guid OrderId);

// The code under test: pushes a webhook and emails the customer.
public sealed class ShipmentNotifier(
    IWebhookDispatcher webhooks,
    [FromKeyedServices(NotificationChannel.Email)] INotificationSender email)
{
    public async Task NotifyAsync(Guid orderId, string customerEmail, CancellationToken ct)
    {
        await webhooks.DispatchAsync(new OrderShipped(orderId), ct);
        await email.SendAsync(new NotificationMessage<ShippedModel>
        {
            NotificationDeliveryId = Guid.NewGuid(),
            Channel = NotificationChannel.Email,
            Recipient = customerEmail,
            TemplateId = "order-shipped",
            TemplateModel = new ShippedModel(orderId),
        }, ct);
    }
}

public sealed class ShipmentNotifierTests
{
    [Fact]
    public async Task Shipping_pushes_a_webhook_and_emails_the_customer()
    {
        var services = new ServiceCollection()
            .AddInMemoryWebhookDispatcher()
            .AddInMemoryNotificationSender(NotificationChannel.Email)
            .AddScoped<ShipmentNotifier>();
        using var provider = services.BuildServiceProvider();
        var orderId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ShipmentNotifier>()
                .NotifyAsync(orderId, "ada@example.com", CancellationToken.None);
        }

        var webhooks = provider.GetRequiredService<InMemoryWebhookDispatcher>();
        Assert.Equal(orderId, webhooks.ShouldHaveDispatched<OrderShipped>().OrderId);

        var email = provider.GetRequiredKeyedService<InMemoryNotificationSender>(NotificationChannel.Email);
        var sent = email.ShouldHaveSent<ShippedModel>(m => m.Recipient == "ada@example.com");
        Assert.Equal("order-shipped", sent.TemplateId);
    }
}
```

## How it works

- **Recorders, not simulators.** The dispatcher performs no subscription lookup, signing, SSRF check, retry or HTTP
  delivery; the sender performs no template rendering, attachment download or provider call. Each call is recorded
  and a result is returned.
- **Default results.** `DispatchAsync` returns an empty list (as if no subscription matched) unless
  `SetDispatchResult<TEvent>` is configured. `DispatchToSubscriptionAsync` and `SendTestDeliveryAsync` return a
  success (`StatusCode` 200, `Attempts` 1, a new `DeliveryId`) unless configured for that subscription id.
  `SendAsync` returns a success echoing the message's `NotificationDeliveryId`. Failures are always results, never
  exceptions — the production contract.
- **Independent members.** `SendTestDeliveryAsync` is recorded in `TestDeliveries` only; unlike production it does
  not route through `DispatchToSubscriptionAsync`.
- **No observer calls.** The in-memory dispatcher and sender never invoke `IWebhookDeliveryObserver` or
  `INotificationDeliveryObserver`. The observer doubles are for testing a real `WebhookDispatcher` or provider
  sender, or your own code that drives an observer.
- **No event validation.** The dispatcher does not read `[IntegrationEvent]`; production does. Declare test events
  as real integration events anyway, so the same types work against the real dispatcher.
- **Lifetimes.** Every `AddInMemory…()` registers a **singleton**, although production dispatchers, senders and
  observers are scoped, so you can assert after the SUT's scope ends. The notification sender is registered keyed by
  channel, both as `INotificationSender` and as `InMemoryNotificationSender`.
- **Thread-safe.** Recording uses concurrent collections; the `Should…` helpers only read.

## Reference

### Registration

| Method | Registers (all singletons) |
| --- | --- |
| `AddInMemoryWebhookDispatcher(this IServiceCollection)` | `InMemoryWebhookDispatcher`, and `IWebhookDispatcher` resolving to it |
| `AddInMemoryWebhookDeliveryObserver(this IServiceCollection)` | `InMemoryWebhookDeliveryObserver`, and `IWebhookDeliveryObserver` resolving to it |
| `AddInMemoryNotificationSender(this IServiceCollection, NotificationChannel channel)` | One `InMemoryNotificationSender` keyed by `channel`, as itself and as `INotificationSender` |
| `AddInMemoryNotificationDeliveryObserver(this IServiceCollection)` | `InMemoryNotificationDeliveryObserver`, and `INotificationDeliveryObserver` resolving to it |

The methods add registrations; call them after any production registration so single-service resolution picks the
double.

### `InMemoryWebhookDispatcher : IWebhookDispatcher`

| Member | Purpose |
| --- | --- |
| `Dispatched` → `IReadOnlyList<object>` | Events passed to `DispatchAsync`, in order |
| `DispatchedTo` → `IReadOnlyList<(Guid SubscriptionId, object Event)>` | Calls to `DispatchToSubscriptionAsync` |
| `TestDeliveries` → `IReadOnlyList<WebhookSubscription>` | Calls to `SendTestDeliveryAsync` |
| `SetDispatchResult<TEvent>(Func<TEvent, IReadOnlyList<WebhookDeliveryResult>>)` | Result list for `DispatchAsync` of that event type |
| `SetDispatchResult(Guid subscriptionId, WebhookDeliveryResult)` | Result for `DispatchToSubscriptionAsync` to that subscription |
| `SetTestDeliveryResult(Guid subscriptionId, WebhookDeliveryResult)` | Result for `SendTestDeliveryAsync` to that subscription |
| `ShouldHaveDispatched<TEvent>()` → `TEvent` | First dispatched event of that type |
| `ShouldNotHaveDispatched<TEvent>()` | None of that type was dispatched |
| `ShouldHaveDispatchedTo(Guid subscriptionId)`, `ShouldHaveSentTestDelivery(Guid subscriptionId)` | At least one call for that subscription |

### `InMemoryNotificationSender : INotificationSender`

| Member | Purpose |
| --- | --- |
| `InMemoryNotificationSender(NotificationChannel supportedChannel)` | One instance per channel; `SupportedChannel` returns it |
| `Sent` → `IReadOnlyList<object>` | Every `NotificationMessage<T>`, in order |
| `SentOf<TTemplateModel>()` | The messages with that template model type |
| `SetSendResult<TTemplateModel>(Func<NotificationMessage<TTemplateModel>, NotificationDeliveryResult>)` | Result for sends of that model type |
| `ShouldHaveSent<TTemplateModel>(Predicate<NotificationMessage<TTemplateModel>>? filter = null)` → the message | First matching message |
| `ShouldNotHaveSent<TTemplateModel>()` | No message of that model type was sent |

### Observers

| Type | Records | Assertions |
| --- | --- | --- |
| `InMemoryWebhookDeliveryObserver` | `Attempts` `(WebhookSubscription, int AttemptNumber)`, `Completions` `(WebhookSubscription, WebhookDeliveryResult)` | `ShouldHaveObservedAttempt(Guid subscriptionId)`, `ShouldHaveObservedCompletion(Guid subscriptionId)` → the result |
| `InMemoryNotificationDeliveryObserver` | `Attempts` `(NotificationDeliveryContext, int AttemptNumber)`, `Completions` `(NotificationDeliveryContext, NotificationDeliveryResult)` | `ShouldHaveSucceeded(Guid notificationDeliveryId)`, `ShouldHaveFailed(Guid notificationDeliveryId)` → the result |

Assertions throw `InvalidOperationException` with a readable message; the observers never throw from their
callbacks, matching production's rule that an observer never faults a delivery.

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Integration.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Integration.Testing/SharedKernel.Integration.Testing.Tests),
proving each double against the `IWebhookDispatcher`, `INotificationSender` and observer contracts. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
(`FakeClock`, `TestRequestContext`, `IntegrationEventFaker<TEvent>`) and, for notification attachments,
[`SharedKernel.Storage.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Storage.Testing/README.md).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference it from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Expect `DispatchAsync` to report deliveries by default | Configure `SetDispatchResult<TEvent>` when the code reads the results | Unconfigured, it returns an empty list — no subscription matched |
| Register the observer double next to the in-memory sender and expect callbacks | Use the observer doubles with a real dispatcher or sender | The in-memory dispatcher and sender never call observers |
| Resolve `INotificationSender` without a key | Resolve by `NotificationChannel`, as production does | Senders are keyed by channel |
| Rely on the fake to reject an event without `[IntegrationEvent]` | Declare a valid `[IntegrationEvent("name", Version = n)]` | Only the real dispatcher reads the descriptor |
| Assert signing, SSRF, retries or attachment resolution here | Test those against the real `15.Integration` packages | The doubles only record and return results |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
