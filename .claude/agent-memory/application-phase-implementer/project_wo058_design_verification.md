---
name: project_wo058_design_verification
description: WO-058 dual-control (maker-checker) Design phase closed as a pure verification pass — no gaps found; also documents a root state-map.md structural quirk and a Phase-Backlog-closure judgment call
metadata:
  type: project
---

**WO-058 (P-380, dual-control/maker-checker `DualApprovalBehavior`) Design phase (D-72..D-80) closed 2026-08-14 as a pure verification pass — zero code, zero `CLAUDE.md` content changes.** The prior `application-arch-planner` dispatch (same day, 2026-08-13) had already written the FULL design into `05.Application/CLAUDE.md` in one pass, not merely scaffolded it — contracts, pipeline order, builder guard, hard violations, and the worked maker-checker retry example (including the self-approval case) were all already present and matched every task's exact wording. This confirms the phase spec's own hint pattern (also seen in `12.Security`/`13.ServiceDefaults` WO-058 sessions) that a "Design" phase dispatched same-session by the arch-planner is often already fully written — always verify against the actual file content before assuming work remains.

**Why:** Saves redundant/duplicate design authoring and avoids overwriting already-correct content.

**How to apply:** Before writing any design content in a Design-phase session, grep/read the relevant `CLAUDE.md` sections named in the task table first. Only write if a genuine gap is found.

---

**Judgment call: do NOT blindly close a multi-phase Root Backlog ID (P-NNN) just because ONE phase key's promotion condition fires.** `05.Application/state-map.md`'s Phase Key Registry lists `P-380 (WO-058)` as the Root Backlog ID for `SK.05.Design`, but the SAME `P-380` entry is also the Root Backlog ID for `SK.05.Scaffold`/`Core`/`Tests`/`Docs`/`Published` — i.e., one Phase Backlog entry spans the entire work order's six lifecycle phases, not a 1:1 phase-to-backlog-entry mapping. The `state-map-phase` skill's Step S8a instructs closing the Root Backlog ID on promotion, but doing so after only Design completes would falsely mark the whole capability (behavior implementation, `16.Testing` fake, docs) as `● Complete` when none of it is built yet. Verified precedent: `05.Application`'s own `P-246` (WO-040) stayed `◐` Dispatched through Design/Scaffold/Core/Tests and was only closed at Docs (the WO's actual final phase). Applied the same rule here — left `P-380` at `◐` Dispatched, documented the reasoning inline in both the sub and root changelogs.

**Why:** A P-NNN Phase Backlog entry whose acceptance criteria span an entire multi-phase capability (common for WO-scoped "gap closure"/new-capability phases, as opposed to standard six-phase lifecycle entries) must only close when the LAST relevant phase key for that WO completes — check the acceptance criteria against actual shipped state, not just "did a phase key I'm touching map to this ID."

**How to apply:** Before closing a Root Backlog ID in Step S8a, read the actual Phase Backlog entry's acceptance criteria. If they describe the full capability (not just the phase you just completed), and other phase keys for the same WO still map to the same ID and remain open, leave it `◐` Dispatched.

---

**Root `state-map.md` structural quirk (13,398 lines as of 2026-08-14): the file's actual, live "Changelog" is at the END of the file, past line 13,200+, even though a `## Changelog` heading also appears at line 2221.** New Phase Backlog entries (`### P-NNN`) and changelog lines have both been appended near the end of the file over many sessions rather than inserted back into their "logically correct" mid-file section (Phase Backlog nominally spans lines 144–2220, but entries like `P-380` actually live around line 13,287, well past the `## Changelog` heading). This is because `dispatch-phase`/`state-map-phase` tooling appends new content at the file's tail for simplicity across a huge, long-lived history file.

**Why:** Grepping for `## Changelog` or `## Phase Backlog` and reading only the first match will miss the actual current/live content.

**How to apply:** When updating the root `state-map.md`, always find the true tail of the file (`wc -l` then read the last ~20 lines) to locate the most recent Phase Backlog entries and changelog lines — do not assume the section boundaries implied by `grep -n "^## "` reflect where new content should be appended.
