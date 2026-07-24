using SharedKernel.Primitives.Errors;

namespace SharedKernel.Workflows.Temporal.Errors;

/// <summary>
/// The static <see cref="Error"/> factory catalog for <c>17.Workflows</c>. Every error surfaced by
/// this domain's dispatch surface routes through one of these factories — never an ad hoc
/// <see cref="Error"/> constructed inline.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SharedKernel.Primitives.Errors.Error"/> exposes exactly six factories —
/// <c>Unexpected</c>, <c>Validation</c>, <c>NotFound</c>, <c>Conflict</c>, <c>Unauthorized</c>,
/// <c>BusinessRule</c> — plus the <c>None</c> sentinel. There is no <c>Error.Failure</c>.
/// <see cref="Error.None"/> is never returned by any member below, and
/// <see cref="Error.BusinessRule(string, string)"/> is used zero times in this catalog — nothing in
/// a capability package is a domain rule.
/// </para>
/// </remarks>
public static class WorkflowErrors
{
    private const string Prefix = "workflow";

    /// <summary>The addressed workflow execution could not be located.</summary>
    public static Error NotFound(string workflowId) => Error.NotFound(
        $"{Prefix}.not_found",
        $"No workflow execution was found for id '{workflowId}'.");

    /// <summary>
    /// A workflow with the composed id is already running. Temporal enforces at most one running
    /// execution per workflow id per namespace — this is a <see cref="ErrorType.Conflict"/>, not a
    /// validation failure, because the caller's request is well-formed; it conflicts with existing
    /// running state.
    /// </summary>
    public static Error AlreadyStarted(string workflowId) => Error.Conflict(
        $"{Prefix}.already_started",
        $"A workflow execution with id '{workflowId}' is already running.");

    /// <summary>The workflow execution completed in a cancelled state.</summary>
    public static Error Cancelled(string workflowId) => Error.Unexpected(
        $"{Prefix}.cancelled",
        $"Workflow execution '{workflowId}' was cancelled.");

    /// <summary>The workflow execution was terminated by an operator, without compensation.</summary>
    public static Error Terminated(string workflowId) => Error.Unexpected(
        $"{Prefix}.terminated",
        $"Workflow execution '{workflowId}' was terminated.");

    /// <summary>The workflow execution timed out (execution, run, or task timeout).</summary>
    public static Error TimedOut(string workflowId) => Error.Unexpected(
        $"{Prefix}.timed_out",
        $"Workflow execution '{workflowId}' timed out.");

    /// <summary>
    /// A query could not be dispatched or was rejected — e.g. the worker fleet holding the
    /// execution is unavailable. Never surfaced by hanging; always an explicit failure.
    /// </summary>
    public static Error QueryFailed(string reason) => Error.Unexpected(
        $"{Prefix}.query_failed",
        $"The workflow query failed: {reason}");

    /// <summary>A signal could not be delivered to the addressed workflow execution.</summary>
    public static Error SignalFailed(string reason) => Error.Unexpected(
        $"{Prefix}.signal_failed",
        $"The workflow signal failed: {reason}");

    /// <summary>The Temporal service could not be reached (connectivity/RPC-level failure).</summary>
    public static Error ServiceUnavailable(string reason) => Error.Unexpected(
        $"{Prefix}.service_unavailable",
        $"The Temporal service is unavailable: {reason}");

    /// <summary>The configured Temporal namespace does not exist on the target cluster.</summary>
    public static Error NamespaceNotFound(string @namespace) => Error.Unexpected(
        $"{Prefix}.namespace_not_found",
        $"Temporal namespace '{@namespace}' could not be addressed.");

    /// <summary>
    /// A dispatch call was made with <see cref="Dispatch.TenantScope.None"/> against a
    /// tenant-scoped operation. A missing tenant scope on a cross-tenant-addressable keyspace is an
    /// authorization failure, not a validation failure — presenting it as a 422 would frame a
    /// would-be cross-tenant signal as a merely-unprocessable business request.
    /// </summary>
    public static Error TenantScopeMissing() => Error.Unauthorized(
        $"{Prefix}.tenant_scope_missing",
        "A tenant scope is required for this operation but none was supplied.");

    /// <summary>The supplied business key or composed workflow id was invalid (null/empty/whitespace).</summary>
    public static Error InvalidWorkflowId(string reason) => Error.Validation(
        $"{Prefix}.invalid_workflow_id",
        $"The workflow id is invalid: {reason}");

    /// <summary>No worker is configured to service the requested task queue.</summary>
    public static Error WorkerNotConfigured(string taskQueue) => Error.Unexpected(
        $"{Prefix}.worker_not_configured",
        $"No worker is configured for task queue '{taskQueue}'.");

    /// <summary>
    /// A non-deterministic operation was detected inside workflow code, or a replay failed because
    /// history no longer matches the deployed workflow code.
    /// </summary>
    public static Error DeterminismViolation(string reason) => Error.Unexpected(
        $"{Prefix}.determinism_violation",
        $"A determinism violation was detected: {reason}");

    /// <summary>The payload codec could not encode or decode a workflow/activity payload.</summary>
    public static Error PayloadCodecFailure(string reason) => Error.Unexpected(
        $"{Prefix}.payload_codec_failure",
        $"The payload codec failed: {reason}");

    /// <summary>
    /// A type passed to <c>ITemporalWorkflowsBuilder.AddWorkflow&lt;T&gt;()</c> is not
    /// <c>[Workflow]</c>-attributed, or another worker registration is otherwise invalid.
    /// </summary>
    public static Error InvalidWorkflowRegistration(string reason) => Error.Validation(
        $"{Prefix}.invalid_workflow_registration",
        $"The workflow registration is invalid: {reason}");
}
