using Microsoft.Extensions.Logging;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// Log statements of <c>SharedKernel.Persistence.EfCore.Auditing</c>. No tenant id, idempotency key or actor ever reaches
/// a log: a chain is named by its resource type only, and a problem is a closed set, never composed text.
/// </summary>
internal static partial class AuditingLog
{
    [LoggerMessage(
        EventId = AuditingLoggingEventIds.IdempotentDuplicateReturned,
        Level = LogLevel.Debug,
        Message = "Audit append for resource type '{ResourceType}' returned the record already stored for the same event.")]
    public static partial void IdempotentDuplicateReturned(ILogger logger, string resourceType);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.FailureAuditWriteFailed,
        Level = LogLevel.Error,
        Message = "Recording a failed-outcome audit entry for resource type '{ResourceType}' failed, so the failure is not in the ledger.")]
    public static partial void FailureAuditWriteFailed(ILogger logger, string resourceType, Exception exception);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.ChainVerificationFailed,
        Level = LogLevel.Error,
        Message = "Audit chain of resource type '{ResourceType}' verified as {Status} with failure kind {FailureKind} at sequence {Sequence}.")]
    public static partial void ChainVerificationFailed(
        ILogger logger, string resourceType, AuditVerificationStatus status, AuditVerificationFailureKind failureKind, long? sequence);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.SealPassCompleted,
        Level = LogLevel.Debug,
        Message = "Audit sealing pass sealed {RecordCount} records in {ElapsedMilliseconds} ms.")]
    public static partial void SealPassCompleted(ILogger logger, int recordCount, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.SealRoundFailed,
        Level = LogLevel.Error,
        Message = "Audit sealing round failed and is retried after the sealer interval.")]
    public static partial void SealRoundFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.SealerLockHeldElsewhere,
        Level = LogLevel.Debug,
        Message = "Another instance holds the audit sealer lock, so this instance skips the round.")]
    public static partial void SealerLockHeldElsewhere(ILogger logger);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.CheckpointEmitted,
        Level = LogLevel.Debug,
        Message = "Checkpoint of the audit chain of resource type '{ResourceType}' was signed at sequence {Sequence} with key '{SigningKeyId}'.")]
    public static partial void CheckpointEmitted(ILogger logger, string resourceType, long sequence, string signingKeyId);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.CheckpointEmissionFailed,
        Level = LogLevel.Error,
        Message = "Checkpoint emission for the audit chain of resource type '{ResourceType}' failed.")]
    public static partial void CheckpointEmissionFailed(ILogger logger, string resourceType, Exception exception);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.SelfCheckFinding,
        Level = LogLevel.Warning,
        Message = "The audit ledger self-check found the cause {Cause}.")]
    public static partial void SelfCheckFinding(ILogger logger, AuditSelfCheckProblem cause);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.SelfCheckFailed,
        Level = LogLevel.Critical,
        Message = "The audit ledger self-check found {FindingCount} problems and SelfCheck is Fail, so the host does not start.")]
    public static partial void SelfCheckFailed(ILogger logger, int findingCount);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.SelfCheckPassed,
        Level = LogLevel.Information,
        Message = "The audit ledger self-check passed.")]
    public static partial void SelfCheckPassed(ILogger logger);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.PayloadsErased,
        Level = LogLevel.Information,
        Message = "Erased {PayloadCount} audit payloads of resource type '{ResourceType}'.")]
    public static partial void PayloadsErased(ILogger logger, int payloadCount, string resourceType);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.ChainsResealed,
        Level = LogLevel.Warning,
        Message = "Resealed {ChainCount} audit chains under key '{KeyId}' with {RecordsSealed} records sealed and {CheckpointCount} checkpoints emitted.")]
    public static partial void ChainsResealed(ILogger logger, int chainCount, string keyId, int recordsSealed, int checkpointCount);

    [LoggerMessage(
        EventId = AuditingLoggingEventIds.SealerStarted,
        Level = LogLevel.Information,
        Message = "Audit sealer started with interval {Interval} and batch size {BatchSize}.")]
    public static partial void SealerStarted(ILogger logger, TimeSpan interval, int batchSize);
}

/// <summary>What the audit ledger self-check found: a closed set, so a log carries no role name or composed text.</summary>
internal enum AuditSelfCheckProblem
{
    /// <summary>A checked role is a superuser and can disable the ledger triggers.</summary>
    RoleIsSuperuser,

    /// <summary>A ledger table does not exist.</summary>
    TableMissing,

    /// <summary>A checked role owns a ledger table, or is a member of its owner.</summary>
    RoleOwnsTable,

    /// <summary>A checked role holds UPDATE on a ledger table.</summary>
    RoleHasUpdate,

    /// <summary>A checked role holds DELETE on an insert-only ledger table.</summary>
    RoleHasDelete,

    /// <summary>A checked role holds TRUNCATE on a ledger table.</summary>
    RoleHasTruncate,

    /// <summary>Row-level security is enabled on a ledger table.</summary>
    RowLevelSecurityEnabled,

    /// <summary>The runtime role holds INSERT on a table only the separate sealer role writes.</summary>
    RuntimeCanInsertSealedTable,

    /// <summary>An append-only trigger is missing.</summary>
    TriggerMissing,

    /// <summary>An append-only trigger is not ENABLE ALWAYS.</summary>
    TriggerNotAlways,
}
