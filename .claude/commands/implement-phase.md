---
description: Implement the next (or a named) open phase of one capability domain with its phase-implementer agent
argument-hint: <domain> [phase]
---

You are the phase implementation launcher for Platform.SharedKernel. You pick one open phase from a domain's `state-map.md` and hand it to that domain's `{slug}-phase-implementer` agent. You never edit a file yourself.

**Input:**
$ARGUMENTS

> `<domain>` is required: a slug (`persistence`), a domain id (`06.Persistence`), a domain number (`06` or `6`), a domain name (`Persistence`) or its folder (`src/Infrastructure/Persistence`), case-insensitive. `[phase]` is optional: a phase key (`SK.06.BulkPurge`) or its name (`BulkPurge`, or the entry title). Without it the next actionable phase is chosen.

---

## Step 1 — Resolve the domain

| Slug | Domain | Folder | Implementer | Planner |
| --- | --- | --- | --- | --- |
| `governance` | `00.Governance` | `tools/Governance` | `governance-phase-implementer` | `governance-arch-planner` |
| `core` | `01.Core` | `src/Foundation` | `core-phase-implementer` | `core-arch-planner` |
| `caching` | `02.Caching` | `src/Infrastructure/Caching` | `caching-phase-implementer` | `caching-arch-planner` |
| `domain` | `03.Domain` | `src/Model/Domain` | `domain-phase-implementer` | `domain-arch-planner` |
| `contracts` | `04.Contracts` | `src/Model/Contracts` | `contracts-phase-implementer` | `contracts-arch-planner` |
| `application` | `05.Application` | `src/Application` | `application-phase-implementer` | `application-arch-planner` |
| `persistence` | `06.Persistence` | `src/Infrastructure/Persistence` | `persistence-phase-implementer` | `persistence-arch-planner` |
| `messaging` | `07.Messaging` | `src/Infrastructure/Messaging` | `messaging-phase-implementer` | `messaging-arch-planner` |
| `storage` | `08.Storage` | `src/Infrastructure/Storage` | `storage-phase-implementer` | `storage-arch-planner` |
| `search` | `09.Search` | `src/Infrastructure/Search` | `search-phase-implementer` | `search-arch-planner` |
| `intelligence` | `10.Intelligence` | `src/Infrastructure/AI` | `intelligence-phase-implementer` | `intelligence-arch-planner` |
| `communication` | `11.Communication` | `src/Infrastructure/Communication` | `communication-phase-implementer` | `communication-arch-planner` |
| `security` | `12.Security` | `src/Hosting/Security` | `security-phase-implementer` | `security-arch-planner` |
| `servicedefaults` | `13.ServiceDefaults` | `src/Hosting/ServiceDefaults` | `servicedefaults-phase-implementer` | `servicedefaults-arch-planner` |
| `presentation` | `14.Presentation` | `src/Hosting/Presentation` | `presentation-phase-implementer` | `presentation-arch-planner` |
| `integration` | `15.Integration` | `src/Infrastructure/Integration` | `integration-phase-implementer` | `integration-arch-planner` |
| `testing` | `16.Testing` | `src/Testing` | `testing-phase-implementer` | `testing-arch-planner` |
| `workflow` | `17.Workflows` | `src/Infrastructure/Workflows` | `workflow-phase-implementer` | `workflow-arch-planner` |
| `idempotency` | `18.Idempotency` | `src/Infrastructure/Idempotency` | `idempotency-phase-implementer` | `idempotency-arch-planner` |
| `scheduling` | `19.Scheduling` | `src/Infrastructure/Scheduling` | `scheduling-phase-implementer` | `scheduling-arch-planner` |
| `reporting` | `20.Reporting` | `src/Infrastructure/Reporting` | `reporting-phase-implementer` | `reporting-arch-planner` |

Aliases: `workflows` → `workflow`, `service-defaults` → `servicedefaults`, `ai` → `intelligence`.

If `<domain>` is missing or matches no row, output the slug list and stop:
```
implement-phase: unknown domain "{input}". Use one of: governance, core, caching, … reporting (or a folder such as 06.Persistence).
```

---

## Step 2 — Pick the phase

Read `{Folder}/state-map.md` in full. Open phases are the `### SK.{NN}.{Key} — {title} {state}` entries under `## Open Work`; closed ones are lines under `## Completed Phases` and `●`/`⊘` rows in `## Phase Key Registry`.

**With `[phase]`:** match the phase key exactly, else the key's name part or the entry title case-insensitively.
- Found under `## Completed Phases` or registry `●`/`⊘` → `Phase {key} is already complete in {Folder}. Nothing to do.` Stop.
- Not found anywhere → `Phase {input} not found in {Folder}/state-map.md. Open phases: {keys under ## Open Work, or "none"}.` Stop.
- Its state is `⚑` → report the matching `## Blocked` entry and stop.

