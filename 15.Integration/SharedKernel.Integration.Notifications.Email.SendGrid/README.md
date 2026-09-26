# SharedKernel.Integration.Notifications.Email.SendGrid

First shipping email provider for `SharedKernel.Integration.Notifications.Abstractions` — calls
SendGrid's v3 Mail Send REST API directly through `IHttpClientFactory` +
`Microsoft.Extensions.Http.Resilience`. No `SendGrid` vendor NuGet SDK dependency.

| | |
| --- | --- |
| Tier | Adapter |
| Install | `<PackageReference Include="SharedKernel.Integration.Notifications.Email.SendGrid" />` (brings `.Notifications.Abstractions` and `SharedKernel.Storage.Abstractions`) |
| Sends | Only the message itself: no tenant, actor or correlation header leaves the platform |

---

## Setup

```csharp
// Program.cs — after AddSharedKernelNotifications() and your own INotificationSenderIdentityResolver
builder.Services.AddSendGridEmailNotifications(options =>
{
    options.ApiKey = builder.Configuration["SendGrid:ApiKey"]!;
});

// Attachments are read through IFileStorageFactory (08.Storage): register every store an attachment
// can name, e.g. with the S3 provider.
builder.Services.AddSharedKernelStorage()
    .AddS3(builder.Configuration)
    .AddStore("invoices");
```

`AddSendGridEmailNotifications()` registers:

- `SendGridNotificationOptions`, validated eagerly at startup (`ValidateOnStart()`).
- A named `HttpClient` (`"SharedKernel.Integration.Notifications.Email.SendGrid"`) wired with the
  standard resilience handler, configured from the shared `NotificationDeliveryOptions` — the same
  field-mapping formula `SharedKernel.Integration.Webhooks` already uses.
- The keyed `INotificationSender` for `NotificationChannel.Email`.

It does **not** register `INotificationSenderIdentityResolver` or any storage — the consuming
service registers both (`AddSharedKernelStorage()` provides the `IFileStorageFactory` this sender uses).

---

## Sending a templated email

```csharp
var sender = serviceProvider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email);

var result = await sender.SendAsync(
    new NotificationMessage<OrderReceiptTemplateModel>
    {
        NotificationDeliveryId = order.ReceiptDeliveryId,
        Channel = NotificationChannel.Email,
        Recipient = order.CustomerEmail,
        TemplateId = "d-order-receipt", // a SendGrid Dynamic Template ID
        TemplateModel = new OrderReceiptTemplateModel(order.Number, order.Total.ToString()),
        Attachments = [new NotificationAttachment
        {
            FileReference = invoiceFileReference,
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
        }],
    },
    ct);
```

`TemplateModel` is serialized into SendGrid's `personalizations[0].dynamic_template_data` field.
Each attachment's `FileReference` (store, tenant, key) is opened with `IFileStorageFactory.Open(reference)`,
downloaded with `IFileStorage.DownloadAsync` and base64-encoded by streaming through a
`CryptoStream`/`ToBase64Transform` pair — the raw attachment bytes are never held as a single
contiguous `byte[]` (SendGrid's Mail Send API has no true streaming-upload path, so the base64
*text* is still assembled as one JSON string field, which is an unavoidable consequence of that
API's request shape, not of this provider's own implementation choice).

---

## `NotificationDeliveryId` — correlation only, not a request-level dedup guarantee

`NotificationDeliveryId` is propagated via SendGrid's top-level `custom_args` field, which SendGrid treats as opaque
metadata attached to the send for correlating with SendGrid's own event webhooks — **not** a
request-level idempotency key the way Twilio's `Idempotency-Key` header is
(`SharedKernel.Integration.Notifications.Sms.Twilio`). True duplicate-send prevention for email
remains the caller's own outbox-level responsibility (e.g. checking whether a
`NotificationDeliveryId` was already recorded as sent before calling `SendAsync` again).

---

## Never throws for a provider-level failure

A non-2xx response, timeout, transport exception, or an unresolvable attachment `FileReference`
(a store that is not registered, or a tenant its store does not have: `notifications.attachment_unresolvable`)
all surface as a `NotificationDeliveryResult` with `IsSuccess == false` — this sender never throws
for those cases, mirroring `IWebhookDispatcher`'s established convention. Only invalid input (a
null `message`) throws.
