---
name: session5_published_phase_consumer_verify
description: 10.Intelligence SK.10.Published session — pack verification, two new consumer-verify harnesses (Qdrant, SemanticKernel), negative-compile-probe capability-segregation proof. How mixed-scope tasks (package column includes permanently-absent Milvus) get scored, and the CLAUDE.md changelog gap this session found and fixed.
type: project
---

## Mixed-scope Published tasks: package column including Milvus means the whole task stays ⚑, never ●

All six `SK.10.Published` tasks (P-01–P-06) list a package scope spanning either all four packages or
at least two-of-three providers including Milvus. Even though the entire actionable (non-Milvus) portion
was completed and verified, every task row was marked `⚑`, never `●` — the same treatment
`SK.10.Tests`' T-02/T-07 (mixed Qdrant+Milvus scope) already received. The test for "does a
mixed-Milvus-scope task get ⚑ or ●" is NOT "does the package column mention Milvus" alone — compare
against `SK.10.Docs`' DO-05 (drift check across all four packages, including Milvus) which WAS marked
`●` despite listing Milvus, because a drift/reconciliation check against a genuinely nonexistent
package is **vacuously complete** (there is nothing to ever diverge). By contrast, P-01 (verify a
`.csproj`'s metadata), P-02 (pack), P-03 (build a harness), P-04/P-05 (DI/segregation proof) all
describe **real, well-defined, deferred future work** that will become necessary and meaningful the
moment Milvus ships — that's genuinely-pending work, not vacuous, so `⚑` is correct.

**How to apply:** when scoring a task whose package column spans an absent package, ask "is there a
concrete action that remains undone and will need doing once the package exists" (→ `⚑`, deferred) vs.
"is the check already permanently satisfied because there's nothing there to compare against" (→ `●`,
vacuous). Don't default to either symbol without asking this question explicitly.

## Package Board (per-package) vs. task table (cross-cutting) — different granularities, both updated

The Published phase's task table is cross-cutting (one row spans all packages), but the Package Board
is per-package. A package can genuinely reach `Published`/`●` in the Package Board even while every
cross-cutting task row referencing it stays `⚑`, as long as that specific package's own portion of every
task is complete. Applied this session: `SharedKernel.AI.Abstractions`/`.Qdrant`/`.SemanticKernel`
Package Board rows → `Published`/`●`; `SharedKernel.AI.Milvus` stays `Scaffold`/`⚑`, unchanged. This
mirrors exactly how Docs phase treated Package Board rows (Qdrant/SemanticKernel/Abstractions → `Docs`/`●`
even while `SK.10.Docs`' own DO-03 task, Milvus-only, stayed `⚑`).

## Negative-compile-probe technique, adapted for 10.Intelligence — only ONE diagnostic fires, not two

Followed `09.Search`'s Published-phase technique verbatim: append a disallowed `using` + a bare field
declaration for the sibling provider's exclusive type directly into the real `consumer-verify.{X}`
`Program.cs`, with NO matching `<ProjectReference>`, build, capture the real diagnostic, revert.
**Difference from `09.Search`'s transcript**: only `CS0234` fired (namespace does not exist), never a
second cascading `CS0246` on the field's own type — confirmed by testing both directions
(`consumer-verify.Qdrant` probing `SharedKernel.AI.SemanticKernel.Raw.IKernelRawClientAccessor`, and the
mirror). This appears to depend on exact top-level-statement/using-placement shape (09.Search's probe
added the type reference deeper in the file, at a bare field declaration further from the failed
`using`; ours declared the field in the top-level-statement region immediately, closer to the failed
using). **One genuine compiler error is sufficient proof** — do not manufacture a second diagnostic
artificially or assume both must always appear; capture and document whatever the compiler actually
emits, verbatim, then revert.

## The third "pairing" is structurally impossible, not merely unattempted, when a package doesn't exist

`10.Intelligence/CLAUDE.md`'s P-05 phrasing asks to prove segregation "for the other two pairings" (3
total: Qdrant-only, Milvus-only, SemanticKernel-only). With `SharedKernel.AI.Milvus` absent from disk
entirely, the Milvus-only pairing cannot be attempted at all — there's no assembly, no namespace, no
project to build against. Recorded this explicitly in both harnesses' header comments and in
`CLAUDE.md`/state-map prose as "structurally moot, not merely unattempted" — never silently skipped,
never fabricated as done. This is a distinct failure mode from a genuinely-blocked-but-eventually-doable
task (P-01/P-02/etc.) — worth naming precisely rather than lumping all Milvus gaps under one label.

## QdrantClient and OpenAIClient construction is lazy — consumer-verify needs no live backend

Confirmed empirically (not assumed): `new QdrantClient(host, port, ...)` only builds a gRPC channel and
`new OpenAIClient(new ApiKeyCredential(...), options)` only wires a `System.ClientModel` pipeline
transport — neither issues an eager network call at construction. This let both Published-phase
`consumer-verify` harnesses drive a real `Host.CreateApplicationBuilder()` → `IHost.StartAsync()`
composition with a placeholder `Host=localhost`/`ApiKey=sk-consumer-verify-placeholder` and zero Docker
/ zero live endpoint dependency for every DI-composition/singleton/raw-client-gating/
`OptionsValidationException` surface. The real-backend guard stays exclusively `SK.10.Tests`' job.
`AddValidatedOptions`'s `ValidateOnStart()` only runs DataAnnotations validation at `IHost.StartAsync()`
— no network echo either.

## CLAUDE.md changelog had a fully-missing phase entry — found and fixed proactively

`10.Intelligence/CLAUDE.md`'s own `## Changelog` jumped straight from a `SK.10.Tests` entry to nothing —
the entire `SK.10.Docs` session's changelog line was never appended, even though the Docs session's
*content* edits (Status section, Technology Stack corrections, Filter-AST NOTE fix) had genuinely
landed in the file. This is the CLAUDE.md-changelog analogue of the recurring root-state-map staleness
pattern already documented in `session3`/`session4` memory. **How to apply:** before appending a new
CLAUDE.md changelog entry, scan the tail of the existing changelog for a gap matching the immediately
preceding phase (if the last entry is `SK.10.Tests` but you know a `SK.10.Docs` session genuinely landed
per the file's own Status/prose sections, the changelog entry for it is missing) — backfill a concise
retroactive entry in the same pass rather than compounding the gap silently.

## dotnet pack sanity: `--no-incremental` needed to force NU5039/NU5128 re-check after a warm build

`dotnet pack` on an already-built project (bin/obj populated from an earlier `dotnet build`) can skip
re-emitting pack-time warnings on a subsequent invocation unless something changed. When re-verifying
"zero NU5039/NU5128" as a genuine, fresh check (not just trusting the first pack's output), re-run with
`--no-incremental` (or from clean `bin`/`obj`) to force a real re-evaluation, then grep the full output
for the diagnostic codes rather than trusting a clean-looking tail.

## Root state-map.md structure: changelog entries are NOT all in one contiguous block

Discovered the root `state-map.md`'s nominal `## Changelog` section (started ~line 1640) gets
interrupted by interleaved `## WO-NNN` archival sections and Phase Backlog subsections; newer changelog
lines for a given domain get appended locally, right after that domain's most recent Phase-Backlog-
adjacent entry cluster (e.g. `10.Intelligence`'s own recent lines lived near line 11098, not near 1640).
**How to apply:** never assume the file has one clean changelog tail — `grep -n` for the domain's most
recent existing changelog line first, and append immediately after it, matching the file's actual
(messy but consistent) local-clustering convention rather than searching for a single canonical EOF.

## Phase Backlog Status field has no "Blocked" (⚑) value in practice

Checked for a `**Status:** \`⚑\`` precedent across the whole root `state-map.md` Phase Backlog — none
exists. Only `●` Complete, `◐` Dispatched, and (presumably) `○` Pending are used. When a phase-backlog
entry (e.g. P-281/Milvus) is genuinely blocked, it stays at its last real status (`◐` Dispatched) rather
than being given a fabricated `⚑` value — the blocking information lives in the Domain Summary Board's
"Blocked" column and the sub-domain's own `state-map.md`, not as a Phase Backlog Status enum value.
