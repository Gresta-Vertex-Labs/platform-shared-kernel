# SharedKernel.Search.Abstractions

Full-text search abstraction contracts for Platform.SharedKernel microservices. Defines `ISearchIndex<TDocument>` (index/delete/bulk/search/get/count/enumerate), `ISearchIndexProvisioner` (ensure/exists/delete/cutover/verify), `ISearchProviderDescriptor` (ceilings + zero-I/O pre-flight validation), `IQueryBuilder` over the closed 8-node `SearchFilter` AST and the closed five-kind `SearchValue` scalar union, plus the `SearchErrors` factory and `SearchIndexReadinessProbe`. **Zero third-party NuGet dependencies** — an Abstractions-tier package referencing only `SharedKernel.Primitives`, `SharedKernel.Execution` (for `TenantScope`/`TenantId`) and `SharedKernel.Contracts`. Implemented by `SharedKernel.Search.Meilisearch` (BFF/fast) and `SharedKernel.Search.ElasticSearch` (analytics/heavy).

Application code should always inject `ISearchIndex<TDocument>` / `ISearchIndexProvisioner` / `ISearchProviderDescriptor` from this package — never a concrete engine SDK type (`MeilisearchClient`, `ElasticsearchClient`) directly.

## Included Types

- `ISearchDocument` — a single self-supplied `string DocumentId { get; }` member every indexed document type implements
- `ISearchIndex<TDocument>` — twelve-member provider-agnostic contract: write (`IndexAsync`, `IndexManyAsync`, `DeleteAsync`, `DeleteManyAsync`, `DeleteByFilterAsync`, `ClearAsync`, `WaitUntilSearchableAsync`), read (`SearchAsync`, `GetAsync`, `CountAsync`), corpus walk (`EnumerateAsync`)
- `ISearchIndexProvisioner` — non-generic, one per provider: `EnsureIndexAsync`, `IndexExistsAsync`, `DeleteIndexAsync`, `CutoverAsync`, `VerifyRegisteredIndexesAsync`
- `SearchIndexReadinessProbe` — the `IReadinessProbe` (`SharedKernel.Primitives.Health`) each provider registers per index, named `search-{provider}-{index}` (`ProbeNameFor`); ready when the engine is reachable, the index addressable and a zero-row search succeeds
- `ISearchProviderDescriptor` — singleton, zero I/O: `ProviderName`, `MaxTotalHits`, `MaxFacetValues`, `RegisteredIndexes`, `Validate`
- `IQueryBuilder` / `SearchQueryBuilder` / `SearchQuery.New()` — the fluent, immutable query-building entry point. Non-generic: a `SearchRequest` is document-type-independent, and because fields are strings by design a `TDocument` parameter here would constrain nothing
- `SearchFilter` — closed 8-node AST (`Eq`/`Ne`/`In`/`Between`/`Exists`/`All`/`Any`/`Negate`) over the closed five-kind `SearchValue` scalar union (`String`/`Int64`/`Double`/`Boolean`/`DateTimeOffset`)
- `Models/` — `SearchRequest`, `SearchResults<TDocument>`, `SearchHit<TDocument>`, `SearchWriteConsistency`, `SearchWriteReceipt`, `SearchBulkReceipt`, `SearchIndexDefinition` (fields, tenant field, ceilings, and index-level `Synonyms`/`StopWords`) + `SearchIndexDefinitionBuilder`, `SearchCount`, `SearchIndexHealth`, `IndexCutoverRequest`, `HighlightRequest`, `FacetResult`, `TotalHitsAccuracy`
- `SearchErrors` — static `Error` factory covering not-found, validation, conflict, unauthorized, unavailable (`search.unreachable`, HTTP 503), timeout (`search.timeout`, HTTP 504), and unexpected outcomes
- `SearchWellKnown` — the domain-local named-constants holder (default field names, page-size/ceiling defaults, `ActivitySource`/`Meter` names, provider names, OTel tag keys)
- Tenant scoping is `SharedKernel.Execution.Tenancy.TenantScope` (`TenantScope.For(tenantId)` / `TenantScope.Global`) — this package declares no tenant type of its own
- `SearchStreamException` — the `Error`-carrying exception thrown from `EnumerateAsync`'s `MoveNextAsync`, the domain's one documented exception to the `Result`-first rule

