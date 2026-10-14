---
proven-by:
  - samples/Shop/Notify/Shop.Notify.Worker/Program.cs
  - samples/Shop/Notify/Shop.Notify.Worker/Receipts.cs
  - samples/Shop/Notify/Shop.Notify.Tests/NotifyTests.cs
  - samples/Shop/Billing/Shop.Billing.Api/Payments/MerchantWebhooks.cs
  - samples/Shop/Billing/Shop.Billing.Api/Program.cs
  - samples/Shop/Billing/Shop.Billing.Tests/BillingTests.cs
  - samples/Shop/Merchant/Shop.Merchant.Api/Program.cs
unproven:
  - Email attachments from object storage: the reference platform sends template-only emails
  - Redelivering one failed webhook (DispatchToSubscriptionAsync): the reference platform relies on the dispatcher's own retries
---
# Add notifications and webhooks

**Use when:** you email or text a person (a receipt, a passcode) through SendGrid or Twilio, send integration events
to an external HTTP endpoint as signed webhooks, or receive and verify such a webhook. **Not when:** the receiver
is another service of the platform; publish an integration event instead (add-messaging.md). A call you make and
whose answer you read is add-outbound-call.md.

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Integration.Notifications.Abstractions` | `INotificationSender`, `NotificationMessage<T>`, `NotificationChannel` |
| Infrastructure | `SharedKernel.Integration.Notifications.Email.SendGrid` | Email through SendGrid dynamic templates |
| Infrastructure | `SharedKernel.Integration.Notifications.Sms.Twilio` | SMS through Twilio content templates |
| Infrastructure | `SharedKernel.Integration.Webhooks` | Signed, retried outbound webhooks, and the signature verifier |
| Tests | `SharedKernel.Integration.Testing` | In-memory senders and webhook dispatcher |

The reference platform's Notify (a Worker) and Billing and Merchant (Api) are single-project hosts; in a four-project
service the code below sits in the projects the table names.

## Steps

1. **Application or Worker: send through the channel's sender.** Senders are keyed by `NotificationChannel`. The
   message names a provider template and a typed model; give each notification a delivery id derived from what caused
   it, so a redelivered event reuses it. The reference platform sends from a message consumer (add-messaging.md); a
   command handler injects the same senders.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Messaging.MassTransit.Consumers;

public sealed class ReceiptDueConsumer(
    [FromKeyedServices(NotificationChannel.Email)] INotificationSender email,
    [FromKeyedServices(NotificationChannel.Sms)] INotificationSender sms,
    IRequestContext caller,
    IOptions<NotifyOptions> options,
    ILogger<ReceiptDueConsumer> logger
) : ConsumerBase<EventEnvelope<ReceiptDue>>(logger)
{
    protected override async Task ConsumeAsync(EventEnvelope<ReceiptDue> envelope, CancellationToken ct)
    {
        var receipt = envelope.Data;
        // ...order and amount formatted from the receipt...
        var emailed = await email.SendAsync(
            new NotificationMessage<ReceiptEmailModel>
            {
                NotificationDeliveryId = DeliveryId(receipt.EventId, NotificationChannel.Email),
                Channel = NotificationChannel.Email,
                Recipient = receipt.CustomerEmail,
                TemplateId = options.Value.ReceiptTemplateId,
                TemplateModel = new ReceiptEmailModel(order, amount, receipt.Currency),
            },
            ct
        );
        // !emailed.IsSuccess: throw, so the bus retries the message (emailed.Error.Code says why)
    }
}
```

   Twilio uses the delivery id as its idempotency key; SendGrid only records it, so a redelivery after an accepted
   email can send it twice. Inside a command handler, return the failure as a `Result` instead of throwing.

2. **Infrastructure: register the notification core and each provider.** Neither provider binds configuration: read
   your own section and pass the values in. The sender identity (the "from" address) comes from your
   `INotificationSenderIdentityResolver`.

```csharp
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Email.SendGrid.Extensions;
using SharedKernel.Integration.Notifications.Sms.Twilio.Extensions;

var providers = builder.Configuration.GetSection(NotifyProviders.Section);
builder.Services.AddSharedKernelNotifications();
builder.Services.AddScoped<INotificationSenderIdentityResolver, ShopSenderIdentity>();
builder.Services.AddSendGridEmailNotifications(o =>
{
    o.ApiKey = providers[NotifyProviders.SendGridApiKey]!;
    o.BaseAddress = new Uri(providers[NotifyProviders.SendGridBaseAddress]!);
});
builder.Services.AddTwilioSmsNotifications(o =>
{
    o.AccountSid = providers[NotifyProviders.TwilioAccountSid]!;
    o.AuthToken = providers[NotifyProviders.TwilioAuthToken]!;
    o.From = providers[NotifyProviders.TwilioFrom];
});
```

