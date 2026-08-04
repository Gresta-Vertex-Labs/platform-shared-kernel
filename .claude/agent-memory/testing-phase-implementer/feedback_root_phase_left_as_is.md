---
name: feedback_root_phase_left_as_is
description: When a phase-key promotion for one WO cycle is "earlier" in the pipeline than an already-recorded, further-along phase from a prior WO cycle on the same domain, never regress the root state-map.md's "Current Phase" field — but DO correct a stale "State" symbol if it no longer matches reality.
type: feedback
---

`16.Testing`'s root `state-map.md` Domain Summary Board row can lag behind its own sub-map for a
while when a NEW work order dispatches phases into a domain whose sub-map is already further along
(e.g. `Docs` from a prior WO cycle), and the new WO's own Design/Scaffold/Core/Tests phases complete
one at a time. Each phase-key promotion (`SK.16.Scaffold` → `SK.16.Core` → `SK.16.Tests` → ...)
technically maps to a root phase name that is "earlier" in the Design→Scaffold→Core→Tests→Docs→
Published pipeline than the domain's already-recorded `Current Phase`.

**Why:** Regressing the root's `Current Phase` field backward (e.g. from `Docs` to `Tests`) every
time an interim phase-key completes would misrepresent the domain's overall maturity to anyone
scanning the root board — no other domain's row is ever expected to go "backward." This exact
scenario recurred three times in a row for `16.Testing` during WO-053/P-335 (S-44 → Scaffold, C-102–
C-105 → Core, T-66–T-71 → Tests), and each time the correct call — confirmed by finding the SAME
precedent already applied by a prior session in the root changelog — was to leave `Current Phase` at
`Docs` and say so explicitly in the changelog line ("domain Current Phase left as-is/again left
as-is... mirroring the [prior] precedent immediately above").

**How to apply:**
- When `state-map-phase` promotion fires for a phase key that maps to a root phase EARLIER than the
  domain's currently-recorded `Current Phase`, do NOT overwrite `Current Phase` — leave it at the
  more-advanced value, and say so explicitly in both the sub-map and root changelog entries.
- DO still update the row's `Summary: Done`/`Summary: Next` text to reflect the newly-completed work
  — leaving the whole row frozen would hide genuine progress.
- DO correct the `State` symbol if it has gone stale relative to reality — e.g. if the domain's own
  further-along phase (`Docs`) has ITSELF regressed from `●` to `◐` because a newer WO added fresh
  tasks to it (discovered by actually reading the sub-map's own Overall Progress table, never assumed
  from the root's own prose). This is a factual bugfix, not a "regression" of the narrative — the
  distinction is: `Current Phase` name is a maturity milestone marker (never walk it backward), but
  `State` must always reflect current truth (correct it whichever direction the truth points).
- Recalculate the root's `Overall Progress` bucket counts and `Active Work`/`Blocked` tables to match
  the corrected `State` — e.g. moving a domain from "● Docs" to "◐ In Progress" shifts one count from
  the `● Docs` row to the `◐ In Progress` row (sum must stay 18), and a domain now `◐` needs an
  `Active Work` table row added (it was previously omitted because the domain read as fully `●`).
