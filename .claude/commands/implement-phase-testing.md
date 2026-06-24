You are the phase implementation launcher for the **16.Testing** capability domain of Platform.SharedKernel.

This command reads `16.Testing/state-map.md`, identifies the next actionable phase, and dispatches it to the `testing-phase-implementer` agent.

---

**Input:**
$ARGUMENTS

> Optional: a specific phase name or phase key to target (e.g. `Scaffold`, `SK.16.Core`).
> If omitted, the command auto-detects the next phase in sequence order.

---

## Step 1 — Read the domain state-map

Read `16.Testing/state-map.md` in full.

---

## Step 2 — Identify the target phase

### If `$ARGUMENTS` is provided
Parse the input as either:
- A phase key: `SK.16.{Phase}` — match directly against the `## Overall Progress` table.
- A phase name: `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, or `Published` — match case-insensitively.

Find the matching row in `## Overall Progress`. If the row's State is `●` Complete, output:
```
Phase {name} is already complete in 16.Testing/state-map.md. Nothing to do.
```
Then stop.

### If `$ARGUMENTS` is empty — auto-detect
Scan the `## Overall Progress` table in this fixed sequence order:

```
1. Design      (SK.16.Design)
2. Scaffold    (SK.16.Scaffold)
3. Core        (SK.16.Core)
4. Tests       (SK.16.Tests)
5. Docs        (SK.16.Docs)
6. Published   (SK.16.Published)
```

**Priority 1:** Find the first row with State = `◐` (in progress). This is a phase already started — resume it.

**Priority 2:** If none are `◐`, find the first row with State = `○` (not started) where all prior rows in the sequence are `●` (complete). This is the next phase to begin.

**Stop condition:** If all rows are `●` Complete, output:
```
All phases complete in 16.Testing/state-map.md. The 16.Testing domain is fully implemented.
```
Then stop.

**Stop condition:** If the next `○` phase has a prior phase that is not yet `●` (i.e., a prior phase is still `○` or `◐`), output:
```
Phase {name} cannot start yet — {prior-phase} must complete first.
Current state: {prior-phase} is {state}.
```
Then stop.

**Stop condition:** If the target phase section has no task rows yet (still reads `_No tasks defined yet._`), output:
```
Phase {name} has no tasks defined yet in 16.Testing/state-map.md.
Run the testing-arch-planner agent first to populate this phase from a capability request.
```
Then stop.

---

## Step 3 — Extract the phase content

From the identified phase (e.g. `SK.16.Core`), extract:
1. The phase key and name.
2. The phase description line (the `>` block at the top of the phase section, if present).
3. The full task table: every row from the `| ID | Task | Package(s) | State |` table for that phase.
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

Use the Agent tool to spawn the `testing-phase-implementer` agent in **foreground** mode (wait for completion).

Pass this prompt:

```
Implement the following phase from 16.Testing/state-map.md.

{phase brief from Step 3}

Begin by reading 16.Testing/CLAUDE.md and 16.Testing/state-map.md in full before writing any code.
For every task that fakes or fixtures an abstraction owned by another domain, read that domain's CLAUDE.md first to get the exact interface signature — never guess it.
Work through every incomplete task in the phase. When all tasks are complete and build/regression verification passes, call state-map-phase and sync-brain as instructed in your execution order.
```

---

## Step 5 — Report

After the implementer agent returns, output a one-line confirmation:
```
implement-phase-testing: {Phase Name} dispatched to testing-phase-implementer. See agent output above for results.
```

---

## Format Contract

- Reads `16.Testing/state-map.md` only (plus spawning the implementer agent).
- Never modifies any file directly — all writes are done by the `testing-phase-implementer` agent.
- Never skips phases out of sequence unless explicitly targeted via `$ARGUMENTS`.
- If the agent tool call fails, output the error and leave the state-map unchanged.
