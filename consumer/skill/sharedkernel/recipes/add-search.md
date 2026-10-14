---
proven-by:
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/ProductDocument.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/CatalogIndexes.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/SearchQueries.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/ProductIndexer.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/Search/CatalogDefinitions.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/Search/CatalogProvisioning.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/CatalogInfrastructure.cs
  - samples/Shop/Catalog/Shop.Catalog.Tests/CatalogPipelineTests.cs
unproven:
  - Engine-only features (Meilisearch tenant tokens, Elasticsearch aggregations and point-in-time paging): the reference platform uses only the neutral contract
  - Rebuilding an index behind an alias cutover: the reference platform provisions once at startup
---
# Add search

**Use when:** users search your data by words (typo-tolerant storefront search on Meilisearch, back-office search
with facets on Elasticsearch), kept in a read model your commands write to. **Not when:** you search by meaning
(embeddings and vectors); that is add-ai.md. A plain filtered list from your own database is a repository query
(add-persistence.md).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Search.Abstractions` | `ISearchIndex<T>`, `ISearchDocument`, `SearchRequest`, `SearchFilter`, the index definition builder |
| Infrastructure | `SharedKernel.Search.Meilisearch` | Meilisearch provider |
| Infrastructure | `SharedKernel.Search.ElasticSearch` | Elasticsearch provider (only if you use it) |
| Tests | `SharedKernel.Search.Testing` | In-memory index with the same definition checks and tenant rule |

## Steps

1. **Application: the document and its field names.** A document implements `ISearchDocument`; its field names are
   constants, so a filter or facet cannot misspell one. Each index gets its own document type: two providers
   registered for one type would shadow each other.

```csharp
using System.Text.Json.Serialization;
using SharedKernel.Search.Abstractions.Abstractions;

public static class ProductFields
{
    public const string DocumentId = "documentId";
    public const string TenantId = "tenantId";
    public const string Name = "name";
    public const string Category = "category";
    public const string Price = "price";
}

public sealed class ProductDocument : ISearchDocument
{
    [JsonPropertyName(ProductFields.DocumentId)]
    public required string DocumentId { get; init; }

    [JsonPropertyName(ProductFields.TenantId)]
    public required string TenantId { get; init; }

    [JsonPropertyName(ProductFields.Name)]
    public required string Name { get; init; }

    [JsonPropertyName(ProductFields.Category)]
    public required string Category { get; init; }

    [JsonPropertyName(ProductFields.Price)]
    public required double Price { get; init; }
}
```

2. **Application: query with the caller's tenant.** Every read takes a `TenantScope`; build it from the caller's
   tenant and fail closed when there is none.

```csharp
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

public sealed class SearchProductsHandler(IRequestContext caller, ISearchIndex<ProductDocument> storefront)
    : IQueryHandler<SearchProductsQuery, ProductSearchPage>
{
    public async Task<Result<ProductSearchPage>> Handle(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result<ProductSearchPage>.Failure(tenant.Error);
        }

        var request = new SearchRequest
        {
            FreeText = query.Text,
            Filter = string.IsNullOrWhiteSpace(query.Category)
                ? null
                : SearchFilter.Eq(ProductFields.Category, SearchValue.From(query.Category)),
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 50),
        };

        var results = await storefront.SearchAsync(request, TenantScope.For(tenant.Value), cancellationToken);
        // ...map results.Value.Hits (hit.Document) and results.Value.TotalHits...
    }
}
```

   Facets are a `SearchRequest` property too (`Facets = [ProductFields.Brand]`), read back from
   `results.Value.Facets`.

3. **Application: write the document when the aggregate changes.** Index inside the command, so a failed write fails
   the command:

```csharp
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;

