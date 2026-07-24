namespace SharedKernel.Workflows.Temporal.Health;

/// <summary>
/// A point-in-time readiness snapshot returned by <see cref="IWorkflowServiceProbe.ProbeAsync"/>.
/// </summary>
/// <remarks>
/// This is a probe primitive, not a health check — <c>17.Workflows</c> ships no
/// <c>IHealthCheck</c> implementation. Wiring this into <c>AddHealthChecks()</c> is
/// <c>13.ServiceDefaults</c>'s responsibility.
/// </remarks>
public sealed record WorkflowServiceHealth
{
    /// <summary>Gets a value indicating whether the Temporal service could be reached at all.</summary>
    public required bool Reachable { get; init; }

    /// <summary>Gets a value indicating whether the configured namespace could be addressed.</summary>
    public required bool NamespaceAddressable { get; init; }

    /// <summary>
    /// Gets a value indicating whether this process's worker pollers are actively polling.
    /// </summary>
    /// <remarks>
    /// This is the member a naive probe omits, and the one that matters most: a worker whose
    /// pollers have died is up, connected, and useless — it accepts traffic, reports healthy, and
    /// silently processes nothing. Reachability alone cannot distinguish it from a working fleet.
    /// On a client-only (non-worker-hosting) service this is always <see langword="true"/> — there
    /// are no pollers to fail.
    /// </remarks>
    public required bool WorkerPollersActive { get; init; }

    /// <summary>
    /// Gets the approximate depth of the worker's task queue backlog, if known.
    /// </summary>
    /// <remarks>
    /// This is a gauge, never a readiness failure — a deep backlog means work is slow, not that the
    /// service is unavailable. Permanently nullable: not every deployment shape can report it.
    /// </remarks>
    public long? TaskQueueBacklog { get; init; }

    /// <summary>Gets how long the probe took to complete.</summary>
    public required TimeSpan Latency { get; init; }
}
