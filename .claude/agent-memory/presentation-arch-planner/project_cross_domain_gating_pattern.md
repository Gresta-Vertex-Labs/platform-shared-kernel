---
name: project_cross_domain_gating_pattern
description: How to plan a 14.Presentation phase whose implementation depends on an unshipped 01.Core (or other domain) constant/type
metadata:
  type: project
---

Recurring pattern across WO-041/P-256 and WO-042/P-262: arch-lead dispatches a phase whose *design* can be fully locked now but whose *code* literally cannot compile until another domain (usually `01.Core`) ships a constant/type the design references.

**How to handle it:**
- Do the Design task now and mark it `●` — design is pure documentation/decision-making, it doesn't need the upstream code to exist.
- Add the Core/Tests/Docs/Published tasks as `○` with an explicit "**BLOCKED on `{domain}`'s `{task-id}` shipping**" prefix in the task description, not just in prose elsewhere — makes the gate visible to anyone scanning the table.
- Add a Cross-Domain Dependencies row with status `Pending`/`**Pending**` (bold) naming the exact upstream compile-time symbol needed and which upstream task ships it.
- Overall Progress phase totals must include the new gated tasks in the denominator (e.g. Core goes from 18/18 `●` back to 18/20 `◐`) — don't leave a phase falsely reported as 100% once new pending tasks are added under it.
- When later re-dispatched (arch-lead or a phase-implementer) after the upstream lands: verify the upstream symbol actually exists (grep/read the source, not just the upstream state-map's checkbox) before greenlighting the gated tasks — [[project_p256_eventid_allocation]] confirms this played out exactly once already (P-249 landed, then C-14–C-18 closed same-session).

**Why:** Keeps state-map.md honest about what's actually buildable vs. merely planned, and prevents a phase-implementer from attempting to compile against a symbol that doesn't exist yet.

**How to apply:** Any future phase input that says "depends on X's design being locked but not yet shipped" (P-259/D-30 pattern for WellKnownHeaders/WellKnownBaggageKeys, P-262) — follow this exact shape: design now, code/tests/docs/publish gated with explicit BLOCKED language, Cross-Domain Dependencies row added, Overall Progress denominators updated.
