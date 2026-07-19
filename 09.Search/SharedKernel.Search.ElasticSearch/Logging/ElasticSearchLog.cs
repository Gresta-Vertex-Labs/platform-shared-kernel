using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Search.ElasticSearch.Logging;

/// <summary>
/// The <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.Search.ElasticSearch</c>,
/// reserved <c>EventId</c> sub-block 9200-9299 (<see cref="LoggingEventIdRanges.Search"/> + 200..299).
/// </summary>
internal static partial class ElasticSearchLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 200,
        Level = LogLevel.Information,
        Message = "ElasticSearch client configured for {NodeCount} node(s) with {IndexCount} registered index(es).")]
    public static partial void ElasticSearchClientConfigured(this ILogger logger, int nodeCount, int indexCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 201,
        Level = LogLevel.Information,
        Message = "ElasticSearch engine version {EngineVersion} verified as supported.")]
    public static partial void ElasticSearchEngineVersionVerified(this ILogger logger, string engineVersion);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 202,
        Level = LogLevel.Error,
        Message = "ElasticSearch engine version {EngineVersion} is not supported; expected {SupportedRange}.")]
    public static partial void ElasticSearchEngineVersionUnsupported(
        this ILogger logger, string engineVersion, string supportedRange);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 203,
        Level = LogLevel.Information,
        Message = "ElasticSearch index '{IndexName}' ensured with {FieldCount} declared field(s) and max_result_window {MaxResultWindow}.")]
    public static partial void ElasticSearchIndexEnsured(
        this ILogger logger, string indexName, int fieldCount, int maxResultWindow);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 204,
        Level = LogLevel.Debug,
        Message = "Indexed {DocumentCount} document(s) on '{IndexName}' with refresh mode {RefreshMode}.")]
    public static partial void ElasticSearchDocumentsIndexed(
        this ILogger logger, string indexName, int documentCount, string refreshMode);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 205,
        Level = LogLevel.Debug,
        Message = "Bulk operation on '{IndexName}' completed for {DocumentCount} document(s) in {ElapsedMs}ms.")]
    public static partial void ElasticSearchBulkCompleted(
        this ILogger logger, string indexName, int documentCount, long elapsedMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 206,
        Level = LogLevel.Warning,
        Message = "Bulk operation on index '{IndexName}' partially failed: {FailedCount} of {TotalCount} document(s).")]
    public static partial void ElasticSearchBulkPartialFailure(
        this ILogger logger, string indexName, int failedCount, int totalCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 207,
        Level = LogLevel.Warning,
        Message = "Waiting for a refresh on index '{IndexName}' timed out after {ElapsedMs}ms.")]
    public static partial void ElasticSearchRefreshWaitTimedOut(this ILogger logger, string indexName, long elapsedMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 208,
        Level = LogLevel.Debug,
        Message = "Search on index '{IndexName}' returned {HitCount} hit(s) of {TotalHits} ({Accuracy}) in {TookMs}ms.")]
    public static partial void ElasticSearchSearchExecuted(
        this ILogger logger, string indexName, int hitCount, long totalHits, string accuracy, long tookMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 209,
        Level = LogLevel.Warning,
        Message = "Search request against index '{IndexName}' rejected before any I/O: {Reason}")]
    public static partial void ElasticSearchRequestRejected(this ILogger logger, string indexName, string reason);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 210,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' declares a TenantField but the caller supplied TenantScope.None.")]
    public static partial void ElasticSearchTenantScopeMissing(this ILogger logger, string indexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 211,
        Level = LogLevel.Information,
        Message = "ElasticSearch alias cutover completed: staging '{StagingIndexName}' is now live as '{LiveIndexName}'.")]
    public static partial void ElasticSearchAliasCutoverCompleted(
        this ILogger logger, string stagingIndexName, string liveIndexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 212,
        Level = LogLevel.Information,
        Message = "Staging index '{StagingIndexName}' deleted after cutover.")]
    public static partial void ElasticSearchStagingIndexDeleted(this ILogger logger, string stagingIndexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 213,
        Level = LogLevel.Debug,
        Message = "Opened a point-in-time on index '{IndexName}' with a {KeepAliveSeconds}s keep-alive.")]
    public static partial void ElasticSearchPointInTimeOpened(this ILogger logger, string indexName, int keepAliveSeconds);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 214,
        Level = LogLevel.Debug,
        Message = "Closed the point-in-time on index '{IndexName}' after {BatchCount} batch(es).")]
    public static partial void ElasticSearchPointInTimeClosed(this ILogger logger, string indexName, int batchCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 215,
        Level = LogLevel.Warning,
        Message = "Failed to close the point-in-time on index '{IndexName}'.")]
    public static partial void ElasticSearchPointInTimeCloseFailed(this ILogger logger, string indexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 216,
        Level = LogLevel.Warning,
        Message = "ElasticSearch raw client access is enabled. The raw client bypasses tenant scoping.")]
    public static partial void ElasticSearchRawClientAccessEnabled(this ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 217,
        Level = LogLevel.Warning,
        Message = "ElasticSearch TLS certificate validation is disabled. Never enable this in production.")]
    public static partial void ElasticSearchCertificateValidationDisabled(this ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 218,
        Level = LogLevel.Information,
        Message = "Cluster health for index '{IndexName}' is yellow, treated as healthy.")]
    public static partial void ElasticSearchClusterYellowAccepted(this ILogger logger, string indexName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 219,
        Level = LogLevel.Warning,
        Message = "Probe of index '{IndexName}' degraded: ClusterStatus={ClusterStatus} IndexAddressable={IndexAddressable} Searchable={Searchable}.")]
    public static partial void ElasticSearchProbeDegraded(
        this ILogger logger, string indexName, string clusterStatus, bool indexAddressable, bool searchable);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 220,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' schema fingerprint mismatch: expected '{Expected}', found '{Actual}'.")]
    public static partial void ElasticSearchSchemaFingerprintMismatch(
        this ILogger logger, string indexName, string expected, string actual);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 221,
        Level = LogLevel.Warning,
        Message = "No JsonSerializerContext registered via WithSourceSerializerContext(...) for document type '{DocumentTypeName}'.")]
    public static partial void ElasticSearchSourceSerializerContextMissing(this ILogger logger, string documentTypeName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 222,
        Level = LogLevel.Error,
        Message = "ElasticSearch operation '{Operation}' on index '{IndexName}' faulted with status code {StatusCode}.")]
    public static partial void ElasticSearchEngineFault(
        this ILogger logger, string operation, string indexName, int? statusCode);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Search + 223,
        Level = LogLevel.Debug,
        Message = "Aggregation on index '{IndexName}' executed {AggregationCount} request(s) in {TookMs}ms.")]
    public static partial void ElasticSearchAggregationExecuted(
        this ILogger logger, string indexName, int aggregationCount, long tookMs);
}
