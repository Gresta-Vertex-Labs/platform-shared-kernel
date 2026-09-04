namespace SharedKernel.Scheduling.Policies;

/// <summary>
/// Governs what happens to a recurring or deferred job's scheduled occurrence when the hosted
/// scheduling loop was not actively ticking at the moment that occurrence was due — most commonly
/// because the process was down.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mandatory, never defaulted.</b> Registration (<c>IScheduledJobRegistry.AddRecurring</c>/
/// <c>.AddDeferred</c>) throws at call time if the <c>configure</c> delegate leaves
/// <c>ScheduledJobOptions.MisfirePolicy</c> unset — guessing on a team's behalf here is exactly how
/// duplicate reconciliation runs happen.
/// </para>
/// <para>
/// <b>Detection is in-process only, never against a persistent job store</b> (Domain Invariant 2 —
/// this package never adopts Quartz's <c>JobStore</c>). The hosted loop compares
/// <c>IClock.UtcNow</c> against its own in-memory record of a job's next-fire time at each tick; a
/// gap larger than the configured tick interval is treated as a misfire. A process restart therefore
/// re-anchors every job's schedule from "now" and cannot detect occurrences missed before the restart
/// — this is a deliberate consequence of shipping no durable store, not an oversight.
/// </para>
/// <para>
/// <b>How the three members differ, concretely</b> (see <c>SchedulingHostedService.HandleMisfire</c>
/// for the implementation this documents):
/// <list type="bullet">
/// <item><description>
/// <see cref="Skip"/> — the missed occurrence is discarded entirely. No catch-up execution happens.
/// The job's next fire time is recomputed as the next cron occurrence strictly after "now" (a
/// recurring job), or the job is marked terminal and never fires (a one-shot deferred job).
/// </description></item>
/// <item><description>
/// <see cref="FireOnce"/> — exactly one catch-up execution happens immediately, regardless of how
/// many occurrences were actually missed during the downtime window. After that single catch-up run,
/// the schedule jumps straight to the next cron occurrence after "now" — remaining missed occurrences
/// are never individually replayed.
/// </description></item>
/// <item><description>
/// <see cref="RunImmediatelyThenReschedule"/> — a catch-up execution happens immediately for the
/// earliest missed occurrence, but the next fire time is computed from that missed occurrence's own
/// timestamp rather than from "now", preserving the original cron cadence's alignment grid. If
/// multiple occurrences were missed, each is caught up individually, one per subsequent tick of the
/// hosted loop (never in a tight inner loop with no delay) — this bounds the catch-up execution rate
/// to the configured <c>SchedulingOptions.TickInterval</c> and avoids a downtime-triggered execution
/// burst, at the cost of taking longer to fully catch up after a long outage.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// For a one-shot deferred job, <see cref="FireOnce"/> and <see cref="RunImmediatelyThenReschedule"/>
/// behave identically — there is only ever one occurrence, so "catch up once" and "catch up while
/// preserving cadence" collapse to the same action.
/// </para>
/// </remarks>
public enum MisfirePolicy
{
    /// <summary>Discard the missed occurrence; never execute it. See the type-level remarks.</summary>
    Skip,

    /// <summary>
    /// Execute exactly one catch-up run for the missed window, then resume the schedule from "now".
    /// See the type-level remarks.
    /// </summary>
    FireOnce,

    /// <summary>
    /// Execute a catch-up run for the earliest missed occurrence, preserving the original cadence
    /// anchor so every missed occurrence is eventually replayed, one per tick. See the type-level
    /// remarks.
    /// </summary>
    RunImmediatelyThenReschedule,
}
