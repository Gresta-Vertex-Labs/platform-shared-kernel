using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Errors;
using SharedKernel.Search.Meilisearch.Logging;
using SharedKernel.Search.Meilisearch.Options;

namespace SharedKernel.Search.Meilisearch.Provisioning;

/// <summary>The Meilisearch implementation of <see cref="ISearchIndexProvisioner"/> — singleton.</summary>
internal sealed class MeilisearchIndexProvisioner : ISearchIndexProvisioner
{
    private const string FingerprintDictionaryPrefix = "__sk_schema_fingerprint__:";

    private readonly global::Meilisearch.MeilisearchClient _client;
    private readonly MeilisearchOptions _options;
    private readonly ILogger<MeilisearchIndexProvisioner> _logger;

    /// <summary>Initializes a new <see cref="MeilisearchIndexProvisioner"/>.</summary>
    public MeilisearchIndexProvisioner(
        global::Meilisearch.MeilisearchClient client,
        MeilisearchOptions options,
        ILogger<MeilisearchIndexProvisioner> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> EnsureIndexAsync(SearchIndexDefinition definition, CancellationToken cancellationToken = default)
    {
        var existsResult = await IndexExistsAsync(definition.Name, cancellationToken).ConfigureAwait(false);
        if (existsResult.IsFailure)
        {
            return Result.Failure(existsResult.Error);
        }

        var index = _client.Index(definition.Name);
        IReadOnlyList<string> existingSearchable = [];
        IReadOnlyList<string> existingFilterable = [];
        IReadOnlyList<string> existingSortable = [];

        if (!existsResult.Value)
        {
            var createTask = await _client.CreateIndexAsync(definition.Name, definition.PrimaryKeyField, cancellationToken)
                .ConfigureAwait(false);
            var createWait = await WaitAsync(createTask.TaskUid, cancellationToken).ConfigureAwait(false);
            if (createWait.IsFailure)
            {
                return createWait;
            }
        }
        else
        {
            var currentSettings = await index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            existingSearchable = (currentSettings.SearchableAttributes ?? []).ToArray();
            existingFilterable = (currentSettings.FilterableAttributes ?? [])
                .Select(a => a.Attribute)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToArray();
            existingSortable = (currentSettings.SortableAttributes ?? []).ToArray();
        }

        // Additive-only: the applied settings are the UNION of whatever is already live and what this
        // definition declares — EnsureIndexAsync never drops a field's role.
        var searchableAttributes = definition.Fields
            .Where(f => f.Searchable)
            .Select(f => f.Name)
            .Concat(existingSearchable)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var filterableNames = definition.Fields.Where(f => f.Filterable || f.Facetable).Select(f => f.Name);
        if (definition.TenantField is { } tenantField)
        {
            filterableNames = filterableNames.Append(tenantField);
        }

        var filterableAttributeNames = filterableNames
            .Concat(existingFilterable)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var filterableAttributes = filterableAttributeNames
            .Select(name => (global::Meilisearch.FilterableAttribute)name)
            .ToArray();

        var sortableAttributes = definition.Fields
            .Where(f => f.Sortable)
            .Select(f => f.Name)
            .Concat(existingSortable)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var settings = new global::Meilisearch.Settings
        {
            SearchableAttributes = searchableAttributes,
            FilterableAttributes = filterableAttributes,
            SortableAttributes = sortableAttributes,
            Pagination = new global::Meilisearch.Pagination { MaxTotalHits = definition.MaxTotalHits },
            Faceting = new global::Meilisearch.Faceting { MaxValuesPerFacet = definition.MaxFacetValues },
            Dictionary = [BuildFingerprintDictionaryEntry(definition.Fingerprint)],
        };

        var updateTask = await index.UpdateSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
        var updateWait = await WaitAsync(updateTask.TaskUid, cancellationToken).ConfigureAwait(false);
        if (updateWait.IsFailure)
        {
            return updateWait;
        }

        _logger.MeilisearchIndexEnsured(definition.Name, definition.Fields.Count);
        _logger.MeilisearchIndexSettingsApplied(
            definition.Name,
            filterableAttributes.Length,
            sortableAttributes.Length,
            definition.Fields.Count(f => f.Facetable));

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<bool>> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetIndexAsync(indexName, cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
        catch (global::Meilisearch.MeilisearchApiError ex) when (IsNotFound(ex))
        {
            return Result<bool>.Success(false);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default)
    {
        var task = await _client.DeleteIndexAsync(indexName, cancellationToken).ConfigureAwait(false);
        return await WaitAsync(task.TaskUid, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> CutoverAsync(IndexCutoverRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var task = await _client
                .SwapIndexesAsync(
                    [new global::Meilisearch.IndexSwap(request.StagingIndexName, request.LiveIndexName, rename: false)],
                    cancellationToken)
                .ConfigureAwait(false);
            var waitResult = await WaitAsync(task.TaskUid, cancellationToken).ConfigureAwait(false);
            if (waitResult.IsFailure)
            {
                return Result.Failure(SearchErrors.CutoverFailed(
                    request.StagingIndexName, request.LiveIndexName, waitResult.Error.Message));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure(SearchErrors.CutoverFailed(request.StagingIndexName, request.LiveIndexName, ex.Message));
        }

        _logger.MeilisearchIndexesSwapped(request.StagingIndexName, request.LiveIndexName);

        if (!request.DeleteStagingAfterCutover)
        {
            // After a Meilisearch swap the staging name still holds the OLD (pre-cutover) data.
            _logger.MeilisearchStagingIndexRetained(request.StagingIndexName);
            return Result.Success();
        }

        return await DeleteIndexAsync(request.StagingIndexName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<SearchIndexHealth>> ProbeAsync(string indexName, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        bool reachable;
        try
        {
            reachable = await _client.IsHealthyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            reachable = false;
        }

        var indexAddressable = true;
        try
        {
            // Meilisearch keys are per-index, so this call — made with this service's own configured
            // key — is what catches a mis-scoped key that /health cannot see.
            await _client.GetIndexAsync(indexName, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            indexAddressable = false;
        }

        var searchable = false;
        long documentCount = 0;
        long? pendingWriteCount = null;
        string? schemaFingerprint = null;

        if (indexAddressable)
        {
            var index = _client.Index(indexName);

            try
            {
                var probeQuery = new global::Meilisearch.SearchQuery { Limit = 0 };
                await index.SearchAsync<JsonElement>(string.Empty, probeQuery, cancellationToken).ConfigureAwait(false);
                searchable = true;
            }
            catch
            {
                searchable = false;
            }

            try
            {
                var stats = await index.GetStatsAsync(cancellationToken).ConfigureAwait(false);
                documentCount = stats.NumberOfDocuments;
            }
            catch
            {
                documentCount = 0;
            }

            try
            {
                var tasksQuery = new global::Meilisearch.QueryParameters.TasksQuery
                {
                    IndexUids = [indexName],
                    Statuses = [global::Meilisearch.TaskInfoStatus.Enqueued],
                };
                var pending = await index.GetTasksAsync(tasksQuery, cancellationToken).ConfigureAwait(false);
                pendingWriteCount = pending.Total ?? pending.Results.Count();
            }
            catch
            {
                pendingWriteCount = null;
            }

            try
            {
                var currentSettings = await index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
                schemaFingerprint = ExtractFingerprint(currentSettings.Dictionary);
            }
            catch
            {
                schemaFingerprint = null;
            }
        }

        var engineVersion = string.Empty;
        try
        {
            var version = await _client.GetVersionAsync(cancellationToken).ConfigureAwait(false);
            engineVersion = version.Version;
        }
        catch
        {
            engineVersion = string.Empty;
        }

        stopwatch.Stop();

        if (!reachable || !indexAddressable || !searchable)
        {
            _logger.MeilisearchProbeDegraded(indexName, reachable, indexAddressable, searchable);
        }

        if (pendingWriteCount is { } pendingCount && pendingCount > 0)
        {
            _logger.MeilisearchWriteBacklogDeep(pendingCount);
        }

        return Result<SearchIndexHealth>.Success(new SearchIndexHealth
        {
            Reachable = reachable,
            IndexAddressable = indexAddressable,
            Searchable = searchable,
            DocumentCount = documentCount,
            PendingWriteCount = pendingWriteCount,
            EngineVersion = engineVersion,
            SchemaFingerprint = schemaFingerprint,
            Latency = stopwatch.Elapsed,
        });
    }

    private static bool IsNotFound(global::Meilisearch.MeilisearchApiError error)
        => string.Equals(error.Code, "index_not_found", StringComparison.Ordinal);

    private static string BuildFingerprintDictionaryEntry(string fingerprint) => FingerprintDictionaryPrefix + fingerprint;

    private static string? ExtractFingerprint(IEnumerable<string>? dictionary)
    {
        var entry = dictionary?.FirstOrDefault(e => e.StartsWith(FingerprintDictionaryPrefix, StringComparison.Ordinal));
        return entry?[FingerprintDictionaryPrefix.Length..];
    }

    private async Task<Result> WaitAsync(int taskUid, CancellationToken cancellationToken)
    {
        try
        {
            var resource = await _client
                .WaitForTaskAsync(
                    taskUid, _options.TaskWaitTimeoutSeconds * 1000.0, _options.TaskPollIntervalMilliseconds, cancellationToken)
                .ConfigureAwait(false);

            if (resource.Status == global::Meilisearch.TaskInfoStatus.Failed)
            {
                var code = resource.Error is { } error && error.TryGetValue("code", out var codeValue)
                    ? codeValue?.ToString() ?? "unknown"
                    : "unknown";
                return Result.Failure(MeilisearchErrors.IndexingTaskFailed(taskUid.ToString(CultureInfo.InvariantCulture), code));
            }

            return Result.Success();
        }
        catch (global::Meilisearch.MeilisearchTimeoutError)
        {
            return Result.Failure(SearchErrors.Timeout(
                "Provisioning", TimeSpan.FromSeconds(_options.TaskWaitTimeoutSeconds)));
        }
    }
}
