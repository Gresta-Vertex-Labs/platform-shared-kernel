# SharedKernel.Integration.Webhooks

Outbound webhook delivery for the Platform.SharedKernel ecosystem: dispatch a platform integration
event to externally-configured HTTP subscribers as a signed, retried, fan-out-safe webhook, and
verify an inbound webhook claiming to originate from this dispatcher elsewhere on the platform.

This package is delivery plumbing, not business logic. It does not decide *which* events matter to
*which* subscriber — it delivers a given integration event to a given subscriber reliably and
verifiably. Subscription storage and delivery-history persistence are the consuming microservice's
responsibility, expressed through two seams this package defines but never implements:
`IWebhookSubscriptionStore` (required) and `IWebhookDeliveryObserver` (optional).

---

## Minimal setup

```csharp
// Program.cs
builder.Services.AddSharedKernelWebhooks();

// Required — DI resolution fails at first dispatch without this registration.
builder.Services.AddScoped<IWebhookSubscriptionStore, EfWebhookSubscriptionStore>();
```

`AddSharedKernelWebhooks()` registers:

- `WebhookDeliveryOptions`, bound to configuration section `SharedKernel:Integration:Webhooks` and
  validated eagerly at startup (`ValidateOnStart()` — a misconfigured deployment fails fast, not on
  first dispatch).
- `WebhookSignatureProvider` as a singleton (stateless HMAC-SHA256 signer).
- `IWebhookDispatcher` → `WebhookDispatcher` as scoped.
- A named `HttpClient` (`"SharedKernel.Integration.Webhooks"`) wired with
  `Microsoft.Extensions.Http.Resilience`'s standard resilience handler for retry/backoff/timeout.

It deliberately does **not** register `IWebhookSubscriptionStore` — there is no default
implementation. The consuming service implements it against its own `06.Persistence` stack:

```csharp
public sealed class EfWebhookSubscriptionStore(AppDbContext db) : IWebhookSubscriptionStore
{
    public async Task<IReadOnlyList<WebhookSubscription>> GetActiveSubscriptionsAsync(
        string eventType, CancellationToken ct)
    {
        var rows = await db.WebhookSubscriptions
            .Where(s => s.IsActive)
            .Where(s => s.EventTypes.Count == 0 || s.EventTypes.Contains(eventType))
            .ToListAsync(ct);

        return rows.Select(r => new WebhookSubscription(
            r.Id, r.Url, r.Secret, r.EventTypes, r.IsActive)).ToList();
    }
}
```

The implementation owns the active/matching filter logic — `IWebhookDispatcher` trusts whatever
`IWebhookSubscriptionStore` returns.

---

## Custom `WebhookDeliveryOptions`

