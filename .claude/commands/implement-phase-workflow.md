You are the phase implementation launcher for the **17.Workflows** capability domain of Platform.SharedKernel.

This command reads `17.Workflows/state-map.md`, identifies the next actionable phase, and dispatches it to the `workflow-phase-implementer` agent.

---

**Input:**
$ARGUMENTS

> Optional: a specific phase name or phase key to target (e.g. `Scaffold`, `SK.17.Core`).
> If omitted, the command auto-detects the next phase in sequence order.

---

## Step 1 — Read the domain state-map

Read `17.Workflows/state-map.md` in full.

---

## Step 2 — Identify the target phase

### If `$ARGUMENTS` is provided
Parse the input as either:
- A phase key: `SK.17.{Phase}` — match directly against the `## Phase Key Registry` table.
- A phase name: `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, or `Published` — match case-insensitively.

Find the matching phase section. If every task under that phase key is `●` Complete, output:
```
Phase {name} is already complete in 17.Workflows/state-map.md. Nothing to do.
```
Then stop.

### If `$ARGUMENTS` is empty — auto-detect
Scan the phase sections in this fixed sequence order:

```
1. Design      (SK.17.Design)
2. Scaffold    (SK.17.Scaffold)
3. Core        (SK.17.Core)
4. Tests       (SK.17.Tests)
5. Docs        (SK.17.Docs)
6. Published   (SK.17.Published)
```

**Priority 1:** Find the first phase containing at least one `◐` (in progress) task. This is a phase already started — resume it.

**Priority 2:** If none contain `◐`, find the first phase containing at least one `○` (not started) task where all prior phases in the sequence are fully `●` (complete). This is the next phase to begin.

**Stop condition:** If all phases are fully `●` Complete, output:
```
All phases complete in 17.Workflows/state-map.md. The 17.Workflows domain is fully implemented.
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

From the identified phase (e.g. `SK.17.Scaffold`), extract:
1. The phase key and name.
2. The phase description line (the `>` block at the top of the phase section).
3. The full task table: every row from the `| ID | Task | Package(s) | State |` tables for that phase, including all sub-pass groupings and their bold intro lines.
4. The number of incomplete tasks (State = `○` or `◐`) and blocked tasks (State = `⚑`).

Additionally extract, and pass through verbatim:
- The `## Blocked` section — it records why this domain deliberately carries **no** `16.Testing` container-fixture blocker (`Temporalio.Testing`'s `WorkflowEnvironment` manages the Temporal dev-server binary itself), and any blocker discovered since. The implementer must re-verify this rather than rediscover it mid-phase.
- The `## Cross-Domain Dependencies` rows whose `This Phase Key` matches the target phase — including the rows flagged **unconfirmed** (`ISymmetricEncryptionService`'s exact API shape, and the `04.Contracts` reference question), which the implementer must resolve on disk rather than assume.

If all tasks in the phase are already `●` Complete but the phase key has not been promoted to the root state-map, note this discrepancy and proceed — the implementer will call `state-map-phase` to fix it.

Build a phase brief:
```
## Phase: {Phase Name} ({Phase Key})

{phase description line}

### Tasks
{full task table, including sub-pass groupings}

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

Use the Agent tool to spawn the `workflow-phase-implementer` agent in **foreground** mode (wait for completion).

Pass this prompt:

```
Implement the following phase from 17.Workflows/state-map.md.

{phase brief from Step 3}

Begin by reading 17.Workflows/CLAUDE.md and 17.Workflows/state-map.md in full before writing any code.

Verify every unconfirmed SDK and cross-domain API shape directly against the real compiled assembly or
real source before building on it — do not trust CLAUDE.md prose, this prompt, or SDK documentation.
In particular: the Temporalio id-reuse/id-conflict policy enum names and member sets,
WorkflowOptions/ActivityOptions required members, ApplicationFailureException's constructor parameter
order and nonRetryable/errorType parameter names, IClientInterceptor/IWorkerInterceptor and
IPayloadCodec member shapes, Workflow.Patched/DeprecatePatch naming, the native-core RID list, the
JsonSerializerContext seam for trimmed consumers, and 01.Core's ISymmetricEncryptionService signatures
and key-version shape. Record every correction back into 17.Workflows/CLAUDE.md so a later phase does
not re-derive it.

Note that this domain deliberately needs no 16.Testing container fixture — Temporalio.Testing is in-box
in the Temporalio package and WorkflowEnvironment manages the Temporal dev-server binary itself. Never
add Testcontainers or a separate Temporalio.Testing package reference. If the dev-server binary is
genuinely unreachable on this machine, mark only the real-environment tasks `⚑` Blocked with the
evidence recorded in the Blocked section — never hand-roll a substitute.

The determinism rule governs everything: workflow code is replay code. If a phase item would require a
clock, a random source, I/O, DI, configuration, static state, or a thread-pool escape inside a
[Workflow] type, stop and flag it — it belongs in an activity. Remember the deliberate SK0001
inversion: inside a workflow, DateTimeOffset.UtcNow AND an injected IClock are both wrong
(Workflow.UtcNow is the only correct clock), while IClock stays mandatory inside activities.

Work through every incomplete task in the phase. When all tasks are complete and tests pass, call
state-map-phase and sync-brain as instructed in your execution order.
```

---

## Step 5 — Report

After the implementer agent returns, output a one-line confirmation:
```
implement-phase-workflow: {Phase Name} dispatched to workflow-phase-implementer. See agent output above for results.
```

---

## Format Contract

- Reads `17.Workflows/state-map.md` only (plus spawning the implementer agent).
- Never modifies any file directly — all writes are done by the `workflow-phase-implementer` agent.
- Never skips phases out of sequence unless explicitly targeted via `$ARGUMENTS`.
- Never invents a phase — the six phase keys (`SK.17.Design` through `SK.17.Published`) are fixed.
- If the agent tool call fails, output the error and leave the state-map unchanged.
