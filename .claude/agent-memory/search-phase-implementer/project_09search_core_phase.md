---
name: project_09search_core_phase
description: SK.09.Core (C-01–C-48) session — SDK verification technique, confirmed real SDK shapes, provider-specific engine workarounds, EventId usage, build-warning baseline
type: project
---
> WO-086 (2026-09): `ISearchIndexProvisioner.ProbeAsync` was removed — readiness is one `IReadinessProbe` per registered index (`SearchIndexReadinessProbe`, `search-{provider}-{index}`); the search-local `TenantScope` (string-keyed `TenantScope.Of(...)`) is now the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global`, `For(TenantId)`, `FromNullable`); container fixtures live in `16.Testing/SharedKernel.Testing.Internal/Containers/` and the in-memory fakes in `SharedKernel.Search.Testing`. The findings below are history.

SK.09.Core (all 48 C-01–C-48 tasks) completed 2026-07-19 — full implementation of
`SharedKernel.Search.Abstractions`, `.Meilisearch`, `.ElasticSearch`. All three packages build with 0
errors. This memory captures findings that cost real investigation time and are NOT fully re-derivable
from reading `09.Search/CLAUDE.md` alone (though CLAUDE.md now also documents most of these in place —
check there first; this file has the "how we found it" context CLAUDE.md deliberately omits for brevity).

## SDK-shape verification technique (reusable for any future "open design risk against an unverified
compiled assembly" note in this domain)

Two Design-phase risks were flagged: Elastic.Clients.Elasticsearch 9.4.2's leaf-query constructor shape,
and MeiliSearch 0.20.0's `dynamic Filter`/`ISearchable<T>` shapes. Both were resolved by reflecting
against the REAL compiled NuGet assemblies rather than reading docs or guessing from the changelog:
- PowerShell `[System.Reflection.Assembly]::LoadFrom(path)` against the assembly in the NuGet
  global-packages cache, with `ReflectionTypeLoadException` recovery
  (`ex.Exception.InnerException.Types` — note the double `.Exception` unwrap) for assemblies with
  unresolved dependencies.
- When an assembly fails to load cleanly under legacy Windows PowerShell 5.1 (this environment's default
  PowerShell tool), fall back to a scratch `net10.0` console project (`dotnet new console` in the
  scratchpad dir) referencing the exact NuGet package version, and reflect from inside actual running
  .NET 10 code instead. This resolved the Elastic.Clients.Elasticsearch 9.4.2 probe after the raw
  PowerShell LoadFrom path hit assembly-resolution friction.
**Why this matters**: Design-phase risk notes in this domain are written to force verification, not to
be guessed away — trust the CLAUDE.md contract's own instruction to validate against the real package
before writing the translator.

## Confirmed real SDK shapes (now also captured inline in `09.Search/CLAUDE.md`)

- **Elastic.Clients.Elasticsearch 9.4.2**: every leaf query type (`TermQuery`, `TermsQuery`,
  `RangeQuery`/`NumberRangeQuery`, `ExistsQuery`) uses parameterless-ctor-plus-object-initializer, same
  as container types (`Query`, `Aggregation`, `SortOptions`). Each leaf type ships a convenience
  constructor (e.g. `TermQuery(Field field)`) but it's obsolete-annotated and does NOT satisfy `required`
  members without `[SetsRequiredMembers]` — `new TermQuery(f.Field) { Value = ... }` fails CS9035. Always
  use `new TermQuery { Field = ..., Value = ... }`.
- **MeiliSearch 0.20.0**: `Filter` is declared `dynamic` in source but erases to `object` at the CLR
  level — a plain `string` assignment works, no `Microsoft.CSharp` binder invoked at runtime for that
  assignment. `ISearchable<T>`/`SearchResult<T>`/`PaginatedSearchResult<T>` shapes fully mapped by
  reflection; typed `SearchAsync<TDocument>` genuinely has no `_formatted` capture hook (see below).

## Provider-specific engine capability-gap workarounds (load-bearing — do not "simplify" these away)

1. **Meilisearch highlighting**: the SDK's typed `SearchAsync<TDocument>` path never surfaces the
   per-hit `_formatted` sibling object. `MeilisearchResultMapper` searches with `T = JsonElement`
   instead of the caller's document type, manually deserializes via
   `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`, and separately extracts `_formatted`
   for `SearchHit.Highlights`. A future "cleanup" back to typed `SearchAsync<TDocument>` silently kills
   highlighting.
2. **Meilisearch schema-fingerprint storage**: Meilisearch has no index-metadata field analogous to ES's
   index `_meta`. Fingerprint is stored as a sentinel-prefixed entry
   (`"__sk_schema_fingerprint__:{hash}"`) inside `Settings.Dictionary` (normally the custom-tokenizer
   dictionary-words list) — a deliberate repurposing.
3. **ElasticSearch resumable cursor token**: `SearchCursor.Token` is Base64 over a JSON
   `CursorState { PitId, KeepAliveSeconds, PageSize, QueryBase64, SearchAfter }`. `QueryBase64` is the
   ALREADY-COMPILED ES `Query` object serialized via the client's own
   `RequestResponseSerializer.Serialize`/`Deserialize<Query>` — never plain STJ — because `Query`'s
   Union/container types need the SDK's own custom converters to round-trip. This was necessary because
   `SearchFilter`'s node constructors are `internal` to `.Abstractions`, so the caller's original filter
   tree cannot be round-tripped directly; the already-translated `Query` is round-tripped instead.

## Reusable implementation patterns established this session

- **DI singleton lazy-resolution**: both provider `AddSharedKernel*Search()` extensions register the
  engine client (`MeilisearchClient`/`ElasticsearchClient`) as a singleton via a factory lambda that
  captures the *builder* object itself and reads its registered-index state lazily at first container
  resolution (always after `.Build()` has run). Solves "the client factory needs config that isn't final
  until the fluent chain completes" with zero reflection and no two-phase registration trick.
- **ES SDK type-name collision**: `SharedKernel.Search.ElasticSearch.Analytics` deliberately reuses the
  SDK's own aggregation type names (`TermsAggregation`, `RangeBucket`, `DateHistogramBucket`, etc.).
  `ElasticSearchAnalytics<T>` resolves this by fully-qualifying every SDK reference as
  `global::Elastic.Clients.Elasticsearch.Aggregations.*` and taking NO `using` for that namespace — never
  alias/rename either side. Any future ElasticSearch analytics code must follow the same convention.
- **Namespace-collision aliasing at file scope**: `ElasticSearchIndex.cs`, `ElasticSearchRequestTranslator.cs`,
  `ElasticSearchIndexProvisioner.cs`, `ElasticSearchCursorSearch.cs` all need
  `using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;` and/or
  `using Result = SharedKernel.Primitives.Results.Result;` because `Elastic.Clients.Elasticsearch`'s root
  namespace declares its own `SearchRequest`/`SearchRequest<T>` and `Result` (enum) types.

## EventId sub-blocks actually used (matches CLAUDE.md exactly — confirmed, not drifted)

Meilisearch: 9100–9120 used (21 entries) inside the reserved 9100–9199 block.
ElasticSearch: 9200–9223 used (24 entries) inside the reserved 9200–9299 block.
Abstractions: 9000–9099 reserved, zero usage (ships no `[LoggerMessage]` at all — by design).

## Expected build-warning baseline (NOT defects — do not "fix" these away in a future session)

- `.Meilisearch`: 2 warnings — CS8509 (SearchFilter switch, no default arm) + CS8524 (SearchValueKind
  switch, no default arm).
- `.ElasticSearch`: 3 warnings — 2x CS8509 (SearchFilter switch in filter compiler + AggregationRequest
  switch in analytics) + 1x CS8524 (SearchValueKind switch).
These are the deliberate, no-discard-arm exhaustive-switch pattern the domain's Hard Violations list
mandates. A future session seeing these warnings should NOT add a `_ =>` arm to silence them — that is
exactly the violation the pattern exists to prevent.

## Scope note

Test-writing was explicitly OUT OF SCOPE for SK.09.Core per the phase spec — deferred entirely to
SK.09.Tests. No `.Tests` files were touched this session. SK.09.Tests real-backend tasks are blocked on
`16.Testing`'s `MeilisearchContainerFixture`/`ElasticsearchContainerFixture`, confirmed absent on disk as
of this session (only PostgreSQL/Redis/RabbitMQ/MinIO fixtures exist in
`16.Testing/SharedKernel.Testing/Containers/`) — re-verify on disk at the start of any Tests-phase
session rather than trusting this note, per the standing cross-domain-dependency-verification rule.
