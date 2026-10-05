# SharedKernel.Search.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **In-memory `ISearchIndex<TDocument>`, provisioner and provider descriptor, so indexing and search code runs in a
> unit test without Meilisearch or Elasticsearch — with the same definition checks and tenant fail-closed rule as the
> real engines.**

| You get | So that |
| --- | --- |
| `InMemorySearchIndex<TDocument>` | Index, delete, search, count and enumerate against an in-memory corpus |
| Validation against your `SearchIndexDefinition` | Undeclared or wrong-role fields and the page ceiling fail with the real `search.*` errors |
| The mandatory `TenantScope`, enforced | A tenant-declaring index answers `TenantScope.Global` with `search.tenant_scope_missing`, as production does |
| The full `SearchFilter` AST evaluated | Equality, `In`, ranges, `Exists`, `All`/`Any`/`Negate` filters behave as written |
| `WasIndexed`, `WasDeleted`, `IsSearchable`, `Seed` | Arrange a corpus and assert what the code under test wrote |
| `InMemorySearchIndexProvisioner` / `InMemorySearchProviderDescriptor` | Provisioning, cutover and pre-flight validation paths are testable |
| `SimulateFailure` | Write-rejection handling is testable |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Search.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Add it to a **test project** only. A production project that references it fails the architecture rule
`TestingNeverReferencedByProduction`.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Search.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` (no engine SDK) |
| Namespaces | `SharedKernel.Testing.Search` |

## Quick start

```csharp
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Testing.Search;
using Xunit;

public sealed class ProductIndexerTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("0a0a0a0a-0000-4000-8000-00000000000a"));

    private static SearchIndexDefinition Definition() =>
        new SearchIndexDefinitionBuilder("products")
            .TenantField("TenantId")
            .Field("Name", SearchFieldKind.Text, searchable: true)
            .Field("Status", SearchFieldKind.Keyword, filterable: true, facetable: true)
            .Field("TenantId", SearchFieldKind.Keyword, filterable: true)
            .Build()
            .Value;

    [Fact]
    public async Task Indexed_products_are_found_only_in_their_tenant()
    {
        var index = new InMemorySearchIndex<ProductDocument>(Definition());
        var indexer = new ProductIndexer(index);   // your class, taking ISearchIndex<ProductDocument>

        await indexer.IndexAsync(new Product("widget-1", "Blue widget", Tenant), CancellationToken.None);

        Assert.True(index.WasIndexed("widget-1"));
        var request = SearchRequest.Default with { FreeText = "widget" };
        var found = await index.SearchAsync(request, TenantScope.For(Tenant), CancellationToken.None);
        Assert.Single(found.Value.Hits);

        var global = await index.SearchAsync(request, TenantScope.Global, CancellationToken.None);
        Assert.Equal("search.tenant_scope_missing", global.Error.Code);
    }
}
```

`ProductDocument` implements `ISearchDocument` (`DocumentId`) and has public `Name`, `Status` and `TenantId`
(a `string` holding the tenant GUID) properties.

## How it works

- **Store.** A thread-safe dictionary keyed by `DocumentId`; every write is an upsert and is searchable immediately.
  Document ids must match `A-Z a-z 0-9 - _` or the write fails with `search.invalid_document_id` (per item in bulk
  writes, which never collapse into an outer failure).
- **Validation, before any work.** Sort fields must be sortable, filter fields filterable, facet and numeric-stats
  fields facetable, and `Page × PageSize` must not exceed the definition's `MaxTotalHits`.
- **Tenancy.** When the definition declares a `TenantField`, `SearchAsync`, `CountAsync` and `DeleteByFilterAsync`
  fail with `search.tenant_scope_missing` for `TenantScope.Global`, `EnumerateAsync` throws `SearchStreamException`,
  and a tenant scope adds an equality clause on that field. `GetAsync` returns `search.document_not_found` for a
  document of another tenant.
- **Field access.** Filter, sort and facet fields resolve to public `TDocument` properties by name, ignoring case.
  A field with no matching property throws `InvalidOperationException` — a bug in the test, not a `Result`.
- **Simplified:** `TotalHits` and `CountAsync` are always exact (`TotalHitsAccuracy.Exact`); free text is one
  case-insensitive substring match over `Text` fields declared searchable — no relevance ranking, `MatchAllTerms`
  ignored, hits in store order unless sorted; highlights, `ReturnFields`, synonyms and stop words are not applied;
  bulk throttling is recorded in `LastBulkWriteOptions` but not applied; receipts carry a fixed `AcceptedAt`
  (2024-01-01T00:00:00Z).
- **No readiness probe.** Real providers register `search-{provider}-{index}` probes; the fakes do not.
- **Independent fakes.** The index, provisioner and descriptor do not share state. Pass the same definition to each
  when they must agree.
- **Lifetime.** `AddInMemorySearchIndex<TDocument>` registers a **singleton** (production registers the index scoped),
  so the recorded history outlives the scope the system under test ran in.

## Recipes

### 1. Seed a corpus and test a query handler

```csharp
var index = new InMemorySearchIndex<ProductDocument>(Definition());
index.Seed(new ProductDocument { DocumentId = "a-1", Name = "Anvil", Status = "active", TenantId = Tenant.ToString() });
index.Seed(new ProductDocument { DocumentId = "b-2", Name = "Bolt", Status = "retired", TenantId = Tenant.ToString() });

