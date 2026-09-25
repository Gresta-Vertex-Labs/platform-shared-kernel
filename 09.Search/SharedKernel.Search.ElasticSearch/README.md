# SharedKernel.Search.ElasticSearch

ElasticSearch (analytics/heavy) implementation of [`SharedKernel.Search.Abstractions`](../SharedKernel.Search.Abstractions/README.md). Provides `ElasticSearchIndex<TDocument>` (`ISearchIndex<TDocument>`, alias-based read/write split), `ElasticSearchIndexProvisioner` (atomic alias cutover), `ElasticSearchProviderDescriptor`, `ElasticSearchFilterCompiler`, plus the ElasticSearch-exclusive `IAnalyticsSearch<TDocument>` (terms/cardinality/stats/date-histogram/range aggregations), `ISuggestSearch<TDocument>` (completion-suggester type-ahead) and `ICursorSearch<TDocument>` (point-in-time + `search_after` deep pagination). Backed by `Elastic.Clients.Elasticsearch`.

## Included Types

- `ElasticSearchIndex<TDocument>` — sealed `ISearchIndex<TDocument>` implementation, scoped
- `ElasticSearchIndexProvisioner` — sealed `ISearchIndexProvisioner` implementation, singleton
- `ElasticSearchProviderDescriptor` — sealed `ISearchProviderDescriptor` implementation, singleton
- `IAnalyticsSearch<TDocument>` / `ElasticSearchAnalytics<TDocument>` — ElasticSearch-exclusive structured aggregations (`Terms`, `Cardinality`, `Stats`, `DateHistogram`, `Range`)
- `ICursorSearch<TDocument>` / `ElasticSearchCursorSearch<TDocument>` — ElasticSearch-exclusive relevance-ordered deep pagination (point-in-time + `search_after`), including a resumable Open/Read/Close cursor triple
- `IElasticSearchRawClientAccessor` — the last-resort raw-client escape hatch, registered only via `.AllowRawClientAccess()`
- `ISuggestSearch<TDocument>` / `SearchSuggestion` — the ElasticSearch-exclusive completion suggester (declared here, never in `.Abstractions`)
- `ElasticSearchOptions` — Options-pattern configuration, validated at startup
- `ElasticSearchErrors` — ElasticSearch-specific `Error` factory (`InvalidCursor`, `CursorExpired`, `AggregationFailed`, `SourceSerializerContextMissing`)
- `AddSharedKernelElasticSearchSearch(IConfiguration)` — DI registration entry point returning the fluent `ElasticSearchBuilder`

**This package must never reference `SharedKernel.Search.Meilisearch`, directly or transitively** — the two providers are independent siblings, never a shared base. `NEST`/`Elasticsearch.Net` are EOL and are never referenced anywhere in this package or the platform.

## Install

```xml
<ProjectReference Include="..\SharedKernel.Search.ElasticSearch\SharedKernel.Search.ElasticSearch.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Search.ElasticSearch` (which brings in `SharedKernel.Search.Abstractions` transitively).

## Setup

```csharp
services
    .AddSharedKernelElasticSearchSearch(configuration)
    .AddIndex<OrderSearchDocument>(readAlias: "orders", writeAlias: "orders-write", index => index
        .TenantField(OrderSearchFields.TenantId)
        .Field(OrderSearchFields.Reference, SearchFieldKind.Keyword, filterable: true)
        .Field(OrderSearchFields.PlacedOn,  SearchFieldKind.DateTimeOffset, filterable: true, sortable: true))
    .WithSourceSerializerContext(OrderSearchJsonContext.Default)   // required for trimmed/AOT consumers
    .Build();

// Application code injects the abstractions, never Elastic.Clients.Elasticsearch.ElasticsearchClient directly:
public sealed class OrderSearchService(
    ISearchIndex<OrderSearchDocument> index,
    IAnalyticsSearch<OrderSearchDocument> analytics,
    ICursorSearch<OrderSearchDocument> cursorSearch)
{
    // ...
}
```

`AddSharedKernelElasticSearchSearch` binds and validates `ElasticSearchOptions`, registers `ElasticsearchClient` as a **singleton** (thread-safe, pools its own resources), and registers `ISearchIndexProvisioner`/`ISearchProviderDescriptor` as singletons. Each `.AddIndex<TDocument>(...)` call registers scoped `ISearchIndex<TDocument>`, `IAnalyticsSearch<TDocument>`, and `ICursorSearch<TDocument>`. A misconfigured section fails at `IHost.StartAsync()`, not at first query. `readAlias` is what reads (`search`/`get`/`count`/`enumerate`) target and `writeAlias` is what writes (`index`/`delete`/`bulk`) target — `CutoverAsync` flips the read alias atomically via a single aliases request.

