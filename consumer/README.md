# consumer/

Maintainer notes for the **`sharedkernel` Claude Code skill**, the guidance a consuming service's AI
assistant uses to pick, place and wire SharedKernel packages. The design is in
[`docs/proposals/consumer-skill.md`](../docs/proposals/consumer-skill.md).

| Path | What |
| --- | --- |
| `skill/sharedkernel/` | The folder that ships, exactly as a service receives it in `.claude/skills/sharedkernel/`. |
| `skill/sharedkernel/SKILL.md` | The index: version lookup, non-negotiables, needs table, generated package catalog, recipe list. |
| `skill/sharedkernel/recipes/` | One file per multi-package task. |
| `eval/tasks.md` | The evaluation prompt set and how to score it. |

The skill has three layers: **index → recipes → package READMEs.** The assistant loads only what the
task needs. API detail (configuration keys, overloads, pitfalls) lives only in the package READMEs,
which ship inside each nupkg. The skill names packages and entry points and links nothing else.

## How it is kept correct

`dotnet run eng/generate-package-index.cs` rewrites the generated catalog region in `SKILL.md` (from
each csproj's tier and the first sentence of each README callout) and validates the folder. CI runs it
with `-- --check`, which fails when:

- the catalog region is stale;
- a Model, Abstractions, Adapter or Host package is in no needs-table row and not in a
  `<!-- not-routed: <id> — <reason> -->` comment;
- a `SharedKernel.*` id outside a code block is not a real package;
- a recipe named in `SKILL.md` does not exist, or a recipe file is not named there;
- a `proven-by` path does not exist, or an `unproven` entry has no reason;
- a shipped file uses maintainer vocabulary: a domain id (`06.Persistence`), `CLAUDE.md`,
  `state-map`, a phase key or a work-order id.

Changing the routing is part of normal work: a change that adds, removes or renames a package, or
changes a registration entry point or a recipe-covered flow, updates the needs table and the affected
recipes in the same change. The root `CLAUDE.md` "What Goes Where" table is the maintainer view of the
same routing; the two change together.

## How it ships

`release.yml` runs `eng/pack-consumer-skill.sh <version> <outdir>` after the packages are verified. The
script copies `skill/sharedkernel/`, replaces `{{SharedKernelVersion}}` with the release version, drops
the maintainer-only recipe frontmatter, and zips it as `sharedkernel-skill-v<version>.zip` with
`sharedkernel/` at the archive root. The zip is attached to the GitHub Release for the tag. A
pre-release tag ships a pre-release skill. **The contract is the asset name, the archive layout and the
stamped version**; how a service syncs it belongs to the service template.

Try it locally: `bash eng/pack-consumer-skill.sh 0.0.0-local artifacts/skill`.

## Recipe standard

A recipe is a **task**, not a package: which packages go into which project, in what order, and how they
connect. It then points to each README for detail.

```markdown
---
proven-by:
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
unproven:
  - Azure Service Bus transport: the reason Shop does not exercise it
---
# Add messaging

**Use when:** … **Not when:** … (name the sibling recipe)

## Packages
| Project | Package | Why |

## Steps
1. Domain / Application: … (code)
2. Infrastructure: … (registration)
3. Api / Worker: … (host wiring)
4. Configuration: section names only; the keys are in the package README.

## Test it
## Check
## Read next
```

Rules:

1. **Proven by Shop.** Every snippet mirrors code in `samples/Shop`, named in `proven-by` (repo-relative
   paths; checked by CI and removed from the shipped copy). A flow Shop does not exercise goes under
   `unproven:` with the reason, and the recipe body says so; it gets no snippet.
2. **Current public API**, with the `using` lines it needs. Check names against the cited Shop file.
3. **80–250 lines.** Anything longer is two recipes.
4. **No README content.** No configuration tables, option lists or pitfalls; name the package id under
   "Read next".
5. **Plain references only.** Packages by id, recipes by bare file name (`add-caching.md`). No
   repo-relative links: they mean nothing inside a service.
6. **Consumer vocabulary.** Projects are Domain, Application, Infrastructure, Api/Worker and Tests.

## Evaluation

`eval/tasks.md` lists task prompts with the expected packages, projects and recipe. Run the set before
each release and after any large change to `SKILL.md`:

1. Copy a Shop service (for example `samples/Shop/Ordering`) to a scratch folder outside the repo.
2. Unzip a locally packed skill into its `.claude/skills/`.
3. Run each prompt in a fresh Claude Code session.
4. Score three things per task: the package choice, the project placement, and whether the build stays
   clean. Record the date, version and score at the bottom of `eval/tasks.md`.

Automating the run is a later, separate decision.
