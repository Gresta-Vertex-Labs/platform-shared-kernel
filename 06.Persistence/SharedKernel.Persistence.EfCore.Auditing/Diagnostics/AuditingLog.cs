using Microsoft.Extensions.Logging;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.Persistence.EfCore.Auditing</c>.
/// </summary>
/// <remarks>
/// Claims EventId sub-block <c>6400-6499</c> — the next unclaimed 100-wide slot after
/// <c>SharedKernel.Persistence.EfCore</c> (6000-6099), <c>.Npgsql</c> (6100-6199), <c>.Dapper</c>
/// (6200-6299), and <c>.EfCore.Encryption</c> (6300-6399), all within <c>01.Core</c>'s
/// <c>LoggingEventIdRanges.Persistence</c> (6000-6999) base.
/// </remarks>
internal static partial class AuditingLog
{
    [LoggerMessage(
        EventId = 6400,
        Level = LogLevel.Warning,
        Message = "No IAdvisoryTransactionLock is registered — concurrent appends to the same audit chain rely solely on unique-constraint retry, not a per-chain lock. Register SharedKernel.Persistence.Npgsql's NpgsqlAdvisoryTransactionLock for stronger serialization.")]
    public static partial void NoAdvisoryTransactionLockRegistered(ILogger logger);

    [LoggerMessage(
        EventId = 6401,
        Level = LogLevel.Information,
        Message = "RecordAsync for chain '{ChainKey}' returned the existing record for idempotency key '{IdempotencyKey}' instead of appending a duplicate.")]
    public static partial void IdempotentDuplicateReturned(ILogger logger, string chainKey, string idempotencyKey);

    [LoggerMessage(
        EventId = 6402,
        Level = LogLevel.Warning,
        Message = "Append to audit chain '{ChainKey}' lost a sequence race on attempt {Attempt} of {MaxAttempts}; retrying.")]
    public static partial void SequenceConflictRetried(ILogger logger, string chainKey, int attempt, int maxAttempts);

    [LoggerMessage(
        EventId = 6403,
        Level = LogLevel.Error,
        Message = "Append to audit chain '{ChainKey}' failed after {MaxAttempts} sequence-conflict retries.")]
    public static partial void SequenceConflictExhausted(ILogger logger, string chainKey, int maxAttempts);

    [LoggerMessage(
        EventId = 6404,
        Level = LogLevel.Error,
        Message = "Chain verification for '{ChainKey}' found a break at sequence {Sequence}: {Reason}.")]
    public static partial void ChainVerificationBroken(ILogger logger, string chainKey, long sequence, string reason);

    [LoggerMessage(
        EventId = 6405,
        Level = LogLevel.Error,
        Message = "Recording the audit entry for a faulted/rejected command failed on chain '{ChainKey}'; the business transaction, if any, still rolls back independently of this failure.")]
    public static partial void FailureAuditWriteFailed(ILogger logger, Exception exception, string chainKey);
}
