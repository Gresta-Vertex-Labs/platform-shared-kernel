# SharedKernel.Search.Abstractions

Full-text search abstraction contracts for Platform.SharedKernel microservices. Defines `ISearchIndex<TDocument>` (index/delete/bulk/search/get/count/enumerate), `ISearchIndexProvisioner` (ensure/exists/delete/cutover/probe), `ISearchProviderDescriptor` (ceilings + zero-I/O pre-flight validation), `IQueryBuilder<TDocument>` over the closed 8-node `SearchFilter` AST and the closed five-kind `SearchValue` scalar union, plus the `SearchErrors` factory. **Zero third-party NuGet dependencies** — references only `SharedKernel.Primitives` and `SharedKernel.Contracts`. Implemented by `SharedKernel.Search.Meilisearch` (BFF/fast) and `SharedKernel.Search.ElasticSearch` (analytics/heavy).

Application code should always inject `ISearchIndex<TDocument>` / `ISearchIndexProvisioner` / `ISearchProviderDescriptor` from this package — never a concrete engine SDK type (`MeilisearchClient`, `ElasticsearchClient`) directly.

## Included Types

- `ISearchDocument` — a single self-supplied `string DocumentId { get; }` member every indexed document type implements
- `ISearchIndex<TDocument>` — twelve-member provider-agnostic contract: write (`IndexAsync`, `IndexManyAsync`, `DeleteAsync`, `DeleteManyAsync`, `DeleteByFilterAsync`, `ClearAsync`, `WaitUntilSearchableAsync`), read (`SearchAsync`, `GetAsync`, `CountAsync`), corpus walk (`EnumerateAsync`)
- `ISearchIndexProvisioner` — non-generic, one per provider: `EnsureIndexAsync`, `IndexExistsAsync`, `DeleteIndexAsync`, `CutoverAsync`, `ProbeAsync`
- `ISearchProviderDescriptor` — singleton, zero I/O: `ProviderName`, `MaxTotalHits`, `MaxFacetValues`, `RegisteredIndexes`, `Validate`
- `IQueryBuilder<TDocument>` / `SearchQueryBuilder<TDocument>` / `SearchQuery.For<TDocument>()` — the fluent, immutable query-building entry point
- `SearchFilter` — closed 8-node AST (`Eq`/`Ne`/`In`/`Between`/`Exists`/`All`/`Any`/`Negate`) over the closed five-kind `SearchValue` scalar union (`String`/`Int64`/`Double`/`Boolean`/`DateTimeOffset`)
- `Models/` — `SearchRequest`, `SearchResults<TDocument>`, `SearchHit<TDocument>`, `TenantScope`, `SearchWriteConsistency`, `SearchWriteReceipt`, `SearchBulkReceipt`, `SearchIndexDefinition` + `SearchIndexDefinitionBuilder`, `SearchIndexHealth`, `IndexCutoverRequest`, `HighlightRequest`, `FacetResult`, `TotalHitsAccuracy`
- `SearchErrors` — static `Error` factory covering not-found, validation, conflict, unauthorized, and unexpected outcomes
- `SearchWellKnown` — the domain-local named-constants holder (default field names, page-size/ceiling defaults, `ActivitySource`/`Meter` names, provider names, OTel tag keys)
- `SearchStreamException` — the `Error`-carrying exception thrown from `EnumerateAsync`'s `MoveNextAsync`, the domain's one documented exception to the `Result`-first rule

## Install

```xml
<ProjectReference Include="..\SharedKernel.Search.Abstractions\SharedKernel.Search.Abstractions.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Search.Abstractions`, plus a provider package (`SharedKernel.Search.Meilisearch` or `SharedKernel.Search.ElasticSearch`) to actually resolve `ISearchIndex<TDocument>`/`ISearchIndexProvisioner`/`ISearchProviderDescriptor` — this package ships no DI extensions and no implementation.

## Design principles

- **Intersection-only.** Every member on this package's public surface is one both `SharedKernel.Search.Meilisearch` and `SharedKernel.Search.ElasticSearch` can implement completely and correctly. If a capability would force one adapter to throw, degrade, approximate, or silently drop a clause, it does not live here.
- **A provider swap is a compile error, not a startup error.** Capabilities only one engine genuinely has — Meilisearch's instant/typo-tolerant search and engine-enforced tenant tokens; ElasticSearch's structured aggregations and cursor-based deep pagination — are declared as typed contracts **inside their own provider package** (`IInstantSearch<TDocument>`/`ITenantSearchTokenIssuer` in `.Meilisearch`; `IAnalyticsSearch<TDocument>`/`ICursorSearch<TDocument>` in `.ElasticSearch`), never here. Referencing one of these from application code takes a compile-time dependency on that provider package, so swapping providers makes every non-portable call site a **build error** enumerating exactly what needs to change — never a `GetRequiredService` failure discovered in production. This package deliberately carries no capability-flags enum (see `ISearchProviderDescriptor`'s own remarks) for the same reason: an `if (caps.HasFlag(...))` branch at a call site is exactly the silent-degradation shape this design exists to prevent.
- **`Result`-valued expected failures.** Not-found, unauthorized, invalid request, undeclared field, and pagination-ceiling breaches are `Error` values via `SearchErrors` — never thrown exceptions. The only exception is `EnumerateAsync`, which returns a bare `IAsyncEnumerable<TDocument>` and surfaces mid-stream transport faults as `SearchStreamException` from `MoveNextAsync` — mirroring the `06.Persistence`/`08.Storage` streaming-read precedent.
- **`TenantScope` is a mandatory, separate method parameter — never a filter clause, never a request member.** A tenant predicate travelling through the same filter tree as business predicates can be dropped by a translation bug; a dropped business clause is a bug, a dropped tenant clause is a cross-tenant data leak. Adapters inject it as the outermost `AND` after translating the caller's filter. If the registered `SearchIndexDefinition` declares a `TenantField` and the caller passes `TenantScope.None`, the provider fails closed with `SearchErrors.TenantScopeMissing` and performs **no I/O**.
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

## Worked example — `SearchQuery.For<T>()`

```csharp
public sealed class ProductSearchService(ISearchIndex<ProductSearchDocument> index)
{
    public async Task<Result<SearchResults<ProductSearchDocument>>> SearchAsync(
        string tenantId, string? freeText, string? status, CancellationToken ct)
    {
        var filters = new List<SearchFilter>();
        if (status is not null)
        {
            filters.Add(SearchFilter.Eq(ProductSearchFields.Status, SearchValue.From(status)));
        }

        var buildResult = SearchQuery.For<ProductSearchDocument>()
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

        return await index.SearchAsync(buildResult.Value, TenantScope.Of(tenantId), ct);
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
