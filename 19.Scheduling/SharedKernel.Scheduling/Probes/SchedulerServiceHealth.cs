namespace SharedKernel.Scheduling.Probes;

/// <summary>
/// A point-in-time readiness snapshot returned by <see cref="ISchedulerServiceProbe.ProbeAsync"/>.
/// </summary>
/// <remarks>
/// This is a probe primitive, not a health check — <c>19.Scheduling</c> ships no <c>IHealthCheck</c>
/// implementation. Wiring this into <c>AddHealthChecks()</c> is <c>13.ServiceDefaults</c>'s
/// responsibility (<c>AddSchedulerReadinessCheck</c>).
/// </remarks>
public sealed record SchedulerServiceHealth
{
    /// <summary>Gets a value indicating whether the hosted scheduling loop is currently running.</summary>
    public required bool IsRunning { get; init; }

    /// <summary>Gets the number of jobs registered at startup.</summary>
    public required int RegisteredJobCount { get; init; }

    /// <summary>
    /// Gets the UTC time of the hosted loop's most recent tick, or <see langword="null"/> if it has
    /// not ticked yet (e.g. immediately after startup, before the first <c>TickInterval</c> elapses).
    /// </summary>
    public DateTimeOffset? LastTickUtc { get; init; }
}
