# Proposal: the SharedKernel consumer skill

> Input for `/arch`. It describes **only the platform-shared-kernel side**. The service template
> (`platform-service-template`) is separate work that follows the first release that ships this skill.

## 1. Problem

A service built on SharedKernel is mostly written with an AI assistant. With 104 packages, the assistant
cannot know which package solves which need, which service project references it, or how to wire it.
It then hand-rolls infrastructure the kernel already provides, picks the wrong package, or references
it from the wrong project.

What already exists:

- **Package READMEs are consumer-facing and AI-aware.** `docs/package-readme-standard.md` fixes their
  shape (callout, Install, Quick start, Configuration, Reference, Testing, Pitfalls), and rule 8 says
  they are written for assistants. `PackageReadmeStandardTests` enforces the mechanical parts.
- **Every README ships inside its NuGet package** (`PackageReadmeFile`, `Directory.Build.props`;
  SKPKG003 fails a packable project without one). A service that restored version X has the exact
  README for X at `~/.nuget/packages/<id lowercased>/<X>/README.md`.
- **`eng/generate-package-index.cs`** already reads every packable project's tier and writes
  `docs/packages.md`. `verify.yml` runs it with `--check`.

What is missing is the layer above the READMEs:

1. **Routing:** "I need X" → which package(s), which service project, which entry point.
2. **Recipes:** tasks that span several packages ("publish an integration event through the outbox,
   consume it idempotently"). Assistants get these wrong even when each README is clear.

The root `CLAUDE.md` "What Goes Where" table does routing, but for maintainers. It names domain ids,
internal files and maintainer rules, and it is not shipped to consumers.

## 2. Goal

Ship a **Claude Code skill** named `sharedkernel` with every release, at the same version as the
packages. A consuming service syncs it into `.claude/skills/sharedkernel/`, and its assistant then:

- loads the skill whenever an infrastructure need comes up
- finds the package and the service project in the index
- follows a recipe for multi-package tasks
- reads the package README from the NuGet cache at the service's pinned version for the exact API

### Design principles

| Principle | Why |
| --- | --- |
| **Three layers: index → recipes → READMEs** | The assistant loads only what the task needs. One big file costs tens of thousands of tokens per session and dilutes attention. |
| **READMEs stay the only source of API detail** | The skill never copies configuration tables or API listings. Copies drift; the README is already versioned with the code and test-enforced. |
| **Version-locked to the packages** | A service on 1.2.0 must get 1.2.0 guidance. The skill is built from the tagged commit and attached to that release. |
| **Generated where data exists, hand-written where judgment is needed** | The package catalog comes from csproj files and README callouts. The needs table and recipes are judgment. |
| **Checked in CI like every other generated view** | A new package that the index does not route to is invisible to assistants. CI must catch it. |
| **Consumer vocabulary only** | No domain ids, `CLAUDE.md`, `state-map.md`, phase keys or internal type names. This is the same rule as README standard rule 7. |
| **Guidance, not enforcement** | The analyzers and the service's architecture tests are the guardrail. The skill steers; the build catches. |

## 3. Deliverables

### 3.1 Folder layout

A new top-level folder, `consumer/`, deliberately outside `docs/` (maintainer material):

```
consumer/
├─ README.md                         maintainer notes: what this folder is, how to change it, how it ships
├─ skill/
│  └─ sharedkernel/                  the folder that ships, exactly as a service receives it
│     ├─ SKILL.md                    the index (hand-written + one generated region)
│     └─ recipes/
│        ├─ new-service.md
│        ├─ add-persistence.md
│        ├─ add-http-endpoint.md
│        ├─ add-messaging.md
│        ├─ add-caching.md
│        ├─ add-authentication.md
│        ├─ add-multi-tenancy.md
│        ├─ add-idempotency.md
│        ├─ add-background-job.md
│        ├─ add-long-running-workflow.md
│        ├─ add-outbound-call.md
│        ├─ add-file-storage.md
│        ├─ add-search.md
│        ├─ add-ai.md
│        ├─ add-notifications-webhooks.md
│        ├─ add-report-export.md
│        └─ write-tests.md
└─ eval/
   └─ tasks.md                       the evaluation prompt set (section 3.6)
```

The recipe list is a starting set. Merge or split recipes as writing them shows. Check every new path
against the MAX_PATH rule (`eng/verify-path-lengths.sh`).

### 3.2 `SKILL.md`: the index

Target size: **200–300 lines.** It is loaded every time the skill triggers, so every line costs context.

**Frontmatter.** The `description` decides when Claude loads the skill, so it must name the needs
broadly:

```markdown
---
name: sharedkernel
description: Use for any infrastructure or cross-cutting need in a service built on Platform.SharedKernel: persistence (PostgreSQL), messaging and integration events, caching, idempotency, HTTP/gRPC/SignalR/GraphQL endpoints, authentication and authorization, multi-tenancy, outbound REST/gRPC calls, background and scheduled jobs, durable workflows, file storage, search, AI/embeddings, email/SMS/webhooks, report export, logging, telemetry, health checks, and unit/integration tests. Tells you which package to use, which project references it, and how to wire it.
---
```

**Sections, in this order:**

1. **Version and detail lookup.** The version is `{{SharedKernelVersion}}`, stamped at release
   (section 3.5). Before writing code against a package, read its README at
   `~/.nuget/packages/<package id lowercased>/<version>/README.md` (or under `NUGET_PACKAGES` when
   set). Run `dotnet restore` if it is missing. The fallback is the README on GitHub at tag
   `v<version>`.
2. **Non-negotiables (about 30 lines).** The rules a consumer breaks most often:
   - The four-project shape and what each project references: Domain → `SharedKernel.Domain`;
     Application → `SharedKernel.Application` (+ `.Abstractions` packages); Infrastructure → adapters;
     Api/Worker → Host packages.
   - Expected failures return `Result`/`Result<T>`, not exceptions.
   - Logging is `[LoggerMessage]` only.
   - Use cases go through `ISender`; never reference MediatR directly.
   - Caller, tenant and correlation come from `IRequestContext`; tenant values are `TenantId`, never a
     string or Guid.
   - Options go through `AddValidatedOptions<T>` with `ISectionBoundOptions`.
   - Never hand-roll what a package covers; if a need is not in the table, say so rather than invent.
3. **Needs table (about 60 rows).** Columns: **Need** · **Package(s)** · **Reference from** ·
   **Entry point** · **Recipe**. Example rows:

   | Need | Package(s) | Reference from | Entry point | Recipe |
   | --- | --- | --- | --- | --- |
   | Publish an integration event | `SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit` (+ `.RabbitMq`/`.AzureServiceBus`) | Application; Infrastructure | `IEventPublisher`; `AddSharedKernelMessaging(configuration).UseRabbitMq(…)` | `add-messaging.md` |
   | Publish atomically with a DB write | + `SharedKernel.Messaging.MassTransit.EfCore` | Infrastructure | `WithEntityFrameworkOutbox<TDbContext>()` | `add-messaging.md` |
   | Cache a query result | `SharedKernel.Caching.Abstractions`, `SharedKernel.Application.Pipeline.Caching`, `SharedKernel.Caching.FusionCache` | Application; Api; Infrastructure | `ICacheableQuery<T>`, `app.WithCaching()` | `add-caching.md` |

   Derive the rows from the root `CLAUDE.md` "What Goes Where" table, rewritten for a consumer:
   - no domain ids
   - no maintainer files
   - no internal types
   - every entry point a real public API name

4. **Package catalog (generated, section 3.4).** One row per packable package: id, tier, "reference
   from", and its purpose (the README callout's first sentence).
5. **Recipes list.** One line per recipe: file and when to open it.

**Rules for `SKILL.md`:**
- No API detail beyond the entry-point name. Configuration, overloads and pitfalls live in READMEs.
- Testing-tier packages appear in the catalog and in `write-tests.md`, not in the needs table.
- Tooling packages appear once, in the non-negotiables: reference `SharedKernel.Analyzers`, which ships
  every rule at Warning, and escalate in the service's `.editorconfig`.

### 3.3 Recipes

A recipe is a **task**, not a package. It tells the assistant which packages to add to which
project, in what order, and how they connect. It then points to each README for detail.

**Fixed shape** (write a short standard in `consumer/README.md`, the same way
`docs/package-readme-standard.md` does for READMEs):

```markdown
---
proven-by:
  - samples/Shop/Ordering/...        repo paths that implement this recipe (checked by CI, never shipped as links)
---
# Add messaging

**Use when:** … **Not when:** … (point to the sibling recipe)

## Packages
| Project | Package | Why |

## Steps
1. Domain / Application: … (code)
2. Infrastructure: … (registration)
3. Api / Worker: … (host wiring)
4. Configuration: the section names only; the keys are in the package README.

## Test it
Which `.Testing` fakes to use, and where the integration test goes.

## Check
What the build and analyzers will flag if this is wrong.

## Read next
The package READMEs to read, by package id.
```

**Rules:**
- Every snippet is current public API with its `using` lines. Snippets mirror code in `samples/Shop`,
  named in `proven-by`. If Shop does not do it, either add it to Shop or do not put it in a recipe.
- 80–250 lines per recipe. Anything longer is two recipes.
- Do not repeat README content. Link by package id.
- `new-service.md` is the most important recipe. It is the four-project skeleton the service template
  will later be generated from, so write it from Shop's Ordering service.

### 3.4 Generator and CI checks

Extend `eng/generate-package-index.cs` (keep one generator, one `--check` run in `verify.yml`):

1. **Generated catalog region.** Rewrite the region between
   `<!-- BEGIN GENERATED: package-catalog -->` and `<!-- END GENERATED: package-catalog -->` in
   `consumer/skill/sharedkernel/SKILL.md`. Source each row from data that already exists:
   - **Package id, tier, "reference from":** the csproj tier mapping the generator already uses for
     `docs/packages.md`.
   - **Purpose:** the first sentence of the README callout (the bold blockquote after the badges),
     with Markdown stripped. Fail generation if a README has no parseable callout.
2. **`--check` additions** (same exit code as today):
   - **Region freshness:** the generated region is current.
   - **Routing coverage:** every packable package of tier Model, Abstractions, Adapter or Host appears
     in at least one needs-table row, or in an explicit `<!-- not-routed: <id> — <reason> -->` list.
     An example is `SharedKernel.Caching.Redis.Core`, which a consumer gets transitively. Foundation
     packages are covered by the catalog alone.
   - **No dangling ids:** every package id named anywhere under `consumer/skill/` is a real packable
     package.
   - **Recipe links:** every recipe named in `SKILL.md` exists, and every recipe file is named in
     `SKILL.md`.
   - **`proven-by` paths:** every listed path exists in the repo.
   - **Consumer vocabulary:** no domain ids (`NN.Name`), and no `CLAUDE.md`, `state-map`, `P-NNN` or
     `WO-NNN` under `consumer/skill/`.
3. Generated output stays deterministic and LF-terminated, like the existing views.

`eng/verify-markdown-links.sh` already checks relative links in tracked Markdown. In shipped files,
reference packages by id and recipes by plain file name, not by repo-relative links. Repo paths are
meaningless inside a service.

### 3.5 Release: ship the skill

In `release.yml`, after the package set is verified:

1. Copy `consumer/skill/sharedkernel/` to a staging folder.
2. Replace `{{SharedKernelVersion}}` with the tag's version (the same value MinVer gives the packages).
3. Zip it as `sharedkernel-skill-v<version>.zip`. The archive root is the `sharedkernel/` folder.
4. Attach the zip to the GitHub Release for the tag (create the release if the workflow does not yet).
   This needs `contents: write` on that job only. Keep actions SHA-pinned.
5. Add the zip to the job summary next to the package list.

Acceptance: one tag gives one package version and one skill version, both from the same commit. A
pre-release tag (`-rc.N`) ships a pre-release skill the same way.

How a service downloads and syncs the zip belongs to the service template, not this work. The
contract this repo guarantees is **the asset name, the archive layout, and the stamped version.**

### 3.6 Evaluation set

`consumer/eval/tasks.md` lists 15–20 task prompts. Each one has the expected package(s), the
expected project per package, and the expected recipe. For example:

| Task | Expected packages → project | Recipe |
| --- | --- | --- |
| "Publish `OrderPlaced` when an order is confirmed, without losing it if the broker is down." | `Messaging.Abstractions` → Application; `Messaging.MassTransit` + `.EfCore` → Infrastructure | `add-messaging.md` |
| "Run the stock reconciliation every night at 02:00 on one replica only." | `SharedKernel.Scheduling` → Infrastructure | `add-background-job.md` |

Procedure (manual for now, recorded in `consumer/README.md`):
1. Copy a Shop service to a scratch folder and put the skill in its `.claude/skills/sharedkernel/`.
2. Run each prompt in a fresh session.
3. Score the package choice, the project placement, and whether the build stays clean.

Run the set before each release and after any large change to `SKILL.md`. Automating it is a later,
separate decision.

### 3.7 Keeping it current

The skill rots unless changing it is part of normal work:

1. **`.claude/agents/_common.md`.** A phase that adds, removes or renames a packable package, or
   changes a registration entry point or a recipe-covered flow, updates the needs table and affected
   recipes in the same change. The `--check` coverage rule catches the add and rename cases.
2. **Root `CLAUDE.md`.** Add `consumer/` to "Other top-level locations". Note that the "What Goes
   Where" table (maintainers) and the needs table (consumers) are two views of the same routing and
   change together.
3. **`/sync-brain`.** Include `consumer/skill/` in what it reconciles against the code.
4. **`docs/package-readme-standard.md`.** Note that the callout's first sentence becomes the
   package's purpose in the skill catalog, so it must stand alone.

## 4. Prerequisite: README callout audit

The catalog quotes each README's callout and the recipes defer to READMEs, so READMEs must hold up:
- The callout's first sentence states what the package is for, on its own.
- The callout says when to use a sibling instead (rule 8).
- The Install table names the service project.
- Quick start is runnable as written.

Each domain checks its own packages. The audit is a sweep, not a rewrite. Fix what fails; most
READMEs should already pass.

## 5. Suggested phase breakdown

| # | Phase | Owner | Depends on |
| --- | --- | --- | --- |
| 1 | `consumer/` scaffold, `consumer/README.md` (purpose + recipe standard), `SKILL.md` hand-written sections and needs table | arch-lead / root | — |
| 2 | README callout audit (section 4) | every domain, one sweep | — |
| 3 | Generator region + `--check` rules (section 3.4) | devops-lead | 1, 2 |
| 4 | Recipes, `new-service.md` first, each with `proven-by` in Shop; extend Shop where a recipe has no proof | per recipe: the owning domain; Shop changes via samples | 1 |
| 5 | Release asset (section 3.5) | devops-lead | 3 |
| 6 | Maintenance rules (section 3.7) | arch-lead / tooling | 1 |
| 7 | Evaluation set and first run (section 3.6) | arch-lead | 3, 4 |

**Relation to P-577 (first release train):** this work should not block P-577. If phases 1–5 land
before the tag, v1.0.0 ships with the skill. Otherwise the first patch release carries it. The skill
needs no package change, so a patch release is enough.

## 6. Out of scope

- The service template, the skill sync script, and a service's own `CLAUDE.md`. These follow in
  `platform-service-template`.
- The integration-event catalog. It belongs to platform-docs and is per service.
- Plugin-marketplace distribution. The release asset is the contract; a marketplace can wrap it later.
- Automated evaluation.

## 7. Open questions for arch-lead

1. **Ownership of `consumer/`:** root (arch-lead), with domains editing their rows and recipes? Or a
   new non-numbered area with its own `CLAUDE.md`?
2. **Analyzer severity for consumers:** every rule ships at Warning. Should the skill (or a later
   package) ship a recommended consumer `.globalconfig` that escalates the critical rules, so the
   "the build catches it" guardrail actually holds in services?
3. **Recipe coverage gaps:** if a recipe needs a flow Shop does not exercise, extend Shop (preferred,
   keeps `proven-by` honest) or accept a recipe without proof?