## Configuration reference

Binds from the `Search:ElasticSearch` section (`ElasticSearchOptions.SectionName`):

| Property | Type | Required | Default | Notes |
| --- | --- | --- | --- | --- |
| `Nodes` | `string[]` | Yes | — | Cluster node URIs, at least one. |
| `ApiKey` | `string?` | No | `null` | API-key authentication. Takes precedence over `Username`/`Password` when set. |
| `Username` | `string?` | No | `null` | Basic-auth username, used only when `ApiKey` is not set. |
| `Password` | `string?` | No | `null` | Basic-auth password, used only when `ApiKey` is not set. |
| `CertificateFingerprint` | `string?` | No | `null` | Expected server certificate fingerprint, for self-signed deployments. |
| `AllowInvalidCertificates` | `bool` | No | `false` | Disables TLS certificate validation. Logs a startup warning when `true` — **never enable in production.** |
| `RequestTimeoutSeconds` | `int` | No | `30` | Request timeout, `[1, 300]`. |
| `PingTimeoutSeconds` | `int` | No | `2` | Ping timeout, `[1, 30]`. |
| `MaxTotalHits` | `int` | No | `1000` | Pagination ceiling per registered index, `[1, 1000000]` — see the hard warning below. |
| `MaxFacetValues` | `int` | No | `100` | Per-facet value-count cap per registered index, `[1, 10000]`. |
| `BulkMaxBytes` | `int` | No | `10485760` | Target payload-byte ceiling per bulk batch, `[1048576, 52428800]`. |
| `BulkMaxDocuments` | `int` | No | `2000` | Document-count ceiling per bulk batch, `[1, 50000]`. |
| `PointInTimeKeepAliveSeconds` | `int` | No | `300` | Point-in-time keep-alive duration for `ICursorSearch`, `[10, 3600]`. |
| `ProbeCacheSeconds` | `int` | No | `5` | How long a `ProbeAsync` result is cached; `0` disables caching, `[0, 60]`. |
| `NumberOfShards` | `int` | No | `1` | Primary shard count applied when provisioning a new index, `[1, 100]`. |
| `NumberOfReplicas` | `int` | No | `1` | Replica count applied when provisioning a new index, `[0, 10]`. |
| `RefreshIntervalSeconds` | `int` | No | `1` | Refresh interval applied when provisioning a new index, `[-1, 3600]`. |

```json
{
  "Search": {
    "ElasticSearch": {
      "Nodes": ["https://localhost:9200"],
      "ApiKey": "base64EncodedApiKey",
      "MaxTotalHits": 1000
    }
  }
}
```

## Verifying the engine version and the live indexes

```csharp
// From a startup task or a deployment smoke test — never implicitly at first resolve.
Result version = await host.Services.VerifyElasticSearchEngineVersionAsync(ct);
Result indexes = await provisioner.VerifyRegisteredIndexesAsync(ct);
```

`VerifyElasticSearchEngineVersionAsync` checks the connected cluster is 9.x or 10.x, returning `search.engine_version_unsupported` for a reachable cluster on the wrong version and `search.unreachable` for one that does not answer. It is an explicit asynchronous call because the alternative was worse than useless: this check previously ran inside the `ElasticsearchClient` DI factory as a blocking `InfoAsync().GetAwaiter().GetResult()` gated by a `ValidateEngineVersionOnStart` flag, which ran at *first resolution* of the client — typically inside the first request, not at startup — blocked a thread pool thread on a network round trip to do it, and only logged, so an unsupported cluster served traffic anyway.

`VerifyRegisteredIndexesAsync` (on the neutral `ISearchIndexProvisioner`) checks every registered index exists, is addressable with this service's credentials, and matches its declared schema fingerprint — catching the quiet failure where code ships declaring a field, synonym or stop word the live index was never rebuilt for.

## The `JsonSerializerContext` requirement for trimmed/AOT consumers

`Elastic.Clients.Elasticsearch` sets `IsAotCompatible` for net8+ and **disables reflection-based System.Text.Json by default**. Document (de)serialization must therefore be wired through a source-generated `JsonSerializerContext`:

```csharp
[JsonSerializable(typeof(OrderSearchDocument))]
internal sealed partial class OrderSearchJsonContext : JsonSerializerContext;

services.AddSharedKernelElasticSearchSearch(configuration)
    .AddIndex<OrderSearchDocument>("orders", "orders-write", index => { /* ... */ })
    .WithSourceSerializerContext(OrderSearchJsonContext.Default)
    .Build();
```

