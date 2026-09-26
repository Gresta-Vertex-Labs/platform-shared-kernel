---
name: feedback-verify-dependency-claims
description: Always verify a phase input's stated cross-domain dependency status against the target domain's own state-map before recording it as Available
metadata:
  type: feedback
---

> WO-086 (2026-09): `AddSearchReadinessCheck` was deleted (search indexes register `IReadinessProbe`s); the verification lesson below still holds.

A dispatched phase input's "Depends on" line is a claim, not a fact. WO-042/P-261 (2026-07-14) stated
"P-259 (already dispatched and implemented in 01.Core...)" — but reading `01.Core/state-map.md` directly
showed `SK.01.P259` was only D-30 (design) `●`; C-43 (the actual `WellKnownHeaders`/`WellKnownBaggageKeys`
implementation), T-34, and DO-16 were still `○`. The dependency was designed, not implemented.

**Why this matters:** recording the Cross-Domain Dependencies row as "Available" when it is actually
"design-locked, not yet shipped" would let a future implementer agent try to compile against a type that
doesn't exist yet, or worse, silently skip verifying and assume the phase is unblocked.

**How to apply:** before writing any Cross-Domain Dependencies row or Blocked-section entry that names
another domain's artifact, actually open that domain's `state-map.md` (or grep its source tree for the
named type/file) rather than trusting the phase-input's stated status. This mirrors the general
"before recommending from memory" discipline, applied to phase inputs from other agents/work orders too
— an upstream work order's premise can be stale or optimistic by the time it reaches this planner.
Record the true status (Available / Design-locked-not-shipped / Blocked) explicitly, with the exact
task IDs (`D-30 ●`, `C-43 ○`) as evidence, not just a one-word label.

See [[ambient_logging_enrichment]] for the concrete case this surfaced in (the `WellKnownBaggageKeys`
retrofit, WO-042/P-261).

**Recurred, worse, on WO-043/P-270 (2026-07-16):** P-270's "Depends on: P-265, P-266, P-267" line
implied those phases were satisfiable prerequisites. Reading `08.Storage/state-map.md` directly showed
`SK.08.Design` at 0/15 `○` and `SK.08.Core` at 0/30 `○` — not merely "designed but not implemented"
(the WO-042 case) but **entirely unstarted**, only a dispatched task breakdown. Distinguish these two
severities when recording a Cross-Domain Dependencies row: "design ● / implementation ○" (still enables
locking your own Design task against the other domain's ratified contract, e.g. its `CLAUDE.md` prose)
versus "design ○ / implementation ○" (nothing ratified yet in that domain's own task tracking — but if
that domain's `CLAUDE.md` already documents a ratified signature in prose, as `08.Storage/CLAUDE.md` did
for `IFileStorage.CheckHealthAsync`, that prose is still a reliable enough source to lock your own design
task against, even while its state-map shows the task itself un-ticked). Either way, Core/Tests/Docs
implementation tasks that call the not-yet-existing type must be `⚑` Blocked, never optimistically `○`.

**Third occurrence, now a confirmed pattern, on WO-044/P-277 (2026-07-19):** P-277's "Depends on: P-272,
P-273, P-274" line again implied satisfiable prerequisites. Reading `09.Search/state-map.md` directly
showed `SK.09.Design` at 0/28 `○` and `SK.09.Scaffold`/`SK.09.Core` entirely unstarted — the same
"design ○ / implementation ○" severity as WO-043's 08.Storage case, not the milder WO-042 case. But
`09.Search/CLAUDE.md` had *already* ratified the exact `AddSearchReadinessCheck`/`WithSearchTelemetry`
contract in its own "Cross-domain work this design requires" section (a section other domains'
`CLAUDE.md` files apparently now proactively write for the express purpose of unblocking a downstream
domain's Design phase — treat this section, if present, as the first thing to check when a new domain's
brain is read for a cross-domain dependency). So D-08/D-09 were locked `●` immediately from that prose,
while C-37/C-38/T-33/DO-07 were recorded `⚑` Blocked. **Standing rule going forward, no longer a special
case:** always open the target domain's own `state-map.md` for the Design/Core/Scaffold phase-completion
counts (never trust the phase input's "Depends on" line at face value), and separately check whether that
domain's `CLAUDE.md` already contains a "Cross-domain work this design requires" (or equivalent
already-ratified prose) section addressed to this domain specifically — its presence is what makes
locking a Design task immediately legitimate even when the state-map shows 0% implemented.