**Without `[phase]`**, in order:
1. The first Open Work entry marked `◐` (resume).
2. Otherwise the first entry marked `○` whose `**Depends on:**` items are all done: a domain phase key is done when it is `●` in its domain's registry; a `P-NNN` is done when it is `●` on the root board or listed under root `## Completed Work Orders`. Skip entries whose dependencies are open and name them in the stop message if nothing else qualifies.
3. `⚑` entries are never auto-selected.

Stop conditions:
- `## Open Work` says none → `All phases complete in {Folder}. Nothing to implement.`
- Only blocked or dependency-waiting entries remain → list each with its blocker or open dependency and stop.
- The selected entry has no task table → `Phase {key} has no tasks yet. Run /dispatch-phase (or the {planner} agent) to plan it first.` Stop; never dispatch an empty phase.

---

## Step 3 — Build the phase brief

Copy verbatim from the domain state-map:
- The selected Open Work entry: heading, work-order line, goal, task table (every sub-table), acceptance criteria.
- The `## Blocked` entries and `## Cross-Domain Dependencies` rows that mention this phase key, or "None recorded."
- The `## Package Board` rows for the packages the tasks name.

Count the tasks: total, `●` done, `○`/`◐` remaining, `⚑` blocked. If every task is already `●` but the entry is still open, say so in the brief; the implementer then only closes the phase through `/state-map-phase`.

---

## Step 4 — Spawn the implementer

Use the Agent tool with `subagent_type` = the domain's implementer, in the foreground. Prompt:

```
Implement phase {key} of {Folder}.

Read .claude/agents/_common.md, then {Folder}/CLAUDE.md and {Folder}/state-map.md in full before writing code.
Follow the implementer execution order in _common.md: code, tests, build and test the affected lane,
PublicAPI.Unshipped.txt, README, /state-map-phase, CLAUDE.md sync, report.
{domain notes from the table below, if the domain has any}

{phase brief}

Tasks: {total} total, {done} done, {remaining} remaining, {blocked} blocked.
```

### Per-domain notes (add to the prompt verbatim)

| Domain | Note |
| --- | --- |
| `intelligence` | The domain `CLAUDE.md` separates ratified contract from candidate shapes; never implement an unratified shape. Verify on disk that `QdrantContainerFixture` (`SharedKernel.Testing.Internal`) and the doubles in `SharedKernel.AI.Testing` exist before building on them. Verify every third-party version (existence, latest stable, frameworks, licence, maintenance) before adding a `PackageReference`. Never call a paid or live model endpoint from the default test suite, and never assert on model-generated text. |
| `search` | Verify on disk that `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (`SharedKernel.Testing.Internal`) and the doubles in `SharedKernel.Search.Testing` exist before building on them; if one is absent, finish the container-free tasks and mark only the real-engine tasks `⚑`. |
| `workflow` | Verify every Temporalio SDK shape (id-reuse/id-conflict policy enums, `WorkflowOptions`/`ActivityOptions` required members, `ApplicationFailureException` constructor, interceptor and `IPayloadCodec` members, `Workflow.Patched`/`DeprecatePatch`, native RID list, the `JsonSerializerContext` seam) and the `ISymmetricEncryptionService` signatures against the compiled assembly or source, and record corrections in `src/Infrastructure/Workflows/CLAUDE.md`. No container fixture: `WorkflowEnvironment` (in-box in `Temporalio`) runs the dev server; never add Testcontainers or a separate `Temporalio.Testing` package; if the dev-server binary is unreachable, mark only real-environment tasks `⚑` with evidence. Determinism: workflow code is replay code — no clock, randomness, I/O, DI, configuration or static state inside a `[Workflow]` type (use `Workflow.UtcNow`, never `DateTimeOffset.UtcNow` or `IClock`); `IClock` stays mandatory in activities. Build against current source: Adapter tier, `TenantScope` from `SharedKernel.Execution` (never `Global` on a dispatch), `CommandActivity<>` sends through the kernel `ISender`, readiness is the internal `"workflows"` `IReadinessProbe`. |
| `testing` | For every fake or fixture of another domain's abstraction, read that domain's `CLAUDE.md` and the real interface first; never guess a signature. |

---

## Step 5 — Report

Relay the implementer's report unchanged, then one line:
```
implement-phase: {key} ({Folder}) handed to {implementer}.
```
If the Agent call fails, output the error; nothing was changed.

---

## Format contract

- Reads the domain `state-map.md` (and, for dependency checks, the root `state-map.md` or another domain's registry). Writes nothing; every write is the implementer's.
- One phase per run.
- Never selects a `⚑` phase, a phase with open dependencies, or a phase without tasks.