Omitting `.WithSourceSerializerContext(...)` logs a startup warning (`ElasticSearchSourceSerializerContextMissing`) and surfaces `ElasticSearchErrors.SourceSerializerContextMissing` at first document (de)serialization — a runtime failure, not a build failure, precisely because trimming/AOT status is a deployment concern this package cannot detect at compile time. Always register this for any service that will ever publish a trimmed or Native AOT deployment.

## Structured aggregations

```csharp
Result<AggregationResultSet> result = await analytics.AggregateAsync(
    filter: SearchFilter.Eq(OrderSearchFields.Status, SearchValue.From("shipped")),
    aggregations: [AggregationRequest.Terms("byRegion", OrderSearchFields.Region, size: 20)],
    TenantScope.Of(tenantId), ct);

if (result.IsSuccess && result.Value.TryGetTerms("byRegion", out var byRegion))
{
    foreach (var bucket in byRegion.Buckets)
    {
        // bucket.Key / bucket.DocCount
    }
}
```

`AggregateAsync` and its closed `AggregationRequest`/`AggregationResult` hierarchies are the hardest wall in this domain: Meilisearch offers only facet-count distributions and numeric min/max, with no sum, average, cardinality, percentiles, date-histogram, or nested/pipeline aggregations. This is declared here — never in `SharedKernel.Search.Abstractions` — precisely so a call site that references it takes a compile-time dependency on ElasticSearch: a provider swap away from ElasticSearch surfaces as a **build error** enumerating every aggregation call site, not a runtime capability check.

## The read alias, the write alias, and a silent failure worth knowing about

`AddIndex<TDocument>(readAlias, writeAlias, …)` addresses reads and writes separately, because during a
rebuild `CutoverAsync` repoints the read alias at a freshly-built staging index while writes continue
elsewhere. **That split is not the starting configuration:** a service starts with both names equal to
the concrete index `EnsureIndexAsync` creates.

If you do split them, make sure the write alias actually resolves. ElasticSearch **auto-creates an index
on write**, so a write alias pointing at nothing does not fail — every write lands in a brand-new,
mapping-less, analysis-less index that no read ever touches. The bulk call reports success, the counts
look plausible, and the data is simply not where the service is looking.

`VerifyRegisteredIndexesAsync` checks for exactly this: that the write alias resolves *and* carries the
schema fingerprint `EnsureIndexAsync` writes, which an implicitly-created index never has. Call it from
a startup task or a deployment smoke test and the misconfiguration is loud instead of invisible.

## Completion suggestions (type-ahead)

```csharp
builder.Services
    .AddSharedKernelElasticSearchSearch(builder.Configuration)
    .AddIndex<ProductSearchDocument>("products-read", "products-write", index => index /* ... */)
    .WithCompletionField<ProductSearchDocument>("products-read", "nameSuggest")
    .Build();

// then, in application code:
Result<IReadOnlyList<SearchSuggestion>> suggestions = await suggest.SuggestAsync(
    "nameSuggest", prefix: "wirel", TenantScope.Of(tenantId), size: 10, fuzzy: false, ct);
```

`ISuggestSearch<TDocument>` exposes ElasticSearch's completion suggester — a purpose-built in-memory FST that answers prefix queries in roughly constant time and returns **suggestion strings with weights**, not documents. Meilisearch has no such structure and no such field type; its type-ahead story is ordinary prefix matching over the regular index, exposed as `IInstantSearch<TDocument>` in that package. The two solve the same product problem with different data structures and different result shapes, which is why each is declared in its own provider package rather than neutralised.

A completion field must exist in the mapping **before** documents are indexed, so `WithCompletionField` is a provisioning-time declaration, not a query option — adding one to a populated index needs a staging rebuild and a cutover for existing documents to become suggestable. Your document type populates the field itself. On a tenanted index the tenant field is registered as a **category context** on the completion mapping: the suggester ignores query filters entirely, so a context is the only mechanism that can scope a suggestion, and without it one tenant's product names would complete another tenant's typing. `TenantScope` is mandatory and fails closed, exactly as on the neutral read path.

## Cursor-based deep pagination

```csharp
await foreach (var hit in cursorSearch.StreamAsync(request, TenantScope.Of(tenantId), keepAlive: TimeSpan.FromMinutes(5), ct))
{
    // hit.Document / hit.Rank — relevance order, past MaxTotalHits
}

// Or the resumable triple, for a long-running export that checkpoints across process restarts:
Result<SearchCursor> cursor = await cursorSearch.OpenCursorAsync(request, TenantScope.Of(tenantId), TimeSpan.FromMinutes(5), ct);
Result<CursorPage<OrderSearchDocument>> page = await cursorSearch.ReadCursorAsync(cursor.Value, ct);
await cursorSearch.CloseCursorAsync(cursor.Value, ct);
```

