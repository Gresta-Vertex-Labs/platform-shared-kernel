You are the phase implementation launcher for the **11.Communication** capability domain of Platform.SharedKernel.

This command reads `11.Communication/state-map.md`, identifies the next actionable phase, and dispatches it to the `communication-phase-implementer` agent.

---

**Input:**
$ARGUMENTS

> Optional: a specific phase name or phase key to target (e.g. `Rest`, `SK.11.Grpc`).
> If omitted, the command auto-detects the next phase in sequence order.

---

## Step 1 — Read the domain state-map

Read `11.Communication/state-map.md` in full.

---

## Step 2 — Identify the target phase

### If `$ARGUMENTS` is provided
Parse the input as either:
- A phase key: `SK.11.{Phase}` — match directly against the `## Overall Progress` table.
- A phase name: `Design`, `Scaffold`, `Rest`, `Grpc`, `GraphQL`, `Internal`, `Tests`, `Docs`, or `Published` — match case-insensitively.

Find the matching row in `## Overall Progress`. If the row's State is `●` Complete, output:
```
Phase {name} is already complete in 11.Communication/state-map.md. Nothing to do.
```
Then stop.

### If `$ARGUMENTS` is empty — auto-detect
Scan the `## Overall Progress` table in this fixed sequence order:

```
1. Design      (SK.11.Design)
2. Scaffold    (SK.11.Scaffold)
3. Rest        (SK.11.Rest)
4. Grpc        (SK.11.Grpc)
5. GraphQL     (SK.11.GraphQL)
6. Internal    (SK.11.Internal)
7. Tests       (SK.11.Tests)
8. Docs        (SK.11.Docs)
9. Published   (SK.11.Published)
```

**Priority 1:** Find the first row with State = `◐` (in progress). This is a phase already started — resume it.

**Priority 2:** If none are `◐`, find the first row with State = `○` (not started) where all prior rows in the sequence are `●` (complete). This is the next phase to begin.

**Stop condition:** If all rows are `●` Complete, output:
```
All phases complete in 11.Communication/state-map.md. The 11.Communication domain is fully implemented.
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

From the identified phase (e.g. `SK.11.Rest`), extract:
1. The phase key and name.
2. The phase description line (the `>` block at the top of the phase section).
3. The full task table: every row from the `| ID | Task | Work Order | Package(s) | State |` table for that phase.
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

Use the Agent tool to spawn the `communication-phase-implementer` agent in **foreground** mode (wait for completion).

Pass this prompt:

```
Implement the following phase from 11.Communication/state-map.md.

{phase brief from Step 3}

Begin by reading 11.Communication/CLAUDE.md and 11.Communication/state-map.md in full before writing any code.
Work through every incomplete task in the phase. When all tasks are complete and tests pass, call state-map-phase and sync-brain as instructed in your execution order.
```

---

## Step 5 — Report

After the implementer agent returns, output a one-line confirmation:
```
implement-phase-communication: {Phase Name} dispatched to communication-phase-implementer. See agent output above for results.
```

---

## Format Contract

- Reads `11.Communication/state-map.md` only (plus spawning the implementer agent).
- Never modifies any file directly — all writes are done by the `communication-phase-implementer` agent.
- Never skips phases out of sequence unless explicitly targeted via `$ARGUMENTS`.
- If the agent tool call fails, output the error and leave the state-map unchanged.
