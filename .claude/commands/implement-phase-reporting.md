You are the phase implementation launcher for the **20.Reporting** capability domain of Platform.SharedKernel.

This command reads `20.Reporting/state-map.md`, identifies the next actionable phase, and dispatches it to the `reporting-phase-implementer` agent.

---

**Input:**
$ARGUMENTS

> Optional: a specific phase name or phase key to target (e.g. `Scaffold`, `SK.20.Core`).
> If omitted, the command auto-detects the next phase in sequence order.

---

## Step 1 — Read the domain state-map

Read `20.Reporting/state-map.md` in full.

---

## Step 2 — Identify the target phase

### If `$ARGUMENTS` is provided
Parse the input as either:
- A **phase key** — `SK.20.{Phase}`, matched exactly against the `Phase Key` column of the `## Overall Progress` table.
- A **phase name** — the `Phase` column value, matched case-insensitively.

Accept **any** phase key present in the table — never restrict the accepted values to the six
lifecycle phase names.

If no row matches, output:
```
Phase {input} not found in 20.Reporting/state-map.md.
Available phases: {comma-separated list of every Phase Key in the Overall Progress table}
```
Then stop.

If the matched row's State is `●` Complete, output:
```
Phase {name} is already complete in 20.Reporting/state-map.md. Nothing to do.
```
Then stop.

If the matched row's State is `⚑` Blocked, report the blocker from the domain's `## Blocked` section and stop.

### If `$ARGUMENTS` is empty — auto-detect

**Read every row of the `## Overall Progress` table, in the order they appear.** That table is the
single source of truth for which phases exist — never a hardcoded phase list. Domains accumulate
work-order/feature phase keys well beyond the canonical six (as of 2026-08-05: `00.Governance` has
38 rows, `02.Caching` 36, `07.Messaging` 28, `11.Communication` 9, `15.Integration` 7). A hardcoded
six-phase scan reports "All phases complete" while dozens of genuinely pending tasks sit in the
table — the exact defect this step was rewritten to fix.

For each row capture: **Phase Key**, **Phase** name, **Total**, **● Done**, **○ Pending**,
**⚑ Blocked** (present in some domains only), and **State**.

Classify each row by its phase-key suffix:
- **Lifecycle phase** — `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, `Published`. Strictly ordered
  relative to one another, in exactly that order.
- **Extension phase** — any other suffix (`SK.07.EnvelopeTenancy`, `SK.02.CacheWarmup`,
  `SK.00.MagicStringGuard`, …). These carry **no positional ordering**. Their prerequisites live in
  the domain's `## Pending Phases` / `## Cross-Domain Dependencies` / `## Blocked` sections and in
  the root `state-map.md` Phase Backlog's `Depends on` field — never in table position.

Select a target in this priority order:

**Priority 1 — resume.** The first row (table order) whose State is `◐` In progress.

**Priority 2 — start next.** If no row is `◐`, the first row whose State is `○` Not started, subject
to one guard:

- If it is a **lifecycle phase**, every *earlier lifecycle phase* must be `●` Complete (or `—` N/A).
  If one is not, stop and output:
  ```
  Phase {name} cannot start yet — {prior-phase} must complete first.
  Current state: {prior-phase} is {state}.
  ```
  Ignore extension phases entirely when evaluating this guard — an `○` extension phase never blocks
  a lifecycle phase, and vice versa.

- If it is an **extension phase**, the lifecycle-ordering guard does **not** apply. Instead check the
  domain's `## Blocked` and `## Cross-Domain Dependencies` sections for an unresolved entry naming
  this phase key. If one exists, output it and stop:
  ```
  Phase {name} is blocked — {blocker text from the domain state-map}.
  ```

Rows whose State is `⚑` Blocked are **never** auto-selected — skip them and keep scanning.

**Stop condition — nothing left.** If every row is `●` Complete (or `—` N/A), output:
```
All phases complete in 20.Reporting/state-map.md. The 20.Reporting domain is fully implemented.
```
Then stop.

**Stop condition — only blocked work remains.** If the only non-complete rows are `⚑` Blocked, output:
```
No actionable phase in 20.Reporting/state-map.md — {N} phase(s) remain, all ⚑ Blocked:
  {phase key} — {blocker from the domain's ## Blocked section}
```
Then stop.

**Stop condition — phase not yet populated.** If the selected phase's task table is empty or still
reads `_No tasks defined yet._`, output:
```
Phase {name} has no tasks defined yet in 20.Reporting/state-map.md.
Run the reporting-arch-planner agent first to populate this phase from a capability request.
```
Then stop — do not dispatch the implementer against an empty phase.

---

## Step 3 — Extract the phase content

> **Locate the phase section by its `<!-- phase-key: {key} -->` HTML comment marker, not by heading text.**
> Heading formats vary across domains (`## Phase: Core <!-- … -->` vs `## Phase SK.00.MagicStringGuard — Governance: … <!-- … -->`);
> the marker is the one reliable anchor. Task-table column sets vary too — `| ID | Task | Work Order | Package(s) | State |`,
> `| ID | Task | Maps to | Package(s) | State |`, and `| ID | Task | Package(s) | State |` all occur — so read whatever columns
> that phase's own table declares. Only `ID`, `Task`, and `State` are guaranteed present.

From the identified phase (e.g. `SK.20.Scaffold`), extract:
1. The phase key and name.
2. The phase description line (the `>` block at the top of the phase section).
3. The full task table: every row from the `| ID | Task | Package(s) | State |` table for that phase.
4. The number of incomplete tasks (State = `○` or `◐`).

If all tasks in the phase are already `●` Complete (Done count = Total), but the phase key's State in `## Overall Progress` is not yet `●`, note this discrepancy and proceed — the implementer will call `state-map-phase` to fix it.

Build a phase brief:
```
## Phase: {Phase Name} ({Phase Key})

{phase description line}

### Tasks
{full task table}

### Context
- Total tasks: {N}
- Completed: {N}
- Remaining: {N}
```

---

## Step 4 — Spawn the implementer agent

Use the Agent tool to spawn the `reporting-phase-implementer` agent in **foreground** mode (wait for completion).

Pass this prompt:

```
Implement the following phase from 20.Reporting/state-map.md.

{phase brief from Step 3}

Begin by reading 20.Reporting/CLAUDE.md and 20.Reporting/state-map.md in full before writing any code.
Work through every incomplete task in the phase. When all tasks are complete and tests pass, call state-map-phase and sync-brain as instructed in your execution order.
```

---

## Step 5 — Report

After the implementer agent returns, output a one-line confirmation:
```
implement-phase-reporting: {Phase Name} dispatched to reporting-phase-implementer. See agent output above for results.
```

---

## Format Contract

- Reads `20.Reporting/state-map.md` only (plus spawning the implementer agent).
- Never modifies any file directly — all writes are done by the `reporting-phase-implementer` agent.
- Never skips phases out of sequence unless explicitly targeted via `$ARGUMENTS`.
- If the agent tool call fails, output the error and leave the state-map unchanged.