3. **Infrastructure: send signed webhooks.** Implement `IWebhookSubscriptionStore` (who subscribed to which event,
   with which secrets) for the caller's tenant only, then dispatch the integration event. The dispatcher signs,
   retries and guards against private-network targets; the result list reports each delivery.

```csharp
using Microsoft.Extensions.Configuration;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Primitives.Clocks;

public sealed class ConfigurationWebhookSubscriptionStore(IConfiguration configuration, IRequestContext caller)
    : IWebhookSubscriptionStore
{
    private static readonly string[] Events = [IntegrationEventDescriptor.For<PaymentCaptured>().Name];

    public Task<IReadOnlyList<WebhookSubscription>> GetActiveSubscriptionsAsync(string eventType, CancellationToken cancellationToken)
    {
        if (caller.TenantId is not { } tenant || !Events.Contains(eventType))
        {
            return Task.FromResult<IReadOnlyList<WebhookSubscription>>([]);
        }
        // ...read the tenant's url and secret...
        return Task.FromResult<IReadOnlyList<WebhookSubscription>>([
            new WebhookSubscription(tenant.Value, url, [secret], Events, IsActive: true),
        ]);
    }
}

public sealed class PaymentAnnouncements(IWebhookDispatcher webhooks, IClock clock)
{
    public async Task AnnounceCapturedAsync(PaymentView payment, CancellationToken ct)
    {
        // The payment has committed; a merchant that is down must not fail it.
        _ = await webhooks.DispatchAsync(
            new PaymentCaptured(payment.PaymentId, clock.UtcNow, payment.PaymentId, payment.OrderId, payment.Amount, payment.Currency),
            ct
        );
    }
}
```

```csharp
using SharedKernel.Integration.Webhooks.Extensions;
using SharedKernel.Integration.Webhooks.Subscriptions;

builder.Services.AddSharedKernelWebhooks();
builder.Services.AddScoped<IWebhookSubscriptionStore, ConfigurationWebhookSubscriptionStore>();
builder.Services.AddScoped<PaymentAnnouncements>();
```

   `IWebhookDispatcher` is an Infrastructure type: in a four-project service, put the dispatching class in
   Infrastructure behind a port the Application owns. Keep secrets in a secret store, never in `appsettings.json`.

4. **Api: receive and verify a webhook.** The signature covers the raw body and the timestamp, so read the body as
   a string, verify, and only then parse it:

```csharp
using SharedKernel.Integration.Webhooks.Signing;

app.MapPost(
    "/webhooks/billing",
    async (HttpRequest request, IConfiguration configuration, ReceivedWebhooks received) =>
    {
        using var reader = new StreamReader(request.Body);
        string body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        bool verified = WebhookSignatureVerifier.Verify(
            body,
            request.Headers[WebhookSignatureHeaders.TimestampHeaderName],
            request.Headers[WebhookSignatureHeaders.SignatureHeaderName],
            configuration[MerchantSettings.SecretKey]
        );
        if (!verified)
        {
            return Results.Unauthorized();
        }

        received.Add(request.Headers[WebhookSignatureHeaders.DeliveryIdHeaderName].ToString(), body);
        return Results.NoContent();
    }
);
```

   Deliveries are retried, so deduplicate by the event id inside the body.

5. **Configuration:** your own section for the notification providers' keys and templates; webhook delivery options
   in `SharedKernel:Integration:Webhooks`. The keys are in each package's README.

## Test it

```csharp
using FluentAssertions;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Integration;
using SharedKernel.Testing.Notifications;

var email = new InMemoryNotificationSender(NotificationChannel.Email);
// ...run the consumer or handler with it...
var mail = email.ShouldHaveSent<ReceiptEmailModel>();
mail.Recipient.Should().Be("customer@contoso.example");

var dispatcher = new InMemoryWebhookDispatcher();
await new PaymentAnnouncements(dispatcher, new FakeClock()).AnnounceCapturedAsync(payment, default);
dispatcher.ShouldHaveDispatched<PaymentCaptured>().EventId.Should().Be(payment.PaymentId);
```

Test the subscription store with `TestRequestContext.ForTenant(...)` for two tenants: one tenant's events must never
reach the other's endpoint. Fakes and harnesses are in write-tests.md.

## Check

- **SK0030**: a `DispatchAsync` or `SendAsync` result nobody reads; discard it explicitly with `_ =` when the
  failure must not fail the caller.
- **SK0022**: literal header or configuration names; use `WebhookSignatureHeaders` and your own constants.
- **SK0013**: an `HttpClient` injected to call SendGrid, Twilio or a webhook endpoint yourself.
- A webhook to a private-network address is refused unless the delivery options allow it (development only).

## Read next

- `SharedKernel.Integration.Notifications.Abstractions`
- `SharedKernel.Integration.Notifications.Email.SendGrid`
- `SharedKernel.Integration.Notifications.Sms.Twilio`
- `SharedKernel.Integration.Webhooks`
- `SharedKernel.Integration.Testing`
