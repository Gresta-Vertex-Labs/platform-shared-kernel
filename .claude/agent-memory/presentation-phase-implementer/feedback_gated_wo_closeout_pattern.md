---
name: feedback_gated_wo_closeout_pattern
description: How to close a multi-phase-key WO chain that was gated on another domain and unblocks all at once — single root state-map-phase touch, skip sync-brain for pure design-execution.
type: feedback
---

When a phase spec says "the full chain (Scaffold/Core/Tests/Docs/Published) was gated on one
upstream dependency; now that it's shipped, complete every task in one session," the instructions
literally list calling `state-map-phase` once per phase key (`SK.14.Scaffold`, `SK.14.Core`, ...).
Do not do this mechanically 5 times — it produces 5 redundant root Domain-Summary-Board rewrites
and 5 near-duplicate changelog lines, and (per the tool's own Root-mode semantics) each intermediate
call would temporarily set the domain's displayed "Current Phase" backwards (e.g. to "Scaffold")
before the final "Published" call corrects it.

**Why:** Grepping the actual root `state-map.md` changelog for the prior, structurally identical
closeout (WO-041/P-256, also a single-session multi-phase-key completion) showed only ONE root-level
entry pair was ever produced: `"14 → Published (●) — ... promoted from SK.14.Design/Core/Tests/
Docs/Published (all 6 phases now fully ●)"` plus `"Phase Backlog P-256 → ● Complete"`. There is no
history of 5 separate per-phase-key root touches for this kind of bundled closeout. The sub-map
(`14.Presentation/state-map.md`) itself was updated directly by the implementer (task rows, Overall
Progress counts, Package Board, changelog) — not through 5 mechanical `state-map-phase` sub-map-mode
calls either.

**How to apply:** When a session completes every remaining phase key of a domain in one shot because
of a single unblocking dependency: (1) update the sub-map file directly yourself (task states, Overall
Progress, Package Board, changelog) — this is faster and more precise than routing through the skill's
generic task-row templating; (2) make exactly ONE `state-map-phase` pass that does the root-level
work: update the Domain Summary Board row to the final phase/state, append one `"{N} → Published (●)
— ... promoted from SK.{N}.*"` changelog line, and close the specific `### P-NNN` Phase Backlog
entry(ies) tied to that WO (flip `**Status:**` to `● Complete`, check off its acceptance-criteria
boxes). Also **skip `sync-brain`** when the session only executes an already-locked design (a prior
Design-phase task already documented the target shape as "PENDING implementation") — grepping root
`CLAUDE.md`'s changelog for the prior P-259/P-260/P-261 domain-level closures (structurally identical:
each closed a WO-042 sub-phase in one session) found zero corresponding root `CLAUDE.md` entries for
any of them; only the domain's own `CLAUDE.md` picked up the DO-08-style doc update. Root `CLAUDE.md`
gets a new entry only when the *work order as a whole* closes (all its P-NNN phases across all
domains), authored by arch-lead, not per-domain-phase.
