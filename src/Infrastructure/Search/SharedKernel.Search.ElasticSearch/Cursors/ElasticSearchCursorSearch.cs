using System.Runtime.CompilerServices;
using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Transport;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Errors;
using SharedKernel.Search.ElasticSearch.Logging;
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
    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly IClock _clock;
    private readonly ILogger<ElasticSearchCursorSearch<TDocument>> _logger;

    /// <summary>Initializes a new <see cref="ElasticSearchCursorSearch{TDocument}"/>.</summary>
    public ElasticSearchCursorSearch(
        ElasticsearchClient client, SearchIndexDefinition definition, IClock clock, ILogger<ElasticSearchCursorSearch<TDocument>> logger)
    {
        _client = client;
        _definition = definition;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchHit<TDocument>> StreamAsync(
        SearchRequest request,
        TenantScope tenantScope,
        TimeSpan keepAlive,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.Page > 1)
        {
            throw new SearchStreamException(SearchErrors.InvalidSearchRequest(
                "Cursor iteration and offset paging are mutually exclusive; SearchRequest.Page must be 1."));
        }

        var filterResult = Querying.ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, request.Filter, tenantScope);
        if (filterResult.IsFailure)
        {
            throw new SearchStreamException(filterResult.Error);
        }

        var pitResponse = await _client
            .OpenPointInTimeAsync(new OpenPointInTimeRequest(_definition.Name) { KeepAlive = keepAlive }, cancellationToken)
            .ConfigureAwait(false);
        if (!pitResponse.IsValidResponse)
        {
            throw new SearchStreamException(SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName, "StreamAsync", pitResponse.DebugInformation));
        }

        var pitId = pitResponse.Id;
        _logger.ElasticSearchPointInTimeOpened(_definition.Name, (int)keepAlive.TotalSeconds);
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

                var response = await _client.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);
                if (!response.IsValidResponse)
                {
                    throw new SearchStreamException(SearchErrors.EngineFault(
                        SearchWellKnown.ElasticSearchProviderName, "StreamAsync", response.DebugInformation));
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
            var closeToken = cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;
            var closeResponse = await _client.ClosePointInTimeAsync(new ClosePointInTimeRequest(pitId), closeToken)
                .ConfigureAwait(false);

            if (closeResponse.IsValidResponse)
            {
                _logger.ElasticSearchPointInTimeClosed(_definition.Name, batchCount);
            }
            else
            {
                _logger.ElasticSearchPointInTimeCloseFailed(_definition.Name);
            }
        }
    }

    /// <inheritdoc />
    public async Task<Result<SearchCursor>> OpenCursorAsync(
        SearchRequest request, TenantScope tenantScope, TimeSpan keepAlive, CancellationToken cancellationToken = default)
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
            return Result<SearchCursor>.Failure(SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName, "OpenCursorAsync", pitResponse.DebugInformation));
        }

        _logger.ElasticSearchPointInTimeOpened(_definition.Name, (int)keepAlive.TotalSeconds);

        var query = filterResult.Value ?? new Query { MatchAll = new MatchAllQuery() };
        var token = EncodeToken(pitResponse.Id, (int)keepAlive.TotalSeconds, request.PageSize, query, searchAfter: null);
        return Result<SearchCursor>.Success(new SearchCursor(token, _clock.UtcNow.Add(keepAlive)));
    }

    /// <inheritdoc />
    public async Task<Result<CursorPage<TDocument>>> ReadCursorAsync(SearchCursor cursor, CancellationToken cancellationToken = default)
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
            return Result<CursorPage<TDocument>>.Failure(ElasticSearchErrors.InvalidCursor(ex.Message));
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
        catch (Exception)
        {
            return Result<CursorPage<TDocument>>.Failure(ElasticSearchErrors.CursorExpired(_clock.UtcNow));
        }

        if (!response.IsValidResponse)
        {
            return Result<CursorPage<TDocument>>.Failure(ElasticSearchErrors.CursorExpired(_clock.UtcNow));
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
    public async Task<Result> CloseCursorAsync(SearchCursor cursor, CancellationToken cancellationToken = default)
    {
        CursorState state;
        try
        {
            state = DecodeToken(cursor.Token);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Result.Failure(ElasticSearchErrors.InvalidCursor(ex.Message));
        }

        var response = await _client.ClosePointInTimeAsync(new ClosePointInTimeRequest(state.PitId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsValidResponse)
        {
            _logger.ElasticSearchPointInTimeCloseFailed(_definition.Name);
            return Result.Failure(SearchErrors.EngineFault(SearchWellKnown.ElasticSearchProviderName, "CloseCursorAsync", response.DebugInformation));
        }

        return Result.Success();
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
        return JsonSerializer.Deserialize<CursorState>(json) ?? throw new FormatException("Cursor token is empty.");
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
