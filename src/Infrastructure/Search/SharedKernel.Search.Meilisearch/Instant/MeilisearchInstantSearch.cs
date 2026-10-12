using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Errors;
using SharedKernel.Search.Meilisearch.Logging;
using SharedKernel.Search.Meilisearch.Querying;

namespace SharedKernel.Search.Meilisearch.Instant;

/// <summary>The Meilisearch implementation of <see cref="IInstantSearch{TDocument}"/> — scoped.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
internal sealed class MeilisearchInstantSearch<TDocument> : IInstantSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    private const string OperationInstantSearch = "instant_search";
    private const string OperationFacetSearch = "search_facet_values";

    private readonly global::Meilisearch.MeilisearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ILogger<MeilisearchInstantSearch<TDocument>> _logger;

    /// <summary>Initializes a new <see cref="MeilisearchInstantSearch{TDocument}"/>.</summary>
    public MeilisearchInstantSearch(
        global::Meilisearch.MeilisearchClient client,
        SearchIndexDefinition definition,
        ILogger<MeilisearchInstantSearch<TDocument>> logger)
    {
        _client = client;
        _definition = definition;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<SearchResults<TDocument>>> InstantAsync(
        InstantSearchRequest request, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        var filterResult = MeilisearchFilterCompiler.CompileWithTenantScope(_definition, request.Filter, tenantScope);
        if (filterResult.IsFailure)
        {
            _logger.MeilisearchTenantScopeMissing(_definition.Name);
            return Result<SearchResults<TDocument>>.Failure(filterResult.Error);
        }

        var query = new global::Meilisearch.SearchQuery
        {
            Limit = request.Limit,
            MatchingStrategy = request.MatchingStrategy switch
            {
                InstantMatchingStrategy.All => "all",
                InstantMatchingStrategy.Frequency => "frequency",
                _ => "last",
            },
            CropMarker = request.CropMarker,
        };

        if (!string.IsNullOrEmpty(filterResult.Value))
        {
            query.Filter = filterResult.Value;
        }

        if (request.AttributesToSearchOn.Count > 0)
        {
            query.AttributesToSearchOn = request.AttributesToSearchOn;
        }

        string? highlightPreTag = null;
        if (request.Highlight is { } highlight)
        {
            highlightPreTag = highlight.PreTag;
            query.AttributesToHighlight = highlight.Fields;
            query.HighlightPreTag = highlight.PreTag;
            query.HighlightPostTag = highlight.PostTag;
        }

        if (request.CropLength is { } cropLength)
        {
            query.CropLength = cropLength;
            query.AttributesToCrop = request.Highlight?.Fields ?? request.AttributesToSearchOn;
        }

        global::Meilisearch.ISearchable<JsonElement> searchable;
        try
        {
            searchable = await _client.Index(_definition.Name)
                .SearchAsync<JsonElement>(request.FreeText, query, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (MeilisearchFaultMapper.TryMap(ex, _definition.Name, OperationInstantSearch, cancellationToken, out var error))
        {
            return Result<SearchResults<TDocument>>.Failure(Faulted(error, ex, OperationInstantSearch));
        }

        return MeilisearchResultMapper.Map<TDocument>(
            searchable,
            expectExact: false,
            _definition.MaxFacetValues,
            highlightPreTag,
            SearchWellKnown.MeilisearchProviderName);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FacetValue>>> SearchFacetValuesAsync(
        string facetField,
        string facetQuery,
        SearchFilter? filter,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        var filterResult = MeilisearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            _logger.MeilisearchTenantScopeMissing(_definition.Name);
            return Result<IReadOnlyList<FacetValue>>.Failure(filterResult.Error);
        }

        var query = new global::Meilisearch.FacetSearchQuery
        {
            FacetName = facetField,
            FacetQuery = facetQuery,
        };

        if (!string.IsNullOrEmpty(filterResult.Value))
        {
            query.Filter = filterResult.Value;
        }

        global::Meilisearch.FacetSearchResult result;
        try
        {
            result = await _client.Index(_definition.Name)
                .FacetSearchAsync(facetField, query, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (MeilisearchFaultMapper.TryMap(ex, _definition.Name, OperationFacetSearch, cancellationToken, out var error))
        {
            return Result<IReadOnlyList<FacetValue>>.Failure(Faulted(error, ex, OperationFacetSearch));
        }

        var values = result.FacetHits.Select(hit => new FacetValue(hit.Value, hit.Count)).ToArray();
        return Result<IReadOnlyList<FacetValue>>.Success(values);
    }

    private SharedKernel.Primitives.Errors.Error Faulted(SharedKernel.Primitives.Errors.Error error, Exception exception, string operation)
    {
        _logger.MeilisearchOperationFaulted(operation, _definition.Name, error.Code, exception);
        return error;
    }
}
