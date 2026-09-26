# SharedKernel.Integration.Notifications.Sms.Twilio

First shipping SMS provider for `SharedKernel.Integration.Notifications.Abstractions` — calls
Twilio's Content API (templated messaging) directly through `IHttpClientFactory` +
`Microsoft.Extensions.Http.Resilience`. No `Twilio` vendor NuGet SDK dependency.

| | |
| --- | --- |
| Tier | Adapter |
| Install | `<PackageReference Include="SharedKernel.Integration.Notifications.Sms.Twilio" />` (brings `.Notifications.Abstractions`) |
| Sends | Only the message itself: no tenant, actor or correlation header leaves the platform |

---

## Setup

```csharp
// Program.cs — after AddSharedKernelNotifications()
builder.Services.AddTwilioSmsNotifications(options =>
{
    options.AccountSid = builder.Configuration["Twilio:AccountSid"]!;
    options.AuthToken = builder.Configuration["Twilio:AuthToken"]!;
    options.MessagingServiceSid = builder.Configuration["Twilio:MessagingServiceSid"];
});
```

`AddTwilioSmsNotifications()` registers:

- `TwilioNotificationOptions`, validated eagerly at startup — at least one of `From` or
  `MessagingServiceSid` must be configured.
- A named `HttpClient` (`"SharedKernel.Integration.Notifications.Sms.Twilio"`) wired with the
  standard resilience handler, configured from the shared `NotificationDeliveryOptions`.
- The keyed `INotificationSender` for `NotificationChannel.Sms`.

This provider takes no `SharedKernel.Storage.Abstractions` reference — SMS carries no attachments.

---

## Sending a templated SMS

```csharp
public sealed record OtpTemplateModel(string Code);

var sender = serviceProvider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Sms);

var result = await sender.SendAsync(
    new NotificationMessage<OtpTemplateModel>
    {
        NotificationDeliveryId = otpRequest.DeliveryId,
        Channel = NotificationChannel.Sms,
        Recipient = customer.PhoneNumberE164,
        TemplateId = "HXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", // a Twilio Content Template SID
        TemplateModel = new OtpTemplateModel(otp.Code),
    },
    ct);
```

The outbound request body is `application/x-www-form-urlencoded` — `To`, `ContentSid`,
`From`/`MessagingServiceSid`, and `ContentVariables` (a JSON-*encoded string* value, not a nested
JSON object; the request envelope itself is never a JSON body). Twilio's Content API
template-variable substitution never produces markup for an SMS body, so plain-text rendering is
guaranteed structurally by this API shape — there is no HTML-rendering code path anywhere in this
provider.

---

## `NotificationDeliveryId` — a genuine, provider-enforced dedup guarantee

`NotificationDeliveryId` is propagated via Twilio's documented Messages API `Idempotency-Key`
header on every send — a real request-level dedup guarantee, stronger than
`SharedKernel.Integration.Notifications.Email.SendGrid`'s `custom_args` (correlation-only). A retry
with the same `NotificationDeliveryId` after a caller-level crash is safe: Twilio itself prevents
the duplicate send.

---

## Never throws for a provider-level failure

A non-2xx response, timeout, or transport exception all surface as a `NotificationDeliveryResult`
with `IsSuccess == false` — this sender never throws for those cases, mirroring
`IWebhookDispatcher`'s established convention. Only invalid input (a null `message`) throws.
