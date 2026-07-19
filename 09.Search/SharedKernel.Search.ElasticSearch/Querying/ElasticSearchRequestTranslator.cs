using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
// Elastic.Clients.Elasticsearch declares its own non-generic SearchRequest/Result types; alias ours
// explicitly so the bare identifiers in this file resolve to the neutral domain contract.
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;
using Result = SharedKernel.Primitives.Results.Result;

namespace SharedKernel.Search.ElasticSearch.Querying;

/// <summary>Translates a neutral <see cref="SearchRequest"/> into an ElasticSearch <see cref="SearchRequest{TDocument}"/>.</summary>
internal static class ElasticSearchRequestTranslator
{
    /// <summary>Reserved aggregation-name suffix distinguishing a numeric-facet-stats aggregation from a same-named terms facet.</summary>
    private const string FacetStatsSuffix = "__stats";

    /// <summary>Translates <paramref name="request"/> against <paramref name="indexName"/> into the ElasticSearch SDK's request shape.</summary>
    public static global::Elastic.Clients.Elasticsearch.SearchRequest<TDocument> Translate<TDocument>(
        string indexName, SearchIndexDefinition definition, SearchRequest request, Query? filterQuery)
        where TDocument : class
    {
        var searchRequest = new global::Elastic.Clients.Elasticsearch.SearchRequest<TDocument>(indexName)
        {
            From = (request.Page - 1) * request.PageSize,
            Size = request.PageSize,
            Query = BuildQuery(definition, request, filterQuery),
            TrackTotalHits = request.RequireExactTotalHits ? new TrackHits(true) : null,
        };

        if (request.Sort.Count > 0)
        {
            // Assigned ONCE as a full collection — never via chained descriptor calls, which silently
            // keep only the last field (issue #8471).
            ICollection<SortOptions> sort = request.Sort
                .Select(s => new SortOptions
                {
                    Field = new FieldSort(s.Field)
                    {
                        Order = s.Direction == SortDirection.Ascending ? SortOrder.Asc : SortOrder.Desc,
                    },
                })
                .ToList();
            searchRequest.Sort = sort;
        }

        if (request.ReturnFields.Count > 0)
        {
            Field[] includeFields = request.ReturnFields.Select(f => (Field)f).ToArray();
            searchRequest.Source = new SourceConfig(new SourceFilter { Includes = includeFields });
        }

        if (request.Highlight is { } highlight)
        {
            searchRequest.Highlight = BuildHighlight(highlight);
        }

        if (request.Facets.Count > 0 || request.NumericFacetStats.Count > 0)
        {
            searchRequest.Aggregations = BuildFacetAggregations(request, definition.MaxFacetValues);
        }

        return searchRequest;
    }

    /// <summary>Gets the aggregation name a terms facet on <paramref name="field"/> is stored under.</summary>
    public static string FacetAggregationName(string field) => field;

    /// <summary>Gets the aggregation name a numeric facet-stats request on <paramref name="field"/> is stored under.</summary>
    public static string FacetStatsAggregationName(string field) => field + FacetStatsSuffix;

    private static Query BuildQuery(SearchIndexDefinition definition, SearchRequest request, Query? filterQuery)
    {
        Query? textQuery = null;
        if (!string.IsNullOrEmpty(request.FreeText))
        {
            var searchFields = request.SearchFields.Count > 0
                ? request.SearchFields
                : definition.Fields.Where(f => f.Searchable).Select(f => f.Name).ToArray();

            textQuery = new Query
            {
                MultiMatch = new MultiMatchQuery(request.FreeText)
                {
                    Fields = searchFields.Select(f => (Field)f).ToArray(),
                    Operator = request.MatchAllTerms ? Operator.And : Operator.Or,
                },
            };
        }

        return (textQuery, filterQuery) switch
        {
            (null, null) => new Query { MatchAll = new MatchAllQuery() },
            (not null, null) => textQuery,
            (null, not null) => filterQuery,
            (not null, not null) => new Query { Bool = new BoolQuery { Must = [textQuery], Filter = [filterQuery] } },
        };
    }

    private static Highlight BuildHighlight(HighlightRequest highlight)
    {
        var fields = highlight.Fields
            .Select(f => new KeyValuePair<Field, HighlightField>((Field)f, new HighlightField()))
            .ToList();

        var result = new Highlight(fields)
        {
            PreTags = [highlight.PreTag],
            PostTags = [highlight.PostTag],
        };

        if (highlight.FragmentSize is { } fragmentSize)
        {
            result.FragmentSize = fragmentSize;
        }

        if (highlight.MaxFragments is { } maxFragments)
        {
            result.NumberOfFragments = maxFragments;
        }

        return result;
    }

    private static Dictionary<string, Aggregation> BuildFacetAggregations(SearchRequest request, int maxFacetValues)
    {
        var aggregations = new Dictionary<string, Aggregation>();

        foreach (var field in request.Facets)
        {
            aggregations[FacetAggregationName(field)] = new Aggregation
            {
                Terms = new TermsAggregation { Field = field, Size = maxFacetValues },
            };
        }

        foreach (var field in request.NumericFacetStats)
        {
            aggregations[FacetStatsAggregationName(field)] = new Aggregation
            {
                Stats = new StatsAggregation { Field = field },
            };
        }

        return aggregations;
    }
}

