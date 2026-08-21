---
name: project_wo063_presentation_followup_review
description: WO-063 — same-day 14.Presentation follow-up review after WO-062 shipped; source-verified the shipment, found one unwired cross-domain handoff, dispatched 10 new phases
type: project
---

WO-063 (2026-08-20): user asked for another gold-standard/big-fintech pass over `14.Presentation` on the same day `WO-062` (P-402–P-409) shipped end to end. Rather than re-accepting the domain's own "Published"/134-tests-green status at face value, dispatched a dedicated research agent to source-verify every WO-062 claim against real `.cs` files first — see [[feedback_verify_shipped_code_not_docs]].

**Verification result:** all eight WO-062 capabilities held up with zero refutations (multi-field validation, security headers, CORS, idempotency-key, step-up auth, ETag, rate-limit bridge, SignalR defaults all genuinely wired, not just claimed).

**One real cross-domain defect found:** `14.Presentation`'s `RateLimitRejectionProblemDetails` (P-408) and `13.ServiceDefaults`'s `AddSharedKernelRateLimiting` (P-397) were each individually shipped as "the two halves of one handoff" per both domains' own `CLAUDE.md` prose — but `RateLimitingExtensions.cs:52` still leaves `OnRejected` at the BCL bare-429 default. Two already-shipped, individually-tested pieces that were never actually wired together. Dispatched as P-419 (`13.ServiceDefaults`), which must first resolve a layering question (13's ceiling doesn't list 14) before choosing a direct-reference vs. consumer-recipe fix — mirrors the WO-047 `17.Workflows` layering-exception precedent.

**Eight new `14.Presentation` hardening phases dispatched (P-411–P-418), none overlapping WO-062:**
- P-411 payload-size/JSON-max-depth DoS protection (zero coverage found — confirmed by grep)
- P-412 OpenAPI security-scheme completeness — only Bearer is documented despite `12.Security` shipping `.ApiKey`/`.Mtls` siblings
- P-413 RFC 8594 Sunset/Deprecation headers on deprecated API versions
- P-414 security-audit `[LoggerMessage]` logging retrofit — only 3 of 1000 reserved `14000-14999` EventIds are used; none of the four WO-062 rejection paths log anything
- P-415 caller-supplied correlation-id format validation — `CorrelationIdMiddleware` accepts any non-empty string verbatim into `Activity` baggage/logs, a log-injection-class gap
- P-416 file/multipart upload size+content-type boundary validation (not virus scanning)
- P-417 SignalR hub-level per-method invocation rate limiting/argument validation
- P-418 SignalR CORS/negotiate-endpoint origin-policy integration — `AddSharedKernelCors` covers WebApi only today

**P-420** (`00.Governance`) mechanically locks the new correlation-id validation default against regression, mirroring `SecureDefaultsAssertion`.

**Explicitly declined:** a mass-assignment/over-posting-protection helper — reasoned as a consumer-side DTO-design convention, not a generic library feature; a reflection-based property-allowlisting middleware would also contradict the platform's own anti-reflection governance rule.

All ten phases `○` Pending as of this entry. Domain boards for 14/13/00 were already `●` Published, so no `state-map-phase` calls were made — phases wait in the backlog for `/dispatch-phase`.

**Recurring pattern this confirms:** even a same-day, fully-shipped, all-green domain benefits from an independent second pass when explicitly asked for "gold standard" — the WO-060 (`12.Security`, third pass) and now WO-063 (`14.Presentation`, second pass) both found genuine new material on a domain the prior pass had already called complete. See [[project_wo062_presentation_goldstandard_review]] for the shipment this review verified.
