using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Querying;

/// <summary>
/// The sealed, immutable implementation of <see cref="IQueryBuilder"/> shipped in
/// <c>SharedKernel.Search.Abstractions</c> — the only provider-free query builder in this domain.
/// </summary>
public sealed class SearchQueryBuilder : IQueryBuilder
{
    private readonly string? _freeText;
    private readonly bool _matchAllTerms;
    private readonly IReadOnlyList<string> _searchFields;
    private readonly SearchFilter? _filter;
    private readonly IReadOnlyList<SearchSort> _sort;
    private readonly int _page;
    private readonly int _pageSize;
    private readonly IReadOnlyList<string> _facets;
    private readonly IReadOnlyList<string> _numericFacetStats;
    private readonly HighlightRequest? _highlight;
    private readonly IReadOnlyList<string> _returnFields;
    private readonly bool _requireExactTotalHits;

    /// <summary>Initializes a new, empty <see cref="SearchQueryBuilder"/>.</summary>
    public SearchQueryBuilder()
        : this(
            freeText: null,
            matchAllTerms: false,
            searchFields: [],
            filter: null,
            sort: [],
            page: 1,
            pageSize: SearchWellKnown.DefaultPageSize,
            facets: [],
            numericFacetStats: [],
            highlight: null,
            returnFields: [],
            requireExactTotalHits: false)
    {
    }

    private SearchQueryBuilder(
        string? freeText,
        bool matchAllTerms,
        IReadOnlyList<string> searchFields,
        SearchFilter? filter,
        IReadOnlyList<SearchSort> sort,
        int page,
        int pageSize,
        IReadOnlyList<string> facets,
        IReadOnlyList<string> numericFacetStats,
        HighlightRequest? highlight,
        IReadOnlyList<string> returnFields,
        bool requireExactTotalHits)
    {
        _freeText = freeText;
        _matchAllTerms = matchAllTerms;
        _searchFields = searchFields;
        _filter = filter;
        _sort = sort;
        _page = page;
        _pageSize = pageSize;
        _facets = facets;
        _numericFacetStats = numericFacetStats;
        _highlight = highlight;
        _returnFields = returnFields;
        _requireExactTotalHits = requireExactTotalHits;
    }

    /// <inheritdoc />
    public IQueryBuilder Matching(string? freeText) => new SearchQueryBuilder(
        freeText, _matchAllTerms, _searchFields, _filter, _sort, _page, _pageSize,
        _facets, _numericFacetStats, _highlight, _returnFields, _requireExactTotalHits);

    /// <inheritdoc />
    public IQueryBuilder MatchAllTerms(bool matchAll) => new SearchQueryBuilder(
        _freeText, matchAll, _searchFields, _filter, _sort, _page, _pageSize,
        _facets, _numericFacetStats, _highlight, _returnFields, _requireExactTotalHits);

    /// <inheritdoc />
    public IQueryBuilder SearchingIn(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new SearchQueryBuilder(
            _freeText, _matchAllTerms, fields.ToArray(), _filter, _sort, _page, _pageSize,
            _facets, _numericFacetStats, _highlight, _returnFields, _requireExactTotalHits);
    }

    /// <inheritdoc />
    public IQueryBuilder Where(SearchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var combined = _filter is null ? filter : SearchFilter.All(_filter, filter);
        return new SearchQueryBuilder(
            _freeText, _matchAllTerms, _searchFields, combined, _sort, _page, _pageSize,
            _facets, _numericFacetStats, _highlight, _returnFields, _requireExactTotalHits);
    }

    /// <inheritdoc />
    public IQueryBuilder OrderBy(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new SearchQueryBuilder(
            _freeText, _matchAllTerms, _searchFields, _filter, Append(_sort, SearchSort.Ascending(field)),
            _page, _pageSize, _facets, _numericFacetStats, _highlight, _returnFields, _requireExactTotalHits);
    }

    /// <inheritdoc />
    public IQueryBuilder OrderByDescending(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new SearchQueryBuilder(
            _freeText, _matchAllTerms, _searchFields, _filter, Append(_sort, SearchSort.Descending(field)),
            _page, _pageSize, _facets, _numericFacetStats, _highlight, _returnFields, _requireExactTotalHits);
    }

