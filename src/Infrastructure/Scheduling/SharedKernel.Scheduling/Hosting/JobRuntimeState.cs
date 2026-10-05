namespace SharedKernel.Scheduling.Hosting;

/// <summary>
/// The mutable, in-process runtime state <c>SchedulingHostedService</c> tracks for one registered job
/// across ticks. Never persisted — see Domain Invariant 2 (no job store).
/// </summary>
internal sealed class JobRuntimeState
{
    /// <summary>
    /// Guards <see cref="RunningCount"/> and <see cref="PendingQueue"/> together for the
    /// <c>OverlapPolicy.Skip</c>/<c>.Queue</c> check-and-set — <c>OverlapPolicy.Allow</c> never reads
    /// or takes this lock, since it imposes no single-concurrency constraint to protect.
    /// </summary>
    public readonly object Gate = new();

    /// <summary>FIFO of fire attempts queued under <c>OverlapPolicy.Queue</c> while a run was already in flight.</summary>
    public readonly Queue<PendingFire> PendingQueue = new();

    /// <summary>The next UTC time this job is due to fire, or <see langword="null"/> once a terminal (one-shot, already-fired) job has nothing left to schedule.</summary>
    public DateTimeOffset? NextFireTimeUtc { get; set; }

    /// <summary>
    /// <see langword="true"/> once a one-shot deferred job has fired (or had its single occurrence
    /// discarded by <c>MisfirePolicy.Skip</c>) and will never fire again. Always <see langword="false"/>
    /// for a recurring job.
    /// </summary>
    public bool Terminal { get; set; }

    /// <summary>
    /// The number of executions of this job currently in flight in this process. Under
    /// <c>OverlapPolicy.Skip</c>/<c>.Queue</c> this is 0 or 1, guarded by <see cref="Gate"/>. Under
    /// <c>OverlapPolicy.Allow</c> this can exceed 1 and is updated via <see cref="System.Threading.Interlocked"/>
    /// without taking <see cref="Gate"/>.
    /// </summary>
    public int RunningCount;
}

/// <summary>One fire attempt waiting in a job's <see cref="JobRuntimeState.PendingQueue"/> under <c>OverlapPolicy.Queue</c>.</summary>
internal readonly record struct PendingFire(DateTimeOffset ScheduledFireTimeUtc, DateTimeOffset ActualFireTimeUtc);
