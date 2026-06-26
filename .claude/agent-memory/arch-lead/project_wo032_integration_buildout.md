---
name: wo032-integration-buildout
description: 15.Integration first real build-out — accepted pre-drafted webhook brain, split Core into Signing/Dispatch sub-phases
metadata:
  type: project
---

WO-032 (2026-06-26): 15.Integration's first dispatchable phases, P-200–P-204. Domain was `○ Not Started` on the root board going in (the folder had a `CLAUDE.md` domain brain drafted on 2026-06-25, but zero phases had ever been written to the root `state-map.md` Phase Backlog for it, and zero tasks existed in `15.Integration/state-map.md`).

**What existed before this WO:** A fully-drafted `15.Integration/CLAUDE.md` for a single package, `SharedKernel.Integration.Webhooks` — outbound signed webhook dispatch. Packages: subscriptions (`WebhookSubscription`/`IWebhookSubscriptionStore`), dispatch (`IWebhookDispatcher`/`WebhookDeliveryResult`), signing (`WebhookSignatureProvider`/`WebhookSignatureVerifier`/`WebhookSignatureHeaders`), delivery-exhausted notification (`WebhookDeliveryExhaustedEvent`), observability hook (`IWebhookDeliveryObserver`), options (`WebhookDeliveryOptions`), DI extensions (`AddSharedKernelWebhooks`/`WithDeliveryObserver<T>`). No `.Abstractions` split — correctly justified as single-provider, single-mechanism (re-evaluate only if a second outbound channel appears later).

**Verdict: ACCEPT with one upgrade.** The drafted design was validated clean against root layering rules:
- Correctly restricts to `01.Core` + `04.Contracts` + `SharedKernel.Messaging.Abstractions` only — never `06.Persistence`, never `11.Communication.*`, never `07.Messaging.MassTransit`.
- Correctly reasons that `11.Communication.Rest`'s typed-client model doesn't fit even ignoring the layering wall — webhook targets are arbitrary externally-configured URLs, not service-discovery-resolved typed clients. Goes through `IHttpClientFactory` directly; consistent with P-159 (factory injected, not client instance).
- Correctly keeps persistence out — `IWebhookSubscriptionStore`/`IWebhookDeliveryObserver` are seams the consuming service implements against its own `06.Persistence` stack, same pattern as `07.Messaging`'s `IIdempotencyStore` (WO-022).
- Signing design is gold-standard: timestamp-prefixed HMAC-SHA256, `CryptographicOperations.FixedTimeEquals` constant-time comparison, shared header-name constants preventing dispatcher/verifier drift.

**The upgrade:** Original brain's six-phase template (Design/Scaffold/Core/Tests/Docs/Published) bundled signing primitives + retrying dispatcher + DI wiring all into one "Core" phase. Split Core into **P-202 (Signing)** — pure stateless crypto, zero dependency on dispatcher/store/DI, fully testable in isolation — and **P-203 (Dispatch)** — the retrying fan-out pipeline built *on top of* proven signing. Mirrors the precedent set in WO-031 (P-194 WebApi / P-195 SignalR split of 14.Presentation's Core phase). Also folded a standalone "Tests" phase into each implementation phase's acceptance criteria rather than keeping it trailing — avoids the drift pattern of "a phase that exists only to test something built two phases ago."

**Phases written:** P-200 (Design — re-verify the draft against current `01.Core`/`04.Contracts`/`07.Messaging.Abstractions`, since those surfaces have all shipped/evolved since the brain was drafted on 2026-06-25), P-201 (Scaffold), P-202 (Core: Signing), P-203 (Core: Dispatch, depends on P-202), P-204 (Docs + Published combined). All single-domain (15.Integration only) — no cross-domain phase needed because the domain's only seams (`IWebhookSubscriptionStore`, `IWebhookDeliveryObserver`) are consumer-implemented, not SharedKernel-implemented.

**Scope expansion checked and correctly found empty:** no 03.Domain concept (event implements `04.Contracts.IIntegrationEvent`, not a domain event), no 05.Application handler, no 06.Persistence phase (by design — this domain never touches persistence), no new 07.Messaging contract (consumes existing `IEventPublisher` as-is), no 16.Testing fake needed (HTTP-layer testing is a local fake `DelegatingHandler`, same reasoning 16.Testing's own brain uses to decide what does/doesn't warrant a shared fake — this isn't "implements another domain's interface for others to reuse"), no 00.Governance rule needed yet (existing generic `SharedKernelLayeringRules` NetArchTest already polices the cross-domain reference restrictions; add a bespoke rule only if a future audit finds actual drift, per the WO-030 methodology).

**state-map-phase called:** yes — domain was `○`, promoted to `◐ Design`. Domain Summary Board, Active Work, and Overall Progress counts (In Progress 2→3, Not Started 6→5) all updated.

**sync-brain:** skipped — no new technology, package, or "What Goes Where" entry introduced; root `CLAUDE.md` already documents 15.Integration's folder-map row and layering allowance from a prior session. This WO activates an already-documented domain, doesn't introduce new architecture.

See [[project_phase_numbering]] for the updated last-phase-written pointer (now P-204/WO-032).