## Install

```xml
<ProjectReference Include="..\SharedKernel.Search.Abstractions\SharedKernel.Search.Abstractions.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Search.Abstractions`, plus a provider package (`SharedKernel.Search.Meilisearch` or `SharedKernel.Search.ElasticSearch`) to actually resolve `ISearchIndex<TDocument>`/`ISearchIndexProvisioner`/`ISearchProviderDescriptor` — this package ships no DI extensions and no implementation.

## Design principles

- **Intersection-only.** Every member on this package's public surface is one both `SharedKernel.Search.Meilisearch` and `SharedKernel.Search.ElasticSearch` can implement completely and correctly. If a capability would force one adapter to throw, degrade, approximate, or silently drop a clause, it does not live here.
- **A provider swap is a compile error, not a startup error.** Capabilities only one engine genuinely has — Meilisearch's instant/typo-tolerant search and engine-enforced tenant tokens; ElasticSearch's structured aggregations and cursor-based deep pagination — are declared as typed contracts **inside their own provider package** (`IInstantSearch<TDocument>`/`ITenantSearchTokenIssuer` in `.Meilisearch`; `IAnalyticsSearch<TDocument>`/`ICursorSearch<TDocument>` in `.ElasticSearch`), never here. Referencing one of these from application code takes a compile-time dependency on that provider package, so swapping providers makes every non-portable call site a **build error** enumerating exactly what needs to change — never a `GetRequiredService` failure discovered in production. This package deliberately carries no capability-flags enum (see `ISearchProviderDescriptor`'s own remarks) for the same reason: an `if (caps.HasFlag(...))` branch at a call site is exactly the silent-degradation shape this design exists to prevent.
- **`Result`-valued expected failures, including operational ones.** Not-found, unauthorized, invalid request, undeclared field, and pagination-ceiling breaches are `Error` values via `SearchErrors` — never thrown exceptions. **So is an unreachable engine:** a search cluster that is down, refusing credentials, overloaded, or simply not answering comes back as `search.unreachable`, `search.timeout` or `search.unauthorized`, identically on both providers, so a caller can tell "retry in a moment" from "this request will never succeed". That holds even though the two engine SDKs signal failure in opposite ways — Meilisearch's throws, ElasticSearch's returns an invalid response — because each provider classifies its own SDK's shape onto the same vocabulary. The only exception is `EnumerateAsync`, which returns a bare `IAsyncEnumerable<TDocument>` and surfaces mid-stream faults as an `Error`-carrying `SearchStreamException` from `MoveNextAsync` — mirroring the `06.Persistence`/`08.Storage` streaming-read precedent. Cancellation you requested always propagates as `OperationCanceledException` and is never reported as a search failure.
- **Every operation is traced and measured.** Both providers emit a `search {operation}` client span on the `SharedKernel.Search` `ActivitySource` and record `search.client.operation.duration` (seconds) and `search.client.documents` on the `SharedKernel.Search` `Meter`, tagged with `search.index`, `search.operation`, `search.provider` and, on failure, `error.type`. Wire them with `SharedKernel.ServiceDefaults`' `WithSearchTelemetry()`. Query text, filter values and document ids are never recorded — free text is user input and routinely carries personal data.
- **`TenantScope` is a mandatory, separate method parameter — never a filter clause, never a request member.** A tenant predicate travelling through the same filter tree as business predicates can be dropped by a translation bug; a dropped business clause is a bug, a dropped tenant clause is a cross-tenant data leak. Adapters inject it as the outermost `AND` after translating the caller's filter. If the registered `SearchIndexDefinition` declares a `TenantField` and the caller passes `TenantScope.Global`, the provider fails closed with `SearchErrors.TenantScopeMissing` and performs **no I/O**.
- **`SearchWriteConsistency` is mandatory and non-defaulted on every write.** A bare fire-and-forget write is dishonest on both engines in different ways (ElasticSearch is durable-but-not-yet-searchable; Meilisearch is enqueued behind a global sequential task queue). `Accepted` maps to each engine's fastest acknowledgement; `Searchable` blocks until the write is visible to search.
- **No `Score`.** `SearchHit<TDocument>.Rank` (the 0-based ordinal within the result page) is the only portable ordering signal — Meilisearch's ranking-rule buckets and ElasticSearch's BM25 share no scale, range, or monotonicity guarantee.

## Mandatory convention: a field-constants class per document type

Filter/sort/facet/return fields are plain `string`s, not `Expression<Func<TDocument, object>>` — there is deliberately no `IQueryable`/LINQ surface (an `IQueryable` promises a completeness no search engine delivers, and this package cannot reference `03.Domain`'s specification machinery in any case). Refactor safety instead comes from declaring a small constants class per document type and using `nameof()` at every call site — never a bare string literal repeated across filter/sort/index-definition code:

```csharp
public static class ProductSearchFields
{
    public const string DocumentId = nameof(ProductSearchDocument.DocumentId);
    public const string Name = nameof(ProductSearchDocument.Name);
    public const string Status = nameof(ProductSearchDocument.Status);
    public const string Price = nameof(ProductSearchDocument.Price);
    public const string TenantId = nameof(ProductSearchDocument.TenantId);
}
```

A typo'd field name is a rejected filter on Meilisearch (loud, visible at first call) and a silent zero-result on ElasticSearch (quiet, dangerous) — the constants-plus-`nameof()` convention is what keeps a rename in the document type from silently drifting out of sync with filter/sort/index-definition code.

## The mandated flattening technique for object-array data

`SearchFilter` has **no nested/object-array path node** — this is a deliberate correctness decision, not a feature gap. ElasticSearch's `nested` mapping preserves intra-element field correlation; Meilisearch flattens arrays of objects into independent per-field arrays. Given a document shaped like:

```json
{ "variants": [ { "size": "M", "colour": "blue" }, { "size": "L", "colour": "red" } ] }
```

a naive nested filter for `size == "M" AND colour == "red"` **matches on Meilisearch** (it has already lost the M-blue/L-red pairing) **and does not match on ElasticSearch-with-`nested`** (which correctly preserves it). A neutral filter node over this shape would therefore be silently, confidently wrong on one provider — exactly the class of bug this domain exists to make impossible.

**The mandated portable technique is to flatten at document-mapping time into a precomputed composite filterable field, then filter it with `In(...)`:**

```csharp
public sealed record ProductSearchDocument : ISearchDocument
{
    public required string DocumentId { get; init; }

    // Precomputed at mapping time from the aggregate's Variants collection:
    //   variants.Select(v => $"{v.Size}|{v.Colour}") → ["M|blue", "L|red"]
    public required IReadOnlyList<string> VariantSizeColour { get; init; }
}

var filter = SearchFilter.In(ProductSearchFields.VariantSizeColour, SearchValue.From("M|red"));
```

This is exact and identical on both engines, because it turns a correlated-pair query into a plain set-membership check over precomputed composite values — there is no correlation left for either engine's array semantics to get wrong.

## Worked example — `SearchQuery.New()`

```csharp
public sealed class ProductSearchService(ISearchIndex<ProductSearchDocument> index)
{
    public async Task<Result<SearchResults<ProductSearchDocument>>> SearchAsync(
        TenantId tenantId, string? freeText, string? status, CancellationToken ct)   // SharedKernel.Execution.Tenancy
    {
        var filters = new List<SearchFilter>();
        if (status is not null)
        {
            filters.Add(SearchFilter.Eq(ProductSearchFields.Status, SearchValue.From(status)));
        }

        var buildResult = SearchQuery.New()
            .Matching(freeText)
            .Where(SearchFilter.All(filters.ToArray()))
            .OrderByDescending(ProductSearchFields.Price)
            .Faceting(ProductSearchFields.Status)
            .Page(page: 1, pageSize: 20)
            .Highlighting(new HighlightRequest { Fields = [ProductSearchFields.Name] })
            .Build();

        if (buildResult.IsFailure)
        {
            return Result<SearchResults<ProductSearchDocument>>.Failure(buildResult.Error);
        }

        return await index.SearchAsync(buildResult.Value, TenantScope.For(tenantId), ct);
    }
}
```

Repeated `Where(...)` calls **AND together rather than replace** — the alternative (last-call-wins) is exactly the silent-clause-dropping defect class that leaks tenant data. Compose an OR explicitly: `Where(SearchFilter.Any(a, b))`. Both construction paths stay legal — the builder is optional sugar over the plain `SearchRequest` record, so `SearchRequest.Default with { FreeText = "x", Facets = ["status"] }` works identically and is validated by the same executor.

## Pacing a large bulk write — `SearchBulkWriteOptions`

`IndexManyAsync`/`DeleteManyAsync` each have a second, 4-argument overload accepting a `SearchBulkWriteOptions` that caps how fast the provider's own internal batch-dispatch loop may proceed:

```csharp
Result<SearchBulkReceipt> receipt = await index.IndexManyAsync(
    documents,
    SearchWriteConsistency.Accepted,
    new SearchBulkWriteOptions { MaxBatchesPerSecond = 5 },
    ct);
```

`MaxBatchesPerSecond` is a **rate** cap over the batch dispatches both providers already issue internally — it does not introduce concurrency (neither provider ever dispatches batches in parallel) and it is independent of each provider's own chunk-**size** knobs (`MeilisearchOptions.DefaultBatchSize`, `ElasticSearchOptions.BulkMaxBytes`/`BulkMaxDocuments`). Use it to protect a shared engine's availability for concurrent read/query traffic while a large reindex or bulk import runs against it.

The existing 3-argument `IndexManyAsync`/`DeleteManyAsync` overloads are unchanged — they delegate to the 4-argument overload passing `SearchBulkWriteOptions.Default` (`MaxBatchesPerSecond = null`), which is byte-for-byte today's unthrottled, sequential behavior. Passing `SearchBulkWriteOptions.Default` explicitly is therefore equivalent to calling the 3-argument overload. Note that `DeleteManyAsync`'s 4-argument overload accepts `bulkOptions` for interface parity only — neither provider chunks a delete-by-id bulk call into multiple dispatches, so there is no inter-batch gap to pace; see each provider's own README for the throttled `IndexManyAsync` path, which does chunk.

## Counting documents — `CountAsync` returns a qualified `SearchCount`

`CountAsync` returns `Result<SearchCount>`, not `Result<long>`, because the two engines cannot both answer "how many documents match?" exactly:

```csharp
Result<SearchCount> count = await index.CountAsync(filter, TenantScope.For(tenantId), ct);
if (count.IsSuccess && count.Value.IsExact)
{
    // count.Value.Value is the real total.
}
```

ElasticSearch answers from its `_count` API and is always `TotalHitsAccuracy.Exact`. Meilisearch has no count endpoint at all — the only way to obtain a total there is to read `totalHits` off a paginated search, and the engine caps that value at the index's own `pagination.maxTotalHits` (provisioned from `SearchIndexDefinition.MaxTotalHits`, default 1000). A Meilisearch count that reaches the ceiling therefore comes back as `TotalHitsAccuracy.LowerBound` — "at least N" — rather than being published as fact. `SearchCount.ToString()` renders a lower bound as `>=1000` so a truncated figure can never be mistaken for an exact one in a log line.

## Synonyms and stop words — the only portable text-analysis surface

`SearchIndexDefinition` carries index-level `Synonyms` and `StopWords`. These are the one text-analysis knob both engines implement identically; everything finer (analyzers, tokenizers, normalizers, language packs) stays permanently off `SearchFieldDefinition`, because that is where a neutral mapping DSL lies most convincingly.

```csharp
new SearchIndexDefinitionBuilder("catalog")
    .Field("name", SearchFieldKind.Text, searchable: true)
    .Synonym("tv", "television")   // one-way: searching "tv" also matches "television"
    .StopWords("the", "a", "of")   // supply them already lowercased
    .Build();
```

**Synonyms are one-way, deliberately.** Meilisearch's `synonyms` setting is natively one-way per key; ElasticSearch's two-way equivalence syntax has no Meilisearch counterpart, so this platform emits ElasticSearch's one-way explicit-mapping form instead. Declare both directions explicitly when you want symmetry — the asymmetry is then visible in your own configuration rather than differing silently per engine.

Both are applied when `EnsureIndexAsync` **creates** the index and are part of its schema fingerprint. Neither engine can change them on a live index (ElasticSearch cannot alter an open index's analysis settings at all), so changing a list on an existing index returns `SearchErrors.IndexDefinitionConflict`; the remedy is the same as for an incompatible field mapping — provision a staging index, bulk-load it, then `CutoverAsync`.

## Running both engines in one service

Give each provider its own document type — `ISearchIndex<TDocument>` is then unambiguous — and resolve
the two **non-generic** contracts by provider-name key:

```csharp
var meili = services.GetRequiredKeyedService<ISearchIndexProvisioner>(SearchWellKnown.MeilisearchProviderName);
var elastic = services.GetRequiredKeyedService<ISearchIndexProvisioner>(SearchWellKnown.ElasticSearchProviderName);
```

`ISearchIndexProvisioner` and `ISearchProviderDescriptor` have no type parameter to tell two providers
apart, so an **unkeyed** resolution in a two-provider host silently returns whichever provider was
registered last — and a distinct `TDocument` does not help, because it is not part of their signature.
Both provider packages register them keyed as well as unkeyed, and both resolutions return the same
instance. A single-provider service — the overwhelmingly common case — keeps resolving unkeyed and
changes nothing. Readiness needs no key: each index's probe is named with its provider
(`search-meilisearch-products`, `search-elasticsearch-orders`).

Registering both providers against the **same** `TDocument` remains a hard violation: the second
`ISearchIndex<TDocument>` registration silently wins. That one is not keyed, deliberately.

## Catching a forgotten rebuild — `VerifyRegisteredIndexesAsync`

```csharp
Result verification = await provisioner.VerifyRegisteredIndexesAsync(ct);
```

Walks every index the composition root registered and checks that it exists, is addressable with this service's own credentials, and carries the schema fingerprint of the definition the code declares. It catches the quiet deployment failure where code ships declaring a field, synonym or stop word the live index was never rebuilt for — filters then silently match nothing and relevance silently changes, while the index itself looks perfectly healthy. Call it from a startup task, a service's own `IReadinessProbe`, or a deployment smoke test; it performs one round trip per registered index, so it is explicitly invoked rather than fired implicitly.

## The `RequireExactTotalHits` → `ToPagedList()` cost and accuracy note

By default (`RequireExactTotalHits = false`), Meilisearch reports an **estimated** total (`TotalHitsAccuracy.Estimated`, which can be over or under the true count) and ElasticSearch caps its exact-count work at its `track_total_hits` default (`TotalHitsAccuracy.LowerBound` once that ceiling is hit). Both are materially cheaper than an exact count. Setting `RequireExactTotalHits = true` costs more on both engines but guarantees `TotalHitsAccuracy.Exact`.

`SearchResults<TDocument>.ToPagedList()` — the **only** sanctioned bridge from this package to `04.Contracts`' `PagedList<T>` — enforces this honestly rather than silently publishing an estimate as fact:

```csharp
Result<PagedList<ProductSearchDocument>> paged = results.ToPagedList();
// Fails with SearchErrors.TotalHitsNotExact unless the request set RequireExactTotalHits = true
// and the provider actually returned TotalHitsAccuracy.Exact.
```

Set `RequireExactTotalHits` on the request when you intend to call `ToPagedList()` afterwards — `SearchErrors.TotalHitsNotExact`'s own message names this member as the remedy. `TotalHits` maps directly onto `PagedList<T>.TotalCount` (both are `long`, so ElasticSearch analytics totals above `int.MaxValue` project without loss). `ToPagedList()` also fails with `SearchErrors.InvalidSearchRequest` for a negative total, a page or page size below 1, or more hits than the page size, and drops `Facets`, `Rank`, and `Highlights` in the projection — none of those have a place on `PagedList<T>`.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [09.Search/CLAUDE.md](../CLAUDE.md) for the full interface contracts, provider implementation rules, and AOT posture.
