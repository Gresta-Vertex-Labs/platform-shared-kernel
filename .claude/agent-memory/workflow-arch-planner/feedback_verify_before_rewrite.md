---
name: feedback_verify_before_rewrite
description: Always read the current on-disk state-map/CLAUDE.md fully before drafting a "new" phase — it may already be complete
type: feedback
---

When given a phase-definition input (e.g. a `P-xxx`/`WO-xxx` work order) to process for `17.Workflows`, always read `17.Workflows/CLAUDE.md` and `17.Workflows/state-map.md` **in full** first and check whether the content already satisfies the incoming requirement before drafting anything new.

**Why:** on the WO-046/P-287 full-package-build-out dispatch, both files already contained the entire 81-task plan and ratified design matching the incoming requirement almost word-for-word — apparently drafted ahead of the formal dispatch. Treating the dispatch as "start from scratch" would have produced needless duplicate rewriting and risked drifting from an already-carefully-reasoned design (determinism adjudications, the `Result<T>`↔failure mapping table, the single-package-shape rejection reasoning, etc.).

**How to apply:** for every incoming phase, do a criterion-by-criterion cross-check against what's already on disk. If it already matches, the correct output is a **small confirmation edit** (status markers, a changelog entry recording the dispatch was checked against the plan) — not a rewrite. Only draft new Design/Scaffold/Core/Tests/Docs/Published task rows for the genuine delta between what's already recorded and what the new requirement asks for. This keeps effort proportional to the actual gap, per the general "match effort to task size" rule.
