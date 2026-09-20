using System.Diagnostics.Metrics;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// The <see cref="System.Diagnostics.Metrics.Meter"/> for <c>SharedKernel.Persistence.EfCore.Auditing</c>.
/// </summary>
/// <remarks>
/// A dedicated meter, mirroring <c>SharedKernel.Persistence.EfCore.Diagnostics.PersistenceMeter</c>'s
/// shape — public instrument-name constants so <c>13.ServiceDefaults</c>'s <c>WithPersistenceTelemetry</c>
/// can reference this meter by name without a compile-time reference to this package.
/// </remarks>
public static class AuditingMeter
{
    /// <summary>The meter's name, as registered with OpenTelemetry.</summary>
    public const string MeterName = "SharedKernel.Persistence.EfCore.Auditing";

    /// <summary>Histogram name: <c>IAuditTrailWriter.RecordAsync</c>'s duration, milliseconds.</summary>
    public const string AppendDurationInstrument = "audit.append.duration";

    /// <summary>Counter name: a <c>RecordAsync</c> call that returned an existing record instead of appending (idempotency-key retry-safety).</summary>
    public const string IdempotentDuplicateInstrument = "audit.append.idempotent_duplicates";

    /// <summary>Counter name: a <c>RecordAsync</c> attempt that lost a sequence race and retried.</summary>
    public const string SequenceConflictRetryInstrument = "audit.append.sequence_conflicts";

    /// <summary>Counter name: a chain-verification call (<c>VerifyFullChainAsync</c>/<c>VerifyChainFromCheckpointAsync</c>) that found a break.</summary>
    public const string ChainVerificationFailureInstrument = "audit.chain.verification_failures";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> AppendDuration =
        Meter.CreateHistogram<double>(AppendDurationInstrument, unit: "ms", description: "Duration of IAuditTrailWriter.RecordAsync, milliseconds.");

    private static readonly Counter<long> IdempotentDuplicates =
        Meter.CreateCounter<long>(IdempotentDuplicateInstrument, description: "RecordAsync calls that returned an already-persisted record instead of appending.");

    private static readonly Counter<long> SequenceConflictRetries =
        Meter.CreateCounter<long>(SequenceConflictRetryInstrument, description: "RecordAsync attempts that lost a per-chain sequence race and retried.");

    private static readonly Counter<long> ChainVerificationFailures =
        Meter.CreateCounter<long>(ChainVerificationFailureInstrument, description: "Chain-verification calls that reported a broken chain.");

    /// <summary>Records the duration of a completed <c>RecordAsync</c> call.</summary>
    public static void RecordAppendDuration(double milliseconds) => AppendDuration.Record(milliseconds);

    /// <summary>Increments the idempotent-duplicate counter.</summary>
    public static void RecordIdempotentDuplicate() => IdempotentDuplicates.Add(1);

    /// <summary>Increments the sequence-conflict-retry counter.</summary>
    public static void RecordSequenceConflictRetry() => SequenceConflictRetries.Add(1);

    /// <summary>Increments the chain-verification-failure counter.</summary>
    public static void RecordChainVerificationFailure() => ChainVerificationFailures.Add(1);
}