var stored = await storefront.IndexAsync(document, SearchWriteConsistency.Searchable, cancellationToken);
if (stored.IsFailure)
{
    return Result.Failure(stored.Error);
}
```

4. **Infrastructure: declare the index once, register it, provision it.** The definition names the primary key,
   the tenant field and every field's kind and role:

```csharp
using SharedKernel.Search.Abstractions.Models;

public static void Storefront(SearchIndexDefinitionBuilder index) =>
    index
        .PrimaryKey(ProductFields.DocumentId)
        .TenantField(ProductFields.TenantId)
        .Field(ProductFields.DocumentId, SearchFieldKind.Keyword, filterable: true)
        .Field(ProductFields.TenantId, SearchFieldKind.Keyword, filterable: true)
        .Field(ProductFields.Name, SearchFieldKind.Text, searchable: true)
        .Field(ProductFields.Category, SearchFieldKind.Keyword, filterable: true, facetable: true)
        .Field(ProductFields.Price, SearchFieldKind.Decimal, filterable: true, sortable: true);
```

```csharp
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.Meilisearch.Extensions;

services
    .AddSharedKernelMeilisearchSearch(configuration)
    .AddIndex<ProductDocument>(CatalogIndexes.Storefront, CatalogDefinitions.Storefront)
    .Build();
services
    .AddSharedKernelElasticSearchSearch(configuration)
    .AddIndex<ProductAdminDocument>(CatalogIndexes.BackOffice, CatalogIndexes.BackOffice, CatalogDefinitions.BackOffice)
    .Build();
```

   On Elasticsearch `AddIndex` takes a read alias and a write alias. Create the indexes before traffic with a hosted
   service over `ISearchIndexProvisioner`: build each `SearchIndexDefinition` with a
   `new SearchIndexDefinitionBuilder(name)` and the same configure method, then call
   `EnsureIndexAsync(definition, ct)` for each index the provider registered (`ISearchProviderDescriptor.RegisteredIndexes`).
   An existing index with the same declaration is left alone; a different one returns a failure, which should stop
   the host.

5. **Api/Worker:** `builder.Services.AddHealthChecks().AddSharedKernelReadiness()` maps each engine's readiness
   probe. Mark the startup gate ready after provisioning has run (new-service.md).

6. **Configuration:** `Search:Meilisearch` and `Search:ElasticSearch`; the keys are in each provider's README.

## Test it

Register an in-memory index with the same definition; it rejects a bad declaration and enforces the tenant scope.

```csharp
using SharedKernel.Testing.Search;

services.AddInMemorySearchIndex<ProductDocument>(
    CatalogDefinitions.Build(CatalogIndexes.Storefront, CatalogDefinitions.Storefront)
);
```

```csharp
using FluentAssertions;

var found = await fabrikam.SendAsync(new SearchProductsQuery("keyboard", null));

found.IsSuccess.Should().BeTrue();
found.Value.Hits.Should().BeEmpty("Fabrikam must never see Contoso's products");
```

`CatalogDefinitions.Build` is the service's own helper: it runs the step 4 configure method on a
`SearchIndexDefinitionBuilder` and throws when `Build()` fails. `InMemorySearchIndex<T>.WasIndexed(id)` asserts that
a command wrote the document. The pipeline harness is in write-tests.md.

## Check

- **SK0024**: a literal field name in `SearchFilter.Eq`/`In`/`Between` or a query builder; use the field constants.
  A typo is a silent zero-result search on Elasticsearch.
- **SK0025**: a `NEST` or `Elasticsearch.Net` type in your code.
- **SK0030**: an `IndexAsync` or `DeleteAsync` whose `Result` nobody reads.
- A search without a `TenantScope` does not compile; a `TenantField` that is not declared `filterable` fails the
  definition's `Build()`.
- A provisioning mismatch (the index exists with another declaration) fails startup, not the first query.

## Read next

- `SharedKernel.Search.Abstractions`
- `SharedKernel.Search.Meilisearch`
- `SharedKernel.Search.ElasticSearch`
- `SharedKernel.Search.Testing`