    /// <inheritdoc />
    public IQueryBuilder Page(int page, int pageSize) => new SearchQueryBuilder(
        _freeText, _matchAllTerms, _searchFields, _filter, _sort, page, pageSize,
        _facets, _numericFacetStats, _highlight, _returnFields, _requireExactTotalHits);

    /// <inheritdoc />
    public IQueryBuilder RequireExactTotalHits() => new SearchQueryBuilder(
        _freeText, _matchAllTerms, _searchFields, _filter, _sort, _page, _pageSize,
        _facets, _numericFacetStats, _highlight, _returnFields, requireExactTotalHits: true);

    /// <inheritdoc />
    public IQueryBuilder Faceting(params string[] facetFields)
    {
        ArgumentNullException.ThrowIfNull(facetFields);
        return new SearchQueryBuilder(
            _freeText, _matchAllTerms, _searchFields, _filter, _sort, _page, _pageSize,
            facetFields.ToArray(), _numericFacetStats, _highlight, _returnFields, _requireExactTotalHits);
    }

    /// <inheritdoc />
    public IQueryBuilder WithNumericFacetStats(params string[] facetFields)
    {
        ArgumentNullException.ThrowIfNull(facetFields);
        return new SearchQueryBuilder(
            _freeText, _matchAllTerms, _searchFields, _filter, _sort, _page, _pageSize,
            _facets, facetFields.ToArray(), _highlight, _returnFields, _requireExactTotalHits);
    }

    /// <inheritdoc />
    public IQueryBuilder Highlighting(HighlightRequest highlight)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        return new SearchQueryBuilder(
            _freeText, _matchAllTerms, _searchFields, _filter, _sort, _page, _pageSize,
            _facets, _numericFacetStats, highlight, _returnFields, _requireExactTotalHits);
    }

    /// <inheritdoc />
    public IQueryBuilder Returning(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new SearchQueryBuilder(
            _freeText, _matchAllTerms, _searchFields, _filter, _sort, _page, _pageSize,
            _facets, _numericFacetStats, _highlight, fields.ToArray(), _requireExactTotalHits);
    }

    /// <inheritdoc />
    public Result<SearchRequest> Build()
    {
        if (_page < 1)
        {
            return Result<SearchRequest>.Failure(
                SearchErrors.InvalidSearchRequest($"Page must be at least 1; received {_page}."));
        }

        if (_pageSize < 1)
        {
            return Result<SearchRequest>.Failure(
                SearchErrors.InvalidSearchRequest($"PageSize must be at least 1; received {_pageSize}."));
        }

        if (_pageSize > SearchWellKnown.MaxPageSize)
        {
            return Result<SearchRequest>.Failure(
                SearchErrors.InvalidSearchRequest(
                    $"PageSize must not exceed {SearchWellKnown.MaxPageSize}; received {_pageSize}."));
        }

        foreach (var field in _searchFields.Concat(_facets).Concat(_numericFacetStats).Concat(_returnFields))
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return Result<SearchRequest>.Failure(
                    SearchErrors.InvalidSearchRequest("Field names must not be empty."));
            }
        }

        var duplicateSortField = _sort
            .GroupBy(s => s.Field, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateSortField is not null)
        {
            return Result<SearchRequest>.Failure(
                SearchErrors.InvalidSearchRequest($"Duplicate sort field '{duplicateSortField.Key}'."));
        }

        if (_highlight is not null && _highlight.Fields.Count == 0)
        {
            return Result<SearchRequest>.Failure(
                SearchErrors.InvalidSearchRequest("HighlightRequest.Fields must not be empty."));
        }

        return Result<SearchRequest>.Success(new SearchRequest
        {
            FreeText = _freeText,
            MatchAllTerms = _matchAllTerms,
            SearchFields = _searchFields,
            Filter = _filter,
            Sort = _sort,
            Page = _page,
            PageSize = _pageSize,
            Facets = _facets,
            NumericFacetStats = _numericFacetStats,
            Highlight = _highlight,
            ReturnFields = _returnFields,
            RequireExactTotalHits = _requireExactTotalHits,
        });
    }

    private static IReadOnlyList<SearchSort> Append(IReadOnlyList<SearchSort> source, SearchSort item)
    {
        var result = new SearchSort[source.Count + 1];
        for (var i = 0; i < source.Count; i++)
        {
            result[i] = source[i];
        }

        result[source.Count] = item;
        return result;
    }
}
