using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Abstractions.Querying;

namespace CatalogApi.Features.Storefront;

/// <summary>Free text + filters + sort + facets + highlighting + paging, all through the neutral builder.</summary>
public sealed record SearchProducts(
    TenantId TenantId,
    string? Text,
    string? Category,
    double? MaxPrice,
    bool? InStockOnly,
    int Page,
    int PageSize) : IQuery<SearchResults<ProductDocument>>;

public sealed class SearchProductsHandler(ISearchIndex<ProductDocument> index)
    : IQueryHandler<SearchProducts, SearchResults<ProductDocument>>
{
    public Task<Result<SearchResults<ProductDocument>>> Handle(SearchProducts query, CancellationToken cancellationToken)
    {
        var filters = new List<SearchFilter>();
        if (query.Category is not null)
        {
            filters.Add(SearchFilter.Eq(ProductFields.Category, SearchValue.From(query.Category)));
        }

        if (query.MaxPrice is { } ceiling)
        {
            filters.Add(SearchFilter.Between(ProductFields.Price, from: null, to: SearchValue.From(ceiling)));
        }

        if (query.InStockOnly == true)
        {
            filters.Add(SearchFilter.Eq(ProductFields.InStock, SearchValue.From(true)));
        }

        var search = SearchQuery.New()
            .Matching(query.Text)
            .Page(query.Page, query.PageSize)
            .Faceting(ProductFields.Category, ProductFields.Brand)
            .Highlighting(new HighlightRequest { Fields = [ProductFields.Name, ProductFields.Description] });

        // Repeated Where(...) calls AND together — they never replace a prior call.
        foreach (var filter in filters)
        {
            search = search.Where(filter);
        }

        // A malformed query fails Build() and never reaches the engine; one past this index's ceilings fails
        // SearchAsync's pre-flight check, also before any I/O. Either way the client gets the 400.
        return search.Build()
            .Bind(request => index.SearchAsync(request, TenantScope.For(query.TenantId), cancellationToken));
    }
}
