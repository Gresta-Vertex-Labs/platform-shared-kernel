using System.Runtime.CompilerServices;
using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Abstractions.Telemetry;
using SharedKernel.Search.ElasticSearch.Errors;
using SharedKernel.Search.ElasticSearch.Diagnostics;
// Elastic.Clients.Elasticsearch declares its own non-generic SearchRequest/Result types; alias ours
// explicitly so the bare identifiers in this file resolve to the neutral domain contract.
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;
using Result = SharedKernel.Primitives.Results.Result;

namespace SharedKernel.Search.ElasticSearch.Cursors;

/// <summary>The ElasticSearch implementation of <see cref="ICursorSearch{TDocument}"/> — scoped.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
internal sealed class ElasticSearchCursorSearch<TDocument> : ICursorSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    private const string MalformedTokenReason = "the token is malformed.";

    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly IClock _clock;
    private readonly ILogger<ElasticSearchCursorSearch<TDocument>> _logger;
    private readonly SearchTelemetry _telemetry;

    /// <summary>Initializes a new <see cref="ElasticSearchCursorSearch{TDocument}"/>.</summary>
    public ElasticSearchCursorSearch(
        ElasticsearchClient client,
        SearchIndexDefinition definition,
        IClock clock,
        ILogger<ElasticSearchCursorSearch<TDocument>> logger,
        SearchTelemetry telemetry)
    {
        _client = client;
        _definition = definition;
        _clock = clock;
        _logger = logger;
        _telemetry = telemetry;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchHit<TDocument>> StreamAsync(
        SearchRequest request,
        TenantScope tenantScope,
        TimeSpan keepAlive,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var operation = _telemetry.Start(SearchOperations.Stream, _definition.Name);
        SharedKernel.Primitives.Errors.Error? failure = null;
        Exception? faulted = null;

        if (request.Page > 1)
        {
            var pagingError = SearchErrors.InvalidSearchRequest(
                "Cursor iteration and offset paging are mutually exclusive; SearchRequest.Page must be 1.");
            operation.Failed(pagingError);
            throw new SearchStreamException(pagingError);
        }

        var filterResult = Querying.ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, request.Filter, tenantScope);
        if (filterResult.IsFailure)
        {
            operation.Failed(filterResult.Error);
            throw new SearchStreamException(filterResult.Error);
        }

        OpenPointInTimeResponse pitResponse;
        try
        {
            pitResponse = await _client
                .OpenPointInTimeAsync(new OpenPointInTimeRequest(_definition.Name) { KeepAlive = keepAlive }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            operation.Faulted(ex);
            throw;
        }

        if (!pitResponse.IsValidResponse)
        {
            SharedKernel.Primitives.Errors.Error pitError;
            try
            {
                pitError = Fault(pitResponse, SearchOperations.Stream, cancellationToken);
            }
            catch (Exception ex)
            {
                operation.Faulted(ex);
                throw;
            }

            operation.Failed(pitError);
            throw new SearchStreamException(pitError);
        }

        var pitId = pitResponse.Id;
        ElasticSearchLog.PointInTimeOpened(_logger, _definition.Name, (int)keepAlive.TotalSeconds);
        var batchCount = 0;

        try
        {
            var query = filterResult.Value ?? new Query { MatchAll = new MatchAllQuery() };
            ICollection<FieldValue>? searchAfter = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var searchRequest = new global::Elastic.Clients.Elasticsearch.SearchRequest<TDocument>
                {
                    Size = request.PageSize,
                    Query = query,
                    Sort = [new SortOptions { Field = new FieldSort("_doc") }],
                    Pit = new PointInTimeReference(pitId) { KeepAlive = keepAlive },
                };

                if (searchAfter is not null)
                {
                    searchRequest.SearchAfter = searchAfter;
                }

                global::Elastic.Clients.Elasticsearch.SearchResponse<TDocument> response;
                try
                {
                    response = await _client.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ElasticSearchFaultMapper.CallerCancellation(ex, cancellationToken) is { } cancellation)
                {
                    faulted = cancellation;
                    throw new OperationCanceledException(cancellation.Message, cancellation, cancellationToken);
                }
                catch (Exception ex) when (ElasticSearchFaultMapper.TryClassify(ex, SearchOperations.Stream, DocumentTypeName, out var streamError))
                {
                    failure = streamError;
                    ElasticSearchLog.OperationFaulted(_logger, SearchOperations.Stream, _definition.Name, streamError.Code, ex);
                    throw new SearchStreamException(streamError, ex);
                }

                if (!response.IsValidResponse)
                {
                    SharedKernel.Primitives.Errors.Error responseError;
                    try
                    {
                        responseError = Fault(response, SearchOperations.Stream, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        faulted = ex;
                        throw;
                    }

                    failure = responseError;
                    throw new SearchStreamException(responseError);
                }

                batchCount++;
                var hits = response.HitsMetadata.Hits.ToList();
                if (hits.Count == 0)
                {
                    yield break;
                }

                var rank = 0;
                foreach (var hit in hits)
                {
                    if (hit.Source is { } source)
                    {
                        yield return new SearchHit<TDocument>
                        {
                            Document = source,
                            Rank = rank,
                            Highlights = MapHighlights(hit),
                        };
                    }

                    rank++;
                }

                if (hits.Count < request.PageSize)
                {
                    yield break;
                }

                // Every hit carries a Sort tuple because the request always sorts (by _doc); the tuple
                // is what makes search_after pagination possible.
                searchAfter = hits[^1].Sort!.ToList();
            }
        }
        finally
        {
            if (faulted is not null)
            {
                operation.Faulted(faulted);
            }
            else if (failure is not null)
            {
                operation.Failed(failure);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                operation.Canceled();
            }
            else
            {
                operation.Succeeded();
            }

            var closeToken = cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;
            var closeResponse = await _client.ClosePointInTimeAsync(new ClosePointInTimeRequest(pitId), closeToken)
                .ConfigureAwait(false);

            if (closeResponse.IsValidResponse)
            {
                ElasticSearchLog.PointInTimeClosed(_logger, _definition.Name, batchCount);
            }
            else
            {
                ElasticSearchLog.PointInTimeCloseFailed(
                    _logger,
                    _definition.Name, ElasticSearchFaultMapper.OriginalException(closeResponse));
            }
        }
    }

    /// <inheritdoc />
    public Task<Result<SearchCursor>> OpenCursorAsync(
        SearchRequest request, TenantScope tenantScope, TimeSpan keepAlive, CancellationToken cancellationToken = default)
        => TraceAsync(SearchOperations.OpenCursor, ct => OpenCursorCoreAsync(request, tenantScope, keepAlive, ct), cancellationToken);

    private async Task<Result<SearchCursor>> OpenCursorCoreAsync(
        SearchRequest request, TenantScope tenantScope, TimeSpan keepAlive, CancellationToken cancellationToken)
    {
        if (request.Page > 1)
        {
            return Result<SearchCursor>.Failure(SearchErrors.InvalidSearchRequest(
                "Cursor iteration and offset paging are mutually exclusive; SearchRequest.Page must be 1."));
        }

        var filterResult = Querying.ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, request.Filter, tenantScope);
        if (filterResult.IsFailure)
        {
            return Result<SearchCursor>.Failure(filterResult.Error);
        }

        var pitResponse = await _client
            .OpenPointInTimeAsync(new OpenPointInTimeRequest(_definition.Name) { KeepAlive = keepAlive }, cancellationToken)
            .ConfigureAwait(false);
        if (!pitResponse.IsValidResponse)
        {
            return Result<SearchCursor>.Failure(Fault(pitResponse, SearchOperations.OpenCursor, cancellationToken));
        }

        ElasticSearchLog.PointInTimeOpened(_logger, _definition.Name, (int)keepAlive.TotalSeconds);

        var query = filterResult.Value ?? new Query { MatchAll = new MatchAllQuery() };
        var token = EncodeToken(pitResponse.Id, (int)keepAlive.TotalSeconds, request.PageSize, query, searchAfter: null);
        return Result<SearchCursor>.Success(new SearchCursor(token, _clock.UtcNow.Add(keepAlive)));
    }

    /// <inheritdoc />
    public Task<Result<CursorPage<TDocument>>> ReadCursorAsync(SearchCursor cursor, CancellationToken cancellationToken = default)
        => TraceAsync(SearchOperations.ReadCursor, ct => ReadCursorCoreAsync(cursor, ct), cancellationToken);

    private async Task<Result<CursorPage<TDocument>>> ReadCursorCoreAsync(SearchCursor cursor, CancellationToken cancellationToken)
    {
        CursorState state;
        Query query;
        try
        {
            state = DecodeToken(cursor.Token);
            query = DeserializeQuery(state.QueryBase64);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            var invalid = ElasticSearchErrors.InvalidCursor(MalformedTokenReason);
            ElasticSearchLog.CursorReadFaulted(_logger, _definition.Name, invalid.Code, ex);
            return Result<CursorPage<TDocument>>.Failure(invalid);
        }

        var searchRequest = new global::Elastic.Clients.Elasticsearch.SearchRequest<TDocument>
        {
            Size = state.PageSize,
            Query = query,
            Sort = [new SortOptions { Field = new FieldSort("_doc") }],
            Pit = new PointInTimeReference(state.PitId) { KeepAlive = TimeSpan.FromSeconds(state.KeepAliveSeconds) },
        };

        if (state.SearchAfter is { Length: > 0 } searchAfter)
        {
            searchRequest.SearchAfter = searchAfter.Select(JsonElementToFieldValue).ToList();
        }

        global::Elastic.Clients.Elasticsearch.SearchResponse<TDocument> response;
        try
        {
            response = await _client.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ElasticSearchFaultMapper.CallerCancellation(ex, cancellationToken) is { } cancellation)
        {
            // The client wraps most exceptions it surfaces; a cancellation the caller asked for still reaches the
            // caller as an OperationCanceledException.
            throw new OperationCanceledException(cancellation.Message, cancellation, cancellationToken);
        }
        catch (Exception ex) when (ElasticSearchFaultMapper.TryClassify(ex, SearchOperations.ReadCursor, DocumentTypeName, out var classified))
        {
            ElasticSearchLog.CursorReadFaulted(_logger, _definition.Name, classified.Code, ex);
            return Result<CursorPage<TDocument>>.Failure(classified);
        }

        if (!response.IsValidResponse)
        {
            // Only the point-in-time-missing signal means the cursor lapsed; every other fault (outage,
            // credentials, a rejected request) is classified like any other call.
            var error = ElasticSearchFaultMapper.IsPointInTimeMissing(response)
                ? ElasticSearchErrors.CursorExpired(_clock.UtcNow)
                : ElasticSearchFaultMapper.Map(
                    response, _definition.Name, SearchOperations.ReadCursor, cancellationToken, DocumentTypeName);
            ElasticSearchLog.CursorReadFaulted(
                _logger,
                _definition.Name, error.Code, ElasticSearchFaultMapper.OriginalException(response));
            return Result<CursorPage<TDocument>>.Failure(error);
        }

        var hits = response.HitsMetadata.Hits.ToList();
        var mappedHits = hits
            .Select((hit, index) => new SearchHit<TDocument>
            {
                Document = hit.Source!,
                Rank = index,
                Highlights = MapHighlights(hit),
            })
            .ToArray();

        SearchCursor? nextCursor = null;
        if (hits.Count > 0 && hits.Count >= state.PageSize)
        {
            var lastSort = hits[^1].Sort!.Select(FieldValueToJsonElement).ToArray();
            var nextToken = EncodeToken(state.PitId, state.KeepAliveSeconds, state.PageSize, query, lastSort);
            nextCursor = new SearchCursor(nextToken, _clock.UtcNow.AddSeconds(state.KeepAliveSeconds));
        }

        return Result<CursorPage<TDocument>>.Success(new CursorPage<TDocument> { Hits = mappedHits, NextCursor = nextCursor });
    }

    /// <inheritdoc />
    public Task<Result> CloseCursorAsync(SearchCursor cursor, CancellationToken cancellationToken = default)
        => TraceAsync(SearchOperations.CloseCursor, ct => CloseCursorCoreAsync(cursor, ct), cancellationToken);

    private async Task<Result> CloseCursorCoreAsync(SearchCursor cursor, CancellationToken cancellationToken)
    {
        CursorState state;
        try
        {
            state = DecodeToken(cursor.Token);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            var invalid = ElasticSearchErrors.InvalidCursor(MalformedTokenReason);
            ElasticSearchLog.CursorReadFaulted(_logger, _definition.Name, invalid.Code, ex);
            return Result.Failure(invalid);
        }

        var response = await _client.ClosePointInTimeAsync(new ClosePointInTimeRequest(state.PitId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsValidResponse)
        {
            // Mapped first: a failure caused by the caller's own cancellation throws here, before anything is logged.
            var error = ElasticSearchFaultMapper.IsPointInTimeMissing(response)
                ? ElasticSearchErrors.CursorExpired(_clock.UtcNow)
                : ElasticSearchFaultMapper.Map(response, _definition.Name, SearchOperations.CloseCursor, cancellationToken);
            ElasticSearchLog.PointInTimeCloseFailed(_logger, _definition.Name, ElasticSearchFaultMapper.OriginalException(response));
            return Result.Failure(error);
        }

        return Result.Success();
    }

    private async Task<Result<T>> TraceAsync<T>(
        string operation, Func<CancellationToken, Task<Result<T>>> body, CancellationToken cancellationToken)
    {
        using var scope = _telemetry.Start(operation, _definition.Name);
        try
        {
            var result = await body(cancellationToken).ConfigureAwait(false);
            scope.Complete(result);
            return result;
        }
        catch (Exception ex)
        {
            scope.Faulted(ex);
            throw;
        }
    }

    private async Task<Result> TraceAsync(
        string operation, Func<CancellationToken, Task<Result>> body, CancellationToken cancellationToken)
    {
        using var scope = _telemetry.Start(operation, _definition.Name);
        try
        {
            var result = await body(cancellationToken).ConfigureAwait(false);
            scope.Complete(result);
            return result;
        }
        catch (Exception ex)
        {
            scope.Faulted(ex);
            throw;
        }
    }

    private static string DocumentTypeName => typeof(TDocument).Name;

    private Primitives.Errors.Error Fault(
        ElasticsearchResponse response, string operation, CancellationToken cancellationToken)
    {
        var error = ElasticSearchFaultMapper.Map(
            response, _definition.Name, operation, cancellationToken, DocumentTypeName);
        ElasticSearchLog.EngineFault(
            _logger,
            operation,
            _definition.Name,
            (int?)response.ApiCallDetails?.HttpStatusCode,
            ElasticSearchFaultMapper.OriginalException(response));
        return error;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> MapHighlights(Hit<TDocument> hit)
        => hit.Highlight is { Count: > 0 }
            ? hit.Highlight.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToArray())
            : new Dictionary<string, IReadOnlyList<string>>();

    private static FieldValue JsonElementToFieldValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => FieldValue.String(element.GetString()!),
        JsonValueKind.Number => element.TryGetInt64(out var longValue) ? FieldValue.Long(longValue) : FieldValue.Double(element.GetDouble()),
        JsonValueKind.True => FieldValue.Boolean(true),
        JsonValueKind.False => FieldValue.Boolean(false),
        _ => FieldValue.String(element.ToString()),
    };

    private static JsonElement FieldValueToJsonElement(FieldValue value) => JsonSerializer.SerializeToElement(value.Value);

    private string EncodeToken(string pitId, int keepAliveSeconds, int pageSize, Query query, JsonElement[]? searchAfter)
    {
        using var queryStream = new MemoryStream();
        _client.RequestResponseSerializer.Serialize(query, queryStream, SerializationFormatting.None);

        var state = new CursorState
        {
            PitId = pitId,
            KeepAliveSeconds = keepAliveSeconds,
            PageSize = pageSize,
            QueryBase64 = Convert.ToBase64String(queryStream.ToArray()),
            SearchAfter = searchAfter,
        };

        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(state));
    }

    private static CursorState DecodeToken(string token)
    {
        var json = Convert.FromBase64String(token);
        return JsonSerializer.Deserialize<CursorState>(json) ?? throw new FormatException("The cursor token is empty.");
    }

    private Query DeserializeQuery(string queryBase64)
    {
        var bytes = Convert.FromBase64String(queryBase64);
        using var stream = new MemoryStream(bytes);
        return _client.RequestResponseSerializer.Deserialize<Query>(stream);
    }

    /// <summary>The plain-data envelope encoded (opaque, Base64/JSON) into a <see cref="SearchCursor"/>'s token.</summary>
    private sealed record CursorState
    {
        public required string PitId { get; init; }

        public required int KeepAliveSeconds { get; init; }

        public required int PageSize { get; init; }

        public required string QueryBase64 { get; init; }

        public JsonElement[]? SearchAfter { get; init; }
    }
}
