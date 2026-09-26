---
name: project_09search_t26_and_completion
description: SK.09.Tests T-26 (cross-provider parity suite) + final domain-wide completion session — empty-Or divergence, EnumerateAsync/tenant-facet verdicts, state-map/CLAUDE.md annotation pattern applied
type: project
---
> WO-086 (2026-09): `ISearchIndexProvisioner.ProbeAsync` was removed — readiness is one `IReadinessProbe` per registered index (`SearchIndexReadinessProbe`, `search-{provider}-{index}`); the search-local `TenantScope` (string-keyed `TenantScope.Of(...)`) is now the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global`, `For(TenantId)`, `FromNullable`); container fixtures live in `16.Testing/SharedKernel.Testing.Internal/Containers/` and the in-memory fakes in `SharedKernel.Search.Testing`. The findings below are history.

Completed 2026-07-20, same session that closed out T-13–T-17 ([[project_09search_meilisearch_realbackend]])
and T-21–T-25 ([[project_09search_tests_realbackend_elasticsearch]]). This entry covers the parts only the
coordinating pass did: T-26 itself, the two required-by-phase-spec real-evidence CLAUDE.md updates, and the
full `state-map.md`/`CLAUDE.md` documentation close-out for all 26 `SK.09.Tests` tasks. Final state: 345
tests green (155 `SharedKernel.Search.Abstractions.Tests` + 92 `SharedKernel.Search.Meilisearch.Tests` + 98
`SharedKernel.Search.ElasticSearch.Tests`), `SK.09.Tests` 26/26 `●`, promoted to root `state-map.md`.

## T-26 design: one case table, run independently in each sibling-isolated `.Tests` project

`SearchFilter`/`SearchRequest` construction can't be shared across `.Meilisearch.Tests` and
`.ElasticSearch.Tests` as a literal shared source file — sibling-independence rules (no cross-provider
`using`) forbid a shared test-support project that both would reference. Instead: a byte-identical 15-doc/
2-tenant `TestProductCorpus` (already shared) plus an independently-typed-out-but-logically-identical
`TheoryData<string, SearchFilter, IReadOnlyList<string>>` case table in each project's own
`{Provider}CrossProviderParityTests.cs`, expected `DocumentId` lists computed by running the equivalent
LINQ predicate against `TestProductCorpus.All` directly in the test file (never hand-typed literals) so a
corpus edit can't silently desync the expectation from the seed data. Both `.cs` files were written by hand
to mirror each other case-for-case; there's no compiler enforcement that they stay in sync — a future
change to one must be manually mirrored to the other, same as the corpus files themselves.

## Two required real-evidence findings (phase spec explicitly asked for both, in `CLAUDE.md`)

1. **`EnumerateAsync` ordering: CONFIRMED insertion order on BOTH engines, contract deliberately NOT
   strengthened.** Meilisearch evidence: seeded `prod-010,003,007,001,009,005` in that scrambled order,
   walk returned it in seed order (see [[project_09search_meilisearch_realbackend]] for the raw
   test detail). ElasticSearch evidence: `search_after`+PIT over `Sort=[_doc]` returns strict
   `_doc`/segment-insertion order, which happens to equal ID order only because the shared corpus was
   seeded in ID order (see [[project_09search_tests_realbackend_elasticsearch]]). **Judgment call, worth
   remembering for any future similar "confirm and maybe strengthen a guarantee" task**: confirmed CURRENT
   BEHAVIOUR is not automatically promoted to a LOCKED CONTRACT GUARANTEE. Insertion order is an
   implementation detail of each engine's own storage/iteration model (Meilisearch's `/documents`
   offset+limit walk; ES's segment/`_doc` order), not a documented API promise on either engine's own
   docs — a future engine version could change it silently. The contract's "unspecified, do not rely on
   it" language was retained verbatim; only the NOTE was extended with the confirmed-but-not-promoted
   finding. Do not re-litigate this into "just lock it in, we tested it" — the whole point of the original
   design caveat was engine-version risk, not a lack of testing effort.
2. **Tenant-scoped facet counts: CONFIRMED correct (tenant filter applied before faceting) on both
   engines.** Meilisearch: `filterableAttributes` includes the tenant field, and the compiler prepends
   `TenantScope` as the outermost `AND` before the request ever reaches the engine, so faceting always
   operates over the already-tenant-filtered set structurally (no separate faceting-time filter application
   step exists to get this wrong). ElasticSearch: `BoolQuery.Filter` (which the tenant clause is injected
   into) applies before any aggregation runs in the same request, confirmed empirically via a tenant-scoped
   faceted search whose per-value counts summed to the tenant's own document count, not the full corpus's.
   No cross-tenant cardinality leak on either engine.

## Genuine cross-provider semantic divergence found — NOT a bug, documented on both filter compilers

`SearchFilter.Any()` with **zero** operands compiles to the literal string `"()"` on Meilisearch, which
that engine's filter parser genuinely **rejects** as invalid syntax — confirmed via raw HTTP: `400`,
`code: invalid_search_filter`. The adapter correctly surfaces this as `SearchErrors.EngineFault` before my
test's original assumption (that it should succeed as an empty/unfiltered search) turned out to be wrong —
**the test's assumption was the bug, not the adapter**. ElasticSearch's equivalent construction (an empty
`BoolQuery.Should` array) is valid ES query syntax and evaluates as **match-all**. Both are faithful,
correct translations of each engine's own native empty-boolean-query semantics; this is a genuine, load-
bearing cross-provider portability hazard, not a defect on either side. Documented with a `VERIFIED` note
on both `MeilisearchFilterCompiler` and `ElasticSearchFilterCompiler` in `09.Search/CLAUDE.md`'s Interface
Contracts section (search for "T-26" in that file), plus a dedicated Test Rules bullet. **Lesson for any
future cross-provider parity test: when a test's expectation and the adapter's actual behaviour disagree,
verify against the RAW ENGINE (curl/raw HTTP) before assuming the adapter is wrong — in this specific case
the adapter was already correct, and the fix was to correct the test's assumption, not the code.**

## `state-map.md`/`CLAUDE.md` "annotate, never silently rewrite" pattern — applied end to end this session

The repo convention (confirmed against `16.Testing/state-map.md`'s own precedent for its analogous
P-268/P-269/P-275/P-276 blocker-clearance corrections) for resolving a stale `⚑ Blocked`/stale-status
write-up is: **wrap the old text with an HTML comment marker** (`<!-- prior resolved blocker (WO-XXX/P-XXX),
retained for history -->`) immediately followed by a **new, condensed** `**RESOLVED YYYY-MM-DD**: ...`
paragraph — not a byte-for-byte retention of the entire old prose (which in `16.Testing`'s own precedent
was itself heavily condensed relative to the original blocker write-up), and not a silent deletion either.
For a Cross-Domain Dependencies table row specifically, the established phrasing is `**Available** —
CORRECTED YYYY-MM-DD: previously "{old status string}" ... {what was re-verified and how}`. Applied both
patterns to `09.Search/state-map.md`'s `## Blocked` section and its two `16.Testing`-fixture Cross-Domain
Dependencies rows this session — condensed the original two-sub-problems/version-alignment-decision/
11-row-task-table write-up into one paragraph (the granular per-task detail was already redundant with the
Phase: Tests task table itself showing all 26 rows `●`), rather than preserving the full original verbosity
inline.

## Root `state-map.md` promotion mechanics (Sub-map mode, applied manually per the loaded skill's own
## instructions rather than re-invoking the Skill tool 11 times)

The `state-map-phase` skill's Sub-map mode is written to process ONE `task_id` per invocation. For 11
tasks completing in the same session, re-invoking the Skill tool eleven times would reload identical
procedural instructions eleven times for zero benefit — instead, invoked it ONCE to load the methodology,
then applied Steps S3–S9 directly across all 11 task rows in one coherent pass (batch of `Edit` calls on
the sub state-map, one recalculated Overall Progress row, one changelog line), then Step S8 (root
propagation) once the phase key's Overall Progress state genuinely reached `●`. Confirmed Step S8a
("close individual Phase Backlog entries") does NOT apply here — `SK.09.Tests`' `Maps to Root Phase` value
is exactly `Tests`, a standard lifecycle phase key per the registry's own Case 3 rule, so promotion updates
only the Domain Summary Board, mirroring the exact precedent already visible in this domain's own prior
Design/Core promotions and in `16.Testing`'s parallel Scaffold/Core promotions the same day (root
`state-map.md` changelog: "Tests is a standard lifecycle phase key with no individual Phase Backlog entry
to close").
