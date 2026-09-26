---
name: project_09search_tests_phase
description: SK.09.Tests container-free session — bugs found and fixed, test techniques established, blocker re-confirmed
type: project
---
> WO-086 (2026-09): `ISearchIndexProvisioner.ProbeAsync` was removed — readiness is one `IReadinessProbe` per registered index (`SearchIndexReadinessProbe`, `search-{provider}-{index}`); the search-local `TenantScope` (string-keyed `TenantScope.Of(...)`) is now the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global`, `For(TenantId)`, `FromNullable`); container fixtures live in `16.Testing/SharedKernel.Testing.Internal/Containers/` and the in-memory fakes in `SharedKernel.Search.Testing`. The findings below are history.

SK.09.Tests container-free tasks (T-01–T-12, T-18–T-20; 15/26) completed 2026-07-19 — 248 tests green
(155 Abstractions, 49 Meilisearch, 44 ElasticSearch). T-13–T-17/T-21–T-26 (11 tasks, real-backend) were
marked `⚑` Blocked at that time (fixtures absent on disk).

**STALE as of 2026-07-20 — the blocker cleared.** `16.Testing`'s `MeilisearchContainerFixture`/
`ElasticsearchContainerFixture` landed; T-13–T-17 (Meilisearch real-backend) are now done, 79/79 green,
with three genuine production bugs found+fixed along the way — see
[[project_09search_meilisearch_realbackend]] for the full writeup. Always re-verify
`16.Testing/SharedKernel.Testing/Containers/` on disk yourself before trusting either this paragraph or
that one — this note itself is exactly the kind of thing that goes stale.

## Two genuine Core-phase production bugs found via test-writing, fixed (not deferred)

1. **`SearchValue.ToString()` always threw.** A plain `readonly record struct` with no positional
   primary constructor synthesizes `PrintMembers`/`ToString()` to print **every public property**
   unconditionally — including the five kind-checked `As*` accessors (`AsString`/`AsInt64`/etc.), each
   of which throws `InvalidOperationException` unless `Kind` matches. Since at most one accessor ever
   matches, `ToString()` on ANY `SearchValue` (or any containing record — `EqualFilter`/`RangeFilter`/
   `InFilter`) always threw. Found because a FluentAssertions failure-message formatter call crashed
   with a *different* exception than the one the test was actually checking for. Fixed with an explicit
   `Kind`-aware `public override string ToString()` on `SearchValue`
   (`09.Search/SharedKernel.Search.Abstractions/Models/SearchValue.cs`) that never throws — the general
   lesson: **a record struct's compiler-synthesized `ToString()` calls every public property blindly; any
   type with kind-checked/mode-dependent accessors needs an explicit override.**

2. **`MeilisearchIndexProvisioner`/`ElasticSearchIndexProvisioner`/`MeilisearchTenantTokenIssuer` could
   never resolve via DI in ANY consuming service, production included** — not a test-only issue.
   Their constructors take a raw `TOptions` (`MeilisearchOptions`/`ElasticSearchOptions`), not
   `IOptions<TOptions>`, but `AddValidatedOptions` only ever registers `IOptions<TOptions>` in the
   container. `MeilisearchSearchBuilder.Build()`/`ElasticSearchBuilder.Build()` registered these three
   via the plain shorthand `services.AddSingleton<TInterface, TImplementation>()`, which resolves
   constructor parameters by type from the container — and no `TOptions`-typed registration existed.
   Fixed by switching both to an explicit factory lambda unwrapping
   `sp.GetRequiredService<IOptions<TOptions>>().Value`, matching the pattern `AddIndex<TDocument>()`
   already used correctly for `ISearchIndex<TDocument>`/`IInstantSearch<TDocument>`/etc. **General
   lesson: any DI registration for a type whose constructor takes a raw options type (not
   `IOptions<T>`) must use a factory, never the open `AddSingleton<TInterface, TImplementation>()`
   shorthand — a container-free `BuildServiceProvider()` DI test that actually resolves the service is
   the only thing that catches this; a test that only checks `.Build()` doesn't throw will NOT catch it.**

## Confirmed NOT a bug (repo-wide convention, verified by grep)

`IClock` is never self-registered by any `AddSharedKernelXxx()` DI extension anywhere in the platform —
grepped for `.AddClock()` call sites repo-wide, found zero production call sites (only the doc comment
inside `01.Core/SharedKernel.Primitives/Clocks/IClock.cs` mentions it). Registration is uniformly the
consuming host's job, identical to the already-documented `ILogger<T>` precedent. Container-free DI
tests must register `SharedKernel.Testing.Clocks.FakeClock` explicitly alongside
`services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))` — both are test-fixture omissions to
watch for, not things to "fix" in the DI extension itself.

## Reusable test techniques established this session

- **`InternalsVisibleTo`** added to both `SharedKernel.Search.Meilisearch.csproj` and
  `SharedKernel.Search.ElasticSearch.csproj`, granted to their own `.Tests` project only — mirrors the
  `06.Persistence.EfCore`/`13.ServiceDefaults`/`15.Integration.Webhooks` precedent (grep
  `InternalsVisibleTo` repo-wide to find the exact `<InternalsVisibleTo Include="..." />` MSBuild item
  syntax used everywhere — it's a built-in SDK item, no manual `AssemblyInfo.cs` attribute needed).
  Needed because `MeilisearchFilterCompiler`/`ElasticSearchFilterCompiler`,
  `Meilisearch/ElasticSearchRequestValidator`, and `*ProviderDescriptor` are all `internal`.
- **Null-client no-I/O-proof technique** for pre-flight validation tests: `MeilisearchClient`/
  `ElasticsearchClient` ship no interface (can't `NSubstitute`), so construct
  `MeilisearchIndex<TDocument>`/`ElasticSearchIndex<TDocument>` directly with `client: null!`.
  `SearchAsync` validates before ever touching `_client`, so a clean `Result` failure (no
  `NullReferenceException`) is structural proof of zero I/O. Always pair with one "guard passes"
  companion test asserting the OPPOSITE (`NullReferenceException` IS thrown) to prove the guard itself
  — not an unrelated code path — is what stopped I/O in the rejection case.
- **`Elastic.Clients.Elasticsearch.Number` equality gotcha**: no public properties (FluentAssertions
  renders failures as `Number{ }`, useless for diagnosis), and distinguishes long-backed from
  double-backed representations for equality even at equal numeric value —
  `((Number)10L).Equals((Number)10.0) == false`. `ElasticSearchFilterCompiler`'s numeric range path
  (`ToDouble`) always produces a `double`-backed `Number`, so test assertions must compare against
  `(Number)10.0`, never `(Number)10L`, or the assertion silently fails with a confusing message.
  `FieldValue.String/Long/Double/Boolean` DOES have working `==`/`.Equals()` — this asymmetry between
  two similar-looking SDK value types is exactly why the reflection-verification-before-writing-
  assertions technique (established in the Core-phase memory) must be reapplied per SDK type, not
  assumed to generalize.
- **Golden-value fingerprint test**: computed the SHA-256 golden value by running the actual shipped
  `SearchIndexDefinition.Fingerprint` implementation against a scratch console project (not by hand — a
  hand-computed SHA-256 is error-prone and would test the wrong thing if miscalculated). Reused the
  same scratch project (`fingerprint-check.csproj` in the session scratchpad) for all SDK-shape
  reflection probes and equality checks this session — same technique as the Core-phase memory's
  "SDK-shape verification technique," now also proven useful for computing exact expected values, not
  just discovering API shapes.

## Scope discipline maintained

No hand-rolled competing container setup was added to either `.Tests` project despite the temptation —
the 08.Storage precedent (fixtures belong exclusively in `16.Testing/SharedKernel.Testing/Containers/`)
was followed exactly. `SK.09.Docs` is next once either the fixtures land in `16.Testing` (unblocking
T-13–T-26) or a future session decides to proceed with Docs while leaving Tests `◐` — check with the
dispatching command/user before assuming Docs can start with Tests incomplete, since the phase's own
Promotion Condition ("All tasks in Phase: Tests are `●`") was NOT met this session, so root
`state-map.md` was correctly left untouched at Core/`●`.
