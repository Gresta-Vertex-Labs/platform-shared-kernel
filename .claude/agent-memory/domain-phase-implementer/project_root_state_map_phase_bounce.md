---
name: Root state-map-phase Domain Summary Board bounces per phase key
description: When a domain already at Published gets a new WO batch across all 6 phases at once, root Domain Summary Board's Current Phase legitimately moves backward (e.g. Published -> Design) as each phase key promotes, and Phase Backlog P-NNN entries stay Dispatched until every phase for that P-NNN closes
type: project
---

Root `state-map.md`'s `## Domain Summary Board` has exactly one row per domain, tracking only the most-recently-promoted phase key — not a rolled-up "how far along is this whole WO" view. Confirmed by direct grep of `state-map.md`'s changelog history for `03.Domain` across WO-009, WO-010/011, WO-016/014, WO-024: every time, the row's Current Phase field cycled Design -> Scaffold -> Core -> Tests -> Docs -> Published across several separate `state-map-phase` calls, even when the domain had already shown "Published" from a prior WO cycle. WO-051 (2026-07-30) is the same pattern again: closing SK.03.Design alone moved the root row from "Published ●" back to "Design ●", even though WO-051's Core/Tests/Docs/Published sub-phases (9/9/7/1 tasks) are still pending in the sub-map.

**Why:** A domain-arch-planner batches new tasks across all 6 phase tables simultaneously (Design/Scaffold/Core/Tests/Docs/Published all gain rows in the same pass), but domain-phase-implementer completes them one phase at a time across separate sessions. The root board's "state -> ●" instruction in the state-map-phase skill (Step S8) is unconditional on promotion firing — it does not check whether sibling phases from the same WO are still pending. This is intentional, not a bug to route around.

**How to apply:**
1. When propagating a sub-phase completion to root via state-map-phase, always set `state: ●` when the phase key's promotion condition is met, and `phase:` = that specific phase's root-phase name (e.g. `Design`), even if the domain was previously shown at a later phase like `Published`. Do not try to preserve the "more advanced" label or leave it at `Published`.
2. Check the sub-map's `## Phase Key Registry` row for the phase key being promoted. If `Maps to Root Phase` is exactly one of the six standard lifecycle names (`Design`/`Scaffold`/`Core`/`Tests`/`Docs`/`Published`) — Case 3 in Step S8a — skip closing any root `## Phase Backlog` `### P-NNN` entries. Those entries only close via S8b when the domain reaches `Published` again (bulk-close), or via S8a Case 1/2 for a phase key that names a specific `P-NNN` directly in its own registry row.
3. Recalculate the root `## Overall Progress` phase-bucket counts (they must sum to 18) — moving a domain's row changes both its old bucket (e.g. Published 11->10) and its new bucket (e.g. Design 0->1).
