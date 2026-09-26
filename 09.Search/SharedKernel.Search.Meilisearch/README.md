# SharedKernel.Search.Meilisearch

Meilisearch (BFF/fast) implementation of [`SharedKernel.Search.Abstractions`](../SharedKernel.Search.Abstractions/README.md). Provides `MeilisearchIndex<TDocument>` (`ISearchIndex<TDocument>`), `MeilisearchIndexProvisioner`, `MeilisearchProviderDescriptor`, `MeilisearchFilterCompiler`, plus the Meilisearch-exclusive `IInstantSearch<TDocument>` (typo-tolerant/prefix instant search) and `ITenantSearchTokenIssuer` (engine-enforced per-tenant search tokens). Backed by the official `MeiliSearch` SDK.

## Included Types

- `MeilisearchIndex<TDocument>` — sealed `ISearchIndex<TDocument>` implementation, scoped
- `MeilisearchIndexProvisioner` — sealed `ISearchIndexProvisioner` implementation, singleton
- `MeilisearchRankingRule` — the typed, Meilisearch-exclusive relevance ordering rules applied via `WithRankingRules`
- `MeilisearchProviderDescriptor` — sealed `ISearchProviderDescriptor` implementation, singleton
- `IInstantSearch<TDocument>` / `MeilisearchInstantSearch<TDocument>` — Meilisearch-exclusive typo-tolerant/prefix instant search and facet-value type-ahead
- `ITenantSearchTokenIssuer` / `MeilisearchTenantTokenIssuer` — Meilisearch-exclusive engine-enforced per-tenant search tokens, registered only via `.WithTenantTokens()`
- `IMeilisearchRawClientAccessor` — the last-resort raw-client escape hatch, registered only via `.AllowRawClientAccess()`
- `MeilisearchOptions` — Options-pattern configuration, validated at startup
- `MeilisearchErrors` — Meilisearch-specific `Error` factory (`IndexingTaskFailed`, `TenantTokenIssuanceFailed`, `TenantTokenTtlOutOfRange`)
- `AddSharedKernelMeilisearchSearch(IConfiguration)` — DI registration entry point returning the fluent `MeilisearchSearchBuilder`

**This package must never reference `SharedKernel.Search.ElasticSearch`, directly or transitively** — the two providers are independent siblings, never a shared base.

## Install

```xml
<ProjectReference Include="..\SharedKernel.Search.Meilisearch\SharedKernel.Search.Meilisearch.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Search.Meilisearch` (which brings in `SharedKernel.Search.Abstractions` transitively).

## Setup

```csharp
services
    .AddSharedKernelMeilisearchSearch(configuration)
    .AddIndex<ProductSearchDocument>("products", index => index
        .PrimaryKey(ProductSearchFields.DocumentId)
        .TenantField(ProductSearchFields.TenantId)
        .Field(ProductSearchFields.Name,   SearchFieldKind.Text,    searchable: true)
        .Field(ProductSearchFields.Status, SearchFieldKind.Keyword, filterable: true, facetable: true)
        .Field(ProductSearchFields.Price,  SearchFieldKind.Decimal, filterable: true, sortable: true))
    .WithTenantTokens()          // opt-in: engine-enforced per-tenant search tokens
    .Build();

// Application code injects the abstractions, never Meilisearch.MeilisearchClient directly:
public sealed class ProductSearchService(
    ISearchIndex<ProductSearchDocument> index,
    IInstantSearch<ProductSearchDocument> instantSearch)
{
    // ...
}
```

`AddSharedKernelMeilisearchSearch` binds and validates `MeilisearchOptions`, registers `MeilisearchClient` as a **singleton** built from a named `IHttpClientFactory` client (never `new HttpClient()`), and registers `ISearchIndexProvisioner`/`ISearchProviderDescriptor` as singletons. Each `.AddIndex<TDocument>(...)` call registers scoped `ISearchIndex<TDocument>` and `IInstantSearch<TDocument>`. A misconfigured section fails at `IHost.StartAsync()`, not at first query.

## Configuration reference

Binds from the `Search:Meilisearch` section (`MeilisearchOptions.SectionName`):

