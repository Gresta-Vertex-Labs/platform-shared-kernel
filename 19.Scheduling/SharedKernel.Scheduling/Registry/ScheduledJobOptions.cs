using SharedKernel.Scheduling.Policies;

namespace SharedKernel.Scheduling.Registry;

/// <summary>
/// Per-job registration options supplied to the <c>configure</c> delegate of
/// <see cref="IScheduledJobRegistry.AddRecurring{TCommand}"/>/<see cref="IScheduledJobRegistry.AddDeferred{TCommand}"/>.
/// </summary>
/// <remarks>
/// <see cref="MisfirePolicy"/> and <see cref="OverlapPolicy"/> are the two members that matter most:
/// both are mandatory and never defaulted. Registration throws an <see cref="ArgumentException"/> at
/// the <c>AddRecurring</c>/<c>AddDeferred</c> call site — not lazily, at host startup, and not silently
/// at the first missed tick — when either is left <see langword="null"/> after the <c>configure</c>
/// delegate runs.
/// </remarks>
public sealed class ScheduledJobOptions
{
    /// <summary>
    /// Gets or sets the misfire policy. Mandatory — see the remarks on
    /// <see cref="ScheduledJobOptions"/> and on <see cref="Policies.MisfirePolicy"/> itself.
    /// </summary>
    public MisfirePolicy? MisfirePolicy { get; set; }

    /// <summary>
    /// Gets or sets the overlap policy. Mandatory — see the remarks on
    /// <see cref="ScheduledJobOptions"/> and on <see cref="Policies.OverlapPolicy"/> itself.
    /// </summary>
    public OverlapPolicy? OverlapPolicy { get; set; }

    /// <summary>
    /// Gets or sets an optional, purely informational tenant label for this job registration. See the
    /// remarks on <see cref="TenantScope"/> for why this is nullable and carries no isolation
    /// enforcement.
    /// </summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>
    /// Gets or sets the TTL for this job's per-occurrence distributed-lock claim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a claim TTL, not a critical-section hold duration: the lock for one occurrence is
    /// acquired and then deliberately never released early (see <c>SchedulingLockKeys</c>'s remarks
    /// for why) — it always expires naturally after this duration. Set it comfortably longer than the
    /// worst-case skew between replicas' clock/tick-loop observations (network jitter, GC pauses,
    /// Redis round-trip time), but short enough not to accumulate excessive claim keys on the lock
    /// backend for a very frequently-firing job — a job firing every few seconds should set an
    /// explicit, shorter value here rather than accept a multi-minute default.
    /// </para>
    /// <para>
    /// When <see langword="null"/> (the default), <c>SchedulingOptions.DefaultLockExpiry</c> is used
    /// instead. Only meaningful when an <c>IDistributedLockService</c> is registered — see Domain
    /// Invariant 3 (single-replica-without-lock is a supported, loudly-warned mode).
    /// </para>
    /// </remarks>
    public TimeSpan? LockExpiry { get; set; }
}
