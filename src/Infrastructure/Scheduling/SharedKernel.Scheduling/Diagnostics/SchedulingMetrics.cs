using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// Holds the instruments of this package, built from <see cref="IMeterFactory"/> so their lifetime belongs to
/// the host's container and not to the process.
/// </summary>
/// <remarks>
/// Registered by <c>AddSharedKernelScheduling</c> together with <c>services.AddMetrics()</c>, which makes an
/// <see cref="IMeterFactory"/> resolvable. Durations are seconds measured by the caller with <c>IClock</c>.
/// </remarks>
internal sealed class SchedulingMetrics
{
    internal const string ExecutionsName = "sharedkernel.scheduling.job.executions";
    internal const string DurationName = "sharedkernel.scheduling.job.duration";
    internal const string OverlapSkippedName = "sharedkernel.scheduling.job.overlap_skipped";
    internal const string OverlapQueuedName = "sharedkernel.scheduling.job.overlap_queued";
    internal const string MisfiredName = "sharedkernel.scheduling.job.misfired";
    internal const string LockNotAcquiredName = "sharedkernel.scheduling.lock.not_acquired";
    internal const string LockStoreUnavailableName = "sharedkernel.scheduling.lock.store_unavailable";

    private readonly Counter<long> _executions;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _overlapSkipped;
    private readonly Counter<long> _overlapQueued;
    private readonly Counter<long> _misfired;
    private readonly Counter<long> _lockNotAcquired;
    private readonly Counter<long> _lockStoreUnavailable;

    /// <summary>Initialises the instruments.</summary>
    /// <param name="meterFactory">The factory that owns the meter.</param>
    public SchedulingMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        Meter meter = meterFactory.Create(new MeterOptions(SchedulingTelemetry.Name)
        {
            Version = TelemetryVersion.Of(typeof(SchedulingTelemetry).Assembly),
        });

        _executions = meter.CreateCounter<long>(
            ExecutionsName, unit: "{execution}", description: "Number of job executions that ran the command, by outcome.");
        _duration = meter.CreateHistogram<double>(
            DurationName, unit: "s", description: "Duration, in seconds, of a job execution from claim to completion.");
        _overlapSkipped = meter.CreateCounter<long>(
            OverlapSkippedName, unit: "{occurrence}", description: "Number of occurrences discarded because the previous execution was still running.");
        _overlapQueued = meter.CreateCounter<long>(
            OverlapQueuedName, unit: "{occurrence}", description: "Number of occurrences queued because the previous execution was still running.");
        _misfired = meter.CreateCounter<long>(
            MisfiredName, unit: "{occurrence}", description: "Number of detected misfires.");
        _lockNotAcquired = meter.CreateCounter<long>(
            LockNotAcquiredName, unit: "{claim}", description: "Number of occurrence claims lost to another replica.");
        _lockStoreUnavailable = meter.CreateCounter<long>(
            LockStoreUnavailableName, unit: "{claim}", description: "Number of occurrence claims abandoned because the lock store could not be reached.");
    }

    /// <summary>Counts one execution and records its duration, both tagged with the job and the outcome.</summary>
    public void RecordExecution(string jobName, string outcome, double seconds)
    {
        var tags = new TagList
        {
            { SchedulingTagKeys.JobName, jobName },
            { WellKnownTagKeys.Outcome, outcome },
        };
        _executions.Add(1, tags);
        _duration.Record(seconds, tags);
    }

    /// <summary>Counts an occurrence discarded under <c>OverlapPolicy.Skip</c>.</summary>
    public void RecordOverlapSkipped(string jobName) => _overlapSkipped.Add(1, JobTag(jobName));

    /// <summary>Counts an occurrence queued under <c>OverlapPolicy.Queue</c>.</summary>
    public void RecordOverlapQueued(string jobName) => _overlapQueued.Add(1, JobTag(jobName));

    /// <summary>Counts a detected misfire.</summary>
    public void RecordMisfire(string jobName) => _misfired.Add(1, JobTag(jobName));

    /// <summary>Counts a claim lost to another replica.</summary>
    public void RecordLockNotAcquired(string jobName) => _lockNotAcquired.Add(1, JobTag(jobName));

    /// <summary>Counts a claim abandoned because the lock store was unreachable.</summary>
    public void RecordLockStoreUnavailable(string jobName) => _lockStoreUnavailable.Add(1, JobTag(jobName));

    private static KeyValuePair<string, object?> JobTag(string jobName) => new(SchedulingTagKeys.JobName, jobName);
}