| Property | Type | Required | Default | Notes |
| --- | --- | --- | --- | --- |
| `Url` | `string` | Yes | — | The Meilisearch instance URL. |
| `ApiKey` | `string` | Yes | — | The API key used to authenticate against Meilisearch. |
| `ApiKeyUid` | `string?` | Conditional | `null` | The UID of `ApiKey` — required only when `.WithTenantTokens()` is used, since token generation needs it. |
| `HttpTimeoutSeconds` | `int` | No | `30` | HTTP client timeout, `[1, 300]`. |
| `TaskWaitTimeoutSeconds` | `int` | No | `120` | Maximum time `WaitUntilSearchableAsync`/`SearchWriteConsistency.Searchable` polls for, `[1, 3600]`. |
| `TaskPollIntervalMilliseconds` | `int` | No | `250` | Polling interval while waiting for a Meilisearch task, `[25, 5000]`. |
| `MaxTotalHits` | `int` | No | `1000` | Pagination ceiling applied to every registered index, `[1, 100000]`. |
| `MaxFacetValues` | `int` | No | `100` | Per-facet value-count cap applied to every registered index, `[1, 10000]`. |
| `DefaultBatchSize` | `int` | No | `1000` | Default batch size used by bulk write operations, `[1, 100000]`. |
| `TenantTokenMaxTtlMinutes` | `int` | No | `15` | Maximum TTL, in minutes, a tenant search token may be issued for, `[1, 60]`. |
| `PooledConnectionLifetimeMinutes` | `int` | No | `5` | How long a pooled HTTP connection to Meilisearch is reused before it is recycled, `[1, 60]`. The `MeilisearchClient` is a singleton, so `IHttpClientFactory` can never rotate its handler — without this, a Meilisearch pod rescheduled onto a new address would stay unreachable until the service restarted. Not a request timeout. |

```json
{
  "Search": {
    "Meilisearch": {
      "Url": "http://localhost:7700",
      "ApiKey": "masterKeyOrScopedKey",
      "TaskWaitTimeoutSeconds": 120,
      "MaxTotalHits": 1000
    }
  }
}
```

## Instant search and facet-value type-ahead

```csharp
Result<SearchResults<ProductSearchDocument>> instant = await instantSearch.InstantAsync(
    new InstantSearchRequest { FreeText = "wireles head", Limit = 10 },
    TenantScope.For(tenantId), ct);

Result<IReadOnlyList<FacetValue>> facetTypeahead = await instantSearch.SearchFacetValuesAsync(
    ProductSearchFields.Status, facetQuery: "act", filter: null, TenantScope.For(tenantId), ct);
```

`InstantAsync` applies Meilisearch's automatic typo tolerance, prefix matching, and crop-marker highlighting — none of which is expressible as a neutral `SearchRequest` option, since ElasticSearch's nearest equivalents (`fuzziness`, `match_phrase_prefix`) have materially different edit-distance behaviour and cost. `SearchFacetValuesAsync` is type-ahead **within** one facet's own values — it is not the facet distribution `ISearchIndex.SearchAsync` already returns, and must not be conflated with it.

## Tenant search tokens

```csharp
services.AddSharedKernelMeilisearchSearch(configuration)
    .AddIndex<ProductSearchDocument>("products", index => index.TenantField(ProductSearchFields.TenantId) /* ... */)
    .WithTenantTokens()
    .Build();

// Injected only when .WithTenantTokens() was called:
public sealed class StorefrontTokenService(ITenantSearchTokenIssuer tokenIssuer)
{
    public Task<Result<TenantSearchToken>> IssueStorefrontTokenAsync(string tenantId, CancellationToken ct) =>
        tokenIssuer.IssueAsync(
            TenantScope.For(tenantId), ProductSearchFields.TenantId, ["products"], TimeSpan.FromMinutes(10), ct);
}
```

A tenant search token is an HS256 JWT carrying `searchRules` the **engine itself** enforces below the application layer — the strongest tenant-isolation primitive either provider offers, strong enough to hand directly to an untrusted storefront client. The rule dictionary is always constructed by this package from `TenantScope` and the tenant field name — callers can never hand-write it, since the SDK's own rule constructor takes an untyped dictionary where a mistake is a silent cross-tenant leak.

> **Hard warning — tenant tokens cannot be revoked.** Tokens are not tracked or stored server-side by Meilisearch, so a token **cannot be revoked before its expiry** once issued. There is no "logout" or "invalidate" call. This is exactly why `TenantTokenMaxTtlMinutes` exists and defaults conservatively (15 minutes) — issue short-lived tokens and re-issue rather than relying on any form of revocation. A ttl request above `TenantTokenMaxTtlMinutes` returns `MeilisearchErrors.TenantTokenTtlOutOfRange`. Tenant tokens also scope **search only** — they do not scope document writes, which remain the caller's `TenantScope` responsibility on every `ISearchIndex<TDocument>` write member.

