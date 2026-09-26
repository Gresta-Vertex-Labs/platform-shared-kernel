using SharedKernel.Execution.Tenancy;
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
    /// Gets or sets the tenant this job runs as. Defaults to <see cref="TenantScope.Global"/>, meaning a
    /// system-level job with no tenant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Optional here, unlike every other tenant-aware domain.</b> <c>09.Search</c>,
    /// <c>10.Intelligence</c>, <c>17.Workflows</c> and <c>18.Idempotency</c> take a mandatory,
    /// non-defaulted tenant on every operation. A scheduled job is registered <b>once, at startup, as a
    /// system-level actor</b>, so <see cref="TenantScope.Global"/> is the normal value.
    /// </para>
    /// <para>
    /// A per-tenant recurring job (for example, "send each active tenant's weekly digest") is one
    /// system-level registration whose command handler iterates its own tenant directory; the scheduler
    /// never fans out per-tenant executions. A tenant set here is for the rare job owned by a single tenant.
    /// </para>
    /// <para>
    /// <b>The scope is the job's caller tenant, not a label.</b> Every execution runs under an ambient
    /// <c>SystemRequestContext</c> (identity = the job name, no permissions, a new correlation id) whose
    /// <c>IRequestContext.TenantId</c> is this scope's tenant — <see langword="null"/> for
    /// <see cref="TenantScope.Global"/>. Persistence filters, guards and stamps rows by that tenant, and
    /// the outbound calls, messages and workflows the job starts carry it. A
    /// <see cref="TenantScope.Global"/> job that touches tenant-scoped data therefore fails closed unless it
    /// enters a cross-tenant scope (<c>ICrossTenantScope</c>). The same value is surfaced on
    /// <see cref="Jobs.ScheduledJobExecutionContext.TenantScope"/>.
    /// </para>
    /// </remarks>
    public TenantScope TenantScope { get; set; }

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
