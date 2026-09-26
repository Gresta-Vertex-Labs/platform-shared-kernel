using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace CatalogApi.Features.BackOffice;

/// <summary>The same neutral contract the storefront uses, against a different engine and document type.</summary>
public sealed record CountOrderLines(TenantId TenantId, string? Region) : IQuery<SearchCount>;

public sealed class CountOrderLinesHandler(ISearchIndex<OrderLineDocument> index) : IQueryHandler<CountOrderLines, SearchCount>
{
    public Task<Result<SearchCount>> Handle(CountOrderLines query, CancellationToken cancellationToken)
    {
        SearchFilter? filter = query.Region is null
            ? null
            : SearchFilter.Eq(OrderLineFields.Region, SearchValue.From(query.Region));

        return index.CountAsync(filter, TenantScope.For(query.TenantId), cancellationToken);
    }
}
