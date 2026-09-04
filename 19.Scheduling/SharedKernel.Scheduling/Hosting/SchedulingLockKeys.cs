namespace SharedKernel.Scheduling.Hosting;

/// <summary>
/// Builds the distributed-lock resource key for a single job occurrence's
/// <c>IDistributedLockService</c> claim. Centralized here rather than string-concatenated ad hoc at
/// each call site (SK0022-shaped magic-string discipline).
/// </summary>
/// <remarks>
/// <b>Deliberately keyed by (job name, scheduled fire time) — never job name alone.</b> This lock is a
/// CLAIM on one specific occurrence, acquired once and never proactively released (see
/// <c>SchedulingHostedService.ExecuteOneAsync</c>) — it expires naturally via its configured TTL. A
/// job-name-only key combined with releasing the lock immediately after execution finishes would open a
/// genuine cross-replica duplicate-execution window: replica B, evaluating the exact same due occurrence
/// slightly later than replica A (bounded only by inter-replica clock/loop skew, GC pauses, or network
/// jitter — nothing bounds this tightly in practice), would find the lock already released by A and
/// successfully re-acquire it, firing the same occurrence a second time. Scoping the key to the
/// occurrence's own scheduled fire time means the claim persists for its full TTL regardless of how
/// quickly the winning replica's execution finishes, closing that window — the standard pattern used by
/// every production-grade distributed cron implementation (Quartz's clustered <c>JobStore</c>, Hangfire,
/// db-scheduler) for exactly this reason.
/// </remarks>
internal static class SchedulingLockKeys
{
    private const string Prefix = "scheduling:occurrence:";

    /// <summary>Builds the lock resource key for one occurrence of the given job.</summary>
    public static string ForOccurrence(string jobName, DateTimeOffset scheduledFireTimeUtc) =>
        $"{Prefix}{jobName}:{scheduledFireTimeUtc.UtcTicks}";
}
