using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.AI.Qdrant.Logging;

/// <summary>
/// The <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.AI.Qdrant</c>,
/// reserved <c>EventId</c> sub-block 10100–10199 (<see cref="LoggingEventIdRanges.Intelligence"/> + 100..199).
/// </summary>
internal static partial class QdrantLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 100,
        Level = LogLevel.Information,
        Message = "Qdrant client configured for {Host}:{Port} with {CollectionCount} registered collection(s).")]
    public static partial void QdrantClientConfigured(this ILogger logger, string host, int port, int collectionCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 101,
        Level = LogLevel.Information,
        Message = "Qdrant collection '{CollectionName}' ensured with {FieldCount} filterable field(s).")]
    public static partial void QdrantCollectionEnsured(this ILogger logger, string collectionName, int fieldCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 102,
        Level = LogLevel.Debug,
        Message = "Upserted {RecordCount} record(s) into collection '{CollectionName}' as operation '{ProviderToken}'.")]
    public static partial void QdrantRecordsUpserted(this ILogger logger, string collectionName, int recordCount, string providerToken);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 103,
        Level = LogLevel.Debug,
        Message = "Deleted {RecordCount} record(s) from collection '{CollectionName}' as operation '{ProviderToken}'.")]
    public static partial void QdrantRecordsDeleted(this ILogger logger, string collectionName, int recordCount, string providerToken);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 104,
        Level = LogLevel.Warning,
        Message = "Bulk operation on collection '{CollectionName}' partially failed: {FailedCount} of {TotalCount} record(s).")]
    public static partial void QdrantBulkPartialFailure(this ILogger logger, string collectionName, int failedCount, int totalCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 105,
        Level = LogLevel.Debug,
        Message = "Query against collection '{CollectionName}' returned {HitCount} hit(s) in {DurationMs}ms.")]
    public static partial void QdrantQueryExecuted(this ILogger logger, string collectionName, int hitCount, long durationMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 106,
        Level = LogLevel.Warning,
        Message = "Request against collection '{CollectionName}' rejected before any I/O: {Reason}")]
    public static partial void QdrantRequestRejected(this ILogger logger, string collectionName, string reason);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 107,
        Level = LogLevel.Warning,
        Message = "Collection '{CollectionName}' declares a TenantField but the caller supplied TenantScope.None.")]
    public static partial void QdrantTenantScopeMissing(this ILogger logger, string collectionName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 108,
        Level = LogLevel.Information,
        Message = "Qdrant alias '{LiveCollectionName}' cut over from staging collection '{StagingCollectionName}'.")]
    public static partial void QdrantCollectionsCutOver(this ILogger logger, string stagingCollectionName, string liveCollectionName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 109,
        Level = LogLevel.Warning,
        Message = "Staging collection '{StagingCollectionName}' was retained after cutover and still holds pre-cutover data.")]
    public static partial void QdrantStagingCollectionRetained(this ILogger logger, string stagingCollectionName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 110,
        Level = LogLevel.Warning,
        Message = "Qdrant raw client access is enabled. The raw client bypasses tenant scoping.")]
    public static partial void QdrantRawClientAccessEnabled(this ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 111,
        Level = LogLevel.Warning,
        Message = "Probe of collection '{CollectionName}' degraded: Reachable={Reachable} CollectionAddressable={CollectionAddressable} Queryable={Queryable}.")]
    public static partial void QdrantProbeDegraded(this ILogger logger, string collectionName, bool reachable, bool collectionAddressable, bool queryable);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 112,
        Level = LogLevel.Warning,
        Message = "Collection '{CollectionName}' schema fingerprint mismatch: expected '{Expected}', found '{Actual}'.")]
    public static partial void QdrantSchemaFingerprintMismatch(this ILogger logger, string collectionName, string expected, string actual);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 113,
        Level = LogLevel.Error,
        Message = "Qdrant operation '{Operation}' on collection '{CollectionName}' faulted.")]
    public static partial void QdrantEngineFault(this ILogger logger, string operation, string collectionName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Intelligence + 114,
        Level = LogLevel.Debug,
        Message = "Started record walk over collection '{CollectionName}' with batch size {BatchSize}.")]
    public static partial void QdrantRecordWalkStarted(this ILogger logger, string collectionName, int batchSize);
}
