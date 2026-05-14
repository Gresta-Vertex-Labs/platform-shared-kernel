You are the phase implementation launcher for the **02.Caching** capability domain of Platform.SharedKernel.

This command reads `02.Caching/state-map.md`, identifies the next actionable phase, and dispatches it to the `caching-phase-implementer` agent.

---

**Input:**
$ARGUMENTS

> Optional: a specific phase name or phase key to target (e.g. `Scaffold`, `SK.02.Core`).
> If omitted, the command auto-detects the next phase in sequence order.

---

## Step 1 — Read the domain state-map

Read `02.Caching/state-map.md` in full.

---

## Step 2 — Identify the target phase

### If `$ARGUMENTS` is provided
Parse the input as either:
- A phase key: `SK.02.{Phase}` — match directly against the `## Overall Progress` table.
- A phase name: `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, or `Published` — match case-insensitively.

Find the matching row in `## Overall Progress`. If the row's State is `●` Complete, output:
```
Phase {name} is already complete in 02.Caching/state-map.md. Nothing to do.
```
Then stop.

### If `$ARGUMENTS` is empty — auto-detect
Scan the `## Overall Progress` table in this fixed sequence order:

```
1. Design      (SK.02.Design)
2. Scaffold    (SK.02.Scaffold)
3. Core        (SK.02.Core)
4. Tests       (SK.02.Tests)
5. Docs        (SK.02.Docs)
6. Published   (SK.02.Published)
```

**Priority 1:** Find the first row with State = `◐` (in progress). This is a phase already started — resume it.

**Priority 2:** If none are `◐`, find the first row with State = `○` (not started) where all prior rows in the sequence are `●` (complete). This is the next phase to begin.

**Stop condition:** If all rows are `●` Complete, output:
```
All phases complete in 02.Caching/state-map.md. The 02.Caching domain is fully implemented.
```
Then stop.

**Stop condition:** If the next `○` phase has a prior phase that is not yet `●` (i.e., a prior phase is still `○` or `◐`), output:
```
Phase {name} cannot start yet — {prior-phase} must complete first.
Current state: {prior-phase} is {state}.
```
Then stop.

---

## Step 3 — Extract the phase content

From the identified phase (e.g. `SK.02.Scaffold`), extract:
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

Use the Agent tool to spawn the `caching-phase-implementer` agent in **foreground** mode (wait for completion).

Pass this prompt:

```
Implement the following phase from 02.Caching/state-map.md.

{phase brief from Step 3}

Begin by reading 02.Caching/CLAUDE.md and 02.Caching/state-map.md in full before writing any code.
Work through every incomplete task in the phase. When all tasks are complete and tests pass, call state-map-phase and sync-brain as instructed in your execution order.
```

---

## Step 5 — Report

After the implementer agent returns, output a one-line confirmation:
```
implement-phase-caching: {Phase Name} dispatched to caching-phase-implementer. See agent output above for results.
```

---

## Format Contract

- Reads `02.Caching/state-map.md` only (plus spawning the implementer agent).
- Never modifies any file directly — all writes are done by the `caching-phase-implementer` agent.
- Never skips phases out of sequence unless explicitly targeted via `$ARGUMENTS`.
- If the agent tool call fails, output the error and leave the state-map unchanged.
