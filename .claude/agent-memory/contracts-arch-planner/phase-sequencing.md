---
name: phase-sequencing
description: Work order and phase sequencing for 04.Contracts — WO-011 and WO-012
metadata:
  type: project
---

## Phase Sequencing for 04.Contracts

**WO-011** covers P-055 (EventEnvelope<TEvent>) only. It depends on P-053 (03.Domain base domain implementations being complete).

**WO-012** covers the full implementation chain:
- P-058 Scaffold — depends on nothing (creates project structure)
- P-059 Core — depends on P-058 (scaffold) and P-055 (EventEnvelope from WO-011)
- P-060 Tests — depends on P-059 (core implementation complete)
- P-061 Docs — depends on P-060 (tests validate implementation before docs are written)
- P-062 Published — depends on P-061 (docs + XML complete before packaging)

**Total tasks as of 2026-05-30:** 32 tasks across 6 phases (D-01–D-06, S-01–S-04, C-01–C-06, T-01–T-06, DO-01–DO-06, P-01–P-04).

**Why P-055 is separate from WO-012:** EventEnvelope<TEvent> was identified during WO-011 arch-lead analysis as a contracts-layer responsibility. It was logged as a standalone phase so it could be dispatched independently without blocking the full scaffold+core chain.

**How to apply:** When dispatching P-059 (Core), verify P-055 has been completed first — C-05 in the core phase integrates EventEnvelope. If P-055 is not complete, C-05 must be implemented inline.
