using SharedKernel.Application;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Suggest;

namespace CatalogApi.Features.BackOffice;

/// <summary>
/// The completion suggester — ElasticSearch-exclusive. Returns suggestion STRINGS with weights, not documents: a
/// different data structure and a different result shape from Meilisearch's instant search, which is exactly why
/// neither was neutralised into a shared "type-ahead" contract.
/// </summary>
public sealed record SuggestOrderLines(string TenantId, string Prefix, bool Fuzzy) : IQuery<IReadOnlyList<SearchSuggestion>>;

public sealed class SuggestOrderLinesHandler(ISuggestSearch<OrderLineDocument> suggest)
    : IQueryHandler<SuggestOrderLines, IReadOnlyList<SearchSuggestion>>
{
    public Task<Result<IReadOnlyList<SearchSuggestion>>> Handle(SuggestOrderLines query, CancellationToken cancellationToken) =>
        suggest.SuggestAsync(Catalog.OrderLineSuggestField, query.Prefix, TenantScope.Of(query.TenantId), size: 5, fuzzy: query.Fuzzy, cancellationToken);
}
