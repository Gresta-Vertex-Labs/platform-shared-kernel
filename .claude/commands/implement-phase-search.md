You are the phase implementation launcher for the **09.Search** capability domain of Platform.SharedKernel.

This command reads `09.Search/state-map.md`, identifies the next actionable phase, and dispatches it to the `search-phase-implementer` agent.

---

**Input:**
$ARGUMENTS

> Optional: a specific phase name or phase key to target (e.g. `Scaffold`, `SK.09.Core`).
> If omitted, the command auto-detects the next phase in sequence order.

---

## Step 1 — Read the domain state-map

Read `09.Search/state-map.md` in full.

---

## Step 2 — Identify the target phase

### If `$ARGUMENTS` is provided
Parse the input as either:
- A phase key: `SK.09.{Phase}` — match directly against the `## Phase Key Registry` table.
- A phase name: `Design`, `Scaffold`, `Core`, `Tests`, `Docs`, or `Published` — match case-insensitively.

Find the matching phase section. If every task under that phase key is `●` Complete, output:
```
Phase {name} is already complete in 09.Search/state-map.md. Nothing to do.
```
Then stop.

### If `$ARGUMENTS` is empty — auto-detect
Scan the phase sections in this fixed sequence order:

```
1. Design      (SK.09.Design)
2. Scaffold    (SK.09.Scaffold)
3. Core        (SK.09.Core)
4. Tests       (SK.09.Tests)
5. Docs        (SK.09.Docs)
6. Published   (SK.09.Published)
```

**Priority 1:** Find the first phase containing at least one `◐` (in progress) task. This is a phase already started — resume it.

**Priority 2:** If none contain `◐`, find the first phase containing at least one `○` (not started) task where all prior phases in the sequence are fully `●` (complete). This is the next phase to begin.

**Stop condition:** If all phases are fully `●` Complete, output:
```
All phases complete in 09.Search/state-map.md. The 09.Search domain is fully implemented.
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

From the identified phase (e.g. `SK.09.Scaffold`), extract:
1. The phase key and name.
2. The phase description line (the `>` block at the top of the phase section).
3. The full task table: every row from the `| ID | Task | Package(s) | State |` tables for that phase, including all sub-pass groupings and their bold intro lines.
4. The number of incomplete tasks (State = `○` or `◐`) and blocked tasks (State = `⚑`).

Additionally extract, and pass through verbatim:
- The `## Blocked` section — it records verified-on-disk inbound blockers (notably that `16.Testing` ships no Meilisearch or Elasticsearch container fixture) that the implementer must re-verify rather than rediscover mid-phase.
- The `## Cross-Domain Dependencies` rows whose `This Phase Key` matches the target phase.

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

Use the Agent tool to spawn the `search-phase-implementer` agent in **foreground** mode (wait for completion).

Pass this prompt:

```
Implement the following phase from 09.Search/state-map.md.

{phase brief from Step 3}

Begin by reading 09.Search/CLAUDE.md and 09.Search/state-map.md in full before writing any code.

Verify every cross-domain dependency directly on disk before building on it — in particular, confirm
whether 16.Testing's MeilisearchContainerFixture and ElasticsearchContainerFixture exist rather than
trusting any prose that says they do or do not. If a fixture is genuinely absent, implement every
container-free task and mark only the real-backend tasks `⚑` Blocked. Never hand-roll a competing
ad-hoc container setup inside a .Tests project.

Work through every incomplete task in the phase. When all tasks are complete and tests pass, call
state-map-phase and sync-brain as instructed in your execution order.
```

---

## Step 5 — Report

After the implementer agent returns, output a one-line confirmation:
```
implement-phase-search: {Phase Name} dispatched to search-phase-implementer. See agent output above for results.
```

---

## Format Contract

- Reads `09.Search/state-map.md` only (plus spawning the implementer agent).
- Never modifies any file directly — all writes are done by the `search-phase-implementer` agent.
- Never skips phases out of sequence unless explicitly targeted via `$ARGUMENTS`.
- Never invents a phase — the six phase keys (`SK.09.Design` through `SK.09.Published`) are fixed.
- If the agent tool call fails, output the error and leave the state-map unchanged.
