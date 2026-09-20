using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.Persistence.EfCore.Encryption</c>.
/// </summary>
/// <remarks>
/// Claims EventId sub-block <c>6300-6399</c> within <c>01.Core</c>'s <see cref="LoggingEventIdRanges.Persistence"/>
/// (<c>6000-6999</c>) block, the next unclaimed 100-wide slot after <c>SharedKernel.Persistence.EfCore</c>
/// (<c>6000-6099</c>), <c>.Npgsql</c> (<c>6100-6199</c>) and <c>.Dapper</c> (<c>6200-6299</c>).
/// <c>SharedKernel.Persistence.EfCore.Auditing</c> has not yet claimed a sub-block as of this package's own; the
/// next package to start logging claims <c>6400-6499</c>, never renumbering retroactively.
/// </remarks>
internal static partial class EncryptionLog
{
    /// <summary>
    /// Logged by <see cref="KeyRing.EncryptionKeyRingRefreshHostedService"/> when a periodic refresh of the key
    /// ring from the wrapped asynchronous key provider fails. The previous snapshot stays in use. Never logs key
    /// material.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 300,
        Level = LogLevel.Warning,
        Message = "Refreshing the encryption key ring from the external key provider failed; the last warmed keys remain in use.")]
    internal static partial void KeyRingRefreshFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logged by <c>EncryptionRotationService</c> at each batch boundary. Never logs a key byte, a Base64-encoded
    /// key string, a row's primary key, or any column plaintext/ciphertext value — only counts and already-
    /// non-secret key id strings. Earlier revisions of this message logged the last-processed primary key's text
    /// form as "checkpoint"; for a non-Guid rotation key (e.g. a string business key) that value can itself be
    /// PII, so it was removed rather than only truncated or hashed — the resumable checkpoint token itself is
    /// returned to the caller in <c>EncryptionRotationReport.CheckpointToken</c>, never logged.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 301,
        Level = LogLevel.Information,
        Message = "Encryption rotation for '{EntityType}' processed {RowsInBatch} row(s) toward key '{ToKeyId}'.")]
    internal static partial void RotationBatchProcessed(ILogger logger, string entityType, int rowsInBatch, string toKeyId);

    /// <summary>
    /// Logged by <c>EncryptionRotationService</c> once, on overall completion of a rotation run. Never logs a key
    /// byte, a Base64-encoded key string, or any column plaintext/ciphertext value.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 302,
        Level = LogLevel.Information,
        Message = "Encryption rotation for '{EntityType}' to key '{ToKeyId}' completed: {RowsProcessed} processed, {RowsRotated} rotated, {RowsFailed} failed, {RowsSkippedUnparseable} skipped (unparseable).")]
    internal static partial void RotationCompleted(
        ILogger logger, string entityType, string toKeyId, long rowsProcessed, long rowsRotated, long rowsFailed, long rowsSkippedUnparseable);

    /// <summary>
    /// Logged by <c>EncryptionRotationService</c> when a row's compare-and-swap update did not match — another
    /// writer changed the row between the read and the write. That row is left for the next rotation pass rather
    /// than overwritten. The row's primary key is never logged.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 303,
        Level = LogLevel.Debug,
        Message = "Encryption rotation for '{EntityType}' skipped one row: a concurrent writer changed it between read and re-encrypt.")]
    internal static partial void RotationRowConcurrentlyModified(ILogger logger, string entityType);

    /// <summary>
    /// Logged by <c>EncryptionRotationService</c> when a row's stored value was neither <see langword="null"/> nor
    /// a parseable encrypted payload — left un-rotated and counted in
    /// <c>EncryptionRotationReport.RowsSkippedUnparseable</c>. Warning, not Debug: unlike a concurrent-write
    /// collision, this needs a human to look at the data, not just a second rotation pass. The row's primary key
    /// and the stored value are never logged.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 304,
        Level = LogLevel.Warning,
        Message = "Encryption rotation for '{EntityType}' skipped one row: its stored value did not parse as an encrypted payload.")]
    internal static partial void RotationRowUnparseable(ILogger logger, string entityType);
}
