using System.Diagnostics;
using System.Runtime.CompilerServices;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
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
using SharedKernel.Search.ElasticSearch.Diagnostics;
using SharedKernel.Search.ElasticSearch.Errors;
using SharedKernel.Search.ElasticSearch.Logging;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.ElasticSearch.Querying;
// Elastic.Clients.Elasticsearch declares its own non-generic SearchRequest/Result types; alias ours
// explicitly so the bare identifiers in this file resolve to the neutral domain contract.
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;
using Result = SharedKernel.Primitives.Results.Result;

namespace SharedKernel.Search.ElasticSearch.Index;

/// <summary>The ElasticSearch implementation of <see cref="ISearchIndex{TDocument}"/> — scoped.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// Reads and filtered writes target the read alias (<see cref="IndexName"/>); single/bulk writes
/// target the write alias — the ElasticSearch alias-based cutover model
/// (<c>IndexCutoverRequest</c>'s own remarks) means the two may point at different concrete indexes
/// during a rebuild.
/// </remarks>
internal sealed class ElasticSearchIndex<TDocument> : ISearchIndex<TDocument>
    where TDocument : class, ISearchDocument
{
    private const string OperationIndex = "index";
    private const string OperationIndexMany = "index_many";
    private const string OperationDelete = "delete";
    private const string OperationDeleteMany = "delete_many";
    private const string OperationDeleteByFilter = "delete_by_filter";
    private const string OperationClear = "clear";
    private const string OperationWaitUntilSearchable = "wait_until_searchable";
    private const string OperationSearch = "search";
    private const string OperationGet = "get";
    private const string OperationCount = "count";
    private const string OperationEnumerate = "enumerate";

    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly string _writeAlias;
    private readonly ElasticSearchOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<ElasticSearchIndex<TDocument>> _logger;

    /// <summary>Initializes a new <see cref="ElasticSearchIndex{TDocument}"/>.</summary>
    public ElasticSearchIndex(
        ElasticsearchClient client,
        SearchIndexDefinition definition,
        string writeAlias,
        ElasticSearchOptions options,
        IClock clock,
        ILogger<ElasticSearchIndex<TDocument>> logger)
    {
        _client = client;
        _definition = definition;
        _writeAlias = writeAlias;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string IndexName => _definition.Name;

    /// <inheritdoc />
    public Task<Result<SearchWriteReceipt>> IndexAsync(
        TDocument document, SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
        => ExecuteAsync(
            OperationIndex,
            ct => IndexCoreAsync(document, consistency, ct),
            documentCount: 1,
            cancellationToken);

    /// <inheritdoc />
    public Task<Result<SearchWriteReceipt>> DeleteAsync(
        string documentId, SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
        => ExecuteAsync(
            OperationDelete,
            ct => DeleteCoreAsync(documentId, consistency, ct),
            documentCount: 1,
            cancellationToken);

    /// <inheritdoc />
    public Task<Result<SearchWriteReceipt>> DeleteByFilterAsync(
        SearchFilter filter,
        TenantScope tenantScope,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            OperationDeleteByFilter,
            ct => DeleteByFilterCoreAsync(filter, tenantScope, consistency, ct),
            documentCount: 0,
            cancellationToken);

    /// <inheritdoc />
    public async Task<Result> ClearAsync(
        SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
                OperationClear,
                async ct =>
                {
                    var inner = await ClearCoreAsync(consistency, ct).ConfigureAwait(false);
                    return inner.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(inner.Error);
                },
                documentCount: 0,
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    /// <inheritdoc />
    public async Task<Result> WaitUntilSearchableAsync(
        SearchWriteReceipt receipt, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
                OperationWaitUntilSearchable,
                async ct =>
                {
                    var inner = await WaitUntilSearchableCoreAsync(receipt, timeout, ct).ConfigureAwait(false);
                    return inner.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(inner.Error);
                },
                documentCount: 0,
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    /// <inheritdoc />
    public Task<Result<SearchResults<TDocument>>> SearchAsync(
        SearchRequest request, TenantScope tenantScope, CancellationToken cancellationToken = default)
        => ExecuteAsync(
            OperationSearch,
            ct => SearchCoreAsync(request, tenantScope, ct),
            documentCount: 0,
            cancellationToken);

    /// <inheritdoc />
    public Task<Result<TDocument>> GetAsync(
        string documentId, TenantScope tenantScope, CancellationToken cancellationToken = default)
        => ExecuteAsync(
            OperationGet,
            ct => GetCoreAsync(documentId, tenantScope, ct),
            documentCount: 0,
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Always <see cref="TotalHitsAccuracy.Exact"/> on this provider: it answers from ElasticSearch's
    /// own <c>_count</c> API, which has no equivalent of the ceiling that forces the Meilisearch
    /// sibling to report a lower bound.
    /// </remarks>
    public Task<Result<SearchCount>> CountAsync(
        SearchFilter? filter, TenantScope tenantScope, CancellationToken cancellationToken = default)
        => ExecuteAsync(
            OperationCount,
            ct => CountCoreAsync(filter, tenantScope, ct),
            documentCount: 0,
            cancellationToken);

    /// <inheritdoc />
    private async Task<Result<SearchWriteReceipt>> IndexCoreAsync(
        TDocument document, SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        if (!IsValidDocumentId(document.DocumentId))
        {
            return Result<SearchWriteReceipt>.Failure(SearchErrors.InvalidDocumentId(document.DocumentId));
        }

        var refresh = ToRefresh(consistency);
        var request = new IndexRequest<TDocument>(document, _writeAlias, new Id(document.DocumentId)) { Refresh = refresh };
        var response = await _client.IndexAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsValidResponse)
        {
            _logger.ElasticSearchEngineFault("IndexAsync", _definition.Name, (int?)response.ApiCallDetails?.HttpStatusCode);
            return Result<SearchWriteReceipt>.Failure(ElasticSearchFaultMapper.MapWrite(response, _definition.Name, OperationIndex, DescribeEndpoint()));
        }

        _logger.ElasticSearchDocumentsIndexed(_definition.Name, 1, refresh.ToString());

        return Result<SearchWriteReceipt>.Success(new SearchWriteReceipt
        {
            IndexName = _definition.Name,
            ProviderToken = $"{response.Index}:{response.SeqNo}:{response.PrimaryTerm}",
            AffectedCount = 1,
            RequestedConsistency = consistency,
            AcceptedAt = _clock.UtcNow,
        });
    }

    /// <inheritdoc />
    public Task<Result<SearchBulkReceipt>> IndexManyAsync(
        IReadOnlyCollection<TDocument> documents,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default)
        => IndexManyAsync(documents, consistency, SearchBulkWriteOptions.Default, cancellationToken);

    /// <inheritdoc />
    public Task<Result<SearchBulkReceipt>> IndexManyAsync(
        IReadOnlyCollection<TDocument> documents,
        SearchWriteConsistency consistency,
        SearchBulkWriteOptions bulkOptions,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            OperationIndexMany,
            ct => IndexManyCoreAsync(documents, consistency, bulkOptions, ct),
            documents.Count,
            cancellationToken);

    /// <inheritdoc />
    private async Task<Result<SearchBulkReceipt>> IndexManyCoreAsync(
        IReadOnlyCollection<TDocument> documents,
        SearchWriteConsistency consistency,
        SearchBulkWriteOptions bulkOptions,
        CancellationToken cancellationToken = default)
    {
        foreach (var document in documents)
        {
            if (!IsValidDocumentId(document.DocumentId))
            {
                return Result<SearchBulkReceipt>.Failure(SearchErrors.InvalidDocumentId(document.DocumentId));
            }
        }

        if (documents.Count == 0)
        {
            return Result<SearchBulkReceipt>.Success(EmptyBulkReceipt(consistency));
        }

        var refresh = ToRefresh(consistency);
        var path = $"{Uri.EscapeDataString(_writeAlias)}/_bulk?refresh={ToRefreshQueryValue(refresh)}";
        var failures = new List<SearchItemFailure>();
        var succeededCount = 0;
        var stopwatch = Stopwatch.StartNew();
        var isFirstBatch = true;

        foreach (var batch in SerializeAndBatch(documents, _options.BulkMaxDocuments, _options.BulkMaxBytes))
        {
            if (!isFirstBatch && bulkOptions.MaxBatchesPerSecond is { } maxBatchesPerSecond)
            {
                var delay = TimeSpan.FromSeconds(1.0 / maxBatchesPerSecond);
                _logger.ElasticSearchBulkThrottled(_definition.Name, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            isFirstBatch = false;

            var bulkBody = ConcatenateBulkBody(batch);
            var response = await _client.Transport
                .RequestAsync<BulkResponse>(
                    Elastic.Transport.HttpMethod.POST, path, PostData.Bytes(bulkBody), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsValidResponse && response.Items.Count == 0)
            {
                return Result<SearchBulkReceipt>.Failure(
                    ElasticSearchFaultMapper.MapWrite(response, _definition.Name, OperationIndexMany, DescribeEndpoint()));
            }

            var errorItems = response.ItemsWithErrors.ToList();
            foreach (var item in errorItems)
            {
                failures.Add(new SearchItemFailure
                {
                    DocumentId = item.Id ?? string.Empty,
                    Error = SearchErrors.WriteRejected(_definition.Name, item.Error?.Reason ?? "unknown"),
                });
            }

            succeededCount += response.Items.Count - errorItems.Count;
        }

        _logger.ElasticSearchBulkCompleted(_definition.Name, documents.Count, (long)stopwatch.ElapsedMilliseconds);
        if (failures.Count > 0)
        {
            _logger.ElasticSearchBulkPartialFailure(_definition.Name, failures.Count, documents.Count);
        }

        return Result<SearchBulkReceipt>.Success(new SearchBulkReceipt
        {
            Receipt = new SearchWriteReceipt
            {
                IndexName = _definition.Name,
                ProviderToken = $"{_writeAlias}:bulk:{documents.Count}",
                AffectedCount = documents.Count,
                RequestedConsistency = consistency,
                AcceptedAt = _clock.UtcNow,
            },
            SucceededCount = succeededCount,
            Failures = failures,
        });
    }

    /// <inheritdoc />
    private async Task<Result<SearchWriteReceipt>> DeleteCoreAsync(
        string documentId, SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        if (!IsValidDocumentId(documentId))
        {
            return Result<SearchWriteReceipt>.Failure(SearchErrors.InvalidDocumentId(documentId));
        }

        var refresh = ToRefresh(consistency);
        var request = new DeleteRequest(_writeAlias, documentId) { Refresh = refresh };
        var response = await _client.DeleteAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsValidResponse)
        {
            _logger.ElasticSearchEngineFault("DeleteAsync", _definition.Name, (int?)response.ApiCallDetails?.HttpStatusCode);
            return Result<SearchWriteReceipt>.Failure(ElasticSearchFaultMapper.MapWrite(response, _definition.Name, OperationDelete, DescribeEndpoint()));
        }

        return Result<SearchWriteReceipt>.Success(new SearchWriteReceipt
        {
            IndexName = _definition.Name,
            ProviderToken = $"{response.Index}:{response.SeqNo}:{response.PrimaryTerm}",
            AffectedCount = 1,
            RequestedConsistency = consistency,
            AcceptedAt = _clock.UtcNow,
        });
    }

    /// <inheritdoc />
    public Task<Result<SearchBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> documentIds,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default)
        => DeleteManyAsync(documentIds, consistency, SearchBulkWriteOptions.Default, cancellationToken);

    /// <inheritdoc />
    public Task<Result<SearchBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> documentIds,
        SearchWriteConsistency consistency,
        SearchBulkWriteOptions bulkOptions,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            OperationDeleteMany,
            ct => DeleteManyCoreAsync(documentIds, consistency, bulkOptions, ct),
            documentIds.Count,
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// This provider issues a delete-by-id bulk operation as a single request regardless of
    /// <paramref name="documentIds"/>'s size — there is no existing per-batch dispatch loop to pace, so
    /// <paramref name="bulkOptions"/>'s <see cref="SearchBulkWriteOptions.MaxBatchesPerSecond"/> has no
    /// observable effect here (see <see cref="IndexManyAsync(IReadOnlyCollection{TDocument}, SearchWriteConsistency, SearchBulkWriteOptions, CancellationToken)"/>
    /// for the throttled path, which does chunk).
    /// </remarks>
    private async Task<Result<SearchBulkReceipt>> DeleteManyCoreAsync(
        IReadOnlyCollection<string> documentIds,
        SearchWriteConsistency consistency,
        SearchBulkWriteOptions bulkOptions,
        CancellationToken cancellationToken = default)
    {
        _ = bulkOptions;

        foreach (var documentId in documentIds)
        {
            if (!IsValidDocumentId(documentId))
            {
                return Result<SearchBulkReceipt>.Failure(SearchErrors.InvalidDocumentId(documentId));
            }
        }

        if (documentIds.Count == 0)
        {
            return Result<SearchBulkReceipt>.Success(EmptyBulkReceipt(consistency));
        }

        var refresh = ToRefresh(consistency);
        var operations = new BulkOperationsCollection(
            documentIds.Select(id => (IBulkOperation)new BulkDeleteOperation(id) { Index = _writeAlias }));
        var request = new BulkRequest(_writeAlias) { Operations = operations, Refresh = refresh };
        var response = await _client.BulkAsync(request, cancellationToken).ConfigureAwait(false);

        var errorItems = response.ItemsWithErrors.ToList();
        var failures = errorItems
            .Select(item => new SearchItemFailure
            {
                DocumentId = item.Id ?? string.Empty,
                Error = SearchErrors.WriteRejected(_definition.Name, item.Error?.Reason ?? "unknown"),
            })
            .ToList();

        _logger.ElasticSearchBulkCompleted(_definition.Name, documentIds.Count, 0);
        if (failures.Count > 0)
        {
            _logger.ElasticSearchBulkPartialFailure(_definition.Name, failures.Count, documentIds.Count);
        }

        return Result<SearchBulkReceipt>.Success(new SearchBulkReceipt
        {
            Receipt = new SearchWriteReceipt
            {
                IndexName = _definition.Name,
                ProviderToken = $"{_writeAlias}:bulk:{documentIds.Count}",
                AffectedCount = documentIds.Count,
                RequestedConsistency = consistency,
                AcceptedAt = _clock.UtcNow,
            },
            SucceededCount = documentIds.Count - errorItems.Count,
            Failures = failures,
        });
    }

    /// <inheritdoc />
    private async Task<Result<SearchWriteReceipt>> DeleteByFilterCoreAsync(
        SearchFilter filter,
        TenantScope tenantScope,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default)
    {
        var filterResult = ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            _logger.ElasticSearchTenantScopeMissing(_definition.Name);
            return Result<SearchWriteReceipt>.Failure(filterResult.Error);
        }

        var request = new DeleteByQueryRequest(_writeAlias)
        {
            Query = filterResult.Value ?? new Query { MatchAll = new MatchAllQuery() },
            Refresh = consistency == SearchWriteConsistency.Searchable,
        };

        var response = await _client.DeleteByQueryAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            return Result<SearchWriteReceipt>.Failure(ElasticSearchFaultMapper.MapWrite(response, _definition.Name, OperationDeleteByFilter, DescribeEndpoint()));
        }

        return Result<SearchWriteReceipt>.Success(new SearchWriteReceipt
        {
            IndexName = _definition.Name,
            ProviderToken = $"{_writeAlias}:deleteByQuery",
            AffectedCount = (int)Math.Min(response.Deleted ?? 0, int.MaxValue),
            RequestedConsistency = consistency,
            AcceptedAt = _clock.UtcNow,
        });
    }

    /// <inheritdoc />
    private async Task<Result> ClearCoreAsync(SearchWriteConsistency consistency, CancellationToken cancellationToken)
    {
        var request = new DeleteByQueryRequest(_writeAlias)
        {
            Query = new Query { MatchAll = new MatchAllQuery() },
            Refresh = consistency == SearchWriteConsistency.Searchable,
        };

        var response = await _client.DeleteByQueryAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsValidResponse
            ? Result.Success()
            : Result.Failure(ElasticSearchFaultMapper.MapWrite(response, _definition.Name, OperationClear, DescribeEndpoint()));
    }

    /// <inheritdoc />
    private async Task<Result> WaitUntilSearchableCoreAsync(
        SearchWriteReceipt receipt, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var indexName = receipt.ProviderToken.Split(':') is [var name, ..] && !string.IsNullOrEmpty(name)
            ? name
            : _writeAlias;

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await _client.Indices.RefreshAsync(indexName, cancellationToken).ConfigureAwait(false);
            if (response.IsValidResponse)
            {
                return Result.Success();
            }

            if (stopwatch.Elapsed >= timeout)
            {
                _logger.ElasticSearchRefreshWaitTimedOut(_definition.Name, (long)stopwatch.ElapsedMilliseconds);
                return Result.Failure(SearchErrors.WriteTimeout(_definition.Name, stopwatch.Elapsed));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    private async Task<Result<SearchResults<TDocument>>> SearchCoreAsync(
        SearchRequest request, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        var validation = ElasticSearchRequestValidator.Validate(_definition, request);
        if (validation.IsFailure)
        {
            _logger.ElasticSearchRequestRejected(_definition.Name, validation.Error.Message);
            return Result<SearchResults<TDocument>>.Failure(validation.Error);
        }

        var filterResult = ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, request.Filter, tenantScope);
        if (filterResult.IsFailure)
        {
            _logger.ElasticSearchTenantScopeMissing(_definition.Name);
            return Result<SearchResults<TDocument>>.Failure(filterResult.Error);
        }

        var searchRequest = ElasticSearchRequestTranslator.Translate<TDocument>(
            _definition.Name, _definition, request, filterResult.Value);

        var response = await _client.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);

        // Classify before mapping. The result mapper's job is shaping a successful response into the
        // neutral model; asking it to also diagnose why the cluster said no collapsed an outage, a
        // rejected credential and a malformed query into one indistinguishable failure.
        if (!response.IsValidResponse)
        {
            _logger.ElasticSearchEngineFault(
                OperationSearch, _definition.Name, (int?)response.ApiCallDetails?.HttpStatusCode);
            return Result<SearchResults<TDocument>>.Failure(
                ElasticSearchFaultMapper.Map(response, _definition.Name, OperationSearch, DescribeEndpoint()));
        }

        var mapped = ElasticSearchResultMapper.Map(
            response,
            _definition.MaxFacetValues,
            request.Facets,
            request.NumericFacetStats,
            request.Page,
            request.PageSize,
            SearchWellKnown.ElasticSearchProviderName);

        if (mapped.IsSuccess)
        {
            _logger.ElasticSearchSearchExecuted(
                _definition.Name, mapped.Value.Hits.Count, mapped.Value.TotalHits, mapped.Value.Accuracy.ToString(), response.Took);
        }
        else
        {
            _logger.ElasticSearchEngineFault("SearchAsync", _definition.Name, (int?)response.ApiCallDetails?.HttpStatusCode);
        }

        return mapped;
    }

    /// <inheritdoc />
    private async Task<Result<TDocument>> GetCoreAsync(
        string documentId, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        if (_definition.TenantField is not null && tenantScope.IsGlobal)
        {
            _logger.ElasticSearchTenantScopeMissing(_definition.Name);
            return Result<TDocument>.Failure(SearchErrors.TenantScopeMissing(_definition.Name));
        }

        var idsQuery = new Query { Ids = new IdsQuery { Values = new[] { documentId } } };
        var filterResult = ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, filter: null, tenantScope);
        if (filterResult.IsFailure)
        {
            return Result<TDocument>.Failure(filterResult.Error);
        }

        var query = filterResult.Value is { } tenantQuery
            ? new Query { Bool = new BoolQuery { Filter = [idsQuery, tenantQuery] } }
            : idsQuery;

        var searchRequest = new global::Elastic.Clients.Elasticsearch.SearchRequest<TDocument>(_definition.Name)
        {
            Query = query,
            Size = 1,
        };

        var response = await _client.SearchAsync<TDocument>(searchRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            return Result<TDocument>.Failure(
                ElasticSearchFaultMapper.Map(response, _definition.Name, OperationGet, DescribeEndpoint()));
        }

        var hit = response.HitsMetadata.Hits.FirstOrDefault();
        if (hit?.Source is null)
        {
            return Result<TDocument>.Failure(SearchErrors.DocumentNotFound(_definition.Name, documentId));
        }

        return Result<TDocument>.Success(hit.Source);
    }

    /// <inheritdoc />
    private async Task<Result<SearchCount>> CountCoreAsync(
        SearchFilter? filter, TenantScope tenantScope, CancellationToken cancellationToken)
    {
        var filterResult = ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            _logger.ElasticSearchTenantScopeMissing(_definition.Name);
            return Result<SearchCount>.Failure(filterResult.Error);
        }

        var request = new CountRequest(_definition.Name);
        if (filterResult.Value is { } query)
        {
            request.Query = query;
        }

        var response = await _client.CountAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            return Result<SearchCount>.Failure(
                ElasticSearchFaultMapper.Map(response, _definition.Name, OperationCount, DescribeEndpoint()));
        }

        // Always exact: the _count API walks the whole match set rather than reading a capped
        // hits.total off a search response, so ElasticSearch has no equivalent of the Meilisearch
        // maxTotalHits ceiling that forces its sibling to report a lower bound.
        return Result<SearchCount>.Success(SearchCount.Exact(response.Count));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TDocument> EnumerateAsync(
        SearchFilter? filter,
        TenantScope tenantScope,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var filterResult = ElasticSearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            throw new SearchStreamException(filterResult.Error);
        }

        var keepAlive = TimeSpan.FromSeconds(_options.PointInTimeKeepAliveSeconds);
        var pitResponse = await _client
            .OpenPointInTimeAsync(new OpenPointInTimeRequest(_definition.Name) { KeepAlive = keepAlive }, cancellationToken)
            .ConfigureAwait(false);

        if (!pitResponse.IsValidResponse)
        {
            throw new SearchStreamException(
                ElasticSearchFaultMapper.Map(pitResponse, _definition.Name, OperationEnumerate, DescribeEndpoint()));
        }

        var pitId = pitResponse.Id;
        _logger.ElasticSearchPointInTimeOpened(_definition.Name, _options.PointInTimeKeepAliveSeconds);
        var batchCount = 0;
        var startTimestamp = Stopwatch.GetTimestamp();
        using var activity = SearchDiagnostics.StartActivity(OperationEnumerate, _definition.Name);

        try
        {
            ICollection<FieldValue>? searchAfter = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var request = new global::Elastic.Clients.Elasticsearch.SearchRequest<TDocument>
                {
                    Size = batchSize,
                    Query = filterResult.Value ?? new Query { MatchAll = new MatchAllQuery() },
                    Sort = [new SortOptions { Field = new FieldSort("_doc") }],
                    Pit = new PointInTimeReference(pitId) { KeepAlive = keepAlive },
                };

                if (searchAfter is not null)
                {
                    request.SearchAfter = searchAfter;
                }

                var response = await _client.SearchAsync<TDocument>(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsValidResponse)
                {
                    throw new SearchStreamException(
                        ElasticSearchFaultMapper.Map(response, _definition.Name, OperationEnumerate, DescribeEndpoint()));
                }

                batchCount++;
                var hits = response.HitsMetadata.Hits.ToList();
                if (hits.Count == 0)
                {
                    yield break;
                }

                foreach (var hit in hits)
                {
                    if (hit.Source is { } source)
                    {
                        yield return source;
                    }
                }

                if (hits.Count < batchSize)
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

            SearchDiagnostics.Complete(
                activity, OperationEnumerate, _definition.Name, startTimestamp, errorCode: null);
        }
    }

    private static Refresh ToRefresh(SearchWriteConsistency consistency)
        // Refresh.True is never emitted — it forces an immediate cluster-wide refresh disturbing
        // other in-flight requests, with no Meilisearch analogue.
        => consistency == SearchWriteConsistency.Searchable ? Refresh.WaitFor : Refresh.False;

    private static string ToRefreshQueryValue(Refresh refresh) => refresh switch
    {
        Refresh.WaitFor => "wait_for",
        Refresh.True => "true",
        _ => "false",
    };

    private static bool IsValidDocumentId(string documentId)
        => documentId.Length > 0 && documentId.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private SearchBulkReceipt EmptyBulkReceipt(SearchWriteConsistency consistency) => new()
    {
        Receipt = new SearchWriteReceipt
        {
            IndexName = _definition.Name,
            ProviderToken = string.Empty,
            AffectedCount = 0,
            RequestedConsistency = consistency,
            AcceptedAt = _clock.UtcNow,
        },
        SucceededCount = 0,
    };

    /// <summary>
    /// Serializes each document's <c>_bulk</c> index-operation wire bytes exactly once — via
    /// <see cref="ElasticsearchClient.ElasticsearchClientSettings"/>, the same settings
    /// <see cref="ElasticsearchClient.BulkAsync(BulkRequest, CancellationToken)"/> would have used —
    /// and groups them into batches bounded by <paramref name="maxDocuments"/> and
    /// <paramref name="maxBytes"/> using the real serialized length rather than a separate estimate.
    /// Each document's buffer is reused verbatim as part of the wire body in
    /// <see cref="ConcatenateBulkBody"/>, eliminating the prior double-serialization (once for a
    /// chunk-size estimate via a standalone <c>JsonSerializer.SerializeToUtf8Bytes</c> call, once more
    /// inside the SDK's own typed <c>BulkAsync</c> — a pass that could silently diverge from the first
    /// if a source-serializer context were configured, since the estimate never saw it).
    /// </summary>
    private IEnumerable<List<byte[]>> SerializeAndBatch(
        IReadOnlyCollection<TDocument> documents, int maxDocuments, int maxBytes)
    {
        var batch = new List<byte[]>();
        var batchBytes = 0;

        foreach (var document in documents)
        {
            var serialized = SerializeIndexOperation(document);
            if (batch.Count > 0 && (batch.Count >= maxDocuments || batchBytes + serialized.Length > maxBytes))
            {
                yield return batch;
                batch = [];
                batchBytes = 0;
            }

            batch.Add(serialized);
            batchBytes += serialized.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    /// <summary>
    /// Serializes one document's <c>_bulk</c> index-operation (the action-meta line plus the source
    /// line) via the SDK's own <see cref="BulkOperationsCollection.Serialize"/> — never a standalone
    /// raw <c>System.Text.Json</c> call — so the byte buffer is guaranteed identical to what the SDK
    /// itself would have produced for this document, and reusable verbatim on the wire.
    /// </summary>
    private byte[] SerializeIndexOperation(TDocument document)
    {
        var operations = new BulkOperationsCollection(
            new IBulkOperation[] { new BulkIndexOperation<TDocument>(document, _writeAlias) { Id = document.DocumentId } });
        using var stream = new MemoryStream();
        operations.Serialize(stream, _client.ElasticsearchClientSettings, SerializationFormatting.None);
        return stream.ToArray();
    }

    /// <summary>
    /// Concatenates a batch's pre-serialized per-document buffers into one <c>_bulk</c> NDJSON body.
    /// Safe because each buffer is an independently complete, newline-terminated
    /// <c>{action}\n{source}\n</c> pair — concatenating N such buffers is byte-identical to serializing
    /// all N operations together as one <see cref="BulkOperationsCollection"/> (verified against the
    /// real compiled SDK; see <c>src/Infrastructure/Search/CLAUDE.md</c>).
    /// </summary>
    private static byte[] ConcatenateBulkBody(List<byte[]> batch)
    {
        var totalLength = batch.Sum(bytes => bytes.Length);
        var buffer = new byte[totalLength];
        var offset = 0;
        foreach (var bytes in batch)
        {
            Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
            offset += bytes.Length;
        }

        return buffer;
    }

    /// <summary>
    /// Returns the endpoint description used in unreachability errors — the configured node list, never
    /// a credential.
    /// </summary>
    private string DescribeEndpoint() => string.Join(",", _options.Nodes);

    /// <summary>
    /// Runs <paramref name="action"/> inside a <c>search {operation}</c> client span, records the
    /// operation-duration histogram and the document counter, and converts an unexpected exception into
    /// a classified <see cref="SearchErrors"/> failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ElasticSearch client reports a failed call by returning a response whose
    /// <c>IsValidResponse</c> is <see langword="false"/> rather than throwing, so the individual
    /// <c>…CoreAsync</c> bodies already classify their own failures through
    /// <see cref="ElasticSearchFaultMapper"/>. The catch here is the backstop for what the client does
    /// still throw — serializer failures, and the <c>SwitchExpressionException</c> a closed-hierarchy
    /// translation switch raises if a future filter node reaches an adapter that has not been taught to
    /// translate it. Both would otherwise escape a <c>Result</c>-returning method.
    /// </para>
    /// <para>
    /// <see cref="OperationCanceledException"/> raised by the caller's own token is rethrown untouched:
    /// cancellation is an instruction, not an engine failure.
    /// </para>
    /// </remarks>
    private async Task<Result<T>> ExecuteAsync<T>(
        string operation,
        Func<CancellationToken, Task<Result<T>>> action,
        int documentCount,
        CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        using var activity = SearchDiagnostics.StartActivity(operation, _definition.Name);

        Result<T> result;
        try
        {
            result = await action(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = SearchErrors.EngineFault(
                SearchWellKnown.ElasticSearchProviderName, operation, ex.Message);
            _logger.ElasticSearchOperationFaulted(operation, _definition.Name, error.Code);
            result = Result<T>.Failure(error);
        }

        if (result.IsSuccess)
        {
            SearchDiagnostics.RecordDocuments(operation, _definition.Name, documentCount);
        }

        SearchDiagnostics.Complete(
            activity, operation, _definition.Name, startTimestamp, result.IsFailure ? result.Error.Code : null);
        return result;
    }
}
