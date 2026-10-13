using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Persistence.EfCore.Auditing</c> sub-block of <c>06.Persistence</c>: EventIds 6700-6799.
/// </summary>
internal static class AuditingLoggingEventIds
{
    /// <summary>First id of the sub-block (6700).</summary>
    public const int Base = LoggingEventIdRanges.Persistence + (7 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>An append returned the record already stored for the same event.</summary>
    public const int IdempotentDuplicateReturned = Base + 0;

    /// <summary>Recording a failed-outcome audit entry failed.</summary>
    public const int FailureAuditWriteFailed = Base + 1;

    /// <summary>A chain verification did not report intact.</summary>
    public const int ChainVerificationFailed = Base + 2;

    /// <summary>A sealing pass completed.</summary>
    public const int SealPassCompleted = Base + 3;

    /// <summary>A sealing round failed.</summary>
    public const int SealRoundFailed = Base + 4;

    /// <summary>Another instance holds the sealer lock.</summary>
    public const int SealerLockHeldElsewhere = Base + 5;

    /// <summary>A checkpoint was signed and stored.</summary>
    public const int CheckpointEmitted = Base + 6;

    /// <summary>Emitting a checkpoint failed.</summary>
    public const int CheckpointEmissionFailed = Base + 7;

    /// <summary>The ledger self-check found a problem.</summary>
    public const int SelfCheckFinding = Base + 8;

    /// <summary>The ledger self-check failed and the host does not start.</summary>
    public const int SelfCheckFailed = Base + 9;

    /// <summary>The ledger self-check passed.</summary>
    public const int SelfCheckPassed = Base + 10;

    /// <summary>Audit payloads were erased.</summary>
    public const int PayloadsErased = Base + 11;

    /// <summary>Audit chains were resealed under a new key.</summary>
    public const int ChainsResealed = Base + 12;

    /// <summary>The background sealer started.</summary>
    public const int SealerStarted = Base + 13;
}
