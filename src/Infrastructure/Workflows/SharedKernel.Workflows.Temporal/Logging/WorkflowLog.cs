using Microsoft.Extensions.Logging;

namespace SharedKernel.Workflows.Temporal.Logging;

/// <summary>
/// Source-generated <see cref="ILogger"/> extension methods for <c>17.Workflows</c>. Every
/// <c>EventId</c> falls inside the reserved sub-block <b>17000–17099</b>
/// (<see cref="SharedKernel.Primitives.Logging.LoggingEventIdRanges.Workflows"/>).
/// </summary>
/// <remarks>
/// These are ordinary <c>ILogger</c> extension methods, so the same generated methods are callable
/// both from activity code (an injected <c>ILogger&lt;T&gt;</c>, which implements <see cref="ILogger"/>)
/// and from workflow code (<c>Workflow.Logger</c>, which also implements <see cref="ILogger"/> and is
/// replay-aware) — see <c>src/Infrastructure/Workflows/CLAUDE.md</c>'s <c>[LoggerMessage]</c>-on-<c>Workflow.Logger</c>
/// reconciliation.
/// </remarks>
internal static partial class WorkflowLog
{
    [LoggerMessage(
        EventId = 17000,
        Level = LogLevel.Information,
        Message = "Started workflow {WorkflowId} of type {WorkflowType}")]
    public static partial void WorkflowStarted(ILogger logger, string workflowId, string workflowType);

    [LoggerMessage(
        EventId = 17001,
        Level = LogLevel.Warning,
        Message = "Dispatch rejected — no tenant scope supplied for {WorkflowType}")]
    public static partial void TenantScopeMissingOnDispatch(ILogger logger, string workflowType);

    [LoggerMessage(
        EventId = 17002,
        Level = LogLevel.Debug,
        Message = "Signal {SignalName} dispatched to workflow {WorkflowId}")]
    public static partial void SignalDispatched(ILogger logger, string workflowId, string signalName);

    [LoggerMessage(
        EventId = 17003,
        Level = LogLevel.Debug,
        Message = "Query {QueryName} dispatched to workflow {WorkflowId}")]
    public static partial void QueryDispatched(ILogger logger, string workflowId, string queryName);

    [LoggerMessage(
        EventId = 17004,
        Level = LogLevel.Warning,
        Message = "Workflow {WorkflowId} terminated — reason: {Reason}. No compensation runs.")]
    public static partial void WorkflowTerminated(ILogger logger, string workflowId, string reason);

    [LoggerMessage(
        EventId = 17005,
        Level = LogLevel.Warning,
        Message = "No tenant header was present on workflow {WorkflowId} — surfacing TenantScope.Global")]
    public static partial void TenantHeaderMissingOnWorkflow(ILogger logger, string workflowId);

    [LoggerMessage(
        EventId = 17006,
        Level = LogLevel.Error,
        Message = "Payload codec failed to {Operation} a payload: {Reason}")]
    public static partial void PayloadCodecFailed(ILogger logger, string operation, string reason);

    [LoggerMessage(
        EventId = 17007,
        Level = LogLevel.Information,
        Message = "Temporal worker built for task queue {TaskQueue} with {WorkflowCount} workflow(s) and {ActivityCount} activity type(s)")]
    public static partial void WorkerBuilt(ILogger logger, string taskQueue, int workflowCount, int activityCount);

    [LoggerMessage(
        EventId = 17008,
        Level = LogLevel.Information,
        Message = "Temporal client-only composition built — no worker hosted")]
    public static partial void ClientOnlyBuilt(ILogger logger);

    [LoggerMessage(
        EventId = 17009,
        Level = LogLevel.Warning,
        Message = "Workflow service probe degraded — Reachable={Reachable} NamespaceAddressable={NamespaceAddressable} WorkerPollersActive={WorkerPollersActive}")]
    public static partial void ProbeDegraded(ILogger logger, bool reachable, bool namespaceAddressable, bool workerPollersActive);

    [LoggerMessage(
        EventId = 17010,
        Level = LogLevel.Information,
        Message = "Payload encryption configured for this Temporal client composition")]
    public static partial void PayloadEncryptionConfigured(ILogger logger);

    [LoggerMessage(
        EventId = 17011,
        Level = LogLevel.Debug,
        Message = "Heartbeat recorded for activity {ActivityType}")]
    public static partial void ActivityHeartbeatRecorded(ILogger logger, string activityType);

    [LoggerMessage(
        EventId = 17012,
        Level = LogLevel.Warning,
        Message = "Raw Temporal client access enabled via AllowRawClientAccess() — TENANT SCOPING AND WORKFLOW-ID COMPOSITION ARE BYPASSED for any code that consumes it")]
    public static partial void RawClientAccessEnabled(ILogger logger);
}
