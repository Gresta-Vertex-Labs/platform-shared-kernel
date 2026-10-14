# 15.Integration — Domain Brain

> Outbound delivery to destinations the platform does not control, with resilience, retry and observer discipline. Two families share that identity: **webhooks** (`SharedKernel.Integration.Webhooks` — signed, retried, SSRF-guarded dispatch of integration events to external HTTP subscribers, plus the `WebhookSignatureVerifier` primitive) and **human-facing notifications** (`SharedKernel.Integration.Notifications.*` — email via SendGrid, SMS via Twilio, behind one contract). The domain is delivery plumbing: it never decides *which* event matters to *which* subscriber or *when* a notification fires. It deliberately owns **no** persistence (subscription storage, delivery history and sender identity are the consuming service's, through seams defined here), **no** inbound HTTP endpoint (a `14.Presentation` receiver calls `WebhookSignatureVerifier`), **no** vendor SDKs and **no** use of `11.Communication`.

## Packages

| Package | Tier | Purpose |
|---|---|---|
| `SharedKernel.Integration.Webhooks` | Adapter | Subscription seam, HMAC-SHA256 signing + replay-resistant verification, fan-out dispatch with retry, SSRF guard, multi-secret rotation, custom headers, opt-in payload encryption, test (ping) deliveries, delivery-exhausted integration event. References `Primitives`, `Execution`, `Configuration`, `Cryptography`, `Contracts`, `Messaging.Abstractions`; `Microsoft.Extensions.Http(.Resilience)`. |
| `SharedKernel.Integration.Notifications.Abstractions` | Abstractions | Provider-neutral contract: `NotificationChannel` (`Email`, `Sms`), `NotificationMessage<TTemplateModel>`, `NotificationAttachment`, `INotificationSender`, `NotificationDeliveryResult`, `INotificationDeliveryObserver`, `INotificationSenderIdentityResolver`, `NotificationDeliveryOptions`, `NotificationErrorCodes`/`NotificationErrors` (the error catalogue both providers build), tracing source, `NotificationTagKeys` and `NotificationMetrics`. Zero I/O, no concrete sender. References `Primitives`, `Configuration`, `Storage.Abstractions` only. |
| `SharedKernel.Integration.Notifications.Email.SendGrid` | Adapter | SendGrid v3 Mail Send over `IHttpClientFactory` (no SDK); dynamic templates; attachments streamed from `08.Storage`. |
| `SharedKernel.Integration.Notifications.Sms.Twilio` | Adapter | Twilio Content API over `IHttpClientFactory` (no SDK); form-encoded templated sends; `Idempotency-Key` dedup. |

Also in the folder: `SharedKernel.Integration.Testing` (fakes), nested `.Tests` projects and `consumer-verify/` (resolves every public surface, including both notification senders keyed side by side).

## Public Entry Points

API detail, option keys and defaults: the package READMEs and `SharedKernel.Integration.Webhooks/docs/configuration-reference.md`.

### Webhooks (`SharedKernel.Integration.Webhooks`)

- `services.AddSharedKernelWebhooks(configure?)` — binds `WebhookDeliveryOptions` from `SharedKernel:Integration:Webhooks`; registers `WebhookSignatureProvider`, the default `IWebhookUrlValidator` (`PrivateNetworkWebhookUrlValidator`), scoped `IWebhookDispatcher` and the named client `WebhookHttpClientName.Name` with the standard resilience handler, plus `IClock` (`AddClock()`) and the metrics.
- The consumer **must** register `IWebhookSubscriptionStore`; there is no default.
- `services.WithDeliveryObserver<T>()` (zero or more), `services.WithUrlValidator<T>()` (replaces the validator).
- `IWebhookDispatcher` — `DispatchAsync<TEvent>` (one `WebhookDeliveryResult` per subscription), `DispatchToSubscriptionAsync<TEvent>`, `SendTestDeliveryAsync` (a `WebhookPingEvent`).
- `WebhookSubscription` — `Secrets` newest-first; the single-secret constructor and `Secret` property are `[Obsolete]`.
- `WebhookSignatureVerifier.Verify(...)`; header names on `WebhookSignatureHeaders`.
- `WebhookPayloadAssociatedData.Build(subscriptionId, deliveryId)` — the AAD a subscriber reproduces to decrypt.
- `WebhookDeliveryResult` — `Error` is `Error?` (`null` exactly when `IsSuccess`), `Succeeded(...)`/`Failed(...)` factories; codes in `WebhookErrorCodes` (`integration.webhook.*`).
- `WebhookDeliveryExhaustedEvent` (`LastErrorCode`, the code only), published through `IEventPublisher`.
- `WebhookTagKeys`, `WebhookIntegrationActivitySource`, `WebhookMetrics` — the span and instrument vocabulary of rule 26.

### Notifications

- `services.AddSharedKernelNotifications(configure?)` — binds `NotificationDeliveryOptions` from `SharedKernel:Integration:Notifications`. The consumer **must** register `INotificationSenderIdentityResolver`.
- `services.WithNotificationDeliveryObserver<T>()`.
- `services.AddSendGridEmailNotifications(o => …)` → keyed `INotificationSender` for `NotificationChannel.Email`. Storage (`AddSharedKernelStorage()`, for `IFileStorageFactory`) is needed only for attachments: without it every other message is sent, and a message with attachments fails with `integration.notification.attachment_unresolvable`.
- `services.AddTwilioSmsNotifications(o => …)` → keyed sender for `NotificationChannel.Sms` (at least one of `From`/`MessagingServiceSid`).
- Both provider options carry `BaseAddress` (default: the vendor API) so local and end-to-end environments can point at a stand-in such as WireMock; endpoint paths resolve below it, keeping any path prefix. Never hard-code a vendor URL in a sender.
- Send: `GetRequiredKeyedService<INotificationSender>(channel).SendAsync(NotificationMessage<T>, ct)`.
- `NotificationDeliveryResult` — `Error` is `Error?` (`null` exactly when `IsSuccess`), `Succeeded(...)`/`Failed(...)` factories; codes in `NotificationErrorCodes` (`integration.notification.*`), built by the public `NotificationErrors` factory.
- `NotificationTagKeys`, `NotificationIntegrationActivitySource`, `NotificationMetrics` — the span and instrument vocabulary of rule 26.
- Telemetry: both families use the `ActivitySource` and `Meter` named `SharedKernel.Integration`; `AddServiceDefaults` subscribes by wildcard. `AddSharedKernelWebhooks` and the provider registrations call `AddMetrics()` and register the `WebhookMetrics`/`NotificationMetrics` singletons.

## Rules & Invariants

1. **Outbound HTTP only through `IHttpClientFactory` named clients.** Never a raw `HttpClient` (SK0013), never `SharedKernel.Communication.*` (undeclared Adapter→Adapter edge, and it would forward tenant/actor headers).
2. **Nothing about the caller except the correlation id leaves the platform.** Webhooks send `X-Correlation-Id` (`WellKnownHeaders.CorrelationId`) from the ambient `RequestContextScope` (`CorrelationIds.Current`), omitted when there is none; a subscription header of the same name wins. Never tenant, actor or client id (rule 27 is how). Notification providers send no platform headers at all.
3. **Secrets are signing input only.** `WebhookSubscription.Secrets` never appears in logs, exceptions, bodies, headers or span tags; only the HMAC digest is transmitted.
4. **Signing input is `"{unixSeconds}.{payload}"` (UTF-8), signed with `Secrets[0]`;** verification accepts any candidate, comparing with `CryptographicOperations.FixedTimeEquals` per candidate without short-circuiting.
5. **`WebhookSignatureVerifier.Verify` never throws** — malformed or missing input returns `false`. The skew window is `SignatureTolerance` (default 5 min); do not hardcode another at a call site.
6. **Header names come from `WebhookSignatureHeaders`** only. A `WebhookSubscription.Headers` entry colliding (case-insensitively) with a signature/timestamp/delivery-id header fails that delivery before any HTTP call.
7. **The routing key is the `[IntegrationEvent]` name** via `IntegrationEventDescriptor`, never the CLR type name; an event type without the attribute throws `InvalidOperationException` before lookup. Reference `WebhookPingEvent.EventName`/`WebhookDeliveryExhaustedEvent.EventName`, never retype them.
8. **Per-subscription failures never throw.** HTTP failures, timeouts and SSRF rejections are a failed `WebhookDeliveryResult`; one subscriber cannot fault a fan-out. Fan-out is bounded by `MaxConcurrentDeliveries` — no unbounded `Task.WhenAll`. A fault boundary in `DispatchToSubscriptionAsync` (after the delivery id exists) turns every exception except caller cancellation into a failed result (`Attempts` from the tracker, `StatusCode` null), an Error log (`DeliveryFaulted`) and an exception event on the delivery span; observers still see the outcome, no exhaustion event is published, and `SendTestDeliveryAsync` inherits it. A subscription with no secrets is a failed result, not a fault. Host misconfiguration is checked at startup and resolution, outside the boundary; a store failure in `GetActiveSubscriptionsAsync` and the rule-7 `InvalidOperationException` still propagate.
9. **SSRF guard is fail-closed and runs before every send** against the DNS-resolved addresses (loopback, link-local, private, multicast/reserved; IPv4 and IPv6), never the literal hostname and never cached from registration time. Opt-outs: `AllowPrivateNetworkTargets` or `WithUrlValidator<T>()` only.
10. **One delivery id per delivery.** `X-Webhook-Delivery-Id` is stable across retries and equals `WebhookDeliveryResult.DeliveryId`.
11. **Retry lives in the resilience handler**, configured from `WebhookDeliveryOptions` (`MaxAttempts`, backoff, timeouts) — never a hand-rolled loop; every validated option must actually drive the handler. With `MaxAttempts = 1` retries are short-circuited via `ShouldHandle`.
12. **Exhaustion publishes exactly one `WebhookDeliveryExhaustedEvent`** per exhausted delivery, only after `MaxAttempts`. A failed publish is logged (`ExhaustionEventNotPublished`) and does not change the delivery outcome.
13. **Observers are isolated:** an `IWebhookDeliveryObserver`/`INotificationDeliveryObserver` exception is caught and logged at Error with the exception, never propagated. `OnAttemptAsync` fires once per send (`attemptNumber: 1`); retries happen below the sender.
14. **Payload encryption is opt-in, encrypt-then-sign**, through `ISymmetricEncryptionService.EncryptToStringAsync` only (no crypto of this domain's own), with AAD = `WebhookPayloadAssociatedData.Build(subscriptionId, deliveryId)`. Never send the subscription id as a header — the subscriber must know it out of band, or a captured ciphertext could be replayed with attacker-supplied AAD. `EncryptPayload` without a registered `ISymmetricEncryptionService` fails host startup through options validation (`WebhookEncryptionRegistrationValidator` over `IServiceProviderIsService`, no resolution during validation), and throws `InvalidOperationException` at resolution of `IWebhookDispatcher` as the backstop, before any delivery (the dispatcher constructor resolves the service once).
15. **Test deliveries reuse `DispatchToSubscriptionAsync`** — no parallel signing/send path. `WebhookPingEvent` is never published on the bus or fanned out.
16. **Notifications: `NotificationDeliveryId` is caller-supplied and required** — a provider never generates it, so a caller-level retry reuses it.
17. **Attachments are `SharedKernel.Storage.FileReference` only**, opened with `IFileStorageFactory.Open` and read with `DownloadAsync` at send time; no inline-bytes overload. SendGrid base64-encodes by streaming (`CryptoStream` + `ToBase64Transform`).
18. **Recipient and template model are PII:** never a log placeholder, exception message or span tag.
19. **`INotificationSender.SendAsync` never throws for provider failures** (non-2xx, timeout, transport, unresolvable attachment → `integration.notification.attachment_unresolvable`) and propagates caller cancellation (a client-timeout `TaskCanceledException` is `provider_timeout`); only a null message throws.
20. **No notification router.** Resolve `INotificationSender` keyed by `NotificationChannel`.
21. **Provider packages never reference each other or Webhooks**, and never take the vendor SDK.
22. **No persistence types here** (`DbContext`, repositories); only `Messaging.Abstractions` from `07.Messaging`.
23. **No static mutable state.** Outbound payloads serialize through source-generated `JsonSerializerContext`s.
24. **No exception text in any result, log, event or span tag.** `ex.Message` and an upstream `Error.Message` (a storage failure, say) never reach a `WebhookDeliveryResult`, `NotificationDeliveryResult`, `WebhookDeliveryExhaustedEvent`, log placeholder or span tag/description; a failure carries an `Error` from `WebhookErrors`/`NotificationErrors` whose message is a fixed sentence (the HTTP status code number is the one dynamic part). The caught exception object travels beside the result (internal `SendOutcome`) to the log call only (last parameter); a webhook fault is also recorded on the span (`RecordException`), while a classified failure is recorded as `RecordFailure(error)`. The storage error code may be logged as `{StorageErrorCode}`, never its message. Third-party loggers are out of reach: the standard resilience handler's Polly telemetry logs the exception text under its own category, so a host that must not log it filters that category.
25. **Results carry an `Error`, with `integration.{webhook|notification}.{reason}` codes.** `WebhookDeliveryResult.Error` and `NotificationDeliveryResult.Error` are `Error?`, `null` exactly when `IsSuccess`. Codes live in public `WebhookErrorCodes` and `NotificationErrorCodes` (the two families cannot share a type, rule 21); `NotificationErrors` is public because both providers build it, `WebhookErrors` is internal. The exhaustion event carries `LastErrorCode`.
26. **Telemetry follows the gold standard.** Spans `webhook dispatch` (Internal), `webhook deliver` (Client), `notification send` (Client); tag keys from `WebhookTagKeys`/`NotificationTagKeys`, outcome from `WellKnownTagKeys.Outcome` with `WellKnownOutcomes` values; failures recorded by the shared Primitives recorder; instruments `sharedkernel.integration.*` on the `SharedKernel.Integration` meter from `IMeterFactory` (`WebhookMetrics`, `NotificationMetrics`), with units, never tagged by subscription id, delivery id or recipient.
27. **The correlation id leaves only through `RequestContextPropagation`.** The dispatcher calls `WriteHeaders` with a `null` context and the explicit correlation id, so no tenant, actor or client header can be written; no hand-rolled header write.

## Decisions

| Decision | Why |
|---|---|
| Webhooks is one package, no `.Abstractions` split | One delivery mechanism (HTTP); split only if a second webhook channel appears. |
| Notifications split `.Abstractions` + one package per provider | Two genuinely different providers behind one contract. |
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
| The webhook fault boundary sits inside `DispatchToSubscriptionAsync`, after the delivery id exists | A faulted subscription still has a real `DeliveryId`, reaches observers and the span, and `SendTestDeliveryAsync` inherits it; caller cancellation is the one exception that propagates. |
| Missing `ISymmetricEncryptionService` fails at host startup (options validation), with a resolution-time guard behind it | A host misconfiguration must not be swallowed by the per-subscription boundary or surface mid-fan-out; the startup check uses `IServiceProviderIsService`, so validating options never resolves the cryptography stack. |
| Two public code classes, not one `IntegrationErrorCodes` | Webhooks and Notifications cannot reference each other (rule 21); the shared `integration.` prefix keeps one wire namespace. |
| `WebhookMetrics`/`NotificationMetrics` are public sealed singletons | The public dispatcher and sender constructors cannot take an internal type, and a per-scope `IMeterFactory` use would duplicate instruments. |
| `LastError` becomes `LastErrorCode` on the exhaustion event, version left at 1 | Pre-GA wire rename (WO-087 decision 2); the event must never carry message text. |

## Logging

Block **15000–15999** (`LoggingEventIdRanges.Integration`), 100-wide sub-blocks; every EventId is written `LoggingEventIdRanges.Integration + n`.

| Package | Sub-block | In use |
|---|---|---|
| `Integration.Webhooks` | 15000–15099 | `WebhooksLoggingEventIds` / `Diagnostics/WebhooksLog`: +0 `ObserverException` (Error, carries the exception), +1 `DeliverySucceeded` (Debug), +2 `DeliveryFailed` (Warning), +3 `DeliveryExhausted` (Warning), +4 `ExhaustionEventNotPublished` (Warning, a failed `Result`), +5 `DeliveryFaulted` (Error, carries the exception), +6 `ExhaustionPublisherFaulted` (Error, carries the exception). Next free +7. `DeliveryFailed`/`DeliveryExhausted` take `Exception?` as last parameter. |
| `Integration.Notifications.Abstractions` | 15100–15199 | Reserved, unused (no I/O). |
| `Integration.Notifications.Email.SendGrid` | 15200–15299 | `SendGridLoggingEventIds` / `Diagnostics/SendGridLog`: +200 `DeliverySucceeded` (Debug), +201 `DeliveryFailed` (Warning; `{ErrorType}`, `{ErrorCode}`, `{StatusCode}`, `Exception?`), +202 `ObserverException` (Error), +203 `AttachmentUnresolvable` (Warning; `{StorageErrorCode}`). Next free +204. |
| `Integration.Notifications.Sms.Twilio` | 15300–15399 | `TwilioLoggingEventIds` / `Diagnostics/TwilioLog`: +300 `DeliverySucceeded` (Debug), +301 `DeliveryFailed` (Warning; `{ErrorType}`, `{ErrorCode}`, `{StatusCode}`, `Exception?`), +302 `ObserverException` (Error). Next free +303. |
| — | 15400–15999 | Unallocated (next package). |

Templates are one sentence with quoted ids (`'{DeliveryId}'`, `'{SubscriptionId}'`, `'{NotificationDeliveryId}'`) and `{ErrorType}`/`{ErrorCode}`, never free text. Levels: success Debug, a failed `Result` Warning, a fault or an observer exception Error. Every category is the sender or dispatcher type.

Tracing and metrics: both families use the source and meter `SharedKernel.Integration` (version from `TelemetryVersion.Of`). Spans `webhook dispatch` (Internal), `webhook deliver` (Client), `notification send` (Client); tags from `WebhookTagKeys`/`NotificationTagKeys` plus `WellKnownTagKeys.Outcome`; instruments `sharedkernel.integration.webhook.deliveries` (`{delivery}`), `...webhook.delivery.duration` (`s`), `...notification.sends` (`{message}`), `...notification.send.duration` (`s`), tagged by event type or channel and outcome only. Never a URL, secret, recipient or template field as a tag. A cross-domain tag reuses `WellKnownTagKeys`.

## Cross-Domain Couplings

- **01.Core:** `Result`/`Error`, `IClock`, `WellKnownHeaders` (Primitives); `RequestContextScope`/`CorrelationIds` for the correlation id (Execution); options helpers (Configuration); `ISymmetricEncryptionService` for payload encryption (Cryptography — the consumer registers `AddSharedKernelCryptography(configuration).AddSymmetricEncryption()` and an `IEncryptionKeyProvider`).
- **04.Contracts:** `IIntegrationEvent`, `[IntegrationEvent]`, `IntegrationEventDescriptor` — the webhook routing key equals the CloudEvents `type` of the same event over `07.Messaging`.
- **07.Messaging:** `IEventPublisher` publishes `WebhookDeliveryExhaustedEvent`; the host supplies the MassTransit implementation.
- **08.Storage:** `FileReference`, `IFileStorageFactory`, `IFileStorage.DownloadAsync` for SendGrid attachments.
- **13.ServiceDefaults:** `AddServiceDefaults` subscribes to `SharedKernel.*` by wildcard, which covers the `SharedKernel.Integration` source and meter; this domain registers no host telemetry.
- **01.Core / 16.Testing:** `TelemetryVersion`, `ActivityFailureExtensions` (`RecordFailure`, `RecordException`), `WellKnownOutcomes`, `WellKnownTagKeys.Outcome` and `RequestContextPropagation.WriteHeaders` (Primitives, Execution); the tests assert through the `SharedKernel.Testing` telemetry capture helpers.
- **14.Presentation:** a receiver endpoint calls `WebhookSignatureVerifier.Verify`; this domain hosts no endpoint.
- **16.Testing:** `SharedKernel.Integration.Testing` doubles the dispatcher, the sender and both observers. A change to `IWebhookDispatcher`, `IWebhookDeliveryObserver`, `INotificationSender` or `INotificationDeliveryObserver` must update them.

## Testing

- Every `.Tests` project and `consumer-verify` runs in the **Unit lane**: no network, no containers. HTTP is stubbed with a `DelegatingHandler` behind the named client.
- Webhooks suites pin: sign/verify round trip, tamper, expiry and malformed input (never throws); fan-out isolation; resilience field mapping (non-default options actually drive attempts, delays and timeouts — gating test); SSRF rejection via a fake validator (no real DNS); delivery-id stability across retries; span tags never carry URL/secret (`WebhookTracingTests`); multi-secret rotation; header collisions; payload encryption round trip and cross-subscription AAD swap failure (`WebhookPayloadEncryptionTests`); exactly-once exhaustion via `SharedKernel.Messaging.Testing`'s `InMemoryEventPublisher`; outcome logs via `SharedKernel.Testing`'s `InMemoryLogger`.
- Notification suites pin: options validation; no inline-bytes path on `NotificationAttachment`; recipient/template fields never logged; SendGrid `custom_args` carries the delivery id and attachments stream without one contiguous `byte[]`, with unresolvable attachments (via `SharedKernel.Storage.Testing`) returning a failed result; Twilio body is form-encoded with `ContentVariables` as a JSON string and `Idempotency-Key` carries the delivery id.
- Conformance suites (`WebhookConformanceTests`, `WebhookPropagationAndStartupTests`, `SendGridConformanceTests`, `TwilioConformanceTests`, `NotificationErrorsTests`) pin, through the `SharedKernel.Testing` telemetry helpers: every error condition's code, `ErrorType` and fixed message; `IsSuccess == (Error is null)`; the exhaustion event's `lastErrorCode` on the wire; span name, kind and exact tag set with the failure, exception and cancellation shapes (`ShouldBeRecordedFailure`/`Exception`/`Cancellation`, `ShouldNotLeak`); instruments with units, exact tag sets and one count per terminal outcome (a retried delivery counts once); EventIds, levels, templates and category (`ShouldFollowLogConvention`, `ShouldOnlyLogEventIdsInSubBlock`); only the correlation id on the wire; startup validation of `EncryptPayload`. The sender test classes share one xUnit collection each because the notification meter is process-wide by name; webhook metric tests isolate by a private event type.
- Fakes: `SharedKernel.Integration.Testing` — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- `SendGridNotificationOptions` and `TwilioNotificationOptions` are configured only through the registration delegate; their XML docs mention a `SharedKernel:Integration:Notifications:{SendGrid|Twilio}` section, but nothing binds it. Correct the doc or add binding in a code change.
- `AddSharedKernelWebhooks`/`AddSharedKernelNotifications` pass the section path as a literal to `BindConfiguration` rather than through `ISectionBoundOptions`.
- SendGrid dedup is correlation-only; duplicate-email prevention is the caller's outbox responsibility.
- `NotificationMessage.Locale` is a reserved seam; nothing consumes it yet.
- `NotificationDeliveryResult` carries no attempt count (retries are inside the resilience pipeline).
- SendGrid's API has no streaming upload, so an attachment's base64 text is still one JSON string field in the request.
- `Microsoft.Extensions.Http.Resilience` (Polly v8) AOT compatibility must be re-checked on each major bump.
