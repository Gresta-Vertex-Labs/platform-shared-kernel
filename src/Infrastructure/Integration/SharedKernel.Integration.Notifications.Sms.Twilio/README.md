# SharedKernel.Integration.Notifications.Sms.Twilio

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Dedup: provider enforced](https://img.shields.io/badge/dedup-Idempotency--Key-success)

> **The SMS provider for `SharedKernel.Integration.Notifications`: sends Twilio Content-template messages over the
> Messages REST API — no vendor SDK — with the caller's `NotificationDeliveryId` as Twilio's `Idempotency-Key`, so a
> retried send never texts the customer twice.**

| You get | So that |
| --- | --- |
| `AddTwilioSmsNotifications(o => …)` | The keyed `INotificationSender` for `NotificationChannel.Sms` in one call |
| Content templates (`TemplateId` = Content SID, `TemplateModel` = variables) | Message text lives in Twilio, versioned and approved |
| `Idempotency-Key` = `NotificationDeliveryId` | A caller-level retry after a crash is deduplicated by Twilio itself |
| A sender number or a Messaging Service | Use one number, or let Twilio pick from a pool |
| The shared resilience settings | Retries, timeouts and throughput match every other notification provider |
| `NotificationDeliveryResult` with Twilio's message `sid` | Correlate with Twilio's status callbacks; branch on failure without exceptions |

## Install

```xml
<PackageReference Include="SharedKernel.Integration.Notifications.Sms.Twilio" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project |
| Depends on | `SharedKernel.Integration.Notifications.Abstractions`, `Microsoft.Extensions.Http.Resilience` |
| Namespaces | `SharedKernel.Integration.Notifications.Sms.Twilio.Extensions`, `.Options` |
| Needs in the host | `AddSharedKernelNotifications()` (retry and timeout settings) |

## Quick start

```csharp
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Sms.Twilio.Extensions;

builder.Services.AddSharedKernelNotifications();
builder.Services.AddTwilioSmsNotifications(o =>
{
    o.AccountSid = builder.Configuration["Twilio:AccountSid"]!;
    o.AuthToken = builder.Configuration["Twilio:AuthToken"]!;
    o.MessagingServiceSid = builder.Configuration["Twilio:MessagingServiceSid"];   // or o.From = "+15551234567"
});
```

```csharp
public sealed record OtpModel(string Code);

var result = await sms.SendAsync(                           // [FromKeyedServices(NotificationChannel.Sms)] INotificationSender
    new NotificationMessage<OtpModel>
    {
        NotificationDeliveryId = otpRequest.DeliveryId,     // stored with the request; reused on retry
        Channel = NotificationChannel.Sms,
        Recipient = customer.PhoneNumberE164,
        TemplateId = "HXb5b62575e6e4ff6129ad7c8efe1f983e",   // a Twilio Content SID
        TemplateModel = new OtpModel(otp.Code),
    },
    ct);
```

## How it works

- **Request.** `POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json` with HTTP Basic
  authentication (`AccountSid:AuthToken`) and an `application/x-www-form-urlencoded` body: `To`, `ContentSid`,
  `ContentVariables` (the template model serialized as a JSON **string**), and `MessagingServiceSid` — or `From` when
  no Messaging Service is set.
- **Deduplication.** The `Idempotency-Key` header carries `NotificationDeliveryId`, so Twilio rejects a duplicate of
  the same send — a real request-level guarantee, unlike SendGrid's correlation-only `custom_args`.
- **Resilience.** The named client `SharedKernel.Integration.Notifications.Sms.Twilio` uses the standard resilience
  handler configured from `NotificationDeliveryOptions`: `MaxAttempts - 1` exponential retries between
  `BaseBackoffDelay` and `MaxBackoffDelay`, `RequestTimeout` per attempt, and a rate limiter of `MaxConcurrentSends`
  to stay within the account's throughput. The same `Idempotency-Key` is sent on every retry.
- **Result.** Success carries Twilio's message `sid` as `ProviderMessageId`. A non-2xx response, timeout or transport
  error returns `IsSuccess = false`; only a `null` message throws.
- **Plain text only.** Content-API variable substitution never produces markup, so there is no HTML path. SMS carries
  no attachments, and `INotificationSenderIdentityResolver` is not used — the sender is `From`/`MessagingServiceSid`.
- **Privacy.** No platform header (tenant, actor, correlation) is sent; the phone number and template model are never
  logged or used as span tags.

## Configuration

`TwilioNotificationOptions` is set through the registration delegate and validated when the host starts; no
configuration section is bound — read the credentials from your secret store yourself.

| Option | Type | Default | Meaning |
| --- | --- | --- | --- |
| `TwilioNotificationOptions.AccountSid` | `string` | — (required) | Twilio account SID |
| `TwilioNotificationOptions.AuthToken` | `string` | — (required) | Twilio auth token |
| `TwilioNotificationOptions.From` | `string?` | `null` | Sender phone number (E.164); used when no `MessagingServiceSid` is set |
| `TwilioNotificationOptions.MessagingServiceSid` | `string?` | `null` | Messaging Service SID; takes precedence over `From` |
| `TwilioNotificationOptions.BaseAddress` | `Uri` | `https://api.twilio.com/` | Where `2010-04-01/Accounts/{AccountSid}/Messages.json` is sent. Change it only to reach a stand-in (WireMock) in local or end-to-end environments; the credentials go to this address |

At least one of `From` and `MessagingServiceSid` must be set. Retry, timeout and concurrency come from
`SharedKernel:Integration:Notifications` — see
[`SharedKernel.Integration.Notifications.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Integration/SharedKernel.Integration.Notifications.Abstractions/README.md#configuration).

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddTwilioSmsNotifications(Action<TwilioNotificationOptions> configure)` | The options (validated on start); `INotificationSender` keyed by `NotificationChannel.Sms` (scoped); the named `HttpClient` with the standard resilience handler |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 15300 | Information | Twilio SMS delivery succeeded |
| 15301 | Warning | Twilio SMS delivery failed: `{Error}` |
| 15302 | Warning | A delivery observer threw; the outcome is unaffected |

Spans come from `NotificationIntegrationActivitySource` (`SharedKernel.Integration`); subscribe with
`WithIntegrationTelemetry()`.

## Testing

Unit tests of code that sends SMS use
[`SharedKernel.Integration.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Integration/SharedKernel.Integration.Testing/README.md):
`services.AddInMemoryNotificationSender(NotificationChannel.Sms)` replaces this sender and records every message
(`ShouldHaveSent<TModel>(…)`). To test this provider itself, stub the named client with a `DelegatingHandler` and
assert the form body and the `Idempotency-Key` header.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Create a new `NotificationDeliveryId` per attempt | Create it once and reuse it | Twilio deduplicates only on the same key |
| Pass message text as `TemplateId` | Use a Content SID (`HX…`) | The provider sends Content-template messages only |
| Send national-format numbers | Use E.164 (`+905551234567`) | Twilio rejects or misroutes other formats |
| Leave both `From` and `MessagingServiceSid` empty | Set at least one | The host fails to start |
| Put the auth token in `appsettings.json` | Load it from a secret store | It controls your Twilio account |

## Design decisions

**Why no Twilio SDK?** One form-encoded REST call does the job. The SDK would bring its own `HttpClient` lifecycle,
bypass the platform's resilience pipeline and add an unaudited dependency.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Integration domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Integration/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
