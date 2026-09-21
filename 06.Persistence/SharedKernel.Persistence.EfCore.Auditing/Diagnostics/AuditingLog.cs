using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> statements of <c>SharedKernel.Persistence.EfCore.Auditing</c>.
/// </summary>
/// <remarks>
/// EventIds <c>6700-6899</c> (<see cref="LoggingEventIdRanges.Persistence"/> + 700..899), the sub-block
/// assigned to this package by P-558 (it previously used 6400-6499, now the Dapper package's). A chain's
/// tenant is logged as <c>ChainTenant</c> — it names the chain being processed, not the ambient caller.
/// </remarks>
internal static partial class AuditingLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 700,
        Level = LogLevel.Information,
        Message = "Audit append for resource type '{ResourceType}' returned the record already stored under idempotency key '{IdempotencyKey}'.")]
    public static partial void IdempotentDuplicateReturned(ILogger logger, string resourceType, string idempotencyKey);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 701,
        Level = LogLevel.Error,
        Message = "Recording a failed-outcome audit entry for resource type '{ResourceType}' failed; the failure is not in the ledger.")]
    public static partial void FailureAuditWriteFailed(ILogger logger, Exception exception, string resourceType);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 702,
        Level = LogLevel.Error,
        Message = "Audit chain ({ChainTenant}, '{ResourceType}') verified as {Status}: {FailureKind} at sequence {Sequence} ({Reason}).")]
    public static partial void ChainVerificationFailed(
        ILogger logger, string chainTenant, string resourceType, AuditVerificationStatus status, AuditVerificationFailureKind failureKind, long? sequence, string? reason);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 703,
        Level = LogLevel.Debug,
        Message = "Audit sealing pass sealed {RecordCount} record(s) in {ElapsedMilliseconds} ms.")]
    public static partial void SealPassCompleted(ILogger logger, int recordCount, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 704,
        Level = LogLevel.Error,
        Message = "Audit sealing round failed; it is retried after the sealer interval.")]
    public static partial void SealRoundFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 705,
        Level = LogLevel.Debug,
        Message = "Another instance holds the audit sealer lock; this instance skips the round.")]
    public static partial void SealerLockHeldElsewhere(ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 706,
        Level = LogLevel.Information,
        Message = "Checkpoint of audit chain ({ChainTenant}, '{ResourceType}') at sequence {Sequence} signed with key '{SigningKeyId}'.")]
    public static partial void CheckpointEmitted(ILogger logger, string chainTenant, string resourceType, long sequence, string signingKeyId);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 707,
        Level = LogLevel.Error,
        Message = "Checkpoint emission for audit chain ({ChainTenant}, '{ResourceType}') failed.")]
    public static partial void CheckpointEmissionFailed(ILogger logger, Exception exception, string chainTenant, string resourceType);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 708,
        Level = LogLevel.Warning,
        Message = "Audit ledger self-check: {Finding}")]
    public static partial void SelfCheckFinding(ILogger logger, string finding);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 709,
        Level = LogLevel.Critical,
        Message = "Audit ledger self-check found {FindingCount} problem(s) and SelfCheck is Fail; the host will not start.")]
    public static partial void SelfCheckFailed(ILogger logger, int findingCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 710,
        Level = LogLevel.Information,
        Message = "Audit ledger self-check passed.")]
    public static partial void SelfCheckPassed(ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 711,
        Level = LogLevel.Information,
        Message = "Erased {PayloadCount} audit payload(s) of resource type '{ResourceType}'.")]
    public static partial void PayloadsErased(ILogger logger, int payloadCount, string resourceType);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 712,
        Level = LogLevel.Warning,
        Message = "Resealed {ChainCount} audit chain(s) under key '{KeyId}' ({RecordsSealed} record(s) sealed, {CheckpointCount} checkpoint(s) emitted).")]
    public static partial void ChainsResealed(ILogger logger, int chainCount, string keyId, int recordsSealed, int checkpointCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 713,
        Level = LogLevel.Information,
        Message = "Audit sealer started (interval {Interval}, batch size {BatchSize}).")]
    public static partial void SealerStarted(ILogger logger, TimeSpan interval, int batchSize);
}
