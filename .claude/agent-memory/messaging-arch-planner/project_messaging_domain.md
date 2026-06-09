---
name: project-messaging-domain
description: WO-021 gap phases P-125 through P-131 fully planned in state-map.md and CLAUDE.md; current phase states and capabilities queued for implementation dispatch
metadata:
  type: project
---

WO-021 deep architectural review (2026-06-08) identified 9 gaps after the initial 6-phase delivery cycle. Phases P-125 through P-131 are fully planned in `07.Messaging/state-map.md` and `07.Messaging/CLAUDE.md`. P-132 (OTel) and P-133 (Governance rules) are tracked in the Pending Phases table but belong to other domains.

**Why:** The initial WO-020 delivery covered the core messaging infrastructure. WO-021 added resilience, scheduling, saga support, batch consumers, a critical Build() anti-pattern fix, and cross-service routing.

**How to apply:** When dispatching implementation phases, use the Phase Key Registry in state-map.md. The phase keys are: `SK.07.Resilience` (P-125/P-126), `SK.07.Scheduling` (P-127), `SK.07.Saga` (P-128), `SK.07.Batch` (P-129), `SK.07.Core` (P-130 fix C-21–C-24), `SK.07.Routing` (P-131). Each has a corresponding task section (R-xx, SC-xx, SA-xx, B-xx, RO-xx).

Phase completion states as of 2026-06-08:
- Design: 9/23 complete (◐) — D-10 through D-23 pending (WO-021 tasks)
- Scaffold: 6/8 complete (◐) — S-07, S-08 pending
- Core: 20/24 complete (◐) — C-21 through C-24 pending (Build() anti-pattern fix)
- Tests: 14/17 complete (◐) — T-15 through T-17 pending
- Docs: 8/14 complete (◐) — DO-09 through DO-14 pending
- Published: 5/5 complete (●)
- Resilience: 0/9 (○)
- Scheduling: 0/10 (○)
- Saga: 0/7 (○)
- Batch: 0/7 (○)
- Routing: 0/8 (○)

Related: [[project-arch-decisions]]
