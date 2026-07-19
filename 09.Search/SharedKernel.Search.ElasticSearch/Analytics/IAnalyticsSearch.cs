using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>
/// The ElasticSearch-exclusive aggregation contract — declared here, not in
/// <c>SharedKernel.Search.Abstractions</c>, so referencing it takes a compile-time dependency on this
/// package.
/// </summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
public interface IAnalyticsSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>
    /// Runs <paramref name="aggregations"/> over the documents matching <paramref name="filter"/> (or
    /// the whole index when <paramref name="filter"/> is <see langword="null"/>), scoped to
    /// <paramref name="tenantScope"/> — a mandatory separate parameter, never smuggled into the
    /// aggregation body, for the same reason it is mandatory on the neutral read path.
    /// </summary>
    Task<Result<AggregationResultSet>> AggregateAsync(
        SearchFilter? filter,
        IReadOnlyCollection<AggregationRequest> aggregations,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
