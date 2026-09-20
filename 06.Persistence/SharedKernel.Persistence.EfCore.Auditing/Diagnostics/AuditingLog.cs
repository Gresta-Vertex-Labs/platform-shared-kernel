using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.Persistence.EfCore.Auditing</c>.
/// </summary>
/// <remarks>
/// Claims EventId sub-block <c>6400-6499</c> (<see cref="LoggingEventIdRanges.Persistence"/> + 400..499)
/// — the next unclaimed 100-wide slot after <c>SharedKernel.Persistence.EfCore</c> (6000-6099),
/// <c>.Npgsql</c> (6100-6199), <c>.Dapper</c> (6200-6299), and <c>.EfCore.Encryption</c> (6300-6399), all
/// within <c>01.Core</c>'s <see cref="LoggingEventIdRanges.Persistence"/> (6000-6999) base. Every
/// <c>EventId</c> below is expressed relative to that registry constant, never as a bare numeric
/// literal — see the root brain's "never invent an ad hoc numeric range" rule.
/// </remarks>
internal static partial class AuditingLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 400,
        Level = LogLevel.Warning,
        Message = "No IAdvisoryTransactionLock is registered — concurrent appends to the same audit chain rely solely on unique-constraint retry, not a per-chain lock. Register SharedKernel.Persistence.Npgsql's NpgsqlAdvisoryTransactionLock for stronger serialization.")]
    public static partial void NoAdvisoryTransactionLockRegistered(ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 401,
        Level = LogLevel.Information,
        Message = "RecordAsync for chain '{ChainKey}' returned the existing record for idempotency key '{IdempotencyKey}' instead of appending a duplicate.")]
    public static partial void IdempotentDuplicateReturned(ILogger logger, string chainKey, string idempotencyKey);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 402,
        Level = LogLevel.Warning,
        Message = "Append to audit chain '{ChainKey}' lost a sequence race on attempt {Attempt} of {MaxAttempts}; retrying.")]
    public static partial void SequenceConflictRetried(ILogger logger, string chainKey, int attempt, int maxAttempts);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 403,
        Level = LogLevel.Error,
        Message = "Append to audit chain '{ChainKey}' failed after {MaxAttempts} sequence-conflict retries.")]
    public static partial void SequenceConflictExhausted(ILogger logger, string chainKey, int maxAttempts);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 404,
        Level = LogLevel.Error,
        Message = "Chain verification for '{ChainKey}' found a break at sequence {Sequence}: {Reason}.")]
    public static partial void ChainVerificationBroken(ILogger logger, string chainKey, long sequence, string reason);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 405,
        Level = LogLevel.Error,
        Message = "Recording the audit entry for a faulted/rejected command failed on chain '{ChainKey}'; the business transaction, if any, still rolls back independently of this failure.")]
    public static partial void FailureAuditWriteFailed(ILogger logger, Exception exception, string chainKey);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 406,
        Level = LogLevel.Error,
        Message = "Acquiring the per-chain advisory lock (or writing the record) for '{ChainKey}' on this writer's OWN connection/transaction timed out after {Timeout}. A concurrently-open AMBIENT transaction holding the SAME chain's lock, from the same logical request, is a common cause — see EfAuditTrailWriter's and AuditChainOptions.AdvisoryLockTimeout's remarks.")]
    public static partial void AdvisoryLockTimedOut(ILogger logger, string chainKey, TimeSpan timeout);
}