Built on point-in-time plus `search_after` — never the scroll API, which Elastic explicitly de-recommends for deep pagination. `SearchCursor.Token` is opaque and must never be parsed. Distinct from `ISearchIndex<TDocument>.EnumerateAsync`, which is an unordered corpus walk for reindex/export, not relevance-ordered — Meilisearch has no `search_after`/point-in-time equivalent at any price, which is why this contract is declared here, not on the neutral surface.

## Pacing a large bulk write — `SearchBulkWriteOptions`

`IndexManyAsync`'s 4-argument overload paces this provider's existing byte/document-count batch loop (`BulkMaxBytes`/`BulkMaxDocuments`) so a large reindex does not starve concurrent read/query traffic against the same cluster:

```csharp
Result<SearchBulkReceipt> receipt = await index.IndexManyAsync(
    orders,
    SearchWriteConsistency.Accepted,
    new SearchBulkWriteOptions { MaxBatchesPerSecond = 5 },
    ct);
```

This provider already runs a sequential `foreach` over its own byte/document-count batches, so honoring `MaxBatchesPerSecond` was a straightforward insertion of a computed delay before every batch dispatch after the first — no restructuring of the batching or serialization logic itself. `MaxBatchesPerSecond = null` (the 3-argument overload's default) skips the delay entirely, leaving today's unthrottled `foreach` unchanged. `DeleteManyAsync`'s 4-argument overload accepts `bulkOptions` for interface parity only — this provider dispatches a document-id bulk delete as a single `BulkAsync` call regardless of size, so there is no inter-batch gap to pace.

## Hard warning — `MaxTotalHits` defaults to 1000, not ElasticSearch's native 10 000

ElasticSearch's own `index.max_result_window` defaults to 10 000; this package defaults `MaxTotalHits` to **1000** instead — the same ceiling Meilisearch's `maxTotalHits` defaults to — and `EnsureIndexAsync` pushes `index.max_result_window` down to match. This is deliberate, cross-provider-parity behaviour, not an oversight: a query proven legal against one provider is thereby guaranteed legal against the other, so a provider swap never converts a page-51 query into a production surprise. The trade-off is real — this knowingly hobbles ElasticSearch's stronger native ceiling — and is the change most likely to generate "the abstraction broke my search" friction.

**If this service will never swap to Meilisearch**, raise the ceiling per index by setting `MaxTotalHits` higher on the `SearchIndexDefinitionBuilder` for that index (`.MaxTotalHits(10_000)` or higher, up to `ElasticSearchOptions`'s own `[1, 1000000]` range) — this is a per-index override, not a global one, so services with a mix of swappable and ElasticSearch-only indexes can set each ceiling independently.

## Hard warning — ElasticSearch document-level security is a commercial-tier feature

Meilisearch's tenant search tokens (`ITenantSearchTokenIssuer`, in `SharedKernel.Search.Meilisearch`) are **engine-enforced** — the engine itself refuses to return another tenant's documents, below the application layer, even to a client holding only the token. ElasticSearch has no equivalent in this package, and that absence is deliberate rather than an oversight: ElasticSearch's own equivalent capability, **document-level security, is a commercial (subscription) tier feature**, not something available in the OSS/Basic distribution this package targets.

**Consequence for a provider swap:** a service that swaps from Meilisearch-with-tenant-tokens to ElasticSearch silently downgrades tenant isolation from **engine-enforced** to **application-enforced** — i.e., from "the engine itself cannot return the wrong tenant's data even if the application forgets to filter" to "correctness depends entirely on the application always passing the right `TenantScope`." This package's own `TenantScope`-as-mandatory-parameter design (see [`SharedKernel.Search.Abstractions`'s README](../SharedKernel.Search.Abstractions/README.md)) is the strongest application-enforced mitigation available, but it is not the same guarantee.

The OSS-tier substitute for document-level security is **deployment configuration this package neither performs nor can verify**: a filtered alias per tenant (or per tenant group) combined with role privileges that deny direct access to the concrete backing index, so that even a compromised or misconfigured application component cannot bypass the alias filter. Set this up at the cluster/deployment level if engine-enforced isolation is a hard requirement for a service on this provider.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [09.Search/CLAUDE.md](../CLAUDE.md) for the full interface contracts, filter-compiler object-graph rules, and AOT posture.
