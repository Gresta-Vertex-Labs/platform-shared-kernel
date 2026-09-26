You are executing a smart git commit workflow for Platform.SharedKernel. Your job is to read every changed file, understand what actually changed, group by domain, determine the commit type, and produce clean conventional commits — one per domain group.

---

## Step 1 — Gather full context (run all in parallel)

1. `git status --short` — every changed, staged, deleted, and untracked file
2. `git diff HEAD` — full diff of all tracked changes
3. `git diff --cached` — staged changes if any exist  
4. Read the root `state-map.md` — understand the active work orders and current phases so commit messages can reference the work in progress
5. `git log --oneline -5` — learn the commit style and casing conventions of this repo

Do NOT skip the `state-map.md` read. It gives you the "why" behind the changes, which belongs in commit messages.

---

## Step 2 — Map every changed file to its domain

Use this path-prefix table. The first matching prefix wins.

| Path prefix | Domain scope |
|-------------|-------------|
| `00.Governance/` | governance |
| `01.Core/` | core |
| `02.Caching/` | caching |
| `03.Domain/` | domain |
| `04.Contracts/` | contracts |
| `05.Application/` | application |
| `06.Persistence/` | persistence |
| `07.Messaging/` | messaging |
| `08.Storage/` | storage |
| `09.Search/` | search |
| `10.Intelligence/` | intelligence |
| `11.Communication/` | communication |
| `12.Security/` | security |
| `13.ServiceDefaults/` | service-defaults |
| `14.Presentation/` | presentation |
| `15.Integration/` | integration |
| `16.Testing/` | testing |
| `17.Workflows/` | workflows |
| `18.Idempotency/` | idempotency |
| `19.Scheduling/` | scheduling |
| `20.Reporting/` | reporting |
| `samples/` | samples |
| `.github/`, `eng/` | ci |
| `docs/refactor/` | refactor |
| `.claude/` | tooling |
| Root-level files (`*.md`, `*.slnx`, `*.slnf`, `*.json`, `Directory.*`) | root |

Files that share a domain scope form one commit group.  

**Package sub-scope (optional).** When every file in a group belongs to one package of a multi-package domain, the scope may name it as `domain/package`, the way `search/meilisearch` is used in this repo's history. Use the package's name after its `SharedKernel.{Capability}.` prefix, lowercase, dots as hyphens. Current packages that commonly need it:

| Package | Sub-scope |
|---------|-----------|
| `SharedKernel.Execution` (01.Core) | `core/execution` |
| `SharedKernel.Application.Pipeline`, `.Pipeline.Caching` | `application/pipeline`, `application/pipeline-caching` |
| `SharedKernel.Application.Mediator.MediatR` | `application/mediator` |
| `SharedKernel.Idempotency.Abstractions` / `.Redis` / `.EfCore` | `idempotency/abstractions`, `idempotency/redis`, `idempotency/efcore` |
| `SharedKernel.Messaging.MassTransit.RabbitMq` / `.AzureServiceBus` / `.EfCore` (outbox) | `messaging/rabbitmq`, `messaging/azureservicebus`, `messaging/outbox` |
| `SharedKernel.Presentation.Core` / `.GraphQL` / `.SignalR.Redis` | `presentation/core`, `presentation/graphql`, `presentation/signalr-redis` |
| `SharedKernel.{Capability}.Testing`, `SharedKernel.Testing.Internal` | `testing/{capability}` (e.g. `testing/persistence`), `testing/internal` |
| `eng/SharedKernelTiers.targets` and the tier rules in `SharedKernel.ArchitectureTests` | `governance` (tier rules are governance even though the target lives in `eng/`) |

GraphQL is a `presentation` package (it moved out of 11.Communication). There is no `application-abstractions`, `behaviors` or `communication/graphql` scope any more, and the probe-only `SharedKernel.ServiceDefaults.*` packages (AI, Caching, Caching.Redis, Messaging, Scheduling, Search, Storage, Workflows.Temporal, Cryptography.KeyVault) no longer exist — a change to a remaining ServiceDefaults package is `service-defaults`.  
If a group has only `.md` / documentation files and no code, mark it `docs-only`.

