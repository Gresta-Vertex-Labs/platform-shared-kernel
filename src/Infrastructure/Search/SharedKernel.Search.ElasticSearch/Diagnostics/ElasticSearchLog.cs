using Microsoft.Extensions.Logging;

namespace SharedKernel.Search.ElasticSearch.Diagnostics;

/// <summary>
/// Log statements of <c>SharedKernel.Search.ElasticSearch</c>, EventIds 9200-9299 (see
/// <see cref="ElasticSearchLoggingEventIds"/>). Query text, filter values, document ids, tenant ids, cursor tokens,
/// endpoints and engine error text are never logged; engine detail reaches the log only through the exception parameter.
/// </summary>
internal static partial class ElasticSearchLog
{
    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.ClientConfigured,
        Level = LogLevel.Information,
        Message = "The client is configured for {NodeCount} nodes with {IndexCount} registered indexes.")]
    public static partial void ClientConfigured(ILogger logger, int nodeCount, int indexCount);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.EngineVersionVerified,
        Level = LogLevel.Information,
        Message = "Engine version '{EngineVersion}' was verified as supported.")]
    public static partial void EngineVersionVerified(ILogger logger, string engineVersion);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.EngineVersionNotSupported,
        Level = LogLevel.Error,
        Message = "Engine version '{EngineVersion}' is not supported, expected '{SupportedRange}'.")]
    public static partial void EngineVersionNotSupported(ILogger logger, string engineVersion, string supportedRange);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.IndexEnsured,
        Level = LogLevel.Information,
        Message = "Index '{IndexName}' was ensured with {FieldCount} declared fields and a max_result_window of {MaxResultWindow}.")]
    public static partial void IndexEnsured(ILogger logger, string indexName, int fieldCount, int maxResultWindow);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.DocumentsIndexed,
        Level = LogLevel.Debug,
        Message = "Indexed {DocumentCount} documents on index '{IndexName}' with refresh mode '{RefreshMode}'.")]
    public static partial void DocumentsIndexed(ILogger logger, string indexName, int documentCount, string refreshMode);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.BulkCompleted,
        Level = LogLevel.Debug,
        Message = "Bulk operation on index '{IndexName}' completed for {DocumentCount} documents in {ElapsedMilliseconds} ms.")]
    public static partial void BulkCompleted(ILogger logger, string indexName, int documentCount, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.BulkPartialFailure,
        Level = LogLevel.Warning,
        Message = "Bulk operation on index '{IndexName}' partially failed for {FailedCount} of {TotalCount} documents.")]
    public static partial void BulkPartialFailure(ILogger logger, string indexName, int failedCount, int totalCount);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.RefreshWaitTimedOut,
        Level = LogLevel.Warning,
        Message = "Waiting for a refresh on index '{IndexName}' timed out after {ElapsedMilliseconds} ms.")]
    public static partial void RefreshWaitTimedOut(ILogger logger, string indexName, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.SearchExecuted,
        Level = LogLevel.Debug,
        Message = "Search on index '{IndexName}' returned {HitCount} hits of {TotalHits} ({Accuracy}) in {ElapsedMilliseconds} ms.")]
    public static partial void SearchExecuted(
        ILogger logger, string indexName, int hitCount, long totalHits, string accuracy, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.RequestRejected,
        Level = LogLevel.Warning,
        Message = "Search request against index '{IndexName}' was rejected before any I/O with error type '{ErrorType}' and error code '{ErrorCode}'.")]
    public static partial void RequestRejected(ILogger logger, string indexName, string errorType, string errorCode);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.TenantScopeMissing,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' declares a tenant field but the caller supplied the global tenant scope.")]
    public static partial void TenantScopeMissing(ILogger logger, string indexName);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.AliasCutoverCompleted,
        Level = LogLevel.Information,
        Message = "Alias cutover completed, so staging index '{StagingIndexName}' is now live as index '{LiveIndexName}'.")]
    public static partial void AliasCutoverCompleted(ILogger logger, string stagingIndexName, string liveIndexName);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.StagingIndexDeleted,
        Level = LogLevel.Information,
        Message = "Staging index '{StagingIndexName}' was deleted after the cutover.")]
    public static partial void StagingIndexDeleted(ILogger logger, string stagingIndexName);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.PointInTimeOpened,
        Level = LogLevel.Debug,
        Message = "Opened a point-in-time on index '{IndexName}' with a keep-alive of {KeepAliveSeconds} s.")]
    public static partial void PointInTimeOpened(ILogger logger, string indexName, int keepAliveSeconds);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.PointInTimeClosed,
        Level = LogLevel.Debug,
        Message = "Closed the point-in-time on index '{IndexName}' after {BatchCount} batches.")]
    public static partial void PointInTimeClosed(ILogger logger, string indexName, int batchCount);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.PointInTimeCloseFailed,
        Level = LogLevel.Warning,
        Message = "Closing the point-in-time on index '{IndexName}' failed.")]
    public static partial void PointInTimeCloseFailed(ILogger logger, string indexName, Exception? exception);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.RawClientAccessEnabled,
        Level = LogLevel.Warning,
        Message = "Raw client access is enabled, so the raw client bypasses tenant scoping.")]
    public static partial void RawClientAccessEnabled(ILogger logger);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.CertificateValidationDisabled,
        Level = LogLevel.Warning,
        Message = "TLS certificate validation is disabled, so never enable this in production.")]
    public static partial void CertificateValidationDisabled(ILogger logger);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.ClusterYellowAccepted,
        Level = LogLevel.Information,
        Message = "Cluster health for index '{IndexName}' is yellow and is treated as healthy.")]
    public static partial void ClusterYellowAccepted(ILogger logger, string indexName);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.ProbeDegraded,
        Level = LogLevel.Warning,
        Message = "Probe of index '{IndexName}' is degraded with cluster status '{ClusterStatus}', addressable {IndexAddressable} and searchable {Searchable}.")]
    public static partial void ProbeDegraded(
        ILogger logger, string indexName, string clusterStatus, bool indexAddressable, bool searchable, Exception? exception);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.SchemaFingerprintMismatch,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' has schema fingerprint '{Actual}' where '{Expected}' was expected.")]
    public static partial void SchemaFingerprintMismatch(ILogger logger, string indexName, string expected, string actual);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.SourceSerializerContextMissing,
        Level = LogLevel.Warning,
        Message = "No JsonSerializerContext was registered with WithSourceSerializerContext, so the client falls back to reflection-based serialization for {IndexCount} registered indexes.")]
    public static partial void SourceSerializerContextMissing(ILogger logger, int indexCount);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.EngineFault,
        Level = LogLevel.Warning,
        Message = "Operation '{Operation}' on index '{IndexName}' faulted with status code {StatusCode}.")]
    public static partial void EngineFault(
        ILogger logger, string operation, string indexName, int? statusCode, Exception? exception);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.AggregationExecuted,
        Level = LogLevel.Debug,
        Message = "Aggregation on index '{IndexName}' executed {AggregationCount} requests in {ElapsedMilliseconds} ms.")]
    public static partial void AggregationExecuted(ILogger logger, string indexName, int aggregationCount, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.BulkThrottled,
        Level = LogLevel.Debug,
        Message = "Bulk operation on index '{IndexName}' is waiting {DelayMilliseconds} ms before the next batch.")]
    public static partial void BulkThrottled(ILogger logger, string indexName, double delayMilliseconds);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.OperationFaulted,
        Level = LogLevel.Warning,
        Message = "Operation '{Operation}' on index '{IndexName}' faulted and was mapped to error code '{ErrorCode}'.")]
    public static partial void OperationFaulted(
        ILogger logger, string operation, string indexName, string errorCode, Exception? exception);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.TextAnalysisApplied,
        Level = LogLevel.Information,
        Message = "Text analysis of index '{IndexName}' was applied with {SynonymCount} synonym terms and {StopWordCount} stop words.")]
    public static partial void TextAnalysisApplied(ILogger logger, string indexName, int synonymCount, int stopWordCount);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.IndexDrifted,
        Level = LogLevel.Warning,
        Message = "Index '{IndexName}' drifted from its registered definition with drift kind '{DriftKind}'.")]
    public static partial void IndexDrifted(ILogger logger, string indexName, string driftKind);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.IndexVerified,
        Level = LogLevel.Information,
        Message = "Index '{IndexName}' was verified against the registered definition.")]
    public static partial void IndexVerified(ILogger logger, string indexName);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.SuggestExecuted,
        Level = LogLevel.Debug,
        Message = "Completion suggester on index '{IndexName}' returned {SuggestionCount} suggestions.")]
    public static partial void SuggestExecuted(ILogger logger, string indexName, int suggestionCount);

    [LoggerMessage(
        EventId = ElasticSearchLoggingEventIds.CursorReadFaulted,
        Level = LogLevel.Warning,
        Message = "Cursor read on index '{IndexName}' faulted and was mapped to error code '{ErrorCode}'.")]
    public static partial void CursorReadFaulted(ILogger logger, string indexName, string errorCode, Exception? exception);
}
