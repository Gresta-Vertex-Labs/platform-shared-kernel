namespace SharedKernel.Scheduling.Policies;

/// <summary>
/// Governs what happens when a job's next tick becomes due while its previous execution is still
/// running <b>in this same process</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mandatory, never defaulted</b> — the same rationale as <see cref="MisfirePolicy"/>: registration
/// throws at call time if left unset.
/// </para>
/// <para>
/// <b>Per-process, in-memory only, and independent of and additional to cross-replica exclusivity.</b>
/// This policy guards a single job against overlapping <em>itself</em> within one running process. It
/// has nothing to do with, and does not substitute for, the optional distributed-lease-guarded
/// cross-replica exclusivity (Domain Invariant 3) — a service can be fully single-replica, have no
/// distributed lock configured at all, and still need an overlap policy the moment a job's own body
/// occasionally runs longer than its cron interval.
/// </para>
/// <para>Behavior, concretely:
/// <list type="bullet">
/// <item><description>
/// <see cref="Skip"/> — the newly-due tick is discarded; nothing runs for it. The job's next fire
/// time still advances normally — the skipped tick is never retried.
/// </description></item>
/// <item><description>
/// <see cref="Queue"/> — the newly-due tick is queued (FIFO) rather than started immediately. The
/// moment the in-flight execution completes, the oldest queued tick starts, and so on until the queue
/// drains — at most one execution of this job runs at a time, but no due tick is silently dropped.
/// </description></item>
/// <item><description>
/// <see cref="Allow"/> — the newly-due tick starts a fully concurrent execution alongside the one
/// still running, with no coordination between them. Only appropriate for job bodies that are already
/// safe to run concurrently against themselves (e.g. because they are naturally idempotent, or because
/// the command handler itself acquires a resource-level lock).
/// </description></item>
/// </list>
/// </para>
/// </remarks>
public enum OverlapPolicy
{
    /// <summary>Discard the newly-due tick; the still-running execution is left alone.</summary>
    Skip,

    /// <summary>Queue the newly-due tick to run immediately after the in-flight execution completes.</summary>
    Queue,

    /// <summary>Start a new, fully concurrent execution regardless of the still-running one.</summary>
    Allow,
}
