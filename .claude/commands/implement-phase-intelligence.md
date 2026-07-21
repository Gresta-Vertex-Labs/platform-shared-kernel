You are the phase implementation launcher for the **10.Intelligence** capability domain of Platform.SharedKernel.

This command reads `10.Intelligence/state-map.md`, identifies the next actionable phase, and dispatches it to the `intelligence-phase-implementer` agent.

---

**Input:**
$ARGUMENTS

> Optional: a specific phase name or phase key to target (e.g. `Scaffold`, `SK.10.Core`).
> If omitted, the command auto-detects the next phase in sequence order.

---

## Step 1 — Read the domain state-map

Read `10.Intelligence/state-map.md` in full.

---

## Step 2 — Identify the target phase

### If `$ARGUMENTS` is provided
Parse the input as either:
- A phase key: `SK.10.{Phase}` — match directly against the `## Phase Key Registry` table.
- A phase name: `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, or `Published` — match case-insensitively.

Find the matching phase section. If every task under that phase key is `●` Complete, output:
```
Phase {name} is already complete in 10.Intelligence/state-map.md. Nothing to do.
```
Then stop.

### If `$ARGUMENTS` is empty — auto-detect
Scan the phase sections in this fixed sequence order:

```
1. Design      (SK.10.Design)
2. Scaffold    (SK.10.Scaffold)
3. Core        (SK.10.Core)
4. Tests       (SK.10.Tests)
5. Docs        (SK.10.Docs)
6. Published   (SK.10.Published)
```

**Priority 1:** Find the first phase containing at least one `◐` (in progress) task. This is a phase already started — resume it.

**Priority 2:** If none contain `◐`, find the first phase containing at least one `○` (not started) task where all prior phases in the sequence are fully `●` (complete). This is the next phase to begin.

**Stop condition — no tasks authored yet:** If the target phase section has **no task rows at all** (it still carries a `_No tasks defined yet._` placeholder, or its task table is empty), output:
```
implement-phase-intelligence: Phase {name} has no tasks defined in 10.Intelligence/state-map.md.
The 10.Intelligence domain has not been designed yet — there is nothing for the implementer to build.
Run /arch to raise a work order for this domain, then /dispatch-phase to route it to intelligence-arch-planner,
which populates the phase task tables.
```
Then stop — do **not** spawn the implementer. This domain starts life with an empty state-map template, so this is the expected first-run outcome, not an error.

**Stop condition:** If all phases are fully `●` Complete, output:
```
All phases complete in 10.Intelligence/state-map.md. The 10.Intelligence domain is fully implemented.
```
Then stop.

**Stop condition:** If the next `○` phase has a prior phase that is not yet fully `●` (i.e., a prior phase still has `○` or `◐` tasks), output:
```
Phase {name} cannot start yet — {prior-phase} must complete first.
Current state: {prior-phase} has {N} incomplete task(s).
```
Then stop.

---

## Step 3 — Extract the phase content

From the identified phase (e.g. `SK.10.Scaffold`), extract:
1. The phase key and name.
2. The phase description line (the `>` block at the top of the phase section).
3. The full task table: every row from the `| ID | Task | Package(s) | State |` tables for that phase, including all sub-pass groupings and their bold intro lines.
4. The number of incomplete tasks (State = `○` or `◐`) and blocked tasks (State = `⚑`).

Additionally extract, and pass through verbatim:
- The `## Blocked` section — it records verified-on-disk inbound blockers that the implementer must re-verify rather than rediscover mid-phase.
- The `## Cross-Domain Dependencies` rows whose `This Phase Key` matches the target phase.
- The `## Package Board` rows — this domain's package split may still carry an open Design decision, and the implementer must not invent one mid-phase.

If all tasks in the phase are already `●` Complete but the phase key has not been promoted to the root state-map, note this discrepancy and proceed — the implementer will call `state-map-phase` to fix it.

Build a phase brief:
```
## Phase: {Phase Name} ({Phase Key})

{phase description line}

### Tasks
{full task table, including sub-pass groupings}

### Package Board
{package board rows}

### Cross-Domain Dependencies for this phase
{matching rows}

### Known Blockers
{Blocked section content, or "None recorded."}

### Context
- Total tasks: {N}
- Completed: {N}
- Blocked: {N}
- Remaining: {N}
```

---

## Step 4 — Spawn the implementer agent

Use the Agent tool to spawn the `intelligence-phase-implementer` agent in **foreground** mode (wait for completion).

Pass this prompt:

```
Implement the following phase from 10.Intelligence/state-map.md.

{phase brief from Step 3}

Begin by reading 10.Intelligence/CLAUDE.md and 10.Intelligence/state-map.md in full before writing any code.
Note that 10.Intelligence/CLAUDE.md carries a Status section marking which parts of it are ratified contract
and which are still candidate shapes — respect that distinction and never implement an unratified shape.

Verify every cross-domain dependency directly on disk before building on it — in particular, confirm whether
16.Testing ships a container fixture for this domain's vector database(s) and any in-memory doubles for its
abstractions, rather than trusting prose that says it does or does not. If a fixture is genuinely absent,
implement every container-free task and mark only the real-backend tasks `⚑` Blocked. Never hand-roll a
competing ad-hoc container setup inside a .Tests project.

Verify every third-party version before writing a PackageReference — this domain's technology stack is
explicitly unpinned and unverified. Confirm the package exists, its latest stable version, target frameworks,
license, publisher, maintenance status, and transitive graph. Never call a paid or live model endpoint from
the default test suite, and never assert on model-generated text.

Work through every incomplete task in the phase. When all tasks are complete and tests pass, call
state-map-phase and sync-brain as instructed in your execution order.
```

---

## Step 5 — Report

After the implementer agent returns, output a one-line confirmation:
```
implement-phase-intelligence: {Phase Name} dispatched to intelligence-phase-implementer. See agent output above for results.
```

---

## Format Contract

- Reads `10.Intelligence/state-map.md` only (plus spawning the implementer agent).
- Never modifies any file directly — all writes are done by the `intelligence-phase-implementer` agent.
- Never skips phases out of sequence unless explicitly targeted via `$ARGUMENTS`.
- Never invents a phase — the six phase keys (`SK.10.Design` through `SK.10.Published`) are fixed.
- Never spawns the implementer against a phase with no authored tasks — that is an arch-planner gap, not an implementation gap.
- If the agent tool call fails, output the error and leave the state-map unchanged.
