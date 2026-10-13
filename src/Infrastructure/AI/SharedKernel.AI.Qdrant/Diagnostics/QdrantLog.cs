using Microsoft.Extensions.Logging;

namespace SharedKernel.AI.Qdrant.Diagnostics;

/// <summary>
/// The package's log messages, source generated, in the <c>EventId</c> sub-block 10100-10199
/// (<see cref="QdrantLoggingEventIds"/>). The category is the calling kernel type's <c>ILogger&lt;T&gt;</c>.
/// </summary>
internal static partial class QdrantLog
{
    [LoggerMessage(
        EventId = QdrantLoggingEventIds.ClientConfigured,
        Level = LogLevel.Information,
        Message = "Qdrant client configured with {CollectionCount} registered collection(s).")]
    public static partial void QdrantClientConfigured(ILogger logger, int collectionCount);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.CollectionEnsured,
        Level = LogLevel.Information,
        Message = "Qdrant collection '{CollectionName}' ensured with {FieldCount} filterable field(s).")]
    public static partial void QdrantCollectionEnsured(ILogger logger, string collectionName, int fieldCount);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.RecordsUpserted,
        Level = LogLevel.Debug,
        Message = "Upserted {RecordCount} record(s) into collection '{CollectionName}' as operation '{ProviderToken}'.")]
    public static partial void QdrantRecordsUpserted(ILogger logger, string collectionName, int recordCount, string providerToken);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.RecordsDeleted,
        Level = LogLevel.Debug,
        Message = "Deleted {RecordCount} record(s) from collection '{CollectionName}' as operation '{ProviderToken}'.")]
    public static partial void QdrantRecordsDeleted(ILogger logger, string collectionName, int recordCount, string providerToken);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.BulkPartialFailure,
        Level = LogLevel.Warning,
        Message = "Bulk operation on collection '{CollectionName}' partially failed for {FailedCount} of {TotalCount} record(s).")]
    public static partial void QdrantBulkPartialFailure(ILogger logger, string collectionName, int failedCount, int totalCount);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.QueryExecuted,
        Level = LogLevel.Debug,
        Message = "Query against collection '{CollectionName}' returned {HitCount} hit(s) in {ElapsedMilliseconds} ms.")]
    public static partial void QdrantQueryExecuted(ILogger logger, string collectionName, int hitCount, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.RequestRejected,
        Level = LogLevel.Warning,
        Message = "Request against collection '{CollectionName}' was rejected before any I/O with error type '{ErrorType}' and code '{ErrorCode}'.")]
    public static partial void QdrantRequestRejected(ILogger logger, string collectionName, string errorType, string errorCode);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.TenantScopeMissing,
        Level = LogLevel.Warning,
        Message = "Collection '{CollectionName}' declares a tenant field but the caller supplied the global tenant scope.")]
    public static partial void QdrantTenantScopeMissing(ILogger logger, string collectionName);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.CollectionsCutOver,
        Level = LogLevel.Information,
        Message = "Qdrant alias '{LiveCollectionName}' was cut over from staging collection '{StagingCollectionName}'.")]
    public static partial void QdrantCollectionsCutOver(ILogger logger, string stagingCollectionName, string liveCollectionName);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.StagingCollectionRetained,
        Level = LogLevel.Warning,
        Message = "Staging collection '{StagingCollectionName}' was retained after cutover and still holds pre-cutover data.")]
    public static partial void QdrantStagingCollectionRetained(ILogger logger, string stagingCollectionName);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.RawClientAccessEnabled,
        Level = LogLevel.Warning,
        Message = "Qdrant raw client access is enabled and bypasses tenant scoping.")]
    public static partial void QdrantRawClientAccessEnabled(ILogger logger);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.ProbeDegraded,
        Level = LogLevel.Warning,
        Message = "Probe of collection '{CollectionName}' degraded with reachable {Reachable}, addressable {CollectionAddressable} and queryable {Queryable}.")]
    public static partial void QdrantProbeDegraded(ILogger logger, string collectionName, bool reachable, bool collectionAddressable, bool queryable, Exception? exception);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.SchemaFingerprintMismatch,
        Level = LogLevel.Warning,
        Message = "Collection '{CollectionName}' has schema fingerprint '{ActualFingerprint}' but '{ExpectedFingerprint}' was expected.")]
    public static partial void QdrantSchemaFingerprintMismatch(ILogger logger, string collectionName, string expectedFingerprint, string actualFingerprint);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.EngineFault,
        Level = LogLevel.Warning,
        Message = "Qdrant operation '{Operation}' on collection '{CollectionName}' faulted with error code '{ErrorCode}'.")]
    public static partial void QdrantEngineFault(ILogger logger, string operation, string collectionName, string errorCode, Exception exception);

    [LoggerMessage(
        EventId = QdrantLoggingEventIds.RecordWalkStarted,
        Level = LogLevel.Debug,
        Message = "Started record walk over collection '{CollectionName}' with batch size {BatchSize}.")]
    public static partial void QdrantRecordWalkStarted(ILogger logger, string collectionName, int batchSize);
}
