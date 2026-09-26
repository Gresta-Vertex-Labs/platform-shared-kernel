# SharedKernel.Integration.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**In-memory doubles for outbound webhooks and customer notifications (email, SMS), so the code that sends them is
tested without HTTP, SendGrid or Twilio.** Every dispatch and send is recorded; the result each call returns can be
scripted to exercise failure paths.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Integration.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespaces: `SharedKernel.Testing.Integration` (webhooks) and
`SharedKernel.Testing.Notifications`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `InMemoryWebhookDispatcher` | `IWebhookDispatcher` | Records `Dispatched`, `DispatchedTo`, `TestDeliveries`; `SetDispatchResult(subscriptionId, result)`, `SetDispatchResult<TEvent>(evt => results)`, `SetTestDeliveryResult`; assertions `ShouldHaveDispatched<TEvent>()` (returns the event), `ShouldNotHaveDispatched<TEvent>()`, `ShouldHaveDispatchedTo(subscriptionId)`, `ShouldHaveSentTestDelivery(subscriptionId)` |
| `InMemoryWebhookDeliveryObserver` | `IWebhookDeliveryObserver` | `Attempts`, `Completions`; `ShouldHaveObservedAttempt(subscriptionId)`, `ShouldHaveObservedCompletion(subscriptionId)` |
| `InMemoryNotificationSender` | `INotificationSender` for one `NotificationChannel` | `Sent`, `SentOf<TModel>()`, `SetSendResult<TModel>(message => result)`; `ShouldHaveSent<TModel>(filter?)`, `ShouldNotHaveSent<TModel>()` |
| `InMemoryNotificationDeliveryObserver` | `INotificationDeliveryObserver` | `Attempts`, `Completions`; `ShouldHaveSucceeded(deliveryId)`, `ShouldHaveFailed(deliveryId)` |

Test events passed to the webhook dispatcher must be real integration events: a `sealed` `IIntegrationEvent` with
`[IntegrationEvent("name", Version = n)]`, as in production.

## Registration

```csharp
services.AddInMemoryWebhookDispatcher();
services.AddInMemoryWebhookDeliveryObserver();
services.AddInMemoryNotificationSender(NotificationChannel.Email);   // keyed by channel, like the real senders
services.AddInMemoryNotificationDeliveryObserver();
```

All singletons — a deliberate difference from the scoped production senders, so the recorder outlives the scope of
the code under test. Resolve the sender as production code does:
`provider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email)`, or the concrete type with the
same key.

## Example

```csharp
var webhooks = new InMemoryWebhookDispatcher();
var handler = new OrderShippedHandler(webhooks);

await handler.Handle(new OrderShipped(orderId), ct);

webhooks.ShouldHaveDispatched<OrderShippedIntegrationEvent>().OrderId.Should().Be(orderId);
```

```csharp
var email = new InMemoryNotificationSender(NotificationChannel.Email);
await new ReceiptMailer(email).SendAsync(order, ct);

email.ShouldHaveSent<ReceiptModel>(m => m.Recipient == order.CustomerEmail);
```

## Related packages

- References `SharedKernel.Integration.Webhooks`, `SharedKernel.Integration.Notifications.Abstractions` and
  `SharedKernel.Contracts`.
- [`SharedKernel.Storage.Testing`](../SharedKernel.Storage.Testing/README.md) — an in-memory store for notification
  attachments.
