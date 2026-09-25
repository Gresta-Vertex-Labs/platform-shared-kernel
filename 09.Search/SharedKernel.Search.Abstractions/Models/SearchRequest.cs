using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Constants;

namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// A neutral, provider-independent search request — free text, structured filter, sort, paging,
/// faceting, highlighting, and field projection.
/// </summary>
/// <remarks>
/// <para>
/// A plain sealed record with public <c>init</c> members and a static <see cref="Default"/> — the
/// query-builder is optional sugar, so
/// <c>SearchRequest.Default with { FreeText = "x", Facets = ["status"] }</c> is a fully supported
/// construction path. The executor validates every request with the same validator the builder uses,
/// so a hand-constructed invalid request fails before any I/O.
/// </para>
/// <para>
/// There is deliberately no <c>TenantScope</c> member here — see <see cref="TenantScope"/>'s own
/// remarks for why.
/// </para>
/// </remarks>
public sealed record SearchRequest
{
    /// <summary>Gets the free-text query, or <see langword="null"/> for no free-text component.</summary>
    public string? FreeText { get; init; }

    /// <summary>Gets a value indicating whether every free-text term must match (AND) rather than any (OR).</summary>
    public bool MatchAllTerms { get; init; }

    /// <summary>Gets the fields free text is matched against, or empty for every searchable field.</summary>
    public IReadOnlyList<string> SearchFields { get; init; } = [];

    /// <summary>Gets the structured filter predicate, or <see langword="null"/> for no filter.</summary>
    public SearchFilter? Filter { get; init; }

    /// <summary>Gets the sort entries, or empty for engine relevance order.</summary>
    public IReadOnlyList<SearchSort> Sort { get; init; } = [];

    /// <summary>Gets the 1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Gets the page size.</summary>
    public int PageSize { get; init; } = SearchWellKnown.DefaultPageSize;

    /// <summary>Gets the fields to return facet value/count distributions for.</summary>
    public IReadOnlyList<string> Facets { get; init; } = [];

    /// <summary>Gets the numeric fields to return min/max facet statistics for.</summary>
    public IReadOnlyList<string> NumericFacetStats { get; init; } = [];

    /// <summary>Gets the highlight request, or <see langword="null"/> for no highlighting.</summary>
    public HighlightRequest? Highlight { get; init; }

    /// <summary>Gets the fields to return on each hit, or empty for every retrievable field.</summary>
    public IReadOnlyList<string> ReturnFields { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether an exact <see cref="SearchResults{TDocument}.TotalHits"/> is
    /// required. Set this when you intend to call <see cref="SearchResults{TDocument}.ToPagedList"/> —
    /// both engines cost materially more to compute an exact count.
    /// </summary>
    public bool RequireExactTotalHits { get; init; }

    /// <summary>Gets a request with every member at its default value.</summary>
    public static SearchRequest Default { get; } = new();
}
