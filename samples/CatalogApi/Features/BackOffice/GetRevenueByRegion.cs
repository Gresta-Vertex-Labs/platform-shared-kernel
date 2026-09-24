using SharedKernel.Application;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;

namespace CatalogApi.Features.BackOffice;

/// <summary>
/// Structured aggregations — ElasticSearch-exclusive. Meilisearch offers facet counts and numeric min/max and nothing
/// else; the gap is absence, not degree, which is why this contract lives in the provider package rather than being
/// watered down into the neutral surface.
/// </summary>
public sealed record GetRevenueByRegion(string TenantId) : IQuery<AggregationResultSet>;

public sealed class GetRevenueByRegionHandler(IAnalyticsSearch<OrderLineDocument> analytics)
    : IQueryHandler<GetRevenueByRegion, AggregationResultSet>
{
    /// <summary>The terms aggregation of order lines per region.</summary>
    public const string ByRegion = "by_region";

    /// <summary>The cardinality aggregation of distinct categories.</summary>
    public const string DistinctCategories = "distinct_categories";

    public Task<Result<AggregationResultSet>> Handle(GetRevenueByRegion query, CancellationToken cancellationToken)
    {
        var aggregations = new[]
        {
            AggregationRequest.Terms(
                ByRegion,
                OrderLineFields.Region,
                size: 10,
                subAggregations: [AggregationRequest.Stats("revenue", OrderLineFields.Revenue)]),
            AggregationRequest.Cardinality(DistinctCategories, OrderLineFields.Category),
        };

        return analytics.AggregateAsync(filter: null, aggregations, TenantScope.Of(query.TenantId), cancellationToken);
    }
}
