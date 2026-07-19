using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Meilisearch.Instant;

/// <summary>
/// The Meilisearch-exclusive instant-search contract — declared here, not in
/// <c>SharedKernel.Search.Abstractions</c>, so referencing it takes a compile-time dependency on this
/// package.
/// </summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// Meilisearch applies typo tolerance automatically with word-length thresholds and ships prefix
/// matching, crop markers, and a dedicated facet-value-search endpoint as first-class primitives. The
/// ElasticSearch equivalents (fuzziness, <c>match_phrase_prefix</c>, a terms aggregation with an
/// include regex) have materially different edit-distance behaviour and cost profiles, so a neutral
/// <c>TypoTolerant = true</c> boolean would be a no-op on one engine and a query-plan change on the
/// other. Not neutralised.
/// </remarks>
public interface IInstantSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>Executes an instant/type-ahead search.</summary>
    Task<Result<SearchResults<TDocument>>> InstantAsync(
        InstantSearchRequest request, TenantScope tenantScope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Type-ahead search <em>within</em> a facet's own values (<c>POST /indexes/{uid}/facet-search</c>)
    /// — not the facet distribution returned by <c>ISearchIndex.SearchAsync</c>, and must not be
    /// conflated with it.
    /// </summary>
    Task<Result<IReadOnlyList<FacetValue>>> SearchFacetValuesAsync(
        string facetField,
        string facetQuery,
        SearchFilter? filter,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
