namespace SharedKernel.Workflows.Temporal.Health;

/// <summary>The readiness probe <c>AddSharedKernelTemporalWorkflows</c> registers.</summary>
/// <remarks>
/// Resolve it with <c>GetRequiredReadinessProbe(WorkflowReadiness.ProbeName)</c>. It is healthy when the Temporal
/// service is reachable, this service's namespace is addressable, and — on a worker-hosting composition — every
/// worker poller is still running. The three checks are reported separately under the data keys below.
/// </remarks>
public static class WorkflowReadiness
{
    /// <summary>The probe's name.</summary>
    public const string ProbeName = "workflows";

    /// <summary>Data key: whether the Temporal service answered its health check (<see cref="bool"/>).</summary>
    public const string ReachableKey = "Reachable";

    /// <summary>Data key: whether the configured namespace could be described (<see cref="bool"/>).</summary>
    public const string NamespaceAddressableKey = "NamespaceAddressable";

    /// <summary>
    /// Data key: whether every worker hosted by this process is still polling (<see cref="bool"/>); <see langword="true"/>
    /// on a client-only composition.
    /// </summary>
    public const string WorkerPollersActiveKey = "WorkerPollersActive";
}