/// <summary>
/// The pre-flight validator shared by <c>ElasticSearchProviderDescriptor.Validate</c> and
/// <c>ElasticSearchIndex&lt;TDocument&gt;</c>'s executor — every check runs before any I/O.
/// </summary>
/// <remarks>
/// Identical rules to <c>MeilisearchRequestValidator</c>, deliberately enforcing restrictions
/// ElasticSearch itself does not impose (an undeclared-but-mapped filter field is still rejected; the
/// pagination ceiling is enforced even where <c>index.max_result_window</c> would allow more) — see
/// the "Why MaxTotalHits defaults to 1000 on both providers" note in <c>09.Search/CLAUDE.md</c>.
/// </remarks>
internal static class ElasticSearchRequestValidator
{
    /// <summary>Validates <paramref name="request"/> against <paramref name="definition"/>.</summary>
    public static Result Validate(SearchIndexDefinition definition, SearchRequest request)
    {
        if (request.Page < 1)
        {
            return Result.Failure(SearchErrors.InvalidSearchRequest($"Page must be at least 1; received {request.Page}."));
        }

        if (request.PageSize < 1)
        {
            return Result.Failure(SearchErrors.InvalidSearchRequest($"PageSize must be at least 1; received {request.PageSize}."));
        }

        if (request.PageSize > SearchWellKnown.MaxPageSize)
        {
            return Result.Failure(
                SearchErrors.InvalidSearchRequest($"PageSize must not exceed {SearchWellKnown.MaxPageSize}; received {request.PageSize}."));
        }

        var duplicateSort = request.Sort
            .GroupBy(s => s.Field, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateSort is not null)
        {
            return Result.Failure(SearchErrors.InvalidSearchRequest($"Duplicate sort field '{duplicateSort.Key}'."));
        }

        if (request.Highlight is not null && request.Highlight.Fields.Count == 0)
        {
            return Result.Failure(SearchErrors.InvalidSearchRequest("HighlightRequest.Fields must not be empty."));
        }

        var requestedMaxHit = (long)request.Page * request.PageSize;
        if (requestedMaxHit > definition.MaxTotalHits)
        {
            return Result.Failure(SearchErrors.PaginationLimitExceeded(
                request.Page, request.PageSize, definition.MaxTotalHits, SearchWellKnown.ElasticSearchProviderName));
        }

        var facetFieldCount = request.Facets.Count + request.NumericFacetStats.Count;
        if (facetFieldCount > definition.MaxFacetValues)
        {
            return Result.Failure(SearchErrors.FacetLimitExceeded(facetFieldCount, definition.MaxFacetValues));
        }

        var fieldsByName = definition.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

        foreach (var field in request.Facets.Concat(request.NumericFacetStats))
        {
            if (!fieldsByName.TryGetValue(field, out var declared) || !declared.Facetable)
            {
                return Result.Failure(SearchErrors.FieldNotFacetable(definition.Name, field));
            }
        }

        foreach (var sort in request.Sort)
        {
            if (!fieldsByName.TryGetValue(sort.Field, out var declared) || !declared.Sortable)
            {
                return Result.Failure(SearchErrors.FieldNotSortable(definition.Name, sort.Field));
            }
        }

        if (request.Filter is not null)
        {
            var filterValidation = ValidateFilterFields(request.Filter, definition.Name, fieldsByName);
            if (filterValidation.IsFailure)
            {
                return filterValidation;
            }
        }

        return Result.Success();
    }

    private static Result ValidateFilterFields(
        SearchFilter filter, string indexName, IReadOnlyDictionary<string, SearchFieldDefinition> fieldsByName)
    {
        switch (filter)
        {
            case EqualFilter equal:
                return CheckFilterable(equal.Field, indexName, fieldsByName);
            case NotEqualFilter notEqual:
                return CheckFilterable(notEqual.Field, indexName, fieldsByName);
            case InFilter inFilter:
                return CheckFilterable(inFilter.Field, indexName, fieldsByName);
            case RangeFilter range:
                return CheckFilterable(range.Field, indexName, fieldsByName);
            case ExistsFilter exists:
                return CheckFilterable(exists.Field, indexName, fieldsByName);
            case AndFilter and:
                return ValidateOperands(and.Operands, indexName, fieldsByName);
            case OrFilter or:
                return ValidateOperands(or.Operands, indexName, fieldsByName);
            case NotFilter not:
                return ValidateFilterFields(not.Operand, indexName, fieldsByName);
            default:
                throw new NotSupportedException(
                    $"Unrecognized SearchFilter node type '{filter.GetType()}' — the closed hierarchy has grown a new node.");
        }
    }

    private static Result ValidateOperands(
        IReadOnlyList<SearchFilter> operands, string indexName, IReadOnlyDictionary<string, SearchFieldDefinition> fieldsByName)
    {
        foreach (var operand in operands)
        {
            var result = ValidateFilterFields(operand, indexName, fieldsByName);
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Result.Success();
    }

    private static Result CheckFilterable(
        string field, string indexName, IReadOnlyDictionary<string, SearchFieldDefinition> fieldsByName)
        => fieldsByName.TryGetValue(field, out var declared) && declared.Filterable
            ? Result.Success()
            : Result.Failure(SearchErrors.FieldNotFilterable(indexName, field));
}
