using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Workflows.Temporal.Diagnostics;

/// <summary>
/// Source-generated log messages for <c>17.Workflows</c>. Every <c>EventId</c> falls inside the package's sub-block
/// 17000-17099 (<see cref="WorkflowLoggingEventIds"/>).
/// </summary>
/// <remarks>
/// These are ordinary static methods taking an <see cref="ILogger"/>, so the same generated methods are callable from
/// activity code (an injected <c>ILogger&lt;T&gt;</c>) and from workflow code (<c>Workflow.Logger</c>, which is
/// replay-aware). No template carries a workflow id, tenant id, business key or terminate reason.
/// </remarks>
internal static partial class WorkflowLog
{
    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.WorkflowStarted,
        Level = LogLevel.Debug,
        Message = "Started a workflow of type '{WorkflowType}'.")]
    public static partial void WorkflowStarted(ILogger logger, string workflowType);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.TenantScopeMissingOnDispatch,
        Level = LogLevel.Warning,
        Message = "The '{Operation}' workflow dispatch was refused because no tenant scope was supplied.")]
    public static partial void TenantScopeMissingOnDispatch(ILogger logger, string operation);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.SignalDispatched,
        Level = LogLevel.Debug,
        Message = "Dispatched signal '{SignalName}' to a workflow.")]
    public static partial void SignalDispatched(ILogger logger, string signalName);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.QueryDispatched,
        Level = LogLevel.Debug,
        Message = "Dispatched query '{QueryName}' to a workflow.")]
    public static partial void QueryDispatched(ILogger logger, string queryName);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.WorkflowTerminated,
        Level = LogLevel.Information,
        Message = "A workflow was terminated and no compensation runs.")]
    public static partial void WorkflowTerminated(ILogger logger);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.TenantHeaderMissingOnWorkflow,
        Level = LogLevel.Warning,
        Message = "No tenant header was present on a workflow of type '{WorkflowType}', so its tenant scope is global.")]
    public static partial void TenantHeaderMissingOnWorkflow(ILogger logger, string workflowType);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.PayloadCodecFailed,
        Level = LogLevel.Error,
        Message = "The payload codec failed to {Operation} a payload.")]
    public static partial void PayloadCodecFailed(ILogger logger, string operation, Exception exception);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.WorkerBuilt,
        Level = LogLevel.Information,
        Message = "Built a Temporal worker for task queue '{TaskQueue}' with {WorkflowCount} workflows and {ActivityCount} activity types.")]
    public static partial void WorkerBuilt(ILogger logger, string taskQueue, int workflowCount, int activityCount);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.ClientOnlyBuilt,
        Level = LogLevel.Information,
        Message = "Built a client-only Temporal composition with no worker hosted.")]
    public static partial void ClientOnlyBuilt(ILogger logger);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.ProbeDegraded,
        Level = LogLevel.Warning,
        Message = "The workflow service probe is degraded with reachable {Reachable}, namespace addressable {NamespaceAddressable} and worker pollers active {WorkerPollersActive}.")]
    public static partial void ProbeDegraded(ILogger logger, bool reachable, bool namespaceAddressable, bool workerPollersActive);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.PayloadEncryptionConfigured,
        Level = LogLevel.Information,
        Message = "Payload encryption is configured for this Temporal client composition.")]
    public static partial void PayloadEncryptionConfigured(ILogger logger);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.ActivityHeartbeatRecorded,
        Level = LogLevel.Debug,
        Message = "Recorded a heartbeat for activity type '{ActivityType}'.")]
    public static partial void ActivityHeartbeatRecorded(ILogger logger, string activityType);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.RawClientAccessEnabled,
        Level = LogLevel.Warning,
        Message = "Raw Temporal client access is enabled and bypasses tenant scoping and workflow id composition.")]
    public static partial void RawClientAccessEnabled(ILogger logger);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.ProbeCheckFailed,
        Level = LogLevel.Warning,
        Message = "The workflow service probe {Check} check faulted.")]
    public static partial void ProbeCheckFailed(ILogger logger, string check, Exception exception);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.DispatchFailed,
        Level = LogLevel.Warning,
        Message = "The '{Operation}' workflow dispatch failed with error code '{ErrorCode}' of type {ErrorType}.")]
    public static partial void DispatchFailed(ILogger logger, string operation, string errorCode, ErrorType errorType);

    [LoggerMessage(
        EventId = WorkflowLoggingEventIds.DispatchFaulted,
        Level = LogLevel.Error,
        Message = "The '{Operation}' workflow dispatch hit a Temporal failure with no mapping.")]
    public static partial void DispatchFaulted(ILogger logger, string operation, Exception exception);
}
