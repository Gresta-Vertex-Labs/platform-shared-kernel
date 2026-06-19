You are the phase implementation launcher for the **13.ServiceDefaults** capability domain of Platform.SharedKernel.

This command reads `13.ServiceDefaults/state-map.md`, identifies the next actionable phase, and dispatches it to the `servicedefaults-phase-implementer` agent.

---

**Input:**
$ARGUMENTS

> Optional: a specific phase name or phase key to target (e.g. `Scaffold`, `SK.13.Core`).
> If omitted, the command auto-detects the next phase in sequence order.

---

## Step 1 — Read the domain state-map

Read `13.ServiceDefaults/state-map.md` in full.

---

## Step 2 — Identify the target phase

### If `$ARGUMENTS` is provided
Parse the input as either:
- A phase key: `SK.13.{Phase}` — match directly against the `## Overall Progress` table.
- A phase name: `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, or `Published` — match case-insensitively.

Find the matching row in `## Overall Progress`. If the row's State is `●` Complete, output:
```
Phase {name} is already complete in 13.ServiceDefaults/state-map.md. Nothing to do.
```
Then stop.

### If `$ARGUMENTS` is empty — auto-detect
Scan the `## Overall Progress` table in this fixed sequence order:

```
1. Design      (SK.13.Design)
2. Scaffold    (SK.13.Scaffold)
3. Core        (SK.13.Core)
4. Tests       (SK.13.Tests)
5. Docs        (SK.13.Docs)
6. Published   (SK.13.Published)
```

**Priority 1:** Find the first row with State = `◐` (in progress). This is a phase already started — resume it.

**Priority 2:** If none are `◐`, find the first row with State = `○` (not started) where all prior rows in the sequence are `●` (complete). This is the next phase to begin.

**Stop condition:** If all rows are `●` Complete, output:
```
All phases complete in 13.ServiceDefaults/state-map.md. The 13.ServiceDefaults domain is fully implemented.
```
Then stop.

**Stop condition:** If the next `○` phase has a prior phase that is not yet `●` (i.e., a prior phase is still `○` or `◐`), output:
```
Phase {name} cannot start yet — {prior-phase} must complete first.
Current state: {prior-phase} is {state}.
```
Then stop.

**Stop condition:** If the target phase's task table contains only the `_No tasks defined yet._` placeholder row, output:
```
Phase {name} has no tasks defined yet in 13.ServiceDefaults/state-map.md.
Run the servicedefaults-arch-planner agent first to design and populate this phase.
```
Then stop — do not dispatch the implementer against an empty phase.

---

## Step 3 — Extract the phase content

From the identified phase (e.g. `SK.13.Scaffold`), extract:
1. The phase key and name.
2. The phase description line (the `>` block at the top of the phase section), if present.
3. The full task table: every row from the `| ID | Task | Work Order | Package(s) | State |` table for that phase.
4. The number of incomplete tasks (State = `○` or `◐`).

If all tasks in the phase are already `●` Complete (Done count = Total), but the phase key's State in `## Overall Progress` is not yet `●`, note this discrepancy and proceed — the implementer will call `state-map-phase` to fix it.

Build a phase brief:
```
## Phase: {Phase Name} ({Phase Key})

{phase description line, if present}

### Tasks
{full task table}

### Context
- Total tasks: {N}
- Completed: {N}
- Remaining: {N}
```

---

## Step 4 — Spawn the implementer agent

Use the Agent tool to spawn the `servicedefaults-phase-implementer` agent in **foreground** mode (wait for completion).

Pass this prompt:

```
Implement the following phase from 13.ServiceDefaults/state-map.md.

{phase brief from Step 3}

Begin by reading 13.ServiceDefaults/CLAUDE.md and 13.ServiceDefaults/state-map.md in full before writing any code.
Work through every incomplete task in the phase. When all tasks are complete and tests pass, call state-map-phase and sync-brain as instructed in your execution order.
```

---

## Step 5 — Report

After the implementer agent returns, output a one-line confirmation:
```
implement-phase-servicedefaults: {Phase Name} dispatched to servicedefaults-phase-implementer. See agent output above for results.
```

---

## Format Contract

- Reads `13.ServiceDefaults/state-map.md` only (plus spawning the implementer agent).
- Never modifies any file directly — all writes are done by the `servicedefaults-phase-implementer` agent.
- Never skips phases out of sequence unless explicitly targeted via `$ARGUMENTS`.
- Never dispatches against a phase whose task table is still the empty `_No tasks defined yet._` placeholder — that phase must be planned by `servicedefaults-arch-planner` first.
- If the agent tool call fails, output the error and leave the state-map unchanged.
