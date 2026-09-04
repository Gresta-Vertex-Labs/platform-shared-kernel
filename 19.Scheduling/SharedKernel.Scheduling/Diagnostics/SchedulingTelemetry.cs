using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// The single <see cref="ActivitySource"/> and companion <see cref="Meter"/> this package uses for
/// every fire/skip/misfire/overlap/lock-acquisition-failed event.
/// </summary>
/// <remarks>
/// Both are process-lifetime singletons, matching the standard OpenTelemetry-in-.NET pattern (a
/// <c>static readonly</c> instance per logical component, registered with the host's tracer/meter
/// provider by the consuming service via <c>AddSource</c>/<c>AddMeter</c> with the matching name — this
/// package performs no OTel SDK wiring of its own).
/// </remarks>
internal static class SchedulingTelemetry
{
    /// <summary>The name shared by <see cref="ActivitySource"/> and <see cref="Meter"/>.</summary>
    public const string Name = "SharedKernel.Scheduling";

    /// <summary>The <see cref="System.Diagnostics.ActivitySource"/> for this package's spans.</summary>
    public static readonly ActivitySource ActivitySource = new(Name);

    /// <summary>The <see cref="System.Diagnostics.Metrics.Meter"/> for this package's counters.</summary>
    public static readonly Meter Meter = new(Name);

    /// <summary>Number of job execution attempts started (an on-time fire or a misfire catch-up run).</summary>
    public static readonly Counter<long> FireCount =
        Meter.CreateCounter<long>("scheduling.job.fired", unit: "{execution}", description: "Number of job execution attempts started.");

    /// <summary>Number of job executions that completed with a successful <c>Result</c>.</summary>
    public static readonly Counter<long> SucceededCount =
        Meter.CreateCounter<long>("scheduling.job.succeeded", unit: "{execution}", description: "Number of job executions that completed successfully.");

    /// <summary>Number of job executions that completed with a failed <c>Result</c> or an unhandled exception.</summary>
    public static readonly Counter<long> FailedCount =
        Meter.CreateCounter<long>("scheduling.job.failed", unit: "{execution}", description: "Number of job executions that failed or threw.");

    /// <summary>Number of ticks discarded under <see cref="Policies.OverlapPolicy.Skip"/>.</summary>
    public static readonly Counter<long> SkippedOverlapCount =
        Meter.CreateCounter<long>("scheduling.job.overlap_skipped", unit: "{tick}", description: "Number of ticks discarded because the previous execution was still running.");

    /// <summary>Number of ticks queued under <see cref="Policies.OverlapPolicy.Queue"/>.</summary>
    public static readonly Counter<long> QueuedOverlapCount =
        Meter.CreateCounter<long>("scheduling.job.overlap_queued", unit: "{tick}", description: "Number of ticks queued because the previous execution was still running.");

    /// <summary>Number of detected misfires (a tick observed later than one tick interval past its due time).</summary>
    public static readonly Counter<long> MisfireCount =
        Meter.CreateCounter<long>("scheduling.job.misfired", unit: "{occurrence}", description: "Number of detected misfires.");

    /// <summary>Number of ticks where the per-tick distributed lock could not be acquired (another replica is executing).</summary>
    public static readonly Counter<long> LockAcquisitionFailedCount =
        Meter.CreateCounter<long>("scheduling.lock.acquisition_failed", unit: "{attempt}", description: "Number of ticks where the distributed lock was held by another replica.");
}
