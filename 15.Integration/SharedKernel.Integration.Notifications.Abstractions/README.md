# SharedKernel.Integration.Notifications.Abstractions

Provider-neutral contracts for human-facing notification delivery (email, SMS) across the
Platform.SharedKernel ecosystem: send a customer a receipt, a one-time passcode, or a transaction
alert, without this package deciding which templating engine, vendor, or channel your service uses.

This package ships **zero I/O and zero concrete `INotificationSender`** — it is implemented by
`SharedKernel.Integration.Notifications.Email.SendGrid` and
`SharedKernel.Integration.Notifications.Sms.Twilio`. It mirrors
`SharedKernel.Integration.Webhooks`'s "outbound delivery to a destination outside our control, with
resilience/signing/retry/observer discipline" identity, applied to a person instead of a
subscriber's API endpoint.

---

## Minimal setup

```csharp
// Program.cs
builder.Services.AddSharedKernelNotifications();

// Required — DI resolution fails at first send without this registration.
builder.Services.AddScoped<INotificationSenderIdentityResolver, TenantNotificationSenderIdentityResolver>();

// Register whichever provider(s) you need — each keys its INotificationSender by NotificationChannel.
builder.Services.AddSendGridEmailNotifications(options => options.ApiKey = configuration["SendGrid:ApiKey"]!);
builder.Services.AddTwilioSmsNotifications(options =>
{
    options.AccountSid = configuration["Twilio:AccountSid"]!;
    options.AuthToken = configuration["Twilio:AuthToken"]!;
    options.MessagingServiceSid = configuration["Twilio:MessagingServiceSid"];
});
```

`AddSharedKernelNotifications()` registers only `NotificationDeliveryOptions`, bound to
configuration section `SharedKernel:Integration:Notifications` and validated eagerly at startup
(`ValidateOnStart()`). It deliberately does **not** register `INotificationSenderIdentityResolver`
(no default implementation — you supply one against your own tenant catalog/config) or any
`INotificationSender` (each provider package registers its own, keyed by channel).

---

## `INotificationSenderIdentityResolver` — the per-tenant "from" address seam

```csharp
public sealed class TenantNotificationSenderIdentityResolver(ITenantCatalog tenants, ITenantProvider tenantProvider)
    : INotificationSenderIdentityResolver
{
    public async Task<NotificationSenderIdentity> ResolveAsync(NotificationChannel channel, CancellationToken ct)
    {
        var tenant = await tenants.GetAsync(tenantProvider.TenantId, ct);
        return channel switch
        {
            NotificationChannel.Email => new NotificationSenderIdentity(tenant.SupportEmail, tenant.DisplayName),
            NotificationChannel.Sms => new NotificationSenderIdentity(tenant.SmsSenderId),
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
    }
}
```

This mirrors `05.Application`'s `IRequestContext` bridge pattern — this package never reaches
into a persistence store or `13.ServiceDefaults` directly; the consuming service bridges its own
real identity source at its own composition root.

---

## Sending a notification

No router/dispatcher type exists in this package — resolve the keyed `INotificationSender` for the
channel you want and call it directly. This is deliberate: unlike `IWebhookDispatcher`, which owns a
genuine fan-out across N subscriptions, a notification send is always one message to one channel.

```csharp
public sealed record OrderReceiptTemplateModel(string OrderNumber, string Total);

public sealed class OrderReceiptSender(IServiceProvider services)
{
    public async Task SendAsync(Order order, CancellationToken ct)
    {
        var sender = services.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email);

        var result = await sender.SendAsync(
            new NotificationMessage<OrderReceiptTemplateModel>
            {
                // Caller-supplied and REQUIRED — reused verbatim on a caller-level retry so the
                // provider's own dedup mechanism actually prevents a double-send.
                NotificationDeliveryId = order.ReceiptDeliveryId,
                Channel = NotificationChannel.Email,
                Recipient = order.CustomerEmail,
                TemplateId = "d-order-receipt",
                TemplateModel = new OrderReceiptTemplateModel(order.Number, order.Total.ToString()),
            },
            ct);

        if (!result.IsSuccess)
        {
            // result.Error — never log order.CustomerEmail or the TemplateModel's own fields.
        }
    }
}
```

### Attachments — always an object-storage reference, never inline bytes

```csharp
Attachments = [new NotificationAttachment
{
    FileReference = invoiceFileReference, // SharedKernel.Storage.Abstractions.Models.FileReference
    FileName = "invoice.pdf",
    ContentType = "application/pdf",
}],
```

There is no byte-array/inline-content overload anywhere on `NotificationAttachment` — the sending
provider resolves the object at send time via `IFileStorage.DownloadAsync`.

---

## Delivery observation

```csharp
builder.Services.WithNotificationDeliveryObserver<EfNotificationDeliveryLedger>();

public sealed class EfNotificationDeliveryLedger(AppDbContext db) : INotificationDeliveryObserver
{
    public Task OnAttemptAsync(NotificationDeliveryContext context, int attemptNumber, CancellationToken ct) =>
        Task.CompletedTask;

    public Task OnCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, CancellationToken ct) =>
        db.NotificationDeliveries.AddAsync(new(context.NotificationDeliveryId, result.IsSuccess), ct).AsTask();
}
```

An observer's exception is caught and logged, never allowed to fault the send outcome — the same
hard rule `IWebhookDeliveryObserver` already carries. `NotificationDeliveryContext.Recipient` is
handed to observer *code*, not logged by this package — logging it is that observer implementation's
own responsibility (and, per this domain's PII rule, its own violation if it does).

---

## `NotificationDeliveryId` — a deliberately different idempotency shape than webhooks

`WebhookDeliveryResult.DeliveryId` is generated *internally* by `WebhookDispatcher`, because an
entire webhook delivery — retries included — happens inside one dispatcher call.
`NotificationMessage.NotificationDeliveryId` is the opposite: **caller-supplied and required**. A
notification send can be retried by the *caller* after a crash (e.g. a background job re-processing
an outbox row), and only the caller can guarantee the same id is reused on that retry so the
provider's own dedup mechanism actually prevents a double-send.

The two shipped providers differ in how strong that dedup guarantee actually is — this is a property
of the vendors' own APIs, not a gap in this contract:

- **Twilio** (`SharedKernel.Integration.Notifications.Sms.Twilio`) propagates
  `NotificationDeliveryId` via the Messages API's documented `Idempotency-Key` header — a genuine,
  provider-enforced request-level guarantee.
- **SendGrid** (`SharedKernel.Integration.Notifications.Email.SendGrid`) propagates it via the Mail
  Send API's `custom_args` field, which SendGrid does not treat as a request-level dedup key —
  correlation-only. True dedup enforcement for email remains the caller's own outbox-level
  responsibility.

---

## No `Push` channel — yet

`NotificationChannel` has exactly two members, `Email` and `Sms`. Device-token registration and
platform-specific payload shaping are materially more scope than text delivery and are explicitly
out of scope for this package's first release (WO-072).

---

## `Locale` — a forward-compatible seam only

`NotificationMessage.Locale` exists on the contract today but is not consumed by any logic in this
package or either shipped provider. It is reserved for future composition with
`SharedKernel.Localization` once that package ships.
