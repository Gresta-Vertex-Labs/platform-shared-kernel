---
name: project_09search_wo055_tests_phase_blocked
description: SK.09.Tests WO-055 sub-pass (T-27–T-34) session — entire phase found genuinely blocked on 16.Testing P-355 not shipping; zero code written, full reproduction and handling technique
type: project
---
> WO-086 (2026-09): `ISearchIndexProvisioner.ProbeAsync` was removed — readiness is one `IReadinessProbe` per registered index (`SearchIndexReadinessProbe`, `search-{provider}-{index}`); the search-local `TenantScope` (string-keyed `TenantScope.Of(...)`) is now the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global`, `For(TenantId)`, `FromNullable`); container fixtures live in `16.Testing/SharedKernel.Testing.Internal/Containers/` and the in-memory fakes in `SharedKernel.Search.Testing`. The findings below are history.

Session 2026-08-10: dispatched to implement T-27 through T-34 (the WO-055 sub-pass test coverage for
P-353's two production fixes and P-354's `SearchBulkWriteOptions` throttle, both of which shipped in
`SK.09.Core` C-49–C-55 the prior session — see [[project_09search_wo055_core_phase]]). Found the entire
remaining phase genuinely, totally blocked before writing a single test. No `09.Search` file was changed
this session except `09.Search/state-map.md` and `09.Search/CLAUDE.md` (documentation only).

## The blocker, confirmed two ways (never trust one alone)

1. **Source read**: `16.Testing/SharedKernel.Testing/Search/InMemorySearchIndex.cs` still declares only
   the pre-P-355 3-arg `IndexManyAsync`/`DeleteManyAsync` — no `SearchBulkWriteOptions` parameter, no
   `LastBulkWriteOptions` property anywhere in the file.
2. **Empirical build**: `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj
   --configuration Release` fails with two `CS0535` errors — `InMemorySearchIndex<TDocument>` does not
   implement the two new 4-arg `ISearchIndex<TDocument>` members that `09.Search`'s own C-52 (shipped the
   PRIOR session) added to the interface. `SharedKernel.Testing.dll` cannot be produced at all today.

**The consequence is total, not partial.** All three `09.Search` `.Tests` projects
(`Abstractions.Tests`/`Meilisearch.Tests`/`ElasticSearch.Tests`) carry a `ProjectReference` to
`SharedKernel.Testing` (S-03/S-07/S-10). When a `ProjectReference` target fails to build, MSBuild cannot
produce the referencing project's output either — there is no partial-build path where unrelated code in
the referencing project compiles anyway. This means **every one of T-27–T-34 is blocked**, including the
four container-free ones (T-27/T-28/T-30/T-31) that would otherwise need no Docker at all. This is a
different, more severe failure mode than "Docker unavailable" (which the phase brief separately
anticipated and gave a partial-completion path for) — here NOTHING can even compile, let alone run.

## Why zero test files were written this session (a deliberate choice, not an oversight)

The phase brief explicitly forbade two things once this exact scenario was confirmed: (a) editing any
`16.Testing` file to route around it, and (b) repeating the prior session's temporary-patch-then-revert
diagnostic trick "as if it were a real verification." Given I could not get a genuine, unforced compile
of any `.Tests` project, writing new test source code would mean shipping unverified code — risking
syntax errors, wrong API-surface assumptions (exact receipt field names, error codes, etc.) that a
compiler would normally catch. Writing plausible-looking but never-compiled test files and leaving them
in the tree, even clearly marked "unverified," risks a future session mistaking draft code for proven
code. Given this domain's extremely strong "never report an unrun test as passing" discipline throughout
its own CLAUDE.md, the correct call was: write NO test code, and instead produce a maximally useful,
precisely-reproducible blocker record so the next session (once P-355 ships) can move straight to writing
tests with zero re-diagnosis needed.

**Feedback signal for future sessions of this exact shape**: if this pattern recurs (an inbound
cross-domain compile break discovered via a phase brief's own "re-verify before trusting" mandate), the
correct action is full-stop documentation, not partial/unverified code. Do not split the difference by
writing "best effort" untested code.

## State-map mechanics used

- `## Blocked` section: added a new **ACTIVE** entry above the existing historical **RESOLVED** entry
  (never overwrote or removed the resolved one — this domain's "annotate, never silently rewrite"
  convention, demonstrated repeatedly across many prior sessions).
- Cross-Domain Dependencies table: added a new row for `SK.09.Tests` → `16.Testing` naming P-355
  specifically, distinct from the two already-`Available` rows for the container fixtures (which remain
  correctly marked Available — they are unaffected; the blocker is specific to `InMemorySearchIndex<TDocument>`
  conformance, not the fixtures).
- Phase: Tests task table: T-27–T-34 state column changed `○` → `⚑`, with a one-line blockquote under the
  table pointing at the `## Blocked` section rather than repeating the full narrative eight times.
  T-01–T-26 were **left untouched at `●`** — this new blocker postdates their verified-passing runs and
  does not retroactively invalidate historical results.
- Package Board: the three packages' existing "WO-055 remaining (..., `○`)" parenthetical notes were
  updated to `⚑` with a short "blocked on 16.Testing P-355" tag, not rewritten wholesale.
- No `state-map-phase` call was made — the phase's promotion condition was not met and nothing new
  reached `●`.
- No `sync-brain` formal invocation — none of that command's listed triggers matched (no new package, no
  new abstraction, no DI convention change, no seam-rule adjudication). Still made a direct, proportionate
  edit to `09.Search/CLAUDE.md`'s own Changelog (one entry) documenting the finding, since that file is
  the first thing a future session reads and the state-map's Blocked section alone would not surface this
  to someone skimming CLAUDE.md first.

## General lesson: verify cross-domain build health empirically, not just by reading source

Reading `InMemorySearchIndex.cs` and confirming the two overloads are absent is necessary but not
sufficient proof of a build break — always follow up with an actual `dotnet build` of the specific
upstream `.csproj` when a phase brief flags a "did the other domain ship X" question with real stakes.
Source-reading alone can miss e.g. a member existing under a different name/shape that still satisfies
the interface, or (the actual case found in the PRIOR `SK.09.Core` session) a scenario where the
interface's OWN shape changed and the implementer needs to check both sides. The empirical build is the
tie-breaker that removes all ambiguity.

## Related memories

[[project_09search_wo055_core_phase]] — the immediately-preceding session whose C-52 change (shipping the
two new `ISearchIndex<TDocument>` overloads) is what broke `16.Testing`'s `InMemorySearchIndex<TDocument>`
in the first place, and whose temporary-patch-then-revert diagnostic technique this session's brief
explicitly instructed not to repeat as a substitute for real verification.
