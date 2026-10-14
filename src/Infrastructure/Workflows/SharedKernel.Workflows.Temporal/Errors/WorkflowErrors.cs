using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Workflows.Temporal.Errors;

/// <summary>
/// The internal <see cref="Error"/> factory for <c>17.Workflows</c>. Every error surfaced by this domain's dispatch
/// surface routes through one of these factories, never an ad hoc <see cref="Error"/> constructed inline; callers
/// compare <see cref="WorkflowErrorCodes"/>.
/// </summary>
/// <remarks>
/// Every message is a fixed sentence: it names no workflow id (a workflow id is <c>{tenant}:{type}:{businessKey}</c>),
/// tenant, host or exception text.
/// </remarks>
internal static class WorkflowErrors
{
    /// <summary>The addressed workflow execution could not be located, or belongs to another tenant.</summary>
    public static Error NotFound() => Error.NotFound(
        WorkflowErrorCodes.NotFound,
        "No workflow execution was found for the requested id.");

    /// <summary>
    /// A workflow with the composed id is already running. Temporal enforces at most one running execution per
    /// workflow id per namespace; this is a conflict with existing running state, not a validation failure.
    /// </summary>
    public static Error AlreadyStarted() => Error.Conflict(
        WorkflowErrorCodes.AlreadyStarted,
        "A workflow execution with the composed id is already running.");

    /// <summary>The workflow execution completed in a cancelled state.</summary>
    public static Error Cancelled() => Error.Unexpected(
        WorkflowErrorCodes.Cancelled,
        "The workflow execution was cancelled.");

    /// <summary>The workflow execution was terminated by an operator, without compensation.</summary>
    public static Error Terminated() => Error.Unexpected(
        WorkflowErrorCodes.Terminated,
        "The workflow execution was terminated.");

    /// <summary>The workflow execution timed out (execution, run, or task timeout).</summary>
    public static Error TimedOut() => Error.Timeout(
        WorkflowErrorCodes.TimedOut,
        "The workflow execution timed out.");

    /// <summary>
    /// A dispatch request to the Temporal service ran out of time before the service answered. Distinct from
    /// <see cref="TimedOut"/>, which is a workflow execution that ran out of time.
    /// </summary>
    public static Error RequestTimedOut() => Error.Timeout(
        WorkflowErrorCodes.RequestTimedOut,
        "The request to the Temporal service timed out.");

    /// <summary>
    /// The Temporal service answered with a status that is a deployment or contract fault (credential, permission,
    /// internal error, unsupported call), not a condition the caller can fix.
    /// </summary>
    public static Error RpcFailed() => Error.Unexpected(
        WorkflowErrorCodes.RpcFailed,
        "The request to the Temporal service failed.");

    /// <summary>
    /// A query could not be dispatched or was rejected, for example because the worker fleet holding the execution is
    /// unavailable. Never surfaced by hanging; always an explicit failure.
    /// </summary>
    /// <param name="reason">A short, fixed-vocabulary reason that names no id or exception text.</param>
    public static Error QueryFailed(string reason) => Error.Unexpected(
        WorkflowErrorCodes.QueryFailed,
        $"The workflow query failed: {reason}");

    /// <summary>The Temporal service could not be reached or is overloaded.</summary>
    public static Error ServiceUnavailable() => Error.Unavailable(
        WorkflowErrorCodes.ServiceUnavailable,
        "The Temporal service is unavailable.");

    /// <summary>The configured Temporal namespace does not exist on the target cluster.</summary>
    /// <param name="namespace">The namespace the server reported missing; configuration, not user data.</param>
    public static Error NamespaceNotFound(string @namespace) => Error.Unexpected(
        WorkflowErrorCodes.NamespaceNotFound,
        $"Temporal namespace '{@namespace}' could not be addressed.");

    /// <summary>
    /// A dispatch call was made with <see cref="TenantScope.Global"/> against a tenant-scoped operation. A missing
    /// tenant scope on a cross-tenant-addressable keyspace is an authorization failure, not a validation failure.
    /// </summary>
    public static Error TenantScopeMissing() => Error.Unauthorized(
        WorkflowErrorCodes.TenantScopeMissing,
        "A tenant scope is required for this operation but none was supplied.");

    /// <summary>The supplied business key or composed workflow id was blank.</summary>
    /// <param name="reason">A fixed sentence fragment naming which input was blank.</param>
    public static Error InvalidWorkflowId(string reason) => Error.Validation(
        WorkflowErrorCodes.InvalidWorkflowId,
        $"The workflow id is invalid: {reason}");

    /// <summary>
    /// The payload codec could not encode or decode a payload. The message carries no key id, ciphertext or
    /// cryptographic detail.
    /// </summary>
    public static Error PayloadCodecFailure() => Error.Unexpected(
        WorkflowErrorCodes.PayloadCodecFailure,
        "The payload codec could not process a workflow payload.");

    /// <summary>A Temporal failure the mapper has no row for. The message is fixed and carries no exception text.</summary>
    public static Error UnmappedFailure() => Error.Unexpected(
        WorkflowErrorCodes.UnmappedFailure,
        "The workflow operation failed with an unmapped error.");
}
