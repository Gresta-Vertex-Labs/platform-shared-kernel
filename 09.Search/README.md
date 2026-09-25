<div align="center">

# 🔎 SharedKernel Search

**Full-text search for multi-tenant .NET services — Meilisearch and ElasticSearch behind one contract, where a
provider swap is a build error instead of a production surprise, and no engine is ever allowed to answer
approximately while pretending to be exact.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Meilisearch](https://img.shields.io/badge/Meilisearch-verified-FF5CAA?logo=meilisearch&logoColor=white)](SharedKernel.Search.Meilisearch/README.md)
[![Elasticsearch](https://img.shields.io/badge/Elasticsearch-verified-005571?logo=elasticsearch&logoColor=white)](SharedKernel.Search.ElasticSearch/README.md)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 3](https://img.shields.io/badge/packages-3-informational)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

[Packages](#the-packages) · [Architecture](#architecture) · [10-minute start](#a-search-feature-in-10-minutes) · [Providers](#what-each-provider-adds) · [Guarantees](#what-you-can-rely-on)

</div>

---

```csharp
builder.Services
    .AddSharedKernelMeilisearchSearch(builder.Configuration)
    .AddIndex<ProductDocument>("products", index => index
        .PrimaryKey("documentId")
        .TenantField("tenantId")
        .Field("tenantId", SearchFieldKind.Keyword, filterable: true)
        .Field("name", SearchFieldKind.Text, searchable: true)
        .Field("category", SearchFieldKind.Keyword, filterable: true, facetable: true)
        .Field("price", SearchFieldKind.Decimal, filterable: true, sortable: true)
        .Synonym("tv", "television")
        .StopWords("the", "a", "of"))
    .Build();
```

That is the whole registration. Application code never sees a `MeilisearchClient` or an `ElasticsearchClient`:

```csharp
public sealed class ProductSearch(ISearchIndex<ProductDocument> index)
{
    public Task<Result<SearchResults<ProductDocument>>> FindAsync(
        string tenantId, string? text, string? category, CancellationToken ct)
    {
        var request = SearchQuery.New()
            .Matching(text)
            .Where(category is null ? SearchFilter.Exists("category") : SearchFilter.Eq("category", category))
            .OrderByDescending("price")
            .Faceting("category")
            .Page(page: 1, pageSize: 20)
            .Build();

        return request.IsFailure
            ? Task.FromResult(Result<SearchResults<ProductDocument>>.Failure(request.Error))
            : index.SearchAsync(request.Value, TenantScope.Of(tenantId), ct);
    }
}
```

Swapping `AddSharedKernelMeilisearchSearch` for `AddSharedKernelElasticSearchSearch` changes nothing above.

---

## The packages

| Package | What it is | Depends on |
| --- | --- | --- |
| [`SharedKernel.Search.Abstractions`](SharedKernel.Search.Abstractions/README.md) | The contracts everything else is written against — `ISearchIndex<TDocument>`, `ISearchIndexProvisioner`, `ISearchProviderDescriptor`, the closed 8-node `SearchFilter` AST, `SearchIndexDefinition`. **Zero third-party NuGet dependencies.** | `SharedKernel.Primitives`, `SharedKernel.Contracts` |
| [`SharedKernel.Search.Meilisearch`](SharedKernel.Search.Meilisearch/README.md) | The BFF/fast provider — typo tolerance, prefix search, engine-enforced per-tenant search tokens a browser can hold. | `MeiliSearch` SDK |
| [`SharedKernel.Search.ElasticSearch`](SharedKernel.Search.ElasticSearch/README.md) | The analytics/heavy provider — structured aggregations, deep cursor pagination, completion-suggester type-ahead. | `Elastic.Clients.Elasticsearch` |

Reference `.Abstractions` from your application code and exactly one provider package from your composition root.

---

## Architecture

The design is one rule: **`SharedKernel.Search.Abstractions` contains no type that either provider cannot implement
completely and correctly.** If a member would force one adapter to throw, degrade, approximate or no-op, it does not
belong there. That is the whole philosophy and it is mechanically checkable in review.

The consequence people notice first is what happens to capabilities only one engine has. They are declared as typed
contracts **inside their own provider package**, never neutralised:

```text
                  ┌─────────────────────────────────────────────┐
                  │      SharedKernel.Search.Abstractions       │
   your code ───▶ │  ISearchIndex · ISearchIndexProvisioner     │
                  │  ISearchProviderDescriptor · SearchFilter   │
                  └───────────────┬──────────────┬──────────────┘
                                  │              │
             ┌────────────────────┘              └────────────────────┐
             ▼                                                        ▼
  ┌──────────────────────────┐                        ┌──────────────────────────┐
  │  …Search.Meilisearch     │                        │  …Search.ElasticSearch   │
  │                          │                        │                          │
  │  IInstantSearch<T>       │  ← exclusive, typed →  │  IAnalyticsSearch<T>     │
  │  ITenantSearchTokenIssuer│                        │  ICursorSearch<T>        │
  │  MeilisearchRankingRule  │                        │  ISuggestSearch<T>       │
  └──────────────────────────┘                        └──────────────────────────┘
```

Use one of those and then swap providers, and the compiler enumerates every non-portable call site for you. There is
deliberately **no capability-flags enum** — an `if (caps.HasFlag(...))` branch at a call site is the silent-degradation
shape this design exists to prevent. Degradation in search is not a downgraded experience; it is a wrong answer that
looks like a right one.

---

## A search feature in 10 minutes

**1. Declare the document.** One self-supplied id, primitive members only.

```csharp
public sealed class ProductDocument : ISearchDocument
{
    public required string DocumentId { get; init; }   // A-Z a-z 0-9 - _ only, and stable across rebuilds
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required double Price { get; init; }
}
```

**2. Register the index** (the snippet at the top of this file). Field roles are declared, not inferred: Meilisearch
rejects a filter on an attribute that is not in `filterableAttributes`, so a field's roles have to be neutral
information or the identical query would succeed on one engine and 400 on the other.

**3. Provision it at deploy time.**

```csharp
await provisioner.EnsureIndexAsync(definition, ct);      // idempotent, additive-only
await provisioner.VerifyRegisteredIndexesAsync(ct);      // catches a forgotten rebuild
```

**4. Write.** Every write states the consistency it needs — there is no fire-and-forget, because that word means two
different things on the two engines.

```csharp
await index.IndexAsync(product, SearchWriteConsistency.Searchable, ct);
await index.IndexManyAsync(products, SearchWriteConsistency.Accepted, ct);
```

**5. Read.** Every read takes an explicit `TenantScope`.

```csharp
await index.SearchAsync(request, TenantScope.Of(tenantId), ct);
await index.GetAsync(id, TenantScope.Of(tenantId), ct);
await index.CountAsync(filter, TenantScope.Of(tenantId), ct);
```

**6. Rebuild without downtime**, when a mapping, synonym or stop-word list has to change:

```csharp
await provisioner.EnsureIndexAsync(stagingDefinition, ct);   // 1. create staging
await index.IndexManyAsync(everything, /* … */ ct);          // 2. bulk-load it
await provisioner.CutoverAsync(new IndexCutoverRequest { /* … */ }, ct);  // 3. atomic swap
```

---

## What each provider adds

| | Meilisearch | ElasticSearch |
| --- | --- | --- |
| **Best at** | BFF / user-facing search | Analytics / large corpora |
| Typo tolerance | Automatic, always on | Explicit `fuzziness`, opt-in and costly |
| Type-ahead | `IInstantSearch<T>` — prefix matching over the index, returns **documents** | `ISuggestSearch<T>` — completion suggester over an FST, returns **suggestion strings** |
| Aggregations | Facet counts and numeric min/max only | `IAnalyticsSearch<T>` — terms, cardinality, stats, date histogram, range, with sub-aggregations |
| Deep pagination | Capped by `maxTotalHits`; walk the corpus with `EnumerateAsync` | `ICursorSearch<T>` — point-in-time + `search_after` |
| Client-side search | `ITenantSearchTokenIssuer` — a signed, expiring token a browser holds, whose tenant filter the **engine** enforces | Document-level security is a commercial-tier feature; not available here |
| Relevance tuning | `MeilisearchRankingRule` — an ordered tie-breaker list | BM25 plus per-query boosts (not exposed; use the raw-client hatch) |
| Exact counts | Capped at the index ceiling — reported as a **lower bound**, never as fact | Uncapped `_count` API — always exact |

---

## What you can rely on

- **A dropped tenant clause is structurally impossible.** `TenantScope` is a mandatory, non-defaulted, *separate*
  parameter on every read and every filtered write — never a member of the request object, never a clause in the
  filter tree. A dropped business clause is a bug; a dropped tenant clause is a cross-tenant data leak, so it does not
  travel with the business predicates. Adapters inject it as the outermost `AND` after translating the caller's
  filter. An index that declares a tenant field and receives `TenantScope.None` fails closed and performs **no I/O**.

- **An unreachable engine is a `Result`, not an exception — on both providers.** A cluster that is down, refusing
  credentials, overloaded or simply not answering comes back as `search.unreachable`, `search.timeout` or
  `search.unauthorized`, so a caller can tell "retry in a moment" from "this will never succeed". The two SDKs signal
  failure in opposite ways — Meilisearch's throws, ElasticSearch's returns an invalid response — and each provider
  classifies its own shape onto the same vocabulary. Cancellation you asked for always propagates untouched.

- **Nothing answers approximately while claiming to be exact.** `SearchResults.TotalHits` carries a
  `TotalHitsAccuracy`, `CountAsync` returns a qualified `SearchCount`, and `ToPagedList()` refuses outright unless the
  total is genuinely exact. A Meilisearch count that hits the engine's ceiling reports `>=N`, not `N`.

- **Every operation is traced and measured.** A `search {operation}` client span plus
  `search.client.operation.duration` and `search.client.documents`, under the `SharedKernel.Search` source and meter
  that `13.ServiceDefaults`' `WithSearchTelemetry()` wires. Query text, filter values and document ids are never
  recorded — free text is user input and routinely carries personal data.

- **A schema change you forgot to deploy is caught.** Every provisioned index carries a fingerprint of the definition
  it was built from. `ProbeAsync` reports it and `VerifyRegisteredIndexesAsync` checks every registered index against
  the code's own declaration, catching the deployment where filters silently match nothing because the rebuild never
  ran.

- **The public API of all three packages is tracked**, so an addition, removal or signature change fails the build
  until `PublicAPI.*.txt` is updated — a breaking change is a reviewed diff, not something noticed after publish.

- **Both providers are verified against real engines**, not mocks: the test suites run against real Meilisearch and
  Elasticsearch containers, and `consumer-verify/` composes each package through a real `IHost.StartAsync()` exactly
  as a downstream service would.

---

## Maintainer documentation

[`09.Search/CLAUDE.md`](CLAUDE.md) holds the full interface contracts, the seam rule and its hard violations, the
per-provider implementation rules, the AOT posture, and the changelog.