## Hard warning — Meilisearch's task queue is global, sequential, and instance-wide

Every write on a Meilisearch instance — across **every index** on that instance — is processed by a single, global, sequential task queue. This has two consequences that nothing in this package's contract shape predicts:

1. **Do not share one Meilisearch instance between a bulk-import workload and a latency-sensitive BFF workload.** A large reindex or bulk load on one index will queue behind (or force queueing behind it) writes to a completely unrelated index on the same instance. A `SearchWriteConsistency.Searchable` write on your BFF's catalogue index can stall for as long as an unrelated analytics-export job's backlog takes to drain — even though the two indexes share nothing but the instance. Provision a dedicated Meilisearch instance per latency-sensitive workload, or route bulk-import traffic to a separate instance entirely.
2. **`SearchIndexHealth.PendingWriteCount` is instance-wide, not index-scoped.** `ProbeAsync`'s reported pending-write count reflects the entire instance's task queue depth, not the depth for the specific index being probed. A healthy-looking probe for index A can still be sitting behind a deep backlog caused entirely by index B — the count does not isolate by index because Meilisearch's own task-listing API does not either.

Neither of these is visible from this package's types alone — `ISearchIndex<TDocument>`/`MeilisearchOptions` say nothing about instance topology, because instance topology is a deployment decision, not an API concern. It is deliberately called out here rather than left as a surprise.

## Ranking rules — Meilisearch-exclusive relevance ordering

```csharp
builder.Services
    .AddSharedKernelMeilisearchSearch(builder.Configuration)
    .AddIndex<ProductSearchDocument>("products", index => index /* ... */)
    .WithRankingRules(
        "products",
        MeilisearchRankingRule.Words,
        MeilisearchRankingRule.Typo,
        MeilisearchRankingRule.Proximity,
        MeilisearchRankingRule.Attribute,
        MeilisearchRankingRule.Sort,
        MeilisearchRankingRule.Exactness,
        MeilisearchRankingRule.Descending("price"))
    .Build();
```

Ranking rules are an ordered list of tie-breakers Meilisearch applies in sequence to decide relevance order. **ElasticSearch has no counterpart** — its ordering comes from BM25 plus per-query boosts and `function_score`, a different mechanism applied at a different time — so this lives here rather than on the neutral `SearchIndexDefinition`. A call site that configures ranking rules takes a compile-time dependency on this package, so a provider swap surfaces as a build error naming every non-portable site.

Order is meaning: the rules apply in the sequence given, and supplying a list **replaces** the engine default sequence entirely rather than adding to it — include every rule you still want. `MeilisearchRankingRule` is typed rather than raw strings because a misspelled rule is accepted by the settings endpoint in some engine versions and silently changes relevance, a failure mode whose only symptom is "search results feel wrong". Custom `Ascending`/`Descending` rules must name a field declared sortable on the index definition.

## Pacing a large reindex — `SearchBulkWriteOptions`

The global-queue behaviour above is exactly what `IndexManyAsync`'s 4-argument overload exists to mitigate — pace a large reindex so it does not starve latency-sensitive writes to other indexes on the same instance:

```csharp
Result<SearchBulkReceipt> receipt = await index.IndexManyAsync(
    products,
    SearchWriteConsistency.Accepted,
    new SearchBulkWriteOptions { MaxBatchesPerSecond = 5 },
    ct);
```

This provider dispatches each `MeilisearchOptions.DefaultBatchSize`-sized batch via the SDK's single-batch `AddDocumentsAsync` call and awaits a computed delay between batches after the first — the same call `IndexAsync` already makes with a one-element list, so no other write behaviour changes. `MaxBatchesPerSecond = null` (the 3-argument overload's default) skips the delay entirely, leaving today's single-`AddDocumentsInBatchesAsync`-equivalent pacing unchanged. `DeleteManyAsync`'s 4-argument overload accepts `bulkOptions` for interface parity only — this provider dispatches a document-id bulk delete as one request regardless of size, so there is no inter-batch gap to pace.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [09.Search/CLAUDE.md](../CLAUDE.md) for the full interface contracts, filter-compiler escaping/precedence rules, and AOT posture.
