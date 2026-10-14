using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Abstractions.Telemetry;
using SharedKernel.Search.Meilisearch.Diagnostics;
using SharedKernel.Search.Meilisearch.Errors;
using SharedKernel.Search.Meilisearch.Querying;

namespace SharedKernel.Search.Meilisearch.Instant;

/// <summary>The Meilisearch implementation of <see cref="IInstantSearch{TDocument}"/> — scoped.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
internal sealed class MeilisearchInstantSearch<TDocument> : IInstantSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    private readonly global::Meilisearch.MeilisearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ILogger<MeilisearchInstantSearch<TDocument>> _logger;
    private readonly SearchTelemetry _telemetry;

    /// <summary>Initializes a new <see cref="MeilisearchInstantSearch{TDocument}"/>.</summary>
    public MeilisearchInstantSearch(
        global::Meilisearch.MeilisearchClient client,
        SearchIndexDefinition definition,
        ILogger<MeilisearchInstantSearch<TDocument>> logger,
        SearchTelemetry telemetry)
    {
        _client = client;
        _definition = definition;
        _logger = logger;
        _telemetry = telemetry;
    }

    /// <inheritdoc />
    public async Task<Result<SearchResults<TDocument>>> InstantAsync(
        InstantSearchRequest request, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        using var scope = _telemetry.Start(SearchOperations.InstantSearch, _definition.Name);
        var filterResult = MeilisearchFilterCompiler.CompileWithTenantScope(_definition, request.Filter, tenantScope);
        if (filterResult.IsFailure)
        {
            MeilisearchLog.TenantScopeMissing(_logger, _definition.Name);
            scope.Failed(filterResult.Error);
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
        catch (Exception ex) when (MeilisearchFaultMapper.TryMap(ex, _definition.Name, SearchOperations.InstantSearch, cancellationToken, out var error))
        {
            scope.Failed(error);
            return Result<SearchResults<TDocument>>.Failure(Faulted(error, ex, SearchOperations.InstantSearch));
        }
        catch (Exception ex)
        {
            scope.Faulted(ex);
            throw;
        }

        var mapped = MeilisearchResultMapper.Map<TDocument>(
            searchable,
            expectExact: false,
            _definition.MaxFacetValues,
            highlightPreTag,
            SearchWellKnown.MeilisearchProviderName);
        scope.Complete(mapped);
        return mapped;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FacetValue>>> SearchFacetValuesAsync(
        string facetField,
        string facetQuery,
        SearchFilter? filter,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        using var scope = _telemetry.Start(SearchOperations.SearchFacetValues, _definition.Name);
        var filterResult = MeilisearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            MeilisearchLog.TenantScopeMissing(_logger, _definition.Name);
            scope.Failed(filterResult.Error);
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
        catch (Exception ex) when (MeilisearchFaultMapper.TryMap(ex, _definition.Name, SearchOperations.SearchFacetValues, cancellationToken, out var error))
        {
            scope.Failed(error);
            return Result<IReadOnlyList<FacetValue>>.Failure(Faulted(error, ex, SearchOperations.SearchFacetValues));
        }
        catch (Exception ex)
        {
            scope.Faulted(ex);
            throw;
        }

        var values = result.FacetHits.Select(hit => new FacetValue(hit.Value, hit.Count)).ToArray();
        scope.Succeeded();
        return Result<IReadOnlyList<FacetValue>>.Success(values);
    }

    private SharedKernel.Primitives.Errors.Error Faulted(SharedKernel.Primitives.Errors.Error error, Exception exception, string operation)
    {
        MeilisearchLog.OperationFaulted(_logger, operation, _definition.Name, error.Code, exception);
        return error;
    }
}
