# SharedKernel.Integration.Webhooks

Outbound webhook delivery for the Platform.SharedKernel ecosystem: dispatch a platform integration
event to externally-configured HTTP subscribers as a signed, retried, fan-out-safe webhook, and
verify an inbound webhook claiming to originate from this dispatcher elsewhere on the platform.

This package is delivery plumbing, not business logic. It does not decide *which* events matter to
*which* subscriber — it delivers a given integration event to a given subscriber reliably and
verifiably. Subscription storage and delivery-history persistence are the consuming microservice's
responsibility, expressed through two seams this package defines but never implements:
`IWebhookSubscriptionStore` (required) and `IWebhookDeliveryObserver` (optional).

| | |
| --- | --- |
| Tier | Adapter (references Foundation, Model and Abstractions packages only: `SharedKernel.Primitives`, `.Execution`, `.Configuration`, `.Cryptography`, `SharedKernel.Contracts`, `SharedKernel.Messaging.Abstractions`) |
| Install | `<PackageReference Include="SharedKernel.Integration.Webhooks" />` |
| Test doubles | `SharedKernel.Integration.Testing`: `AddInMemoryWebhookDispatcher()`, `AddInMemoryWebhookDeliveryObserver()` |

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
            r.Id, r.Url, r.Secrets, r.EventTypes, r.IsActive)).ToList();
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

## Outbound URL validation (SSRF guard)

Every delivery is validated against `IWebhookUrlValidator` immediately before the HTTP send — never
once at subscription-registration time, closing the DNS-rebinding bypass where a hostname resolves to
a public IP at validation time and a private one at connection time. The default implementation,
`PrivateNetworkWebhookUrlValidator`, resolves the target host and rejects delivery when the resolved
IP falls in a loopback, link-local (`169.254.0.0/16`/`fe80::/10`), private (RFC1918/RFC4193), or
multicast/reserved range, for both IPv4 and IPv6. A rejected target surfaces as a failed, non-throwing
`WebhookDeliveryResult` — never a thrown exception — exactly like any other HTTP-level failure.

For legitimate internal test/staging subscriptions, opt out via `WebhookDeliveryOptions`:

```csharp
builder.Services.AddSharedKernelWebhooks(options =>
{
    options.AllowPrivateNetworkTargets = true; // never enable this for externally-supplied URLs
});
```

A consuming service with a non-default target-network policy (e.g. an internal allowlist) can supply
a fully custom validator instead — the last-registered validator wins:

```csharp
builder.Services.WithUrlValidator<CustomAllowlistWebhookUrlValidator>();
```

Prefer `AllowPrivateNetworkTargets` for the common "allow internal staging targets" case; the
SSRF-guard default posture must never be silently disabled.

---

## Zero-downtime signing-secret rotation

`WebhookSubscription.Secrets` is a newest-first list of every HMAC-SHA256 secret currently valid for
a subscription. A delivery is always signed with `Secrets[0]` (the newest); verification accepts a
match against *any* candidate in the list, supporting a dual-valid overlap window during rotation:

1. **Issue a new secret** — prepend it to `Secrets` so it becomes `Secrets[0]`, keeping the old secret
   in the list. New deliveries sign with the new secret immediately; the subscriber's own verifier
   (still configured with only the old secret) will reject them until step 2.
2. **Dual-valid overlap window** — update the subscriber's own verifier to accept both secrets
   (`WebhookSignatureVerifier.Verify(..., secretCandidates: subscription.Secrets)`), so both the new
   and the old secret validate successfully during the transition.
3. **Retire the old secret** — once the subscriber confirms they've deployed the new secret, remove
   the old one from `Secrets`.

```csharp
var subscription = subscription with { Secrets = [newSecret, .. subscription.Secrets] }; // step 1
// ...subscriber deploys verification against both secrets (step 2)...
var subscription = subscription with { Secrets = [subscription.Secrets[0]] }; // step 3, retire the old one
```

Existing single-`Secret` callers see no breaking change — `WebhookSubscription`'s obsolete
single-secret constructor still compiles, mapping to a one-element `Secrets` list.

---

## Custom per-subscription headers

`WebhookSubscription.Headers` applies optional static headers to every outbound delivery for that
subscription, alongside the standard signature/timestamp/delivery-id headers:

```csharp
var subscription = new WebhookSubscription(
    subscriptionId, url, secrets, eventTypes, isActive,
    Headers: new Dictionary<string, string> { ["X-Partner-Id"] = "acme-corp" });
```

