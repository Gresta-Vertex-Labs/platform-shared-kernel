using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Search.Meilisearch.Logging;

/// <summary>
/// The <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.Search.Meilisearch</c>,
/// reserved <c>EventId</c> sub-block 9100-9199 (<see cref="LoggingEventIdRanges.Search"/> + 100..199).
/// </summary>
internal static partial class MeilisearchLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 100,
        Level = LogLevel.Information,
        Message = "Meilisearch client configured for {Url} with {IndexCount} registered index(es).")]
    public static partial void MeilisearchClientConfigured(this ILogger logger, string url, int indexCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 101,
        Level = LogLevel.Information,
        Message = "Meilisearch index '{IndexName}' ensured with {FieldCount} declared field(s).")]
    public static partial void MeilisearchIndexEnsured(this ILogger logger, string indexName, int fieldCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 102,
        Level = LogLevel.Information,
        Message = "Meilisearch index '{IndexName}' settings applied: {FilterableCount} filterable, " +
                  "{SortableCount} sortable, {FacetableCount} facetable attribute(s).")]
    public static partial void MeilisearchIndexSettingsApplied(
        this ILogger logger, string indexName, int filterableCount, int sortableCount, int facetableCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 103,
        Level = LogLevel.Debug,
        Message = "Enqueued {DocumentCount} document(s) for index '{IndexName}' as task '{ProviderToken}'.")]
    public static partial void MeilisearchDocumentsEnqueued(
        this ILogger logger, string indexName, int documentCount, string providerToken);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 104,
        Level = LogLevel.Debug,
        Message = "Enqueued deletion of {DocumentCount} document(s) from index '{IndexName}' as task '{ProviderToken}'.")]
    public static partial void MeilisearchDocumentsDeleted(
        this ILogger logger, string indexName, int documentCount, string providerToken);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 105,
        Level = LogLevel.Debug,
        Message = "Meilisearch task '{ProviderToken}' completed with status {TaskStatus} after {ElapsedMs}ms.")]
    public static partial void MeilisearchTaskCompleted(
        this ILogger logger, string providerToken, string taskStatus, long elapsedMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 106,
        Level = LogLevel.Warning,
        Message = "Waiting for Meilisearch task '{ProviderToken}' on index '{IndexName}' timed out after {ElapsedMs}ms.")]
    public static partial void MeilisearchTaskWaitTimedOut(
        this ILogger logger, string providerToken, string indexName, long elapsedMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 107,
        Level = LogLevel.Error,
        Message = "Meilisearch task '{ProviderToken}' on index '{IndexName}' failed with engine error code '{EngineErrorCode}'.")]
    public static partial void MeilisearchTaskFailed(
        this ILogger logger, string providerToken, string indexName, string engineErrorCode);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 108,
        Level = LogLevel.Warning,
        Message = "Bulk operation on index '{IndexName}' partially failed: {FailedCount} of {TotalCount} document(s).")]
    public static partial void MeilisearchBulkPartialFailure(
        this ILogger logger, string indexName, int failedCount, int totalCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 109,
        Level = LogLevel.Debug,
        Message = "Search on index '{IndexName}' returned {HitCount} hit(s) of {TotalHits} ({Accuracy}) in {ProcessingTimeMs}ms.")]
    public static partial void MeilisearchSearchExecuted(
        this ILogger logger, string indexName, int hitCount, long totalHits, string accuracy, int processingTimeMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 110,
        Level = LogLevel.Warning,
        Message = "Search request against index '{IndexName}' rejected before any I/O: {Reason}")]
    public static partial void MeilisearchRequestRejected(this ILogger logger, string indexName, string reason);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 111,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' declares a TenantField but the caller supplied TenantScope.None.")]
    public static partial void MeilisearchTenantScopeMissing(this ILogger logger, string indexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 112,
        Level = LogLevel.Information,
        Message = "Meilisearch indexes swapped: staging '{StagingIndexName}' is now live as '{LiveIndexName}'.")]
    public static partial void MeilisearchIndexesSwapped(
        this ILogger logger, string stagingIndexName, string liveIndexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 113,
        Level = LogLevel.Warning,
        Message = "Staging index '{StagingIndexName}' was retained after cutover and still holds pre-cutover data.")]
    public static partial void MeilisearchStagingIndexRetained(this ILogger logger, string stagingIndexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 114,
        Level = LogLevel.Debug,
        Message = "Issued a Meilisearch tenant search token scoped to {IndexCount} index(es) with a {TtlMinutes} minute TTL.")]
    public static partial void MeilisearchTenantTokenIssued(this ILogger logger, int indexCount, int ttlMinutes);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 115,
        Level = LogLevel.Warning,
        Message = "Meilisearch raw client access is enabled. The raw client bypasses tenant scoping.")]
    public static partial void MeilisearchRawClientAccessEnabled(this ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 116,
        Level = LogLevel.Warning,
        Message = "Probe of index '{IndexName}' degraded: Reachable={Reachable} IndexAddressable={IndexAddressable} Searchable={Searchable}.")]
    public static partial void MeilisearchProbeDegraded(
        this ILogger logger, string indexName, bool reachable, bool indexAddressable, bool searchable);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 117,
        Level = LogLevel.Warning,
        Message = "Meilisearch write backlog is deep: {PendingWriteCount} pending task(s).")]
    public static partial void MeilisearchWriteBacklogDeep(this ILogger logger, long pendingWriteCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 118,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' schema fingerprint mismatch: expected '{Expected}', found '{Actual}'.")]
    public static partial void MeilisearchSchemaFingerprintMismatch(
        this ILogger logger, string indexName, string expected, string actual);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 119,
        Level = LogLevel.Error,
        Message = "Meilisearch operation '{Operation}' on index '{IndexName}' faulted.")]
    public static partial void MeilisearchEngineFault(this ILogger logger, string operation, string indexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 120,
        Level = LogLevel.Debug,
        Message = "Started document walk over index '{IndexName}' with batch size {BatchSize}.")]
    public static partial void MeilisearchDocumentWalkStarted(this ILogger logger, string indexName, int batchSize);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 121,
        Level = LogLevel.Debug,
        Message = "Bulk operation on index '{IndexName}' throttled: waiting {DelayMs}ms before the next batch.")]
    public static partial void MeilisearchBulkThrottled(this ILogger logger, string indexName, double delayMs);
}
