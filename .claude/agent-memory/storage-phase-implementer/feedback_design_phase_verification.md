---
name: feedback_design_phase_verification
description: How to handle a "Design phase" implementation task where the brain (CLAUDE.md) was already written ahead of the state-map task breakdown
type: feedback
---

When a phase dispatch says the domain brain (`CLAUDE.md`) already contains the fleshed-out design and the task is to "verify... make corrections if gaps are found... then mark tasks complete," **actually independently re-derive any counted/summarized claim in the brain instead of trusting its own stated totals**.

**Why:** During 08.Storage's `SK.08.Design` closeout, `CLAUDE.md` and `state-map.md` both repeatedly asserted `IFileStorage` was "ten-member" in five separate places (changelog, Package Board x2, D-07, C-01) — but manually counting the actual method signatures in the interface code block gave nine. This was a genuine, non-obvious documentation defect that a surface read (checking "does each D-task's described signature appear in CLAUDE.md") would not have caught, because each individual signature was correct — only the summary count was wrong, and it was wrong consistently everywhere, so it read as self-consistent unless independently recounted.

**How to apply:** For any Design/ratification phase, after confirming each task's specific deliverable (signature, model shape, rule) is present, do a second pass: independently recount any place the document states "N-member"/"N models"/"N factories" and compare against an actual enumeration. Fix all occurrences (brain + state-map task text) in the same pass, not just the first one found — greppatern across both files first (`Grep` for the suspect number/phrase) rather than fixing one instance and assuming the rest are consistent.

Also worth doing on a design-verification pass: check for rationale asymmetry — if some design decisions have a documented "considered-and-rejected alternative" note and a structurally similar decision (e.g. a sibling method) doesn't, that's a legitimate gap to backfill for documentation-quality parity, even though it doesn't change the locked contract itself.