A header name colliding case-insensitively with `WebhookSignatureHeaders.SignatureHeaderName`,
`.TimestampHeaderName`, or `.DeliveryIdHeaderName` is rejected at dispatch time — as a failed,
non-throwing `WebhookDeliveryResult` — before any HTTP call is attempted. The platform signature
headers are never silently overwritten in either direction.

---

## Opt-in payload encryption

When `WebhookDeliveryOptions.EncryptPayload` is enabled, the outbound JSON payload is encrypted
(AES-GCM, via `01.Core/SharedKernel.Cryptography`'s `ISymmetricEncryptionService.EncryptToStringAsync`)
before signing — encrypt-then-sign, so `WebhookSignatureVerifier` continues to detect tampering on
exactly the bytes that were transmitted. Disabled by default; TLS already provides transport
confidentiality — this is defense-in-depth for subscribers who want payload-level confidentiality
independent of their own TLS termination boundary.

```csharp
builder.Services.AddSingleton<IEncryptionKeyProvider, YourEncryptionKeyProvider>();
builder.Services.AddSharedKernelCryptography(builder.Configuration) // 01.Core/SharedKernel.Cryptography
    .AddSymmetricEncryption();

builder.Services.AddSharedKernelWebhooks(options => options.EncryptPayload = true);
```

The transmitted body is the ciphertext in `SharedKernel.Cryptography`'s canonical format:
`EncryptedPayload.ToString()`, unpadded Base64Url of
`[version 0x01][key id length][key id][nonce][tag][ciphertext]`. A subscriber on the platform reads it back
with `DecryptToStringAsync` (below) or `EncryptedPayload.TryParse`.

### Deriving the associated data (AAD)

Every encrypt call is bound to `WebhookPayloadAssociatedData.Build(subscription.SubscriptionId,
deliveryId)` — never a constant, and never derived solely from data transmitted on the wire. The two
components have different reproducibility stories for the subscriber:

- **`deliveryId`** — per-delivery freshness. Reproducible from the `X-Webhook-Delivery-Id` header,
  sent on every attempt of a given delivery.
- **`subscriptionId`** — identity binding. Deliberately **never sent as a header** — the subscriber
  must already know it out-of-band, through the same pre-established channel that already carries
  `WebhookSubscription.Secrets`. If the subscription id traveled alongside the ciphertext, a captured
  ciphertext could be replayed with matching AAD supplied by the attacker, defeating the entire point
  of binding subscription identity into the AAD.

On the subscriber side, decrypt after verifying the signature (verify-then-decrypt — the signature
covers the ciphertext, so verification must happen first):

```csharp
var isValid = WebhookSignatureVerifier.Verify(rawBody, timestamp, signature, subscription.Secrets);
if (!isValid)
{
    return Results.Unauthorized();
}

var deliveryId = Guid.Parse(deliveryIdHeaderValue); // X-Webhook-Delivery-Id
var associatedData = WebhookPayloadAssociatedData.Build(subscription.SubscriptionId, deliveryId);

var decrypted = await symmetricEncryptionService.DecryptToStringAsync(rawBody, associatedData);
var plaintext = decrypted.Value; // rawBody is the ciphertext
```

A mismatched AAD (wrong subscription id, wrong delivery id, or a captured ciphertext replayed against
a different subscription) fails authentication exactly like a tampered ciphertext — `decrypted.IsFailure`
is `true`, never an exception.

Enabling `EncryptPayload` without registering an `ISymmetricEncryptionService` fails loudly with an
`InvalidOperationException` at first delivery, never silently. This uses only the already-permitted
`01.Core` reference — no new cross-domain dependency.

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

Define an integration event implementing `IIntegrationEvent` (from `SharedKernel.Contracts`) and
declare its wire name with `[IntegrationEvent(...)]`, then call `IWebhookDispatcher.DispatchAsync`.
The routing key is always that declared name (resolved from the event's runtime type through
`IntegrationEventDescriptor`) — the same value as the CloudEvents `type` of its `EventEnvelope<TEvent>`
in `04.Contracts`, so one event routes identically whether it travels over `07.Messaging` or as a
webhook. It is never the CLR class name: `WebhookSubscription.EventTypes` holds names such as
`orders.order-shipped`, and renaming the class never breaks a subscription. Dispatching an event type
without a valid `[IntegrationEvent]` attribute throws `InvalidOperationException` before any lookup.

```csharp
[IntegrationEvent("orders.order-shipped")]
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

### Subscriber-side delivery deduplication

Every delivery carries an `X-Webhook-Delivery-Id` header (`WebhookSignatureHeaders.DeliveryIdHeaderName`)
— a `Guid` generated once per delivery and held stable across every retry attempt of it, also returned
as `WebhookDeliveryResult.DeliveryId`. A subscriber's receiver endpoint can use this value as an
idempotency key to deduplicate a re-sent request (e.g. a retried delivery whose earlier attempt's
response was lost in transit):

```csharp
var deliveryId = request.Headers[WebhookSignatureHeaders.DeliveryIdHeaderName].ToString();
if (await processedDeliveryStore.HasProcessedAsync(deliveryId, ct))
{
    return Results.Ok(); // already processed this exact delivery — ack without reprocessing
}
```

### Correlation id

Every delivery also carries `X-Correlation-Id` (`WellKnownHeaders.CorrelationId`) with the correlation id of
the operation that dispatched it: the ambient `IRequestContext` (`SharedKernel.Execution`), set by the host's
request-context middleware, a message consumer, a workflow activity or a scheduled job. A subscriber can quote
it in a support request and you can find the originating operation in your logs. When no operation is running,
the header is omitted; a subscription header of the same name wins.

Nothing else about the caller is sent. The tenant id, actor and client id stay inside the platform, because a
webhook endpoint is outside its trust boundary.

### Reacting to delivery exhaustion

When a subscription exhausts `WebhookDeliveryOptions.MaxAttempts` without ever receiving a 2xx
response, the dispatcher publishes exactly one `WebhookDeliveryExhaustedEvent` via `IEventPublisher`
(wire name `sharedkernel.webhooks.delivery-exhausted`; its `EventType` property carries the failed
event's `[IntegrationEvent]` name). Any consumer elsewhere on the platform — an ops/alerting handler, or the owning service itself — can
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

## Testing a new subscription

`IWebhookDispatcher.SendTestDeliveryAsync` sends a synthetic onboarding/connectivity-check delivery
to a subscription — signed, retried, and header-complete exactly like a real event dispatch, reusing
`DispatchToSubscriptionAsync` verbatim with zero parallel signing/retry logic. Use it from an
onboarding or admin flow so a new subscriber can verify their endpoint, signature verification, and
header handling before any real business event fires:

```csharp
app.MapPost("/admin/webhook-subscriptions/{subscriptionId:guid}/test", async (
    Guid subscriptionId,
    IWebhookSubscriptionLookup subscriptions,
    IWebhookDispatcher webhookDispatcher,
    CancellationToken ct) =>
{
    var subscription = await subscriptions.GetByIdAsync(subscriptionId, ct);
    if (subscription is null)
    {
        return Results.NotFound();
    }

    var result = await webhookDispatcher.SendTestDeliveryAsync(subscription, ct);
    return Results.Ok(result);
});
```

The delivery's event type is always `"sharedkernel.webhooks.ping"` (`WebhookPingEvent.EventName`, its
`[IntegrationEvent]` name) — a reserved name, recorded on the delivery's trace span and logs. The request body is the serialized `WebhookPingEvent`, which carries only
`EventId` and `OccurredOn`, so a subscriber can tell a test delivery from real business data.
`WebhookPingEvent` is never published onto `07.Messaging` and never fanned out via `DispatchAsync`'s
normal subscription lookup.

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
        secretCandidates: subscription.Secrets); // accepts a match against any active secret

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
- Log, serialize, or transmit `WebhookSubscription.Secret`/`.Secrets` — only the derived HMAC digest
  ever leaves this package.
- Compare a signature digest with `==`/`string.Equals` — `WebhookSignatureVerifier` uses
  `CryptographicOperations.FixedTimeEquals` exclusively, including per-candidate when verifying
  against multiple rotation-window secrets, without short-circuiting the iteration.
- Reference `06.Persistence`, `11.Communication.*`, `SharedKernel.Messaging.MassTransit` or any ASP.NET Core package.
- Send the caller's tenant id, actor or client id to a subscriber — only the correlation id leaves the platform.
- Throw out of `IWebhookDispatcher` because of a single subscriber's HTTP failure or an
  `IWebhookUrlValidator` rejection.
- Deliver to a target resolving to a loopback, link-local, private, or multicast/reserved IP address
  by default — the SSRF guard is fail-closed unless explicitly opted out.
- Introduce a new cryptographic primitive of its own for opt-in payload encryption — it composes
  `01.Core/SharedKernel.Cryptography`'s `ISymmetricEncryptionService` exclusively.
