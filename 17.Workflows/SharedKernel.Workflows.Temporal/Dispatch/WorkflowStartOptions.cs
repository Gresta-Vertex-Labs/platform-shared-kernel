using Temporalio.Api.Enums.V1;
using Temporalio.Common;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// Options controlling how a new workflow execution is started via
/// <see cref="IWorkflowDispatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// The physical workflow id is never supplied here — it is composed by
/// <see cref="IWorkflowIdFactory"/> from the workflow type, <see cref="BusinessKey"/>, and the
/// caller's <see cref="TenantScope"/>, so the tenant segment is structural rather than conventional.
/// </para>
/// <para>
/// <see cref="IdReusePolicy"/> and <see cref="IdConflictPolicy"/> are <c>required</c> — they are
/// deliberately never defaulted to "whatever the SDK does", because the difference between
/// "reject the duplicate" and "terminate the running one and start over" is the difference between
/// a safe retry and destroying in-flight work.
/// </para>
/// </remarks>
public sealed record WorkflowStartOptions
{
    /// <summary>Gets the Temporal task queue the workflow execution is dispatched to.</summary>
    public required string TaskQueue { get; init; }

    /// <summary>
    /// Gets the caller-supplied business key (e.g. an order id or payment reference) that
    /// <see cref="IWorkflowIdFactory"/> composes into the tenant-scoped workflow id. Starting the
    /// same business key twice under the same tenant and workflow type is the platform's durable
    /// idempotency primitive.
    /// </summary>
    public required string BusinessKey { get; init; }

    /// <summary>
    /// Gets the policy governing whether a new run may reuse a workflow id whose most recent
    /// execution has already completed.
    /// </summary>
    public required WorkflowIdReusePolicy IdReusePolicy { get; init; }

    /// <summary>
    /// Gets the policy governing what happens when a workflow id collides with an
    /// <em>already-running</em> execution.
    /// </summary>
    public required WorkflowIdConflictPolicy IdConflictPolicy { get; init; }

    /// <summary>Gets the maximum time the entire workflow execution (including retries/continue-as-new) may run.</summary>
    public TimeSpan? ExecutionTimeout { get; init; }

    /// <summary>Gets the maximum time a single workflow run may take.</summary>
    public TimeSpan? RunTimeout { get; init; }

    /// <summary>Gets the maximum time a single workflow task may take.</summary>
    public TimeSpan? TaskTimeout { get; init; }

    /// <summary>Gets the retry policy applied to the workflow execution itself, if any.</summary>
    public RetryPolicy? RetryPolicy { get; init; }
}
