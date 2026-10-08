# Common agent instructions

Shared instructions every agent in this folder reads first.

This file is not an agent. Every agent in `.claude/agents/` follows it; an agent's own file adds only what is specific to its role and domain. Where the two disagree, the root `CLAUDE.md` wins, then the domain `CLAUDE.md`, then the agent file, then this file.

---

## Repository facts

- **Platform.SharedKernel**: a .NET 10 mono-repo of NuGet packages (the shared kernel of a microservice ecosystem). No business logic lives here. The repository is **public**.
- Capability domains have ids `00.Governance` … `20.Reporting` and a slug (`persistence`). The root `CLAUDE.md` domain table is the only registry: id ↔ slug ↔ folder (for example `06.Persistence` ↔ `persistence` ↔ `src/Infrastructure/Persistence`). `{folder}` in these instructions means that folder; `{slug}` names the agent pair `{slug}-arch-planner` / `{slug}-phase-implementer`. Each has `CLAUDE.md` (maintainer rules), `README.md` (overview) and `state-map.md` (living board). Folder numbers are an address and an EventId block, **not** a dependency layer.
- Every production project declares a `<SharedKernelTier>` (Foundation, Model, Abstractions, Adapter, Host, Testing, Tooling); `eng/SharedKernelTiers.targets` fails the build (SKTIER000–006) on an illegal reference.
- All packages ship together at one MinVer version from a git tag. No `<Version>` in any `.csproj`.
- Sources of truth, read them rather than recalling them:

| Topic | File |
| --- | --- |
| Tiers, declared adapter edges, purity rules, conventions, domain map, "What Goes Where" | root `CLAUDE.md` |
| A domain's packages, entry points, invariants, decisions | `{folder}/CLAUDE.md` |
| What is built and what is open | `state-map.md` (root) and `{folder}/state-map.md` |
| Build, test lanes, CI, release, adding a package (for humans) | `CONTRIBUTING.md` |
| Build internals: props/targets, tier check, MinVer, CI workflows, run settings | `eng/README.md` |
| Package README shape | `docs/package-readme-standard.md` (enforced by `PackageReadmeStandardTests`) |
| Reference services | `samples/README.md`, `samples/OrderApi` |

History is not kept in the repository; `git log` is the record.

---

## Working rules for every agent

**Editing**
- Edit files with the Read/Edit/Write tools. Never round-trip a repo file through PowerShell `Get-Content`/`Set-Content` (it garbles UTF-8: `●`, `—`, `→`). For scripted edits use Bash tools or .NET with explicit UTF-8.
- Never commit, push, tag or open a PR unless the user explicitly asks. The `/commit` skill is the commit path.
- No absolute machine paths, user names, e-mail addresses or other personal data in any tracked file. Paths are repo-relative.
- Never write a secret, token or real connection string into a tracked file or a log.
- Stay inside your jurisdiction (the agent file names it). Work that belongs to another domain becomes a note under `## Cross-Domain Dependencies` or a report line for the caller, never an edit.
- Execute directly: no planning mode, no confirmation gate, unless the user asked for a plan.

