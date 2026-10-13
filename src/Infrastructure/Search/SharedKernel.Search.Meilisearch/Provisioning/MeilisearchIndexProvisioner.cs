using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Abstractions.Telemetry;
using SharedKernel.Search.Meilisearch.Diagnostics;
using SharedKernel.Search.Meilisearch.Errors;
using SharedKernel.Search.Meilisearch.Options;

namespace SharedKernel.Search.Meilisearch.Provisioning;

/// <summary>The Meilisearch implementation of <see cref="ISearchIndexProvisioner"/> — singleton.</summary>
internal sealed class MeilisearchIndexProvisioner : ISearchIndexProvisioner
{
    private const string FingerprintDictionaryPrefix = "__sk_schema_fingerprint__:";

    /// <summary>Meilisearch's error code for creating an index that already exists.</summary>
    private const string IndexAlreadyExistsCode = "index_already_exists";

    private const string StepCreateLive = "create_live";
    private const string StepSwap = "swap";
    private const string StepDeleteStaging = "delete_staging";

    private static class DriftKinds
    {
        internal const string ProbeFailed = "probe_failed";
        internal const string EngineUnreachable = "engine_unreachable";
        internal const string NotAddressable = "not_addressable";
        internal const string NoFingerprint = "no_fingerprint";
        internal const string FingerprintMismatch = "fingerprint_mismatch";
    }

    private readonly global::Meilisearch.MeilisearchClient _client;
    private readonly MeilisearchOptions _options;
    private readonly IReadOnlyDictionary<string, SearchIndexDefinition> _registeredDefinitions;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _rankingRules;
    private readonly ILogger<MeilisearchIndexProvisioner> _logger;
    private readonly SearchTelemetry _telemetry;

    /// <summary>Initializes a new <see cref="MeilisearchIndexProvisioner"/>.</summary>
    /// <param name="client">The shared Meilisearch client.</param>
    /// <param name="options">The validated provider options.</param>
    /// <param name="registeredDefinitions">
    /// Every index definition the composition root registered, keyed by index name — the input to
    /// <see cref="VerifyRegisteredIndexesAsync"/>.
    /// </param>
    /// <param name="rankingRules">
    /// The Meilisearch-exclusive ranking rules declared per index via
    /// <c>MeilisearchSearchBuilder.WithRankingRules</c>, keyed by index name. An index absent from this
    /// map keeps the engine's default ranking-rule sequence.
    /// </param>
    /// <param name="logger">The logger.</param>
    public MeilisearchIndexProvisioner(
        global::Meilisearch.MeilisearchClient client,
        MeilisearchOptions options,
        IReadOnlyDictionary<string, SearchIndexDefinition> registeredDefinitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> rankingRules,
        ILogger<MeilisearchIndexProvisioner> logger,
        SearchTelemetry telemetry)
    {
        _client = client;
        _options = options;
        _registeredDefinitions = registeredDefinitions;
        _rankingRules = rankingRules;
        _logger = logger;
        _telemetry = telemetry;
    }

    /// <inheritdoc />
    public Task<Result> EnsureIndexAsync(SearchIndexDefinition definition, CancellationToken cancellationToken = default)
        => ClassifyAsync(SearchOperations.EnsureIndex, definition.Name, ct => EnsureIndexCoreAsync(definition, ct), cancellationToken);

    private async Task<Result> EnsureIndexCoreAsync(SearchIndexDefinition definition, CancellationToken cancellationToken)
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

        var exists = existsResult.Value;
        if (!exists)
        {
            var createTask = await _client.CreateIndexAsync(definition.Name, definition.PrimaryKeyField, cancellationToken)
                .ConfigureAwait(false);
            var (createWait, engineErrorCode) = await WaitForTaskAsync(SearchOperations.EnsureIndex, createTask.TaskUid, cancellationToken)
                .ConfigureAwait(false);
            if (createWait.IsFailure)
            {
                // Replicas starting together all see "no index" and all create it; every one but the first fails
                // with index_already_exists. The index is there, so continue as on an existing one.
                if (!string.Equals(engineErrorCode, IndexAlreadyExistsCode, StringComparison.Ordinal))
                {
                    return createWait;
                }

                exists = true;
            }
        }

