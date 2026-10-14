using Microsoft.Extensions.Logging;

namespace SharedKernel.Search.Meilisearch.Diagnostics;

/// <summary>
/// Log statements of <c>SharedKernel.Search.Meilisearch</c>, EventIds 9100-9199 (see
/// <see cref="MeilisearchLoggingEventIds"/>). Query text, filter values, document ids, tenant ids, endpoints and engine
/// error text are never logged; engine detail reaches the log only through the exception parameter.
/// </summary>
internal static partial class MeilisearchLog
{
    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.ClientConfigured,
        Level = LogLevel.Information,
        Message = "The client is configured with {IndexCount} registered indexes.")]
    public static partial void ClientConfigured(ILogger logger, int indexCount);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.IndexEnsured,
        Level = LogLevel.Information,
        Message = "Index '{IndexName}' was ensured with {FieldCount} declared fields.")]
    public static partial void IndexEnsured(ILogger logger, string indexName, int fieldCount);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.IndexSettingsApplied,
        Level = LogLevel.Information,
        Message = "Settings of index '{IndexName}' were applied with {FilterableCount} filterable, {SortableCount} sortable and {FacetableCount} facetable attributes.")]
    public static partial void IndexSettingsApplied(
        ILogger logger, string indexName, int filterableCount, int sortableCount, int facetableCount);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.DocumentsEnqueued,
        Level = LogLevel.Debug,
        Message = "Enqueued {DocumentCount} documents for index '{IndexName}' as task '{ProviderToken}'.")]
    public static partial void DocumentsEnqueued(ILogger logger, string indexName, int documentCount, string providerToken);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.DocumentsDeleted,
        Level = LogLevel.Debug,
        Message = "Enqueued the deletion of {DocumentCount} documents from index '{IndexName}' as task '{ProviderToken}'.")]
    public static partial void DocumentsDeleted(ILogger logger, string indexName, int documentCount, string providerToken);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.TaskCompleted,
        Level = LogLevel.Debug,
        Message = "Task '{ProviderToken}' completed with status '{TaskStatus}' after {ElapsedMilliseconds} ms.")]
    public static partial void TaskCompleted(ILogger logger, string providerToken, string taskStatus, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.TaskWaitTimedOut,
        Level = LogLevel.Warning,
        Message = "Waiting for task '{ProviderToken}' on index '{IndexName}' timed out after {ElapsedMilliseconds} ms.")]
    public static partial void TaskWaitTimedOut(ILogger logger, string providerToken, string indexName, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.TaskFailed,
        Level = LogLevel.Error,
        Message = "Task '{ProviderToken}' on index '{IndexName}' failed with engine error code '{EngineErrorCode}'.")]
    public static partial void TaskFailed(ILogger logger, string providerToken, string indexName, string engineErrorCode);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.BulkPartialFailure,
        Level = LogLevel.Warning,
        Message = "Bulk operation on index '{IndexName}' partially failed for {FailedCount} of {TotalCount} documents.")]
    public static partial void BulkPartialFailure(ILogger logger, string indexName, int failedCount, int totalCount);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.SearchExecuted,
        Level = LogLevel.Debug,
        Message = "Search on index '{IndexName}' returned {HitCount} hits of {TotalHits} ({Accuracy}) in {ElapsedMilliseconds} ms.")]
    public static partial void SearchExecuted(
        ILogger logger, string indexName, int hitCount, long totalHits, string accuracy, int elapsedMilliseconds);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.RequestRejected,
        Level = LogLevel.Warning,
        Message = "Search request against index '{IndexName}' was rejected before any I/O with error type '{ErrorType}' and error code '{ErrorCode}'.")]
    public static partial void RequestRejected(ILogger logger, string indexName, string errorType, string errorCode);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.TenantScopeMissing,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' declares a tenant field but the caller supplied the global tenant scope.")]
    public static partial void TenantScopeMissing(ILogger logger, string indexName);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.IndexesSwapped,
        Level = LogLevel.Information,
        Message = "Staging index '{StagingIndexName}' is now live as index '{LiveIndexName}'.")]
    public static partial void IndexesSwapped(ILogger logger, string stagingIndexName, string liveIndexName);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.StagingIndexRetained,
        Level = LogLevel.Warning,
        Message = "Staging index '{StagingIndexName}' was retained after the cutover and still holds the pre-cutover data.")]
    public static partial void StagingIndexRetained(ILogger logger, string stagingIndexName);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.TenantTokenIssued,
        Level = LogLevel.Debug,
        Message = "Issued a tenant search token scoped to {IndexCount} indexes with a lifetime of {TtlMinutes} minutes.")]
    public static partial void TenantTokenIssued(ILogger logger, int indexCount, int ttlMinutes);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.RawClientAccessEnabled,
        Level = LogLevel.Warning,
        Message = "Raw client access is enabled, so the raw client bypasses tenant scoping.")]
    public static partial void RawClientAccessEnabled(ILogger logger);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.ProbeDegraded,
        Level = LogLevel.Warning,
        Message = "Probe of index '{IndexName}' is degraded with reachable {Reachable}, addressable {IndexAddressable} and searchable {Searchable}.")]
    public static partial void ProbeDegraded(
        ILogger logger, string indexName, bool reachable, bool indexAddressable, bool searchable, Exception? exception);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.WriteBacklogDeep,
        Level = LogLevel.Warning,
        Message = "The write backlog is deep with {PendingWriteCount} pending tasks.")]
    public static partial void WriteBacklogDeep(ILogger logger, long pendingWriteCount);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.SchemaFingerprintMismatch,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' has schema fingerprint '{Actual}' where '{Expected}' was expected.")]
    public static partial void SchemaFingerprintMismatch(ILogger logger, string indexName, string expected, string actual);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.EngineFault,
        Level = LogLevel.Warning,
        Message = "Operation '{Operation}' on index '{IndexName}' faulted.")]
    public static partial void EngineFault(ILogger logger, string operation, string indexName, Exception? exception);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.DocumentWalkStarted,
        Level = LogLevel.Debug,
        Message = "Started a document walk over index '{IndexName}' with batch size {BatchSize}.")]
    public static partial void DocumentWalkStarted(ILogger logger, string indexName, int batchSize);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.BulkThrottled,
        Level = LogLevel.Debug,
        Message = "Bulk operation on index '{IndexName}' is waiting {DelayMilliseconds} ms before the next batch.")]
    public static partial void BulkThrottled(ILogger logger, string indexName, double delayMilliseconds);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.CountReachedCeiling,
        Level = LogLevel.Debug,
        Message = "Count on index '{IndexName}' reached the maxTotalHits ceiling of {Ceiling}, so it is reported as a lower bound.")]
    public static partial void CountReachedCeiling(ILogger logger, string indexName, int ceiling);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.OperationFaulted,
        Level = LogLevel.Warning,
        Message = "Operation '{Operation}' on index '{IndexName}' faulted and was mapped to error code '{ErrorCode}'.")]
    public static partial void OperationFaulted(
        ILogger logger, string operation, string indexName, string errorCode, Exception? exception);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.TextAnalysisApplied,
        Level = LogLevel.Information,
        Message = "Text analysis of index '{IndexName}' was applied with {SynonymCount} synonym terms and {StopWordCount} stop words.")]
    public static partial void TextAnalysisApplied(ILogger logger, string indexName, int synonymCount, int stopWordCount);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.IndexSettingsDrifted,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' drifted from its registered definition at startup with drift kind '{DriftKind}'.")]
    public static partial void IndexSettingsDrifted(ILogger logger, string indexName, string driftKind);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.IndexSettingsVerified,
        Level = LogLevel.Information,
        Message = "Settings of index '{IndexName}' were verified against the registered definition at startup.")]
    public static partial void IndexSettingsVerified(ILogger logger, string indexName);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.RankingRulesApplied,
        Level = LogLevel.Information,
        Message = "Ranking rules of index '{IndexName}' were applied with {RankingRuleCount} rules.")]
    public static partial void RankingRulesApplied(ILogger logger, string indexName, int rankingRuleCount);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.TenantTokenIssuanceFailed,
        Level = LogLevel.Warning,
        Message = "Issuing a tenant search token failed.")]
    public static partial void TenantTokenIssuanceFailed(ILogger logger, Exception? exception);

    [LoggerMessage(
        EventId = MeilisearchLoggingEventIds.CutoverStepFailed,
        Level = LogLevel.Warning,
        Message = "Cutover from staging index '{StagingIndexName}' to live index '{LiveIndexName}' failed at step '{Step}'.")]
    public static partial void CutoverStepFailed(
        ILogger logger, string stagingIndexName, string liveIndexName, string step, Exception? exception);
}
