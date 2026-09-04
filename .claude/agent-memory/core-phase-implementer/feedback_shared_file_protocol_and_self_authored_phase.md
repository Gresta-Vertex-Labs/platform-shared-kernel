---
name: feedback-shared-file-protocol-and-self-authored-phase
description: honor an explicit no-touch list for shared files, and self-author a local phase/task table when a root-dispatched phase arrives with no prior core-arch-planner pass
metadata:
  type: feedback
---

Two things confirmed while implementing root Phase Backlog P-487 (WO-080, 2026-09-04):

1. **When the calling prompt gives an explicit "do not touch" list for shared files** (root `state-map.md`,
   root `CLAUDE.md`, `Platform.SharedKernel.slnx`) because other domain implementers are running
   concurrently, that instruction overrides this agent's normal end-of-phase workflow — including the
   `state-map-phase` command's own Sub-map-mode Step S8, which auto-propagates a completed phase key to
   root. Do not invoke `state-map-phase` at all in that situation; instead hand-edit the local
   `{NN}.Name/state-map.md` following the exact same format the command would have produced (Phase Key
   Registry row, phase section with a task table, Active Work paragraph, Overall Progress row, changelog
   line) and say so explicitly in the final report, naming what was deliberately left for the user's
   centralized root propagation. The one exception: if the caller has already told you they hand-edited a
   specific root field themselves (e.g. a `Depends on` value), treat that as done — don't touch it, don't
   second-guess it, and don't re-derive it.
   **Why:** a `state-map-phase` root propagation from two implementers racing in the same session would
   corrupt the root file no matter which one "wins."
   **How to apply:** treat any prompt-level file no-touch list as a hard constraint that beats this agent's
   own default Execution Order step 5, every time — not just for this one session.

2. **A root-dispatched phase can arrive with no preceding `core-arch-planner` design-lock pass** — this
   domain has precedent for combining Design+Core+Tests+Docs+Published into one phase section authored in
   a single implementation session when the change is small and additive (see `01.Core/state-map.md`'s
   `LoggingEventIdRangesNewDomains` phase, and now `P-487`). When that happens: pick the next unused local
   task-ID numbers per prefix by grepping `^\| D-[0-9]+ \|` etc. across the whole sub state-map (not just
   the highest number mentioned in prose — prose in other phases' `## Changelog`/design paragraphs
   sometimes references a *different* domain's task IDs, e.g. `01.Core`'s own P-384 changelog mentions
   `05.Application`'s `C-79`/`C-80`/`C-81` in prose; only exact-match table-row greps are a reliable source
   of truth for "already used").
   **Why:** guessing the next ID from the highest number seen anywhere in the file risks colliding with
   another domain's task ID that happens to be quoted in this file's own prose.
   **How to apply:** always grep for `^\| {PREFIX}-[0-9]+ \|` (the literal row-start pattern) before picking
   a new task ID in any sub state-map, in this domain or any other.
