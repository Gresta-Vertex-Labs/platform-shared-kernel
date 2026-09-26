<div align="center">

# SharedKernel Integration

**Outbound delivery to destinations outside your control — signed, retried, SSRF-guarded webhooks and
customer-facing email and SMS — with one rule: nothing about the caller except a correlation id ever leaves the
platform, and a retry never delivers twice.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 4](https://img.shields.io/badge/packages-4-informational)

[Packages](#the-packages) · [Webhooks](#webhooks) · [Notifications](#notifications) · [Guarantees](#what-you-can-rely-on) · [Testing](#testing)

</div>

---

## The packages

| Package | Tier | What it is |
| --- | --- | --- |
| [`SharedKernel.Integration.Webhooks`](SharedKernel.Integration.Webhooks/README.md) | Adapter | Webhook subscriptions, HMAC-SHA256 signing and replay-resistant verification, retrying fan-out dispatch, SSRF guard, multi-secret rotation, opt-in payload encryption, a synthetic ping delivery. See also the [configuration reference](SharedKernel.Integration.Webhooks/docs/configuration-reference.md). |
| [`SharedKernel.Integration.Notifications.Abstractions`](SharedKernel.Integration.Notifications.Abstractions/README.md) | Abstractions | The provider-neutral `INotificationSender` contract, `NotificationMessage<TTemplateModel>`, the sender-identity and delivery-observer seams. |
| [`SharedKernel.Integration.Notifications.Email.SendGrid`](SharedKernel.Integration.Notifications.Email.SendGrid/README.md) | Adapter | Email through SendGrid's REST API (no vendor SDK); attachments are `08.Storage` file references, streamed, never inlined. |
| [`SharedKernel.Integration.Notifications.Sms.Twilio`](SharedKernel.Integration.Notifications.Sms.Twilio/README.md) | Adapter | SMS through Twilio's REST API (no vendor SDK), with `Idempotency-Key` deduplication. |

```xml
<PackageReference Include="SharedKernel.Integration.Webhooks" />
<PackageReference Include="SharedKernel.Integration.Notifications.Email.SendGrid" />
```

Versions come from the consumer's single `SharedKernelVersion`. None of the four references ASP.NET Core,
persistence or `SharedKernel.Communication.*`: the service supplies its own subscription store and sender
identity.

---

## Webhooks

```csharp
builder.Services.AddSharedKernelWebhooks(options => options.MaxAttempts = 8);
builder.Services.AddScoped<IWebhookSubscriptionStore, EfWebhookSubscriptionStore>();   // your store
```

```csharp
public sealed class OrderShippedWebhooks(IWebhookDispatcher webhooks)
{
    public Task<IReadOnlyList<WebhookDeliveryResult>> PublishAsync(OrderShipped evt, CancellationToken ct) =>
        webhooks.DispatchAsync(evt, ct);   // one signed delivery per matching subscription, never throws
}
```

The event's routing key is its `[IntegrationEvent]` name (`04.Contracts`), so an event routes identically over the
bus and as a webhook. Every delivery carries a signature, a timestamp, an `X-Webhook-Delivery-Id` for
subscriber-side deduplication and the operation's `X-Correlation-Id`. A target that resolves to a loopback, private,
link-local or multicast address is rejected before any HTTP attempt. When every attempt fails, a
delivery-exhausted integration event is published through `IEventPublisher`.

## Notifications

```csharp
builder.Services.AddSharedKernelNotifications();
builder.Services.AddScoped<INotificationSenderIdentityResolver, TenantSenderIdentityResolver>();   // yours
builder.Services.AddSendGridEmailNotifications(o => o.ApiKey = builder.Configuration["SendGrid:ApiKey"]!);
builder.Services.AddTwilioSmsNotifications(o =>
{
    o.AccountSid = builder.Configuration["Twilio:AccountSid"]!;
    o.AuthToken = builder.Configuration["Twilio:AuthToken"]!;
    o.MessagingServiceSid = builder.Configuration["Twilio:MessagingServiceSid"];   // or From
});
```

Each provider registers a keyed `INotificationSender`; resolve it with
`GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email)`. Every message carries a caller-supplied
`NotificationDeliveryId` that you reuse on a retry: Twilio receives it as `Idempotency-Key` (a real dedup
guarantee), SendGrid as `custom_args` (correlation only). A sender-identity resolver that needs the caller's tenant
reads `IRequestContext.TenantId` (`SharedKernel.Execution`).

---

## What you can rely on

- **Nothing about the caller leaves the platform** except the correlation id on a webhook. Tenant id, actor and
  client id stay inside the trust boundary; notifications send only the message.
- **Retries are safe.** Webhook subscribers deduplicate on `X-Webhook-Delivery-Id`; notification retries reuse the
  caller's `NotificationDeliveryId`.
- **Configuration is real.** `WebhookDeliveryOptions` (`MaxAttempts`, backoff, `RequestTimeout`,
  `MaxConcurrentDeliveries`) drive the resilience handler and are validated at startup.
- **SSRF is closed by default.** `AllowPrivateNetworkTargets` or a custom `IWebhookUrlValidator` is the only opt-out.
- **Secrets rotate without downtime.** `WebhookSubscription.Secrets` is newest-first: sign with the first, verify
  against any.
- **Personal data is never logged.** Recipients and template-model values are never log parameters. Delivery
  outcomes are logged with fixed `EventId`s (15000–15999) and traced on the `SharedKernel.Integration` source,
  wired by `WithIntegrationTelemetry()` in `SharedKernel.ServiceDefaults`.

---

## Testing

[`SharedKernel.Integration.Testing`](../16.Testing/SharedKernel.Integration.Testing/README.md) has in-memory doubles
that record instead of sending: `AddInMemoryWebhookDispatcher()`, `AddInMemoryWebhookDeliveryObserver()`,
`AddInMemoryNotificationSender(channel)` and `AddInMemoryNotificationDeliveryObserver()`.

---

## Further reading

- [`CLAUDE.md`](CLAUDE.md) — interface contracts and implementation rules.
- [`state-map.md`](state-map.md) — phase history (WO-032, WO-064, WO-072, WO-081, WO-086).
