using SharedKernel.Primitives.Logging;

namespace SharedKernel.Workflows.Temporal.Diagnostics;

/// <summary>
/// The <c>EventId</c> values of this package, built from the domain block base
/// (<see cref="LoggingEventIdRanges.Workflows"/>). The package owns the single 100-wide sub-block 17000-17099; ids
/// 17016 and above are unallocated.
/// </summary>
internal static class WorkflowLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    public const int Base = LoggingEventIdRanges.Workflows;

    /// <summary>A workflow was started.</summary>
    public const int WorkflowStarted = Base;

    /// <summary>A dispatch was refused because no tenant scope was supplied.</summary>
    public const int TenantScopeMissingOnDispatch = Base + 1;

    /// <summary>A signal was dispatched to a workflow.</summary>
    public const int SignalDispatched = Base + 2;

    /// <summary>A query was dispatched to a workflow.</summary>
    public const int QueryDispatched = Base + 3;

    /// <summary>A workflow was terminated.</summary>
    public const int WorkflowTerminated = Base + 4;

    /// <summary>A workflow ran without a tenant header.</summary>
    public const int TenantHeaderMissingOnWorkflow = Base + 5;

    /// <summary>The payload codec failed to encode or decode a payload.</summary>
    public const int PayloadCodecFailed = Base + 6;

    /// <summary>A worker composition was built.</summary>
    public const int WorkerBuilt = Base + 7;

    /// <summary>A client-only composition was built.</summary>
    public const int ClientOnlyBuilt = Base + 8;

    /// <summary>The readiness probe reported a degraded service.</summary>
    public const int ProbeDegraded = Base + 9;

    /// <summary>Payload encryption was configured.</summary>
    public const int PayloadEncryptionConfigured = Base + 10;

    /// <summary>An activity heartbeat was recorded.</summary>
    public const int ActivityHeartbeatRecorded = Base + 11;

    /// <summary>Raw client access was enabled.</summary>
    public const int RawClientAccessEnabled = Base + 12;

    /// <summary>The readiness probe's reachability or namespace check faulted.</summary>
    public const int ProbeCheckFailed = Base + 13;

    /// <summary>A dispatch returned a failed result.</summary>
    public const int DispatchFailed = Base + 14;

    /// <summary>A dispatch hit a Temporal failure the mapper has no row for.</summary>
    public const int DispatchFaulted = Base + 15;
}