var request = SearchRequest.Default with { Filter = SearchFilter.Eq("Status", "active"), Facets = ["Status"] };
var result = await index.SearchAsync(request, TenantScope.For(Tenant), CancellationToken.None);

Assert.Equal("a-1", Assert.Single(result.Value.Hits).Document.DocumentId);
```

`Seed` stores without recording in `IndexedDocumentIds`.

### 2. Assert a wrong-role field is rejected

```csharp
var request = SearchRequest.Default with { Sort = [SearchSort.Ascending("Name")] };   // Name is not sortable

var result = await index.SearchAsync(request, TenantScope.For(Tenant), CancellationToken.None);

Assert.Equal("search.field_not_sortable", result.Error.Code);
```

### 3. Test the write-rejected path

```csharp
index.SimulateFailure = true;

var result = await indexer.IndexAsync(product, CancellationToken.None);

Assert.Equal("search.write_rejected", result.Error.Code);
```

`SimulateFailure` affects index, delete, delete-by-filter and clear; reads are unaffected.

### 4. Test provisioning and cutover

```csharp
var provisioner = new InMemorySearchIndexProvisioner();

await new CatalogIndexSetup(provisioner).RunAsync(CancellationToken.None);   // your startup code

Assert.Contains("products", provisioner.RegisteredIndexNames);
```

`EnsureIndexAsync` is idempotent and additive: a new field is added, a changed role fails with
`search.index_definition_conflict`. `CutoverAsync` copies the staging definition to the live name and fails with
`search.cutover_failed` when the staging index is unknown. `VerifyRegisteredIndexesAsync` always succeeds — it proves
the call is wired, not that a schema drifted.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddInMemorySearchIndex<TDocument>(this IServiceCollection, SearchIndexDefinition definition)` | `InMemorySearchIndex<TDocument>` and `ISearchIndex<TDocument>` → the same singleton; once per document type |
| `AddInMemorySearchProvisioning(this IServiceCollection, string providerName = "in-memory-fake")` | `ISearchIndexProvisioner` → `InMemorySearchIndexProvisioner`, `ISearchProviderDescriptor` → `InMemorySearchProviderDescriptor` (singletons) |

### Types

| Type | Implements | Test helpers |
| --- | --- | --- |
| `InMemorySearchIndex<TDocument>` (`TDocument : class, ISearchDocument`) | `ISearchIndex<TDocument>`; constructor `(SearchIndexDefinition definition)` | `IndexedDocumentIds`, `DeletedDocumentIds`, `WasIndexed(id)`, `WasDeleted(id)`, `IsSearchable(id)`, `LastBulkWriteOptions`, `Seed(document)`, `SimulateFailure`, `Reset()` |
| `InMemorySearchIndexProvisioner` | `ISearchIndexProvisioner` | `RegisteredIndexNames`, `SimulateFailure`, `Reset()` |
| `InMemorySearchProviderDescriptor` | `ISearchProviderDescriptor`; constructor `(string providerName = "in-memory-fake")` | `ProviderName`, `MaxTotalHits`, `MaxFacetValues` (settable), `RegisterIndex(name, definition)`, `Reset()` |

`InMemorySearchProviderDescriptor.Validate(indexName, request)` returns `search.index_not_found` for an index not
registered with `RegisterIndex`, then runs the same field-role and page-ceiling checks as the index (against its own
`MaxTotalHits`).

### Errors

| Code | Returned when |
| --- | --- |
| `search.tenant_scope_missing` | `TenantScope.Global` on a tenant-declaring index |
| `search.field_not_filterable` / `_sortable` / `_facetable` | A request uses a field outside its declared role |
| `search.pagination_limit_exceeded` | `Page × PageSize` exceeds `MaxTotalHits` |
| `search.invalid_document_id` | A document id outside `A-Z a-z 0-9 - _` |
| `search.document_not_found` | `GetAsync` for a missing id or another tenant's document |
| `search.write_rejected` | `SimulateFailure` is set |
| `search.write_timeout` | `WaitUntilSearchableAsync` with a receipt this instance did not issue |
| `search.index_definition_conflict` / `search.cutover_failed` / `search.index_not_found` | Provisioner and descriptor paths above |

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Search.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Search.Testing/SharedKernel.Search.Testing.Tests),
which prove the three fakes against the `SharedKernel.Search.Abstractions` contract — validation order, tenant
fail-closed, filter evaluation, bulk partial failure and provisioning. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `TestRequestContext` and fakers. Relevance, estimated counts and engine-specific capabilities
(`IInstantSearch<T>`, `IAnalyticsSearch<T>`, …) are tested against the real engine.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Assert on ranking or on a lower-bound count | Assert on membership, filters and sort order | The fake has no relevance and always counts exactly |
| Store the tenant field as a `Guid` property | Use a `string` holding the tenant's "D" GUID | `GetAsync` compares the tenant field as a string; a non-string value reads as another tenant |
| Name a filter field with no matching property | Keep definition fields and document properties aligned | An unresolved field throws `InvalidOperationException` instead of returning a `Result` |
| Expect the provisioner and descriptor to see the index's definition | Pass the same definition to each fake | The three fakes keep independent state |
| Resolve a scoped `ISearchIndex<T>` to assert after the scope ends | Resolve `InMemorySearchIndex<TDocument>` from the root provider | It is registered as a singleton on purpose |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