        if (exists)
        {
            var currentSettings = await index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            existingSearchable = (currentSettings.SearchableAttributes ?? []).ToArray();
            existingFilterable = (currentSettings.FilterableAttributes ?? [])
                .Select(a => a.Attribute)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToArray();
            existingSortable = (currentSettings.SortableAttributes ?? []).ToArray();

            // Text analysis is NOT unioned the way the attribute-role lists above are. Meilisearch would
            // happily rewrite synonyms and stop words on a live index, but ElasticSearch cannot change an
            // index's analysis settings in place at all — so honouring a changed list here while the
            // sibling provider rejects it would reintroduce exactly the cross-provider divergence this
            // domain exists to prevent. Both providers therefore refuse, and the documented remedy is the
            // same one an incompatible field mapping already has: provision a staging index, bulk-load it,
            // then CutoverAsync.
            //
            // Creating an index and configuring it are two Meilisearch tasks, so a replica can find an index another
            // replica has just created but not yet configured: no fingerprint, no documents, empty lists. That index
            // is unclaimed, and configuring it changes nothing anyone has indexed; only a claimed (fingerprinted) or
            // populated index is held to its live text analysis.
            if (!await IsUnclaimedAndEmptyAsync(index, currentSettings, cancellationToken).ConfigureAwait(false))
            {
                var textAnalysisConflict = DetectTextAnalysisConflict(definition, currentSettings);
                if (textAnalysisConflict != Primitives.Errors.Error.None)
                {
                    return Result.Failure(textAnalysisConflict);
                }
            }
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
            Dictionary = [BuildFingerprintDictionaryEntry(definition.ComputeFingerprint())],
            Synonyms = definition.Synonyms.ToDictionary(
                entry => entry.Key,
                entry => (IEnumerable<string>)entry.Value.ToArray(),
                StringComparer.Ordinal),
            StopWords = definition.StopWords.ToArray(),
        };

        if (_rankingRules.TryGetValue(definition.Name, out var rankingRules))
        {
            settings.RankingRules = rankingRules;
        }

        var updateTask = await index.UpdateSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
        var updateWait = await WaitAsync(SearchOperations.EnsureIndex, updateTask.TaskUid, cancellationToken).ConfigureAwait(false);
        if (updateWait.IsFailure)
        {
            return updateWait;
        }

        MeilisearchLog.IndexEnsured(_logger, definition.Name, definition.Fields.Count);
        MeilisearchLog.IndexSettingsApplied(
            _logger,
            definition.Name,
            filterableAttributes.Length,
            sortableAttributes.Length,
            definition.Fields.Count(f => f.Facetable));

        if (definition.Synonyms.Count > 0 || definition.StopWords.Count > 0)
        {
            MeilisearchLog.TextAnalysisApplied(
                _logger,
                definition.Name, definition.Synonyms.Count, definition.StopWords.Count);
        }

        if (rankingRules is not null)
        {
            MeilisearchLog.RankingRulesApplied(_logger, definition.Name, rankingRules.Count);
        }

