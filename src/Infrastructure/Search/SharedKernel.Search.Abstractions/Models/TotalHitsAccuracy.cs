namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The accuracy qualifier attached to <see cref="SearchResults{TDocument}.TotalHits"/>.
/// </summary>
/// <remarks>
/// Three values, not two: labelling Meilisearch's estimated total as <see cref="LowerBound"/> would
/// itself be a small lie — the estimate can be over or under, so it is neither exact nor a bound.
/// <see cref="Estimated"/> is its own value.
/// </remarks>
public enum TotalHitsAccuracy
{
    /// <summary>
    /// <see cref="SearchResults{TDocument}.TotalHits"/> is exact — ElasticSearch
    /// <c>hits.total.relation == "eq"</c>, or a Meilisearch <c>page</c>/<c>hitsPerPage</c> exact total.
    /// </summary>
    Exact = 0,

    /// <summary>
    /// <see cref="SearchResults{TDocument}.TotalHits"/> is a lower bound — ElasticSearch
    /// <c>hits.total.relation == "gte"</c> (the <c>track_total_hits</c> ceiling was reached).
    /// </summary>
    LowerBound = 1,

    /// <summary>
    /// <see cref="SearchResults{TDocument}.TotalHits"/> is an estimate that may be over or under —
    /// Meilisearch <c>offset</c>/<c>limit</c> <c>estimatedTotalHits</c>.
    /// </summary>
    Estimated = 2,
}
