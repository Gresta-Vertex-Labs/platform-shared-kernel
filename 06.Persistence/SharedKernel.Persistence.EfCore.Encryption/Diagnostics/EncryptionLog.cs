using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

/// <summary>Source-generated log statements of <c>SharedKernel.Persistence.EfCore.Encryption</c>.</summary>
/// <remarks>
/// EventIds 6500-6699 of the <see cref="LoggingEventIdRanges.Persistence"/> block. No statement ever logs key
/// material, a stored or decrypted value, a primary key or a tenant id.
/// </remarks>
internal static partial class EncryptionLog
{
    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 500, Level = LogLevel.Warning,
        Message = "Refreshing encryption keys from the key provider failed; the keys loaded earlier stay in use.")]
    internal static partial void KeyRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 501, Level = LogLevel.Error,
        Message = "Encryption keys have not been refreshed for {StaleFor}; a key made current since then cannot be decrypted by this process.")]
    internal static partial void KeysStale(ILogger logger, TimeSpan staleFor);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 502, Level = LogLevel.Information,
        Message = "Encryption key {KeyId} was not loaded; fetched it from the key provider after a value needed it.")]
    internal static partial void KeyFetchedOnMiss(ILogger logger, string keyId);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 503, Level = LogLevel.Warning,
        Message = "Fetching encryption key {KeyId} after a value needed it failed, or the key provider does not know it.")]
    internal static partial void KeyFetchOnMissFailed(ILogger logger, string keyId, Exception? exception);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 504, Level = LogLevel.Warning,
        Message = "A previous save on {ContextType} stopped between encryption and restore; restored the tracked plaintext before saving again.")]
    internal static partial void StalePendingRestored(ILogger logger, string contextType);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 510, Level = LogLevel.Information,
        Message = "Encryption maintenance ({Mode}) started on {Target}: about {EstimatedRows} row(s).")]
    internal static partial void MaintenanceTargetStarted(ILogger logger, string mode, string target, long estimatedRows);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 511, Level = LogLevel.Debug,
        Message = "Encryption maintenance processed {RowsInBatch} row(s) of {Target}.")]
    internal static partial void MaintenanceBatchProcessed(ILogger logger, string target, int rowsInBatch);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 512, Level = LogLevel.Information,
        Message = "Encryption maintenance ({Mode}) finished: {RowsScanned} scanned, {RowsWritten} written, {RowsConcurrentlyModified} changed concurrently, {RowsPlaintext} plaintext, {RowsUndecryptable} undecryptable, {RowsShredded} shredded.")]
    internal static partial void MaintenanceCompleted(
        ILogger logger,
        string mode,
        long rowsScanned,
        long rowsWritten,
        long rowsConcurrentlyModified,
        long rowsPlaintext,
        long rowsUndecryptable,
        long rowsShredded);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 513, Level = LogLevel.Warning,
        Message = "Encryption maintenance found {Count} value(s) in {Target} that it could not process ({Reason}).")]
    internal static partial void MaintenanceValuesSkipped(ILogger logger, string target, long count, string reason);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 520, Level = LogLevel.Information,
        Message = "Created a tenant data key wrapped by master key {MasterKeyId}.")]
    internal static partial void TenantKeyCreated(ILogger logger, string masterKeyId);

    [LoggerMessage(EventId = LoggingEventIdRanges.Persistence + 521, Level = LogLevel.Warning,
        Message = "Shredded a tenant data key and cleared {BlindIndexValuesCleared} blind-index value(s); the tenant's encrypted values can no longer be decrypted.")]
    internal static partial void TenantKeyShredded(ILogger logger, long blindIndexValuesCleared);
}