        return Result.Success();
    }

    /// <summary>
    /// Compares the live index's synonym and stop-word settings against
    /// <paramref name="definition"/>'s, returning <see cref="SearchErrors.IndexDefinitionConflict"/>
    /// when they differ and <see cref="Primitives.Errors.Error.None"/> when they match.
    /// </summary>
    private static Primitives.Errors.Error DetectTextAnalysisConflict(
        SearchIndexDefinition definition, global::Meilisearch.Settings currentSettings)
    {
        var liveStopWords = (currentSettings.StopWords ?? []).OrderBy(w => w, StringComparer.Ordinal).ToArray();
        var declaredStopWords = definition.StopWords.OrderBy(w => w, StringComparer.Ordinal).ToArray();
        if (!liveStopWords.SequenceEqual(declaredStopWords, StringComparer.Ordinal))
        {
            return SearchErrors.IndexDefinitionConflict(definition.Name, "stopWords");
        }

        var liveSynonyms = currentSettings.Synonyms ?? [];
        if (liveSynonyms.Count != definition.Synonyms.Count)
        {
            return SearchErrors.IndexDefinitionConflict(definition.Name, "synonyms");
        }

        foreach (var (term, declaredReplacements) in definition.Synonyms)
        {
            if (!liveSynonyms.TryGetValue(term, out var liveReplacements))
            {
                return SearchErrors.IndexDefinitionConflict(definition.Name, $"synonyms.{term}");
            }

            var live = liveReplacements.OrderBy(v => v, StringComparer.Ordinal).ToArray();
            var declared = declaredReplacements.OrderBy(v => v, StringComparer.Ordinal).ToArray();
            if (!live.SequenceEqual(declared, StringComparer.Ordinal))
            {
                return SearchErrors.IndexDefinitionConflict(definition.Name, $"synonyms.{term}");
            }
        }

        return Primitives.Errors.Error.None;
    }

    /// <inheritdoc />
    public Task<Result<bool>> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default)
        => ClassifyAsync(SearchOperations.IndexExists, indexName, ct => IndexExistsCoreAsync(indexName, ct), cancellationToken);

    private async Task<Result<bool>> IndexExistsCoreAsync(string indexName, CancellationToken cancellationToken)
    {
        // VERIFIED against the real MeiliSearch 0.20.0 SDK (2026-07-20): MeilisearchClient.GetIndexAsync
        // does NOT throw for a non-existent index — it silently returns a synthetic Index object built
        // from the requested uid with no network round trip ever performed, which made the previous
        // GetIndexAsync-based existence check ALWAYS report true regardless of whether the index
        // actually existed. Index(uid).GetSettingsAsync() DOES perform a real, authenticated request and
        // correctly throws — as a plain System.Net.Http.HttpRequestException with StatusCode populated,
        // not the SDK's own MeilisearchApiError — for both a missing index (404) and a mis-scoped key
        // (403). Both exception shapes are caught here for forward-compatibility across SDK versions.
        try
        {
            await _client.Index(indexName).GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
        catch (global::Meilisearch.MeilisearchApiError ex) when (IsNotFound(ex))
        {
            return Result<bool>.Success(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Result<bool>.Success(false);
        }
    }

    /// <inheritdoc />
    public Task<Result> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default)
        => ClassifyAsync(SearchOperations.DeleteIndex, indexName, ct => DeleteIndexCoreAsync(indexName, ct), cancellationToken);

    private async Task<Result> DeleteIndexCoreAsync(string indexName, CancellationToken cancellationToken)
    {
        var task = await _client.DeleteIndexAsync(indexName, cancellationToken).ConfigureAwait(false);
        return await WaitAsync(SearchOperations.DeleteIndex, task.TaskUid, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> CutoverAsync(IndexCutoverRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _telemetry.Start(SearchOperations.Cutover, request.LiveIndexName);
        try
        {
            var result = await CutoverCoreAsync(request, cancellationToken).ConfigureAwait(false);
            scope.Complete(result);
            return result;
        }
        catch (Exception ex)
        {
            scope.Faulted(ex);
            throw;
        }
    }

    private async Task<Result> CutoverCoreAsync(IndexCutoverRequest request, CancellationToken cancellationToken)
    {
        // VERIFIED against the real MeiliSearch 0.20.0 SDK/engine (2026-07-20): SwapIndexesAsync requires
        // BOTH index names to already exist — swapping against a live index name that has never been
        // created fails with a real, engine-side "index_not_found" task error. The documented consumer
        // rebuild flow (EnsureIndexAsync(staging) -> IndexManyAsync -> CutoverAsync) never separately
        // creates the live index, so a service's very first-ever cutover would otherwise always fail.
        // An empty placeholder is therefore created transparently here when the live index is absent —
        // its own (empty) settings/primaryKey are irrelevant because the swap overwrites them with
        // staging's, per Meilisearch's own "swaps documents, settings AND task history" semantics.
        var liveExistsResult = await IndexExistsAsync(request.LiveIndexName, cancellationToken).ConfigureAwait(false);
        if (liveExistsResult.IsFailure)
        {
            return Result.Failure(liveExistsResult.Error);
        }

        if (!liveExistsResult.Value)
        {
            try
            {
                var createLiveTask = await _client.CreateIndexAsync(request.LiveIndexName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var createLiveWait = await WaitAsync(SearchOperations.Cutover, createLiveTask.TaskUid, cancellationToken).ConfigureAwait(false);
                if (createLiveWait.IsFailure)
                {
                    return Result.Failure(CutoverFault(createLiveWait.Error, request, StepCreateLive, exception: null));
                }
            }
            catch (Exception ex) when (MeilisearchFaultMapper.TryMap(ex, request.LiveIndexName, SearchOperations.Cutover, cancellationToken, out var nested))
            {
                return Result.Failure(CutoverFault(nested, request, StepCreateLive, ex));
            }
        }

        try
        {
            var task = await _client
                .SwapIndexesAsync(
                    [new global::Meilisearch.IndexSwap(request.StagingIndexName, request.LiveIndexName, rename: false)],
                    cancellationToken)
                .ConfigureAwait(false);
            var waitResult = await WaitAsync(SearchOperations.Cutover, task.TaskUid, cancellationToken).ConfigureAwait(false);
            if (waitResult.IsFailure)
            {
                return Result.Failure(CutoverFault(waitResult.Error, request, StepSwap, exception: null));
            }
        }
        catch (Exception ex) when (MeilisearchFaultMapper.TryMap(ex, request.LiveIndexName, SearchOperations.Cutover, cancellationToken, out var nested))
        {
            return Result.Failure(CutoverFault(nested, request, StepSwap, ex));
        }

        MeilisearchLog.IndexesSwapped(_logger, request.StagingIndexName, request.LiveIndexName);

        if (!request.DeleteStagingAfterCutover)
        {
            // After a Meilisearch swap the staging name still holds the OLD (pre-cutover) data.
            MeilisearchLog.StagingIndexRetained(_logger, request.StagingIndexName);
            return Result.Success();
        }

        var deleteResult = await DeleteIndexAsync(request.StagingIndexName, cancellationToken).ConfigureAwait(false);
        return deleteResult.IsFailure
            ? Result.Failure(CutoverFault(deleteResult.Error, request, StepDeleteStaging, exception: null))
            : deleteResult;
    }

    /// <summary>
    /// A cutover step failed. An outage, timeout or credential fault keeps its own classification (so an
    /// unreachable engine is <c>search.unreachable</c>, not <c>search.cutover_failed</c>); anything else is
    /// <c>search.cutover_failed</c> naming the failed step and the nested kernel error code, and nothing the
    /// engine or the exception said. The exception, when there is one, goes to the log.
    /// </summary>
    private Primitives.Errors.Error CutoverFault(
        Primitives.Errors.Error nested, IndexCutoverRequest request, string step, Exception? exception)
    {
        MeilisearchLog.CutoverStepFailed(_logger, request.StagingIndexName, request.LiveIndexName, step, exception);
        return nested.Type is Primitives.Errors.ErrorType.Unavailable or Primitives.Errors.ErrorType.Timeout
            || nested.Code == SearchErrorCodes.EngineUnauthorized
            ? nested
            : SearchErrors.CutoverFailed(
                request.StagingIndexName, request.LiveIndexName, $"failed step '{step}' ({nested.Code}).");
    }

    /// <summary>
    /// Runs <paramref name="body"/> and converts a recognised engine or transport fault into a failed
    /// <see cref="Result"/>; an unrecognised exception propagates untouched.
    /// </summary>
    private async Task<Result> ClassifyAsync(
        string operation, string indexName, Func<CancellationToken, Task<Result>> body, CancellationToken cancellationToken)
    {
        using var scope = _telemetry.Start(operation, indexName);
        try
        {
            var result = await body(cancellationToken).ConfigureAwait(false);
            scope.Complete(result);
            return result;
        }
        catch (Exception ex) when (MeilisearchFaultMapper.TryMap(ex, indexName, operation, cancellationToken, out var error))
        {
            MeilisearchLog.OperationFaulted(_logger, operation, indexName, error.Code, ex);
            scope.Failed(error);
            return Result.Failure(error);
        }
        catch (Exception ex)
        {
            scope.Faulted(ex);
            throw;
        }
    }

    private async Task<Result<T>> ClassifyAsync<T>(
        string operation, string indexName, Func<CancellationToken, Task<Result<T>>> body, CancellationToken cancellationToken)
    {
        using var scope = _telemetry.Start(operation, indexName);
        try
        {
            var result = await body(cancellationToken).ConfigureAwait(false);
            scope.Complete(result);
            return result;
        }
        catch (Exception ex) when (MeilisearchFaultMapper.TryMap(ex, indexName, operation, cancellationToken, out var error))
        {
            MeilisearchLog.OperationFaulted(_logger, operation, indexName, error.Code, ex);
            scope.Failed(error);
            return Result<T>.Failure(error);
        }
        catch (Exception ex)
        {
            scope.Faulted(ex);
            throw;
        }
    }


    /// <summary>Measures the index named <paramref name="indexName"/> for its readiness probe (<see cref="SearchIndexReadinessProbe"/>).</summary>
    public async Task<Result<SearchIndexHealth>> ProbeAsync(string indexName, CancellationToken cancellationToken = default)
    {
        Exception? firstFault = null;
        var stopwatch = Stopwatch.StartNew();

        bool reachable;
        try
        {
            reachable = await _client.IsHealthyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            reachable = false;
            firstFault ??= ex;
        }

        var indexAddressable = true;
        try
        {
            // Meilisearch keys are per-index, so this call — made with this service's own configured
            // key — is what catches a mis-scoped key that /health cannot see. Uses
            // Index(uid).GetSettingsAsync(), never GetIndexAsync — VERIFIED against the real SDK that
            // GetIndexAsync never throws (see the identical note on IndexExistsAsync above), which would
            // make this probe report IndexAddressable = true unconditionally.
            await _client.Index(indexName).GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            indexAddressable = false;
            firstFault ??= ex;
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
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                searchable = false;
                firstFault ??= ex;
            }

            try
            {
                var stats = await index.GetStatsAsync(cancellationToken).ConfigureAwait(false);
                documentCount = stats.NumberOfDocuments;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                documentCount = 0;
                firstFault ??= ex;
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
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                pendingWriteCount = null;
                firstFault ??= ex;
            }

            try
            {
                var currentSettings = await index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
                schemaFingerprint = ExtractFingerprint(currentSettings.Dictionary);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                schemaFingerprint = null;
                firstFault ??= ex;
            }
        }

        var engineVersion = string.Empty;
        try
        {
            var version = await _client.GetVersionAsync(cancellationToken).ConfigureAwait(false);
            engineVersion = version.Version;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            engineVersion = string.Empty;
            firstFault ??= ex;
        }

        // A cancellation that surfaced as a swallowed measurement fault is still the caller's: propagate it.
        cancellationToken.ThrowIfCancellationRequested();

        stopwatch.Stop();

        if (!reachable || !indexAddressable || !searchable)
        {
            MeilisearchLog.ProbeDegraded(_logger, indexName, reachable, indexAddressable, searchable, firstFault);
        }

        if (pendingWriteCount is { } pendingCount && pendingCount > 0)
        {
            MeilisearchLog.WriteBacklogDeep(_logger, pendingCount);
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

    private async Task<Result> WaitAsync(string operation, int taskUid, CancellationToken cancellationToken) =>
        (await WaitForTaskAsync(operation, taskUid, cancellationToken).ConfigureAwait(false)).Result;

    /// <summary>Waits for a task; on failure also returns Meilisearch's own error code (e.g. <c>index_already_exists</c>).</summary>
    private async Task<(Result Result, string? EngineErrorCode)> WaitForTaskAsync(string operation, int taskUid, CancellationToken cancellationToken)
    {
        try
        {
            var resource = await _client
                .WaitForTaskAsync(
                    taskUid, _options.TaskWaitTimeoutSeconds * 1000.0, _options.TaskPollIntervalMilliseconds, cancellationToken)
                .ConfigureAwait(false);

            if (resource.Status == global::Meilisearch.TaskInfoStatus.Failed)
            {
                var code = MeilisearchFaultMapper.BoundedEngineCode(
                    resource.Error is { } error && error.TryGetValue("code", out var codeValue) ? codeValue?.ToString() : null)
                    ?? "unknown";
                return (Result.Failure(MeilisearchErrors.IndexingTaskFailed(taskUid.ToString(CultureInfo.InvariantCulture), code)), code);
            }

            return (Result.Success(), null);
        }
        catch (global::Meilisearch.MeilisearchTimeoutError)
        {
            return (Result.Failure(SearchErrors.Timeout(operation)), null);
        }
    }

    /// <summary>
    /// An index no provisioner has configured yet (no schema fingerprint) and that holds no documents: one another
    /// replica created a moment ago, or an empty index created by hand.
    /// </summary>
    private static async Task<bool> IsUnclaimedAndEmptyAsync(
        global::Meilisearch.Index index, global::Meilisearch.Settings currentSettings, CancellationToken cancellationToken)
    {
        if (ExtractFingerprint(currentSettings.Dictionary) is not null)
        {
            return false;
        }

        var stats = await index.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        return stats.NumberOfDocuments == 0;
    }

    /// <inheritdoc />
    public async Task<Result> VerifyRegisteredIndexesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _telemetry.Start(SearchOperations.VerifyIndexes, string.Join(',', _registeredDefinitions.Keys));
        try
        {
            var result = await VerifyRegisteredIndexesCoreAsync(cancellationToken).ConfigureAwait(false);
            scope.Complete(result);
            return result;
        }
        catch (Exception ex)
        {
            scope.Faulted(ex);
            throw;
        }
    }

    private async Task<Result> VerifyRegisteredIndexesCoreAsync(CancellationToken cancellationToken)
    {
        if (_registeredDefinitions.Count == 0)
        {
            return Result.Success();
        }

        var drifted = new List<(string IndexName, string Kind, string Detail)>();

        foreach (var (indexName, definition) in _registeredDefinitions)
        {
            var probeResult = await ProbeAsync(indexName, cancellationToken).ConfigureAwait(false);
            if (probeResult.IsFailure)
            {
                drifted.Add((indexName, DriftKinds.ProbeFailed, $"{indexName}: probe failed ({probeResult.Error.Code})"));
                continue;
            }

            var health = probeResult.Value;
            if (!health.Reachable)
            {
                drifted.Add((indexName, DriftKinds.EngineUnreachable, $"{indexName}: engine unreachable"));
                continue;
            }

            if (!health.IndexAddressable)
            {
                drifted.Add((indexName, DriftKinds.NotAddressable, $"{indexName}: not addressable with this service's API key"));
                continue;
            }

            var expectedFingerprint = definition.ComputeFingerprint();
            if (health.SchemaFingerprint is null)
            {
                drifted.Add((indexName, DriftKinds.NoFingerprint, $"{indexName}: no schema fingerprint recorded, so the index was never provisioned by EnsureIndexAsync"));
                continue;
            }

            if (!string.Equals(health.SchemaFingerprint, expectedFingerprint, StringComparison.Ordinal))
            {
                drifted.Add((
                    indexName,
                    DriftKinds.FingerprintMismatch,
                    $"{indexName}: schema fingerprint '{health.SchemaFingerprint}' does not match the registered definition's '{expectedFingerprint}'"));
            }
        }

        if (drifted.Count == 0)
        {
            foreach (var indexName in _registeredDefinitions.Keys)
            {
                MeilisearchLog.IndexSettingsVerified(_logger, indexName);
            }

            return Result.Success();
        }

        foreach (var (driftedIndex, kind, _) in drifted)
        {
            MeilisearchLog.IndexSettingsDrifted(_logger, driftedIndex, kind);
        }

        var detail = string.Join("; ", drifted.Select(d => d.Detail));
        return Result.Failure(SearchErrors.ProbeFailed(
            string.Join(", ", _registeredDefinitions.Keys),
            $"{drifted.Count} of {_registeredDefinitions.Count} registered index(es) did not match: {detail}"));
    }
}
