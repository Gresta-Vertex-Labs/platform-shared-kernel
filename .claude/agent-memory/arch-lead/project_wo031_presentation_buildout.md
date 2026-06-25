---
name: wo031-presentation-buildout
description: WO-031 — 14.Presentation full lifecycle build-out (P-192–P-199), correlation-id/ServiceDefaults decoupling, ProblemDetails/Result-HTTP governance rule
metadata:
  type: project
---

WO-031 (2026-06-25) phased the first real build-out of `14.Presentation` — the domain brain (`14.Presentation/CLAUDE.md`) was already fully designed (two packages: `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR`, with complete interface contracts) but had zero phases dispatched. Wrote P-192 through P-199 under WO-031, following the standard 6-phase lifecycle (Design/Scaffold/Core/Tests/Docs/Published), with Core split into two parallel phases (P-194 WebApi, P-195 SignalR) since they're independent packages with no shared implementation surface.

**Upgrade applied:** the original domain brain draft had a "Pending" Cross-Domain Dependency from `SK.14.Core` to `13.ServiceDefaults` for "OTel ActivitySource/baggage conventions" backing `CorrelationIdMiddleware`. Removed this — `CorrelationIdMiddleware` owns its own `Activity.SetBaggage` call directly against the BCL `System.Diagnostics.Activity` type, no ProjectReference to `SharedKernel.ServiceDefaults` needed. The root layering table still *permits* `14.Presentation → 13.ServiceDefaults` (left as-is, doesn't need to change), but the actual dependency set for this domain is just `01.Core`, `04.Contracts`, `12.Security.Abstractions`.

**Why:** Per [[feedback_upgrade_pattern_unnecessary_coupling]] (see also WO-023/WO-026 precedent) — a thin presentation/boundary-mapping package should not take a hard reference on a whole host-composition layer for one string constant. If `13.ServiceDefaults` ever wants a dedicated `ActivitySource` for presentation spans, that's an additive `13.ServiceDefaults`-side concern consuming `14.Presentation`'s public baggage key by convention, not the reverse dependency.

**Governance follow-on (P-199):** added two NetArchTest rules to `00.Governance` mirroring the established enforcement pattern — ban direct `ProblemDetails`/`HttpValidationProblemDetails` construction outside `SharedKernel.Presentation.WebApi`, and ban inline `Result.IsSuccess`/`IsFailure` branching immediately before returning an HTTP result type outside that package. This is the third time this exact pattern (raw infra type usable only through a sanctioned wrapper) has needed a governance phase — P-159 (raw HttpClient), WO-026 P-166/167 (Result/Envelope), now P-199 (ProblemDetails/Result-HTTP). **Recurring rule: any new `X→Y` boundary-mapping extension method package should automatically trigger a paired governance phase banning the inline/hand-rolled equivalent — don't wait for drift to be discovered.**

**Domain state at time of WO-031:** 14.Presentation was `○` Not Started (Domain Summary Board), so `state-map-phase` was called for it (now `◐` Design). 00.Governance was already `●` Complete, so P-199 was queued in the Phase Backlog only — `state-map-phase` was correctly *not* called for Governance, per the rule that only `○` domains get that call.

**Phase numbering at close of WO-031:** last P-NNN = P-199, last WO-NNN = WO-031. See [[project_phase_numbering]] for the canonical pointer — update that memory's "last assigned" line after this file is read.
