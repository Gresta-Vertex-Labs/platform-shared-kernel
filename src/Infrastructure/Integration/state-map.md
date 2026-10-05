# 15.Integration — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

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
| --- | --- | :---: |
| `SK.15.Design` | Design (D-01–D-07; WO-032) | ● |
| `SK.15.Scaffold` | Scaffold (S-01–S-06) | ● |
| `SK.15.Core` | Core (C-01–C-17) | ● |
| `SK.15.Tests` | Tests (T-01–T-03) | ● |
| `SK.15.Docs` | Docs (DO-01–DO-03) | ● |
| `SK.15.Published` | Published (P-01–P-05) | ● |
| `SK.15.LoggingRetrofit` | Logging retrofit (LR-01–LR-05; P-257, WO-041) | ● |
| `SK.15.WO064` | Gold-standard hardening (H-01–H-38; P-421–P-429, WO-064) | ● |
| `SK.15.WO072` | Human-facing notification delivery (N-01–N-26; P-460–P-462, WO-072) | ● |
| `SK.15.CryptoAsyncMigration` | Async `IEncryptionKeyProvider` migration (CA-01–CA-03; after `01.Core` P-446) | ● |
| `SK.15.P500` | AAD + async migration for webhook payload encryption (AA-01–AA-11; P-500, WO-081; AA-11 superseded) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. (Old rows naming `01.Core` P-249 as pending were stale — it shipped before LoggingRetrofit closed. `NotificationMessage.Locale` remains a forward-compatible seam; consuming `SharedKernel.Localization` is optional, not a dependency.)

## Completed Phases

- WO-086 ● Tiers declared; webhook delivery reads the correlation id from the request context; fakes moved to `SharedKernel.Integration.Testing` (P-563, P-566, P-571, P-572, P-574, P-575) (2026-09-26)
- SK.15.P500 ● Required associated data + async encryption for webhook payloads (WO-081) (2026-09-08)
- SK.15.WO072 ● Notifications.Abstractions, Email.SendGrid, Sms.Twilio (P-460–P-462) (2026-09-03)
- SK.15.CryptoAsyncMigration ● Build fix after `01.Core`'s async key-provider change (2026-09-03)
- SK.15.WO064 ● Hardening: resilience options, SSRF guard, delivery id, secret rotation, payload encryption, telemetry (P-421–P-429) (2026-08-21)
- SK.15.LoggingRetrofit ● `[LoggerMessage]` conversion (P-257) (2026-07-14)
- SK.15.Design → SK.15.Published ● Webhooks v1 (WO-032, P-200–P-204) (2026-06-26)

## Changelog

- [2026-09-28] State map rewritten as a living board; completed phase detail archived outside the repository.
- [2026-09-26] WO-086 foundation refactor recorded (P-563, P-566, P-571, P-572, P-574, P-575).
- [2026-09-08] AA-07–AA-10 ● in `SK.15.P500`; AA-11 superseded.
- [2026-09-08] P-500 (WO-081) design-locked.
- [2026-09-03] WO072Notifications implemented and shipped end to end (35 new tests).
