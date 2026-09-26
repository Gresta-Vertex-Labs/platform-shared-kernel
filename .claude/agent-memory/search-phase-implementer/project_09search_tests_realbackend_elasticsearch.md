---
name: project_09search_tests_realbackend_elasticsearch
description: SK.09.Tests T-21–T-25 real-backend ElasticSearch session — 1 production bug found+fixed, ES PIT keep_alive polling pitfall, confirmed ordering/tenant-exclusion evidence, SDK shape gotchas
type: project
---
> WO-086 (2026-09): `ISearchIndexProvisioner.ProbeAsync` was removed — readiness is one `IReadinessProbe` per registered index (`SearchIndexReadinessProbe`, `search-{provider}-{index}`); the search-local `TenantScope` (string-keyed `TenantScope.Of(...)`) is now the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global`, `For(TenantId)`, `FromNullable`); container fixtures live in `16.Testing/SharedKernel.Testing.Internal/Containers/` and the in-memory fakes in `SharedKernel.Search.Testing`. The findings below are history.

T-21 through T-25 (real-backend ElasticSearch, `SharedKernel.Search.ElasticSearch.Tests`) implemented
2026-07-20 against a real `docker.elastic.co/elasticsearch/elasticsearch:9.4.2` container via
`16.Testing`'s `ElasticsearchContainerFixture` (confirmed present and working — the blocker recorded in
[[project_09search_tests_phase]] is cleared for ElasticSearch). 85/85 tests green
(`09.Search/SharedKernel.Search.ElasticSearch/SharedKernel.Search.ElasticSearch.Tests/`). T-13–T-17
(Meilisearch) and T-26 (cross-provider parity) were out of scope for this session — a parallel agent
handled Meilisearch; a coordinating session updates `09.Search/CLAUDE.md`/`state-map.md` centrally.

**UPDATE (2026-07-20, same-day coordinating session): done.** The coordinating session implemented T-26
itself (13 tests added to this project, 98/98 total green with T-21–T-25), and wrote both
`09.Search/CLAUDE.md` and `state-map.md` in full — all 26 `SK.09.Tests` tasks are `●`. See
[[project_09search_t26_and_completion]] for the T-26-specific findings not covered here, including a
confirmed genuine cross-provider divergence: `SearchFilter.Any()` with zero operands evaluates as
match-all on ElasticSearch (the empty `BoolQuery.Should` array is valid syntax) but is REJECTED by
Meilisearch's filter parser as invalid syntax — both are correct per-engine behaviour, not a bug on
either side, but a portability hazard worth knowing before relying on it.

## One genuine Core-phase production bug found and fixed

**`ElasticSearchIndexProvisioner.ProbeAsync` scoped its cluster-health check to the specific index
name**, using `new HealthRequest(indexName)` instead of the parameterless `new HealthRequest()`. Since
`GET _cluster/health/<index>` fails/returns invalid for a genuinely non-existent index/alias, this made
`Reachable` incorrectly report `false` whenever `IndexAddressable` was also `false` — collapsing the two
signals into one and defeating the whole point of having them separate (the domain's own contract note
warns explicitly against this: "a green cluster with a missing alias passes cluster health but 100% of
requests fail"). Confirmed via reflection that `Cluster.HealthRequest` has both `ctor()` (overall
cluster health) and `ctor(Indices)` (index-scoped) — fixed by switching to the parameterless overload.
`Reachable` now correctly answers "is the cluster itself up," independent of whether the specific index
exists; `IndexAddressable` (a separate `Indices.ExistsAsync` call, already correct) answers the latter.
Fixed in `09.Search/SharedKernel.Search.ElasticSearch/Provisioning/ElasticSearchIndexProvisioner.cs`
(~line 182-193). **General lesson: any `ProbeAsync`/health-check-shaped method that takes a resource
name as a parameter must verify whether the UNDERLYING health primitive it calls is itself scoped to
that resource — an index-scoped (or bucket-scoped, queue-scoped, etc.) health call silently
re-introduces exactly the "can't tell down-cluster from missing-resource" ambiguity a two-flag design
exists to eliminate.**

## ElasticSearch PIT `keep_alive` semantics — the real gotcha of this session

**Every search request that references a PIT (via `Pit = new PointInTimeReference(pitId) { KeepAlive =
... }`) re-extends that PIT's expiry from THAT request's own timestamp — including a "read" that's
purely checking whether the cursor is still valid.** This makes a *polling* strategy to detect PIT
expiry (open with a short keep_alive, then repeatedly call `ReadCursorAsync` every few seconds waiting
for a `CursorExpired` failure) **self-defeating**: every poll succeeds and pushes the deadline forward
again, so expiry is never observed no matter how long you poll. Confirmed empirically in this exact
order: (1) a single 8-second wait after a 2-second `keepAlive` — NOT sufficient, PIT still servable; (2)
polling `ReadCursorAsync` every 5 seconds for 90 seconds total — ALSO not sufficient, for the reason
above; (3) **a single 75-second wait with ZERO intermediate reads, then exactly ONE `ReadCursorAsync`
call — worked correctly**, confirming `CursorExpired`. ES also does not appear to validate a PIT
reference's remaining `keep_alive` synchronously at query time against the nominal deadline — the
context stays servable until a periodic background sweep actually reaps it, and that sweep's own
cadence (not the requested `keep_alive` value) is what determines how soon expiry becomes observable in
a short-lived test's timeframe. **Rule for any future ES PIT/scroll-expiry test: open with a short
keep_alive, then wait a single long uninterrupted period (≥60-75s proved sufficient against this
fixture's single-node 9.4.2 container) with NO reads in between, then read exactly once.**

## Confirmed observed `EnumerateAsync`/`StreamAsync` ordering (T-26 evidence)

Both `ISearchIndex<TDocument>.EnumerateAsync` (via `search_after` + PIT over `Sort = [_doc]`) and
`ICursorSearch<TDocument>.StreamAsync` yielded the 15-document corpus in **strict document-ID-ascending
order** on every real-container run this session:
tenant-a: `prod-001, prod-002, prod-003, prod-004, prod-005, prod-006, prod-007, prod-008, prod-009, prod-010`
tenant-b: `prod-011, prod-012, prod-013, prod-014, prod-015`
This is consistent across repeated runs (both the initial and the re-run after fixes). The contract
still documents ordering as UNSPECIFIED (never assumed/relied upon) — this is raw evidence for a future
T-26 cross-provider-parity session to weigh alongside Meilisearch's own `/documents`-endpoint ordering
before strengthening the guarantee, not a guarantee itself. Note this is `_doc` order (index/segment
insertion order on a freshly-seeded single-shard index), which happens to coincide with ID order here
only because documents were seeded in ID order — do not generalize to "ES always returns ID order."

## Confirmed tenant-scoped aggregation exclusion (T-24)

`AggregateAsync` scoped to `TenantScope.Of("tenant-a")` against the shared 15-doc/2-tenant corpus: the
Terms-on-`category` bucket doc-count SUM was exactly **10** (tenant-a's count), never 15 — direct proof
the tenant filter is applied before aggregation, not after. Cardinality-on-`category` reported **3**
both times (both tenants happen to share the same 3 category values in this corpus, so cardinality
alone doesn't prove exclusion — the bucket-sum comparison against the full-corpus count of 15 is the
real proof, per the phase spec's own guidance).

## Bulk partial-failure coverage — genuine gap, documented not closed

Could **not** engineer a genuine ENGINE-SIDE per-item bulk failure against the strongly-typed
`TestProduct` document — every field is a plain C# primitive matching its ES-mapped type exactly, and
`IndexManyAsync`'s own contract exposes no per-item version/concurrency token a caller could
deliberately conflict. Tested instead: (a) the all-valid success shape (`SucceededCount == N`,
`Failures` empty) and (b) the CLIENT-SIDE whole-batch-rejected-before-I/O path (one invalid
`DocumentId` in an otherwise-valid batch → `Result.Failure(InvalidDocumentId)`, confirmed via a sibling
valid document in the SAME batch also not landing). **No other test anywhere in this codebase covers
the actual per-item `SearchItemFailure` population logic from `response.ItemsWithErrors`** either,
since `ElasticsearchClient` ships no interface and cannot be substituted via NSubstitute — this is a
real, small, currently-unclosed coverage gap in the domain, not unique to this session's shortcut.

## Cutover mechanics — first-ever-cutover concern resolved (non-issue)

The phase spec flagged a risk that `CutoverAsync`'s `Remove` action (targeting `Indices.All` for
`LiveIndexName`) might error when `LiveIndexName` has never existed as an alias before (first-ever
cutover). **Confirmed NOT an issue**: the single `UpdateAliasesAsync` call containing both the `Remove`
(for a not-yet-existing alias) and the `Add` actions succeeded cleanly on the very first call against
the real 9.4.2 container — no `IndexExistsAsync`/alias-existence pre-check workaround was needed.

## SDK shape findings (extends the Core/Tests-phase reflection-verification technique)

Verified via the established scratch-console-project reflection technique
(`dotnet new console` + `dotnet add package Elastic.Clients.Elasticsearch --version 9.4.2`, then
reflect/instantiate) before writing assertions — all confirmed against the real compiled 9.4.2 assembly:
- `IProperty`'s concrete subtypes (`TextProperty`/`KeywordProperty`/`IntegerNumberProperty`/
  `DoubleNumberProperty`/`BooleanProperty`/`DateProperty`) each expose a get-only `string Type`
  discriminator returning exactly `"text"`/`"keyword"`/`"integer"`/`"double"`/`"boolean"`/`"date"` —
  confirmed by constructing each type directly and reading `.Type`, not just by reflecting the member
  shape (a get-only property's *value* still needs an instance check, metadata alone isn't enough).
- `GetMappingResponse.Mappings` is `IReadOnlyDictionary<string, IndexMappingRecord>`;
  `IndexMappingRecord` has both an indexer and a `.Mappings` property, both yielding the actual
  `TypeMapping` — read as `response.Mappings[indexName].Mappings.Properties[fieldName].Type`.
- `GetIndicesSettingsResponse.Settings` is `IReadOnlyDictionary<string, IndexState>`; `IndexState.Settings`
  (`IndexSettings`) is **self-referential** — it carries both flat properties (e.g. `.MaxResultWindow`
  directly) AND a nested `.Index` property of the SAME `IndexSettings` type, mirroring ES's own
  `GET _settings` response nesting effective settings under an `"index"` JSON key even though
  `CreateIndexRequest.Settings` is *written* with flat property names. Read defensively:
  `settings.Index?.MaxResultWindow ?? settings.MaxResultWindow`.
- `HealthStatus` lives in the `Elastic.Clients.Elasticsearch` namespace directly, **not**
  `.Cluster` — five members: `Green`, `Red`, `Unavailable`, `Unknown`, `Yellow`.
- `Cluster.HealthRequest` has both `ctor()` (overall cluster health) and `ctor(Indices indices)`
  (index-scoped) — see the production-bug note above for which one to use and why.
- `_client.Indices.GetSettingsAsync(string)` is **ambiguous** — the SDK overloads both
  `GetSettingsAsync(Indices?, ...)` and `GetSettingsAsync(Names?, ...)` accept an implicit string
  conversion, so a bare string argument fails CS0121. Disambiguate with an explicit cast:
  `GetSettingsAsync((Elastic.Clients.Elasticsearch.Indices)indexName)`.
- `PropertyName` has an implicit `string -> PropertyName` conversion (confirmed via
  `op_Implicit` reflection), so `properties["fieldName"]` string-indexer syntax works directly.
- Several ES SDK dictionary-shaped response properties (`Properties`'s indexer, `TypeMapping.Properties`,
  `IndexState.Settings`) carry nullable-annotated return types invisible to plain
  `PropertyInfo.PropertyType` reflection (nullability lives in a separate `NullableAttribute` the naive
  reflection dump doesn't decode) — expect `<Nullable>enable</Nullable>` CS8602 warnings on every hop
  of a chained dictionary-of-dictionary access and null-forgive (`!`) each hop explicitly, not just the
  final one; the compiler's "maybe-null" flow state propagates through an assigned local even after a
  later expression in the same statement is null-forgiven.

## T-22 engine-version-guard logger resolution (answers the phase spec's open question)

Used `services.AddInMemoryLoggerFactory()` (registers `InMemoryLoggerFactory` as the singleton
`ILoggerFactory` PLUS the real BCL open-generic `Logger<>` adapter as `ILogger<>`), then cast
`provider.GetRequiredService<ILoggerFactory>()` to the concrete `InMemoryLoggerFactory` and called
`.GetLogger(typeof(ElasticsearchClient).FullName!)` — i.e. category
`"Elastic.Clients.Elasticsearch.ElasticsearchClient"`, confirmed correct because
`ElasticSearchServiceCollectionExtensions.ValidateEngineVersion` receives `ILogger<ElasticsearchClient>`
(`sp.GetRequiredService<ILogger<ElasticsearchClient>>()`), and the BCL's `Logger<T>` derives its category
name from `typeof(T).FullName` for a plain non-generic class. Pointed `Nodes` at
`http://127.0.0.1:1/` (nothing listens on port 1 — fails fast/connection-refused, no long timeout
needed) to exercise the exact same `catch`/`!IsValidResponse` path a genuinely unsupported server
version would hit, per the phase spec's own sanctioned design (never stand up a second real ES 8.x
container for this).

## Test file layout (for future reference)

`Support/TestProduct.cs`/`TestProductCorpus.cs` — byte-identical-by-design copies of the shared 15-doc/
2-tenant fixture (only the namespace differs from the Meilisearch agent's parallel copies; per the
coordinating brief, never touched after creation). `Support/TestProductIndexDefinitions.cs` — a
DRY helper (`Standard`/`WithMaxTotalHits`/`WithMaxFacetValues`, all keyed by caller-supplied index name)
factored OUT of the two "byte-identical" files since it's local-only tooling, not shared fixture data.
`Containers/ElasticsearchCollection.cs` + `ElasticsearchProviderFactory.cs` mirror the `08.Storage`
`MinioCollection`/`MinioProviderFactory` template exactly. Five `RealBackend/*.cs` classes (T-21..T-25),
each `[Collection(ElasticsearchCollection.Name)]` + its own uniquely-named index + `IAsyncLifetime`
ensure/seed/delete. `SiblingIndependenceTests.cs` at project root mirrors `08.Storage.Obs`'s template
verbatim (swap `ForbiddenNamespace` only).
