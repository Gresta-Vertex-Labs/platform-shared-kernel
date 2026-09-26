namespace SharedKernel.Scheduling.Probes;

/// <summary>The readiness probe <c>AddSharedKernelScheduling</c> registers.</summary>
/// <remarks>
/// Resolve it with <c>GetRequiredReadinessProbe(SchedulerReadiness.ProbeName)</c>. It is healthy while the scheduling
/// loop runs, reads only in-process state (never I/O), and carries the loop's state under the data keys below.
/// </remarks>
public static class SchedulerReadiness
{
    /// <summary>The probe's name.</summary>
    public const string ProbeName = "scheduler";

    /// <summary>Data key: whether the scheduling loop is running (<see cref="bool"/>).</summary>
    public const string IsRunningKey = "IsRunning";

    /// <summary>Data key: the number of registered jobs (<see cref="int"/>).</summary>
    public const string RegisteredJobCountKey = "RegisteredJobCount";

    /// <summary>Data key: when the loop last ticked (<see cref="DateTimeOffset"/>); absent before the first tick.</summary>
    public const string LastTickUtcKey = "LastTickUtc";
}
