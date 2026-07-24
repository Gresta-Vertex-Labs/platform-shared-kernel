using SharedKernel.Primitives.Results;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// Starts, attaches to, and describes durable Temporal workflow executions from ordinary
/// application code. Never inject a raw <c>Temporalio.*</c> type — this is the only surface
/// application code should depend on.
/// </summary>
/// <remarks>
/// <para>
/// Registered as <b>scoped</b> — it carries the ambient correlation/tenant context of the current
/// request. <see cref="TenantScope"/> is a mandatory, non-nullable, non-defaulted separate
/// parameter on every member: a workflow execution is addressed by a caller-supplied workflow id in
/// a flat per-namespace keyspace, so without a structural tenant discriminator, tenant B signalling
/// tenant A's workflow is one guessed string away.
/// </para>
/// <para>
/// <c>StartAsync</c> returns once the execution is durably recorded, not once it completes —
/// awaiting the result is a separate, explicit call on the returned <see cref="IWorkflowHandle{TResult}"/>.
/// There is deliberately no <c>StartAndWaitAsync</c> convenience, and deliberately no
/// list/search member backed by Temporal's Visibility API.
/// </para>
/// </remarks>
public interface IWorkflowDispatcher
{
    /// <summary>Starts a new no-argument workflow execution.</summary>
    /// <typeparam name="TWorkflow">The workflow type to start.</typeparam>
    /// <param name="options">Start options, including the business key and id policies.</param>
    /// <param name="tenantScope">The caller's tenant scope. Must not be <see cref="TenantScope.None"/>.</param>
    /// <param name="cancellationToken">A token to cancel the dispatch call.</param>
    Task<Result<IWorkflowHandle>> StartAsync<TWorkflow>(
        WorkflowStartOptions options,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
        where TWorkflow : Authoring.WorkflowBase;

    /// <summary>Starts a new workflow execution taking a single argument.</summary>
    /// <typeparam name="TWorkflow">The workflow type to start.</typeparam>
    /// <typeparam name="TArgs">The workflow's argument type.</typeparam>
    /// <param name="args">The workflow argument.</param>
    /// <param name="options">Start options, including the business key and id policies.</param>
    /// <param name="tenantScope">The caller's tenant scope. Must not be <see cref="TenantScope.None"/>.</param>
    /// <param name="cancellationToken">A token to cancel the dispatch call.</param>
    Task<Result<IWorkflowHandle>> StartAsync<TWorkflow, TArgs>(
        TArgs args,
        WorkflowStartOptions options,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
        where TWorkflow : Authoring.WorkflowBase;

    /// <summary>Starts a new workflow execution taking a single argument and producing a typed result.</summary>
    /// <typeparam name="TWorkflow">The workflow type to start.</typeparam>
    /// <typeparam name="TArgs">The workflow's argument type.</typeparam>
    /// <typeparam name="TResult">The workflow's result type.</typeparam>
    /// <param name="args">The workflow argument.</param>
    /// <param name="options">Start options, including the business key and id policies.</param>
    /// <param name="tenantScope">The caller's tenant scope. Must not be <see cref="TenantScope.None"/>.</param>
    /// <param name="cancellationToken">A token to cancel the dispatch call.</param>
    Task<Result<IWorkflowHandle<TResult>>> StartAsync<TWorkflow, TArgs, TResult>(
        TArgs args,
        WorkflowStartOptions options,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
        where TWorkflow : Authoring.WorkflowBase;

    /// <summary>
    /// Attaches to an already-running (or previously-started) workflow execution. Performs no I/O.
    /// </summary>
    /// <param name="workflowId">The full, already-composed workflow id.</param>
    /// <param name="runId">An optional specific run id. <see langword="null"/> targets the latest run.</param>
    /// <param name="tenantScope">The caller's tenant scope. Must not be <see cref="TenantScope.None"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="workflowId"/> is null/whitespace, or <paramref name="tenantScope"/> is <see cref="TenantScope.None"/>.</exception>
    IWorkflowHandle GetHandle(string workflowId, string? runId, TenantScope tenantScope);

    /// <summary>
    /// Attaches to an already-running (or previously-started) workflow execution whose result type
    /// is known. Performs no I/O.
    /// </summary>
    /// <typeparam name="TResult">The workflow's result type.</typeparam>
    /// <param name="workflowId">The full, already-composed workflow id.</param>
    /// <param name="runId">An optional specific run id. <see langword="null"/> targets the latest run.</param>
    /// <param name="tenantScope">The caller's tenant scope. Must not be <see cref="TenantScope.None"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="workflowId"/> is null/whitespace, or <paramref name="tenantScope"/> is <see cref="TenantScope.None"/>.</exception>
    IWorkflowHandle<TResult> GetHandle<TResult>(string workflowId, string? runId, TenantScope tenantScope);

    /// <summary>Describes a single workflow execution by id. The only read offered against arbitrary executions.</summary>
    /// <param name="workflowId">The full, already-composed workflow id.</param>
    /// <param name="tenantScope">The caller's tenant scope. Must not be <see cref="TenantScope.None"/>.</param>
    /// <param name="cancellationToken">A token to cancel the dispatch call.</param>
    Task<Result<WorkflowExecutionDescription>> DescribeAsync(
        string workflowId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
