using Microsoft.Extensions.Logging;

namespace SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

/// <summary>Log statements of <c>SharedKernel.Persistence.EfCore.Encryption</c>.</summary>
/// <remarks>
/// No statement ever logs key material, a stored or decrypted value, a primary key or a tenant id; a cause is a closed set,
/// never composed text.
/// </remarks>
internal static partial class EncryptionLog
{
    [LoggerMessage(EventId = EncryptionLoggingEventIds.KeyRefreshFailed, Level = LogLevel.Warning,
        Message = "Refreshing encryption keys from the key provider failed, so the keys loaded earlier stay in use.")]
    internal static partial void KeyRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.KeysStale, Level = LogLevel.Error,
        Message = "Encryption keys have not been refreshed for {StaleFor}, so a key made current since then is not decryptable by this process.")]
    internal static partial void KeysStale(ILogger logger, TimeSpan staleFor);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.KeyFetchedOnMiss, Level = LogLevel.Information,
        Message = "Encryption key '{KeyId}' was not loaded, so it was fetched from the key provider after a value needed it.")]
    internal static partial void KeyFetchedOnMiss(ILogger logger, string keyId);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.KeyFetchOnMissFailed, Level = LogLevel.Warning,
        Message = "Fetching encryption key '{KeyId}' after a value needed it failed, or the key provider does not know it.")]
    internal static partial void KeyFetchOnMissFailed(ILogger logger, string keyId, Exception? exception);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.StalePendingRestored, Level = LogLevel.Warning,
        Message = "A previous save on context '{ContextType}' stopped between encryption and restore, so the tracked plaintext was restored before saving again.")]
    internal static partial void StalePendingRestored(ILogger logger, string contextType);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.MaintenanceTargetStarted, Level = LogLevel.Information,
        Message = "Encryption maintenance in mode {Mode} started on target '{Target}' with about {EstimatedRows} rows.")]
    internal static partial void MaintenanceTargetStarted(ILogger logger, string mode, string target, long estimatedRows);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.MaintenanceBatchProcessed, Level = LogLevel.Debug,
        Message = "Encryption maintenance processed {RowsInBatch} rows of target '{Target}'.")]
    internal static partial void MaintenanceBatchProcessed(ILogger logger, string target, int rowsInBatch);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.MaintenanceCompleted, Level = LogLevel.Information,
        Message = "Encryption maintenance in mode {Mode} finished with {RowsScanned} scanned, {RowsWritten} written, {RowsConcurrentlyModified} changed concurrently, {RowsPlaintext} plaintext, {RowsUndecryptable} undecryptable and {RowsShredded} shredded.")]
    internal static partial void MaintenanceCompleted(
        ILogger logger,
        string mode,
        long rowsScanned,
        long rowsWritten,
        long rowsConcurrentlyModified,
        long rowsPlaintext,
        long rowsUndecryptable,
        long rowsShredded);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.MaintenanceValuesSkipped, Level = LogLevel.Warning,
        Message = "Encryption maintenance skipped {Count} values of target '{Target}' with cause {Cause}.")]
    internal static partial void MaintenanceValuesSkipped(ILogger logger, string target, long count, EncryptionMaintenanceSkip cause);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.TenantKeyCreated, Level = LogLevel.Information,
        Message = "A tenant data key was created.")]
    internal static partial void TenantKeyCreated(ILogger logger);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.TenantKeyShredded, Level = LogLevel.Warning,
        Message = "A tenant data key was shredded and {BlindIndexValuesCleared} blind-index values were cleared, so the encrypted values of the tenant are no longer decryptable.")]
    internal static partial void TenantKeyShredded(ILogger logger, long blindIndexValuesCleared);

    [LoggerMessage(EventId = EncryptionLoggingEventIds.TenantShredIncomplete, Level = LogLevel.Warning,
        Message = "The shredded tenant still has {RootKeyValues} values under a root key and {PlaintextValues} plaintext values in encrypted columns, and they are not erased until their rows are deleted.")]
    internal static partial void TenantShredIncomplete(ILogger logger, long rootKeyValues, long plaintextValues);
}

/// <summary>Why the maintenance job skipped values: a closed set, so a log carries no composed text.</summary>
internal enum EncryptionMaintenanceSkip
{
    /// <summary>The values are stored as plaintext and the run was not asked to encrypt them.</summary>
    Plaintext,

    /// <summary>The values could not be decrypted.</summary>
    Undecryptable,

    /// <summary>The values belong to a shredded tenant whose rows are not deleted yet.</summary>
    ShreddedTenantNotErased,
}
