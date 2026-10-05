namespace SharedKernel.Workflows.Temporal.Hosting;

/// <summary>
/// Worker resource-tuning knobs passed to <see cref="ITemporalWorkflowsBuilder.WithWorker"/>. Every
/// member carries a platform default so a consuming service can tune only what it needs to.
/// </summary>
public sealed record WorkerTuningOptions
{
    /// <summary>Gets the default tuning — matches the Temporal SDK's own defaults.</summary>
    public static WorkerTuningOptions Default { get; } = new();

    /// <summary>Gets the maximum number of concurrent workflow tasks the worker will process.</summary>
    public int? MaxConcurrentWorkflowTasks { get; init; }

    /// <summary>Gets the maximum number of concurrent activity tasks the worker will process.</summary>
    public int? MaxConcurrentActivities { get; init; }

    /// <summary>Gets the maximum number of concurrent local activities the worker will process.</summary>
    public int? MaxConcurrentLocalActivities { get; init; }

    /// <summary>Gets the maximum number of workflow executions cached in the worker's sticky cache.</summary>
    public int MaxCachedWorkflows { get; init; } = 1000;

    /// <summary>
    /// Gets the maximum time the worker waits for in-flight activity/workflow tasks to complete
    /// during a graceful shutdown.
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