**Code conventions (summary; the root `CLAUDE.md` "Conventions" section is authoritative)**
- Tiers: a reference is legal only if the tier matrix allows it or it is a declared Adapter → Adapter edge (`<SharedKernelAllowedAdapterReferences>`). Model/Abstractions take no third-party package beyond `Microsoft.Extensions.*.Abstractions`. ASP.NET Core only at Host/Testing. Testing-tier packages are never referenced by production code.
- Logging: only `[LoggerMessage]` source-generated partial methods, each with an explicit `EventId` from the domain block (`{NN} * 1000` … `+999`, registry `LoggingEventIdRanges`; the package's 100-wide sub-block is in the domain `CLAUDE.md`). PascalCase named placeholders. Never CorrelationId/TraceId/TenantId as placeholders. `Domain` and `Contracts` stay logging-free.
- Magic strings (`SK0022`): wire and multi-use identifiers are named constants; cross-package header/baggage/tag names live in `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys`; configuration sections come from `ISectionBoundOptions` + `AddValidatedOptions<TOptions>`.
- Expected failures are `Result`/`Result<T>` values with `Error` factories; a `Result` is never silently discarded (`SK0030`).
- AOT-clean when it costs nothing; never add `<IsAotCompatible>` to a project.
- Public members carry XML doc comments; concrete classes are `sealed` unless designed for inheritance; `CancellationToken` on every async method; no static mutable state.

**Packages and projects**
- Naming: `SharedKernel.{Capability}[.Abstractions|.{Provider}|.{Provider}.Core|.{Provider}.{Role}]`; fakes as `SharedKernel.{Capability}.Testing` **in the capability folder**, next to the contract they fake; tests nested as `{Project}/{Project}.Tests/`.
- **Who edits a `.Testing` double:** the capability's own implementer, in the same phase as the contract change it mirrors, following the double rules in `src/Testing/CLAUDE.md` (determinism, faithful failure modes, mandatory tenant scope, no test framework). The `testing` pair owns those rules, the catalogue, `SharedKernel.Testing` and `SharedKernel.Testing.Internal`.
- **MAX_PATH:** before scaffolding or renaming a package, check that every file of it, its `.Tests` project included, stays within 250 characters with the repo cloned at the reference root the root `CLAUDE.md` names (`bash eng/verify-path-lengths.sh` measures it; CI runs it too). Shorten the name if it does not. Build output goes to `artifacts/` and does not count.
- A new project goes into `Platform.SharedKernel.slnx` (solution folder = its capability folder), `Platform.SharedKernel.Unit.slnf` (every production project), its test project into exactly one lane filter, and — if packable — `Directory.Packages.props`. The `.csproj` carries `<SharedKernelTier>`, `<Description>`, `<PackageTags>`, a `README.md`, no `<Version>`, and `PackageReference`s without versions (Central Package Management).
- A new or changed public member goes into that project's `PublicAPI.Unshipped.txt`.

**Build and test**

| What | Command |
| --- | --- |
| Full build | `dotnet build Platform.SharedKernel.slnx -c Release` |
| Unit lane (no Docker) | `dotnet test Platform.SharedKernel.Unit.slnf -c Release` |
| Integration lane (Testcontainers, Docker required) | `dotnet test Platform.SharedKernel.Integration.slnf -c Release -s eng/testsettings/integration.runsettings` |
| One project | `dotnet test {folder}/{Project}/{Project}.Tests -c Release` |

- Run the test projects you touched, then the lane that contains them. Integration suites use the Testcontainers fixtures in `src/Testing/SharedKernel.Testing.Internal`; never hand-roll a container setup inside a `.Tests` project. If Docker is unavailable, say so in the report and mark only the container-backed tasks `⚑` with that evidence.
- `TreatWarningsAsErrors` is **per project**, not central: many projects opt in, others do not. Treat every warning you introduce as an error anyway; a project you touch must stay warning-free.
- Fix the implementation, not the test, unless the test is demonstrably wrong. Never report a phase done with a failing or skipped test you introduced.
- Verify third-party package versions, licences and target frameworks at the time of use; never add a package version from memory.

---

## The state-map protocol

Boards are **living**: they describe what exists and what is open. Completed work is removed, not collapsed; `git log` is the record.

**Domain `{folder}/state-map.md`**: headings exactly, in this order:

| Heading | Content |
| --- | --- |
| `## Legend` | `○` Not started · `◐` In progress · `●` Done · `⚑` Blocked · `⊘` Declined / superseded |
| `## Package Board` | `\| Package \| Tier \| Status \| Notes \|` — one row per package (planned ones `○`) |
| `## Phase Key Registry` | `\| Phase key \| Phase \| Status \|` — one row per **open** phase key `SK.{NN}.{PascalName}`, or `No open phase keys. …` |
| `## Open Work` | one full entry per open phase (format below), or `None — every phase in this domain is complete.` |
| `## Blocked` | each blocker with on-disk evidence and what it waits on, or `None.` |
| `## Cross-Domain Dependencies` | inbound/outbound obligations still open, or `None open.` |

Open Work entry:

```markdown
### SK.{NN}.{PascalName} — {title} `◐`

**Work order:** WO-NNN / P-NNN · **Depends on:** {None | SK.xx.Key, P-NNN}

{Goal: what the phase delivers and why, one paragraph.}

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | {design decision to ratify} | SharedKernel.X | `○` |
| C-01 | {implementation task} | SharedKernel.X | `○` |
| T-01 | {test task} | SharedKernel.X.Tests | `○` |

**Acceptance:**
- [ ] {verifiable criterion}
```

Task ID prefixes: `D` design, `S` scaffold, `C` core code, `T` tests, `DO` docs/README. IDs are scoped to the phase and start at `01`.

A new phase key must never reuse a closed one. Closed keys are not on the board, so check `git log --oneline -S"SK.{NN}.{PascalName}"` returns nothing before assigning it.

**Root `state-map.md`**: headings exactly: `## Legend`, `## ID Counters` (next `P-` and next `WO-` id), `## Domain Summary Board` (one row per domain, cells one sentence), `## Open Work`, `## Blocked`. No completed or changelog sections.

Root Open Work entry:

```markdown
### WO-NNN — {title}

{Intent and verdict (accept / upgrade), one paragraph.}

#### P-NNN — {capability}
**Status:** `○` Pending
**Domain:** {NN}.{Name}
**Depends on:** {None | P-NNN}
**Phase key:** —
{What is needed and why; acceptance criteria as `- [ ]` bullets. No file or class names.}
```

P-entry status: `○` Pending → `◐` Dispatched (planner wrote the domain phase; `**Phase key:**` filled in) → `●` Complete (domain phase done), or `⊘` Declined (planner or arch-lead, with a one-line reason). When every P-entry of a work order is `●` or `⊘`, the whole WO block is deleted. The root `## Legend` uses the same symbols as the domain boards.

**Who writes what**

| Writer | Writes |
| --- | --- |
| `arch-lead` | root `## Open Work` (new WO + P-entries), `## ID Counters` (advance after allocating); root `CLAUDE.md` when a rule, package or edge changes |
| `/dispatch-phase` | root P-entry `○` → `◐` and its `**Phase key:**`, after the planner returns |
| `{domain}-arch-planner` | its domain `## Open Work` entry, `## Phase Key Registry` row (`○`), planned `## Package Board` rows, `## Cross-Domain Dependencies`, `## Blocked` (with evidence) |
| `{domain}-phase-implementer` (through `/state-map-phase`) | task states; when the phase is done: removes the Open Work entry and its registry row, updates `## Package Board`; then on root: P-entry `●`, deletes a finished WO block, refreshes the domain's Summary Board row |
| `devops-lead` | build work that needs tracking goes into root `## Open Work` as a WO with Domain `eng` (next id from `## ID Counters`) |

Never rewrite another writer's section beyond what this table allows. Never add history back: outcomes go in the commit message.

---

## The CLAUDE.md protocol

Domain `CLAUDE.md` headings, in this order: `## Packages`, `## Public Entry Points`, `## Rules & Invariants`, `## Decisions`, `## Logging`, `## Cross-Domain Couplings`, `## Testing`, `## Known Limitations`. There is **no changelog section** in any `CLAUDE.md`.

- It describes the domain **as it is now**: a reference for the next maintainer, not a log. No phase numbers or dates in prose unless a decision needs its origin.
- Implementers update it in the same session whenever a package, public entry point, invariant, EventId sub-block, coupling, test rule or limitation changes. Surgical edits only; keep headings.
- Planners record ratified design decisions under `## Decisions` and new rules under `## Rules & Invariants`, marked *(planned, SK.{NN}.{Key})* until shipped. Never list an unshipped API under `## Public Entry Points`.
- A change that affects the whole repo (new package, tier move, declared adapter edge, purity rule, "What Goes Where" row) also goes into the root `CLAUDE.md`; only `arch-lead` and `/sync-brain` edit the root file.

---

## README protocol

- Every packable project has a `README.md` next to its `.csproj` (packing fails without it) following `docs/package-readme-standard.md`: fixed section order, absolute GitHub links, snippets that compile against the current public API, a Configuration table with full section paths, and never a link to `CLAUDE.md`, `state-map.md` or `docs/`.
- Update the package README in the same change as any public API, configuration key, error code, EventId or readiness-probe change. Where a `ReadmeSample*Tests.cs` exists, keep it in step with the snippets.
- The domain `README.md` keeps its package list and counts true.

---

## Agent file templates

Every domain agent file has the frontmatter `name`, `description` (one paragraph plus **two** `<example>` blocks), `model`, `color`, `memory: project`, then this body. Sections hold only what is specific to the domain; everything shared lives here.

**`{slug}-arch-planner`**
1. Opening paragraph: read `_common.md`, then `{folder}/CLAUDE.md` and `{folder}/state-map.md`; role (sub-agent of `arch-lead`, jurisdiction `{folder}/`, phase keys `SK.{NN}.*`); one expertise line.
2. `## Packages and where a proposal lands`: placement table (the proposal is… → it belongs in…), and what never goes into the Abstractions package.
3. `## Guardrails`: the checks a proposal must pass, citing rule numbers of `{folder}/CLAUDE.md`.
4. `## Decline patterns`: `Proposal | Why | Redirect`. Declines follow the Planner method below (no board entry).
5. `## Phase-design conventions`: contract-first, lane placement, test obligations, configuration, version pins, wire formats, README tasks — whatever applies to this domain.
6. `## Cross-domain couplings`: one line per coupled domain, naming the shared type or name.
7. Closing line: "Report in the `_common.md` format, with the phase key, task count by prefix, any decline and its rule, blockers and cross-domain notes."

**`{slug}-phase-implementer`**
1. Opening paragraph: read `_common.md`, then `{folder}/CLAUDE.md` and `{folder}/state-map.md`; role (`/implement-phase {slug} [phase]` hands over one phase; build exactly its tasks); "`{folder}/CLAUDE.md` is the law".
2. `## Jurisdiction`: what you may edit (`{folder}/`, including the capability's `.Testing` double), the `Package | Tier | Project | Test project (lane)` table, the tier edges you may use.
3. `## Implementation knowledge`: registration shape, pitfalls that have bitten this domain, logging sub-block.
4. `## Testing`: lanes, fixtures from `SharedKernel.Testing.Internal`, must-cover behaviours, contract tests.
5. `## Domain verification`: checks beyond the common build and test steps (consumer-verify harness, samples, integration lane).
6. Closing line: "Boards, brain, README and report follow `_common.md`. Domain deltas: …" (rule-numbering stability, Logging table, root `CLAUDE.md` changes → ask for `/sync-brain`).

---

## Planner method (`{domain}-arch-planner`)

1. **Read** the domain `CLAUDE.md` and `state-map.md` in full, and the root `CLAUDE.md` sections the request touches. Confirm the P-entry's intent.
2. **Analyse**: the capability requested; which package(s) it belongs in; the files that would be created, changed or deleted; dependencies on other phases or domains; risks. Check it against: the tier matrix and declared edges (no SKTIER error), the purity rules, the domain's invariants and hard violations, the logging sub-block, magic-string and options conventions, AOT, the MAX_PATH rule for any new package, and the licence/version of any new third-party dependency.
3. **Verdict**: accept, reshape, or decline with the rule it violates. A declined request writes no board entry: report the verdict and the rule, and when the ruling should stop the same request coming back, add a row to the domain `CLAUDE.md` `## Decisions` table.
4. **Design** the phase: a new phase key `SK.{NN}.{PascalName}`, goal, task table (D/S/C/T/DO), file-level plan where useful, acceptance criteria, and downstream obligations on other domains as notes only; never plan another domain's work.
5. **Write** the Open Work entry, registry row, planned Package Board rows, Cross-Domain Dependencies and Blocked (with evidence) into the domain `state-map.md`.
6. **Refresh** the domain `CLAUDE.md` per the protocol above.
7. **Stay in bounds**: no production code, no test projects, no files outside the domain folder, no root files.
8. **Report** the phase key, task count, blockers and cross-domain notes (the caller records the phase key on the root P-entry).

---

## Implementer execution order (`{domain}-phase-implementer`)

1. **Read** the domain `CLAUDE.md` → the domain `state-map.md` → the phase brief. Confirm the phase is not already `●`; pick up `◐` tasks where a previous session stopped.
2. **Verify on disk** every cross-domain type, fixture or SDK shape the phase builds on; do not trust prose. If something is genuinely absent, do the rest and mark only the dependent tasks `⚑` with evidence.
3. **Code**: implement exactly the phase's tasks, nothing beyond them; production quality, no TODOs or placeholders.
4. **Tests**: write or update tests in the nested `.Tests` projects (happy path, edge and failure paths named in the phase, DI registration).
5. **Build and test** the touched projects, then their lane; fix until green and warning-free.
6. **Public API**: record new or changed public members in `PublicAPI.Unshipped.txt`.
7. **README**: update every affected package README (and the domain README if packages or counts changed).
8. **State map**: run `/state-map-phase` for the finished tasks; it applies the state-map protocol on the domain board and the root.
9. **CLAUDE.md sync**: update the domain `CLAUDE.md` if packages, entry points, rules, logging, couplings, testing or limitations changed; ask for `/sync-brain` when the root `CLAUDE.md` is affected.
10. **Report** (format below).

---

## Agent memory

- Each agent may keep notes in `.claude/agent-memory/<agent-name>/` (e.g. `.claude/agent-memory/caching-phase-implementer/`). The folder is **local to each developer and gitignored**; never reference it from a tracked file.
- `MEMORY.md` in that folder is the index: one line per memory, `- [Title](file.md) — hook`, under ~150 characters. Each memory is its own file with frontmatter `name`, `description`, `type` (`user`, `feedback`, `project`, `reference`).
- Save: user preferences and corrections (with **Why:** and **How to apply:**), non-obvious project decisions and constraints with absolute dates, and pointers to external resources.
- Do not save: anything derivable from the code, `git log` or a `CLAUDE.md`; conventions, file paths, fix recipes; in-progress task state.
- A memory is a claim about the past. Before acting on one that names a file, type or flag, check it still exists; if memory and the repo disagree, trust the repo and fix or delete the memory.
- Update or remove wrong memories; never duplicate. Honour a user's request to remember, forget or ignore memory.

---

## Report format

End with a short, factual report to the caller (it is relayed to the user):

- **Outcome**: one line (done / partial / declined / blocked, and the phase key or WO).
- **Files**: created or modified, repo-relative, grouped by package.
- **Verification**: build and test results (`N passed, 0 failed` per project or lane), or why something could not run.
- **Boards and docs**: state-map, `CLAUDE.md` and README updates made, or skipped with a reason.
- **Open items**: blockers, cross-domain notes, decisions the user must make.

No code recaps and no narration.
