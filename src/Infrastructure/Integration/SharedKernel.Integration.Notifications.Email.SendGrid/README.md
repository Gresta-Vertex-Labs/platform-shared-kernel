# SharedKernel.Integration.Notifications.Email.SendGrid

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)

> **The email provider for `SharedKernel.Integration.Notifications`: sends SendGrid dynamic-template emails over the
> v3 Mail Send REST API — no vendor SDK — with attachments streamed from object storage and failures returned as
> results.**

| You get | So that |
| --- | --- |
| `AddSendGridEmailNotifications(o => o.ApiKey = …)` | The keyed `INotificationSender` for `NotificationChannel.Email` in one call |
| Dynamic templates (`TemplateId` + `TemplateModel`) | Copy and layout stay in SendGrid; code sends data |
| Attachments from `SharedKernel.Storage` | Files are read and base64-encoded as a stream at send time |
| The shared resilience settings | Retries, timeouts and concurrency match every other notification provider |
| `NotificationDeliveryResult` with SendGrid's `X-Message-Id` | Correlate with SendGrid's event webhooks; branch on failure without exceptions |

## Install

```xml
<PackageReference Include="SharedKernel.Integration.Notifications.Email.SendGrid" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project |
| Depends on | `SharedKernel.Integration.Notifications.Abstractions`, `SharedKernel.Storage.Abstractions`, `Microsoft.Extensions.Http.Resilience` |
| Namespaces | `SharedKernel.Integration.Notifications.Email.SendGrid.Extensions`, `.Options` |
| Needs in the host | `AddSharedKernelNotifications()`, an `INotificationSenderIdentityResolver`, and `AddSharedKernelStorage()` when you send attachments |

## Quick start

```csharp
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Email.SendGrid.Extensions;

builder.Services.AddSharedKernelNotifications();                                   // retry / timeout settings
builder.Services.AddScoped<INotificationSenderIdentityResolver, TenantSenderIdentityResolver>();
builder.Services.AddSendGridEmailNotifications(o => o.ApiKey = builder.Configuration["SendGrid:ApiKey"]!);

builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("invoices");   // for attachments
```

```csharp
var result = await email.SendAsync(                         // [FromKeyedServices(NotificationChannel.Email)] INotificationSender
    new NotificationMessage<OrderReceiptModel>
    {
        NotificationDeliveryId = order.ReceiptDeliveryId,
        Channel = NotificationChannel.Email,
        Recipient = order.CustomerEmail,
        TemplateId = "d-order-receipt",                      // a SendGrid dynamic template id
        TemplateModel = new OrderReceiptModel(order.Number, order.Total.ToString("N2")),
        Attachments = [new NotificationAttachment { FileReference = invoice, FileName = "invoice.pdf", ContentType = "application/pdf" }],
    },
    ct);
```

## How it works

- **Request.** `POST https://api.sendgrid.com/v3/mail/send` with the API key as a bearer token. `TemplateModel` becomes
  `personalizations[0].dynamic_template_data`; the `From` address and display name come from
  `INotificationSenderIdentityResolver.ResolveAsync(NotificationChannel.Email)`; the reply-to is
  `message.ReplyTo`, else the identity's `ReplyTo`.
- **Delivery id.** `NotificationDeliveryId` travels in `custom_args` as `notification_delivery_id`. SendGrid treats it as
  metadata for its event webhooks, **not** as an idempotency key — prevent duplicate emails in your outbox.
- **Attachments.** Each `FileReference` is opened with `IFileStorageFactory.Open`, read with `DownloadAsync`, and
  base64-encoded through a `CryptoStream` + `ToBase64Transform`, so the raw bytes are never one contiguous array (the
  Mail Send API still needs the base64 text as one JSON field). A reference the storage factory cannot open (a store
  that is not registered, a tenant the store does not have) fails the send with
  `notifications.attachment_unresolvable`; a failed download fails it with the storage error (`storage.*`).
- **Resilience.** The named client `SharedKernel.Integration.Notifications.Email.SendGrid` uses the standard
  resilience handler configured from `NotificationDeliveryOptions`: `MaxAttempts - 1` exponential retries between
  `BaseBackoffDelay` and `MaxBackoffDelay`, `RequestTimeout` per attempt, and a rate limiter of `MaxConcurrentSends`.
- **Result.** Success carries SendGrid's `X-Message-Id` as `ProviderMessageId`. A non-2xx response, timeout or
  transport error returns `IsSuccess = false`; only a `null` message throws.
- **Privacy.** No platform header (tenant, actor, correlation) is sent; the recipient and template model are never
  logged or used as span tags.

## Configuration

`SendGridNotificationOptions` is set through the registration delegate and validated when the host starts; no
configuration section is bound — read the key from your secret store yourself.

| Option | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SendGridNotificationOptions.ApiKey` | `string` | — (required) | SendGrid API key with Mail Send permission |

Retry, timeout and concurrency come from `SharedKernel:Integration:Notifications` — see
[`SharedKernel.Integration.Notifications.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Integration/SharedKernel.Integration.Notifications.Abstractions/README.md#configuration).

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddSendGridEmailNotifications(Action<SendGridNotificationOptions> configure)` | The options (validated on start); `INotificationSender` keyed by `NotificationChannel.Email` (scoped); the named `HttpClient` with the standard resilience handler |

### Errors

| Code | When |
| --- | --- |
| `notifications.attachment_unresolvable` | An attachment's `FileReference` could not be opened by `IFileStorageFactory` |
| `storage.*` | An attachment could not be downloaded |

Other failures put the SendGrid status code in `NotificationDeliveryResult.Error`.

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 15200 | Information | SendGrid email delivery succeeded |
| 15201 | Warning | SendGrid email delivery failed: `{Error}` |
| 15202 | Warning | A delivery observer threw; the outcome is unaffected |

Spans come from `NotificationIntegrationActivitySource` (`SharedKernel.Integration`); subscribe with
`WithIntegrationTelemetry()`.

## Testing

Unit tests of code that sends email use
[`SharedKernel.Integration.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Integration/SharedKernel.Integration.Testing/README.md):
`services.AddInMemoryNotificationSender(NotificationChannel.Email)` replaces this sender and records every message
(`ShouldHaveSent<TModel>(…)`). To test this provider itself, stub the named client with a `DelegatingHandler` and use
`SharedKernel.Storage.Testing`'s in-memory stores for attachments.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Rely on SendGrid to drop a duplicate send | Record `NotificationDeliveryId` as sent in your outbox and check it | `custom_args` is correlation only |
| Forget the identity resolver | Register `INotificationSenderIdentityResolver` | The sender cannot be constructed without it |
| Attach from a store the host never registered | Register every store an attachment may name | The send fails with `notifications.attachment_unresolvable` |
| Attach very large files | Link to a presigned download instead | The base64 text is still one JSON field in the request |
| Put the API key in `appsettings.json` | Load it from a secret store | It grants mail-send on your account |

## Design decisions

**Why no SendGrid SDK?** Mail Send is one REST call. The SDK would bring its own `HttpClient` lifecycle, bypass the
platform's resilience pipeline and add an unaudited dependency.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Integration domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Integration/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