---

## Step 3 — Determine the commit type for each group

Read the actual diff for each group and classify:

| What the diff shows | Type |
|--------------------|------|
| New class, interface, method, pattern, or capability | `feat` |
| Bug fix, logic correction, wrong behavior fixed | `fix` |
| Code restructure with no observable behavior change | `refactor` |
| Only markdown / comment / documentation changes | `docs` |
| CI workflow, MSBuild props/targets, `eng/` script, central package pins | `build` |
| Project file, NuGet reference, config, tooling | `chore` |
| New tests for existing code | `test` |

When a group has mixed signals, pick the highest-impact type: `feat` > `fix` > `refactor` > `test` > `build` > `chore` > `docs`.

Special cases:
- `state-map.md` files (any domain folder) → `docs` with that domain's scope
- Root `state-map.md` alone → `docs(root)`  
- `.claude/commands/` files → `chore(tooling)`
- Test projects (`.Tests/` path segment) → `test` unless they contain new test subjects that are themselves the feature

---

## Step 4 — Draft one commit message per group

Format (conventional commits, this repo's style):

```
type(scope): concise description in lowercase, no period
```

Rules:
- Scope is the domain name from Step 2 (e.g. `persistence`, `communication`, `governance`)
- Description explains **what changed and what it enables** — not which files were touched
- Pull context from `state-map.md` active work to enrich the description (e.g. phase name, work-order topic)
- Keep the full line under 72 characters
- No capital letters, no trailing period

Good examples (match these in style and precision):
```
feat(communication): add correlated rest client with polly v8 resilience
feat(governance): reflection guard rules and no-make-generic predicate
fix(persistence): encryption key rotation job skips already-rotated rows
refactor(domain): extract domain event version helper to separate class
docs(persistence): mark bulk-actions phase complete in state-map
chore(tooling): add smart commit command
```

---

## Step 5 — Display the plan

Before touching git, print a clear summary table:

```
Domain        Files   Type       Proposed message
──────────────────────────────────────────────────────────────────────────
governance    7       feat       feat(governance): ...
communication 12      feat       feat(communication): ...
root          3       docs       docs(root): update state-map active work
```

Then print:
```
→ N commit(s) will be created. Proceed? (y / edit / abort)
```

Wait for the user to confirm before proceeding. If they say "edit", ask which message to revise. If they say "abort", stop immediately with no git changes.

---

## Step 6 — Execute commits in dependency order

Commit groups in dependency order: a group whose packages another group's changes consume is committed first, following the tier order (Foundation → Model → Abstractions → Adapter → Host → Testing/Tooling — see root `CLAUDE.md` "Tiers & Dependency Rules"). Folder numbers are domain names, not layers; use them only to break a tie between groups with no dependency between them (lowest first). This keeps git history readable as a dependency graph.

For each group:

1. Stage only that group's files — list every file explicitly:
   ```
   git add path/to/file1 path/to/file2 ...
   ```
   Never use `git add .` or `git add -A`.

2. Commit using a heredoc to preserve formatting:
   ```
   git commit -m "$(cat <<'EOF'
   type(scope): description

   Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
   EOF
   )"
   ```

3. After each commit, run `git status` briefly to confirm it landed cleanly.

---

## Step 7 — Final report

After all commits, print:
```
✓ N commit(s) created:
  abc1234 feat(governance): ...
  def5678 feat(communication): ...
```

Do NOT push. Commits only. If the user wants to push, they will ask separately.

---

## Guard rails

- If `git status` shows nothing changed → print `Nothing to commit. Working tree clean.` and stop.
- If a file appears in both staged and unstaged state → include it once; the staged version wins.
- Never commit files that look like secrets: `.env`, `*secrets*`, `*credentials*`, `appsettings.Production.json`. Warn the user if any such file appears in the diff.
- If there are merge conflicts (file shows `UU` in git status) → stop, warn, and do not attempt to commit.
