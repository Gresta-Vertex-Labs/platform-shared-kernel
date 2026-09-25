using SharedKernel.Application;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace CatalogApi.Features.Storefront;

/// <summary>
/// Counts a tenant's products, optionally in one category. The count says how much it can be trusted: at the index's
/// ceiling Meilisearch reports a lower bound, not a figure it cannot vouch for.
/// </summary>
public sealed record CountProducts(string TenantId, string? Category) : IQuery<SearchCount>;

public sealed class CountProductsHandler(ISearchIndex<ProductDocument> index) : IQueryHandler<CountProducts, SearchCount>
{
    public Task<Result<SearchCount>> Handle(CountProducts query, CancellationToken cancellationToken)
    {
        SearchFilter? filter = query.Category is null
            ? null
            : SearchFilter.Eq(ProductFields.Category, SearchValue.From(query.Category));

        return index.CountAsync(filter, TenantScope.Of(query.TenantId), cancellationToken);
    }
}