Pass a configuration callback to override defaults in code, or bind from `appsettings.json` under
`SharedKernel:Integration:Webhooks` (see the [configuration reference](#configuration-reference)
below — both mechanisms compose, with the code callback applied after binding).

```csharp
builder.Services.AddSharedKernelWebhooks(options =>
{
    options.MaxAttempts = 8;
    options.RequestTimeout = TimeSpan.FromSeconds(15);
    options.MaxConcurrentDeliveries = 16;
});
```

Invalid combinations (e.g. `MaxAttempts = 0`, `MaxBackoffDelay < BaseBackoffDelay`, a non-positive
`TimeSpan`) fail startup validation with an actionable `OptionsValidationException` message —
they never surface as a silent runtime misbehavior.

---

## Registering a delivery observer

`IWebhookDeliveryObserver` is the seam for persisting a delivery-history ledger, emitting metrics,
or triggering alerts — without this package taking a `06.Persistence` dependency. Zero or more
observers can be registered; every registered observer fires for every attempt and completion, in
registration order. An observer's exception is caught and logged at `LogLevel.Warning` — it never
affects the delivery outcome.

```csharp
public sealed class EfWebhookDeliveryLedger(AppDbContext db) : IWebhookDeliveryObserver
{
    public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct)
    {
        // record an attempt row
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct)
    {
        // record the terminal outcome
        return Task.CompletedTask;
    }
}

builder.Services.WithDeliveryObserver<EfWebhookDeliveryLedger>();
```

---

## Dispatching an integration event

Define an integration event implementing `IIntegrationEvent` (from `SharedKernel.Contracts`), then
call `IWebhookDispatcher.DispatchAsync`. The routing key is always `typeof(TEvent).Name` — the same
convention `EventEnvelope<TEvent>.EventType` uses in `04.Contracts`, so one event type routes
identically whether it travels over `07.Messaging` or as a webhook.

```csharp
public sealed record OrderShippedIntegrationEvent(
    Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;

public sealed class OrderShippedHandler(IWebhookDispatcher webhookDispatcher, IClock clock)
{
    public async Task HandleAsync(Guid orderId, CancellationToken ct)
    {
        var results = await webhookDispatcher.DispatchAsync(
            new OrderShippedIntegrationEvent(Guid.NewGuid(), clock.UtcNow, orderId), ct);

        foreach (var result in results.Where(r => !r.IsSuccess))
        {
            // result.SubscriptionId, result.Attempts, result.Error are populated;
            // a WebhookDeliveryExhaustedEvent was already published via IEventPublisher
            // for any subscription that exhausted MaxAttempts.
        }
    }
}
```

`DispatchAsync` looks up active subscriptions via `IWebhookSubscriptionStore`, then delivers to each
concurrently, bounded by `WebhookDeliveryOptions.MaxConcurrentDeliveries`. One subscriber's failure
never faults another's delivery, and `DispatchAsync` never throws because of a per-subscription HTTP
failure — every outcome is a `WebhookDeliveryResult`.

For a single already-resolved subscription (e.g. a manual "redeliver this one" admin action), call
`DispatchToSubscriptionAsync` directly instead of paying for the fan-out lookup:

```csharp
var result = await webhookDispatcher.DispatchToSubscriptionAsync(subscription, integrationEvent, ct);
```

### Reacting to delivery exhaustion

When a subscription exhausts `WebhookDeliveryOptions.MaxAttempts` without ever receiving a 2xx
response, the dispatcher publishes exactly one `WebhookDeliveryExhaustedEvent` via `IEventPublisher`.
Any consumer elsewhere on the platform — an ops/alerting handler, or the owning service itself — can
react to it (disable the subscription, page someone, surface it in an admin UI):

```csharp
public sealed class DisableSubscriptionOnExhaustion(AppDbContext db) // wired via your 07.Messaging consumer base
{
    public async Task HandleAsync(WebhookDeliveryExhaustedEvent evt, CancellationToken ct)
    {
        await db.WebhookSubscriptions
            .Where(s => s.Id == evt.SubscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
    }
}
```

---

## Verifying an inbound webhook

`WebhookSignatureVerifier.Verify` is the primitive a `14.Presentation` Minimal API receiver endpoint
calls to validate a webhook claiming to originate from a `SharedKernel.Integration.Webhooks`
dispatcher elsewhere on the platform. It never throws — a malformed timestamp, malformed signature,
or missing header all simply return `false`.

```csharp
app.MapPost("/webhooks/inbound/{subscriptionId:guid}", async (
    Guid subscriptionId,
    HttpRequest request,
    IWebhookSubscriptionLookup subscriptions, // your own lookup-by-id seam
    CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    var rawBody = await reader.ReadToEndAsync(ct);

    var subscription = await subscriptions.GetByIdAsync(subscriptionId, ct);
    if (subscription is null)
    {
        return Results.NotFound();
    }

    var isValid = WebhookSignatureVerifier.Verify(
        payloadJson: rawBody,
        timestampHeaderValue: request.Headers[WebhookSignatureHeaders.TimestampHeaderName],
        signatureHeaderValue: request.Headers[WebhookSignatureHeaders.SignatureHeaderName],
        secret: subscription.Secret);

    if (!isValid)
    {
        return Results.Unauthorized();
    }

    // process the verified payload...
    return Results.Ok();
});
```

`WebhookSignatureHeaders.SignatureHeaderName` (`X-Webhook-Signature`) and `.TimestampHeaderName`
(`X-Webhook-Timestamp`) are the only permitted header-name literals — always reference the constants,
never hardcode the strings at a call site. The default tolerance is 5 minutes; pass an explicit
`tolerance` argument only if a specific receiver genuinely needs a different window than
`WebhookDeliveryOptions.SignatureTolerance`'s platform default.

---

## Configuration reference

See [`docs/configuration-reference.md`](docs/configuration-reference.md) for every
`WebhookDeliveryOptions` property, its default, its validation bound, and the configuration section
path.

---

## What this package will never do

- Construct `new HttpClient()` or accept a raw `HttpClient` injection — outbound HTTP only goes
  through `IHttpClientFactory`'s named client.
- Log, serialize, or transmit `WebhookSubscription.Secret` — only the derived HMAC digest ever
  leaves this package.
- Compare a signature digest with `==`/`string.Equals` — `WebhookSignatureVerifier` uses
  `CryptographicOperations.FixedTimeEquals` exclusively.
- Reference `06.Persistence`, `11.Communication.*`, or `SharedKernel.Messaging.MassTransit`.
- Throw out of `IWebhookDispatcher` because of a single subscriber's HTTP failure.
