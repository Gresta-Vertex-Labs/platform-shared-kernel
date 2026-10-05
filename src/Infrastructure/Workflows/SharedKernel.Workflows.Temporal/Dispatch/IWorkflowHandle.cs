using SharedKernel.Primitives.Results;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// A durable reference to a single Temporal workflow execution, returned by
/// <see cref="IWorkflowDispatcher"/>. Every member returns <see cref="Result"/>/<see cref="Result{T}"/>
/// — expected failures are values, never exceptions.
/// </summary>
public interface IWorkflowHandle
{
    /// <summary>Gets the workflow id.</summary>
    string WorkflowId { get; }

    /// <summary>Gets the run id of the execution this handle was obtained against, if known.</summary>
    string? RunId { get; }

    /// <summary>Delivers a signal to the workflow execution.</summary>
    /// <typeparam name="TSignalArgs">The signal argument type.</typeparam>
    /// <param name="signalName">The registered signal name.</param>
    /// <param name="args">The signal argument.</param>
    /// <param name="cancellationToken">A token to cancel the dispatch call.</param>
    Task<Result> SignalAsync<TSignalArgs>(string signalName, TSignalArgs args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the workflow execution's live state.
    /// </summary>
    /// <remarks>
    /// A query is dispatched to a worker holding (or replaying) the execution — it is not free. A
    /// query against an execution whose worker fleet is down returns
    /// <see cref="Errors.WorkflowErrors.QueryFailed"/> rather than hanging.
    /// </remarks>
    /// <typeparam name="TQueryResult">The query result type.</typeparam>
    /// <param name="queryName">The registered query name.</param>
    /// <param name="cancellationToken">A token to cancel the dispatch call.</param>
    Task<Result<TQueryResult>> QueryAsync<TQueryResult>(string queryName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cooperative cancellation. The workflow observes its cancellation token, runs its
    /// compensation path, and completes as cancelled.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the dispatch call itself.</param>
    Task<Result> CancelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the execution server-side with no compensation, no cleanup, and no chance for the
    /// workflow to react. An operator action of last resort.
    /// </summary>
    /// <remarks>
    /// <b>THIS DESTROYS IN-FLIGHT WORK WITH NO COMPENSATION.</b> <paramref name="reason"/> is
    /// required and recorded in the execution's history so it is clear who terminated the execution
    /// and why. There is deliberately no overload defaulting the reason.
    /// </remarks>
    /// <param name="reason">A non-empty reason, recorded in the execution's history.</param>
    /// <param name="cancellationToken">A token to cancel the dispatch call itself.</param>
    Task<Result> TerminateAsync(string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// The result-bearing sibling of <see cref="IWorkflowHandle"/>, returned when the caller knows the
/// workflow's result type.
/// </summary>
/// <typeparam name="TResult">The workflow's result type.</typeparam>
public interface IWorkflowHandle<TResult> : IWorkflowHandle
{
    /// <summary>
    /// Awaits the workflow execution's final result.
    /// </summary>
    /// <remarks>
    /// This call may block for as long as the workflow takes to complete — there is deliberately no
    /// bound. Call it only when the caller genuinely intends to wait.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the dispatch call itself, not the workflow.</param>
    Task<Result<TResult>> GetResultAsync(CancellationToken cancellationToken = default);
}
