using System.Diagnostics.Metrics;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// The instruments of the audit ledger, created from the one persistence <see cref="Meter"/>
/// (<c>SharedKernel.Persistence</c>) and named <c>sharedkernel.persistence.audit.*</c>. Durations are in seconds.
/// </summary>
/// <remarks>
/// The meter and the activity source are shared with the EF Core package through <c>InternalsVisibleTo</c>, so a host
/// subscribes once. No instrument is tagged with a tenant, an actor, a record id or a key.
/// </remarks>
internal static class AuditingInstruments
{
    /// <summary>Histogram (<c>s</c>): duration of a request-path append.</summary>
    internal const string AppendDurationInstrument = "sharedkernel.persistence.audit.append.duration";

    /// <summary>Counter (<c>{append}</c>): appends that returned the record already stored under the same idempotency key.</summary>
    internal const string IdempotentDuplicateInstrument = "sharedkernel.persistence.audit.append.idempotent_duplicates";

    /// <summary>Counter (<c>{record}</c>): records sealed into their chains.</summary>
    internal const string SealedRecordsInstrument = "sharedkernel.persistence.audit.seal.records";

    /// <summary>Histogram (<c>s</c>): duration of a sealing pass that sealed at least one record.</summary>
    internal const string SealDurationInstrument = "sharedkernel.persistence.audit.seal.duration";

    /// <summary>Histogram (<c>s</c>): age of the oldest record sealed by a pass (write-to-seal lag).</summary>
    internal const string SealLagInstrument = "sharedkernel.persistence.audit.seal.lag";

    /// <summary>Counter (<c>{verification}</c>): verifications that did not report intact, tagged with <see cref="PersistenceTagKeys.AuditFailureKind"/>.</summary>
    internal const string ChainVerificationFailureInstrument = "sharedkernel.persistence.audit.chain.verification_failures";

    /// <summary>Counter (<c>{checkpoint}</c>): checkpoints signed and stored.</summary>
    internal const string CheckpointsEmittedInstrument = "sharedkernel.persistence.audit.checkpoint.emitted";

    /// <summary>Counter (<c>{payload}</c>): audit payloads erased.</summary>
    internal const string PayloadsErasedInstrument = "sharedkernel.persistence.audit.payload.erased";

    private static readonly Histogram<double> AppendDuration =
        PersistenceMeter.Instance.CreateHistogram<double>(AppendDurationInstrument, unit: "s", description: "Duration of a request-path audit append.");

    private static readonly Counter<long> IdempotentDuplicates =
        PersistenceMeter.Instance.CreateCounter<long>(IdempotentDuplicateInstrument, unit: "{append}", description: "Appends answered with the record already stored under the same idempotency key.");

    private static readonly Counter<long> SealedRecords =
        PersistenceMeter.Instance.CreateCounter<long>(SealedRecordsInstrument, unit: "{record}", description: "Audit records sealed into their chains.");

    private static readonly Histogram<double> SealDuration =
        PersistenceMeter.Instance.CreateHistogram<double>(SealDurationInstrument, unit: "s", description: "Duration of a sealing pass.");

    private static readonly Histogram<double> SealLag =
        PersistenceMeter.Instance.CreateHistogram<double>(SealLagInstrument, unit: "s", description: "Age of the oldest record sealed by a pass.");

    private static readonly Counter<long> VerificationFailures =
        PersistenceMeter.Instance.CreateCounter<long>(ChainVerificationFailureInstrument, unit: "{verification}", description: "Verifications that did not report intact.");

    private static readonly Counter<long> CheckpointsEmitted =
        PersistenceMeter.Instance.CreateCounter<long>(CheckpointsEmittedInstrument, unit: "{checkpoint}", description: "Checkpoints signed and stored.");

    private static readonly Counter<long> PayloadsErased =
        PersistenceMeter.Instance.CreateCounter<long>(PayloadsErasedInstrument, unit: "{payload}", description: "Audit payloads erased.");

    internal static void RecordAppendDuration(double seconds) => AppendDuration.Record(seconds);

    internal static void RecordIdempotentDuplicate() => IdempotentDuplicates.Add(1);

    internal static void RecordSealPass(int sealedCount, double seconds, double oldestAgeSeconds)
    {
        SealedRecords.Add(sealedCount);
        SealDuration.Record(seconds);
        SealLag.Record(Math.Max(0, oldestAgeSeconds));
    }

    internal static void RecordVerificationFailure(AuditVerificationFailureKind kind) =>
        VerificationFailures.Add(1, new KeyValuePair<string, object?>(PersistenceTagKeys.AuditFailureKind, kind.ToString()));

    internal static void RecordCheckpointEmitted() => CheckpointsEmitted.Add(1);

    internal static void RecordPayloadsErased(int count) => PayloadsErased.Add(count);
}
