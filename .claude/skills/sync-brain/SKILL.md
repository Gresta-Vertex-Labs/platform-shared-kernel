---
name: sync-brain
description: Reconcile the CLAUDE.md files, README package lists, counts and badges, and the state-map boards with the code, which is the truth. Use after a change that added, moved or removed a package, edge, rule or entry point, or when docs look out of date.
disable-model-invocation: true
argument-hint: "[domain: NN | change summary]"
---

You are the keeper of the architectural brain for Platform.SharedKernel. The code is the truth; the `CLAUDE.md` files, READMEs and `state-map.md` boards describe it. You find where they have drifted and correct the descriptions. You never change code, project files or tests.

Read `.claude/agents/_common.md` ("The state-map protocol", "The CLAUDE.md protocol", "README protocol") before editing.

**Input:**
$ARGUMENTS

> - `domain: {NN | slug | folder}` → reconcile that domain, plus the root rows that mention it.
> - A free-text change summary (from an agent or the user) → reconcile the domains and root sections it names.
> - Empty → reconcile the whole repo.

There is no changelog file and no changelog section in any `CLAUDE.md`; do not create one.

---

## Step 1 — Inventory the code (the truth)

For each domain in scope, from disk:
- **Packages**: every `.csproj` under the folder that declares `<SharedKernelTier>` and is packable (not `*.Tests`, not `IsPackable=false`, not a consumer-verify harness, not `SharedKernel.Testing.Internal`). Record name, tier, `<SharedKernelAllowedAdapterReferences>`, and `ProjectReference`s.
- **Public surface**: `PublicAPI.Shipped.txt` + `PublicAPI.Unshipped.txt` (registration methods `Add…`/`Use…`/`Map…`, main interfaces).
- **Logging**: the `EventId` values in `[LoggerMessage]` attributes, against the domain block in `LoggingEventIdRanges`.
- **Readiness probes**: names passed to `AddReadinessProbe`/`IReadinessProbe` implementations.
- **Analyzer rules** (00.Governance only): the SK rule IDs the analyzers declare.

Use Glob/Grep; do not build unless the summary asks you to verify a claim that only a build can settle.

## Step 2 — Root `CLAUDE.md`

Check and correct, surgically:
- "Where Things Are": each folder's package count, scope line and tier letters; the `samples/` list against the `samples/` folder.
- "Tiers & Dependency Rules": the declared adapter edges list against every `<SharedKernelAllowedAdapterReferences>` in the repo; purity rules that name packages which no longer exist.
- "What Goes Where" and "Abstractions Packages": rows naming a package, type or registration method that no longer exists, and missing rows for a new package that introduces a placement rule.
- "Working in This Repo with Claude Code": the skill table against `.claude/skills/*/SKILL.md`, and the agent description against `.claude/agents/`.
- The domain table: every row's Slug matches an agent pair `{slug}-arch-planner.md` / `{slug}-phase-implementer.md` in `.claude/agents/`, and every Folder exists.

Keep headings, table columns, tone and density. Add a section only when a genuinely new cross-cutting concern has no home.

## Step 3 — Domain `CLAUDE.md`

For each domain in scope, the headings must be exactly: `## Packages`, `## Public Entry Points`, `## Rules & Invariants`, `## Decisions`, `## Logging`, `## Cross-Domain Couplings`, `## Testing`, `## Known Limitations`. Then:
- `## Packages` lists exactly the packages from Step 1 with their tiers.
- `## Public Entry Points` names only members that exist in the public API files; items marked *(planned, SK.xx.Key)* stay only while that phase is open.
- `## Logging` sub-blocks match the EventIds actually used.
- `## Cross-Domain Couplings` names only existing packages and edges.
- Rules or decisions contradicted by the code: correct them if the code is clearly the intended state; otherwise leave them and report the conflict.
- Strip any changelog or phase-history section, phase/WO ids or dates that have crept back in.
- `src/Testing/CLAUDE.md`: its catalogue lists every packable `SharedKernel.*.Testing` package.

## Step 4 — READMEs (lists, counts and badges only)

- Domain `README.md`: its package list matches Step 1; its `packages-N` badge counts the packable packages it owns, excluding the `.Testing` fakes (which count under `src/Testing`, whose badge counts every Testing-tier package). `PackageReadmeStandardTests` enforces this.
- Package `README.md`: the Tier badge matches the csproj tier; the title is the package id; registration methods and probe names quoted in Quick start / Reference exist. Structural problems against `docs/package-readme-standard.md` are reported, not rewritten here.
- Root `README.md`: its package badge and counts match the total; its analyzer-rule count matches the active rules (`AnalyzerReleases.Shipped.md` + `Unshipped.md`, minus removed).
- Every public README (package, domain, sample, root): no domain id such as `06.Persistence` outside code blocks — name the capability instead (enforced by `PackageReadmeStandardTests`).

## Step 5 — State-map boards

For each domain in scope:
- Headings exactly as in `_common.md`; no completed lists or changelogs (delete them if they crept back).
- `## Package Board` rows match Step 1 (name, tier); a package on disk without a row gets one; a row for a package that no longer exists is deleted.
- Every `## Phase Key Registry` row is open and has an `## Open Work` entry, and vice versa; closed rows are deleted.
- Root `## Domain Summary Board`: the domain's State matches its `## Open Work` (see `/state-map-phase` D5); every root P-entry marked `◐` names a phase key that exists on its domain board.

Only board-level fields are corrected here; task states are `/state-map-phase`'s.

## Step 6 — Report

```
## /sync-brain — {scope}

Fixed
- {file}: {what was wrong → what it says now}

Needs a decision
- {file}: {conflict between description and code, and the two options}

No drift
- {files checked with nothing to change}
```

---

## Format contract

- Writes only `CLAUDE.md` files, `README.md` package lists/counts/badges, and board-level `state-map.md` fields. Never code, project files, tests, `eng/` or CI files.
- Every edit is justified by something found on disk in Step 1; nothing from memory.
- Surgical edits: never rewrite a section that is correct, never change heading text or table columns.
