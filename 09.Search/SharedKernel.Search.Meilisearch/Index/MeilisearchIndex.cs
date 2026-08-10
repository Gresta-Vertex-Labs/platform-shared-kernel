using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Errors;
using SharedKernel.Search.Meilisearch.Logging;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Search.Meilisearch.Querying;

namespace SharedKernel.Search.Meilisearch.Index;

/// <summary>The Meilisearch implementation of <see cref="ISearchIndex{TDocument}"/> — scoped.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
internal sealed class MeilisearchIndex<TDocument> : ISearchIndex<TDocument>
    where TDocument : class, ISearchDocument
{
    private readonly global::Meilisearch.MeilisearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly MeilisearchOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<MeilisearchIndex<TDocument>> _logger;

    /// <summary>Initializes a new <see cref="MeilisearchIndex{TDocument}"/>.</summary>
    public MeilisearchIndex(
        global::Meilisearch.MeilisearchClient client,
        SearchIndexDefinition definition,
        MeilisearchOptions options,
        IClock clock,
        ILogger<MeilisearchIndex<TDocument>> logger)
    {
        _client = client;
        _definition = definition;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string IndexName => _definition.Name;

    /// <inheritdoc />
    public async Task<Result<SearchWriteReceipt>> IndexAsync(
        TDocument document, SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        if (!IsValidDocumentId(document.DocumentId))
        {
            return Result<SearchWriteReceipt>.Failure(SearchErrors.InvalidDocumentId(document.DocumentId));
        }

        var task = await _client.Index(_definition.Name)
            .AddDocumentsAsync([document], _definition.PrimaryKeyField, cancellationToken)
            .ConfigureAwait(false);
        _logger.MeilisearchDocumentsEnqueued(_definition.Name, 1, task.TaskUid.ToString(CultureInfo.InvariantCulture));
        return await BuildReceiptAsync(task, affectedCount: 1, consistency, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<SearchBulkReceipt>> IndexManyAsync(
        IReadOnlyCollection<TDocument> documents,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default)
        => IndexManyAsync(documents, consistency, SearchBulkWriteOptions.Default, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Dispatches one batch at a time via the SDK's single-batch <c>AddDocumentsAsync</c> — the same
    /// call <see cref="IndexAsync"/> already uses for a one-element list — rather than the SDK's own
    /// <c>AddDocumentsInBatchesAsync</c>, which issues every batch internally with no seam for
    /// inter-batch pacing. This explicit loop is what lets
    /// <paramref name="bulkOptions"/>.<see cref="SearchBulkWriteOptions.MaxBatchesPerSecond"/> insert a
    /// delay between batches; batch boundaries are otherwise unchanged
    /// (<see cref="MeilisearchOptions.DefaultBatchSize"/>-sized chunks, in document order).
    /// </remarks>
    public async Task<Result<SearchBulkReceipt>> IndexManyAsync(
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
            return Result<SearchBulkReceipt>.Success(new SearchBulkReceipt
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
            });
        }

        var documentList = documents.ToList();
        var meilisearchIndex = _client.Index(_definition.Name);
        var tasks = new List<global::Meilisearch.TaskInfo>();
        var isFirstBatch = true;

        for (var batchStart = 0; batchStart < documentList.Count; batchStart += _options.DefaultBatchSize)
        {
            if (!isFirstBatch && bulkOptions.MaxBatchesPerSecond is { } maxBatchesPerSecond)
            {
                var delay = TimeSpan.FromSeconds(1.0 / maxBatchesPerSecond);
                _logger.MeilisearchBulkThrottled(_definition.Name, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            isFirstBatch = false;

            var batchCount = Math.Min(_options.DefaultBatchSize, documentList.Count - batchStart);
            var batch = documentList.GetRange(batchStart, batchCount);
            var task = await meilisearchIndex
                .AddDocumentsAsync(batch, _definition.PrimaryKeyField, cancellationToken)
                .ConfigureAwait(false);
            tasks.Add(task);
        }

        var lastTaskUid = tasks.Count > 0 ? tasks[^1].TaskUid.ToString(CultureInfo.InvariantCulture) : string.Empty;
        _logger.MeilisearchDocumentsEnqueued(_definition.Name, documents.Count, lastTaskUid);

        var failures = new List<SearchItemFailure>();
        var succeededCount = documents.Count;

        // Meilisearch reports task failure at the batch level, not per document — the engine does not
        // identify which document within a failed batch caused the failure, so every document in a
        // failed batch is reported as failed.
        if (consistency == SearchWriteConsistency.Searchable)
        {
            var batchIndex = 0;
            foreach (var task in tasks)
            {
                var waitResult = await WaitForTaskAsync(
                        task.TaskUid, TimeSpan.FromSeconds(_options.TaskWaitTimeoutSeconds), cancellationToken)
                    .ConfigureAwait(false);
                if (waitResult.IsFailure)
                {
                    var batchStart = batchIndex * _options.DefaultBatchSize;
                    var batchCount = Math.Min(_options.DefaultBatchSize, documentList.Count - batchStart);
                    for (var i = 0; i < batchCount; i++)
                    {
                        failures.Add(new SearchItemFailure
                        {
                            DocumentId = documentList[batchStart + i].DocumentId,
                            Error = waitResult.Error,
                        });
                    }

                    succeededCount -= batchCount;
                }

                batchIndex++;
            }

            if (failures.Count > 0)
            {
                _logger.MeilisearchBulkPartialFailure(_definition.Name, failures.Count, documents.Count);
            }
        }

        return Result<SearchBulkReceipt>.Success(new SearchBulkReceipt
        {
            Receipt = new SearchWriteReceipt
            {
                IndexName = _definition.Name,
                ProviderToken = lastTaskUid,
                AffectedCount = documents.Count,
                RequestedConsistency = consistency,
                AcceptedAt = _clock.UtcNow,
            },
            SucceededCount = succeededCount,
            Failures = failures,
        });
    }

    /// <inheritdoc />
    public async Task<Result<SearchWriteReceipt>> DeleteAsync(
        string documentId, SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        if (!IsValidDocumentId(documentId))
        {
            return Result<SearchWriteReceipt>.Failure(SearchErrors.InvalidDocumentId(documentId));
        }

        var task = await _client.Index(_definition.Name).DeleteOneDocumentAsync(documentId, cancellationToken)
            .ConfigureAwait(false);
        _logger.MeilisearchDocumentsDeleted(_definition.Name, 1, task.TaskUid.ToString(CultureInfo.InvariantCulture));
        return await BuildReceiptAsync(task, affectedCount: 1, consistency, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<SearchBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> documentIds,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default)
        => DeleteManyAsync(documentIds, consistency, SearchBulkWriteOptions.Default, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// This provider issues a document-id bulk delete as a single request regardless of
    /// <paramref name="documentIds"/>'s size — there is no existing per-batch dispatch loop to pace, so
    /// <paramref name="bulkOptions"/>'s <see cref="SearchBulkWriteOptions.MaxBatchesPerSecond"/> has no
    /// observable effect here (see
    /// <see cref="IndexManyAsync(IReadOnlyCollection{TDocument}, SearchWriteConsistency, SearchBulkWriteOptions, CancellationToken)"/>
    /// for the throttled path, which does chunk).
    /// </remarks>
    public async Task<Result<SearchBulkReceipt>> DeleteManyAsync(
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

        var task = await _client.Index(_definition.Name).DeleteDocumentsAsync(documentIds, cancellationToken)
            .ConfigureAwait(false);
        _logger.MeilisearchDocumentsDeleted(
            _definition.Name, documentIds.Count, task.TaskUid.ToString(CultureInfo.InvariantCulture));

        var failures = new List<SearchItemFailure>();
        var succeededCount = documentIds.Count;

        if (consistency == SearchWriteConsistency.Searchable)
        {
            var waitResult = await WaitForTaskAsync(
                    task.TaskUid, TimeSpan.FromSeconds(_options.TaskWaitTimeoutSeconds), cancellationToken)
                .ConfigureAwait(false);
            if (waitResult.IsFailure)
            {
                foreach (var documentId in documentIds)
                {
                    failures.Add(new SearchItemFailure { DocumentId = documentId, Error = waitResult.Error });
                }

                succeededCount = 0;
                _logger.MeilisearchBulkPartialFailure(_definition.Name, failures.Count, documentIds.Count);
            }
        }

        return Result<SearchBulkReceipt>.Success(new SearchBulkReceipt
        {
            Receipt = new SearchWriteReceipt
            {
                IndexName = _definition.Name,
                ProviderToken = task.TaskUid.ToString(CultureInfo.InvariantCulture),
                AffectedCount = documentIds.Count,
                RequestedConsistency = consistency,
                AcceptedAt = _clock.UtcNow,
            },
            SucceededCount = succeededCount,
            Failures = failures,
        });
    }

    /// <inheritdoc />
    public async Task<Result<SearchWriteReceipt>> DeleteByFilterAsync(
        SearchFilter filter,
        TenantScope tenantScope,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default)
    {
        var filterResult = MeilisearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            _logger.MeilisearchTenantScopeMissing(_definition.Name);
            return Result<SearchWriteReceipt>.Failure(filterResult.Error);
        }

        var task = await _client.Index(_definition.Name)
            .DeleteDocumentsAsync(
                new global::Meilisearch.QueryParameters.DeleteDocumentsQuery { Filter = filterResult.Value },
                cancellationToken)
            .ConfigureAwait(false);
        _logger.MeilisearchDocumentsDeleted(_definition.Name, 0, task.TaskUid.ToString(CultureInfo.InvariantCulture));

        // The number of documents matched by the filter is not known without an extra round trip;
        // AffectedCount is reported as 0 for a filter-based delete.
        return await BuildReceiptAsync(task, affectedCount: 0, consistency, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> ClearAsync(
        SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        var task = await _client.Index(_definition.Name).DeleteAllDocumentsAsync(cancellationToken)
            .ConfigureAwait(false);
        var receiptResult = await BuildReceiptAsync(task, affectedCount: 0, consistency, cancellationToken)
            .ConfigureAwait(false);
        return receiptResult.IsSuccess ? Result.Success() : Result.Failure(receiptResult.Error);
    }

    /// <inheritdoc />
    public async Task<Result> WaitUntilSearchableAsync(
        SearchWriteReceipt receipt, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(receipt.ProviderToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var taskUid))
        {
            return Result.Failure(SearchErrors.EngineFault(
                SearchWellKnown.MeilisearchProviderName,
                "WaitUntilSearchableAsync",
                $"ProviderToken '{receipt.ProviderToken}' is not a valid Meilisearch task uid."));
        }

        return await WaitForTaskAsync(taskUid, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<SearchResults<TDocument>>> SearchAsync(
        SearchRequest request, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        var validation = MeilisearchRequestValidator.Validate(_definition, request);
        if (validation.IsFailure)
        {
            _logger.MeilisearchRequestRejected(_definition.Name, validation.Error.Message);
            return Result<SearchResults<TDocument>>.Failure(validation.Error);
        }

        var filterResult = MeilisearchFilterCompiler.CompileWithTenantScope(_definition, request.Filter, tenantScope);
        if (filterResult.IsFailure)
        {
            _logger.MeilisearchTenantScopeMissing(_definition.Name);
            return Result<SearchResults<TDocument>>.Failure(filterResult.Error);
        }

        var (query, attributes) = MeilisearchRequestTranslator.Translate(request, filterResult.Value);
        var searchable = await _client.Index(_definition.Name)
            .SearchAsync<JsonElement>(query, attributes, cancellationToken)
            .ConfigureAwait(false);

        var mapped = MeilisearchResultMapper.Map<TDocument>(
            searchable,
            request.RequireExactTotalHits,
            _definition.MaxFacetValues,
            request.Highlight?.PreTag,
            SearchWellKnown.MeilisearchProviderName);

        if (mapped.IsSuccess)
        {
            _logger.MeilisearchSearchExecuted(
                _definition.Name,
                mapped.Value.Hits.Count,
                mapped.Value.TotalHits,
                mapped.Value.Accuracy.ToString(),
                (int)mapped.Value.Duration.TotalMilliseconds);
        }
        else
        {
            _logger.MeilisearchEngineFault("SearchAsync", _definition.Name);
        }

        return mapped;
    }

    /// <inheritdoc />
    public async Task<Result<TDocument>> GetAsync(
        string documentId, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        if (_definition.TenantField is not null && string.IsNullOrEmpty(tenantScope.Value))
        {
            _logger.MeilisearchTenantScopeMissing(_definition.Name);
            return Result<TDocument>.Failure(SearchErrors.TenantScopeMissing(_definition.Name));
        }

        JsonElement raw;
        try
        {
            raw = await _client.Index(_definition.Name)
                .GetDocumentAsync<JsonElement>(documentId, fields: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (global::Meilisearch.MeilisearchApiError)
        {
            return Result<TDocument>.Failure(SearchErrors.DocumentNotFound(_definition.Name, documentId));
        }
        // VERIFIED against the real MeiliSearch 0.20.0 SDK (2026-07-20): Index.GetDocumentAsync<T> throws
        // a plain System.Net.Http.HttpRequestException (StatusCode = NotFound) for a missing document on
        // this SDK version's call path, not the SDK's own MeilisearchApiError — the same inconsistency
        // found on Index.GetSettingsAsync (see MeilisearchIndexProvisioner.IndexExistsAsync). Both
        // exception shapes are caught for forward-compatibility.
        catch (global::System.Net.Http.HttpRequestException ex) when (ex.StatusCode == global::System.Net.HttpStatusCode.NotFound)
        {
            return Result<TDocument>.Failure(SearchErrors.DocumentNotFound(_definition.Name, documentId));
        }

        if (_definition.TenantField is { } tenantField)
        {
            if (!TryGetPropertyCaseInsensitive(raw, tenantField, out var tenantValue)
                || tenantValue.ValueKind != JsonValueKind.String
                || !string.Equals(tenantValue.GetString(), tenantScope.Value, StringComparison.Ordinal))
            {
                return Result<TDocument>.Failure(SearchErrors.DocumentNotFound(_definition.Name, documentId));
            }
        }

        var document = raw.Deserialize<TDocument>(DocumentSerializerOptions);
        if (document is null)
        {
            _logger.MeilisearchEngineFault("GetAsync", _definition.Name);
            return Result<TDocument>.Failure(SearchErrors.EngineFault(
                SearchWellKnown.MeilisearchProviderName, "GetAsync", "Meilisearch returned a null document."));
        }

        return Result<TDocument>.Success(document);
    }

    /// <inheritdoc />
    public async Task<Result<long>> CountAsync(
        SearchFilter? filter, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        var filterResult = MeilisearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            _logger.MeilisearchTenantScopeMissing(_definition.Name);
            return Result<long>.Failure(filterResult.Error);
        }

        var query = new global::Meilisearch.SearchQuery { Page = 1, HitsPerPage = 1 };
        if (!string.IsNullOrEmpty(filterResult.Value))
        {
            query.Filter = filterResult.Value;
        }

        var searchable = await _client.Index(_definition.Name)
            .SearchAsync<JsonElement>(string.Empty, query, cancellationToken)
            .ConfigureAwait(false);

        if (searchable is global::Meilisearch.PaginatedSearchResult<JsonElement> paginated)
        {
            return Result<long>.Success(paginated.TotalHits);
        }

        return Result<long>.Failure(SearchErrors.EngineFault(
            SearchWellKnown.MeilisearchProviderName,
            "CountAsync",
            $"Expected a paginated response but received '{searchable.GetType().Name}'."));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TDocument> EnumerateAsync(
        SearchFilter? filter,
        TenantScope tenantScope,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var filterResult = MeilisearchFilterCompiler.CompileWithTenantScope(_definition, filter, tenantScope);
        if (filterResult.IsFailure)
        {
            throw new SearchStreamException(filterResult.Error);
        }

        _logger.MeilisearchDocumentWalkStarted(_definition.Name, batchSize);

        var offset = 0;
        var index = _client.Index(_definition.Name);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = new global::Meilisearch.QueryParameters.DocumentsQuery { Limit = batchSize, Offset = offset };
            if (!string.IsNullOrEmpty(filterResult.Value))
            {
                query.Filter = filterResult.Value;
            }

            global::Meilisearch.ResourceResults<IEnumerable<TDocument>> page;
            try
            {
                page = await index.GetDocumentsAsync<TDocument>(query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SearchStreamException(
                    SearchErrors.EngineFault(SearchWellKnown.MeilisearchProviderName, "EnumerateAsync", ex.Message), ex);
            }

            var items = page.Results.ToList();
            if (items.Count == 0)
            {
                yield break;
            }

            foreach (var item in items)
            {
                yield return item;
            }

            if (items.Count < batchSize)
            {
                yield break;
            }

            offset += batchSize;
        }
    }

    private static readonly JsonSerializerOptions DocumentSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static bool IsValidDocumentId(string documentId)
        => documentId.Length > 0 && documentId.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private async Task<Result<SearchWriteReceipt>> BuildReceiptAsync(
        global::Meilisearch.TaskInfo task, int affectedCount, SearchWriteConsistency consistency, CancellationToken cancellationToken)
    {
        if (consistency == SearchWriteConsistency.Searchable)
        {
            var waitResult = await WaitForTaskAsync(
                    task.TaskUid, TimeSpan.FromSeconds(_options.TaskWaitTimeoutSeconds), cancellationToken)
                .ConfigureAwait(false);
            if (waitResult.IsFailure)
            {
                return Result<SearchWriteReceipt>.Failure(waitResult.Error);
            }
        }

        return Result<SearchWriteReceipt>.Success(new SearchWriteReceipt
        {
            IndexName = _definition.Name,
            ProviderToken = task.TaskUid.ToString(CultureInfo.InvariantCulture),
            AffectedCount = affectedCount,
            RequestedConsistency = consistency,
            AcceptedAt = _clock.UtcNow,
        });
    }

    private async Task<Result> WaitForTaskAsync(int taskUid, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startedAt = _clock.UtcNow;
        global::Meilisearch.TaskResource resource;
        try
        {
            resource = await _client.WaitForTaskAsync(
                    taskUid, timeout.TotalMilliseconds, _options.TaskPollIntervalMilliseconds, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (global::Meilisearch.MeilisearchTimeoutError)
        {
            var elapsed = _clock.UtcNow - startedAt;
            _logger.MeilisearchTaskWaitTimedOut(taskUid.ToString(CultureInfo.InvariantCulture), _definition.Name, (long)elapsed.TotalMilliseconds);
            return Result.Failure(SearchErrors.WriteTimeout(_definition.Name, elapsed));
        }

        var elapsedMs = (long)(_clock.UtcNow - startedAt).TotalMilliseconds;
        _logger.MeilisearchTaskCompleted(taskUid.ToString(CultureInfo.InvariantCulture), resource.Status.ToString(), elapsedMs);

        if (resource.Status == global::Meilisearch.TaskInfoStatus.Failed)
        {
            var errorCode = resource.Error is { } error && error.TryGetValue("code", out var code)
                ? code?.ToString() ?? "unknown"
                : "unknown";
            _logger.MeilisearchTaskFailed(taskUid.ToString(CultureInfo.InvariantCulture), _definition.Name, errorCode);
            return Result.Failure(MeilisearchErrors.IndexingTaskFailed(taskUid.ToString(CultureInfo.InvariantCulture), errorCode));
        }

        return Result.Success();
    }
}
