using SharedKernel.Primitives.Logging;

namespace SharedKernel.Workflows.Temporal.Logging;

/// <summary>
/// The <c>EventId</c> values of this package, built from the domain block base
/// (<see cref="LoggingEventIdRanges.Workflows"/>). The package owns the single 100-wide sub-block 17000-17099.
/// </summary>
internal static class WorkflowLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    public const int Base = LoggingEventIdRanges.Workflows;

    /// <summary>The readiness probe's reachability or namespace check faulted.</summary>
    public const int ProbeCheckFailed = Base + 13;
}
