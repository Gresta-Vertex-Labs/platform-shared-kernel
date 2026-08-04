---
name: project_wo054_messaging_goldstandard_review
description: WO-054 07.Messaging gold-standard review — P-340-352 across 07.Messaging/00.Governance/13.ServiceDefaults/16.Testing; three confirmed shipped defects, declined built-in IIdempotencyStore
metadata:
  type: project
---

WO-054 (2026-08-04): 07.Messaging gold-standard architecture review, triggered by direct user request ("analyse 07.Messaging deeply, find bad practices, bring to gold standard, add as many phases as you like"). Dispatched 13 phases (P-340–P-352, WO-054, all `○` Pending) — 10 in 07.Messaging, 1 each in 00.Governance/13.ServiceDefaults/16.Testing.

**Why this matters:** unlike most prior gold-standard passes (WO-050/051/052/053), this domain was NOT stale/under-reviewed — it had just completed a LoggingRetrofit pass and already ships genuinely strong governance (SK0701-708, the MSG0101-equivalent rules), strong exception-handling discipline, and real Testcontainers-backed RabbitMQ integration tests. A dedicated research agent doing real `.cs`-file verification (not trusting `07.Messaging/CLAUDE.md` prose) still found three genuine, previously-undetected defects:
1. `MassTransitEventPublisher` builds `EventEnvelope<TEvent>` via raw object initializer, bypassing `04.Contracts`'s own mandated `EventEnvelope.Wrap<TEvent>()` factory, and never sets `TenantId` (shipped in 04.Contracts v2.0.0/P-331 specifically for this path) — P-340.
2. `MassTransitMessageBus.SendAsync`/`RequestAsync` never invoke registered `IMessageHeaderPropagator`s — only `PublishAsync` does. Silent, undocumented asymmetry — P-341.
3. `AzureServiceBusOptions.MaxConcurrentCalls` is fully declared/documented/type-safe but never read anywhere the bus is actually built — a dead knob that looks like it works — P-342.

**Lesson reinforced:** [[feedback_verify_shipped_code_not_docs]] — even a domain with unusually thorough, accurate-sounding CLAUDE.md prose and recent completion claims can have silent defects in the actual shipped `.cs` files. Always dispatch a real source-reading research pass before writing phases, never trust "Published"/`●` state alone.

**New capability rows added (all design-locked, queued P-340–P-352/WO-054):** ordered delivery via partition-key/session-affinity (P-344), a built-in ambient correlation propagator + locally-owned tenant-context seam mirroring 05.Application's `IAuthorizationContext`/`IUnitOfWork` bridge pattern (P-345, since `07.Messaging` may not reference `12.Security.Abstractions` directly), opt-in payload compression/encryption built on `01.Core`'s `SharedKernel.Compression`/`SharedKernel.Cryptography` (P-346, mirrors `17.Workflows`'s own payload-encryption precedent), a bus-backed readiness-probe primitive (P-347) paired with a `13.ServiceDefaults` rewiring phase (P-351) that stops the existing RabbitMQ/ASB health checks from building an independent second connection, and a dead-letter/poison-message delivery policy surface (P-343). A new `00.Governance` phase (P-350) mechanically enforces the `EventEnvelope.Wrap<TEvent>()`-only construction rule P-340 fixes, mirroring the raw-HttpClient/inline-ProblemDetails/ad-hoc-logging precedents.

**Explicitly declined (not written as a phase):** a built-in, production-grade `IIdempotencyStore` implementation shipped inside `07.Messaging` itself. Reason: strict layering — `07.Messaging` may reference only `01`–`04`, and neither `02.Caching` nor `06.Persistence` may reference upward into `07.Messaging` to implement its interface (upward references are forbidden platform-wide, not just the explicit Caching↔Messaging hard rule). No layer below `07.Messaging` can legitimately host a reference implementation. The existing "consumer implements it" design is therefore correct and was left unchanged; only a documentation quick-start recipe was queued (folded into P-349) to reduce the from-scratch burden without violating layering.

**Domain Summary Board state at dispatch time:** 07.Messaging=●, 00.Governance=●, 13.ServiceDefaults=●, 16.Testing=● — all four already `●`, so no `state-map-phase` calls were made (per the rule: only call it for domains at `○`).
