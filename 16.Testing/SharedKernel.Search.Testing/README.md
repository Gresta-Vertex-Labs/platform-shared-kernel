# SharedKernel.Search.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**In-memory doubles for `SharedKernel.Search.Abstractions`, so indexing and search code runs in a unit test without
Meilisearch or Elasticsearch.** The index evaluates the neutral `SearchFilter` AST, paging, counts and the mandatory
`TenantScope` against the same `SearchIndexDefinition` the real providers validate against.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Search.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Search`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `InMemorySearchIndex<TDocument>` | `ISearchIndex<TDocument>` | Upsert-only store keyed by `DocumentId`. Validates every request against its definition (undeclared or non-filterable fields, page ceiling) and fails closed with `SearchErrors.TenantScopeMissing` when a tenant-declaring index is called with `TenantScope.Global`. `Seed`, `IndexedDocumentIds`, `DeletedDocumentIds`, `WasIndexed(id)`, `WasDeleted(id)`, `IsSearchable(id)`, `LastBulkWriteOptions`, `SimulateFailure`, `Reset()` |
| `InMemorySearchIndexProvisioner` | `ISearchIndexProvisioner` | Ensure / exists / delete / cutover / verify, recorded in `RegisteredIndexNames`; `SimulateFailure`, `Reset()` |
| `InMemorySearchProviderDescriptor` | `ISearchProviderDescriptor` | Zero-I/O ceilings (`MaxTotalHits`, `MaxFacetValues`) and `Validate`; `RegisterIndex(name, definition)`, `Reset()` |

Deliberate simplifications: `TotalHits` is always `TotalHitsAccuracy.Exact`; free text is a case-insensitive
substring match over searchable fields (no relevance ranking, `MatchAllTerms` ignored); a filter field that does not
resolve to a `TDocument` property throws `InvalidOperationException` (a test bug, not an expected failure).
Readiness is not simulated — the real providers register `search-{provider}-{index}` probes.

## Registration

```csharp
services.AddInMemorySearchIndex<ProductDocument>(definition);   // once per document type
services.AddInMemorySearchProvisioning();                       // provisioner + provider descriptor
```

Every registration is a singleton. For the index that deliberately differs from production (scoped): the recorded
history has to outlive the system under test's scope so the test can assert afterwards.

## Example

```csharp
var index = new InMemorySearchIndex<ProductDocument>(definition);   // declares TenantField "tenantId"
var indexer = new ProductIndexer(index);

await indexer.IndexAsync(product, ct);

index.WasIndexed(product.DocumentId).Should().BeTrue();

var found = await index.SearchAsync(request, TenantScope.For(product.TenantId), ct);
found.Value.Hits.Should().ContainSingle();

var leaked = await index.SearchAsync(request, TenantScope.Global, ct);
leaked.Error.Code.Should().Be("search.tenant_scope_missing");
```

## Related packages

- References `SharedKernel.Search.Abstractions` only; no engine SDK.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `FakeClock`, `InMemoryLogger`, `TestRequestContext`,
  fakers.
- Real-engine tests use the Meilisearch and Elasticsearch container fixtures in the non-packable
  `SharedKernel.Testing.Internal`.
