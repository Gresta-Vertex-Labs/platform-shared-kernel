using SharedKernel.Contracts.Pagination;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Errors;

namespace SharedKernel.Search.Abstractions.Models;

/// <summary>The result of a <see cref="SearchRequest"/> — hits, paging, facets, and total-hit accuracy.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// <para>
/// <see cref="TotalHits"/> is <see cref="long"/>, not <see cref="int"/> — ElasticSearch hit counts
/// routinely exceed <see cref="int.MaxValue"/> on analytics indices, and
/// <see cref="PagedList{T}.TotalCount"/> is an <see cref="int"/> treated as exact.
/// </para>
/// <para>
/// This domain declares its own result type rather than returning <see cref="PagedList{T}"/> directly
/// because <see cref="PagedList{T}"/> is <see langword="sealed"/> (so this type cannot extend it),
/// every property is <c>private init</c> with an <c>internal</c> constructor (so facets, highlights,
/// rank, duration, and an accuracy qualifier have nowhere to live on it), its
/// <see cref="PagedList{T}.TotalCount"/> is an <see cref="int"/> treated as exact, and its
/// <c>Create</c> hard-throws on an invalid page. Routing a Meilisearch estimate through it would
/// publish an estimate as fact. <see cref="ToPagedList"/> is the guarded, lossy bridge instead.
/// </para>
/// </remarks>
public sealed record SearchResults<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>Gets the hits on the current page.</summary>
    public IReadOnlyList<SearchHit<TDocument>> Hits { get; init; } = [];

    /// <summary>Gets the total number of matching documents, qualified by <see cref="Accuracy"/>.</summary>
    public long TotalHits { get; init; }

    /// <summary>Gets the accuracy qualifier for <see cref="TotalHits"/>.</summary>
    public TotalHitsAccuracy Accuracy { get; init; }

    /// <summary>Gets the 1-based page number returned.</summary>
    public int Page { get; init; }

    /// <summary>Gets the page size returned.</summary>
    public int PageSize { get; init; }

    /// <summary>Gets the facet results keyed by facet field name.</summary>
    public IReadOnlyDictionary<string, FacetResult> Facets { get; init; } = new Dictionary<string, FacetResult>();

    /// <summary>Gets the time the provider took to execute this search.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Gets an empty result set.</summary>
    public static SearchResults<TDocument> Empty { get; } = new()
    {
        Hits = [],
        TotalHits = 0,
        Accuracy = TotalHitsAccuracy.Exact,
        Page = 1,
        PageSize = 0,
        Facets = new Dictionary<string, FacetResult>(),
        Duration = TimeSpan.Zero,
    };

    /// <summary>
    /// Projects this result set into a <see cref="PagedList{T}"/> — the only sanctioned bridge from
    /// <c>09.Search</c> to <c>04.Contracts</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="FacetResult"/>s, <see cref="SearchHit{TDocument}.Rank"/>, and
    /// <see cref="SearchHit{TDocument}.Highlights"/> are dropped by this projection — a
    /// <see cref="PagedList{T}"/> has nowhere to carry them.
    /// </remarks>
    /// <returns>
    /// A failed <see cref="Result{T}"/> with <see cref="SearchErrors.TotalHitsNotExact"/> when
    /// <see cref="Accuracy"/> is not <see cref="TotalHitsAccuracy.Exact"/>; with
    /// <see cref="SearchErrors.TotalHitsOverflow"/> when <see cref="TotalHits"/> exceeds
    /// <see cref="int.MaxValue"/>; with <see cref="SearchErrors.InvalidSearchRequest"/> when
    /// <see cref="Page"/> or <see cref="PageSize"/> is less than 1; otherwise a successful
    /// <see cref="Result{T}"/> carrying the projected <see cref="PagedList{T}"/>.
    /// </returns>
    public Result<PagedList<TDocument>> ToPagedList()
    {
        if (Accuracy != TotalHitsAccuracy.Exact)
        {
            return Result<PagedList<TDocument>>.Failure(SearchErrors.TotalHitsNotExact());
        }

        if (TotalHits > int.MaxValue)
        {
            return Result<PagedList<TDocument>>.Failure(SearchErrors.TotalHitsOverflow(TotalHits));
        }

        if (Page < 1)
        {
            return Result<PagedList<TDocument>>.Failure(
                SearchErrors.InvalidSearchRequest($"Page must be at least 1; received {Page}."));
        }

        if (PageSize < 1)
        {
            return Result<PagedList<TDocument>>.Failure(
                SearchErrors.InvalidSearchRequest($"PageSize must be at least 1; received {PageSize}."));
        }

        var items = Hits.Select(hit => hit.Document).ToArray();
        return Result<PagedList<TDocument>>.Success(
            PagedList<TDocument>.Create(items, Page, PageSize, (int)TotalHits));
    }
}
