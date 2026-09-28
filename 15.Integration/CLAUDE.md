# 15.Integration — Domain Brain

> Outbound delivery to destinations the platform does not control, with resilience, retry and observer discipline. Two families share that identity: **webhooks** (`SharedKernel.Integration.Webhooks` — signed, retried, SSRF-guarded dispatch of integration events to external HTTP subscribers, plus the `WebhookSignatureVerifier` primitive) and **human-facing notifications** (`SharedKernel.Integration.Notifications.*` — email via SendGrid, SMS via Twilio, behind one contract). The domain is delivery plumbing: it never decides *which* event matters to *which* subscriber or *when* a notification fires. It deliberately owns **no** persistence (subscription storage, delivery history and sender identity are the consuming service's, through seams defined here), **no** inbound HTTP endpoint (a `14.Presentation` receiver calls `WebhookSignatureVerifier`), **no** vendor SDKs and **no** use of `11.Communication`.

## Packages

| Package | Tier | Purpose |
|---|---|---|
| `SharedKernel.Integration.Webhooks` | Adapter | Subscription seam, HMAC-SHA256 signing + replay-resistant verification, fan-out dispatch with retry, SSRF guard, multi-secret rotation, custom headers, opt-in payload encryption, test (ping) deliveries, delivery-exhausted integration event. References `Primitives`, `Execution`, `Configuration`, `Cryptography`, `Contracts`, `Messaging.Abstractions`; `Microsoft.Extensions.Http(.Resilience)`. |
| `SharedKernel.Integration.Notifications.Abstractions` | Abstractions | Provider-neutral contract: `NotificationChannel` (`Email`, `Sms`), `NotificationMessage<TTemplateModel>`, `NotificationAttachment`, `INotificationSender`, `NotificationDeliveryResult`, `INotificationDeliveryObserver`, `NotificationDeliveryContext`, `INotificationSenderIdentityResolver`/`NotificationSenderIdentity`, `NotificationDeliveryOptions`, tracing source/tags. Zero I/O, no concrete sender. References `Primitives`, `Configuration`, `Storage.Abstractions` only. |
| `SharedKernel.Integration.Notifications.Email.SendGrid` | Adapter | SendGrid v3 Mail Send over `IHttpClientFactory` (no SDK); dynamic templates; attachments streamed from `08.Storage`. |
| `SharedKernel.Integration.Notifications.Sms.Twilio` | Adapter | Twilio Content API over `IHttpClientFactory` (no SDK); form-encoded templated sends; `Idempotency-Key` dedup. |

Also in the folder: nested `.Tests` projects and `consumer-verify/` (resolves every public surface, including both notification senders keyed side by side).

## Public Entry Points

### Webhooks (`SharedKernel.Integration.Webhooks`)

- `services.AddSharedKernelWebhooks(Action<WebhookDeliveryOptions>? configure = null)` — binds `WebhookDeliveryOptions` from `SharedKernel:Integration:Webhooks` (validated on start; `configure` applied after binding), registers `WebhookSignatureProvider`, the default `IWebhookUrlValidator` (`PrivateNetworkWebhookUrlValidator`), scoped `IWebhookDispatcher`, and the named client `WebhookHttpClientName.Name` with the standard resilience handler.
- The consumer **must** register `IWebhookSubscriptionStore` (`GetActiveSubscriptionsAsync(eventType, ct)`); there is no default.
- `services.WithDeliveryObserver<T>()` (zero or more `IWebhookDeliveryObserver`), `services.WithUrlValidator<T>()` (replaces the validator).
- `IWebhookDispatcher.DispatchAsync<TEvent>(evt, ct)` → one `WebhookDeliveryResult` per subscription; `DispatchToSubscriptionAsync<TEvent>(subscription, evt, ct)`; `SendTestDeliveryAsync(subscription, ct)` (a `WebhookPingEvent`, name `WebhookPingEvent.EventName` = `sharedkernel.webhooks.ping`).
- `WebhookSubscription(SubscriptionId, Url, Secrets, EventTypes, IsActive, Headers?)` — `Secrets` newest-first; the single-secret constructor and `Secret` property are `[Obsolete]`.
- `WebhookSignatureVerifier.Verify(payloadJson, timestampHeaderValue, signatureHeaderValue, secretCandidates, tolerance?)`; header names on `WebhookSignatureHeaders` (`X-Webhook-Signature`, `X-Webhook-Timestamp`, `X-Webhook-Delivery-Id`).
- `WebhookPayloadAssociatedData.Build(subscriptionId, deliveryId)` — the AAD a subscriber reproduces to decrypt.
- `WebhookDeliveryExhaustedEvent` (`WebhookDeliveryExhaustedEvent.EventName` = `sharedkernel.webhooks.delivery-exhausted`), published through `IEventPublisher`.
- `WebhookDeliveryOptions`: `MaxAttempts` (5), `BaseBackoffDelay` (2 s), `MaxBackoffDelay` (60 s), `RequestTimeout` (10 s), `SignatureTolerance` (5 min), `MaxConcurrentDeliveries` (8), `AllowPrivateNetworkTargets` (false), `EncryptPayload` (false). Full reference: `SharedKernel.Integration.Webhooks/docs/configuration-reference.md`.

### Notifications

- `services.AddSharedKernelNotifications(Action<NotificationDeliveryOptions>? configure = null)` — binds `NotificationDeliveryOptions` from `SharedKernel:Integration:Notifications` (`MaxAttempts` 3, `BaseBackoffDelay` 1 s, `MaxBackoffDelay` 30 s, `RequestTimeout` 10 s, `MaxConcurrentSends` 16). The consumer **must** register `INotificationSenderIdentityResolver`.
- `services.WithNotificationDeliveryObserver<T>()`.
- `services.AddSendGridEmailNotifications(o => o.ApiKey = …)` → keyed `INotificationSender` for `NotificationChannel.Email`; needs `AddSharedKernelStorage()` for attachments (`IFileStorageFactory`).
- `services.AddTwilioSmsNotifications(o => { o.AccountSid; o.AuthToken; o.From / o.MessagingServiceSid })` → keyed sender for `NotificationChannel.Sms` (at least one of `From`/`MessagingServiceSid`).
- Send: `GetRequiredKeyedService<INotificationSender>(channel).SendAsync(NotificationMessage<T> { NotificationDeliveryId, Channel, Recipient, TemplateId, TemplateModel, Attachments, Locale }, ct)`.
- Telemetry: every family's `ActivitySource` is named `SharedKernel.Integration`; the host subscribes with ServiceDefaults' `WithIntegrationTelemetry()`.

## Rules & Invariants

1. **Outbound HTTP only through `IHttpClientFactory` named clients.** Never `new HttpClient()`, never an injected `HttpClient`, never `SharedKernel.Communication.*` (undeclared Adapter→Adapter edge, and it would forward tenant/actor headers).
2. **Nothing about the caller except the correlation id leaves the platform.** Webhooks send `X-Correlation-Id` (`WellKnownHeaders.CorrelationId`) from the ambient `RequestContextScope` (`CorrelationIds.Current`), omitted when there is none; a subscription header of the same name wins. Never tenant, actor or client id; never `RequestContextPropagation`. Notification providers send no platform headers at all.
3. **Secrets are signing input only.** `WebhookSubscription.Secrets` never appears in logs, exceptions, bodies, headers or span tags; only the HMAC digest is transmitted.
4. **Signing input is `"{unixSeconds}.{payload}"` (UTF-8), signed with `Secrets[0]`;** verification accepts any candidate, comparing with `CryptographicOperations.FixedTimeEquals` per candidate without short-circuiting.
5. **`WebhookSignatureVerifier.Verify` never throws** — malformed or missing input returns `false`. The skew window is `SignatureTolerance` (default 5 min); do not hardcode another at a call site.
6. **Header names come from `WebhookSignatureHeaders`** only. A `WebhookSubscription.Headers` entry colliding (case-insensitively) with a signature/timestamp/delivery-id header fails that delivery before any HTTP call.
7. **The routing key is the `[IntegrationEvent]` name** via `IntegrationEventDescriptor`, never the CLR type name; an event type without the attribute throws `InvalidOperationException` before lookup. Reference `WebhookPingEvent.EventName`/`WebhookDeliveryExhaustedEvent.EventName`, never retype them.
8. **Per-subscription failures never throw.** HTTP failures, timeouts and SSRF rejections are a failed `WebhookDeliveryResult`; one subscriber cannot fault a fan-out. Fan-out is bounded by `MaxConcurrentDeliveries` — no unbounded `Task.WhenAll`.
9. **SSRF guard is fail-closed and runs before every send** against the DNS-resolved addresses (loopback, link-local, private, multicast/reserved; IPv4 and IPv6), never the literal hostname and never cached from registration time. Opt-outs: `AllowPrivateNetworkTargets` or `WithUrlValidator<T>()` only.
10. **One delivery id per delivery.** `X-Webhook-Delivery-Id` is stable across retries and equals `WebhookDeliveryResult.DeliveryId`.
11. **Retry lives in the resilience handler**, configured from `WebhookDeliveryOptions` (`MaxAttempts`, backoff, timeouts) — never a hand-rolled loop; every validated option must actually drive the handler. With `MaxAttempts = 1` retries are short-circuited via `ShouldHandle`.
12. **Exhaustion publishes exactly one `WebhookDeliveryExhaustedEvent`** per exhausted delivery, only after `MaxAttempts`. A failed publish is logged (`ExhaustionEventNotPublished`) and does not change the delivery outcome.
13. **Observers are isolated:** an `IWebhookDeliveryObserver`/`INotificationDeliveryObserver` exception is caught and logged at Warning, never propagated. `OnAttemptAsync` fires once per send (`attemptNumber: 1`); retries happen below the sender.
14. **Payload encryption is opt-in, encrypt-then-sign**, through `ISymmetricEncryptionService.EncryptToStringAsync` only (no crypto of this domain's own), with AAD = `WebhookPayloadAssociatedData.Build(subscriptionId, deliveryId)`. Never send the subscription id as a header — the subscriber must know it out of band, or a captured ciphertext could be replayed with attacker-supplied AAD. `EncryptPayload` without a registered `ISymmetricEncryptionService` throws `InvalidOperationException` at first delivery.
15. **Test deliveries reuse `DispatchToSubscriptionAsync`** — no parallel signing/send path. `WebhookPingEvent` is never published on the bus or fanned out.
16. **Notifications: `NotificationDeliveryId` is caller-supplied and required** — a provider never generates it, so a caller-level retry reuses it.
17. **Attachments are `SharedKernel.Storage.FileReference` only**, opened with `IFileStorageFactory.Open` and read with `DownloadAsync` at send time; no inline-bytes overload. SendGrid base64-encodes by streaming (`CryptoStream` + `ToBase64Transform`).
18. **Recipient and template model are PII:** never a log placeholder, exception message or span tag.
19. **`INotificationSender.SendAsync` never throws for provider failures** (non-2xx, timeout, transport, unresolvable attachment → `notifications.attachment_unresolvable`); only a null message throws.
20. **No notification router.** Resolve `INotificationSender` keyed by `NotificationChannel`.
21. **Provider packages never reference each other or Webhooks**, and never take the vendor SDK.
22. **No persistence types here** (`DbContext`, repositories) and no ASP.NET Core; only `Messaging.Abstractions` from `07.Messaging`.
23. **No static mutable state.** Outbound payloads serialize through source-generated `JsonSerializerContext`s (AOT-clean).

## Decisions

| Decision | Why |
|---|---|
| Webhooks is one package, no `.Abstractions` split | One delivery mechanism (HTTP); split only if a second webhook channel appears. |
| Notifications split `.Abstractions` + one package per provider | Two genuinely different providers behind one contract from day one. |
| Direct REST, no SendGrid/Twilio SDKs | Simple APIs; an SDK would bring its own `HttpClient` lifecycle and an unaudited dependency/AOT surface. |
| Not `SharedKernel.Communication.Rest` | Undeclared Adapter→Adapter edge; it forwards tenant/actor headers that must not reach an external party; targets are arbitrary URLs, not discovered services. |
| Subscription store, delivery ledger and sender identity are consumer seams | They are ordinary application data owned by the service's own `06.Persistence` stack. |
| Delivery-exhausted is an integration event via `IEventPublisher` | Any service (ops alerting, the owner) can react; no Adapter reference to MassTransit. |
| Webhook delivery id is generated internally; notification delivery id is caller-supplied | A webhook delivery (with retries) happens inside one call; a notification may be retried by the caller after a crash. |
| Dedup strength differs by provider | Twilio's `Idempotency-Key` is enforced; SendGrid's `custom_args` is correlation-only, so email dedup is the caller's outbox concern. |
| Each provider inlines its resilience-mapping callback | `.Abstractions` is zero-I/O and takes no `Http.Resilience` dependency, so no shared helper can live there. |
| `MaxConcurrentSends` enforced by the resilience pipeline's rate-limiter stage | No bespoke per-provider throttle type. |
| Two `ActivitySource` instances share the name `SharedKernel.Integration` | OTel subscribes by name; the two families have no legal reference path to share one instance. |
| No `Push` channel | Device-token registration and payload shaping are far more scope than text delivery. |

## Logging

Block **15000–15999** (`LoggingEventIdRanges.Integration`), 100-wide sub-blocks; every EventId is written `LoggingEventIdRanges.Integration + n`.

| Package | Sub-block | In use |
|---|---|---|
| `Integration.Webhooks` | 15000–15099 | `WebhookDispatcher.Log`: +0 `ObserverException` (Warning), +1 `DeliverySucceeded` (Information), +2 `DeliveryFailed` (Warning), +3 `DeliveryExhausted` (Warning), +4 `ExhaustionEventNotPublished` (Error). Next free +5. |
| `Integration.Notifications.Abstractions` | 15100–15199 | Reserved, unused (no I/O). |
| `Integration.Notifications.Email.SendGrid` | 15200–15299 | `SendGridEmailNotificationSender.Log`: +200 `DeliverySucceeded`, +201 `DeliveryFailed`, +202 `ObserverException`. |
| `Integration.Notifications.Sms.Twilio` | 15300–15399 | `TwilioSmsNotificationSender.Log`: +300 `DeliverySucceeded`, +301 `DeliveryFailed`, +302 `ObserverException`. |
| — | 15400–15999 | Unallocated (next package). |

Tracing: spans `WebhookDispatcher.Dispatch` (tags `webhook.subscription_count`, `webhook.event_type`), `WebhookDispatcher.DispatchToSubscription` (`webhook.subscription_id`, `webhook.event_type`, `webhook.outcome`, `webhook.attempt_count`) from `WebhookActivityTags`; `NotificationSender.Send` (`NotificationActivityTags`). Never a URL, secret, recipient or template field as a tag. A cross-domain tag reuses `WellKnownTagKeys`.

## Cross-Domain Couplings

- **01.Core:** `Result`/`Error`, `IClock`, `LoggingEventIdRanges`, `WellKnownHeaders` (Primitives); `RequestContextScope`/`CorrelationIds` for the correlation id (Execution); options helpers (Configuration); `ISymmetricEncryptionService` for payload encryption (Cryptography — the consumer registers `AddSharedKernelCryptography(configuration).AddSymmetricEncryption()` and an `IEncryptionKeyProvider`).
- **04.Contracts:** `IIntegrationEvent`, `[IntegrationEvent]`, `IntegrationEventDescriptor` — the webhook routing key equals the CloudEvents `type` of the same event over `07.Messaging`.
- **07.Messaging:** `IEventPublisher` publishes `WebhookDeliveryExhaustedEvent`; the host supplies the MassTransit implementation.
- **08.Storage:** `FileReference`, `IFileStorageFactory`, `IFileStorage.DownloadAsync` for SendGrid attachments.
- **13.ServiceDefaults:** `WithIntegrationTelemetry()` subscribes to `SharedKernel.Integration`.
- **14.Presentation:** a receiver endpoint calls `WebhookSignatureVerifier.Verify`; this domain hosts no endpoint.
- **16.Testing:** `SharedKernel.Integration.Testing` — `InMemoryWebhookDispatcher`, `InMemoryWebhookDeliveryObserver`, `InMemoryNotificationSender`, `InMemoryNotificationDeliveryObserver` (+ `AddInMemory*()`). A change to `IWebhookDispatcher`, `IWebhookDeliveryObserver`, `INotificationSender` or `INotificationDeliveryObserver` must update them.

## Testing

- All four `.Tests` projects and `consumer-verify` run in the **Unit lane** (`Platform.SharedKernel.Unit.slnf`): no network, no containers. HTTP is stubbed with a `DelegatingHandler` behind the named client.
- Webhooks suites pin: sign/verify round trip, tamper, expiry and malformed input (never throws); fan-out isolation; resilience field mapping (non-default options actually drive attempts, delays and timeouts — gating test); SSRF rejection via a fake validator (no real DNS); delivery-id stability across retries; span tags never carry URL/secret (`WebhookTracingTests`); multi-secret rotation; header collisions; payload encryption round trip and cross-subscription AAD swap failure (`WebhookPayloadEncryptionTests`); exactly-once exhaustion via `SharedKernel.Messaging.Testing`'s `InMemoryEventPublisher`; outcome logs via `SharedKernel.Testing`'s `InMemoryLogger`.
- Notification suites pin: options validation; no inline-bytes path on `NotificationAttachment`; recipient/template fields never logged; SendGrid `custom_args` carries the delivery id and attachments stream without one contiguous `byte[]`, with unresolvable attachments (via `SharedKernel.Storage.Testing`'s `AddInMemoryStore`/`AddInMemoryTenantStore`) returning a failed result; Twilio body is form-encoded with `ContentVariables` as a JSON string and `Idempotency-Key` carries the delivery id.
- The notification `.Tests` projects redirect `BaseIntermediateOutputPath`/`BaseOutputPath` to a temp folder because their nested paths exceed Windows `MAX_PATH`; keep that when touching those csproj files.
- Consumers use `SharedKernel.Integration.Testing`'s in-memory doubles.

## Known Limitations

- `SendGridNotificationOptions` and `TwilioNotificationOptions` are configured only through the registration delegate; their XML docs mention a `SharedKernel:Integration:Notifications:{SendGrid|Twilio}` section, but nothing binds it. Correct the doc or add binding in a code change.
- `AddSharedKernelWebhooks`/`AddSharedKernelNotifications` pass the section path as a literal to `BindConfiguration` rather than through `ISectionBoundOptions`.
- SendGrid dedup is correlation-only; duplicate-email prevention is the caller's outbox responsibility.
- `NotificationMessage.Locale` is a reserved seam; nothing consumes it yet.
- `NotificationDeliveryResult` carries no attempt count (retries are inside the resilience pipeline).
- SendGrid's API has no streaming upload, so an attachment's base64 text is still one JSON string field in the request.
- `Microsoft.Extensions.Http.Resilience` (Polly v8) AOT compatibility must be re-checked on each major bump.
