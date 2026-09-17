using SharedKernel.Scheduling.Registry;

namespace SharedKernel.Scheduling.Jobs;

/// <summary>
/// The per-execution context handed to a scheduled job's command factory and, ultimately, available
/// to the job body itself for correlation and downstream fencing-token propagation.
/// </summary>
/// <remarks>
/// Constructed fresh by <c>SchedulingHostedService</c> for every single execution attempt (an on-time
/// fire, a misfire catch-up run, or a queued overlap run all get their own instance) — never reused or
/// mutated across executions.
/// </remarks>
public sealed record ScheduledJobExecutionContext
{
    /// <summary>Gets the registered job name this execution belongs to.</summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Gets the time this execution was originally scheduled to fire, per the job's cron expression or
    /// deferred fire-at time.
    /// </summary>
    /// <remarks>
    /// On an on-time fire this equals <see cref="ActualFireTimeUtc"/> to within one tick interval. On a
    /// misfire catch-up run, this is the earlier, originally-missed occurrence — see
    /// <see cref="Policies.MisfirePolicy"/>.
    /// </remarks>
    public required DateTimeOffset ScheduledFireTimeUtc { get; init; }

    /// <summary>Gets the time this execution actually started, sourced from <c>IClock.UtcNow</c>.</summary>
    public required DateTimeOffset ActualFireTimeUtc { get; init; }

    /// <summary>
    /// Gets the optional, purely informational tenant label carried from the job's
    /// <see cref="ScheduledJobOptions.TenantScope"/>. See that member's remarks for why this carries no
    /// isolation enforcement.
    /// </summary>
    public TenantScope? TenantScope { get; init; }

    /// <summary>
    /// Gets the fencing token of this occurrence's distributed lease, if any.
    /// </summary>
    /// <remarks>
    /// This is a propagation seam only — see <c>IDistributedLockService</c>'s own documentation for the standard
    /// usage contract. This package owns no protected resource of its own to re-check the token
    /// against; a job body that itself writes to a fencing-token-aware downstream resource is expected
    /// to pass this value along. <see langword="null"/> whenever no <c>IDistributedLockService</c> is
    /// registered (single-replica mode).
    /// </remarks>
    public long? FencingToken { get; init; }
}
