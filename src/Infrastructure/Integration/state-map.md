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

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. (Old rows naming `01.Core` P-249 as pending were stale — it shipped before LoggingRetrofit closed. `NotificationMessage.Locale` remains a forward-compatible seam; consuming `SharedKernel.Localization` is optional, not a dependency.)
