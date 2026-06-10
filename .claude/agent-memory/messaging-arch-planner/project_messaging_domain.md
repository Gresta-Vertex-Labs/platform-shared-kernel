---
name: project-messaging-domain
description: WO-021 gap phases P-125–P-131 fully planned and complete; WO-022 phases P-134–P-137 and P-139 planned; current phase states and queued capabilities
metadata:
  type: project
---

# Project: SharedKernel Messaging Domain

WO-021 deep architectural review (2026-06-08) identified 9 gaps after the initial 6-phase delivery cycle. Phases P-125 through P-131 are fully planned and marked complete in `07.Messaging/state-map.md`. P-132 (OTel) and P-133 (Governance rules) are tracked in the Pending Phases table but belong to other domains.

WO-022 (2026-06-09) added 5 new capability phases: P-134 (Idempotency), P-135 (Header Propagation), P-136 (ConsumerDefinition), P-137 (Version Translation), P-139 (Routing Slip). All are in state `○` pending implementation dispatch.

**Why:** WO-022 phases address systemic platform gaps: ad-hoc deduplication (P-134), missing header propagation automation (P-135), retry misconfiguration risk (P-136), schema evolution fragility (P-137), and absent routing slip ergonomics (P-139).

**How to apply:** When dispatching implementation phases, use the Phase Key Registry in state-map.md. WO-022 phase keys: `SK.07.Idempotency` (P-134), `SK.07.HeaderPropagation` (P-135), `SK.07.ConsumerDefinition` (P-136), `SK.07.VersionTranslation` (P-137), `SK.07.RoutingSlip` (P-139). P-139 depends on P-128 (saga infrastructure must be in place — already complete).

Phase completion states as of 2026-06-09:

- Design: 23/23 complete (●)
- Scaffold: 8/8 complete (●)
- Core: 24/24 complete (●)
- Tests: 17/17 complete (●)
- Docs: 14/14 complete (●)
- Published: 5/5 complete (●)
- Resilience: 9/9 complete (●)
- Scheduling: 10/10 complete (●)
- Saga: 7/7 complete (●)
- Batch: 7/7 complete (●)
- Routing: 8/8 complete (●)
- Idempotency: 0/9 (○)
- HeaderPropagation: 0/8 (○)
- ConsumerDefinition: 0/7 (○)
- VersionTranslation: 0/7 (○)
- RoutingSlip: 0/10 (○)

Related: [[project-arch-decisions]]
