# 15.Integration — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Integration.Webhooks` | Adapter | ● | Signed (HMAC-SHA256, multi-secret rotation), retried, SSRF-guarded webhook dispatch; `X-Webhook-Delivery-Id`; test deliveries; opt-in payload encryption (encrypt-then-sign, AAD bound to subscription and delivery id); custom headers checked against platform headers; sends the correlation id only (subscriber is outside the trust boundary). |
| `SharedKernel.Integration.Notifications.Abstractions` | Abstractions | ● | Provider-neutral `INotificationSender` keyed by channel, `NotificationMessage<TTemplateModel>`, `FileReference`-only attachments (`08.Storage`), caller-supplied `NotificationDeliveryId`, `INotificationDeliveryObserver`. |
| `SharedKernel.Integration.Notifications.Email.SendGrid` | Adapter | ● | Direct SendGrid v3 REST (no vendor SDK); `NotificationDeliveryId` is correlation-only for SendGrid; recipients and template values never logged. |
| `SharedKernel.Integration.Notifications.Sms.Twilio` | Adapter | ● | Direct Twilio Content API REST (no vendor SDK); `NotificationDeliveryId` on the `Idempotency-Key` header — a real provider-enforced dedup guarantee. |

Test doubles: `InMemoryWebhookDispatcher`/`InMemoryNotificationSender` in `src/Infrastructure/Integration/SharedKernel.Integration.Testing`. Verified by `consumer-verify`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **01.Core (inbound):** `SK.01.ObservabilityHelpers` supplies `TelemetryVersion.Of`, `ActivityFailureExtensions` (`RecordFailure`, `RecordException`), `WellKnownOutcomes`, `WellKnownTagKeys.Outcome` and `LoggingEventIdRanges.SubBlockStart/IsInSubBlock`.
- **16.Testing (inbound):** `SK.16.TelemetryCapture` supplies `ActivityRecorder`, `MetricRecorder` and the log/span/metric convention assertions every task above tests with.
- **13.ServiceDefaults (outbound, note):** `SK.13.WildcardTelemetry` deletes `WithIntegrationTelemetry()`; Integration documentation stops naming it in `SK.15.IntegrationConformance`.
- **00.Governance (outbound, note):** P-583 report-only rules must show zero Integration findings after `SK.15.IntegrationConformance`.
- **Root `CLAUDE.md` (outbound, `/sync-brain`):** the "Propagating context on outbound calls" row wrongly lists Webhooks as propagating the full request context through `RequestContextPropagation`; after `SK.15.IntegrationConformance` it sends the correlation id only, through that helper with a null context.
- `NotificationMessage.Locale` remains a forward-compatible seam; consuming `SharedKernel.Localization` is optional, not a dependency.
