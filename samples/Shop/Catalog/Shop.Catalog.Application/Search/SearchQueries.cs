using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace Shop.Catalog.Application.Search;

/// <summary>One search hit.</summary>
public sealed record ProductHit(
    Guid Id,
    string Sku,
    string Name,
    string Brand,
    string Category,
    double Price
);

/// <summary>A page of storefront results.</summary>
public sealed record ProductSearchPage(IReadOnlyList<ProductHit> Hits, long TotalHits);

/// <summary>Full-text storefront search (Meilisearch), optionally narrowed to one category.</summary>
[RequirePermission(CatalogPermissions.Read)]
public sealed record SearchProductsQuery(
    string? Text,
    string? Category,
    int Page = 1,
    int PageSize = 20
) : IQuery<ProductSearchPage>;

public sealed class SearchProductsHandler(
    IRequestContext caller,
    ISearchIndex<ProductDocument> storefront
) : IQueryHandler<SearchProductsQuery, ProductSearchPage>
{
    public async Task<Result<ProductSearchPage>> Handle(
        SearchProductsQuery query,
        CancellationToken cancellationToken
    )
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

        var results = await storefront.SearchAsync(
            request,
            TenantScope.For(tenant.Value),
            cancellationToken
        );
        if (results.IsFailure)
        {
            return Result<ProductSearchPage>.Failure(results.Error);
        }

        var hits = results
            .Value.Hits.Select(hit => new ProductHit(
                Guid.Parse(hit.Document.DocumentId),
                hit.Document.Sku,
                hit.Document.Name,
                hit.Document.Brand,
                hit.Document.Category,
                hit.Document.Price
            ))
            .ToList();
        return Result<ProductSearchPage>.Success(
            new ProductSearchPage(hits, results.Value.TotalHits)
        );
    }
}

/// <summary>Back-office results with the number of products per brand.</summary>
public sealed record BackOfficePage(
    IReadOnlyList<ProductHit> Hits,
    IReadOnlyDictionary<string, long> Brands
);

/// <summary>Back-office search (Elasticsearch) with a brand facet.</summary>
[RequirePermission(CatalogPermissions.Manage)]
public sealed record BackOfficeSearchQuery(string? Text) : IQuery<BackOfficePage>;

public sealed class BackOfficeSearchHandler(
    IRequestContext caller,
    ISearchIndex<ProductAdminDocument> backOffice
) : IQueryHandler<BackOfficeSearchQuery, BackOfficePage>
{
    public async Task<Result<BackOfficePage>> Handle(
        BackOfficeSearchQuery query,
        CancellationToken cancellationToken
    )
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result<BackOfficePage>.Failure(tenant.Error);
        }

        var request = new SearchRequest
        {
            FreeText = query.Text,
            Facets = [ProductFields.Brand],
            PageSize = 50,
        };
        var results = await backOffice.SearchAsync(
            request,
            TenantScope.For(tenant.Value),
            cancellationToken
        );
        if (results.IsFailure)
        {
            return Result<BackOfficePage>.Failure(results.Error);
        }

        var hits = results
            .Value.Hits.Select(hit => new ProductHit(
                Guid.Parse(hit.Document.DocumentId),
                hit.Document.Sku,
                hit.Document.Name,
                hit.Document.Brand,
                hit.Document.Category,
                hit.Document.Price
            ))
            .ToList();
        var brands = results.Value.Facets.TryGetValue(ProductFields.Brand, out var facet)
            ? facet.Values.ToDictionary(
                value => value.Value,
                value => value.Count,
                StringComparer.Ordinal
            )
            : new Dictionary<string, long>(StringComparer.Ordinal);
        return Result<BackOfficePage>.Success(new BackOfficePage(hits, brands));
    }
}

/// <summary>A semantic match and its similarity score.</summary>
public sealed record SemanticHit(Guid Id, string Name, string Category, float Score);

/// <summary>Finds products by meaning (embedding + Qdrant), not by words.</summary>
[RequirePermission(CatalogPermissions.Read)]
public sealed record SemanticSearchQuery(string Question, int Limit = 5)
    : IQuery<IReadOnlyList<SemanticHit>>;

public sealed class SemanticSearchHandler(
    IRequestContext caller,
    IEmbeddingGenerator embeddings,
    IVectorCollection<ProductVector> vectors
) : IQueryHandler<SemanticSearchQuery, IReadOnlyList<SemanticHit>>
{
    public async Task<Result<IReadOnlyList<SemanticHit>>> Handle(
        SemanticSearchQuery query,
        CancellationToken cancellationToken
    )
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result<IReadOnlyList<SemanticHit>>.Failure(tenant.Error);
        }

        var embedded = await embeddings.EmbedAsync(query.Question, cancellationToken);
        if (embedded.IsFailure)
        {
            return Result<IReadOnlyList<SemanticHit>>.Failure(embedded.Error);
        }

        var results = await vectors.QueryAsync(
            new VectorQuery
            {
                Vector = embedded.Value.Vector,
                ModelId = embedded.Value.ModelId,
                Limit = Math.Clamp(query.Limit, 1, 20),
            },
            TenantScope.For(tenant.Value),
            cancellationToken
        );
        if (results.IsFailure)
        {
            return Result<IReadOnlyList<SemanticHit>>.Failure(results.Error);
        }

        IReadOnlyList<SemanticHit> hits = results
            .Value.Hits.Select(hit => new SemanticHit(
                Guid.Parse(hit.Record.Id),
                hit.Record.Metadata[ProductVectorFields.Name].AsString,
                hit.Record.Metadata[ProductVectorFields.Category].AsString,
                hit.Score
            ))
            .ToList();
        return Result<IReadOnlyList<SemanticHit>>.Success(hits);
    }
}
