---
name: project_prepublish_migration_pattern
description: How pre-publish, no-WO-number migration work items (e.g. P-544) reach 16.Testing and how they differ from normal phase dispatch
metadata:
  type: project
---

Some 16.Testing work arrives as a "pre-publish work item" (e.g. P-544, migrating `Application/`
fakes onto a redesigned `05.Application`) rather than through the normal
`testing-arch-planner` → `testing-phase-implementer` phase flow. Recognizable traits:
- No `WO-`/root `P-`-number entry exists in `16.Testing/state-map.md` for the work — it is
  dispatched directly by a coordinating session (often attributed `(coordinator)` in recent
  `16.Testing/CLAUDE.md` changelog entries, e.g. the 2026-09-15 Contracts-redesign entries).
- The owning domain (e.g. `05.Application`) is mid-redesign, breaking changes are explicitly
  "free" (nothing published yet), and shims/`[Obsolete]` are explicitly disallowed.
- Multiple domain agents may be editing sibling domains (`05.Application`, `18.Idempotency`,
  `00.Governance`) concurrently — build only the packages you own (`SharedKernel.Testing` +
  `SharedKernel.Testing.SelfTests`) and report, don't fix, unrelated breakage.

**How to apply:** For this shape of task, skip `state-map-phase` (there is no phase key to
mark — confirmed by grepping `16.Testing/state-map.md` for the P-number and finding nothing).
Still update `16.Testing/CLAUDE.md`'s live sections and append a dated changelog entry
attributed `(testing-phase-implementer)`, mirroring the coordinator-attributed entries' level
of detail. See [[feedback_claudemd_historical_vs_live]] for which sections of that file are
safe to edit versus must be left as history.
