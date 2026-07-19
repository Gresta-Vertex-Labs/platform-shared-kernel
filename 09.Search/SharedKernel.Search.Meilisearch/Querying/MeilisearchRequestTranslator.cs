using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Meilisearch.Querying;

/// <summary>Translates a neutral <see cref="SearchRequest"/> into a Meilisearch <see cref="global::Meilisearch.SearchQuery"/>.</summary>
internal static class MeilisearchRequestTranslator
{
    /// <summary>
    /// Translates <paramref name="request"/> plus its already-compiled (and tenant-scoped)
    /// <paramref name="filterExpression"/> into the free-text query string and
    /// <see cref="global::Meilisearch.SearchQuery"/> attributes the Meilisearch SDK's
    /// <c>SearchAsync</c> expects.
    /// </summary>
    public static (string Query, global::Meilisearch.SearchQuery Attributes) Translate(
        SearchRequest request, string? filterExpression)
    {
        var attributes = new global::Meilisearch.SearchQuery
        {
            MatchingStrategy = request.MatchAllTerms ? "all" : "last",
        };

        if (!string.IsNullOrEmpty(filterExpression))
        {
            // The SDK types this property `dynamic`; only a plain string is ever assigned to it.
            attributes.Filter = filterExpression;
        }

        if (request.SearchFields.Count > 0)
        {
            attributes.AttributesToSearchOn = request.SearchFields;
        }

        if (request.ReturnFields.Count > 0)
        {
            attributes.AttributesToRetrieve = request.ReturnFields;
        }

        if (request.Sort.Count > 0)
        {
            attributes.Sort = request.Sort
                .Select(s => $"{s.Field}:{(s.Direction == SortDirection.Ascending ? "asc" : "desc")}")
                .ToArray();
        }

        var facetFields = request.Facets.Concat(request.NumericFacetStats).Distinct(StringComparer.Ordinal).ToArray();
        if (facetFields.Length > 0)
        {
            attributes.Facets = facetFields;
        }

        if (request.Highlight is { } highlight)
        {
            attributes.AttributesToHighlight = highlight.Fields;
            attributes.HighlightPreTag = highlight.PreTag;
            attributes.HighlightPostTag = highlight.PostTag;

            if (highlight.FragmentSize is { } fragmentSize)
            {
                attributes.CropLength = fragmentSize;
                attributes.AttributesToCrop = highlight.Fields;
            }
        }

        // Pagination-mode branch: an exact TotalHits requires page/hitsPerPage; otherwise
        // offset/limit is cheaper and returns an estimate.
        if (request.RequireExactTotalHits)
        {
            attributes.Page = request.Page;
            attributes.HitsPerPage = request.PageSize;
        }
        else
        {
            attributes.Offset = (request.Page - 1) * request.PageSize;
            attributes.Limit = request.PageSize;
        }

        return (request.FreeText ?? string.Empty, attributes);
    }
}

/// <summary>
/// The pre-flight validator shared by <c>MeilisearchProviderDescriptor.Validate</c> and
/// <c>MeilisearchIndex&lt;TDocument&gt;</c>'s executor — every check runs before any I/O.
/// </summary>
internal static class MeilisearchRequestValidator
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
                request.Page, request.PageSize, definition.MaxTotalHits, SearchWellKnown.MeilisearchProviderName));
        }

        // Interpreted as: the number of distinct facet fields requested in one query (Facets ∪
        // NumericFacetStats) must not exceed the provider's MaxFacetValues ceiling — the only
        // pre-flight, zero-I/O interpretation available, since the number of facet VALUES a query
        // will return cannot be known before execution.
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
