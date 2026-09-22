using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// The telemetry names of <c>SharedKernel.Persistence.EfCore.Auditing</c>: one <see cref="Meter"/> and one
/// <see cref="ActivitySource"/>, both named <see cref="MeterName"/>.
/// </summary>
/// <remarks>
/// Public constants so a host can register them (<c>AddMeter</c>/<c>AddSource</c>) without referencing
/// this package's internals.
/// </remarks>
public static class AuditingMeter
{
    /// <summary>The meter name, as registered with OpenTelemetry.</summary>
    public const string MeterName = "SharedKernel.Persistence.EfCore.Auditing";

    /// <summary>The activity source name (seal passes, checkpoint emission, chain verification).</summary>
    public const string ActivitySourceName = MeterName;

    /// <summary>Histogram: duration of a request-path append, milliseconds.</summary>
    internal const string AppendDurationInstrument = "audit.append.duration";

    /// <summary>Counter: appends that returned the record already stored under the same idempotency key.</summary>
    internal const string IdempotentDuplicateInstrument = "audit.append.idempotent_duplicates";

    /// <summary>Counter: records sealed into their chains.</summary>
    internal const string SealedRecordsInstrument = "audit.seal.records";

    /// <summary>Histogram: duration of a sealing pass that sealed at least one record, milliseconds.</summary>
    internal const string SealDurationInstrument = "audit.seal.duration";

    /// <summary>Histogram: age of the oldest record sealed by a pass (write-to-seal lag), seconds.</summary>
    internal const string SealLagInstrument = "audit.seal.lag";

    /// <summary>Counter: verifications that did not report intact, tagged with <see cref="FailureKindTag"/>.</summary>
    internal const string ChainVerificationFailureInstrument = "audit.chain.verification_failures";

    /// <summary>Counter: checkpoints signed and stored.</summary>
    internal const string CheckpointsEmittedInstrument = "audit.checkpoint.emitted";

    /// <summary>Counter: payloads erased.</summary>
    internal const string PayloadsErasedInstrument = "audit.payload.erased";

    /// <summary>The tag carrying the <see cref="AuditVerificationFailureKind"/> on <see cref="ChainVerificationFailureInstrument"/>.</summary>
    internal const string FailureKindTag = "audit.failure_kind";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> AppendDuration =
        Meter.CreateHistogram<double>(AppendDurationInstrument, unit: "ms", description: "Duration of a request-path audit append.");

    private static readonly Counter<long> IdempotentDuplicates =
        Meter.CreateCounter<long>(IdempotentDuplicateInstrument, description: "Appends answered with the record already stored under the same idempotency key.");

    private static readonly Counter<long> SealedRecords =
        Meter.CreateCounter<long>(SealedRecordsInstrument, description: "Audit records sealed into their chains.");

    private static readonly Histogram<double> SealDuration =
        Meter.CreateHistogram<double>(SealDurationInstrument, unit: "ms", description: "Duration of a sealing pass.");

    private static readonly Histogram<double> SealLag =
        Meter.CreateHistogram<double>(SealLagInstrument, unit: "s", description: "Age of the oldest record sealed by a pass.");

    private static readonly Counter<long> VerificationFailures =
        Meter.CreateCounter<long>(ChainVerificationFailureInstrument, description: "Verifications that did not report intact.");

    private static readonly Counter<long> CheckpointsEmitted =
        Meter.CreateCounter<long>(CheckpointsEmittedInstrument, description: "Checkpoints signed and stored.");

    private static readonly Counter<long> PayloadsErased =
        Meter.CreateCounter<long>(PayloadsErasedInstrument, description: "Audit payloads erased.");

    internal static void RecordAppendDuration(double milliseconds) => AppendDuration.Record(milliseconds);

    internal static void RecordIdempotentDuplicate() => IdempotentDuplicates.Add(1);

    internal static void RecordSealPass(int sealedCount, double milliseconds, double oldestAgeSeconds)
    {
        SealedRecords.Add(sealedCount);
        SealDuration.Record(milliseconds);
        SealLag.Record(Math.Max(0, oldestAgeSeconds));
    }

    internal static void RecordVerificationFailure(AuditVerificationFailureKind kind) =>
        VerificationFailures.Add(1, new KeyValuePair<string, object?>(FailureKindTag, kind.ToString()));

    internal static void RecordCheckpointEmitted() => CheckpointsEmitted.Add(1);

    internal static void RecordPayloadsErased(int count) => PayloadsErased.Add(count);
}
